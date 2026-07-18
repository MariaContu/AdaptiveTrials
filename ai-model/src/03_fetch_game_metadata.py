"""Fetch metadata for unique Steam AppIDs used by valid player profiles.

Sources:
- Steam Store appdetails endpoint: basic product information, type, genres,
  categories, developers, publishers and release data.
- SteamSpy appdetails endpoint: complementary community tag weights.

The script supports checkpoints and resume so long executions can continue
without restarting from the beginning.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import sys
import time
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GAMES_INPUT = PROJECT_ROOT / "data" / "raw" / "player_games_raw.csv"
DEFAULT_STATUS_INPUT = PROJECT_ROOT / "data" / "interim" / "player_library_status.csv"
DEFAULT_OUTPUT = PROJECT_ROOT / "data" / "raw" / "game_metadata_raw.csv"
DEFAULT_SUMMARY_OUTPUT = PROJECT_ROOT / "reports" / "metrics" / "03_game_metadata_summary.json"

STORE_ENDPOINT = "https://store.steampowered.com/api/appdetails"
STEAMSPY_ENDPOINT = "https://steamspy.com/api.php"
LOGGER = logging.getLogger("game-metadata-collector")

OUTPUT_FIELDNAMES = [
    "appid", "store_status", "steamspy_status", "name", "app_type", "is_free",
    "genres_json", "categories_json", "developers_json", "publishers_json",
    "release_date", "coming_soon", "required_age", "supported_languages",
    "short_description", "steamspy_tags_json", "steamspy_genre",
    "steamspy_owners", "steamspy_average_forever_minutes",
    "steamspy_median_forever_minutes", "steamspy_positive", "steamspy_negative",
    "store_http_status", "steamspy_http_status", "store_error", "steamspy_error",
    "collected_at",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Fetch Steam metadata for unique AppIDs.")
    parser.add_argument("--games-input", type=Path, default=DEFAULT_GAMES_INPUT)
    parser.add_argument("--status-input", type=Path, default=DEFAULT_STATUS_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY_OUTPUT)
    parser.add_argument("--max-apps", type=int, default=None)
    parser.add_argument("--store-delay", type=float, default=1.0)
    parser.add_argument("--steamspy-delay", type=float, default=1.0)
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--checkpoint-every", type=int, default=25)
    parser.add_argument("--no-steamspy", action="store_true")
    parser.add_argument("--retry-failed", action="store_true")
    parser.add_argument("--refresh-all", action="store_true")
    args = parser.parse_args()

    if args.max_apps is not None and args.max_apps <= 0:
        parser.error("--max-apps must be greater than zero.")
    if args.store_delay < 0 or args.steamspy_delay < 0:
        parser.error("Request delays cannot be negative.")
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero.")
    if args.checkpoint_every <= 0:
        parser.error("--checkpoint-every must be greater than zero.")
    return args


def configure_logging() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")


def safe_int(value: Any) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return 0


def text(value: Any) -> str:
    return "" if value is None else str(value).strip()


def json_text(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True)


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
    session = requests.Session()
    session.mount("https://", HTTPAdapter(max_retries=retry))
    session.headers.update({"User-Agent": "AdaptiveTrials-AcademicResearch/1.0"})
    return session


def load_valid_player_ids(path: Path) -> set[str]:
    if not path.exists():
        raise FileNotFoundError(f"Status input not found: {path}")
    valid: set[str] = set()
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        required = {"player_id", "status"}
        if reader.fieldnames is None or not required.issubset(reader.fieldnames):
            raise ValueError(f"Status CSV must contain: {sorted(required)}")
        for row in reader:
            if text(row.get("status")) == "valid":
                player_id = text(row.get("player_id"))
                if player_id:
                    valid.add(player_id)
    if not valid:
        raise ValueError("No valid players were found.")
    return valid


def load_unique_appids(path: Path, valid_players: set[str], max_apps: int | None) -> list[int]:
    if not path.exists():
        raise FileNotFoundError(f"Games input not found: {path}")
    appids: set[int] = set()
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        required = {"player_id", "appid"}
        if reader.fieldnames is None or not required.issubset(reader.fieldnames):
            raise ValueError(f"Games CSV must contain: {sorted(required)}")
        for row in reader:
            if text(row.get("player_id")) not in valid_players:
                continue
            appid = safe_int(row.get("appid"))
            if appid > 0:
                appids.add(appid)
    ordered = sorted(appids)
    if max_apps is not None:
        ordered = ordered[:max_apps]
    if not ordered:
        raise ValueError("No valid AppIDs were found.")
    return ordered


def load_existing_rows(path: Path) -> dict[int, dict[str, str]]:
    if not path.exists():
        return {}
    rows: dict[int, dict[str, str]] = {}
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None or "appid" not in reader.fieldnames:
            raise ValueError(f"Invalid existing metadata CSV: {path}")
        for row in reader:
            appid = safe_int(row.get("appid"))
            if appid > 0:
                rows[appid] = {field: text(row.get(field)) for field in OUTPUT_FIELDNAMES}
    return rows


def complete_status(status: str) -> bool:
    return status in {"success", "not_found", "non_game", "unavailable", "skipped"}


def should_skip(row: dict[str, str], no_steamspy: bool, retry_failed: bool) -> bool:
    if not retry_failed:
        return True
    store_ok = complete_status(text(row.get("store_status")))
    steamspy_ok = no_steamspy or complete_status(text(row.get("steamspy_status")))
    return store_ok and steamspy_ok


def fetch_store(session: requests.Session, appid: int, timeout: float) -> tuple[str, dict[str, Any], int | None, str]:
    try:
        response = session.get(
            STORE_ENDPOINT,
            params={"appids": appid, "l": "english", "cc": "us"},
            timeout=timeout,
        )
    except requests.RequestException as error:
        return "request_error", {}, None, str(error)

    if response.status_code != 200:
        return "request_error", {}, response.status_code, f"HTTP {response.status_code}"
    try:
        payload = response.json()
    except requests.JSONDecodeError as error:
        return "invalid_response", {}, response.status_code, f"Invalid JSON: {error}"

    app_payload = payload.get(str(appid))
    if not isinstance(app_payload, dict):
        return "invalid_response", {}, response.status_code, "AppID key missing."
    if app_payload.get("success") is not True:
        return "not_found", {}, response.status_code, ""

    data = app_payload.get("data")
    if not isinstance(data, dict):
        return "invalid_response", {}, response.status_code, "No data object."

    app_type = text(data.get("type")).lower()
    if app_type and app_type != "game":
        return "non_game", data, response.status_code, ""
    return "success", data, response.status_code, ""


def fetch_steamspy(session: requests.Session, appid: int, timeout: float) -> tuple[str, dict[str, Any], int | None, str]:
    try:
        response = session.get(
            STEAMSPY_ENDPOINT,
            params={"request": "appdetails", "appid": appid},
            timeout=timeout,
        )
    except requests.RequestException as error:
        return "request_error", {}, None, str(error)

    if response.status_code != 200:
        return "request_error", {}, response.status_code, f"HTTP {response.status_code}"
    try:
        payload = response.json()
    except requests.JSONDecodeError as error:
        return "invalid_response", {}, response.status_code, f"Invalid JSON: {error}"
    if not isinstance(payload, dict):
        return "invalid_response", {}, response.status_code, "Response is not an object."
    if safe_int(payload.get("appid")) != appid:
        return "not_found", payload, response.status_code, ""
    return "success", payload, response.status_code, ""


def descriptions(values: Any) -> list[str]:
    if not isinstance(values, list):
        return []
    result = {
        text(value.get("description"))
        for value in values
        if isinstance(value, dict) and text(value.get("description"))
    }
    return sorted(result)


def strings(values: Any) -> list[str]:
    if not isinstance(values, list):
        return []
    return sorted({text(value) for value in values if text(value)})


def build_row(
    appid: int,
    store_status: str,
    store_data: dict[str, Any],
    store_http: int | None,
    store_error: str,
    steamspy_status: str,
    steamspy_data: dict[str, Any],
    steamspy_http: int | None,
    steamspy_error: str,
) -> dict[str, Any]:
    release = store_data.get("release_date")
    if not isinstance(release, dict):
        release = {}
    tags = steamspy_data.get("tags")
    if not isinstance(tags, dict):
        tags = {}

    return {
        "appid": appid,
        "store_status": store_status,
        "steamspy_status": steamspy_status,
        "name": text(store_data.get("name") or steamspy_data.get("name")),
        "app_type": text(store_data.get("type")),
        "is_free": bool(store_data.get("is_free", False)),
        "genres_json": json_text(descriptions(store_data.get("genres"))),
        "categories_json": json_text(descriptions(store_data.get("categories"))),
        "developers_json": json_text(strings(store_data.get("developers"))),
        "publishers_json": json_text(strings(store_data.get("publishers"))),
        "release_date": text(release.get("date")),
        "coming_soon": bool(release.get("coming_soon", False)),
        "required_age": safe_int(store_data.get("required_age")),
        "supported_languages": text(store_data.get("supported_languages")),
        "short_description": text(store_data.get("short_description")),
        "steamspy_tags_json": json_text({text(k): safe_int(v) for k, v in tags.items() if text(k)}),
        "steamspy_genre": text(steamspy_data.get("genre")),
        "steamspy_owners": text(steamspy_data.get("owners")),
        "steamspy_average_forever_minutes": safe_int(steamspy_data.get("average_forever")),
        "steamspy_median_forever_minutes": safe_int(steamspy_data.get("median_forever")),
        "steamspy_positive": safe_int(steamspy_data.get("positive")),
        "steamspy_negative": safe_int(steamspy_data.get("negative")),
        "store_http_status": "" if store_http is None else store_http,
        "steamspy_http_status": "" if steamspy_http is None else steamspy_http,
        "store_error": store_error,
        "steamspy_error": steamspy_error,
        "collected_at": datetime.now(timezone.utc).isoformat(),
    }


def write_csv_atomic(path: Path, rows: dict[int, dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=OUTPUT_FIELDNAMES)
        writer.writeheader()
        for appid in sorted(rows):
            writer.writerow(rows[appid])
    temporary.replace(path)


def write_json_atomic(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)
    temporary.replace(path)


def build_summary(
    selected_appids: list[int],
    rows: dict[int, dict[str, Any]],
    processed: int,
    cached: int,
    args: argparse.Namespace,
    started: datetime,
    finished: datetime,
) -> dict[str, Any]:
    selected = [rows[appid] for appid in selected_appids if appid in rows]
    store_counts = Counter(text(row.get("store_status")) for row in selected)
    steamspy_counts = Counter(text(row.get("steamspy_status")) for row in selected)
    genres = sum(text(row.get("genres_json")) not in {"", "[]"} for row in selected)
    categories = sum(text(row.get("categories_json")) not in {"", "[]"} for row in selected)
    tags = sum(text(row.get("steamspy_tags_json")) not in {"", "{}"} for row in selected)
    total = len(selected)

    return {
        "pipeline_stage": "03_fetch_game_metadata",
        "started_at": started.isoformat(),
        "finished_at": finished.isoformat(),
        "duration_seconds": round((finished - started).total_seconds(), 3),
        "parameters": {
            "max_apps": args.max_apps,
            "store_delay": args.store_delay,
            "steamspy_delay": args.steamspy_delay,
            "timeout": args.timeout,
            "checkpoint_every": args.checkpoint_every,
            "steamspy_enabled": not args.no_steamspy,
            "retry_failed": args.retry_failed,
            "refresh_all": args.refresh_all,
        },
        "selected_unique_appids": len(selected_appids),
        "cached_rows_before_run": cached,
        "processed_this_run": processed,
        "rows_available_after_run": total,
        "store_status_counts": dict(sorted(store_counts.items())),
        "steamspy_status_counts": dict(sorted(steamspy_counts.items())),
        "games_with_genres": genres,
        "games_with_categories": categories,
        "games_with_steamspy_tags": tags,
        "genre_coverage_rate": round(genres / total, 6) if total else 0.0,
        "category_coverage_rate": round(categories / total, 6) if total else 0.0,
        "tag_coverage_rate": round(tags / total, 6) if total else 0.0,
        "source_notes": {
            "steam_store": "Public appdetails endpoint used for store metadata; responses are validated and cached.",
            "steamspy": "Third-party complementary source used for tag weights; provenance is stored separately.",
        },
    }


def main() -> int:
    configure_logging()
    args = parse_args()
    started = datetime.now(timezone.utc)

    try:
        valid_players = load_valid_player_ids(args.status_input)
        selected_appids = load_unique_appids(args.games_input, valid_players, args.max_apps)
        existing = {} if args.refresh_all else load_existing_rows(args.output)
        cached = sum(appid in existing for appid in selected_appids)
        rows: dict[int, dict[str, Any]] = dict(existing)
        session = create_session()
        processed = 0
        total = len(selected_appids)

        LOGGER.info("Selected %d unique AppIDs; %d already cached.", total, cached)

        for index, appid in enumerate(selected_appids, start=1):
            if appid in rows and not args.refresh_all and should_skip(rows[appid], args.no_steamspy, args.retry_failed):
                LOGGER.info("[%d/%d] AppID %d: cached.", index, total, appid)
                continue

            store_status, store_data, store_http, store_error = fetch_store(session, appid, args.timeout)
            if args.store_delay:
                time.sleep(args.store_delay)

            if args.no_steamspy:
                steamspy_status, steamspy_data, steamspy_http, steamspy_error = "skipped", {}, None, ""
            else:
                steamspy_status, steamspy_data, steamspy_http, steamspy_error = fetch_steamspy(session, appid, args.timeout)
                if args.steamspy_delay:
                    time.sleep(args.steamspy_delay)

            rows[appid] = build_row(
                appid, store_status, store_data, store_http, store_error,
                steamspy_status, steamspy_data, steamspy_http, steamspy_error,
            )
            processed += 1
            LOGGER.info("[%d/%d] AppID %d: store=%s, steamspy=%s.", index, total, appid, store_status, steamspy_status)

            if processed % args.checkpoint_every == 0:
                write_csv_atomic(args.output, rows)
                LOGGER.info("Checkpoint saved after %d new AppIDs.", processed)

        write_csv_atomic(args.output, rows)
        finished = datetime.now(timezone.utc)
        write_json_atomic(
            args.summary_output,
            build_summary(selected_appids, rows, processed, cached, args, started, finished),
        )

    except (OSError, ValueError, requests.RequestException) as error:
        LOGGER.exception("Game metadata collection failed: %s", error)
        return 1

    LOGGER.info("Metadata collection completed: selected=%d, processed=%d.", len(selected_appids), processed)
    LOGGER.info("Metadata output: %s", args.output)
    LOGGER.info("Summary output: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())