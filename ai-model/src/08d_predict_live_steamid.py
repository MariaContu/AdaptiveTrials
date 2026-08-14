"""Live Steam macro inference for Adaptive Trials.

Flow:
SteamID -> GetOwnedGames -> final category mapping -> 40 deployment features
-> optimized Random Forest -> predict + predict_proba.

Important:
- No source/recruitment game is excluded in live inference, because production
  users were not recruited through a source game.
- The final mapping is frozen from the TCC dataset stage. Unmapped AppIDs are
  ignored when category features are built.
- A minimum categorized-playtime coverage gate is applied before prediction.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import os
import sys
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
import requests
from dotenv import load_dotenv
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_MAPPING = ROOT / "data/interim/final_game_category_scores_v2.csv"
DEFAULT_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_METADATA = ROOT / "models/final/macro_model_optimized_metadata.json"

GET_OWNED_GAMES_ENDPOINT = (
    "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/"
)

MACRO_CATEGORIES = ("combat", "exploration", "strategic_reasoning")
SUBGROUPS = {
    "combat": ("shooter", "melee", "action_other"),
    "exploration": (
        "open_world",
        "narrative_exploration",
        "investigation",
    ),
    "strategic_reasoning": (
        "logic_puzzle",
        "planning_management",
        "strategy_decision",
        "observation_deduction",
    ),
}

LOGGER = logging.getLogger("live-steam-macro-inference")


def norm(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def safe_float(value: Any) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return 0.0
    return result if math.isfinite(result) else 0.0


def safe_divide(numerator: float, denominator: float) -> float:
    if denominator <= 0:
        return 0.0
    return float(numerator / denominator)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run live macro preference inference for one SteamID."
    )
    parser.add_argument("--steamid", required=True)
    parser.add_argument("--mapping", type=Path, default=DEFAULT_MAPPING)
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--metadata", type=Path, default=DEFAULT_METADATA)
    parser.add_argument(
        "--minimum-playtime-coverage",
        type=float,
        default=0.70,
        help="Minimum categorized playtime coverage required for prediction.",
    )
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--output", type=Path, default=None)

    args = parser.parse_args()

    if not str(args.steamid).isdigit():
        parser.error("--steamid must contain only digits.")
    if not 0 <= args.minimum_playtime_coverage <= 1:
        parser.error("--minimum-playtime-coverage must be between 0 and 1.")
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero.")

    return args


def load_api_key() -> str:
    load_dotenv(ROOT / ".env")
    api_key = os.getenv("STEAM_API_KEY", "").strip()

    if not api_key:
        raise ValueError(
            "STEAM_API_KEY was not found. Configure ai-model/.env."
        )

    return api_key


def create_http_session() -> requests.Session:
    retry_strategy = Retry(
        total=5,
        connect=5,
        read=5,
        status=5,
        backoff_factor=1.0,
        status_forcelist=(429, 500, 502, 503, 504),
        allowed_methods=frozenset({"GET"}),
        raise_on_status=False,
    )

    adapter = HTTPAdapter(max_retries=retry_strategy)
    session = requests.Session()
    session.mount("https://", adapter)
    session.headers.update(
        {
            "User-Agent": (
                "AdaptiveTrials-AcademicResearch/1.0 "
                "(live player profile inference)"
            )
        }
    )
    return session


def fetch_owned_games(
    session: requests.Session,
    api_key: str,
    steamid: str,
    timeout: float,
) -> list[dict[str, Any]]:
    response = session.get(
        GET_OWNED_GAMES_ENDPOINT,
        params={
            "key": api_key,
            "steamid": steamid,
            "include_appinfo": "false",
            "include_played_free_games": "true",
            "format": "json",
        },
        timeout=timeout,
    )

    if response.status_code in {401, 403}:
        raise PermissionError(
            "Steam rejected the configured API key."
        )

    if response.status_code != requests.codes.ok:
        raise RuntimeError(
            f"Steam GetOwnedGames returned HTTP {response.status_code}."
        )

    payload = response.json()
    response_payload = payload.get("response")

    if not isinstance(response_payload, dict) or not response_payload:
        raise ValueError(
            "Steam library is private, unavailable, or the profile "
            "cannot be queried."
        )

    raw_games = response_payload.get("games")
    game_count = safe_int(response_payload.get("game_count"))

    if game_count == 0 and not raw_games:
        raise ValueError("Steam profile has an empty visible library.")

    if not isinstance(raw_games, list):
        raise ValueError("Steam response did not contain a valid games list.")

    return [
        game
        for game in raw_games
        if isinstance(game, dict) and safe_int(game.get("appid")) > 0
    ]


def read_mapping(path: Path) -> dict[int, dict[str, str]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)

        if reader.fieldnames is None:
            raise ValueError("Mapping CSV has no header.")

        required = {
            "appid",
            "assigned_category",
            "assigned_subgroup",
        }
        missing = required - set(reader.fieldnames)

        if missing:
            raise ValueError(
                f"Mapping CSV missing columns: {sorted(missing)}"
            )

        mapping: dict[int, dict[str, str]] = {}

        for row in reader:
            appid = safe_int(row.get("appid"))
            if appid > 0:
                mapping[appid] = dict(row)

        return mapping


def normalize_games(raw_games: list[dict[str, Any]]) -> list[dict[str, int]]:
    return [
        {
            "appid": safe_int(game.get("appid")),
            "playtime_forever_minutes": safe_int(
                game.get("playtime_forever")
            ),
        }
        for game in raw_games
        if safe_int(game.get("appid")) > 0
    ]


def build_features(
    games: list[dict[str, int]],
    mapping: dict[int, dict[str, str]],
) -> dict[str, float | int]:
    total_library_games = len(games)
    total_played_games = 0
    total_playtime_minutes = 0

    categorized_games = 0
    categorized_played_games = 0
    categorized_playtime_minutes = 0

    macro_games = {
        category: 0
        for category in MACRO_CATEGORIES
    }
    macro_played = {
        category: 0
        for category in MACRO_CATEGORIES
    }

    subgroup_games = {
        subgroup: 0
        for category in MACRO_CATEGORIES
        for subgroup in SUBGROUPS[category]
    }
    subgroup_played = dict.fromkeys(subgroup_games, 0)

    for game in games:
        appid = game["appid"]
        playtime = game["playtime_forever_minutes"]

        total_playtime_minutes += playtime
        if playtime > 0:
            total_played_games += 1

        mapped = mapping.get(appid)
        if mapped is None:
            continue

        category = norm(mapped.get("assigned_category"))
        subgroup = norm(mapped.get("assigned_subgroup"))

        if category not in MACRO_CATEGORIES:
            continue

        categorized_games += 1
        macro_games[category] += 1

        if playtime > 0:
            categorized_played_games += 1
            categorized_playtime_minutes += playtime
            macro_played[category] += 1

        if subgroup in SUBGROUPS[category]:
            subgroup_games[subgroup] += 1
            if playtime > 0:
                subgroup_played[subgroup] += 1

    features: dict[str, float | int] = {
        "total_library_games": total_library_games,
        "total_played_games": total_played_games,
        "total_playtime_minutes": total_playtime_minutes,
        "categorized_games": categorized_games,
        "categorized_played_games": categorized_played_games,
        "category_game_coverage": safe_divide(
            categorized_games,
            total_library_games,
        ),
        "category_playtime_coverage": safe_divide(
            categorized_playtime_minutes,
            total_playtime_minutes,
        ),
        "avg_playtime_per_played_game_minutes": safe_divide(
            total_playtime_minutes,
            total_played_games,
        ),
    }

    for category in MACRO_CATEGORIES:
        features[f"games_{category}"] = macro_games[category]
        features[f"played_games_{category}"] = macro_played[category]

    for category in MACRO_CATEGORIES:
        for subgroup in SUBGROUPS[category]:
            features[f"games_{subgroup}"] = subgroup_games[subgroup]
            features[f"played_games_{subgroup}"] = subgroup_played[subgroup]

    features["game_share_combat"] = safe_divide(
        macro_games["combat"],
        categorized_games,
    )
    features["game_share_exploration"] = safe_divide(
        macro_games["exploration"],
        categorized_games,
    )
    features["game_share_strategic_reasoning"] = safe_divide(
        macro_games["strategic_reasoning"],
        categorized_games,
    )
    features["played_game_share_combat"] = safe_divide(
        macro_played["combat"],
        categorized_played_games,
    )
    features["played_game_share_exploration"] = safe_divide(
        macro_played["exploration"],
        categorized_played_games,
    )
    features["played_game_share_strategic_reasoning"] = safe_divide(
        macro_played["strategic_reasoning"],
        categorized_played_games,
    )

    return features


def vectorize(
    features: dict[str, Any],
    required_features: list[str],
) -> pd.DataFrame:
    missing = [
        feature
        for feature in required_features
        if feature not in features
    ]

    if missing:
        raise ValueError(
            "Missing model features: " + ", ".join(missing)
        )

    values: dict[str, float] = {}

    for feature in required_features:
        value = safe_float(features[feature])

        if not np.isfinite(value):
            raise ValueError(
                f"Feature is non-finite: {feature}"
            )

        values[feature] = value

    return pd.DataFrame(
        [values],
        columns=required_features,
    )


def get_classes(pipeline: Any) -> list[str]:
    if hasattr(pipeline, "classes_"):
        return [str(value) for value in pipeline.classes_]

    if hasattr(pipeline, "named_steps"):
        model = pipeline.named_steps.get("model")

        if model is not None and hasattr(model, "classes_"):
            return [str(value) for value in model.classes_]

    raise ValueError("Unable to determine model classes.")


def render_output(
    payload: dict[str, Any],
    output_path: Path | None,
) -> None:
    rendered = json.dumps(
        payload,
        ensure_ascii=False,
        indent=2,
    )
    print(rendered)

    if output_path is not None:
        output_path.parent.mkdir(
            parents=True,
            exist_ok=True,
        )
        with output_path.open(
            "w",
            encoding="utf-8",
        ) as file:
            file.write(rendered + "\n")


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    args = parse_args()

    try:
        api_key = load_api_key()
        session = create_http_session()

        LOGGER.info(
            "Fetching visible Steam library for SteamID %s.",
            args.steamid,
        )

        raw_games = fetch_owned_games(
            session,
            api_key,
            args.steamid,
            args.timeout,
        )
        games = normalize_games(raw_games)
        mapping = read_mapping(args.mapping)
        features = build_features(games, mapping)

        playtime_coverage = float(
            features["category_playtime_coverage"]
        )

        quality = {
            "total_library_games": int(
                features["total_library_games"]
            ),
            "total_played_games": int(
                features["total_played_games"]
            ),
            "categorized_games": int(
                features["categorized_games"]
            ),
            "categorized_played_games": int(
                features["categorized_played_games"]
            ),
            "category_game_coverage": round(
                float(features["category_game_coverage"]),
                6,
            ),
            "category_playtime_coverage": round(
                playtime_coverage,
                6,
            ),
            "minimum_playtime_coverage": (
                args.minimum_playtime_coverage
            ),
        }

        if playtime_coverage < args.minimum_playtime_coverage:
            render_output(
                {
                    "status": "insufficient_mapping_coverage",
                    "steamid": args.steamid,
                    "predicted_category": None,
                    "probabilities": None,
                    "profile_quality": quality,
                    "message": (
                        "The visible library was collected, but categorized "
                        "playtime coverage is below the configured threshold."
                    ),
                },
                args.output,
            )
            return 2

        if not args.model.exists():
            raise FileNotFoundError(args.model)

        bundle = joblib.load(args.model)

        if not isinstance(bundle, dict):
            raise ValueError(
                "Unexpected final model artifact format."
            )

        pipeline = bundle.get("pipeline")
        required_features = bundle.get("features")

        if pipeline is None:
            raise ValueError(
                "Model artifact does not contain 'pipeline'."
            )
        if not isinstance(required_features, list):
            raise ValueError(
                "Model artifact does not contain feature list."
            )

        X = vectorize(
            features,
            required_features,
        )

        prediction = str(
            pipeline.predict(X)[0]
        )
        probability_values = pipeline.predict_proba(X)[0]
        classes = get_classes(pipeline)

        probabilities = {
            label: round(float(probability), 8)
            for label, probability in zip(
                classes,
                probability_values,
                strict=True,
            )
        }

        metadata: dict[str, Any] = {}
        if args.metadata.exists():
            with args.metadata.open(
                "r",
                encoding="utf-8",
            ) as file:
                metadata = json.load(file)

        result = {
            "status": "ok",
            "steamid": args.steamid,
            "predicted_category": prediction,
            "probabilities": probabilities,
            "profile_quality": quality,
            "inference": {
                "feature_count": len(required_features),
                "feature_set": bundle.get("feature_set"),
                "model_training_profiles": metadata.get(
                    "training_profiles"
                ),
                "source_game_excluded": False,
                "library_source": "steam_web_api",
                "mapping_source": str(args.mapping),
            },
        }

        render_output(
            result,
            args.output,
        )

    except requests.RequestException as error:
        LOGGER.exception(
            "Steam request failed: %s",
            error,
        )
        return 1
    except Exception as error:
        LOGGER.exception(
            "Live Steam inference failed: %s",
            error,
        )
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())