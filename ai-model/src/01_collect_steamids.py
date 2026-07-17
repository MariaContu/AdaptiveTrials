"""Collect candidate SteamIDs from public Steam game reviews.

This script:
1. Reads source games from config/source_games.json.
2. Queries public Steam reviews using cursor pagination.
3. Collects review author SteamIDs from every configured source game.
4. Saves all accepted source occurrences.
5. Selects a reproducible, stratified and deduplicated pilot sample.
6. Exports collection and sampling statistics.

The collected SteamIDs are candidates only. Player library validation is
performed in the next pipeline stage.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import sys
import time
from collections import Counter, defaultdict, deque
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_CONFIG_PATH = PROJECT_ROOT / "config" / "source_games.json"
DEFAULT_RAW_OUTPUT = PROJECT_ROOT / "data" / "raw" / "review_authors_raw.csv"
DEFAULT_UNIQUE_OUTPUT = PROJECT_ROOT / "data" / "interim" / "steamids_unique.csv"
DEFAULT_SUMMARY_OUTPUT = (
    PROJECT_ROOT / "reports" / "metrics" / "01_steamid_collection_summary.json"
)

REVIEWS_ENDPOINT = "https://store.steampowered.com/appreviews/{appid}"
LOGGER = logging.getLogger("steamid-collector")


@dataclass(frozen=True)
class SourceGame:
    """Game used as a source of public review authors."""

    appid: int
    name: str
    sampling_stratum: str


@dataclass
class GameCollectionState:
    """Mutable pagination and collection state for one source game."""

    cursor: str = "*"
    pages_requested: int = 0
    reviews_received: int = 0
    accepted_rows: int = 0
    exhausted: bool = False
    error: str | None = None


RAW_FIELDNAMES = [
    "steamid",
    "source_appid",
    "source_game",
    "sampling_stratum",
    "recommendation_id",
    "review_language",
    "review_positive",
    "review_timestamp",
    "playtime_at_review_minutes",
    "playtime_forever_minutes",
    "steam_purchase",
    "received_for_free",
    "written_during_early_access",
    "collected_at",
]

UNIQUE_FIELDNAMES = [
    "steamid",
    "primary_sampling_stratum",
    "primary_source_appid",
    "source_count",
    "source_appids",
    "sampling_strata",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Collect candidate SteamIDs from public Steam reviews."
    )

    parser.add_argument(
        "--config",
        type=Path,
        default=DEFAULT_CONFIG_PATH,
        help="Path to the source games JSON file.",
    )
    parser.add_argument(
        "--target-unique",
        type=int,
        default=300,
        help="Target size of the deduplicated pilot sample. Default: 300.",
    )
    parser.add_argument(
        "--max-per-game",
        type=int,
        default=30,
        help="Maximum accepted review authors per source game. Default: 30.",
    )
    parser.add_argument(
        "--reviews-per-page",
        type=int,
        default=100,
        choices=range(1, 101),
        metavar="[1-100]",
        help="Reviews requested per API page. Default: 100.",
    )
    parser.add_argument(
        "--max-pages-per-game",
        type=int,
        default=5,
        help="Maximum number of pages requested for each game. Default: 5.",
    )
    parser.add_argument(
        "--request-delay",
        type=float,
        default=1.0,
        help="Delay in seconds between requests. Default: 1.0.",
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=30.0,
        help="HTTP request timeout in seconds. Default: 30.",
    )
    parser.add_argument(
        "--raw-output",
        type=Path,
        default=DEFAULT_RAW_OUTPUT,
        help="Output path for raw review author data.",
    )
    parser.add_argument(
        "--unique-output",
        type=Path,
        default=DEFAULT_UNIQUE_OUTPUT,
        help="Output path for the selected deduplicated SteamIDs.",
    )
    parser.add_argument(
        "--summary-output",
        type=Path,
        default=DEFAULT_SUMMARY_OUTPUT,
        help="Output path for collection summary JSON.",
    )

    args = parser.parse_args()

    if args.target_unique <= 0:
        parser.error("--target-unique must be greater than zero.")
    if args.max_per_game <= 0:
        parser.error("--max-per-game must be greater than zero.")
    if args.max_pages_per_game <= 0:
        parser.error("--max-pages-per-game must be greater than zero.")
    if args.request_delay < 0:
        parser.error("--request-delay cannot be negative.")
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero.")

    return args


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def load_source_games(config_path: Path) -> list[SourceGame]:
    if not config_path.exists():
        raise FileNotFoundError(f"Configuration file not found: {config_path}")

    with config_path.open("r", encoding="utf-8") as file:
        config = json.load(file)

    raw_games = config.get("games")
    if not isinstance(raw_games, list) or not raw_games:
        raise ValueError("The configuration must contain a non-empty 'games' list.")

    games: list[SourceGame] = []
    appids_seen: set[int] = set()

    for index, item in enumerate(raw_games):
        try:
            appid = int(item["appid"])
            name = str(item["name"]).strip()
            sampling_stratum = str(item["sampling_stratum"]).strip().lower()
        except (KeyError, TypeError, ValueError) as error:
            raise ValueError(
                f"Invalid game configuration at index {index}: {item}"
            ) from error

        if appid <= 0:
            raise ValueError(f"Invalid AppID at index {index}: {appid}")
        if not name:
            raise ValueError(f"Game name cannot be empty at index {index}.")
        if not sampling_stratum:
            raise ValueError(
                f"Sampling stratum cannot be empty at index {index}."
            )
        if appid in appids_seen:
            raise ValueError(f"Duplicated AppID in configuration: {appid}")

        appids_seen.add(appid)
        games.append(
            SourceGame(
                appid=appid,
                name=name,
                sampling_stratum=sampling_stratum,
            )
        )

    return games


def create_http_session() -> requests.Session:
    retry_strategy = Retry(
        total=5,
        connect=5,
        read=5,
        status=5,
        backoff_factor=1.0,
        status_forcelist=(429, 500, 502, 503, 504),
        allowed_methods=frozenset({"GET"}),
        raise_on_status=False,
    )

    adapter = HTTPAdapter(max_retries=retry_strategy)
    session = requests.Session()
    session.mount("https://", adapter)
    session.headers.update(
        {
            "User-Agent": (
                "AdaptiveTrials-AcademicResearch/1.0 "
                "(Steam public review metadata collector)"
            )
        }
    )
    return session


def fetch_review_page(
    session: requests.Session,
    game: SourceGame,
    cursor: str,
    reviews_per_page: int,
    timeout: float,
) -> dict[str, Any]:
    response = session.get(
        REVIEWS_ENDPOINT.format(appid=game.appid),
        params={
            "json": 1,
            "filter": "recent",
            "language": "all",
            "review_type": "all",
            "purchase_type": "all",
            "num_per_page": reviews_per_page,
            "cursor": cursor,
            "filter_offtopic_activity": 0,
        },
        timeout=timeout,
    )

    if response.status_code != requests.codes.ok:
        raise requests.HTTPError(
            f"Steam returned HTTP {response.status_code} for AppID {game.appid}.",
            response=response,
        )

    try:
        payload = response.json()
    except requests.JSONDecodeError as error:
        raise ValueError(
            f"Steam returned invalid JSON for AppID {game.appid}."
        ) from error

    if payload.get("success") != 1:
        raise ValueError(
            f"Steam review request was not successful for AppID {game.appid}."
        )

    return payload


def safe_int(value: Any) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def unix_timestamp_to_iso(value: Any) -> str:
    try:
        timestamp = int(value)
    except (TypeError, ValueError):
        return ""

    return datetime.fromtimestamp(timestamp, tz=timezone.utc).isoformat()


def normalize_review(
    review: dict[str, Any],
    game: SourceGame,
    collected_at: str,
) -> dict[str, Any] | None:
    author = review.get("author") or {}
    steamid = str(author.get("steamid", "")).strip()

    if not steamid.isdigit():
        return None

    return {
        "steamid": steamid,
        "source_appid": game.appid,
        "source_game": game.name,
        "sampling_stratum": game.sampling_stratum,
        "recommendation_id": str(review.get("recommendationid", "")),
        "review_language": str(review.get("language", "")),
        "review_positive": bool(review.get("voted_up", False)),
        "review_timestamp": unix_timestamp_to_iso(
            review.get("timestamp_created")
        ),
        "playtime_at_review_minutes": safe_int(
            author.get("playtime_at_review")
        ),
        "playtime_forever_minutes": safe_int(
            author.get("playtime_forever")
        ),
        "steam_purchase": bool(review.get("steam_purchase", False)),
        "received_for_free": bool(review.get("received_for_free", False)),
        "written_during_early_access": bool(
            review.get("written_during_early_access", False)
        ),
        "collected_at": collected_at,
    }


def collect_candidates(
    games: list[SourceGame],
    session: requests.Session,
    max_per_game: int,
    reviews_per_page: int,
    max_pages_per_game: int,
    request_delay: float,
    timeout: float,
) -> tuple[list[dict[str, Any]], dict[int, GameCollectionState]]:
    """Collect candidates from every configured game before sampling.

    The previous implementation stopped immediately when the global target was
    reached. This could end the run in the middle of a round and exclude games
    placed later in the configuration. Here, each configured game receives the
    same opportunity to contribute up to max_per_game candidates.
    """

    states = {game.appid: GameCollectionState() for game in games}
    rows: list[dict[str, Any]] = []
    steamids_by_game: dict[int, set[str]] = defaultdict(set)
    cursors_seen_by_game: dict[int, set[str]] = defaultdict(set)
    collected_at = datetime.now(timezone.utc).isoformat()

    LOGGER.info(
        "Collecting candidates from %d source games, up to %d per game.",
        len(games),
        max_per_game,
    )

    while True:
        active_games = 0
        progress_in_round = False

        for game in games:
            state = states[game.appid]

            if state.exhausted:
                continue
            if state.accepted_rows >= max_per_game:
                state.exhausted = True
                continue
            if state.pages_requested >= max_pages_per_game:
                state.exhausted = True
                continue

            active_games += 1

            if state.cursor in cursors_seen_by_game[game.appid]:
                state.exhausted = True
                state.error = "Repeated cursor detected."
                continue

            cursors_seen_by_game[game.appid].add(state.cursor)

            try:
                payload = fetch_review_page(
                    session=session,
                    game=game,
                    cursor=state.cursor,
                    reviews_per_page=reviews_per_page,
                    timeout=timeout,
                )
            except (requests.RequestException, ValueError) as error:
                state.exhausted = True
                state.error = str(error)
                LOGGER.error(
                    "Collection failed for %s (%d): %s",
                    game.name,
                    game.appid,
                    error,
                )
                continue

            state.pages_requested += 1
            reviews = payload.get("reviews") or []
            state.reviews_received += len(reviews)

            if not reviews:
                state.exhausted = True
                continue

            for review in reviews:
                if state.accepted_rows >= max_per_game:
                    break

                row = normalize_review(review, game, collected_at)
                if row is None:
                    continue

                steamid = str(row["steamid"])
                if steamid in steamids_by_game[game.appid]:
                    continue

                steamids_by_game[game.appid].add(steamid)
                rows.append(row)
                state.accepted_rows += 1
                progress_in_round = True

            next_cursor = str(payload.get("cursor", "")).strip()
            if not next_cursor or next_cursor == state.cursor:
                state.exhausted = True
                if next_cursor == state.cursor:
                    state.error = "API returned the current cursor again."
            else:
                state.cursor = next_cursor

            if state.accepted_rows >= max_per_game:
                state.exhausted = True

            LOGGER.info(
                "%s: accepted=%d/%d, pages=%d.",
                game.name,
                state.accepted_rows,
                max_per_game,
                state.pages_requested,
            )

            if request_delay > 0:
                time.sleep(request_delay)

        if active_games == 0:
            break
        if not progress_in_round:
            LOGGER.warning(
                "Candidate collection stopped because no new authors were "
                "obtained during the last complete round."
            )
            break

    return rows, states


def calculate_stratum_quotas(
    strata: list[str],
    target_unique: int,
) -> dict[str, int]:
    """Split the target as evenly as possible across sampling strata."""

    ordered = sorted(set(strata))
    base = target_unique // len(ordered)
    remainder = target_unique % len(ordered)

    return {
        stratum: base + (1 if index < remainder else 0)
        for index, stratum in enumerate(ordered)
    }


def select_balanced_unique_sample(
    raw_rows: list[dict[str, Any]],
    games: list[SourceGame],
    target_unique: int,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    """Select unique SteamIDs fairly across strata and source games.

    Selection is deterministic:
    - target is divided equally between sampling strata;
    - inside each stratum, candidates are taken round-robin from its games;
    - a SteamID can enter the selected sample only once;
    - if one stratum lacks candidates, remaining capacity is redistributed.
    """

    rows_by_game: dict[int, deque[dict[str, Any]]] = defaultdict(deque)
    all_sources_by_steamid: dict[str, set[str]] = defaultdict(set)
    all_strata_by_steamid: dict[str, set[str]] = defaultdict(set)

    for row in raw_rows:
        appid = int(row["source_appid"])
        steamid = str(row["steamid"])
        rows_by_game[appid].append(row)
        all_sources_by_steamid[steamid].add(str(appid))
        all_strata_by_steamid[steamid].add(str(row["sampling_stratum"]))

    games_by_stratum: dict[str, list[SourceGame]] = defaultdict(list)
    for game in games:
        games_by_stratum[game.sampling_stratum].append(game)

    quotas = calculate_stratum_quotas(
        list(games_by_stratum),
        target_unique,
    )

    selected: list[dict[str, Any]] = []
    selected_ids: set[str] = set()
    selected_by_stratum: Counter[str] = Counter()
    selected_by_game: Counter[int] = Counter()

    def take_one_from_game(game: SourceGame) -> bool:
        queue = rows_by_game[game.appid]

        while queue:
            row = queue.popleft()
            steamid = str(row["steamid"])

            if steamid in selected_ids:
                continue

            selected_ids.add(steamid)
            selected_by_stratum[game.sampling_stratum] += 1
            selected_by_game[game.appid] += 1

            source_appids = sorted(
                all_sources_by_steamid[steamid],
                key=int,
            )
            sampling_strata = sorted(all_strata_by_steamid[steamid])

            selected.append(
                {
                    "steamid": steamid,
                    "primary_sampling_stratum": game.sampling_stratum,
                    "primary_source_appid": game.appid,
                    "source_count": len(source_appids),
                    "source_appids": ";".join(source_appids),
                    "sampling_strata": ";".join(sampling_strata),
                }
            )
            return True

        return False

    # First pass: satisfy equal stratum quotas.
    for stratum in sorted(games_by_stratum):
        stratum_games = games_by_stratum[stratum]

        while selected_by_stratum[stratum] < quotas[stratum]:
            progress = False

            for game in stratum_games:
                if selected_by_stratum[stratum] >= quotas[stratum]:
                    break
                if take_one_from_game(game):
                    progress = True

            if not progress:
                break

    # Second pass: redistribute unused capacity across any remaining games.
    while len(selected) < target_unique:
        progress = False

        for stratum in sorted(games_by_stratum):
            for game in games_by_stratum[stratum]:
                if len(selected) >= target_unique:
                    break
                if take_one_from_game(game):
                    progress = True

        if not progress:
            break

    diagnostics = {
        "requested_target": target_unique,
        "selected_unique": len(selected),
        "stratum_quotas": quotas,
        "selected_by_stratum": dict(sorted(selected_by_stratum.items())),
        "selected_by_game": {
            str(appid): selected_by_game[appid]
            for appid in sorted(selected_by_game)
        },
        "selection_strategy": (
            "equal quotas by sampling stratum, round-robin by source game, "
            "followed by redistribution of unused capacity"
        ),
    }

    return selected, diagnostics


def write_csv(
    output_path: Path,
    rows: list[dict[str, Any]],
    fieldnames: list[str],
) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = output_path.with_suffix(output_path.suffix + ".tmp")

    with temporary_path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    temporary_path.replace(output_path)


def write_json(output_path: Path, payload: dict[str, Any]) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = output_path.with_suffix(output_path.suffix + ".tmp")

    with temporary_path.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temporary_path.replace(output_path)


def build_summary(
    games: list[SourceGame],
    raw_rows: list[dict[str, Any]],
    unique_rows: list[dict[str, Any]],
    selection_diagnostics: dict[str, Any],
    states: dict[int, GameCollectionState],
    args: argparse.Namespace,
    started_at: datetime,
    finished_at: datetime,
) -> dict[str, Any]:
    candidate_unique_ids = {str(row["steamid"]) for row in raw_rows}
    candidate_rows_by_stratum = Counter(
        str(row["sampling_stratum"]) for row in raw_rows
    )
    candidate_unique_by_stratum: dict[str, set[str]] = defaultdict(set)

    for row in raw_rows:
        candidate_unique_by_stratum[str(row["sampling_stratum"])].add(
            str(row["steamid"])
        )

    per_game = []
    for game in games:
        state = states[game.appid]
        per_game.append(
            {
                "appid": game.appid,
                "name": game.name,
                "sampling_stratum": game.sampling_stratum,
                "pages_requested": state.pages_requested,
                "reviews_received": state.reviews_received,
                "accepted_rows": state.accepted_rows,
                "exhausted": state.exhausted,
                "error": state.error,
            }
        )

    return {
        "pipeline_stage": "01_collect_steamids",
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
        },
        "requested_games": len(games),
        "successful_games": sum(
            1
            for state in states.values()
            if state.pages_requested > 0 and state.error is None
        ),
        "failed_games": sum(
            1 for state in states.values() if state.error is not None
        ),
        "reviews_received": sum(
            state.reviews_received for state in states.values()
        ),
        "candidate_source_rows": len(raw_rows),
        "candidate_unique_steamids": len(candidate_unique_ids),
        "duplicated_source_occurrences": (
            len(raw_rows) - len(candidate_unique_ids)
        ),
        "selected_unique_steamids": len(unique_rows),
        "target_reached": len(unique_rows) >= args.target_unique,
        "candidate_rows_by_stratum": dict(
            sorted(candidate_rows_by_stratum.items())
        ),
        "candidate_unique_by_stratum": {
            stratum: len(steamids)
            for stratum, steamids in sorted(
                candidate_unique_by_stratum.items()
            )
        },
        "selection": selection_diagnostics,
        "games": per_game,
    }


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        games = load_source_games(args.config)
        session = create_http_session()

        raw_rows, states = collect_candidates(
            games=games,
            session=session,
            max_per_game=args.max_per_game,
            reviews_per_page=args.reviews_per_page,
            max_pages_per_game=args.max_pages_per_game,
            request_delay=args.request_delay,
            timeout=args.timeout,
        )

        unique_rows, selection_diagnostics = select_balanced_unique_sample(
            raw_rows=raw_rows,
            games=games,
            target_unique=args.target_unique,
        )

        write_csv(
            output_path=args.raw_output,
            rows=raw_rows,
            fieldnames=RAW_FIELDNAMES,
        )
        write_csv(
            output_path=args.unique_output,
            rows=unique_rows,
            fieldnames=UNIQUE_FIELDNAMES,
        )

        finished_at = datetime.now(timezone.utc)
        summary = build_summary(
            games=games,
            raw_rows=raw_rows,
            unique_rows=unique_rows,
            selection_diagnostics=selection_diagnostics,
            states=states,
            args=args,
            started_at=started_at,
            finished_at=finished_at,
        )
        write_json(args.summary_output, summary)

    except (OSError, ValueError, requests.RequestException) as error:
        LOGGER.exception("SteamID collection failed: %s", error)
        return 1

    LOGGER.info(
        "Collection completed: %d candidate rows and %d selected SteamIDs.",
        len(raw_rows),
        len(unique_rows),
    )
    LOGGER.info("Raw output: %s", args.raw_output)
    LOGGER.info("Selected output: %s", args.unique_output)
    LOGGER.info("Summary output: %s", args.summary_output)

    if len(unique_rows) < args.target_unique:
        LOGGER.warning(
            "The requested target was not reached: %d/%d.",
            len(unique_rows),
            args.target_unique,
        )
        return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())