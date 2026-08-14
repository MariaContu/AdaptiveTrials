"""End-to-end local macro inference for Adaptive Trials.

This stage combines:
- player-game rows;
- final game-category mapping;
- source-game exclusion for parity;
- reconstruction of the 40 deployment features;
- final optimized model loading;
- predict + predict_proba.

It does not call the Steam API yet. It validates the complete local inference
path before connecting live Steam data and the backend API.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import sys
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_GAMES = ROOT / "data/interim/final_player_games_1000.csv"
DEFAULT_MAPPING = ROOT / "data/interim/final_game_category_scores_v2.csv"
DEFAULT_STATUS = ROOT / "data/interim/final_player_library_status_1000.csv"
DEFAULT_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_METADATA = ROOT / "models/final/macro_model_optimized_metadata.json"

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

LOGGER = logging.getLogger("local-macro-inference")


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


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None:
            raise ValueError(f"CSV has no header: {path}")
        return list(reader.fieldnames), [dict(row) for row in reader]


def parse_source_appids(value: Any) -> set[int]:
    text = norm(value)
    if not text:
        return set()

    parts = [text]
    for separator in (";", "|", ","):
        if separator in text:
            parts = [part.strip() for part in text.split(separator)]
            break

    return {
        safe_int(part)
        for part in parts
        if safe_int(part) > 0
    }


def load_mapping(path: Path) -> dict[int, dict[str, str]]:
    fields, rows = read_csv(path)

    required = {
        "appid",
        "assigned_category",
        "assigned_subgroup",
    }
    missing = required - set(fields)
    if missing:
        raise ValueError(
            f"Mapping CSV missing columns: {sorted(missing)}"
        )

    mapping: dict[int, dict[str, str]] = {}

    for row in rows:
        appid = safe_int(row.get("appid"))
        if appid > 0:
            mapping[appid] = row

    return mapping


def load_player_games(
    path: Path,
    player_id: str,
) -> list[dict[str, str]]:
    fields, rows = read_csv(path)

    required = {
        "final_player_id",
        "appid",
        "playtime_forever_minutes",
    }
    missing = required - set(fields)
    if missing:
        raise ValueError(
            f"Games CSV missing columns: {sorted(missing)}"
        )

    result = [
        row
        for row in rows
        if norm(row.get("final_player_id")) == player_id
    ]

    if not result:
        raise ValueError(
            f"No player-game rows found for player: {player_id}"
        )

    return result


def load_source_appids(
    path: Path,
    player_id: str,
) -> set[int]:
    fields, rows = read_csv(path)

    required = {"final_player_id", "source_appid"}
    missing = required - set(fields)
    if missing:
        raise ValueError(
            f"Status CSV missing columns: {sorted(missing)}"
        )

    matches = [
        row
        for row in rows
        if norm(row.get("final_player_id")) == player_id
    ]

    if len(matches) != 1:
        raise ValueError(
            f"Expected one status row for {player_id}, "
            f"found {len(matches)}"
        )

    return parse_source_appids(matches[0].get("source_appid"))


def build_features(
    game_rows: list[dict[str, str]],
    mapping: dict[int, dict[str, str]],
    excluded_appids: set[int],
) -> dict[str, float | int]:
    included = [
        row
        for row in game_rows
        if safe_int(row.get("appid")) not in excluded_appids
    ]

    total_library_games = len(included)
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

    for row in included:
        appid = safe_int(row.get("appid"))
        playtime = safe_int(row.get("playtime_forever_minutes"))

        if playtime > 0:
            total_played_games += 1
        total_playtime_minutes += playtime

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
        features[f"played_games_{category}"] = (
            macro_played[category]
        )

    for category in MACRO_CATEGORIES:
        for subgroup in SUBGROUPS[category]:
            features[f"games_{subgroup}"] = (
                subgroup_games[subgroup]
            )
            features[f"played_games_{subgroup}"] = (
                subgroup_played[subgroup]
            )

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
    features["played_game_share_strategic_reasoning"] = (
        safe_divide(
            macro_played["strategic_reasoning"],
            categorized_played_games,
        )
    )

    return features


def validate_features(
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
    invalid: list[str] = []

    for feature in required_features:
        value = safe_float(features[feature])

        if not np.isfinite(value):
            invalid.append(feature)
        else:
            values[feature] = value

    if invalid:
        raise ValueError(
            "Invalid model features: " + ", ".join(invalid)
        )

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


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()

    parser.add_argument("--player-id", required=True)
    parser.add_argument("--games", type=Path, default=DEFAULT_GAMES)
    parser.add_argument("--mapping", type=Path, default=DEFAULT_MAPPING)
    parser.add_argument("--status", type=Path, default=DEFAULT_STATUS)
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--metadata", type=Path, default=DEFAULT_METADATA)

    parser.add_argument(
        "--include-source-game",
        action="store_true",
        help=(
            "Use the complete library. By default, the recruitment "
            "source game is excluded for parity with model training."
        ),
    )
    parser.add_argument("--output", type=Path, default=None)

    return parser.parse_args()


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    args = parse_args()

    try:
        mapping = load_mapping(args.mapping)
        game_rows = load_player_games(
            args.games,
            args.player_id,
        )

        excluded_appids: set[int] = set()

        if not args.include_source_game:
            excluded_appids = load_source_appids(
                args.status,
                args.player_id,
            )

        features = build_features(
            game_rows,
            mapping,
            excluded_appids,
        )

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

        X = validate_features(
            features,
            required_features,
        )

        predicted_category = str(
            pipeline.predict(X)[0]
        )

        probabilities_raw = pipeline.predict_proba(X)[0]
        classes = get_classes(pipeline)

        probabilities = {
            label: round(float(probability), 8)
            for label, probability in zip(
                classes,
                probabilities_raw,
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
            "player_id": args.player_id,
            "predicted_category": predicted_category,
            "probabilities": probabilities,
            "profile_quality": {
                "total_library_games": (
                    features["total_library_games"]
                ),
                "categorized_games": (
                    features["categorized_games"]
                ),
                "category_game_coverage": round(
                    float(
                        features["category_game_coverage"]
                    ),
                    6,
                ),
                "category_playtime_coverage": round(
                    float(
                        features["category_playtime_coverage"]
                    ),
                    6,
                ),
            },
            "inference": {
                "feature_count": len(required_features),
                "feature_set": bundle.get("feature_set"),
                "excluded_appids": sorted(excluded_appids),
                "source_game_excluded": (
                    not args.include_source_game
                ),
                "model_training_profiles": metadata.get(
                    "training_profiles"
                ),
            },
            "features": {
                feature: features[feature]
                for feature in required_features
            },
        }

        rendered = json.dumps(
            result,
            ensure_ascii=False,
            indent=2,
        )
        print(rendered)

        if args.output is not None:
            args.output.parent.mkdir(
                parents=True,
                exist_ok=True,
            )
            with args.output.open(
                "w",
                encoding="utf-8",
            ) as file:
                file.write(rendered + "\n")

    except Exception as error:
        LOGGER.exception(
            "Local macro inference failed: %s",
            error,
        )
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())