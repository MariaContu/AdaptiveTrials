"""Consolidate the 1,000 final profiles and measure metadata coverage."""

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

DEFAULT_GENERAL_STATUS = PROJECT_ROOT / "data" / "interim" / "final_general_player_library_status_selected.csv"
DEFAULT_STRATEGIC_STATUS = PROJECT_ROOT / "data" / "interim" / "final_strategic_player_library_status_selected.csv"
DEFAULT_GENERAL_GAMES = PROJECT_ROOT / "data" / "raw" / "final_general_player_games_selected.csv"
DEFAULT_STRATEGIC_GAMES = PROJECT_ROOT / "data" / "raw" / "final_strategic_player_games_selected.csv"
DEFAULT_METADATA = PROJECT_ROOT / "data" / "raw" / "game_metadata_raw.csv"

DEFAULT_STATUS_OUTPUT = PROJECT_ROOT / "data" / "interim" / "final_player_library_status_1000.csv"
DEFAULT_GAMES_OUTPUT = PROJECT_ROOT / "data" / "interim" / "final_player_games_1000.csv"
DEFAULT_MISSING_OUTPUT = PROJECT_ROOT / "data" / "interim" / "final_missing_metadata_appids.csv"
DEFAULT_SUMMARY_OUTPUT = PROJECT_ROOT / "reports" / "metrics" / "06_final_metadata_coverage_summary.json"

LOGGER = logging.getLogger("final-metadata-coverage")

MISSING_FIELDS = [
    "priority_rank",
    "appid",
    "player_game_rows",
    "players_affected",
    "total_playtime_minutes",
    "total_playtime_hours",
    "share_of_missing_playtime",
    "cumulative_missing_playtime_share",
]


def configure_logging() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Consolidate final selected profiles and measure metadata coverage.")
    parser.add_argument("--general-status", type=Path, default=DEFAULT_GENERAL_STATUS)
    parser.add_argument("--strategic-status", type=Path, default=DEFAULT_STRATEGIC_STATUS)
    parser.add_argument("--general-games", type=Path, default=DEFAULT_GENERAL_GAMES)
    parser.add_argument("--strategic-games", type=Path, default=DEFAULT_STRATEGIC_GAMES)
    parser.add_argument("--metadata", type=Path, default=DEFAULT_METADATA)
    parser.add_argument("--status-output", type=Path, default=DEFAULT_STATUS_OUTPUT)
    parser.add_argument("--games-output", type=Path, default=DEFAULT_GAMES_OUTPUT)
    parser.add_argument("--missing-output", type=Path, default=DEFAULT_MISSING_OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY_OUTPUT)
    return parser.parse_args()


def normalize(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None:
            raise ValueError(f"CSV has no header: {path}")
        rows = [dict(row) for row in reader]
    return list(reader.fieldnames), rows


def write_csv(path: Path, fieldnames: list[str], rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)
    temp.replace(path)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)
    temp.replace(path)


def find_appid_column(fieldnames: list[str]) -> str:
    lookup = {field.casefold(): field for field in fieldnames}
    for candidate in ("appid", "app_id", "steam_appid"):
        if candidate in lookup:
            return lookup[candidate]
    raise ValueError(f"Could not find AppID column. Available columns: {fieldnames}")


def validate_status_rows(rows: list[dict[str, str]], expected_count: int, source: str) -> None:
    if len(rows) != expected_count:
        raise ValueError(f"{source} status count is {len(rows)}, expected {expected_count}.")
    final_ids = [normalize(row.get("final_player_id")) for row in rows]
    if any(not value for value in final_ids):
        raise ValueError(f"{source} contains empty final_player_id values.")
    if len(final_ids) != len(set(final_ids)):
        raise ValueError(f"{source} contains duplicate final_player_id values.")
    if {normalize(row.get("status")) for row in rows} != {"valid"}:
        raise ValueError(f"{source} selected rows are not all valid.")


def prepare_status_rows(general_rows: list[dict[str, str]], strategic_rows: list[dict[str, str]]) -> list[dict[str, str]]:
    combined = [{**row, "final_sampling_source": "general"} for row in general_rows]
    combined += [{**row, "final_sampling_source": "strategic"} for row in strategic_rows]
    final_ids = [normalize(row.get("final_player_id")) for row in combined]
    steamids = [normalize(row.get("source_steamid")) for row in combined]
    if len(final_ids) != 1000 or len(set(final_ids)) != 1000:
        raise ValueError("Combined final_player_id validation failed.")
    if len(set(steamids)) != 1000:
        raise ValueError("Combined selected SteamIDs are not unique.")
    combined.sort(key=lambda row: normalize(row.get("final_player_id")))
    return combined


def prepare_game_rows(
    general_rows: list[dict[str, str]],
    strategic_rows: list[dict[str, str]],
    valid_final_ids: set[str],
) -> list[dict[str, str]]:
    combined = [{**row, "final_sampling_source": "general"} for row in general_rows]
    combined += [{**row, "final_sampling_source": "strategic"} for row in strategic_rows]
    players_in_games = {normalize(row.get("final_player_id")) for row in combined}
    if players_in_games != valid_final_ids:
        raise ValueError(
            "Player IDs in game rows do not match selected profiles. "
            f"Missing={len(valid_final_ids - players_in_games)}, "
            f"unexpected={len(players_in_games - valid_final_ids)}."
        )
    combined.sort(key=lambda row: (normalize(row.get("final_player_id")), safe_int(row.get("appid"))))
    return combined


def metadata_appids(path: Path) -> tuple[set[int], int]:
    fields, rows = read_csv(path)
    appid_column = find_appid_column(fields)
    appids = {safe_int(row.get(appid_column)) for row in rows if safe_int(row.get(appid_column)) > 0}
    return appids, len(rows)


def build_missing_rows(game_rows: list[dict[str, str]], missing_appids: set[int]) -> list[dict[str, Any]]:
    rows_by_appid: Counter[int] = Counter()
    players_by_appid: dict[int, set[str]] = defaultdict(set)
    playtime_by_appid: Counter[int] = Counter()

    for row in game_rows:
        appid = safe_int(row.get("appid"))
        if appid not in missing_appids:
            continue
        rows_by_appid[appid] += 1
        players_by_appid[appid].add(normalize(row.get("final_player_id")))
        playtime_by_appid[appid] += safe_int(row.get("playtime_forever_minutes"))

    total_missing_playtime = sum(playtime_by_appid.values())
    ordered = sorted(
        missing_appids,
        key=lambda appid: (
            -playtime_by_appid[appid],
            -len(players_by_appid[appid]),
            -rows_by_appid[appid],
            appid,
        ),
    )

    result: list[dict[str, Any]] = []
    cumulative = 0
    for rank, appid in enumerate(ordered, start=1):
        playtime = playtime_by_appid[appid]
        cumulative += playtime
        result.append(
            {
                "priority_rank": rank,
                "appid": appid,
                "player_game_rows": rows_by_appid[appid],
                "players_affected": len(players_by_appid[appid]),
                "total_playtime_minutes": playtime,
                "total_playtime_hours": round(playtime / 60, 3),
                "share_of_missing_playtime": round(playtime / total_missing_playtime, 8)
                if total_missing_playtime
                else 0.0,
                "cumulative_missing_playtime_share": round(cumulative / total_missing_playtime, 8)
                if total_missing_playtime
                else 0.0,
            }
        )
    return result


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        general_status_fields, general_status = read_csv(args.general_status)
        strategic_status_fields, strategic_status = read_csv(args.strategic_status)
        general_game_fields, general_games = read_csv(args.general_games)
        strategic_game_fields, strategic_games = read_csv(args.strategic_games)

        validate_status_rows(general_status, 700, "general")
        validate_status_rows(strategic_status, 300, "strategic")

        status_rows = prepare_status_rows(general_status, strategic_status)
        final_ids = {normalize(row.get("final_player_id")) for row in status_rows}
        game_rows = prepare_game_rows(general_games, strategic_games, final_ids)

        cached_appids, metadata_rows = metadata_appids(args.metadata)
        final_appids = {safe_int(row.get("appid")) for row in game_rows if safe_int(row.get("appid")) > 0}
        missing_appids = final_appids - cached_appids
        covered_appids = final_appids & cached_appids

        total_rows = len(game_rows)
        covered_rows = sum(1 for row in game_rows if safe_int(row.get("appid")) in cached_appids)

        total_playtime = sum(safe_int(row.get("playtime_forever_minutes")) for row in game_rows)
        covered_playtime = sum(
            safe_int(row.get("playtime_forever_minutes"))
            for row in game_rows
            if safe_int(row.get("appid")) in cached_appids
        )

        missing_rows = build_missing_rows(game_rows, missing_appids)

        status_fields = list(dict.fromkeys(general_status_fields + strategic_status_fields + ["final_sampling_source"]))
        game_fields = list(dict.fromkeys(general_game_fields + strategic_game_fields + ["final_sampling_source"]))

        write_csv(args.status_output, status_fields, status_rows)
        write_csv(args.games_output, game_fields, game_rows)
        write_csv(args.missing_output, MISSING_FIELDS, missing_rows)

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_consolidate_final_profiles_and_measure_metadata_coverage",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round((finished_at - started_at).total_seconds(), 3),
            "final_profiles": 1000,
            "profiles_by_sampling_source": {"general": 700, "strategic": 300},
            "player_game_rows": total_rows,
            "total_playtime_minutes": total_playtime,
            "total_playtime_hours": round(total_playtime / 60, 3),
            "metadata_cache_rows": metadata_rows,
            "metadata_cache_unique_appids": len(cached_appids),
            "final_unique_appids": len(final_appids),
            "covered_unique_appids": len(covered_appids),
            "missing_unique_appids": len(missing_appids),
            "coverage": {
                "unique_appid_rate": round(len(covered_appids) / len(final_appids), 6) if final_appids else 0.0,
                "player_game_row_rate": round(covered_rows / total_rows, 6) if total_rows else 0.0,
                "playtime_rate": round(covered_playtime / total_playtime, 6) if total_playtime else 0.0,
            },
            "covered_player_game_rows": covered_rows,
            "missing_player_game_rows": total_rows - covered_rows,
            "covered_playtime_minutes": covered_playtime,
            "missing_playtime_minutes": total_playtime - covered_playtime,
            "missing_metadata_priority": {
                "ranked_appids": len(missing_rows),
                "top_10": missing_rows[:10],
                "appids_needed_for_90_percent_missing_playtime": next(
                    (row["priority_rank"] for row in missing_rows if row["cumulative_missing_playtime_share"] >= 0.90),
                    len(missing_rows),
                ),
                "appids_needed_for_95_percent_missing_playtime": next(
                    (row["priority_rank"] for row in missing_rows if row["cumulative_missing_playtime_share"] >= 0.95),
                    len(missing_rows),
                ),
            },
            "outputs": {
                "consolidated_status": str(args.status_output),
                "consolidated_games": str(args.games_output),
                "missing_appids_ranked": str(args.missing_output),
            },
            "methodological_note": (
                "Metadata coverage is measured by unique AppID, player-game relationships and "
                "accumulated playtime. Missing metadata should be collected in playtime-priority "
                "order instead of treating every missing AppID as equally important."
            ),
        }

        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception("Final consolidation and metadata coverage failed: %s", error)
        return 1

    LOGGER.info("Consolidation completed: profiles=1000, game_rows=%d.", total_rows)
    LOGGER.info(
        "Metadata coverage: appids=%.2f%%, rows=%.2f%%, playtime=%.2f%%.",
        summary["coverage"]["unique_appid_rate"] * 100,
        summary["coverage"]["player_game_row_rate"] * 100,
        summary["coverage"]["playtime_rate"] * 100,
    )
    LOGGER.info("Summary output: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())