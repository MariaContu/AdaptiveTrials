"""Measure metadata coverage for each of the 1,000 final player profiles.

This stage complements aggregate coverage by calculating, per player:
- library rows covered by metadata;
- positive-playtime rows covered;
- total playtime covered;
- coverage bands and low-coverage flags;
- distributions by sampling source and source group.

No profiles are removed automatically. The results support a documented
decision about whether additional metadata collection is required.
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


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_GAMES = (
    PROJECT_ROOT / "data" / "interim" / "final_player_games_1000.csv"
)
DEFAULT_METADATA = (
    PROJECT_ROOT / "data" / "raw" / "game_metadata_raw.csv"
)
DEFAULT_PLAYER_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "final_player_metadata_coverage.csv"
)
DEFAULT_SUMMARY_OUTPUT = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "06_player_metadata_coverage_summary.json"
)

LOGGER = logging.getLogger("player-metadata-coverage")

OUTPUT_FIELDS = [
    "final_player_id",
    "final_sampling_source",
    "source_group",
    "library_rows",
    "covered_library_rows",
    "positive_playtime_rows",
    "covered_positive_playtime_rows",
    "total_playtime_minutes",
    "covered_playtime_minutes",
    "row_coverage",
    "positive_row_coverage",
    "playtime_coverage",
    "coverage_band",
    "below_50_percent",
    "below_70_percent",
    "below_80_percent",
    "below_90_percent",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Measure metadata coverage per final player."
    )
    parser.add_argument("--games", type=Path, default=DEFAULT_GAMES)
    parser.add_argument("--metadata", type=Path, default=DEFAULT_METADATA)
    parser.add_argument(
        "--player-output",
        type=Path,
        default=DEFAULT_PLAYER_OUTPUT,
    )
    parser.add_argument(
        "--summary-output",
        type=Path,
        default=DEFAULT_SUMMARY_OUTPUT,
    )
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

    raise ValueError(
        f"Could not identify metadata AppID column: {fields}"
    )


def coverage_band(value: float) -> str:
    if value < 0.50:
        return "below_50"
    if value < 0.70:
        return "50_to_69"
    if value < 0.80:
        return "70_to_79"
    if value < 0.90:
        return "80_to_89"
    return "90_or_more"


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


def summarize_values(values: list[float]) -> dict[str, float]:
    return {
        "minimum": round(min(values), 6) if values else 0.0,
        "p10": round(percentile(values, 0.10), 6),
        "p25": round(percentile(values, 0.25), 6),
        "median": round(statistics.median(values), 6)
        if values
        else 0.0,
        "mean": round(statistics.mean(values), 6)
        if values
        else 0.0,
        "p75": round(percentile(values, 0.75), 6),
        "p90": round(percentile(values, 0.90), 6),
        "maximum": round(max(values), 6) if values else 0.0,
    }


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        game_fields, game_rows = read_csv(args.games)
        metadata_fields, metadata_rows = read_csv(args.metadata)

        required = {
            "final_player_id",
            "appid",
            "playtime_forever_minutes",
            "final_sampling_source",
            "source_group",
        }
        missing = required - set(game_fields)
        if missing:
            raise ValueError(
                f"Final games CSV is missing fields: {sorted(missing)}"
            )

        appid_column = find_appid_column(metadata_fields)
        covered_appids = {
            safe_int(row.get(appid_column))
            for row in metadata_rows
            if safe_int(row.get(appid_column)) > 0
        }

        rows_by_player: dict[str, list[dict[str, str]]] = defaultdict(list)

        for row in game_rows:
            player_id = normalize(row.get("final_player_id"))
            if player_id:
                rows_by_player[player_id].append(row)

        if len(rows_by_player) != 1000:
            raise ValueError(
                f"Expected 1000 players, found {len(rows_by_player)}."
            )

        player_rows: list[dict[str, Any]] = []

        for player_id, rows in rows_by_player.items():
            source = normalize(rows[0].get("final_sampling_source"))
            source_group = normalize(rows[0].get("source_group"))

            library_rows = len(rows)
            covered_rows = sum(
                1
                for row in rows
                if safe_int(row.get("appid")) in covered_appids
            )

            positive_rows = [
                row
                for row in rows
                if safe_int(row.get("playtime_forever_minutes")) > 0
            ]
            covered_positive_rows = sum(
                1
                for row in positive_rows
                if safe_int(row.get("appid")) in covered_appids
            )

            total_playtime = sum(
                safe_int(row.get("playtime_forever_minutes"))
                for row in rows
            )
            covered_playtime = sum(
                safe_int(row.get("playtime_forever_minutes"))
                for row in rows
                if safe_int(row.get("appid")) in covered_appids
            )

            row_coverage = (
                covered_rows / library_rows if library_rows else 0.0
            )
            positive_row_coverage = (
                covered_positive_rows / len(positive_rows)
                if positive_rows
                else 0.0
            )
            playtime_coverage = (
                covered_playtime / total_playtime
                if total_playtime
                else 0.0
            )

            player_rows.append(
                {
                    "final_player_id": player_id,
                    "final_sampling_source": source,
                    "source_group": source_group,
                    "library_rows": library_rows,
                    "covered_library_rows": covered_rows,
                    "positive_playtime_rows": len(positive_rows),
                    "covered_positive_playtime_rows": (
                        covered_positive_rows
                    ),
                    "total_playtime_minutes": total_playtime,
                    "covered_playtime_minutes": covered_playtime,
                    "row_coverage": round(row_coverage, 6),
                    "positive_row_coverage": round(
                        positive_row_coverage,
                        6,
                    ),
                    "playtime_coverage": round(
                        playtime_coverage,
                        6,
                    ),
                    "coverage_band": coverage_band(
                        playtime_coverage
                    ),
                    "below_50_percent": playtime_coverage < 0.50,
                    "below_70_percent": playtime_coverage < 0.70,
                    "below_80_percent": playtime_coverage < 0.80,
                    "below_90_percent": playtime_coverage < 0.90,
                }
            )

        player_rows.sort(
            key=lambda row: (
                row["playtime_coverage"],
                row["final_player_id"],
            )
        )

        playtime_values = [
            float(row["playtime_coverage"]) for row in player_rows
        ]
        row_values = [
            float(row["row_coverage"]) for row in player_rows
        ]
        positive_row_values = [
            float(row["positive_row_coverage"])
            for row in player_rows
        ]

        bands = Counter(
            str(row["coverage_band"]) for row in player_rows
        )
        by_source: dict[str, dict[str, Any]] = {}

        for source in sorted(
            {
                str(row["final_sampling_source"])
                for row in player_rows
            }
        ):
            source_rows = [
                row
                for row in player_rows
                if row["final_sampling_source"] == source
            ]
            source_values = [
                float(row["playtime_coverage"])
                for row in source_rows
            ]
            by_source[source] = {
                "players": len(source_rows),
                "playtime_coverage": summarize_values(
                    source_values
                ),
                "below_50_percent": sum(
                    bool(row["below_50_percent"])
                    for row in source_rows
                ),
                "below_70_percent": sum(
                    bool(row["below_70_percent"])
                    for row in source_rows
                ),
                "below_80_percent": sum(
                    bool(row["below_80_percent"])
                    for row in source_rows
                ),
                "below_90_percent": sum(
                    bool(row["below_90_percent"])
                    for row in source_rows
                ),
            }

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_measure_player_metadata_coverage",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "players": len(player_rows),
            "coverage_distributions": {
                "playtime": summarize_values(playtime_values),
                "library_rows": summarize_values(row_values),
                "positive_playtime_rows": summarize_values(
                    positive_row_values
                ),
            },
            "playtime_coverage_bands": dict(sorted(bands.items())),
            "players_below_threshold": {
                "50_percent": sum(
                    bool(row["below_50_percent"])
                    for row in player_rows
                ),
                "70_percent": sum(
                    bool(row["below_70_percent"])
                    for row in player_rows
                ),
                "80_percent": sum(
                    bool(row["below_80_percent"])
                    for row in player_rows
                ),
                "90_percent": sum(
                    bool(row["below_90_percent"])
                    for row in player_rows
                ),
            },
            "by_sampling_source": by_source,
            "lowest_20_players": [
                {
                    "final_player_id": row["final_player_id"],
                    "sampling_source": row[
                        "final_sampling_source"
                    ],
                    "source_group": row["source_group"],
                    "playtime_coverage": row[
                        "playtime_coverage"
                    ],
                    "total_playtime_minutes": row[
                        "total_playtime_minutes"
                    ],
                }
                for row in player_rows[:20]
            ],
            "methodological_note": (
                "Aggregate playtime coverage may hide profiles with low "
                "individual coverage. No player is removed automatically; "
                "threshold decisions must be justified after inspecting "
                "this distribution."
            ),
        }

        write_csv(args.player_output, OUTPUT_FIELDS, player_rows)
        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception(
            "Per-player metadata coverage failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Per-player coverage completed: players=%d, median=%.2f%%.",
        len(player_rows),
        summary["coverage_distributions"]["playtime"]["median"]
        * 100,
    )
    LOGGER.info("Summary output: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())