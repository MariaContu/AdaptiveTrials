"""Plan a targeted metadata expansion for the final 1,000 profiles.

The plan combines two priorities:
1. rescue players with individual playtime coverage below a threshold;
2. improve aggregate playtime coverage up to a target.

Missing AppIDs are ranked by the amount of uncovered playtime they recover,
with extra weight for low-coverage players. This stage only creates the plan;
it does not call Steam Store or SteamSpy.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_GAMES = (
    PROJECT_ROOT / "data" / "interim" / "final_player_games_1000.csv"
)
DEFAULT_COVERAGE = (
    PROJECT_ROOT / "data" / "interim" / "final_player_metadata_coverage.csv"
)
DEFAULT_METADATA = (
    PROJECT_ROOT / "data" / "raw" / "game_metadata_raw.csv"
)
DEFAULT_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "final_metadata_collection_plan.csv"
)
DEFAULT_SUMMARY = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "06_metadata_collection_plan_summary.json"
)

LOGGER = logging.getLogger("metadata-collection-plan")

OUTPUT_FIELDS = [
    "priority_rank",
    "appid",
    "selection_reason",
    "weighted_priority_score",
    "total_missing_playtime_minutes",
    "total_missing_playtime_hours",
    "players_affected",
    "low_coverage_players_affected",
    "below_50_players_affected",
    "below_70_players_affected",
    "aggregate_playtime_share_recovered",
    "cumulative_aggregate_playtime_coverage_estimate",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Create a targeted final metadata collection plan."
    )
    parser.add_argument("--games", type=Path, default=DEFAULT_GAMES)
    parser.add_argument("--coverage", type=Path, default=DEFAULT_COVERAGE)
    parser.add_argument("--metadata", type=Path, default=DEFAULT_METADATA)
    parser.add_argument(
        "--individual-target",
        type=float,
        default=0.70,
        help="Coverage threshold used to prioritize low-coverage players.",
    )
    parser.add_argument(
        "--aggregate-target",
        type=float,
        default=0.96,
        help="Estimated aggregate playtime coverage target.",
    )
    parser.add_argument(
        "--maximum-appids",
        type=int,
        default=1500,
        help="Safety cap for the planned metadata requests.",
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)

    args = parser.parse_args()

    if not 0 < args.individual_target <= 1:
        parser.error("--individual-target must be in (0, 1].")
    if not 0 < args.aggregate_target <= 1:
        parser.error("--aggregate-target must be in (0, 1].")
    if args.maximum_appids <= 0:
        parser.error("--maximum-appids must be positive.")

    return args


def normalize(value: Any) -> str:
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
    temp = path.with_suffix(path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    temp.replace(path)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temp.replace(path)


def find_appid_column(fields: list[str]) -> str:
    lookup = {field.casefold(): field for field in fields}
    for candidate in ("appid", "app_id", "steam_appid"):
        if candidate in lookup:
            return lookup[candidate]
    raise ValueError(f"Could not identify AppID column: {fields}")


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        game_fields, game_rows = read_csv(args.games)
        coverage_fields, coverage_rows = read_csv(args.coverage)
        metadata_fields, metadata_rows = read_csv(args.metadata)

        required_games = {
            "final_player_id",
            "appid",
            "playtime_forever_minutes",
        }
        required_coverage = {
            "final_player_id",
            "playtime_coverage",
            "total_playtime_minutes",
        }

        missing_games = required_games - set(game_fields)
        missing_coverage = required_coverage - set(coverage_fields)

        if missing_games:
            raise ValueError(
                f"Games CSV is missing fields: {sorted(missing_games)}"
            )
        if missing_coverage:
            raise ValueError(
                f"Coverage CSV is missing fields: {sorted(missing_coverage)}"
            )

        metadata_appid_column = find_appid_column(metadata_fields)
        cached_appids = {
            safe_int(row.get(metadata_appid_column))
            for row in metadata_rows
            if safe_int(row.get(metadata_appid_column)) > 0
        }

        coverage_by_player = {
            normalize(row.get("final_player_id")): safe_float(
                row.get("playtime_coverage")
            )
            for row in coverage_rows
        }
        total_playtime_by_player = {
            normalize(row.get("final_player_id")): safe_int(
                row.get("total_playtime_minutes")
            )
            for row in coverage_rows
        }

        total_playtime = sum(
            safe_int(row.get("playtime_forever_minutes"))
            for row in game_rows
        )
        currently_covered_playtime = sum(
            safe_int(row.get("playtime_forever_minutes"))
            for row in game_rows
            if safe_int(row.get("appid")) in cached_appids
        )

        playtime_by_appid: Counter[int] = Counter()
        players_by_appid: dict[int, set[str]] = defaultdict(set)
        low_players_by_appid: dict[int, set[str]] = defaultdict(set)
        below_50_by_appid: dict[int, set[str]] = defaultdict(set)
        below_70_by_appid: dict[int, set[str]] = defaultdict(set)
        weighted_score_by_appid: Counter[int] = Counter()

        for row in game_rows:
            appid = safe_int(row.get("appid"))
            if appid <= 0 or appid in cached_appids:
                continue

            player_id = normalize(row.get("final_player_id"))
            playtime = safe_int(row.get("playtime_forever_minutes"))
            coverage = coverage_by_player.get(player_id, 0.0)

            playtime_by_appid[appid] += playtime
            players_by_appid[appid].add(player_id)

            weight = 1.0
            if coverage < args.individual_target:
                low_players_by_appid[appid].add(player_id)
                weight += 2.0
            if coverage < 0.70:
                below_70_by_appid[appid].add(player_id)
                weight += 1.5
            if coverage < 0.50:
                below_50_by_appid[appid].add(player_id)
                weight += 3.0

            if total_playtime_by_player.get(player_id, 0) > 0:
                player_share = (
                    playtime
                    / total_playtime_by_player[player_id]
                )
            else:
                player_share = 0.0

            weighted_score_by_appid[appid] += int(
                playtime * weight * (1.0 + player_share)
            )

        ordered_appids = sorted(
            playtime_by_appid,
            key=lambda appid: (
                -len(below_50_by_appid[appid]),
                -len(below_70_by_appid[appid]),
                -weighted_score_by_appid[appid],
                -playtime_by_appid[appid],
                appid,
            ),
        )

        selected: list[int] = []
        estimated_covered_playtime = currently_covered_playtime

        for appid in ordered_appids:
            current_aggregate = (
                estimated_covered_playtime / total_playtime
                if total_playtime
                else 0.0
            )

            rescues_low_players = bool(
                low_players_by_appid[appid]
            )
            aggregate_still_needed = (
                current_aggregate < args.aggregate_target
            )

            if not rescues_low_players and not aggregate_still_needed:
                continue

            selected.append(appid)
            estimated_covered_playtime += playtime_by_appid[appid]

            if len(selected) >= args.maximum_appids:
                break

        output_rows: list[dict[str, Any]] = []
        cumulative_playtime = currently_covered_playtime

        for rank, appid in enumerate(selected, start=1):
            cumulative_playtime += playtime_by_appid[appid]

            reasons: list[str] = []
            if below_50_by_appid[appid]:
                reasons.append("below_50_rescue")
            if below_70_by_appid[appid]:
                reasons.append("below_70_rescue")
            if low_players_by_appid[appid]:
                reasons.append("individual_target")
            if (
                cumulative_playtime / total_playtime
                <= args.aggregate_target
            ):
                reasons.append("aggregate_target")
            if not reasons:
                reasons.append("marginal_completion")

            output_rows.append(
                {
                    "priority_rank": rank,
                    "appid": appid,
                    "selection_reason": ";".join(reasons),
                    "weighted_priority_score": (
                        weighted_score_by_appid[appid]
                    ),
                    "total_missing_playtime_minutes": (
                        playtime_by_appid[appid]
                    ),
                    "total_missing_playtime_hours": round(
                        playtime_by_appid[appid] / 60,
                        3,
                    ),
                    "players_affected": len(
                        players_by_appid[appid]
                    ),
                    "low_coverage_players_affected": len(
                        low_players_by_appid[appid]
                    ),
                    "below_50_players_affected": len(
                        below_50_by_appid[appid]
                    ),
                    "below_70_players_affected": len(
                        below_70_by_appid[appid]
                    ),
                    "aggregate_playtime_share_recovered": round(
                        playtime_by_appid[appid] / total_playtime,
                        8,
                    )
                    if total_playtime
                    else 0.0,
                    "cumulative_aggregate_playtime_coverage_estimate": round(
                        cumulative_playtime / total_playtime,
                        8,
                    )
                    if total_playtime
                    else 0.0,
                }
            )

        selected_set = set(selected)
        low_players = {
            player_id
            for player_id, coverage in coverage_by_player.items()
            if coverage < args.individual_target
        }
        below_50_players = {
            player_id
            for player_id, coverage in coverage_by_player.items()
            if coverage < 0.50
        }
        below_70_players = {
            player_id
            for player_id, coverage in coverage_by_player.items()
            if coverage < 0.70
        }

        affected_low_players = {
            player_id
            for appid in selected_set
            for player_id in low_players_by_appid[appid]
        }

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_plan_targeted_metadata_expansion",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "individual_target": args.individual_target,
                "aggregate_target": args.aggregate_target,
                "maximum_appids": args.maximum_appids,
            },
            "current_aggregate_playtime_coverage": round(
                currently_covered_playtime / total_playtime,
                6,
            )
            if total_playtime
            else 0.0,
            "planned_appids": len(selected),
            "estimated_aggregate_playtime_coverage_after_plan": round(
                estimated_covered_playtime / total_playtime,
                6,
            )
            if total_playtime
            else 0.0,
            "low_coverage_players": {
                "below_individual_target": len(low_players),
                "below_70_percent": len(below_70_players),
                "below_50_percent": len(below_50_players),
                "affected_by_at_least_one_planned_appid": len(
                    affected_low_players
                ),
            },
            "planned_appids_by_reason": dict(
                sorted(
                    Counter(
                        reason
                        for row in output_rows
                        for reason in str(
                            row["selection_reason"]
                        ).split(";")
                    ).items()
                )
            ),
            "top_20": output_rows[:20],
            "methodological_note": (
                "The plan prioritizes missing metadata that improves "
                "coverage for low-coverage players and then raises "
                "aggregate playtime coverage. The ranking does not treat "
                "all missing AppIDs as equally relevant."
            ),
        }

        write_csv(args.output, OUTPUT_FIELDS, output_rows)
        write_json(args.summary, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception(
            "Targeted metadata plan failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Metadata plan completed: appids=%d, estimated coverage=%.2f%%.",
        len(selected),
        summary[
            "estimated_aggregate_playtime_coverage_after_plan"
        ]
        * 100,
    )
    LOGGER.info("Summary output: %s", args.summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())