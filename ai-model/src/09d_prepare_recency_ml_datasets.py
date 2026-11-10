"""09d - Prepare macro train/test datasets for recency model V2.

This stage preserves EXACTLY the V1 macro train/test membership.

Inputs:
- data/processed/final_player_profiles_recency_v2.csv
- data/modeling/macro_train.csv
- data/modeling/macro_test.csv
- config/model_features_recency_v2.json

Outputs:
- data/modeling/macro_train_recency_v2.csv
- data/modeling/macro_test_recency_v2.csv
- reports/metrics/09d_recency_ml_dataset_preparation_summary.json

No balancing or resampling is applied here.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import pandas as pd

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_recency_v2.csv"
)
DEFAULT_V1_TRAIN = ROOT / "data/modeling/macro_train.csv"
DEFAULT_V1_TEST = ROOT / "data/modeling/macro_test.csv"
DEFAULT_CONFIG = ROOT / "config/model_features_recency_v2.json"

DEFAULT_TRAIN_OUT = (
    ROOT / "data/modeling/macro_train_recency_v2.csv"
)
DEFAULT_TEST_OUT = (
    ROOT / "data/modeling/macro_test_recency_v2.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/09d_recency_ml_dataset_preparation_summary.json"
)

ID_CANDIDATES = (
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
    parser.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    parser.add_argument("--v1-train", type=Path, default=DEFAULT_V1_TRAIN)
    parser.add_argument("--v1-test", type=Path, default=DEFAULT_V1_TEST)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--train-output", type=Path, default=DEFAULT_TRAIN_OUT)
    parser.add_argument("--test-output", type=Path, default=DEFAULT_TEST_OUT)
    parser.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    return parser.parse_args()


def detect_column(
    df: pd.DataFrame,
    candidates: tuple[str, ...],
    label: str,
) -> str:
    for candidate in candidates:
        if candidate in df.columns:
            return candidate

    raise ValueError(
        f"Could not detect {label}. "
        f"Tried: {list(candidates)}"
    )


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


def class_counts(
    df: pd.DataFrame,
    target_column: str,
) -> dict[str, int]:
    counts = (
        df[target_column]
        .astype(str)
        .value_counts()
        .to_dict()
    )

    return {
        str(key): int(value)
        for key, value in counts.items()
    }


def split_overlap(
    train_ids: set[str],
    test_ids: set[str],
) -> list[str]:
    return sorted(train_ids & test_ids)


def main() -> int:
    args = parse_args()

    profiles = pd.read_csv(args.profiles)
    v1_train = pd.read_csv(args.v1_train)
    v1_test = pd.read_csv(args.v1_test)

    config: dict[str, Any] = json.loads(
        args.config.read_text(encoding="utf-8")
    )

    v2_features = config.get("v2", {}).get("features")

    if not isinstance(v2_features, list) or not v2_features:
        raise ValueError(
            "V2 feature list was not found in config."
        )

    profile_id = detect_column(
        profiles,
        ID_CANDIDATES,
        "profile player ID",
    )
    train_id = detect_column(
        v1_train,
        ID_CANDIDATES,
        "V1 train player ID",
    )
    test_id = detect_column(
        v1_test,
        ID_CANDIDATES,
        "V1 test player ID",
    )

    train_target = detect_column(
        v1_train,
        TARGET_CANDIDATES,
        "V1 train target",
    )
    test_target = detect_column(
        v1_test,
        TARGET_CANDIDATES,
        "V1 test target",
    )

    if train_target != test_target:
        raise ValueError(
            "V1 train and test use different target columns: "
            f"{train_target!r} vs {test_target!r}"
        )

    target_column = train_target

    require_columns(
        profiles,
        [profile_id, *v2_features],
        "V2 profiles",
    )

    profiles = profiles.copy()
    profiles[profile_id] = profiles[profile_id].astype(str)

    v1_train = v1_train.copy()
    v1_test = v1_test.copy()

    v1_train[train_id] = v1_train[train_id].astype(str)
    v1_test[test_id] = v1_test[test_id].astype(str)

    if profiles[profile_id].duplicated().any():
        raise ValueError(
            "Duplicate player IDs in V2 profile dataset."
        )

    train_ids = set(v1_train[train_id])
    test_ids = set(v1_test[test_id])

    overlap = split_overlap(
        train_ids,
        test_ids,
    )
    if overlap:
        raise ValueError(
            "V1 train/test overlap detected for "
            f"{len(overlap)} players."
        )

    profile_lookup = profiles.set_index(
        profile_id,
        drop=False,
    )

    missing_train_ids = sorted(
        train_ids - set(profile_lookup.index)
    )
    missing_test_ids = sorted(
        test_ids - set(profile_lookup.index)
    )

    if missing_train_ids or missing_test_ids:
        raise ValueError(
            "Some V1 split players are missing from V2 profiles. "
            f"train_missing={len(missing_train_ids)}, "
            f"test_missing={len(missing_test_ids)}"
        )

    # Preserve the exact row order and labels from V1.
    train_rows = profile_lookup.loc[
        v1_train[train_id].tolist()
    ].reset_index(drop=True)

    test_rows = profile_lookup.loc[
        v1_test[test_id].tolist()
    ].reset_index(drop=True)

    # Copy the historical labels from V1 split files rather than
    # redetecting/rebuilding targets.
    train_rows[target_column] = (
        v1_train[target_column]
        .astype(str)
        .reset_index(drop=True)
    )
    test_rows[target_column] = (
        v1_test[target_column]
        .astype(str)
        .reset_index(drop=True)
    )

    output_columns = [
        profile_id,
        *v2_features,
        target_column,
    ]

    train_out = train_rows[
        output_columns
    ].copy()

    test_out = test_rows[
        output_columns
    ].copy()

    if len(train_out) != len(v1_train):
        raise ValueError(
            "V2 train row count differs from V1."
        )

    if len(test_out) != len(v1_test):
        raise ValueError(
            "V2 test row count differs from V1."
        )

    # Strong parity checks.
    train_membership_identical = (
        train_out[profile_id].tolist()
        == v1_train[train_id].tolist()
    )
    test_membership_identical = (
        test_out[profile_id].tolist()
        == v1_test[test_id].tolist()
    )

    train_targets_identical = (
        train_out[target_column].tolist()
        == v1_train[target_column].astype(str).tolist()
    )
    test_targets_identical = (
        test_out[target_column].tolist()
        == v1_test[target_column].astype(str).tolist()
    )

    if not all(
        [
            train_membership_identical,
            test_membership_identical,
            train_targets_identical,
            test_targets_identical,
        ]
    ):
        raise ValueError(
            "V1/V2 split parity validation failed."
        )

    # Check model feature values are finite/numeric.
    for feature in v2_features:
        train_numeric = pd.to_numeric(
            train_out[feature],
            errors="coerce",
        )
        test_numeric = pd.to_numeric(
            test_out[feature],
            errors="coerce",
        )

        if train_numeric.isna().any():
            raise ValueError(
                f"Train feature contains invalid values: {feature}"
            )

        if test_numeric.isna().any():
            raise ValueError(
                f"Test feature contains invalid values: {feature}"
            )

        train_out[feature] = train_numeric
        test_out[feature] = test_numeric

    args.train_output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    train_out.to_csv(
        args.train_output,
        index=False,
    )
    test_out.to_csv(
        args.test_output,
        index=False,
    )

    summary = {
        "stage": "09d_recency_ml_dataset_preparation",
        "comparison_policy": {
            "split_strategy": (
                "reuse_exact_v1_macro_train_test_membership"
            ),
            "new_random_split": False,
            "balancing_applied": False,
            "reason": (
                "V2 uses exactly the same players, labels and "
                "train/test membership as V1 so performance "
                "differences can be attributed to the recency "
                "feature set rather than split variation."
            ),
        },
        "columns": {
            "player_id": profile_id,
            "target": target_column,
            "feature_count": len(v2_features),
        },
        "train": {
            "rows": int(len(train_out)),
            "class_counts": class_counts(
                train_out,
                target_column,
            ),
            "membership_identical_to_v1": (
                train_membership_identical
            ),
            "targets_identical_to_v1": (
                train_targets_identical
            ),
        },
        "test": {
            "rows": int(len(test_out)),
            "class_counts": class_counts(
                test_out,
                target_column,
            ),
            "membership_identical_to_v1": (
                test_membership_identical
            ),
            "targets_identical_to_v1": (
                test_targets_identical
            ),
        },
        "split_overlap_count": len(overlap),
        "outputs": {
            "train": str(args.train_output),
            "test": str(args.test_output),
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

    print("\n=== RECENCY V2 ML DATASET PREPARATION ===")
    print(f"Feature count: {len(v2_features)}")
    print(f"Train rows: {len(train_out)}")
    print(f"Test rows: {len(test_out)}")
    print(
        "Train membership identical to V1: "
        f"{train_membership_identical}"
    )
    print(
        "Test membership identical to V1: "
        f"{test_membership_identical}"
    )
    print(
        "Train targets identical to V1: "
        f"{train_targets_identical}"
    )
    print(
        "Test targets identical to V1: "
        f"{test_targets_identical}"
    )
    print(f"Train/test overlap: {len(overlap)}")

    print("\nTrain classes:")
    for label, count in class_counts(
        train_out,
        target_column,
    ).items():
        print(f"  {label}: {count}")

    print("\nTest classes:")
    for label, count in class_counts(
        test_out,
        target_column,
    ).items():
        print(f"  {label}: {count}")

    print(f"\nSaved train: {args.train_output}")
    print(f"Saved test: {args.test_output}")
    print(f"Saved summary: {args.summary}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())