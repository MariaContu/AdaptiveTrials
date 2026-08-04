"""Prepare leakage-controlled ML datasets for Adaptive Trials.

The supervised targets are derived from categorized playtime. Therefore,
category-specific playtime, hours, shares, entropy, dominance and gap cannot be
used as predictive inputs without making the evaluation tautological.

This script creates reproducible stratified train/test splits using only
leakage-controlled count and coverage features. Resampling is not applied here.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from sklearn.model_selection import train_test_split


ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_source_excluded.csv"
)
DEFAULT_MACRO_TRAIN = ROOT / "data/modeling/macro_train.csv"
DEFAULT_MACRO_TEST = ROOT / "data/modeling/macro_test.csv"
DEFAULT_EXPLORATION_TRAIN = (
    ROOT / "data/modeling/exploration_granular_train.csv"
)
DEFAULT_EXPLORATION_TEST = (
    ROOT / "data/modeling/exploration_granular_test.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/07_ml_dataset_preparation_summary.json"
)
DEFAULT_FEATURES = ROOT / "config/model_features_v1.json"

LOGGER = logging.getLogger("ml-dataset-preparation")

MACRO_TARGET = "macro_target"
EXPLORATION_TARGET = "exploration_subgroup_target"

MACRO_CLASSES = ("combat", "exploration", "strategic_reasoning")
EXPLORATION_CLASSES = (
    "open_world",
    "narrative_exploration",
    "investigation",
)

# Global library features do not reveal a specific target class.
GLOBAL_FEATURES = [
    "total_library_games",
    "total_played_games",
    "total_playtime_minutes",
    "categorized_games",
    "categorized_played_games",
    "category_game_coverage",
    "category_playtime_coverage",
    "avg_playtime_per_played_game_minutes",
]

# Category and subgroup game counts are retained because they describe library
# composition without directly reproducing the playtime-based target rule.
MACRO_COUNT_FEATURES = [
    "games_combat",
    "played_games_combat",
    "games_exploration",
    "played_games_exploration",
    "games_strategic_reasoning",
    "played_games_strategic_reasoning",
]

SUBGROUP_COUNT_FEATURES = [
    "games_shooter",
    "played_games_shooter",
    "games_melee",
    "played_games_melee",
    "games_action_other",
    "played_games_action_other",
    "games_open_world",
    "played_games_open_world",
    "games_narrative_exploration",
    "played_games_narrative_exploration",
    "games_investigation",
    "played_games_investigation",
    "games_logic_puzzle",
    "played_games_logic_puzzle",
    "games_planning_management",
    "played_games_planning_management",
    "games_strategy_decision",
    "played_games_strategy_decision",
    "games_observation_deduction",
    "played_games_observation_deduction",
]

FEATURE_FIELDS = GLOBAL_FEATURES + MACRO_COUNT_FEATURES + SUBGROUP_COUNT_FEATURES

# Explicitly excluded because the target is defined from categorized playtime.
LEAKAGE_EXCLUDED_FIELDS = [
    "categorized_playtime_minutes",
    "categorized_playtime_hours",
    "playtime_combat_minutes",
    "hours_combat",
    "share_combat",
    "playtime_exploration_minutes",
    "hours_exploration",
    "share_exploration",
    "playtime_strategic_reasoning_minutes",
    "hours_strategic_reasoning",
    "share_strategic_reasoning",
    "diversity",
    "entropy",
    "normalized_entropy",
    "dominance",
    "second_max",
    "gap",
    "combat_subgroup_playtime_coverage",
    "exploration_subgroup_playtime_coverage",
    "strategic_reasoning_subgroup_playtime_coverage",
]

for subgroup in (
    "shooter",
    "melee",
    "action_other",
    "open_world",
    "narrative_exploration",
    "investigation",
    "logic_puzzle",
    "planning_management",
    "strategy_decision",
    "observation_deduction",
):
    LEAKAGE_EXCLUDED_FIELDS.extend(
        [
            f"playtime_{subgroup}_minutes",
            f"hours_{subgroup}",
        ]
    )


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Prepare leakage-controlled train/test datasets."
    )
    parser.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    parser.add_argument("--macro-train", type=Path, default=DEFAULT_MACRO_TRAIN)
    parser.add_argument("--macro-test", type=Path, default=DEFAULT_MACRO_TEST)
    parser.add_argument(
        "--exploration-train",
        type=Path,
        default=DEFAULT_EXPLORATION_TRAIN,
    )
    parser.add_argument(
        "--exploration-test",
        type=Path,
        default=DEFAULT_EXPLORATION_TEST,
    )
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--features-output", type=Path, default=DEFAULT_FEATURES)
    parser.add_argument("--test-size", type=float, default=0.20)
    parser.add_argument("--random-state", type=int, default=42)
    args = parser.parse_args()

    if not 0 < args.test_size < 1:
        parser.error("--test-size must be in (0, 1).")

    return args


def norm(value: Any) -> str:
    return "" if value is None else str(value).strip()


def truthy(value: Any) -> bool:
    return norm(value).casefold() in {"true", "1", "yes"}


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None:
            raise ValueError(f"CSV has no header: {path}")
        return list(reader.fieldnames), [dict(row) for row in reader]


def write_csv(
    path: Path,
    fieldnames: list[str],
    rows: list[dict[str, str]],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")

    with temporary.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    temporary.replace(path)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")

    with temporary.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temporary.replace(path)


def validate_features(fields: list[str]) -> None:
    missing = set(FEATURE_FIELDS) - set(fields)
    if missing:
        raise ValueError(
            f"Profiles CSV is missing configured features: {sorted(missing)}"
        )


def build_records(
    rows: list[dict[str, str]],
    target_field: str,
    allowed_targets: tuple[str, ...],
    eligibility,
) -> list[dict[str, str]]:
    records: list[dict[str, str]] = []

    for row in rows:
        if not eligibility(row):
            continue

        target = norm(row.get(target_field))
        if target not in allowed_targets:
            continue

        record = {
            "final_player_id": norm(row.get("final_player_id")),
            **{
                feature: norm(row.get(feature)) or "0"
                for feature in FEATURE_FIELDS
            },
            target_field: target,
        }
        records.append(record)

    return records


def split_records(
    records: list[dict[str, str]],
    target_field: str,
    test_size: float,
    random_state: int,
) -> tuple[list[dict[str, str]], list[dict[str, str]]]:
    targets = [row[target_field] for row in records]

    train_rows, test_rows = train_test_split(
        records,
        test_size=test_size,
        random_state=random_state,
        stratify=targets,
    )

    train_rows.sort(key=lambda row: row["final_player_id"])
    test_rows.sort(key=lambda row: row["final_player_id"])
    return train_rows, test_rows


def validate_no_overlap(
    train_rows: list[dict[str, str]],
    test_rows: list[dict[str, str]],
) -> None:
    train_ids = {row["final_player_id"] for row in train_rows}
    test_ids = {row["final_player_id"] for row in test_rows}

    overlap = train_ids & test_ids
    if overlap:
        raise ValueError(
            f"Train/test player overlap detected: {len(overlap)}"
        )


def class_counts(
    rows: list[dict[str, str]],
    target_field: str,
) -> dict[str, int]:
    return dict(
        sorted(Counter(row[target_field] for row in rows).items())
    )


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        fields, profiles = read_csv(args.profiles)
        validate_features(fields)

        macro_records = build_records(
            profiles,
            MACRO_TARGET,
            MACRO_CLASSES,
            lambda row: truthy(row.get("macro_target_resolved")),
        )

        exploration_records = build_records(
            profiles,
            EXPLORATION_TARGET,
            EXPLORATION_CLASSES,
            lambda row: (
                truthy(row.get("macro_target_resolved"))
                and norm(row.get("macro_target")) == "exploration"
                and truthy(
                    row.get("exploration_subgroup_target_resolved")
                )
            ),
        )

        macro_train, macro_test = split_records(
            macro_records,
            MACRO_TARGET,
            args.test_size,
            args.random_state,
        )
        exploration_train, exploration_test = split_records(
            exploration_records,
            EXPLORATION_TARGET,
            args.test_size,
            args.random_state,
        )

        validate_no_overlap(macro_train, macro_test)
        validate_no_overlap(exploration_train, exploration_test)

        macro_fields = ["final_player_id"] + FEATURE_FIELDS + [MACRO_TARGET]
        exploration_fields = (
            ["final_player_id"] + FEATURE_FIELDS + [EXPLORATION_TARGET]
        )

        write_csv(args.macro_train, macro_fields, macro_train)
        write_csv(args.macro_test, macro_fields, macro_test)
        write_csv(
            args.exploration_train,
            exploration_fields,
            exploration_train,
        )
        write_csv(
            args.exploration_test,
            exploration_fields,
            exploration_test,
        )

        feature_config = {
            "version": "1.1",
            "profile_variant": "source_excluded",
            "feature_strategy": "leakage_controlled_counts",
            "feature_count": len(FEATURE_FIELDS),
            "feature_fields": FEATURE_FIELDS,
            "leakage_excluded_fields": sorted(
                set(LEAKAGE_EXCLUDED_FIELDS)
            ),
            "targets": {
                "macro": MACRO_TARGET,
                "exploration_granular": EXPLORATION_TARGET,
            },
            "methodological_note": (
                "Targets are derived from categorized playtime. Therefore, "
                "category-specific playtime, hours, shares and distribution "
                "statistics are excluded. Category and subgroup game counts "
                "are retained because they describe library composition "
                "without reproducing the exact target rule."
            ),
        }
        write_json(args.features_output, feature_config)

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "07_prepare_ml_datasets",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "test_size": args.test_size,
                "random_state": args.random_state,
                "stratified_split": True,
                "resampling_applied": False,
                "feature_strategy": "leakage_controlled_counts",
            },
            "feature_count": len(FEATURE_FIELDS),
            "feature_fields": FEATURE_FIELDS,
            "leakage_excluded_fields": sorted(
                set(LEAKAGE_EXCLUDED_FIELDS)
            ),
            "macro": {
                "total_profiles": len(macro_records),
                "train_profiles": len(macro_train),
                "test_profiles": len(macro_test),
                "total_class_counts": class_counts(
                    macro_records,
                    MACRO_TARGET,
                ),
                "train_class_counts": class_counts(
                    macro_train,
                    MACRO_TARGET,
                ),
                "test_class_counts": class_counts(
                    macro_test,
                    MACRO_TARGET,
                ),
            },
            "exploration_granular": {
                "total_profiles": len(exploration_records),
                "train_profiles": len(exploration_train),
                "test_profiles": len(exploration_test),
                "total_class_counts": class_counts(
                    exploration_records,
                    EXPLORATION_TARGET,
                ),
                "train_class_counts": class_counts(
                    exploration_train,
                    EXPLORATION_TARGET,
                ),
                "test_class_counts": class_counts(
                    exploration_test,
                    EXPLORATION_TARGET,
                ),
            },
            "outputs": {
                "macro_train": str(args.macro_train),
                "macro_test": str(args.macro_test),
                "exploration_train": str(args.exploration_train),
                "exploration_test": str(args.exploration_test),
                "feature_config": str(args.features_output),
            },
            "methodological_note": (
                "The split is performed before resampling. SMOTE, "
                "undersampling and scaling must be fitted using training data "
                "only, inside the model-evaluation pipeline."
            ),
        }
        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception("ML dataset preparation failed: %s", error)
        return 1

    LOGGER.info(
        "Leakage-controlled datasets prepared: macro=%d, exploration=%d.",
        len(macro_records),
        len(exploration_records),
    )
    LOGGER.info("Features retained: %d.", len(FEATURE_FIELDS))
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())