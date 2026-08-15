"""Fetch Steam libraries for strategic-reasoning candidates.

This wrapper reuses the general player-library collector while preserving
candidate provenance and keeping all outputs separate from the main sample.

Default inputs:
- data/interim/strategic_reasoning_steamids_candidates.csv

Default outputs:
- data/raw/strategic_reasoning_player_games_raw.csv
- data/interim/strategic_reasoning_player_library_status.csv
- reports/metrics/02b_strategic_reasoning_player_games_summary.json
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_CANDIDATES = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "strategic_reasoning_steamids_candidates.csv"
)
DEFAULT_GENERAL_SCRIPT = PROJECT_ROOT / "src" / "02_fetch_player_games.py"
DEFAULT_GAMES_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "raw"
    / "strategic_reasoning_player_games_raw.csv"
)
DEFAULT_STATUS_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "strategic_reasoning_player_library_status.csv"
)
DEFAULT_SUMMARY_OUTPUT = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "02b_strategic_reasoning_player_games_summary.json"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Fetch libraries for strategic-reasoning candidates while "
            "preserving source-game provenance."
        )
    )
    parser.add_argument(
        "--candidates-input",
        type=Path,
        default=DEFAULT_CANDIDATES,
    )
    parser.add_argument(
        "--general-script",
        type=Path,
        default=DEFAULT_GENERAL_SCRIPT,
    )
    parser.add_argument(
        "--games-output",
        type=Path,
        default=DEFAULT_GAMES_OUTPUT,
    )
    parser.add_argument(
        "--status-output",
        type=Path,
        default=DEFAULT_STATUS_OUTPUT,
    )
    parser.add_argument(
        "--summary-output",
        type=Path,
        default=DEFAULT_SUMMARY_OUTPUT,
    )
    parser.add_argument("--request-delay", type=float, default=1.0)
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--target-valid", type=int, default=None)
    parser.add_argument("--max-players", type=int, default=None)
    return parser.parse_args()


def normalize(value: Any) -> str:
    return "" if value is None else str(value).strip()


def read_candidates(path: Path) -> list[dict[str, str]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        required = {
            "candidate_id",
            "steamid",
            "source_appid",
            "source_game",
            "source_subgroup",
        }

        if reader.fieldnames is None or not required.issubset(
            reader.fieldnames
        ):
            raise ValueError(
                f"Candidate CSV must contain: {sorted(required)}"
            )

        candidates = []

        for row in reader:
            candidate_id = normalize(row.get("candidate_id"))
            steamid = normalize(row.get("steamid"))

            if not candidate_id or not steamid:
                continue

            candidates.append(
                {
                    "candidate_id": candidate_id,
                    "steamid": steamid,
                    "source_appid": normalize(row.get("source_appid")),
                    "source_game": normalize(row.get("source_game")),
                    "source_subgroup": normalize(
                        row.get("source_subgroup")
                    ),
                }
            )

    if not candidates:
        raise ValueError("No valid strategic candidates were found.")

    return candidates


def detect_field(
    fieldnames: list[str] | None,
    candidates: tuple[str, ...],
) -> str:
    if not fieldnames:
        raise ValueError("CSV has no header.")

    lookup = {name.casefold(): name for name in fieldnames}

    for candidate in candidates:
        match = lookup.get(candidate.casefold())
        if match:
            return match

    raise ValueError(
        f"Could not identify one of {candidates}. "
        f"Available fields: {fieldnames}"
    )


def write_input_for_general_collector(
    path: Path,
    candidates: list[dict[str, str]],
) -> None:
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=["player_id", "steamid"],
        )
        writer.writeheader()

        for candidate in candidates:
            writer.writerow(
                {
                    "player_id": candidate["candidate_id"],
                    "steamid": candidate["steamid"],
                }
            )


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None:
            raise ValueError(f"CSV has no header: {path}")
        return list(reader.fieldnames), list(reader)


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


def main() -> int:
    args = parse_args()

    try:
        candidates = read_candidates(args.candidates_input)
        provenance_by_steamid = {
            candidate["steamid"]: candidate
            for candidate in candidates
        }

        with tempfile.TemporaryDirectory(
            prefix="strategic_library_collection_"
        ) as temporary_directory:
            temporary_root = Path(temporary_directory)
            temporary_input = temporary_root / "steamids.csv"
            temporary_games = temporary_root / "games.csv"
            temporary_status = temporary_root / "status.csv"
            temporary_summary = temporary_root / "summary.json"

            write_input_for_general_collector(
                temporary_input,
                candidates,
            )

            command = [
                sys.executable,
                str(args.general_script),
                "--input",
                str(temporary_input),
                "--games-output",
                str(temporary_games),
                "--status-output",
                str(temporary_status),
                "--summary-output",
                str(temporary_summary),
                "--request-delay",
                str(args.request_delay),
                "--timeout",
                str(args.timeout),
            ]

            if args.target_valid is not None:
                command.extend(
                    [
                        "--target-valid",
                        str(args.target_valid),
                    ]
                )

            if args.max_players is not None:
                command.extend(
                    [
                        "--max-players",
                        str(args.max_players),
                    ]
                )

            environment = os.environ.copy()
            subprocess.run(
                command,
                check=True,
                cwd=PROJECT_ROOT,
                env=environment,
            )

            status_fields, status_rows = read_csv(temporary_status)
            games_fields, games_rows = read_csv(temporary_games)

            if len(status_rows) > len(candidates):
                raise ValueError(
                    "The collector returned more status rows than candidates."
                )

            provenance_by_player_id = {}

            for index, status_row in enumerate(status_rows):
                player_id = normalize(status_row.get("player_id"))

                if not player_id:
                    raise ValueError(
                        "A status row does not contain player_id."
                    )

                provenance_by_player_id[player_id] = candidates[index]

            provenance_fields = [
                "source_appid",
                "source_game",
                "source_subgroup",
                "collection_stage",
            ]

            final_status_fields = list(status_fields)
            final_games_fields = list(games_fields)

            for field in provenance_fields:
                if field not in final_status_fields:
                    final_status_fields.append(field)
                if field not in final_games_fields:
                    final_games_fields.append(field)

            for row in status_rows:
                player_id = normalize(row.get("player_id"))
                provenance = provenance_by_player_id.get(player_id, {})

                row["source_appid"] = provenance.get(
                    "source_appid",
                    "",
                )
                row["source_game"] = provenance.get(
                    "source_game",
                    "",
                )
                row["source_subgroup"] = provenance.get(
                    "source_subgroup",
                    "",
                )
                row["collection_stage"] = (
                    "02b_strategic_reasoning_library_collection"
                )

            for row in games_rows:
                player_id = normalize(row.get("player_id"))
                provenance = provenance_by_player_id.get(player_id, {})

                row["source_appid"] = provenance.get(
                    "source_appid",
                    "",
                )
                row["source_game"] = provenance.get(
                    "source_game",
                    "",
                )
                row["source_subgroup"] = provenance.get(
                    "source_subgroup",
                    "",
                )
                row["collection_stage"] = (
                    "02b_strategic_reasoning_library_collection"
                )

            write_csv(
                args.status_output,
                final_status_fields,
                status_rows,
            )
            write_csv(
                args.games_output,
                final_games_fields,
                games_rows,
            )

            with temporary_summary.open(
                "r",
                encoding="utf-8",
            ) as file:
                summary = json.load(file)

            status_counts: dict[str, int] = {}
            subgroup_status_counts: dict[str, dict[str, int]] = {}
            source_game_valid_counts: dict[str, int] = {}

            for row in status_rows:
                status = normalize(row.get("status"))
                subgroup = normalize(row.get("source_subgroup"))
                source_game = normalize(row.get("source_game"))

                status_counts[status] = status_counts.get(status, 0) + 1

                subgroup_bucket = subgroup_status_counts.setdefault(
                    subgroup,
                    {},
                )
                subgroup_bucket[status] = (
                    subgroup_bucket.get(status, 0) + 1
                )

                if status == "valid" and source_game:
                    source_game_valid_counts[source_game] = (
                        source_game_valid_counts.get(source_game, 0) + 1
                    )

            attempted = len(status_rows)
            valid = status_counts.get("valid", 0)
            accessible = sum(
                count
                for status, count in status_counts.items()
                if status != "private_or_unavailable"
            )

            summary.update(
                {
                    "pipeline_stage": (
                        "02b_fetch_strategic_reasoning_player_games"
                    ),
                    "directed_candidates_attempted": attempted,
                    "directed_status_counts": dict(
                        sorted(status_counts.items())
                    ),
                    "directed_access_rate": (
                        round(accessible / attempted, 6)
                        if attempted
                        else 0.0
                    ),
                    "directed_valid_yield_rate": (
                        round(valid / attempted, 6)
                        if attempted
                        else 0.0
                    ),
                    "status_by_source_subgroup": {
                        subgroup: dict(sorted(counts.items()))
                        for subgroup, counts in sorted(
                            subgroup_status_counts.items()
                        )
                    },
                    "valid_profiles_by_source_game": dict(
                        sorted(
                            source_game_valid_counts.items(),
                            key=lambda item: (-item[1], item[0]),
                        )
                    ),
                    "methodological_note": (
                        "This directed sample preserves source provenance, "
                        "but final targets must be derived from complete "
                        "player libraries rather than the source game."
                    ),
                }
            )

            write_json(args.summary_output, summary)

    except (
        OSError,
        ValueError,
        subprocess.CalledProcessError,
        json.JSONDecodeError,
    ) as error:
        print(
            f"Strategic library collection failed: {error}",
            file=sys.stderr,
        )
        return 1

    print(
        "Strategic-reasoning library collection completed: "
        f"profiles={len(status_rows)}, game rows={len(games_rows)}."
    )
    print(f"Games output: {args.games_output}")
    print(f"Status output: {args.status_output}")
    print(f"Summary output: {args.summary_output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())