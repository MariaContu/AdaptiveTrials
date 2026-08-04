"""Analyze final macro and granular modeling eligibility.

Uses the source-excluded profiles as the primary methodological dataset and:
- measures resolved macro class distribution;
- identifies profiles eligible for macro modeling;
- evaluates each granular task only inside its corresponding macro category;
- measures subgroup class balance and unresolved reasons;
- flags classes with insufficient support;
- exports audit-ready eligible profile lists for the next ML preparation stage.

No train/test split or resampling is performed here.
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


ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_source_excluded.csv"
)
DEFAULT_EFFECT = (
    ROOT / "data/processed/final_player_profile_source_effect.csv"
)

DEFAULT_MACRO_OUTPUT = (
    ROOT / "data/processed/final_macro_modeling_candidates.csv"
)
DEFAULT_COMBAT_OUTPUT = (
    ROOT / "data/processed/final_combat_modeling_candidates.csv"
)
DEFAULT_EXPLORATION_OUTPUT = (
    ROOT / "data/processed/final_exploration_modeling_candidates.csv"
)
DEFAULT_STRATEGIC_OUTPUT = (
    ROOT / "data/processed/final_strategic_modeling_candidates.csv"
)
DEFAULT_EXCLUDED_OUTPUT = (
    ROOT / "data/processed/final_modeling_exclusions.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/06_final_modeling_eligibility_summary.json"
)

LOGGER = logging.getLogger("final-modeling-eligibility")

MACRO_CLASSES = ("combat", "exploration", "strategic_reasoning")

TASKS = {
    "combat": {
        "target_field": "combat_subgroup_target",
        "resolved_field": "combat_subgroup_target_resolved",
        "reason_field": "combat_subgroup_target_reason",
        "classes": ("shooter", "melee", "action_other"),
        "output": DEFAULT_COMBAT_OUTPUT,
    },
    "exploration": {
        "target_field": "exploration_subgroup_target",
        "resolved_field": "exploration_subgroup_target_resolved",
        "reason_field": "exploration_subgroup_target_reason",
        "classes": (
            "open_world",
            "narrative_exploration",
            "investigation",
        ),
        "output": DEFAULT_EXPLORATION_OUTPUT,
    },
    "strategic_reasoning": {
        "target_field": "strategic_reasoning_subgroup_target",
        "resolved_field": "strategic_reasoning_subgroup_target_resolved",
        "reason_field": "strategic_reasoning_subgroup_target_reason",
        "classes": (
            "logic_puzzle",
            "planning_management",
            "strategy_decision",
            "observation_deduction",
        ),
        "output": DEFAULT_STRATEGIC_OUTPUT,
    },
}

AUDIT_FIELDS = [
    "final_player_id",
    "final_sampling_source",
    "source_group",
    "source_appid",
    "source_game",
]

MACRO_OUTPUT_FIELDS = AUDIT_FIELDS + [
    "macro_target",
    "category_playtime_coverage",
    "dominance",
    "second_max",
    "gap",
    "total_playtime_minutes",
    "total_played_games",
    "source_game_all_playtime_removed",
]

GRANULAR_OUTPUT_FIELDS = AUDIT_FIELDS + [
    "macro_target",
    "granular_target",
    "granular_task",
    "granular_playtime_coverage",
    "total_category_playtime_minutes",
    "source_game_all_playtime_removed",
]

EXCLUSION_FIELDS = AUDIT_FIELDS + [
    "modeling_task",
    "exclusion_reason",
    "macro_target",
    "granular_target",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Analyze eligibility for final macro and granular models."
    )
    parser.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    parser.add_argument("--effect", type=Path, default=DEFAULT_EFFECT)
    parser.add_argument(
        "--macro-output",
        type=Path,
        default=DEFAULT_MACRO_OUTPUT,
    )
    parser.add_argument(
        "--combat-output",
        type=Path,
        default=DEFAULT_COMBAT_OUTPUT,
    )
    parser.add_argument(
        "--exploration-output",
        type=Path,
        default=DEFAULT_EXPLORATION_OUTPUT,
    )
    parser.add_argument(
        "--strategic-output",
        type=Path,
        default=DEFAULT_STRATEGIC_OUTPUT,
    )
    parser.add_argument(
        "--excluded-output",
        type=Path,
        default=DEFAULT_EXCLUDED_OUTPUT,
    )
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument(
        "--minimum-class-support",
        type=int,
        default=20,
        help="Minimum resolved profiles required for a class to be viable.",
    )

    args = parser.parse_args()

    if args.minimum_class_support <= 0:
        parser.error("--minimum-class-support must be positive.")

    return args


def norm(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


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
        writer = csv.DictWriter(
            file,
            fieldnames=fieldnames,
            extrasaction="ignore",
        )
        writer.writeheader()
        writer.writerows(rows)

    temporary.replace(path)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")

    with temporary.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temporary.replace(path)


def audit_values(row: dict[str, str]) -> dict[str, str]:
    return {
        field: norm(row.get(field))
        for field in AUDIT_FIELDS
    }


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        fields, profiles = read_csv(args.profiles)
        effect_fields, effects = read_csv(args.effect)

        required_profile_fields = {
            "final_player_id",
            "macro_target",
            "macro_target_resolved",
            "macro_target_reason",
            "category_playtime_coverage",
            "dominance",
            "second_max",
            "gap",
            "total_playtime_minutes",
            "total_played_games",
        }

        for task, config in TASKS.items():
            required_profile_fields.update(
                {
                    config["target_field"],
                    config["resolved_field"],
                    config["reason_field"],
                    f"{task}_subgroup_playtime_coverage",
                    f"playtime_{task}_minutes",
                }
            )

        missing = required_profile_fields - set(fields)
        if missing:
            raise ValueError(
                f"Profiles CSV is missing fields: {sorted(missing)}"
            )

        if "final_player_id" not in effect_fields:
            raise ValueError("Effect CSV is missing final_player_id.")

        all_removed_by_player = {
            norm(row.get("final_player_id")): (
                safe_float(row.get("removed_playtime_share")) >= 0.999999
            )
            for row in effects
        }

        if len(profiles) != 1000:
            raise ValueError(
                f"Expected 1000 profiles, found {len(profiles)}."
            )

        macro_rows: list[dict[str, Any]] = []
        granular_rows: dict[str, list[dict[str, Any]]] = {
            task: [] for task in TASKS
        }
        exclusions: list[dict[str, Any]] = []

        macro_reason_counts = Counter()
        macro_class_counts = Counter()

        for row in profiles:
            player_id = norm(row.get("final_player_id"))
            macro_target = norm(row.get("macro_target"))
            macro_resolved = truthy(row.get("macro_target_resolved"))
            all_removed = all_removed_by_player.get(player_id, False)

            if macro_resolved and macro_target in MACRO_CLASSES:
                macro_class_counts[macro_target] += 1
                macro_rows.append(
                    {
                        **audit_values(row),
                        "macro_target": macro_target,
                        "category_playtime_coverage": safe_float(
                            row.get("category_playtime_coverage")
                        ),
                        "dominance": safe_float(row.get("dominance")),
                        "second_max": safe_float(row.get("second_max")),
                        "gap": safe_float(row.get("gap")),
                        "total_playtime_minutes": safe_int(
                            row.get("total_playtime_minutes")
                        ),
                        "total_played_games": safe_int(
                            row.get("total_played_games")
                        ),
                        "source_game_all_playtime_removed": all_removed,
                    }
                )
            else:
                reason = norm(row.get("macro_target_reason"))
                macro_reason_counts[reason] += 1
                exclusions.append(
                    {
                        **audit_values(row),
                        "modeling_task": "macro",
                        "exclusion_reason": reason or "unresolved",
                        "macro_target": macro_target,
                        "granular_target": "",
                    }
                )

            for task, config in TASKS.items():
                if macro_target != task or not macro_resolved:
                    continue

                granular_target = norm(row.get(config["target_field"]))
                granular_resolved = truthy(
                    row.get(config["resolved_field"])
                )
                granular_reason = norm(row.get(config["reason_field"]))

                if (
                    granular_resolved
                    and granular_target in config["classes"]
                ):
                    granular_rows[task].append(
                        {
                            **audit_values(row),
                            "macro_target": macro_target,
                            "granular_target": granular_target,
                            "granular_task": task,
                            "granular_playtime_coverage": safe_float(
                                row.get(
                                    f"{task}_subgroup_playtime_coverage"
                                )
                            ),
                            "total_category_playtime_minutes": safe_int(
                                row.get(f"playtime_{task}_minutes")
                            ),
                            "source_game_all_playtime_removed": (
                                all_removed
                            ),
                        }
                    )
                else:
                    exclusions.append(
                        {
                            **audit_values(row),
                            "modeling_task": task,
                            "exclusion_reason": (
                                granular_reason or "unresolved"
                            ),
                            "macro_target": macro_target,
                            "granular_target": granular_target,
                        }
                    )

        write_csv(args.macro_output, MACRO_OUTPUT_FIELDS, macro_rows)
        write_csv(
            args.combat_output,
            GRANULAR_OUTPUT_FIELDS,
            granular_rows["combat"],
        )
        write_csv(
            args.exploration_output,
            GRANULAR_OUTPUT_FIELDS,
            granular_rows["exploration"],
        )
        write_csv(
            args.strategic_output,
            GRANULAR_OUTPUT_FIELDS,
            granular_rows["strategic_reasoning"],
        )
        write_csv(
            args.excluded_output,
            EXCLUSION_FIELDS,
            exclusions,
        )

        granular_summary: dict[str, Any] = {}

        for task, config in TASKS.items():
            rows = granular_rows[task]
            class_counts = Counter(
                norm(row.get("granular_target")) for row in rows
            )
            expected_classes = config["classes"]
            support = {
                class_name: class_counts.get(class_name, 0)
                for class_name in expected_classes
            }
            insufficient = [
                class_name
                for class_name, count in support.items()
                if count < args.minimum_class_support
            ]

            task_exclusions = [
                row
                for row in exclusions
                if row["modeling_task"] == task
            ]

            granular_summary[task] = {
                "eligible_profiles": len(rows),
                "class_counts": support,
                "class_percentages": {
                    class_name: round(
                        count / len(rows),
                        6,
                    )
                    if rows
                    else 0.0
                    for class_name, count in support.items()
                },
                "unresolved_profiles_within_macro_class": len(
                    task_exclusions
                ),
                "unresolved_reason_counts": dict(
                    sorted(
                        Counter(
                            row["exclusion_reason"]
                            for row in task_exclusions
                        ).items()
                    )
                ),
                "classes_below_minimum_support": insufficient,
                "minimum_class_support": args.minimum_class_support,
                "viable_for_multiclass_modeling": not insufficient,
            }

        source_counts = Counter(
            norm(row.get("final_sampling_source"))
            for row in macro_rows
        )
        macro_insufficient = [
            class_name
            for class_name in MACRO_CLASSES
            if macro_class_counts.get(class_name, 0)
            < args.minimum_class_support
        ]

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_analyze_final_modeling_eligibility",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "primary_profile_variant": "source_excluded",
                "minimum_class_support": args.minimum_class_support,
            },
            "macro": {
                "eligible_profiles": len(macro_rows),
                "excluded_unresolved_profiles": 1000 - len(macro_rows),
                "class_counts": {
                    class_name: macro_class_counts.get(class_name, 0)
                    for class_name in MACRO_CLASSES
                },
                "class_percentages": {
                    class_name: round(
                        macro_class_counts.get(class_name, 0)
                        / len(macro_rows),
                        6,
                    )
                    if macro_rows
                    else 0.0
                    for class_name in MACRO_CLASSES
                },
                "unresolved_reason_counts": dict(
                    sorted(macro_reason_counts.items())
                ),
                "eligible_by_sampling_source": dict(
                    sorted(source_counts.items())
                ),
                "classes_below_minimum_support": macro_insufficient,
                "viable_for_multiclass_modeling": not macro_insufficient,
                "profiles_with_all_source_playtime_removed": sum(
                    bool(row["source_game_all_playtime_removed"])
                    for row in macro_rows
                ),
            },
            "granular": granular_summary,
            "outputs": {
                "macro_candidates": str(args.macro_output),
                "combat_candidates": str(args.combat_output),
                "exploration_candidates": str(
                    args.exploration_output
                ),
                "strategic_candidates": str(
                    args.strategic_output
                ),
                "exclusions": str(args.excluded_output),
            },
            "methodological_note": (
                "Granular eligibility is assessed only among profiles whose "
                "resolved macro target matches the corresponding category. "
                "This avoids assigning a combat subgroup target to players "
                "whose dominant preference is not combat. No resampling or "
                "train-test split is performed at this stage."
            ),
        }

        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception(
            "Final modeling eligibility analysis failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Eligibility completed: macro=%d, combat=%d, exploration=%d, "
        "strategic=%d.",
        len(macro_rows),
        len(granular_rows["combat"]),
        len(granular_rows["exploration"]),
        len(granular_rows["strategic_reasoning"]),
    )
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())