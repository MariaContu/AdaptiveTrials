"""Run macro preference inference with the final Adaptive Trials model."""

from __future__ import annotations

import argparse
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

DEFAULT_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_METADATA = ROOT / "models/final/macro_model_optimized_metadata.json"

RATIO_FEATURES = [
    "game_share_combat",
    "game_share_exploration",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "played_game_share_strategic_reasoning",
]

LOGGER = logging.getLogger("macro-inference")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run inference with the final macro preference model."
    )
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--profile-json", type=Path)
    source.add_argument("--profile-csv", type=Path)

    parser.add_argument("--player-id", type=str)
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--metadata", type=Path, default=DEFAULT_METADATA)
    parser.add_argument("--output", type=Path, default=None)
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)
    with path.open("r", encoding="utf-8") as file:
        payload = json.load(file)
    if not isinstance(payload, dict):
        raise ValueError("Profile JSON must contain one object.")
    return payload


def load_profile_from_csv(path: Path, player_id: str | None) -> dict[str, Any]:
    if player_id is None:
        raise ValueError("--player-id is required with --profile-csv.")
    if not path.exists():
        raise FileNotFoundError(path)

    df = pd.read_csv(path, dtype={"final_player_id": str})
    if "final_player_id" not in df.columns:
        raise ValueError("CSV does not contain final_player_id.")

    matches = df[df["final_player_id"].astype(str) == str(player_id)]
    if len(matches) == 0:
        raise ValueError(f"Player not found: {player_id}")
    if len(matches) > 1:
        raise ValueError(f"Duplicate player id found: {player_id}")

    return matches.iloc[0].to_dict()


def safe_divide(numerator: float, denominator: float) -> float:
    if denominator <= 0:
        return 0.0
    return float(numerator / denominator)


def add_ratio_features(profile: dict[str, Any]) -> dict[str, Any]:
    result = dict(profile)

    def number(name: str) -> float:
        value = result.get(name, 0)
        if value is None:
            return 0.0
        if isinstance(value, float) and math.isnan(value):
            return 0.0
        try:
            return float(value)
        except (TypeError, ValueError):
            raise ValueError(f"Feature '{name}' must be numeric, got {value!r}.")

    categorized_games = number("categorized_games")
    categorized_played_games = number("categorized_played_games")

    derived = {
        "game_share_combat": safe_divide(number("games_combat"), categorized_games),
        "game_share_exploration": safe_divide(number("games_exploration"), categorized_games),
        "game_share_strategic_reasoning": safe_divide(
            number("games_strategic_reasoning"), categorized_games
        ),
        "played_game_share_combat": safe_divide(
            number("played_games_combat"), categorized_played_games
        ),
        "played_game_share_exploration": safe_divide(
            number("played_games_exploration"), categorized_played_games
        ),
        "played_game_share_strategic_reasoning": safe_divide(
            number("played_games_strategic_reasoning"), categorized_played_games
        ),
    }

    for key, value in derived.items():
        if key not in result:
            result[key] = value

    return result


def validate_and_vectorize(
    profile: dict[str, Any],
    features: list[str],
) -> tuple[pd.DataFrame, dict[str, Any]]:
    profile = add_ratio_features(profile)

    missing = [name for name in features if name not in profile]
    if missing:
        raise ValueError(
            "Profile is missing required features: " + ", ".join(missing)
        )

    values: dict[str, float] = {}
    invalid: list[str] = []

    for feature in features:
        raw = profile[feature]
        try:
            value = float(raw)
        except (TypeError, ValueError):
            invalid.append(feature)
            continue

        if not np.isfinite(value):
            invalid.append(feature)
            continue

        values[feature] = value

    if invalid:
        raise ValueError(
            "Non-numeric or non-finite features: " + ", ".join(invalid)
        )

    frame = pd.DataFrame([values], columns=features)
    validation = {
        "required_feature_count": len(features),
        "provided_feature_count": len(values),
        "missing_features": [],
        "invalid_features": [],
        "ratio_feature_count": len(
            [feature for feature in RATIO_FEATURES if feature in values]
        ),
    }
    return frame, validation


def model_classes(pipeline: Any) -> list[str]:
    if hasattr(pipeline, "classes_"):
        return [str(value) for value in pipeline.classes_]

    if hasattr(pipeline, "named_steps"):
        model = pipeline.named_steps.get("model")
        if model is not None and hasattr(model, "classes_"):
            return [str(value) for value in model.classes_]

    raise ValueError("Unable to determine model classes.")


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    args = parse_args()

    try:
        if not args.model.exists():
            raise FileNotFoundError(args.model)

        bundle = joblib.load(args.model)
        if not isinstance(bundle, dict):
            raise ValueError("Unexpected final model artifact format.")

        pipeline = bundle.get("pipeline")
        features = bundle.get("features")

        if pipeline is None:
            raise ValueError("Model bundle does not contain 'pipeline'.")
        if not isinstance(features, list) or not features:
            raise ValueError("Model bundle does not contain a valid feature list.")

        if args.profile_json is not None:
            profile = load_json(args.profile_json)
            source_description = str(args.profile_json)
        else:
            profile = load_profile_from_csv(args.profile_csv, args.player_id)
            source_description = (
                f"{args.profile_csv}#final_player_id={args.player_id}"
            )

        X, validation = validate_and_vectorize(profile, features)

        prediction = str(pipeline.predict(X)[0])

        if not hasattr(pipeline, "predict_proba"):
            raise ValueError("Final pipeline does not support predict_proba.")

        probabilities_raw = pipeline.predict_proba(X)[0]
        classes = model_classes(pipeline)

        probabilities = {
            label: round(float(probability), 8)
            for label, probability in zip(classes, probabilities_raw, strict=True)
        }

        metadata: dict[str, Any] = {}
        if args.metadata.exists():
            metadata = load_json(args.metadata)

        result = {
            "predicted_category": prediction,
            "probabilities": probabilities,
            "model": {
                "artifact": str(args.model),
                "feature_set": bundle.get("feature_set"),
                "feature_count": len(features),
                "classes": classes,
                "training_profiles": metadata.get("training_profiles"),
            },
            "input_validation": {
                **validation,
                "source": source_description,
            },
        }

        rendered = json.dumps(result, ensure_ascii=False, indent=2)
        print(rendered)

        if args.output is not None:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            with args.output.open("w", encoding="utf-8") as file:
                file.write(rendered + "\n")

    except Exception as error:
        LOGGER.exception("Macro inference failed: %s", error)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())