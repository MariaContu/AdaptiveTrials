"""09c - Build leakage-controlled recency feature set for model V2.

This corrected version reconstructs the six optimized V1 ratio features from
the official source-excluded profile dataset before adding recency features.

It does NOT retrain any model.

Outputs:
- data/processed/final_player_profiles_recency_v2.csv
- config/model_features_recency_v2.json
- reports/metrics/09c_recency_feature_policy_summary.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import joblib
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_source_excluded.csv"
)
DEFAULT_RECENCY = (
    ROOT / "data/interim/player_recency_features_v2.csv"
)
DEFAULT_MODEL = (
    ROOT / "models/final/macro_model_optimized.joblib"
)

DEFAULT_OUTPUT = (
    ROOT / "data/processed/final_player_profiles_recency_v2.csv"
)
DEFAULT_CONFIG = (
    ROOT / "config/model_features_recency_v2.json"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/09c_recency_feature_policy_summary.json"
)

V1_RATIO_FEATURES = [
    "game_share_combat",
    "game_share_exploration",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "played_game_share_strategic_reasoning",
]

ALLOWED_RECENCY_FEATURES = [
    "has_recent_activity",
    "recent_total_playtime_minutes",
    "recent_active_games",
    "recent_categorized_playtime_minutes",
    "recent_categorized_active_games",
    "recent_category_playtime_coverage",
    "recent_active_games_combat",
    "recent_active_games_exploration",
    "recent_active_games_strategic_reasoning",
    "recent_active_game_share_combat",
    "recent_active_game_share_exploration",
    "recent_active_game_share_strategic_reasoning",
]

EXCLUDED_RECENCY_FEATURES = [
    "recent_playtime_combat_minutes",
    "recent_playtime_exploration_minutes",
    "recent_playtime_strategic_reasoning_minutes",
    "recent_share_combat",
    "recent_share_exploration",
    "recent_share_strategic_reasoning",
    "recent_dominant_category",
    "recent_dominance",
    "recent_second_max",
    "recent_gap",
    "recent_matches_historical_target",
    "historical_target",
]


def safe_divide_series(
    numerator: pd.Series,
    denominator: pd.Series,
) -> pd.Series:
    denominator = pd.to_numeric(
        denominator,
        errors="coerce",
    ).fillna(0.0)

    numerator = pd.to_numeric(
        numerator,
        errors="coerce",
    ).fillna(0.0)

    result = pd.Series(
        0.0,
        index=numerator.index,
        dtype=float,
    )

    mask = denominator > 0

    result.loc[mask] = (
        numerator.loc[mask]
        / denominator.loc[mask]
    )

    return result


def reconstruct_v1_ratio_features(
    profiles: pd.DataFrame,
) -> pd.DataFrame:
    df = profiles.copy()

    required = [
        "categorized_games",
        "categorized_played_games",
        "games_combat",
        "games_exploration",
        "games_strategic_reasoning",
        "played_games_combat",
        "played_games_exploration",
        "played_games_strategic_reasoning",
    ]

    missing = [
        column
        for column in required
        if column not in df.columns
    ]

    if missing:
        raise ValueError(
            "Cannot reconstruct V1 optimized ratios. "
            f"Missing base columns: {missing}"
        )

    df["game_share_combat"] = safe_divide_series(
        df["games_combat"],
        df["categorized_games"],
    )
    df["game_share_exploration"] = safe_divide_series(
        df["games_exploration"],
        df["categorized_games"],
    )
    df["game_share_strategic_reasoning"] = safe_divide_series(
        df["games_strategic_reasoning"],
        df["categorized_games"],
    )

    df["played_game_share_combat"] = safe_divide_series(
        df["played_games_combat"],
        df["categorized_played_games"],
    )
    df["played_game_share_exploration"] = safe_divide_series(
        df["played_games_exploration"],
        df["categorized_played_games"],
    )
    df[
        "played_game_share_strategic_reasoning"
    ] = safe_divide_series(
        df["played_games_strategic_reasoning"],
        df["categorized_played_games"],
    )

    return df


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--profiles",
        type=Path,
        default=DEFAULT_PROFILES,
    )
    parser.add_argument(
        "--recency",
        type=Path,
        default=DEFAULT_RECENCY,
    )
    parser.add_argument(
        "--model",
        type=Path,
        default=DEFAULT_MODEL,
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
    )
    parser.add_argument(
        "--config",
        type=Path,
        default=DEFAULT_CONFIG,
    )
    parser.add_argument(
        "--summary",
        type=Path,
        default=DEFAULT_SUMMARY,
    )
    return parser.parse_args()


def require_columns(
    df: pd.DataFrame,
    columns: list[str],
    label: str,
) -> None:
    missing = [
        column
        for column in columns
        if column not in df.columns
    ]

    if missing:
        raise ValueError(
            f"{label} missing required columns: {missing}"
        )


def feature_stats(
    df: pd.DataFrame,
    features: list[str],
) -> dict[str, Any]:
    stats: dict[str, Any] = {}

    for feature in features:
        series = pd.to_numeric(
            df[feature],
            errors="coerce",
        ).fillna(0.0)

        stats[feature] = {
            "missing_count": int(
                df[feature].isna().sum()
            ),
            "zero_count": int(
                series.eq(0).sum()
            ),
            "nonzero_count": int(
                series.ne(0).sum()
            ),
            "mean": float(
                series.mean()
            ),
            "median": float(
                series.median()
            ),
        }

    return stats


def main() -> int:
    args = parse_args()

    profiles = pd.read_csv(args.profiles)
    recency = pd.read_csv(args.recency)

    require_columns(
        profiles,
        ["final_player_id"],
        "profiles",
    )

    require_columns(
        recency,
        [
            "final_player_id",
            *ALLOWED_RECENCY_FEATURES,
        ],
        "recency",
    )

    if not args.model.exists():
        raise FileNotFoundError(args.model)

    bundle = joblib.load(args.model)

    if not isinstance(bundle, dict):
        raise ValueError(
            "Unexpected V1 final model artifact format."
        )

    v1_features = bundle.get("features")

    if not isinstance(v1_features, list):
        raise ValueError(
            "V1 model artifact does not contain feature list."
        )

    profiles = reconstruct_v1_ratio_features(
        profiles
    )

    # Verify the reconstructed historical V1 feature set.
    require_columns(
        profiles,
        v1_features,
        "profiles after V1 ratio reconstruction",
    )

    profiles["final_player_id"] = (
        profiles["final_player_id"]
        .astype(str)
    )
    recency["final_player_id"] = (
        recency["final_player_id"]
        .astype(str)
    )

    if profiles["final_player_id"].duplicated().any():
        raise ValueError(
            "Duplicate player IDs in profile dataset."
        )

    if recency["final_player_id"].duplicated().any():
        raise ValueError(
            "Duplicate player IDs in recency dataset."
        )

    recency_keep = recency[
        [
            "final_player_id",
            *ALLOWED_RECENCY_FEATURES,
        ]
    ].copy()

    merged = profiles.merge(
        recency_keep,
        on="final_player_id",
        how="left",
        validate="one_to_one",
        indicator=True,
    )

    missing_recency_rows = int(
        (merged["_merge"] != "both").sum()
    )

    if missing_recency_rows:
        raise ValueError(
            f"{missing_recency_rows} profiles have no recency row."
        )

    merged = merged.drop(
        columns=["_merge"]
    )

    for feature in ALLOWED_RECENCY_FEATURES:
        merged[feature] = pd.to_numeric(
            merged[feature],
            errors="coerce",
        ).fillna(0.0)

    v2_features = [
        *v1_features,
        *[
            feature
            for feature in ALLOWED_RECENCY_FEATURES
            if feature not in v1_features
        ],
    ]

    require_columns(
        merged,
        v2_features,
        "merged V2 profiles",
    )

    args.output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    merged.to_csv(
        args.output,
        index=False,
    )

    config = {
        "version": "recency_v2",
        "baseline": {
            "model_artifact": str(args.model),
            "feature_set": bundle.get(
                "feature_set"
            ),
            "feature_count": len(
                v1_features
            ),
            "features": v1_features,
            "reconstructed_features": (
                V1_RATIO_FEATURES
            ),
        },
        "recency_policy": {
            "source": (
                "playtime_2weeks_minutes"
            ),
            "allowed_features": (
                ALLOWED_RECENCY_FEATURES
            ),
            "excluded_features": (
                EXCLUDED_RECENCY_FEATURES
            ),
            "rationale": (
                "Category-specific recent playtime "
                "variables are excluded from "
                "supervised V2 training because the "
                "macro target is derived from "
                "category-specific lifetime "
                "playtime. Recent playtime is a "
                "temporal subset of that signal and "
                "could partially reconstruct the "
                "target. V2 therefore uses recent "
                "activity counts, overall recent "
                "intensity and coverage signals."
            ),
        },
        "v2": {
            "feature_count": len(
                v2_features
            ),
            "features": v2_features,
        },
    }

    args.config.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    args.config.write_text(
        json.dumps(
            config,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    summary = {
        "stage": (
            "09c_recency_feature_policy"
        ),
        "players": int(
            len(merged)
        ),
        "baseline_feature_count": len(
            v1_features
        ),
        "reconstructed_v1_ratio_features": (
            V1_RATIO_FEATURES
        ),
        "allowed_recency_feature_count": len(
            ALLOWED_RECENCY_FEATURES
        ),
        "excluded_recency_feature_count": len(
            EXCLUDED_RECENCY_FEATURES
        ),
        "v2_feature_count": len(
            v2_features
        ),
        "allowed_recency_features": (
            ALLOWED_RECENCY_FEATURES
        ),
        "excluded_recency_features": (
            EXCLUDED_RECENCY_FEATURES
        ),
        "recency_feature_stats": (
            feature_stats(
                merged,
                ALLOWED_RECENCY_FEATURES,
            )
        ),
        "outputs": {
            "profiles_v2": str(
                args.output
            ),
            "feature_config": str(
                args.config
            ),
        },
    }

    args.summary.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    args.summary.write_text(
        json.dumps(
            summary,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print(
        "\n=== RECENCY FEATURE POLICY V2 ==="
    )
    print(
        f"Players: {len(merged)}"
    )
    print(
        "Reconstructed V1 ratio features: "
        f"{len(V1_RATIO_FEATURES)}"
    )
    print(
        f"Baseline V1 features: {len(v1_features)}"
    )
    print(
        "Allowed recency features: "
        f"{len(ALLOWED_RECENCY_FEATURES)}"
    )
    print(
        "Excluded recency features: "
        f"{len(EXCLUDED_RECENCY_FEATURES)}"
    )
    print(
        "Candidate V2 feature count: "
        f"{len(v2_features)}"
    )

    print("\nAllowed:")
    for feature in ALLOWED_RECENCY_FEATURES:
        print(f"  + {feature}")

    print(
        "\nExcluded from supervised V2:"
    )
    for feature in EXCLUDED_RECENCY_FEATURES:
        print(f"  - {feature}")

    print(
        f"\nSaved V2 profiles: {args.output}"
    )
    print(
        f"Saved feature config: {args.config}"
    )
    print(
        f"Saved summary: {args.summary}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())