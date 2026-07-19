"""Fetch metadata for the targeted final AppID expansion plan.

This stage reuses src/03_fetch_game_metadata.py but reads AppIDs directly from
data/interim/final_metadata_collection_plan.csv.

It:
- preserves the existing game_metadata_raw.csv cache;
- processes only AppIDs listed in the targeted plan;
- supports checkpoints and resume;
- records results specifically for the final expansion;
- keeps Steam Store and SteamSpy provenance separate.
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import json
import logging
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PLAN = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "final_metadata_collection_plan.csv"
)
DEFAULT_BASE_SCRIPT = (
    PROJECT_ROOT / "src" / "03_fetch_game_metadata.py"
)
DEFAULT_OUTPUT = (
    PROJECT_ROOT / "data" / "raw" / "game_metadata_raw.csv"
)
DEFAULT_SUMMARY = (
    PROJECT_ROOT
    / "reports"
    / "metrics"
    / "06_targeted_metadata_expansion_summary.json"
)

LOGGER = logging.getLogger("targeted-metadata-expansion")


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Fetch Steam metadata for AppIDs selected by the final "
            "targeted metadata expansion plan."
        )
    )
    parser.add_argument("--plan", type=Path, default=DEFAULT_PLAN)
    parser.add_argument(
        "--base-script",
        type=Path,
        default=DEFAULT_BASE_SCRIPT,
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--summary-output",
        type=Path,
        default=DEFAULT_SUMMARY,
    )
    parser.add_argument("--max-apps", type=int, default=None)
    parser.add_argument("--store-delay", type=float, default=1.0)
    parser.add_argument("--steamspy-delay", type=float, default=1.0)
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--checkpoint-every", type=int, default=25)
    parser.add_argument("--no-steamspy", action="store_true")
    parser.add_argument("--retry-failed", action="store_true")
    parser.add_argument("--refresh-selected", action="store_true")

    args = parser.parse_args()

    if args.max_apps is not None and args.max_apps <= 0:
        parser.error("--max-apps must be positive.")
    if args.store_delay < 0 or args.steamspy_delay < 0:
        parser.error("Request delays cannot be negative.")
    if args.timeout <= 0:
        parser.error("--timeout must be positive.")
    if args.checkpoint_every <= 0:
        parser.error("--checkpoint-every must be positive.")

    return args


def text(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def import_base_module(path: Path):
    if not path.exists():
        raise FileNotFoundError(path)

    spec = importlib.util.spec_from_file_location(
        "adaptive_trials_base_game_metadata",
        path,
    )
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not import base collector: {path}")

    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def load_planned_appids(
    path: Path,
    max_apps: int | None,
) -> tuple[list[int], dict[int, dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)

    appids: list[int] = []
    plan_rows: dict[int, dict[str, str]] = {}
    seen: set[int] = set()

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)

        if reader.fieldnames is None or "appid" not in reader.fieldnames:
            raise ValueError("Metadata plan must contain an appid column.")

        for row in reader:
            appid = safe_int(row.get("appid"))
            if appid <= 0 or appid in seen:
                continue

            seen.add(appid)
            appids.append(appid)
            plan_rows[appid] = dict(row)

            if max_apps is not None and len(appids) >= max_apps:
                break

    if not appids:
        raise ValueError("No valid AppIDs were found in the metadata plan.")

    return appids, plan_rows


def write_json_atomic(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")

    with temporary.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temporary.replace(path)


def complete_status(status: str) -> bool:
    return status in {
        "success",
        "not_found",
        "non_game",
        "unavailable",
        "skipped",
    }


def should_skip_existing(
    row: dict[str, str],
    no_steamspy: bool,
    retry_failed: bool,
) -> bool:
    if not retry_failed:
        return True

    store_ok = complete_status(text(row.get("store_status")))
    steamspy_ok = (
        no_steamspy
        or complete_status(text(row.get("steamspy_status")))
    )
    return store_ok and steamspy_ok


def main() -> int:
    configure_logging()
    args = parse_args()
    started = datetime.now(timezone.utc)

    try:
        base = import_base_module(args.base_script)
        selected_appids, plan_rows = load_planned_appids(
            args.plan,
            args.max_apps,
        )

        existing = base.load_existing_rows(args.output)
        cached_before = sum(
            appid in existing for appid in selected_appids
        )
        rows: dict[int, dict[str, Any]] = dict(existing)
        session = base.create_session()
        processed = 0
        skipped_cached = 0
        total = len(selected_appids)

        LOGGER.info(
            "Selected %d planned AppIDs; %d already cached.",
            total,
            cached_before,
        )

        for index, appid in enumerate(selected_appids, start=1):
            if (
                appid in rows
                and not args.refresh_selected
                and should_skip_existing(
                    rows[appid],
                    args.no_steamspy,
                    args.retry_failed,
                )
            ):
                skipped_cached += 1
                LOGGER.info(
                    "[%d/%d] AppID %d: cached.",
                    index,
                    total,
                    appid,
                )
                continue

            (
                store_status,
                store_data,
                store_http,
                store_error,
            ) = base.fetch_store(
                session,
                appid,
                args.timeout,
            )

            if args.store_delay:
                base.time.sleep(args.store_delay)

            if args.no_steamspy:
                (
                    steamspy_status,
                    steamspy_data,
                    steamspy_http,
                    steamspy_error,
                ) = ("skipped", {}, None, "")
            else:
                (
                    steamspy_status,
                    steamspy_data,
                    steamspy_http,
                    steamspy_error,
                ) = base.fetch_steamspy(
                    session,
                    appid,
                    args.timeout,
                )

                if args.steamspy_delay:
                    base.time.sleep(args.steamspy_delay)

            rows[appid] = base.build_row(
                appid,
                store_status,
                store_data,
                store_http,
                store_error,
                steamspy_status,
                steamspy_data,
                steamspy_http,
                steamspy_error,
            )
            processed += 1

            LOGGER.info(
                "[%d/%d] AppID %d: store=%s, steamspy=%s.",
                index,
                total,
                appid,
                store_status,
                steamspy_status,
            )

            if processed % args.checkpoint_every == 0:
                base.write_csv_atomic(args.output, rows)
                LOGGER.info(
                    "Checkpoint saved after %d processed AppIDs.",
                    processed,
                )

        base.write_csv_atomic(args.output, rows)

        selected_rows = [
            rows[appid]
            for appid in selected_appids
            if appid in rows
        ]

        store_counts = Counter(
            text(row.get("store_status"))
            for row in selected_rows
        )
        steamspy_counts = Counter(
            text(row.get("steamspy_status"))
            for row in selected_rows
        )

        with_genres = sum(
            text(row.get("genres_json")) not in {"", "[]"}
            for row in selected_rows
        )
        with_categories = sum(
            text(row.get("categories_json")) not in {"", "[]"}
            for row in selected_rows
        )
        with_tags = sum(
            text(row.get("steamspy_tags_json")) not in {"", "{}"}
            for row in selected_rows
        )

        failed_appids = [
            {
                "appid": appid,
                "priority_rank": safe_int(
                    plan_rows[appid].get("priority_rank")
                ),
                "selection_reason": text(
                    plan_rows[appid].get("selection_reason")
                ),
                "store_status": text(
                    rows.get(appid, {}).get("store_status")
                ),
                "steamspy_status": text(
                    rows.get(appid, {}).get("steamspy_status")
                ),
                "store_error": text(
                    rows.get(appid, {}).get("store_error")
                ),
                "steamspy_error": text(
                    rows.get(appid, {}).get("steamspy_error")
                ),
            }
            for appid in selected_appids
            if (
                appid not in rows
                or text(rows[appid].get("store_status"))
                in {"request_error", "invalid_response"}
                or (
                    not args.no_steamspy
                    and text(rows[appid].get("steamspy_status"))
                    in {"request_error", "invalid_response"}
                )
            )
        ]

        finished = datetime.now(timezone.utc)
        total_available = len(selected_rows)

        summary = {
            "pipeline_stage": "06_fetch_targeted_metadata_expansion",
            "started_at": started.isoformat(),
            "finished_at": finished.isoformat(),
            "duration_seconds": round(
                (finished - started).total_seconds(),
                3,
            ),
            "parameters": {
                "max_apps": args.max_apps,
                "store_delay": args.store_delay,
                "steamspy_delay": args.steamspy_delay,
                "timeout": args.timeout,
                "checkpoint_every": args.checkpoint_every,
                "steamspy_enabled": not args.no_steamspy,
                "retry_failed": args.retry_failed,
                "refresh_selected": args.refresh_selected,
            },
            "planned_unique_appids": len(selected_appids),
            "cached_before_run": cached_before,
            "skipped_cached": skipped_cached,
            "processed_this_run": processed,
            "planned_rows_available_after_run": total_available,
            "store_status_counts": dict(
                sorted(store_counts.items())
            ),
            "steamspy_status_counts": dict(
                sorted(steamspy_counts.items())
            ),
            "games_with_genres": with_genres,
            "games_with_categories": with_categories,
            "games_with_steamspy_tags": with_tags,
            "genre_coverage_rate": round(
                with_genres / total_available,
                6,
            )
            if total_available
            else 0.0,
            "category_coverage_rate": round(
                with_categories / total_available,
                6,
            )
            if total_available
            else 0.0,
            "tag_coverage_rate": round(
                with_tags / total_available,
                6,
            )
            if total_available
            else 0.0,
            "failed_appids": failed_appids,
            "failed_appid_count": len(failed_appids),
            "output_cache_rows_after_run": len(rows),
            "methodological_note": (
                "Only AppIDs selected by the targeted expansion plan are "
                "processed. Existing metadata is preserved, and sampling "
                "priority remains traceable through the plan CSV."
            ),
        }

        write_json_atomic(args.summary_output, summary)

    except (
        OSError,
        ValueError,
        ImportError,
    ) as error:
        LOGGER.exception(
            "Targeted metadata collection failed: %s",
            error,
        )
        return 1

    LOGGER.info(
        "Targeted metadata collection completed: planned=%d, "
        "processed=%d, cached=%d.",
        len(selected_appids),
        processed,
        skipped_cached,
    )
    LOGGER.info("Metadata output: %s", args.output)
    LOGGER.info("Summary output: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())