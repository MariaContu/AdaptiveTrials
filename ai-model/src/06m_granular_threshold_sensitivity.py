"""Run sensitivity analysis for granular target thresholds.

This stage recalculates combat, exploration and strategic subgroup targets from
the source-excluded profile shares under multiple threshold combinations.

Purpose:
- distinguish true class scarcity from overly strict resolution thresholds;
- measure how coverage, dominance and gap thresholds affect class support;
- identify the least-relaxed defensible configuration, if one exists;
- avoid merging classes or collecting more data without evidence.

No modeling, train/test split or resampling is performed here.
"""

from __future__ import annotations

import argparse
import csv
import itertools
import json
import logging
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_source_excluded.csv"
)
DEFAULT_OUTPUT = (
    ROOT / "data/processed/final_granular_threshold_sensitivity.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/06_granular_threshold_sensitivity_summary.json"
)

LOGGER = logging.getLogger("granular-threshold-sensitivity")

TASKS = {
    "combat": {
        "macro_target": "combat",
        "coverage_field": "combat_subgroup_playtime_coverage",
        "classes": ("shooter", "melee", "action_other"),
    },
    "exploration": {
        "macro_target": "exploration",
        "coverage_field": "exploration_subgroup_playtime_coverage",
        "classes": (
            "open_world",
            "narrative_exploration",
            "investigation",
        ),
    },
    "strategic_reasoning": {
        "macro_target": "strategic_reasoning",
        "coverage_field": (
            "strategic_reasoning_subgroup_playtime_coverage"
        ),
        "classes": (
            "logic_puzzle",
            "planning_management",
            "strategy_decision",
            "observation_deduction",
        ),
    },
}

OUTPUT_FIELDS = [
    "task",
    "minimum_coverage",
    "minimum_share",
    "minimum_gap",
    "macro_profiles",
    "resolved_profiles",
    "unresolved_profiles",
    "resolved_rate",
    "minimum_class_count",
    "maximum_class_count",
    "imbalance_ratio",
    "classes_below_minimum_support",
    "viable_for_multiclass_modeling",
    "class_counts_json",
    "unresolved_reason_counts_json",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_float_list(value: str) -> list[float]:
    result: list[float] = []

    for raw in value.split(","):
        number = float(raw.strip())
        if not 0 <= number <= 1:
            raise argparse.ArgumentTypeError(
                "Threshold values must be in [0, 1]."
            )
        result.append(number)

    if not result:
        raise argparse.ArgumentTypeError(
            "At least one threshold is required."
        )

    return sorted(set(result), reverse=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run granular target threshold sensitivity."
    )
    parser.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--summary-output",
        type=Path,
        default=DEFAULT_SUMMARY,
    )
    parser.add_argument(
        "--coverage-thresholds",
        type=parse_float_list,
        default=parse_float_list("0.60,0.50,0.40"),
    )
    parser.add_argument(
        "--share-thresholds",
        type=parse_float_list,
        default=parse_float_list("0.40,0.35,0.30"),
    )
    parser.add_argument(
        "--gap-thresholds",
        type=parse_float_list,
        default=parse_float_list("0.10,0.075,0.05"),
    )
    parser.add_argument(
        "--minimum-class-support",
        type=int,
        default=20,
    )

    args = parser.parse_args()

    if args.minimum_class_support <= 0:
        parser.error("--minimum-class-support must be positive.")

    return args


def norm(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_float(value: Any) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


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
    rows: list[dict[str, Any]],
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


def resolve_target(
    shares: dict[str, float],
    coverage: float,
    minimum_coverage: float,
    minimum_share: float,
    minimum_gap: float,
) -> tuple[str, str]:
    ordered = sorted(
        shares.items(),
        key=lambda item: (-item[1], item[0]),
    )

    top_name, top_share = ordered[0]
    second_share = ordered[1][1] if len(ordered) > 1 else 0.0
    gap = top_share - second_share

    if coverage < minimum_coverage:
        return "unresolved", "insufficient_coverage"
    if top_share < minimum_share:
        return "unresolved", "insufficient_dominance"
    if gap < minimum_gap:
        return "unresolved", "insufficient_gap"

    return top_name, "resolved"


def evaluate_configuration(
    rows: list[dict[str, str]],
    task: str,
    coverage_threshold: float,
    share_threshold: float,
    gap_threshold: float,
    minimum_support: int,
) -> dict[str, Any]:
    config = TASKS[task]
    task_rows = [
        row
        for row in rows
        if truthy(row.get("macro_target_resolved"))
        and norm(row.get("macro_target")) == config["macro_target"]
    ]

    class_counts = Counter()
    reason_counts = Counter()

    for row in task_rows:
        coverage = safe_float(row.get(config["coverage_field"]))

        shares = {
            class_name: safe_float(
                row.get(
                    f"share_{class_name}_within_{config['macro_target']}"
                )
            )
            for class_name in config["classes"]
        }

        target, reason = resolve_target(
            shares=shares,
            coverage=coverage,
            minimum_coverage=coverage_threshold,
            minimum_share=share_threshold,
            minimum_gap=gap_threshold,
        )

        if target == "unresolved":
            reason_counts[reason] += 1
        else:
            class_counts[target] += 1

    counts = {
        class_name: class_counts.get(class_name, 0)
        for class_name in config["classes"]
    }
    resolved = sum(counts.values())
    unresolved = len(task_rows) - resolved
    minimum_count = min(counts.values()) if counts else 0
    maximum_count = max(counts.values()) if counts else 0
    insufficient = [
        class_name
        for class_name, count in counts.items()
        if count < minimum_support
    ]
    imbalance_ratio = (
        maximum_count / minimum_count
        if minimum_count > 0
        else None
    )

    return {
        "task": task,
        "minimum_coverage": coverage_threshold,
        "minimum_share": share_threshold,
        "minimum_gap": gap_threshold,
        "macro_profiles": len(task_rows),
        "resolved_profiles": resolved,
        "unresolved_profiles": unresolved,
        "resolved_rate": round(
            resolved / len(task_rows),
            6,
        )
        if task_rows
        else 0.0,
        "minimum_class_count": minimum_count,
        "maximum_class_count": maximum_count,
        "imbalance_ratio": (
            round(imbalance_ratio, 6)
            if imbalance_ratio is not None
            else ""
        ),
        "classes_below_minimum_support": ";".join(insufficient),
        "viable_for_multiclass_modeling": not insufficient,
        "class_counts_json": json.dumps(
            counts,
            ensure_ascii=False,
            sort_keys=True,
        ),
        "unresolved_reason_counts_json": json.dumps(
            dict(sorted(reason_counts.items())),
            ensure_ascii=False,
            sort_keys=True,
        ),
    }


def strictness_key(row: dict[str, Any]) -> tuple[Any, ...]:
    return (
        -safe_float(row["minimum_coverage"]),
        -safe_float(row["minimum_share"]),
        -safe_float(row["minimum_gap"]),
        -int(row["minimum_class_count"]),
        -int(row["resolved_profiles"]),
    )


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        fields, profiles = read_csv(args.profiles)

        required = {
            "macro_target",
            "macro_target_resolved",
        }

        for config in TASKS.values():
            required.add(config["coverage_field"])

            for class_name in config["classes"]:
                required.add(
                    f"share_{class_name}_within_{config['macro_target']}"
                )

        missing = required - set(fields)

        if missing:
            raise ValueError(
                f"Profiles CSV is missing fields: {sorted(missing)}"
            )

        results: list[dict[str, Any]] = []

        combinations = list(
            itertools.product(
                args.coverage_thresholds,
                args.share_thresholds,
                args.gap_thresholds,
            )
        )

        for task in TASKS:
            for coverage, share, gap in combinations:
                results.append(
                    evaluate_configuration(
                        rows=profiles,
                        task=task,
                        coverage_threshold=coverage,
                        share_threshold=share,
                        gap_threshold=gap,
                        minimum_support=args.minimum_class_support,
                    )
                )

        results.sort(
            key=lambda row: (
                row["task"],
                -safe_float(row["minimum_coverage"]),
                -safe_float(row["minimum_share"]),
                -safe_float(row["minimum_gap"]),
            )
        )

        recommendations: dict[str, Any] = {}

        for task in TASKS:
            task_results = [
                row for row in results if row["task"] == task
            ]
            viable = [
                row
                for row in task_results
                if row["viable_for_multiclass_modeling"]
            ]

            least_relaxed = (
                sorted(viable, key=strictness_key)[0]
                if viable
                else None
            )

            best_minimum_support = max(
                task_results,
                key=lambda row: (
                    int(row["minimum_class_count"]),
                    int(row["resolved_profiles"]),
                    safe_float(row["minimum_coverage"]),
                    safe_float(row["minimum_share"]),
                    safe_float(row["minimum_gap"]),
                ),
            )

            recommendations[task] = {
                "viable_configuration_found": bool(viable),
                "least_relaxed_viable_configuration": (
                    least_relaxed
                ),
                "best_observed_minimum_class_support": (
                    best_minimum_support
                ),
            }

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": (
                "06_run_granular_threshold_sensitivity"
            ),
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "coverage_thresholds": args.coverage_thresholds,
                "share_thresholds": args.share_thresholds,
                "gap_thresholds": args.gap_thresholds,
                "minimum_class_support": args.minimum_class_support,
                "configurations_per_task": len(combinations),
            },
            "recommendations": recommendations,
            "methodological_note": (
                "A granular task should only adopt a relaxed threshold "
                "configuration if every class reaches minimum support without "
                "excessive loss of target certainty. If no tested configuration "
                "is viable, the limitation reflects class scarcity rather than "
                "the original threshold alone."
            ),
        }

        write_csv(args.output, OUTPUT_FIELDS, results)
        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception(
            "Granular threshold sensitivity failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Sensitivity completed: tasks=%d, configurations=%d.",
        len(TASKS),
        len(results),
    )
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())