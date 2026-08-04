"""Analyze source-game influence on final player targets.

This stage examines:
- transition matrix between inclusive and source-excluded macro targets;
- changes between resolved and unresolved states;
- changes by sampling source and source group;
- extreme cases where the source game represents a large playtime share;
- profiles where removing the source game removes all observed playtime.

It produces evidence for choosing the modeling profile variant.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]

DEFAULT_INPUT = (
    ROOT / "data/processed/final_player_profile_source_effect.csv"
)
DEFAULT_DETAIL = (
    ROOT / "data/processed/final_player_source_effect_flagged.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/06_source_game_effect_analysis.json"
)

LOGGER = logging.getLogger("source-game-effect-analysis")

DETAIL_FIELDS = [
    "final_player_id",
    "final_sampling_source",
    "source_group",
    "source_appid",
    "source_game",
    "inclusive_macro_target",
    "excluded_macro_target",
    "macro_target_changed",
    "inclusive_macro_resolved",
    "excluded_macro_resolved",
    "removed_game_rows",
    "removed_playtime_minutes",
    "removed_playtime_share",
    "effect_flag",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Analyze source-game influence on final targets."
    )
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--detail-output", type=Path, default=DEFAULT_DETAIL)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument(
        "--high-share-threshold",
        type=float,
        default=0.50,
    )
    parser.add_argument(
        "--moderate-share-threshold",
        type=float,
        default=0.20,
    )
    args = parser.parse_args()

    if not 0 <= args.moderate_share_threshold <= 1:
        parser.error("--moderate-share-threshold must be in [0, 1].")
    if not 0 <= args.high_share_threshold <= 1:
        parser.error("--high-share-threshold must be in [0, 1].")
    if args.moderate_share_threshold > args.high_share_threshold:
        parser.error(
            "Moderate threshold cannot exceed high threshold."
        )

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


def percentile(values: list[float], proportion: float) -> float:
    if not values:
        return 0.0

    ordered = sorted(values)
    position = (len(ordered) - 1) * proportion
    lower = int(position)
    upper = min(lower + 1, len(ordered) - 1)
    fraction = position - lower

    return ordered[lower] + (
        ordered[upper] - ordered[lower]
    ) * fraction


def distribution(values: list[float]) -> dict[str, float]:
    return {
        "minimum": round(min(values), 6) if values else 0.0,
        "p25": round(percentile(values, 0.25), 6),
        "median": round(statistics.median(values), 6)
        if values
        else 0.0,
        "mean": round(statistics.mean(values), 6)
        if values
        else 0.0,
        "p75": round(percentile(values, 0.75), 6),
        "p90": round(percentile(values, 0.90), 6),
        "p95": round(percentile(values, 0.95), 6),
        "maximum": round(max(values), 6) if values else 0.0,
    }


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        fields, rows = read_csv(args.input)

        required = {
            "final_player_id",
            "final_sampling_source",
            "source_group",
            "inclusive_macro_target",
            "excluded_macro_target",
            "macro_target_changed",
            "inclusive_macro_resolved",
            "excluded_macro_resolved",
            "removed_game_rows",
            "removed_playtime_minutes",
            "removed_playtime_share",
        }
        missing = required - set(fields)
        if missing:
            raise ValueError(
                f"Source effect CSV is missing fields: {sorted(missing)}"
            )

        if len(rows) != 1000:
            raise ValueError(
                f"Expected 1000 profile comparisons, found {len(rows)}."
            )

        transitions = Counter()
        resolved_transitions = Counter()
        changed_by_source = Counter()
        changed_by_group = Counter()
        changed_by_transition = Counter()
        flags = Counter()
        flagged_rows: list[dict[str, Any]] = []

        shares = [
            safe_float(row.get("removed_playtime_share"))
            for row in rows
        ]

        for row in rows:
            inclusive = norm(row.get("inclusive_macro_target"))
            excluded = norm(row.get("excluded_macro_target"))
            changed = truthy(row.get("macro_target_changed"))
            inclusive_resolved = truthy(
                row.get("inclusive_macro_resolved")
            )
            excluded_resolved = truthy(
                row.get("excluded_macro_resolved")
            )
            share = safe_float(row.get("removed_playtime_share"))

            transition = f"{inclusive} -> {excluded}"
            transitions[transition] += 1

            resolved_transition = (
                f"{'resolved' if inclusive_resolved else 'unresolved'}"
                f" -> "
                f"{'resolved' if excluded_resolved else 'unresolved'}"
            )
            resolved_transitions[resolved_transition] += 1

            if changed:
                changed_by_source[
                    norm(row.get("final_sampling_source"))
                ] += 1
                changed_by_group[norm(row.get("source_group"))] += 1
                changed_by_transition[transition] += 1

            if share >= 0.999999:
                flag = "all_playtime_removed"
            elif share >= args.high_share_threshold:
                flag = "high_source_share"
            elif share >= args.moderate_share_threshold:
                flag = "moderate_source_share"
            elif changed:
                flag = "target_changed_low_source_share"
            else:
                flag = ""

            if flag:
                flags[flag] += 1
                flagged_rows.append(
                    {
                        **row,
                        "effect_flag": flag,
                    }
                )

        flagged_rows.sort(
            key=lambda row: (
                -safe_float(row.get("removed_playtime_share")),
                norm(row.get("final_player_id")),
            )
        )

        changed_rows = [
            row for row in rows if truthy(row.get("macro_target_changed"))
        ]
        changed_shares = [
            safe_float(row.get("removed_playtime_share"))
            for row in changed_rows
        ]

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_analyze_source_game_effect",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "moderate_share_threshold": (
                    args.moderate_share_threshold
                ),
                "high_share_threshold": args.high_share_threshold,
            },
            "profiles": len(rows),
            "macro_target_changes": len(changed_rows),
            "macro_target_change_rate": round(
                len(changed_rows) / len(rows),
                6,
            ),
            "transition_matrix": dict(sorted(transitions.items())),
            "changed_transition_matrix": dict(
                sorted(changed_by_transition.items())
            ),
            "resolution_transition_matrix": dict(
                sorted(resolved_transitions.items())
            ),
            "changes_by_sampling_source": dict(
                sorted(changed_by_source.items())
            ),
            "changes_by_source_group": dict(
                sorted(changed_by_group.items())
            ),
            "removed_playtime_share_distribution": distribution(shares),
            "changed_profiles_removed_share_distribution": distribution(
                changed_shares
            ),
            "flag_counts": dict(sorted(flags.items())),
            "profiles_with_all_playtime_removed": sum(
                share >= 0.999999 for share in shares
            ),
            "profiles_with_at_least_50_percent_removed": sum(
                share >= args.high_share_threshold for share in shares
            ),
            "profiles_with_at_least_20_percent_removed": sum(
                share >= args.moderate_share_threshold for share in shares
            ),
            "recommended_modeling_variant": "source_excluded",
            "recommendation_rationale": (
                "The source-excluded variant is methodologically safer "
                "because it prevents the game used to discover a profile "
                "from directly contributing to its target. Inclusive "
                "profiles should remain as a sensitivity-analysis baseline."
            ),
            "outputs": {
                "flagged_profiles": str(args.detail_output),
            },
        }

        write_csv(args.detail_output, DETAIL_FIELDS, flagged_rows)
        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception(
            "Source-game effect analysis failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Source effect analyzed: profiles=%d, changes=%d, flagged=%d.",
        len(rows),
        len(changed_rows),
        len(flagged_rows),
    )
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())