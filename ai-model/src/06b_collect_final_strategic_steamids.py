"""Collect the final strategic-reasoning SteamID candidate sample.

This stage:
- reads the final collection plan and the versioned entry-game configuration;
- applies a distinct candidate target to each strategic-reasoning subgroup;
- distributes collection across multiple entry games;
- excludes SteamIDs from previous pilots and optional additional files;
- prevents a single entry game from exceeding the configured source share;
- preserves source provenance for later granularity and leakage analyses.

The entry game is only a sampling point and never defines the final target.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import random
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PLAN = PROJECT_ROOT / "config" / "final_collection_plan.json"
DEFAULT_CONFIG = (
    PROJECT_ROOT / "config" / "strategic_reasoning_entry_games.json"
)
DEFAULT_EXCLUSION_FILES = [
    PROJECT_ROOT / "data" / "interim" / "steamids_unique.csv",
    PROJECT_ROOT / "data" / "interim" / "puzzle_steamids_candidates.csv",
    PROJECT_ROOT
    / "data"
    / "interim"
    / "strategic_reasoning_steamids_candidates.csv",
]
DEFAULT_RAW_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "raw"
    / "final_strategic_review_authors_raw.csv"
)
DEFAULT_SELECTED_OUTPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "final_strategic_steamids_candidates.csv"
)
DEFAULT_SUMMARY_OUTPUT = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "06_strategic_collection_summary.json"
)

REVIEWS_ENDPOINT = "https://store.steampowered.com/appreviews/{appid}"
LOGGER = logging.getLogger("final-strategic-steamid-collector")

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
        description=(
            "Collect final strategic-reasoning SteamID candidates "
            "using the versioned Etapa 06 plan."
        )
    )
    parser.add_argument("--plan", type=Path, default=DEFAULT_PLAN)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument(
        "--exclude-ids",
        type=Path,
        action="append",
        default=None,
        help=(
            "CSV containing SteamIDs to exclude. May be passed multiple "
            "times. When omitted, known pilot files are used."
        ),
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
    parser.add_argument("--reviews-per-page", type=int, default=100)
    parser.add_argument("--max-pages-per-game", type=int, default=10)
    parser.add_argument("--request-delay", type=float, default=1.0)
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument(
        "--minimum-review-playtime",
        type=int,
        default=30,
    )
    parser.add_argument(
        "--collection-buffer",
        type=float,
        default=1.35,
        help=(
            "Multiplier applied to the estimated per-game need before "
            "selection. Values above 1 provide room for duplicates."
        ),
    )
    parser.add_argument("--seed", type=int, default=42)

    args = parser.parse_args()

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
    if args.collection_buffer < 1:
        parser.error("--collection-buffer must be at least 1.")

    if args.exclude_ids is None:
        args.exclude_ids = DEFAULT_EXCLUSION_FILES

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


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8") as file:
        payload = json.load(file)

    if not isinstance(payload, dict):
        raise ValueError(f"Expected a JSON object in {path}.")

    return payload


def load_plan(path: Path) -> dict[str, Any]:
    payload = load_json(path)

    try:
        strategic = payload["sampling"]["strategic_reasoning"]
        subgroup_targets = strategic["subgroup_targets"]
        candidate_target = safe_int(strategic["candidate_target"])
        maximum_game_share = float(
            payload["source_balance"][
                "maximum_share_per_entry_game_within_source"
            ]
        )
        minimum_games_per_subgroup = safe_int(
            payload["source_balance"][
                "minimum_distinct_entry_games_per_subgroup"
            ]
        )
        fail_when_not_reached = bool(
            payload["source_balance"]["fail_when_subgroup_target_not_reached"]
        )
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError(
            "The final collection plan does not contain the required "
            "strategic sampling fields."
        ) from error

    targets: dict[str, int] = {}

    for subgroup, values in subgroup_targets.items():
        target = safe_int(values.get("candidate_target"))
        if target <= 0:
            raise ValueError(
                f"Invalid candidate target for subgroup {subgroup!r}."
            )
        targets[normalize(subgroup)] = target

    if sum(targets.values()) != candidate_target:
        raise ValueError(
            "Strategic subgroup targets do not sum to the strategic "
            "candidate target."
        )

    if not 0 < maximum_game_share <= 1:
        raise ValueError(
            "maximum_share_per_entry_game_within_source must be in (0, 1]."
        )

    return {
        "version": normalize(payload.get("version")),
        "candidate_target": candidate_target,
        "subgroup_targets": targets,
        "maximum_game_share": maximum_game_share,
        "minimum_games_per_subgroup": minimum_games_per_subgroup,
        "fail_when_not_reached": fail_when_not_reached,
    }


def load_games(
    path: Path,
    expected_subgroups: set[str],
    minimum_games_per_subgroup: int,
) -> list[dict[str, Any]]:
    payload = load_json(path)
    games = payload.get("games")

    if not isinstance(games, list):
        raise ValueError("Entry-game config must contain a games list.")

    enabled: list[dict[str, Any]] = []
    seen_appids: set[int] = set()

    for game in games:
        if not isinstance(game, dict) or game.get("enabled", True) is False:
            continue

        appid = safe_int(game.get("appid"))
        name = normalize(game.get("name"))
        subgroup = normalize(game.get("subgroup"))

        if subgroup not in expected_subgroups:
            continue
        if appid <= 0 or not name:
            continue
        if appid in seen_appids:
            raise ValueError(f"Duplicate enabled AppID in config: {appid}")

        seen_appids.add(appid)
        enabled.append(
            {
                "appid": appid,
                "name": name,
                "subgroup": subgroup,
            }
        )

    by_subgroup = Counter(game["subgroup"] for game in enabled)

    for subgroup in sorted(expected_subgroups):
        count = by_subgroup.get(subgroup, 0)
        if count < minimum_games_per_subgroup:
            raise ValueError(
                f"Subgroup {subgroup!r} has {count} enabled entry games; "
                f"at least {minimum_games_per_subgroup} are required."
            )

    return enabled


def find_steamid_column(fieldnames: list[str] | None) -> str:
    if not fieldnames:
        raise ValueError("SteamID CSV has no header.")

    candidates = (
        "steamid",
        "source_steamid",
        "steam_id",
        "steamid64",
        "steam_id_64",
    )
    lookup = {field.casefold(): field for field in fieldnames}

    for candidate in candidates:
        field = lookup.get(candidate.casefold())
        if field:
            return field

    raise ValueError(
        "Could not identify SteamID column. "
        f"Available columns: {fieldnames}"
    )


def load_excluded_steamids(paths: Iterable[Path]) -> tuple[set[str], dict[str, int]]:
    excluded: set[str] = set()
    counts: dict[str, int] = {}

    for path in paths:
        resolved = path.resolve()

        if not resolved.exists():
            LOGGER.warning("Exclusion file not found; skipped: %s", resolved)
            counts[str(resolved)] = 0
            continue

        with resolved.open("r", encoding="utf-8", newline="") as file:
            reader = csv.DictReader(file)
            steamid_column = find_steamid_column(reader.fieldnames)
            file_ids = {
                normalize(row.get(steamid_column))
                for row in reader
                if normalize(row.get(steamid_column))
            }

        excluded.update(file_ids)
        counts[str(resolved)] = len(file_ids)

    return excluded, counts


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
                "(final strategic reasoning review collector)"
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
        raise ValueError(
            f"Steam review request failed for AppID {appid}."
        )

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


def estimate_collection_cap(
    subgroup_target: int,
    games_in_subgroup: int,
    buffer_multiplier: float,
) -> int:
    if games_in_subgroup <= 0:
        raise ValueError("games_in_subgroup must be positive.")

    per_game_need = math.ceil(subgroup_target / games_in_subgroup)
    return max(1, math.ceil(per_game_need * buffer_multiplier))


def collect_for_game(
    session: requests.Session,
    game: dict[str, Any],
    accepted_limit: int,
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
        if len(accepted) >= accepted_limit:
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
        except (
            requests.RequestException,
            ValueError,
            json.JSONDecodeError,
        ) as error:
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

            if len(accepted) >= accepted_limit:
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
        "accepted_limit": accepted_limit,
        "pages_requested": pages_requested,
        "reviews_received": reviews_received,
        "accepted_authors": len(accepted),
        "total_reviews_reported": total_reviews_reported,
        "error": error_message,
    }


def deduplicate_candidates(
    raw_rows: list[dict[str, Any]],
    excluded_ids: set[str],
) -> tuple[
    dict[str, list[dict[str, Any]]],
    int,
    int,
]:
    by_subgroup: dict[str, list[dict[str, Any]]] = defaultdict(list)
    seen: set[str] = set()
    duplicate_occurrences = 0
    excluded_occurrences = 0

    for row in raw_rows:
        steamid = normalize(row.get("steamid"))

        if steamid in excluded_ids:
            excluded_occurrences += 1
            continue
        if steamid in seen:
            duplicate_occurrences += 1
            continue

        seen.add(steamid)
        by_subgroup[normalize(row.get("subgroup"))].append(row)

    return by_subgroup, duplicate_occurrences, excluded_occurrences


def select_subgroup_candidates(
    rows: list[dict[str, Any]],
    target: int,
    maximum_per_game: int,
    seed: int,
) -> list[dict[str, Any]]:
    randomizer = random.Random(seed)
    by_game: dict[int, list[dict[str, Any]]] = defaultdict(list)

    for row in rows:
        by_game[safe_int(row.get("appid"))].append(row)

    for game_rows in by_game.values():
        randomizer.shuffle(game_rows)

    selected: list[dict[str, Any]] = []
    selected_per_game: Counter[int] = Counter()
    appids = sorted(by_game)

    while len(selected) < target:
        added = False

        for appid in appids:
            if len(selected) >= target:
                break
            if selected_per_game[appid] >= maximum_per_game:
                continue

            game_rows = by_game[appid]
            if not game_rows:
                continue

            selected.append(game_rows.pop())
            selected_per_game[appid] += 1
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
        plan = load_plan(args.plan)
        subgroup_targets: dict[str, int] = plan["subgroup_targets"]
        games = load_games(
            path=args.config,
            expected_subgroups=set(subgroup_targets),
            minimum_games_per_subgroup=plan[
                "minimum_games_per_subgroup"
            ],
        )
        excluded_ids, exclusion_counts = load_excluded_steamids(
            args.exclude_ids
        )
        session = create_session()

        games_per_subgroup = Counter(
            game["subgroup"] for game in games
        )
        raw_rows: list[dict[str, Any]] = []
        game_summaries: list[dict[str, Any]] = []

        for index, game in enumerate(games, start=1):
            subgroup = game["subgroup"]
            accepted_limit = estimate_collection_cap(
                subgroup_target=subgroup_targets[subgroup],
                games_in_subgroup=games_per_subgroup[subgroup],
                buffer_multiplier=args.collection_buffer,
            )

            rows, summary = collect_for_game(
                session=session,
                game=game,
                accepted_limit=accepted_limit,
                args=args,
            )
            raw_rows.extend(rows)
            game_summaries.append(summary)

            LOGGER.info(
                "[%d/%d] %s [%s]: status=%s, accepted=%d/%d.",
                index,
                len(games),
                game["name"],
                subgroup,
                summary["status"],
                summary["accepted_authors"],
                accepted_limit,
            )

            if args.request_delay > 0:
                time.sleep(args.request_delay)

        (
            candidates_by_subgroup,
            duplicate_occurrences,
            excluded_occurrences,
        ) = deduplicate_candidates(
            raw_rows=raw_rows,
            excluded_ids=excluded_ids,
        )

        maximum_per_game = math.floor(
            plan["candidate_target"] * plan["maximum_game_share"]
        )
        maximum_per_game = max(1, maximum_per_game)

        selected_source_rows: list[dict[str, Any]] = []
        subgroup_results: dict[str, dict[str, Any]] = {}

        for subgroup_index, subgroup in enumerate(
            sorted(subgroup_targets),
            start=1,
        ):
            target = subgroup_targets[subgroup]
            available_rows = candidates_by_subgroup.get(subgroup, [])
            selected = select_subgroup_candidates(
                rows=available_rows,
                target=target,
                maximum_per_game=maximum_per_game,
                seed=args.seed + subgroup_index,
            )
            selected_source_rows.extend(selected)

            selected_by_game = Counter(
                normalize(row.get("game_name")) for row in selected
            )
            subgroup_results[subgroup] = {
                "target": target,
                "available_unique_candidates": len(available_rows),
                "selected": len(selected),
                "target_reached": len(selected) >= target,
                "selected_by_game": dict(sorted(selected_by_game.items())),
            }

        failed_subgroups = [
            subgroup
            for subgroup, values in subgroup_results.items()
            if not values["target_reached"]
        ]

        if failed_subgroups and plan["fail_when_not_reached"]:
            details = ", ".join(
                (
                    f"{subgroup}: "
                    f"{subgroup_results[subgroup]['selected']}/"
                    f"{subgroup_results[subgroup]['target']}"
                )
                for subgroup in failed_subgroups
            )
            raise ValueError(
                "Strategic subgroup targets were not reached: " + details
            )

        selected_source_rows.sort(
            key=lambda row: (
                normalize(row.get("subgroup")),
                normalize(row.get("game_name")),
                normalize(row.get("steamid")),
            )
        )

        selected_rows = [
            {
                "candidate_id": f"final_strategic_{index:04d}",
                "steamid": row["steamid"],
                "source_appid": row["appid"],
                "source_game": row["game_name"],
                "source_subgroup": row["subgroup"],
                "collection_stage": (
                    "06_final_strategic_reasoning_collection"
                ),
            }
            for index, row in enumerate(selected_source_rows, start=1)
        ]

        selected_by_game = Counter(
            row["source_game"] for row in selected_rows
        )
        selected_by_subgroup = Counter(
            row["source_subgroup"] for row in selected_rows
        )
        maximum_observed_game_count = max(
            selected_by_game.values(),
            default=0,
        )
        maximum_observed_game_share = (
            maximum_observed_game_count / len(selected_rows)
            if selected_rows
            else 0.0
        )

        if maximum_observed_game_count > maximum_per_game:
            raise ValueError(
                "A selected entry game exceeded the configured maximum "
                "share."
            )

        write_csv_atomic(args.raw_output, RAW_FIELDS, raw_rows)
        write_csv_atomic(
            args.selected_output,
            SELECTED_FIELDS,
            selected_rows,
        )

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_collect_final_strategic_steamids",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "plan_version": plan["version"],
            "parameters": {
                "candidate_target": plan["candidate_target"],
                "subgroup_targets": subgroup_targets,
                "maximum_share_per_entry_game_within_source": (
                    plan["maximum_game_share"]
                ),
                "maximum_selected_candidates_per_entry_game": (
                    maximum_per_game
                ),
                "minimum_distinct_entry_games_per_subgroup": (
                    plan["minimum_games_per_subgroup"]
                ),
                "reviews_per_page": args.reviews_per_page,
                "max_pages_per_game": args.max_pages_per_game,
                "request_delay": args.request_delay,
                "timeout": args.timeout,
                "minimum_review_playtime": (
                    args.minimum_review_playtime
                ),
                "collection_buffer": args.collection_buffer,
                "seed": args.seed,
            },
            "enabled_entry_games": len(games),
            "entry_games_by_subgroup": dict(
                sorted(games_per_subgroup.items())
            ),
            "exclusion_files": exclusion_counts,
            "excluded_unique_steamids_loaded": len(excluded_ids),
            "raw_accepted_rows": len(raw_rows),
            "duplicates_inside_final_directed_collection": (
                duplicate_occurrences
            ),
            "occurrences_removed_by_exclusion_files": (
                excluded_occurrences
            ),
            "new_unique_candidates_available": sum(
                len(rows) for rows in candidates_by_subgroup.values()
            ),
            "selected_unique_candidates": len(selected_rows),
            "target_reached": (
                len(selected_rows) >= plan["candidate_target"]
                and not failed_subgroups
            ),
            "selected_by_subgroup": dict(
                sorted(selected_by_subgroup.items())
            ),
            "selected_by_game": dict(sorted(selected_by_game.items())),
            "maximum_observed_entry_game_count": (
                maximum_observed_game_count
            ),
            "maximum_observed_entry_game_share": round(
                maximum_observed_game_share,
                6,
            ),
            "subgroup_results": subgroup_results,
            "game_results": game_summaries,
            "granularity_note": (
                "Source subgroup and source game are retained for sampling "
                "audits and granular comparisons. They do not define the "
                "player target."
            ),
            "methodological_note": (
                "The final target must be derived from the complete "
                "accessible player library and categorized playtime."
            ),
        }

        write_json_atomic(args.summary_output, summary)

    except (
        OSError,
        ValueError,
        requests.RequestException,
        json.JSONDecodeError,
    ) as error:
        LOGGER.exception(
            "Final strategic-reasoning SteamID collection failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Final strategic collection completed: selected=%d.",
        len(selected_rows),
    )
    LOGGER.info("Raw output: %s", args.raw_output)
    LOGGER.info("Selected output: %s", args.selected_output)
    LOGGER.info("Summary output: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())