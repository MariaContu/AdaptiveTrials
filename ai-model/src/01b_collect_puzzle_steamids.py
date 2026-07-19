"""Collect complementary SteamIDs from strongly puzzle-oriented games.

This stage does not overwrite the original sample. It:
- reads a dedicated puzzle entry-game configuration;
- collects review authors;
- removes duplicates inside the complementary collection;
- excludes SteamIDs already present in the original sample;
- balances the selected candidates across puzzle subgroups when possible.

Outputs:
- data/raw/puzzle_review_authors_raw.csv
- data/interim/puzzle_steamids_candidates.csv
- reports/metrics/01b_puzzle_steamid_collection_summary.json
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import random
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_CONFIG = PROJECT_ROOT / "config" / "puzzle_entry_games.json"
DEFAULT_EXISTING_IDS = PROJECT_ROOT / "data" / "interim" / "steamids_unique.csv"
DEFAULT_RAW_OUTPUT = (
    PROJECT_ROOT / "data" / "raw" / "puzzle_review_authors_raw.csv"
)
DEFAULT_SELECTED_OUTPUT = (
    PROJECT_ROOT / "data" / "interim" / "puzzle_steamids_candidates.csv"
)
DEFAULT_SUMMARY_OUTPUT = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "01b_puzzle_steamid_collection_summary.json"
)

REVIEWS_ENDPOINT = "https://store.steampowered.com/appreviews/{appid}"

LOGGER = logging.getLogger("puzzle-steamid-collector")

RAW_FIELDS = [
    "appid",
    "game_name",
    "subgroup",
    "steamid",
    "review_created_at",
    "review_updated_at",
    "voted_up",
    "playtime_forever_minutes",
    "language",
    "weighted_vote_score",
    "collected_at",
]

SELECTED_FIELDS = [
    "candidate_id",
    "steamid",
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
        description="Collect complementary puzzle-oriented SteamIDs."
    )
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument(
        "--existing-ids",
        type=Path,
        default=DEFAULT_EXISTING_IDS,
    )
    parser.add_argument("--raw-output", type=Path, default=DEFAULT_RAW_OUTPUT)
    parser.add_argument(
        "--selected-output",
        type=Path,
        default=DEFAULT_SELECTED_OUTPUT,
    )
    parser.add_argument(
        "--summary-output",
        type=Path,
        default=DEFAULT_SUMMARY_OUTPUT,
    )
    parser.add_argument(
        "--target-unique",
        type=int,
        default=300,
        help="Target number of new unique SteamIDs. Default: 300.",
    )
    parser.add_argument(
        "--max-per-game",
        type=int,
        default=40,
        help="Maximum accepted review authors per game. Default: 40.",
    )
    parser.add_argument(
        "--reviews-per-page",
        type=int,
        default=100,
        help="Reviews requested per page. Default: 100.",
    )
    parser.add_argument(
        "--max-pages-per-game",
        type=int,
        default=5,
        help="Maximum pages per entry game. Default: 5.",
    )
    parser.add_argument(
        "--request-delay",
        type=float,
        default=1.0,
        help="Delay between requests in seconds. Default: 1.",
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=30.0,
        help="HTTP timeout in seconds. Default: 30.",
    )
    parser.add_argument(
        "--minimum-review-playtime",
        type=int,
        default=30,
        help=(
            "Minimum reviewer playtime in minutes for candidate acceptance. "
            "Default: 30."
        ),
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=42,
        help="Random seed used for balanced selection. Default: 42.",
    )

    args = parser.parse_args()

    if args.target_unique <= 0:
        parser.error("--target-unique must be greater than zero.")
    if args.max_per_game <= 0:
        parser.error("--max-per-game must be greater than zero.")
    if not 1 <= args.reviews_per_page <= 100:
        parser.error("--reviews-per-page must be between 1 and 100.")
    if args.max_pages_per_game <= 0:
        parser.error("--max-pages-per-game must be greater than zero.")
    if args.request_delay < 0:
        parser.error("--request-delay cannot be negative.")
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero.")
    if args.minimum_review_playtime < 0:
        parser.error("--minimum-review-playtime cannot be negative.")

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


def load_config(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8") as file:
        payload = json.load(file)

    games = payload.get("games")
    if not isinstance(games, list):
        raise ValueError("Config must contain a games list.")

    enabled = []

    for game in games:
        if not isinstance(game, dict):
            continue
        if game.get("enabled", True) is False:
            continue

        appid = safe_int(game.get("appid"))
        name = normalize(game.get("name"))
        subgroup = normalize(game.get("subgroup"))

        if appid > 0 and name and subgroup:
            enabled.append(
                {
                    "appid": appid,
                    "name": name,
                    "subgroup": subgroup,
                }
            )

    if not enabled:
        raise ValueError("No enabled puzzle entry games were found.")

    return enabled


def find_steamid_column(fieldnames: list[str] | None) -> str:
    if not fieldnames:
        raise ValueError("Existing SteamID CSV has no header.")

    candidates = [
        "steamid",
        "steam_id",
        "steamid64",
        "steam_id_64",
    ]

    lookup = {field.casefold(): field for field in fieldnames}

    for candidate in candidates:
        field = lookup.get(candidate.casefold())
        if field:
            return field

    raise ValueError(
        "Could not identify SteamID column. "
        f"Available columns: {fieldnames}"
    )


def load_existing_steamids(path: Path) -> set[str]:
    if not path.exists():
        LOGGER.warning(
            "Existing SteamID file was not found; no original IDs will be excluded: %s",
            path,
        )
        return set()

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        steamid_column = find_steamid_column(reader.fieldnames)

        return {
            normalize(row.get(steamid_column))
            for row in reader
            if normalize(row.get(steamid_column))
        }


def create_session() -> requests.Session:
    retry = Retry(
        total=5,
        connect=5,
        read=5,
        status=5,
        backoff_factor=1.0,
        status_forcelist=(429, 500, 502, 503, 504),
        allowed_methods=frozenset({"GET"}),
        raise_on_status=False,
    )
    adapter = HTTPAdapter(max_retries=retry)

    session = requests.Session()
    session.mount("https://", adapter)
    session.headers.update(
        {
            "User-Agent": (
                "AdaptiveTrials-AcademicResearch/1.0 "
                "(complementary Steam review collector)"
            )
        }
    )
    return session


def fetch_review_page(
    session: requests.Session,
    appid: int,
    cursor: str,
    reviews_per_page: int,
    timeout: float,
) -> tuple[list[dict[str, Any]], str, int]:
    response = session.get(
        REVIEWS_ENDPOINT.format(appid=appid),
        params={
            "json": 1,
            "filter": "recent",
            "language": "all",
            "day_range": 365922,
            "review_type": "all",
            "purchase_type": "all",
            "num_per_page": reviews_per_page,
            "cursor": cursor,
        },
        timeout=timeout,
    )

    if response.status_code != requests.codes.ok:
        raise requests.HTTPError(
            f"HTTP {response.status_code} for AppID {appid}",
            response=response,
        )

    payload = response.json()

    if safe_int(payload.get("success")) != 1:
        raise ValueError(f"Steam review request failed for AppID {appid}.")

    reviews = payload.get("reviews")
    if not isinstance(reviews, list):
        reviews = []

    next_cursor = normalize(payload.get("cursor"))
    query_summary = payload.get("query_summary")
    total_reviews = (
        safe_int(query_summary.get("total_reviews"))
        if isinstance(query_summary, dict)
        else 0
    )

    return reviews, next_cursor, total_reviews


def review_to_row(
    review: dict[str, Any],
    game: dict[str, Any],
    collected_at: str,
) -> dict[str, Any] | None:
    author = review.get("author")
    if not isinstance(author, dict):
        return None

    steamid = normalize(author.get("steamid"))
    if not steamid:
        return None

    return {
        "appid": game["appid"],
        "game_name": game["name"],
        "subgroup": game["subgroup"],
        "steamid": steamid,
        "review_created_at": safe_int(review.get("timestamp_created")),
        "review_updated_at": safe_int(review.get("timestamp_updated")),
        "voted_up": bool(review.get("voted_up")),
        "playtime_forever_minutes": safe_int(
            author.get("playtime_forever")
        ),
        "language": normalize(review.get("language")),
        "weighted_vote_score": safe_float(
            review.get("weighted_vote_score")
        ),
        "collected_at": collected_at,
    }


def collect_for_game(
    session: requests.Session,
    game: dict[str, Any],
    args: argparse.Namespace,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    accepted: list[dict[str, Any]] = []
    seen_for_game: set[str] = set()
    cursor = "*"
    pages_requested = 0
    reviews_received = 0
    total_reviews_reported = 0
    status = "success"
    error_message = ""

    for _ in range(args.max_pages_per_game):
        if len(accepted) >= args.max_per_game:
            break

        pages_requested += 1

        try:
            reviews, next_cursor, reported_total = fetch_review_page(
                session=session,
                appid=game["appid"],
                cursor=cursor,
                reviews_per_page=args.reviews_per_page,
                timeout=args.timeout,
            )
        except (requests.RequestException, ValueError, json.JSONDecodeError) as error:
            status = "failed"
            error_message = str(error)
            break

        reviews_received += len(reviews)
        total_reviews_reported = max(
            total_reviews_reported,
            reported_total,
        )
        collected_at = datetime.now(timezone.utc).isoformat()

        for review in reviews:
            row = review_to_row(review, game, collected_at)
            if row is None:
                continue

            steamid = row["steamid"]

            if steamid in seen_for_game:
                continue

            if (
                safe_int(row["playtime_forever_minutes"])
                < args.minimum_review_playtime
            ):
                continue

            seen_for_game.add(steamid)
            accepted.append(row)

            if len(accepted) >= args.max_per_game:
                break

        if not reviews or not next_cursor or next_cursor == cursor:
            break

        cursor = next_cursor

        if args.request_delay > 0:
            time.sleep(args.request_delay)

    return accepted, {
        "appid": game["appid"],
        "game_name": game["name"],
        "subgroup": game["subgroup"],
        "status": status,
        "pages_requested": pages_requested,
        "reviews_received": reviews_received,
        "accepted_authors": len(accepted),
        "total_reviews_reported": total_reviews_reported,
        "error": error_message,
    }


def balanced_select(
    rows: list[dict[str, Any]],
    target: int,
    seed: int,
) -> list[dict[str, Any]]:
    randomizer = random.Random(seed)
    by_subgroup: dict[str, list[dict[str, Any]]] = defaultdict(list)

    for row in rows:
        by_subgroup[normalize(row.get("subgroup"))].append(row)

    for subgroup_rows in by_subgroup.values():
        randomizer.shuffle(subgroup_rows)

    selected: list[dict[str, Any]] = []
    subgroup_names = sorted(by_subgroup)

    while len(selected) < target:
        added = False

        for subgroup in subgroup_names:
            if len(selected) >= target:
                break

            subgroup_rows = by_subgroup[subgroup]
            if subgroup_rows:
                selected.append(subgroup_rows.pop())
                added = True

        if not added:
            break

    return selected


def write_csv_atomic(
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


def write_json_atomic(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temp.replace(path)


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        games = load_config(args.config)
        existing_ids = load_existing_steamids(args.existing_ids)
        session = create_session()

        raw_rows: list[dict[str, Any]] = []
        game_summaries: list[dict[str, Any]] = []

        for index, game in enumerate(games, start=1):
            rows, summary = collect_for_game(session, game, args)
            raw_rows.extend(rows)
            game_summaries.append(summary)

            LOGGER.info(
                "[%d/%d] %s: status=%s, accepted=%d.",
                index,
                len(games),
                game["name"],
                summary["status"],
                summary["accepted_authors"],
            )

            if args.request_delay > 0:
                time.sleep(args.request_delay)

        unique_candidates: dict[str, dict[str, Any]] = {}
        duplicate_occurrences = 0
        existing_occurrences = 0

        for row in raw_rows:
            steamid = row["steamid"]

            if steamid in existing_ids:
                existing_occurrences += 1
                continue

            if steamid in unique_candidates:
                duplicate_occurrences += 1
                continue

            unique_candidates[steamid] = row

        selected_source_rows = balanced_select(
            rows=list(unique_candidates.values()),
            target=args.target_unique,
            seed=args.seed,
        )

        selected_rows = [
            {
                "candidate_id": f"puzzle_candidate_{index:04d}",
                "steamid": row["steamid"],
                "source_appid": row["appid"],
                "source_game": row["game_name"],
                "source_subgroup": row["subgroup"],
                "collection_stage": "01b_puzzle_expansion",
            }
            for index, row in enumerate(selected_source_rows, start=1)
        ]

        write_csv_atomic(args.raw_output, RAW_FIELDS, raw_rows)
        write_csv_atomic(
            args.selected_output,
            SELECTED_FIELDS,
            selected_rows,
        )

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "01b_collect_puzzle_steamids",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "target_unique": args.target_unique,
                "max_per_game": args.max_per_game,
                "reviews_per_page": args.reviews_per_page,
                "max_pages_per_game": args.max_pages_per_game,
                "request_delay": args.request_delay,
                "timeout": args.timeout,
                "minimum_review_playtime": (
                    args.minimum_review_playtime
                ),
                "seed": args.seed,
            },
            "enabled_entry_games": len(games),
            "existing_original_steamids_loaded": len(existing_ids),
            "raw_accepted_rows": len(raw_rows),
            "duplicates_inside_complementary_collection": (
                duplicate_occurrences
            ),
            "occurrences_already_in_original_sample": (
                existing_occurrences
            ),
            "new_unique_candidates_available": len(unique_candidates),
            "selected_unique_candidates": len(selected_rows),
            "target_reached": len(selected_rows) >= args.target_unique,
            "selected_by_subgroup": dict(
                sorted(
                    Counter(
                        row["source_subgroup"]
                        for row in selected_rows
                    ).items()
                )
            ),
            "selected_by_game": dict(
                sorted(
                    Counter(
                        row["source_game"]
                        for row in selected_rows
                    ).items()
                )
            ),
            "game_results": game_summaries,
            "methodological_note": (
                "This is a complementary directed sample. It is stored "
                "separately from the original pilot and excludes SteamIDs "
                "already collected in that sample."
            ),
        }

        write_json_atomic(args.summary_output, summary)

    except (OSError, ValueError, requests.RequestException) as error:
        LOGGER.exception("Puzzle SteamID collection failed: %s", error)
        return 1

    LOGGER.info(
        "Complementary collection completed: selected=%d.",
        len(selected_rows),
    )
    LOGGER.info("Raw output: %s", args.raw_output)
    LOGGER.info("Selected output: %s", args.selected_output)
    LOGGER.info("Summary output: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())