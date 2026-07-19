"""Build the final diversified general SteamID candidate sample.

The validated stage-01 collector performs the Steam review requests. This
wrapper runs it with expanded limits, removes IDs from previous pilots and the
final strategic sample, then creates the final balanced general selection.
Sampling origin is preserved for audit and granularity analysis, but never
becomes the player target.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import subprocess
import sys
import tempfile
from collections import Counter, defaultdict, deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PLAN = ROOT / "config" / "final_collection_plan.json"
DEFAULT_CONFIG = ROOT / "config" / "source_games.json"
DEFAULT_BASE_SCRIPT = ROOT / "src" / "01_collect_steamids.py"
DEFAULT_RAW = ROOT / "data" / "raw" / "final_general_review_authors_raw.csv"
DEFAULT_SELECTED = ROOT / "data" / "interim" / "final_general_steamids_candidates.csv"
DEFAULT_SUMMARY = ROOT / "reports" / "metrics" / "06_general_collection_summary.json"
DEFAULT_EXCLUSIONS = [
    ROOT / "data" / "interim" / "steamids_unique.csv",
    ROOT / "data" / "interim" / "puzzle_steamids_candidates.csv",
    ROOT / "data" / "interim" / "strategic_reasoning_steamids_candidates.csv",
    ROOT / "data" / "interim" / "final_strategic_steamids_candidates.csv",
]

SELECTED_FIELDS = [
    "candidate_id", "steamid", "primary_sampling_stratum",
    "primary_source_appid", "primary_source_game", "source_count",
    "source_appids", "source_games", "sampling_strata", "collection_stage",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Collect the final diversified general SteamID sample."
    )
    parser.add_argument("--plan", type=Path, default=DEFAULT_PLAN)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--base-script", type=Path, default=DEFAULT_BASE_SCRIPT)
    parser.add_argument("--exclude-ids", type=Path, action="append", default=None)
    parser.add_argument("--raw-output", type=Path, default=DEFAULT_RAW)
    parser.add_argument("--selected-output", type=Path, default=DEFAULT_SELECTED)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--reviews-per-page", type=int, default=100)
    parser.add_argument("--max-pages-per-game", type=int, default=20)
    parser.add_argument("--request-delay", type=float, default=1.0)
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--collection-buffer", type=float, default=2.5)
    args = parser.parse_args()

    if not 1 <= args.reviews_per_page <= 100:
        parser.error("--reviews-per-page must be between 1 and 100.")
    if args.max_pages_per_game <= 0:
        parser.error("--max-pages-per-game must be greater than zero.")
    if args.collection_buffer < 1:
        parser.error("--collection-buffer must be at least 1.")
    if args.request_delay < 0 or args.timeout <= 0:
        parser.error("Invalid request delay or timeout.")

    if args.exclude_ids is None:
        args.exclude_ids = DEFAULT_EXCLUSIONS
    return args


def read_json(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as file:
        data = json.load(file)
    if not isinstance(data, dict):
        raise ValueError(f"Expected JSON object in {path}.")
    return data


def find_steamid_column(fields: list[str] | None) -> str:
    if not fields:
        raise ValueError("CSV has no header.")
    lookup = {field.casefold(): field for field in fields}
    for candidate in ("steamid", "source_steamid", "steam_id", "steamid64"):
        if candidate in lookup:
            return lookup[candidate]
    raise ValueError(f"SteamID column not found. Fields: {fields}")


def load_exclusions(paths: list[Path]) -> tuple[set[str], dict[str, int]]:
    excluded: set[str] = set()
    counts: dict[str, int] = {}
    for path in paths:
        resolved = path.resolve()
        if not resolved.exists():
            print(f"Warning: exclusion file not found: {resolved}")
            counts[str(resolved)] = 0
            continue
        with resolved.open(encoding="utf-8", newline="") as file:
            reader = csv.DictReader(file)
            column = find_steamid_column(reader.fieldnames)
            values = {
                str(row.get(column, "")).strip()
                for row in reader
                if str(row.get(column, "")).strip()
            }
        excluded.update(values)
        counts[str(resolved)] = len(values)
    return excluded, counts


def write_csv(path: Path, fields: list[str], rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    temporary.replace(path)


def write_json(path: Path, data: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as file:
        json.dump(data, file, ensure_ascii=False, indent=2)
    temporary.replace(path)


def calculate_quotas(strata: list[str], target: int) -> dict[str, int]:
    ordered = sorted(set(strata))
    if not ordered:
        raise ValueError("No sampling strata found.")
    base, remainder = divmod(target, len(ordered))
    return {
        stratum: base + (1 if index < remainder else 0)
        for index, stratum in enumerate(ordered)
    }


def balanced_select(
    rows: list[dict[str, str]],
    target: int,
    maximum_per_game: int,
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    by_game: dict[int, deque[dict[str, str]]] = defaultdict(deque)
    game_name: dict[int, str] = {}
    game_stratum: dict[int, str] = {}
    sources: dict[str, set[int]] = defaultdict(set)
    source_names: dict[str, set[str]] = defaultdict(set)
    strata_by_id: dict[str, set[str]] = defaultdict(set)

    for row in rows:
        steamid = row["steamid"].strip()
        appid = int(row["source_appid"])
        stratum = row["sampling_stratum"].strip()
        name = row["source_game"].strip()
        by_game[appid].append(row)
        game_name[appid] = name
        game_stratum[appid] = stratum
        sources[steamid].add(appid)
        source_names[steamid].add(name)
        strata_by_id[steamid].add(stratum)

    games_by_stratum: dict[str, list[int]] = defaultdict(list)
    for appid, stratum in game_stratum.items():
        games_by_stratum[stratum].append(appid)
    for appids in games_by_stratum.values():
        appids.sort()

    quotas = calculate_quotas(list(games_by_stratum), target)
    selected: list[dict[str, Any]] = []
    selected_ids: set[str] = set()
    selected_by_stratum: Counter[str] = Counter()
    selected_by_game: Counter[int] = Counter()

    def take(appid: int) -> bool:
        if selected_by_game[appid] >= maximum_per_game:
            return False
        queue = by_game[appid]
        while queue:
            row = queue.popleft()
            steamid = row["steamid"].strip()
            if steamid in selected_ids:
                continue
            selected_ids.add(steamid)
            stratum = game_stratum[appid]
            selected_by_stratum[stratum] += 1
            selected_by_game[appid] += 1
            selected.append({
                "candidate_id": "",
                "steamid": steamid,
                "primary_sampling_stratum": stratum,
                "primary_source_appid": appid,
                "primary_source_game": game_name[appid],
                "source_count": len(sources[steamid]),
                "source_appids": ";".join(map(str, sorted(sources[steamid]))),
                "source_games": ";".join(sorted(source_names[steamid])),
                "sampling_strata": ";".join(sorted(strata_by_id[steamid])),
                "collection_stage": "06_final_general_collection",
            })
            return True
        return False

    for stratum in sorted(games_by_stratum):
        while selected_by_stratum[stratum] < quotas[stratum]:
            progress = False
            for appid in games_by_stratum[stratum]:
                if selected_by_stratum[stratum] >= quotas[stratum]:
                    break
                progress = take(appid) or progress
            if not progress:
                break

    while len(selected) < target:
        progress = False
        for stratum in sorted(games_by_stratum):
            for appid in games_by_stratum[stratum]:
                if len(selected) >= target:
                    break
                progress = take(appid) or progress
        if not progress:
            break

    for index, row in enumerate(selected, start=1):
        row["candidate_id"] = f"final_general_{index:04d}"

    return selected, {
        "requested_target": target,
        "selected_unique": len(selected),
        "stratum_quotas": quotas,
        "selected_by_stratum": dict(sorted(selected_by_stratum.items())),
        "selected_by_game": {
            str(appid): selected_by_game[appid]
            for appid in sorted(selected_by_game)
        },
        "maximum_selected_per_game": maximum_per_game,
        "selection_strategy": (
            "equal quotas by source stratum, round-robin by game, then "
            "redistribution of unused capacity"
        ),
    }


def main() -> int:
    args = parse_args()
    started = datetime.now(timezone.utc)

    try:
        plan = read_json(args.plan)
        general = plan["sampling"]["general"]
        target = int(general["candidate_target"])
        maximum_share = float(
            plan["source_balance"][
                "maximum_share_per_entry_game_within_source"
            ]
        )
        config = read_json(args.config)
        enabled_games = [
            game for game in config["games"]
            if game.get("enabled", True) is not False
        ]
        strata = sorted({str(game["sampling_stratum"]).strip().lower() for game in enabled_games})
        games_per_stratum = Counter(
            str(game["sampling_stratum"]).strip().lower()
            for game in enabled_games
        )
        quotas = calculate_quotas(strata, target)
        per_game_limits = {
            int(game["appid"]): max(
                1,
                math.ceil(
                    (quotas[str(game["sampling_stratum"]).strip().lower()]
                     / games_per_stratum[str(game["sampling_stratum"]).strip().lower()])
                    * args.collection_buffer
                ),
            )
            for game in enabled_games
        }
        max_per_game_for_collection = max(per_game_limits.values())

        excluded_ids, exclusion_counts = load_exclusions(args.exclude_ids)

        with tempfile.TemporaryDirectory(prefix="final_general_collection_") as temp_dir:
            temp = Path(temp_dir)
            raw_temp = temp / "raw.csv"
            unique_temp = temp / "selected.csv"
            summary_temp = temp / "summary.json"

            command = [
                sys.executable,
                str(args.base_script),
                "--config", str(args.config),
                "--target-unique", str(max(target * 2, target + 500)),
                "--max-per-game", str(max_per_game_for_collection),
                "--reviews-per-page", str(args.reviews_per_page),
                "--max-pages-per-game", str(args.max_pages_per_game),
                "--request-delay", str(args.request_delay),
                "--timeout", str(args.timeout),
                "--raw-output", str(raw_temp),
                "--unique-output", str(unique_temp),
                "--summary-output", str(summary_temp),
            ]
            result = subprocess.run(command, check=False)
            if result.returncode not in (0, 2):
                raise RuntimeError(
                    f"Base collector failed with code {result.returncode}."
                )

            with raw_temp.open(encoding="utf-8", newline="") as file:
                reader = csv.DictReader(file)
                raw_fields = list(reader.fieldnames or [])
                raw_rows = list(reader)

        filtered_rows = [
            row for row in raw_rows
            if row["steamid"].strip() not in excluded_ids
        ]
        removed_occurrences = len(raw_rows) - len(filtered_rows)
        maximum_per_game = max(1, math.floor(target * maximum_share))
        selected, diagnostics = balanced_select(
            filtered_rows,
            target,
            maximum_per_game,
        )

        if len(selected) < target:
            raise ValueError(
                f"General target not reached: {len(selected)}/{target}."
            )

        selected_ids = [row["steamid"] for row in selected]
        if len(selected_ids) != len(set(selected_ids)):
            raise ValueError("Duplicate SteamIDs remained in final sample.")

        write_csv(args.raw_output, raw_fields, raw_rows)
        write_csv(args.selected_output, SELECTED_FIELDS, selected)

        selected_by_game = diagnostics["selected_by_game"]
        maximum_observed_count = max(selected_by_game.values(), default=0)
        finished = datetime.now(timezone.utc)
        summary = {
            "pipeline_stage": "06_collect_final_general_steamids",
            "started_at": started.isoformat(),
            "finished_at": finished.isoformat(),
            "duration_seconds": round((finished - started).total_seconds(), 3),
            "plan_version": str(plan.get("version", "")),
            "parameters": {
                "candidate_target": target,
                "collection_buffer": args.collection_buffer,
                "base_max_per_game": max_per_game_for_collection,
                "maximum_selected_candidates_per_entry_game": maximum_per_game,
                "reviews_per_page": args.reviews_per_page,
                "max_pages_per_game": args.max_pages_per_game,
                "request_delay": args.request_delay,
                "timeout": args.timeout,
            },
            "enabled_entry_games": len(enabled_games),
            "entry_games_by_stratum": dict(sorted(games_per_stratum.items())),
            "exclusion_files": exclusion_counts,
            "excluded_unique_steamids_loaded": len(excluded_ids),
            "raw_accepted_rows": len(raw_rows),
            "occurrences_removed_by_exclusion_files": removed_occurrences,
            "new_unique_candidates_available": len({row["steamid"] for row in filtered_rows}),
            "selected_unique_candidates": len(selected),
            "target_reached": len(selected) == target,
            "selection": diagnostics,
            "maximum_observed_entry_game_count": maximum_observed_count,
            "maximum_observed_entry_game_share": round(
                maximum_observed_count / len(selected), 6
            ),
            "granularity_note": (
                "Sampling stratum and all source occurrences are retained for "
                "audit and later granular analyses; they never define target."
            ),
            "methodological_note": (
                "The final player target is derived from the complete public "
                "library and categorized playtime."
            ),
        }
        write_json(args.summary_output, summary)

    except (OSError, KeyError, TypeError, ValueError, RuntimeError) as error:
        print(f"Final general collection failed: {error}", file=sys.stderr)
        return 1

    print(f"Final general collection completed: selected={len(selected)}.")
    print(f"Raw output: {args.raw_output}")
    print(f"Selected output: {args.selected_output}")
    print(f"Summary output: {args.summary_output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())