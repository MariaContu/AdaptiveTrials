"""09k - Local inference with the final recency V2 macro model.

This stage validates deployment inference using already-built V2 profiles.

Inputs
------
- models/final/macro_model_recency_v2.joblib
- data/processed/final_player_profiles_recency_v2.csv

Outputs
-------
- prediction printed to terminal
- optional JSON report under reports/metrics/

No Steam API call is made here.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_MODEL = (
    ROOT / "models/final/macro_model_recency_v2.joblib"
)
DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_recency_v2.csv"
)
DEFAULT_OUTPUT = (
    ROOT / "reports/metrics/09k_local_recency_v2_prediction.json"
)

PLAYER_ID_CANDIDATES = (
    "final_player_id",
    "player_id",
)

TARGET_CANDIDATES = (
    "target_macro",
    "macro_target",
    "target",
    "resolved_target",
    "target_category",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--player-id",
        type=str,
        default="general_final_0003",
        help="Internal player ID to predict.",
    )
    parser.add_argument(
        "--model",
        type=Path,
        default=DEFAULT_MODEL,
    )
    parser.add_argument(
        "--profiles",
        type=Path,
        default=DEFAULT_PROFILES,
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
    )

    return parser.parse_args()


def detect_column(
    df: pd.DataFrame,
    candidates: tuple[str, ...],
    label: str,
) -> str | None:
    for candidate in candidates:
        if candidate in df.columns:
            return candidate

    if label == "target":
        return None

    raise ValueError(
        f"Could not detect {label}. Tried: {list(candidates)}"
    )


def load_bundle(
    model_path: Path,
) -> dict[str, Any]:
    if not model_path.exists():
        raise FileNotFoundError(model_path)

    bundle = joblib.load(model_path)

    if not isinstance(bundle, dict):
        raise ValueError(
            "Unexpected final V2 model bundle format."
        )

    required = [
        "pipeline",
        "features",
        "classes",
    ]

    missing = [
        key
        for key in required
        if key not in bundle
    ]

    if missing:
        raise ValueError(
            f"Final V2 bundle missing keys: {missing}"
        )

    features = bundle["features"]

    if not isinstance(features, list) or not features:
        raise ValueError(
            "Final V2 bundle has no valid feature list."
        )

    return bundle


def numeric_vector(
    row: pd.Series,
    features: list[str],
) -> pd.DataFrame:
    missing = [
        feature
        for feature in features
        if feature not in row.index
    ]

    if missing:
        raise ValueError(
            f"Profile row missing model features: {missing}"
        )

    values = {}

    for feature in features:
        value = pd.to_numeric(
            pd.Series([row[feature]]),
            errors="coerce",
        ).iloc[0]

        if pd.isna(value):
            raise ValueError(
                f"Feature {feature!r} is missing/non-numeric."
            )

        value = float(value)

        if not np.isfinite(value):
            raise ValueError(
                f"Feature {feature!r} is not finite."
            )

        values[feature] = [value]

    return pd.DataFrame(
        values,
        columns=features,
    )


def main() -> int:
    args = parse_args()

    bundle = load_bundle(
        args.model
    )

    profiles = pd.read_csv(
        args.profiles
    )

    player_id_col = detect_column(
        profiles,
        PLAYER_ID_CANDIDATES,
        "player ID",
    )

    target_col = detect_column(
        profiles,
        TARGET_CANDIDATES,
        "target",
    )

    profiles[player_id_col] = (
        profiles[player_id_col]
        .astype(str)
    )

    matches = profiles.loc[
        profiles[player_id_col]
        == str(args.player_id)
    ]

    if matches.empty:
        raise ValueError(
            f"Player {args.player_id!r} not found."
        )

    if len(matches) > 1:
        raise ValueError(
            f"Player {args.player_id!r} appears more than once."
        )

    row = matches.iloc[0]

    features = [
        str(feature)
        for feature in bundle["features"]
    ]

    X = numeric_vector(
        row,
        features,
    )

    model = bundle["pipeline"]

    prediction = str(
        model.predict(X)[0]
    )

    probabilities_raw = (
        model.predict_proba(X)[0]
    )

    model_classes = [
        str(value)
        for value in model.classes_
    ]

    probabilities = {
        label: float(probability)
        for label, probability in zip(
            model_classes,
            probabilities_raw,
        )
    }

    expected_target = None

    if target_col is not None:
        target_value = row[target_col]

        if not pd.isna(target_value):
            target_text = str(
                target_value
            ).strip()

            if target_text:
                expected_target = target_text

    recent_summary = {
        "has_recent_activity": int(
            float(
                row.get(
                    "has_recent_activity",
                    0,
                )
            )
        ),
        "recent_total_playtime_minutes": float(
            row.get(
                "recent_total_playtime_minutes",
                0.0,
            )
        ),
        "recent_active_games": int(
            float(
                row.get(
                    "recent_active_games",
                    0,
                )
            )
        ),
        "recent_categorized_playtime_minutes": float(
            row.get(
                "recent_categorized_playtime_minutes",
                0.0,
            )
        ),
        "recent_categorized_active_games": int(
            float(
                row.get(
                    "recent_categorized_active_games",
                    0,
                )
            )
        ),
        "recent_category_playtime_coverage": float(
            row.get(
                "recent_category_playtime_coverage",
                0.0,
            )
        ),
        "recent_active_games_combat": int(
            float(
                row.get(
                    "recent_active_games_combat",
                    0,
                )
            )
        ),
        "recent_active_games_exploration": int(
            float(
                row.get(
                    "recent_active_games_exploration",
                    0,
                )
            )
        ),
        "recent_active_games_strategic_reasoning": int(
            float(
                row.get(
                    "recent_active_games_strategic_reasoning",
                    0,
                )
            )
        ),
    }

    result = {
        "stage": "09k_local_recency_v2_inference",
        "player_id": str(
            args.player_id
        ),
        "model": str(
            args.model
        ),
        "model_version": bundle.get(
            "version",
            "recency_v2",
        ),
        "feature_set": bundle.get(
            "feature_set"
        ),
        "feature_count": len(
            features
        ),
        "predicted_category": prediction,
        "probabilities": probabilities,
        "expected_target": expected_target,
        "prediction_matches_target": (
            None
            if expected_target is None
            else prediction == expected_target
        ),
        "recent_summary": recent_summary,
    }

    args.output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    args.output.write_text(
        json.dumps(
            result,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print(
        "\n=== LOCAL RECENCY V2 INFERENCE ==="
    )
    print(
        f"Player: {args.player_id}"
    )
    print(
        f"Model version: {result['model_version']}"
    )
    print(
        f"Feature set: {result['feature_set']}"
    )
    print(
        f"Feature count: {result['feature_count']}"
    )

    print(
        f"\nPredicted category: {prediction}"
    )

    if expected_target is not None:
        print(
            f"Historical target: {expected_target}"
        )
        print(
            "Prediction matches target: "
            f"{prediction == expected_target}"
        )

    print("\nProbabilities:")
    for label in sorted(
        probabilities.keys()
    ):
        print(
            f"  {label}: "
            f"{probabilities[label]:.8f}"
        )

    print("\nRecent activity:")
    print(
        "  total recent minutes: "
        f"{recent_summary['recent_total_playtime_minutes']:.2f}"
    )
    print(
        "  recent active games: "
        f"{recent_summary['recent_active_games']}"
    )
    print(
        "  categorized recent games: "
        f"{recent_summary['recent_categorized_active_games']}"
    )
    print(
        "  recent coverage: "
        f"{recent_summary['recent_category_playtime_coverage']:.6f}"
    )
    print(
        "  combat recent games: "
        f"{recent_summary['recent_active_games_combat']}"
    )
    print(
        "  exploration recent games: "
        f"{recent_summary['recent_active_games_exploration']}"
    )
    print(
        "  strategic recent games: "
        f"{recent_summary['recent_active_games_strategic_reasoning']}"
    )

    print(
        f"\nSaved prediction: {args.output}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())