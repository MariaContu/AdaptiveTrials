"""Run the validated player-library collector for puzzle candidates.

This wrapper reuses src/02_fetch_player_games.py instead of duplicating its
Steam API logic. It prepares a compatible temporary input, runs the original
collector with separate outputs, and enriches the resulting files with the
candidate's puzzle source game and subgroup.

The original Stage 02 collector renumbers candidates as player_####. Therefore,
source provenance is restored through source_steamid in the status output and
then propagated to the player-game output through player_id.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import subprocess
import sys
from pathlib import Path
from typing import Any


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_INPUT = (
    PROJECT_ROOT / "data" / "interim" / "puzzle_steamids_candidates.csv"
)
DEFAULT_STAGE02_SCRIPT = PROJECT_ROOT / "src" / "02_fetch_player_games.py"

DEFAULT_GAMES_OUTPUT = (
    PROJECT_ROOT / "data" / "raw" / "puzzle_player_games_raw.csv"
)
DEFAULT_STATUS_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "puzzle_player_library_status.csv"
)
DEFAULT_SUMMARY_OUTPUT = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "02b_puzzle_player_games_summary.json"
)

DEFAULT_TEMP_INPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / ".puzzle_candidates_for_stage02.csv"
)
DEFAULT_TEMP_SUMMARY = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / ".02b_base_summary.json"
)

LOGGER = logging.getLogger("puzzle-player-library-wrapper")

SOURCE_FIELDS = [
    "source_appid",
    "source_game",
    "source_subgroup",
    "collection_stage",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Fetch Steam libraries for puzzle candidates by reusing "
            "02_fetch_player_games.py."
        )
    )
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument(
        "--stage02-script",
        type=Path,
        default=DEFAULT_STAGE02_SCRIPT,
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
    parser.add_argument("--max-players", type=int, default=None)
    parser.add_argument("--target-valid", type=int, default=100)
    parser.add_argument("--minimum-games", type=int, default=1)
    parser.add_argument("--minimum-played-games", type=int, default=1)
    parser.add_argument(
        "--keep-temporary-files",
        action="store_true",
    )

    args = parser.parse_args()

    if args.request_delay < 0:
        parser.error("--request-delay cannot be negative.")
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero.")
    if args.max_players is not None and args.max_players <= 0:
        parser.error("--max-players must be greater than zero.")
    if args.target_valid <= 0:
        parser.error("--target-valid must be greater than zero.")
    if args.minimum_games < 0:
        parser.error("--minimum-games cannot be negative.")
    if args.minimum_played_games < 0:
        parser.error("--minimum-played-games cannot be negative.")

    return args


def normalize(value: Any) -> str:
    return "" if value is None else str(value).strip()


def load_candidates(
    path: Path,
    max_players: int | None,
) -> list[dict[str, str]]:
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
            "collection_stage",
        }

        if reader.fieldnames is None or not required.issubset(
            reader.fieldnames
        ):
            raise ValueError(
                f"Candidate CSV must contain: {sorted(required)}"
            )

        candidates = [
            {
                field: normalize(row.get(field))
                for field in required
            }
            for row in reader
            if normalize(row.get("candidate_id"))
            and normalize(row.get("steamid"))
        ]

    if max_players is not None:
        candidates = candidates[:max_players]

    if not candidates:
        raise ValueError("No puzzle candidates were found.")

    steamids = [row["steamid"] for row in candidates]
    if len(steamids) != len(set(steamids)):
        raise ValueError("Duplicate SteamIDs were found.")

    return candidates


def write_stage02_input(
    path: Path,
    candidates: list[dict[str, str]],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)

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


def run_stage02(
    args: argparse.Namespace,
    temp_input: Path,
    temp_summary: Path,
) -> None:
    if not args.stage02_script.exists():
        raise FileNotFoundError(args.stage02_script)

    command = [
        sys.executable,
        str(args.stage02_script),
        "--input",
        str(temp_input),
        "--games-output",
        str(args.games_output),
        "--status-output",
        str(args.status_output),
        "--summary-output",
        str(temp_summary),
        "--request-delay",
        str(args.request_delay),
        "--timeout",
        str(args.timeout),
        "--target-valid",
        str(args.target_valid),
        "--minimum-games",
        str(args.minimum_games),
        "--minimum-played-games",
        str(args.minimum_played_games),
    ]

    LOGGER.info("Running validated Stage 02 collector.")
    subprocess.run(command, check=True, cwd=PROJECT_ROOT)


def enrich_status_csv(
    status_path: Path,
    source_by_steamid: dict[str, dict[str, str]],
) -> dict[str, dict[str, str]]:
    if not status_path.exists():
        raise FileNotFoundError(status_path)

    with status_path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)

        required = {"player_id", "source_steamid"}
        if reader.fieldnames is None or not required.issubset(
            reader.fieldnames
        ):
            raise ValueError(
                "Status output must contain player_id and source_steamid."
            )

        original_fields = list(reader.fieldnames)
        rows = list(reader)

    fieldnames = original_fields + [
        field for field in SOURCE_FIELDS if field not in original_fields
    ]

    source_by_player: dict[str, dict[str, str]] = {}

    for row in rows:
        steamid = normalize(row.get("source_steamid"))
        player_id = normalize(row.get("player_id"))
        source = source_by_steamid.get(steamid, {})

        for field in SOURCE_FIELDS:
            row[field] = source.get(field, "")

        if player_id:
            source_by_player[player_id] = {
                field: row.get(field, "") for field in SOURCE_FIELDS
            }

    temp = status_path.with_suffix(status_path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    temp.replace(status_path)
    return source_by_player


def enrich_games_csv(
    games_path: Path,
    source_by_player: dict[str, dict[str, str]],
) -> None:
    if not games_path.exists():
        raise FileNotFoundError(games_path)

    with games_path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)

        if reader.fieldnames is None or "player_id" not in reader.fieldnames:
            raise ValueError(
                "Games output must contain player_id."
            )

        original_fields = list(reader.fieldnames)
        rows = list(reader)

    fieldnames = original_fields + [
        field for field in SOURCE_FIELDS if field not in original_fields
    ]

    for row in rows:
        player_id = normalize(row.get("player_id"))
        source = source_by_player.get(player_id, {})

        for field in SOURCE_FIELDS:
            row[field] = source.get(field, "")

    temp = games_path.with_suffix(games_path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    temp.replace(games_path)


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8") as file:
        payload = json.load(file)

    if not isinstance(payload, dict):
        raise ValueError(f"JSON root must be an object: {path}")

    return payload


def write_json_atomic(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temp.replace(path)


def count_status_by_subgroup(
    status_path: Path,
) -> dict[str, dict[str, int]]:
    result: dict[str, dict[str, int]] = {}

    with status_path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)

        required = {"status", "source_subgroup"}
        if reader.fieldnames is None or not required.issubset(
            reader.fieldnames
        ):
            return result

        for row in reader:
            subgroup = normalize(row.get("source_subgroup")) or "unknown"
            status = normalize(row.get("status")) or "unknown"

            result.setdefault(subgroup, {})
            result[subgroup][status] = (
                result[subgroup].get(status, 0) + 1
            )

    return {
        subgroup: dict(sorted(counts.items()))
        for subgroup, counts in sorted(result.items())
    }


def count_valid_by_game(
    status_path: Path,
) -> dict[str, int]:
    counts: dict[str, int] = {}

    with status_path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)

        for row in reader:
            if normalize(row.get("status")) != "valid":
                continue
            game = normalize(row.get("source_game")) or "unknown"
            counts[game] = counts.get(game, 0) + 1

    return dict(sorted(counts.items()))


def main() -> int:
    configure_logging()
    args = parse_args()

    temp_input = DEFAULT_TEMP_INPUT
    temp_summary = DEFAULT_TEMP_SUMMARY

    try:
        candidates = load_candidates(
            path=args.input,
            max_players=args.max_players,
        )

        source_by_steamid = {
            candidate["steamid"]: {
                field: candidate[field]
                for field in SOURCE_FIELDS
            }
            for candidate in candidates
        }

        write_stage02_input(temp_input, candidates)

        args.games_output.parent.mkdir(parents=True, exist_ok=True)
        args.status_output.parent.mkdir(parents=True, exist_ok=True)
        temp_summary.parent.mkdir(parents=True, exist_ok=True)

        run_stage02(
            args=args,
            temp_input=temp_input,
            temp_summary=temp_summary,
        )

        source_by_player = enrich_status_csv(
            args.status_output,
            source_by_steamid,
        )
        enrich_games_csv(
            args.games_output,
            source_by_player,
        )

        base_summary = load_json(temp_summary)
        base_summary["pipeline_stage"] = (
            "02b_fetch_puzzle_player_games"
        )
        base_summary["candidate_source"] = str(args.input)
        base_summary["original_stage02_script"] = str(
            args.stage02_script
        )
        base_summary["complementary_candidates_loaded"] = len(
            candidates
        )
        base_summary["status_by_source_subgroup"] = (
            count_status_by_subgroup(args.status_output)
        )
        base_summary["valid_profiles_by_source_game"] = (
            count_valid_by_game(args.status_output)
        )
        base_summary["methodological_note"] = (
            "The complementary puzzle-oriented sample reuses the validated "
            "Stage 02 Steam library collector. Its files remain separate "
            "from the original pilot, and source-game provenance is restored "
            "through source_steamid after collection."
        )

        write_json_atomic(args.summary_output, base_summary)

    except (
        OSError,
        ValueError,
        json.JSONDecodeError,
        subprocess.CalledProcessError,
    ) as error:
        LOGGER.exception(
            "Complementary player-library collection failed: %s",
            error,
        )
        return 1

    finally:
        if not args.keep_temporary_files:
            temp_input.unlink(missing_ok=True)
            temp_summary.unlink(missing_ok=True)

    LOGGER.info(
        "Complementary player-library collection completed: candidates=%d.",
        len(candidates),
    )
    LOGGER.info("Games output: %s", args.games_output)
    LOGGER.info("Status output: %s", args.status_output)
    LOGGER.info("Summary output: %s", args.summary_output)

    return 0


if __name__ == "__main__":
    sys.exit(main())