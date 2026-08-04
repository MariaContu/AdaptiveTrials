"""Fetch and select final public Steam libraries for one sampling source.

This wrapper reuses src/02_fetch_player_games.py and adds final-stage rules:
- processes every candidate instead of stopping by target;
- restores candidate provenance after the base collector renames players;
- preserves all attempted profiles for audit;
- selects an exact number of valid profiles reproducibly;
- balances selection across source strata/subgroups when possible;
- writes both complete and selected datasets;
- keeps sampling origin separate from the final target.

Run once with --source general and once with --source strategic.
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import json
import logging
import math
import os
import sys
import tempfile
from collections import Counter, defaultdict, deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


PROJECT_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PLAN = PROJECT_ROOT / "config" / "final_collection_plan.json"
DEFAULT_BASE_SCRIPT = PROJECT_ROOT / "src" / "02_fetch_player_games.py"

LOGGER = logging.getLogger("final-library-collector")

GENERAL_INPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "final_general_steamids_candidates.csv"
)
STRATEGIC_INPUT = (
    PROJECT_ROOT
    / "data"
    / "interim"
    / "final_strategic_steamids_candidates.csv"
)

PROVENANCE_FIELDS = [
    "candidate_id",
    "sampling_source",
    "source_group",
    "source_appid",
    "source_game",
    "sampling_strata",
    "source_count",
    "collection_stage",
]

SELECTION_FIELDS = [
    "selected_for_final_dataset",
    "final_player_id",
    "selection_order",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Fetch final Steam libraries and select valid profiles."
    )
    parser.add_argument(
        "--source",
        choices=("general", "strategic"),
        required=True,
    )
    parser.add_argument("--plan", type=Path, default=DEFAULT_PLAN)
    parser.add_argument(
        "--base-script",
        type=Path,
        default=DEFAULT_BASE_SCRIPT,
    )
    parser.add_argument("--input", type=Path, default=None)
    parser.add_argument("--request-delay", type=float, default=1.0)
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--minimum-games", type=int, default=1)
    parser.add_argument("--minimum-played-games", type=int, default=1)
    parser.add_argument("--seed", type=int, default=42)

    args = parser.parse_args()

    if args.request_delay < 0:
        parser.error("--request-delay cannot be negative.")
    if args.timeout <= 0:
        parser.error("--timeout must be positive.")
    if args.minimum_games <= 0:
        parser.error("--minimum-games must be positive.")
    if args.minimum_played_games <= 0:
        parser.error("--minimum-played-games must be positive.")

    if args.input is None:
        args.input = (
            GENERAL_INPUT if args.source == "general" else STRATEGIC_INPUT
        )

    return args


def normalize(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8") as file:
        payload = json.load(file)

    if not isinstance(payload, dict):
        raise ValueError(f"Expected JSON object in {path}.")

    return payload


def load_target(plan_path: Path, source: str) -> int:
    plan = load_json(plan_path)

    try:
        if source == "general":
            target = plan["sampling"]["general"]["valid_profile_target"]
        else:
            target = plan["sampling"]["strategic_reasoning"][
                "valid_profile_target"
            ]
    except (KeyError, TypeError) as error:
        raise ValueError(
            f"Could not read valid target for source {source!r}."
        ) from error

    target_int = safe_int(target)
    if target_int <= 0:
        raise ValueError("Valid profile target must be positive.")

    return target_int


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None:
            raise ValueError(f"CSV has no header: {path}")
        rows = [dict(row) for row in reader]

    return list(reader.fieldnames), rows


def write_csv(
    path: Path,
    fieldnames: list[str],
    rows: list[dict[str, Any]],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(
            file,
            fieldnames=fieldnames,
            extrasaction="ignore",
        )
        writer.writeheader()
        writer.writerows(rows)

    temp.replace(path)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")

    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)

    temp.replace(path)


def import_base_module(path: Path):
    if not path.exists():
        raise FileNotFoundError(path)

    spec = importlib.util.spec_from_file_location(
        "adaptive_trials_base_player_games",
        path,
    )
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not import base collector: {path}")

    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def candidate_provenance(
    row: dict[str, str],
    source: str,
) -> dict[str, str]:
    if source == "general":
        return {
            "candidate_id": normalize(row.get("candidate_id")),
            "sampling_source": "general",
            "source_group": normalize(
                row.get("primary_sampling_stratum")
            ),
            "source_appid": normalize(row.get("primary_source_appid")),
            "source_game": normalize(row.get("primary_source_game")),
            "sampling_strata": normalize(row.get("sampling_strata")),
            "source_count": normalize(row.get("source_count")),
            "collection_stage": normalize(row.get("collection_stage")),
        }

    return {
        "candidate_id": normalize(row.get("candidate_id")),
        "sampling_source": "strategic",
        "source_group": normalize(row.get("source_subgroup")),
        "source_appid": normalize(row.get("source_appid")),
        "source_game": normalize(row.get("source_game")),
        "sampling_strata": normalize(row.get("source_subgroup")),
        "source_count": "1",
        "collection_stage": normalize(row.get("collection_stage")),
    }


def ensure_candidate_fields(
    rows: list[dict[str, str]],
    source: str,
) -> None:
    if not rows:
        raise ValueError("Candidate input is empty.")

    required = {"steamid"}

    if source == "general":
        required |= {
            "candidate_id",
            "primary_sampling_stratum",
            "primary_source_appid",
            "primary_source_game",
        }
    else:
        required |= {
            "candidate_id",
            "source_subgroup",
            "source_appid",
            "source_game",
        }

    missing = required - set(rows[0])
    if missing:
        raise ValueError(
            f"Candidate CSV is missing fields: {sorted(missing)}"
        )


def build_candidates(base_module, rows: list[dict[str, str]]):
    candidates = []
    seen: set[str] = set()

    for index, row in enumerate(rows, start=1):
        steamid = normalize(row.get("steamid"))

        if not steamid.isdigit():
            raise ValueError(
                f"Invalid SteamID at candidate row {index}: {steamid!r}"
            )
        if steamid in seen:
            raise ValueError(f"Duplicate candidate SteamID: {steamid}")

        seen.add(steamid)
        candidates.append(
            base_module.CandidatePlayer(
                player_id=f"player_{index:04d}",
                steamid=steamid,
            )
        )

    return candidates


def attach_provenance(
    status_rows: list[dict[str, Any]],
    game_rows: list[dict[str, Any]],
    candidate_rows: list[dict[str, str]],
    source: str,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    if len(status_rows) != len(candidate_rows):
        raise ValueError(
            "Status row count does not match candidate count: "
            f"{len(status_rows)} != {len(candidate_rows)}"
        )

    provenance_by_player: dict[str, dict[str, str]] = {}

    for index, status_row in enumerate(status_rows):
        player_id = normalize(status_row.get("player_id"))
        provenance_by_player[player_id] = candidate_provenance(
            candidate_rows[index],
            source,
        )

    enriched_status: list[dict[str, Any]] = []
    for row in status_rows:
        player_id = normalize(row.get("player_id"))
        enriched_status.append(
            {
                **row,
                **provenance_by_player.get(player_id, {}),
            }
        )

    enriched_games: list[dict[str, Any]] = []
    for row in game_rows:
        player_id = normalize(row.get("player_id"))
        enriched_games.append(
            {
                **row,
                **provenance_by_player.get(player_id, {}),
            }
        )

    return enriched_status, enriched_games


def calculate_group_quotas(
    valid_rows: list[dict[str, Any]],
    target: int,
) -> dict[str, int]:
    groups = sorted(
        {
            normalize(row.get("source_group"))
            for row in valid_rows
            if normalize(row.get("source_group"))
        }
    )
    if not groups:
        raise ValueError("No source groups found among valid profiles.")

    available = Counter(
        normalize(row.get("source_group")) for row in valid_rows
    )
    base = target // len(groups)
    remainder = target % len(groups)

    quotas = {
        group: min(
            available[group],
            base + (1 if index < remainder else 0),
        )
        for index, group in enumerate(groups)
    }

    remaining = target - sum(quotas.values())

    while remaining > 0:
        progress = False

        for group in groups:
            capacity = available[group] - quotas[group]
            if capacity <= 0:
                continue

            quotas[group] += 1
            remaining -= 1
            progress = True

            if remaining == 0:
                break

        if not progress:
            break

    return quotas


def select_valid_profiles(
    status_rows: list[dict[str, Any]],
    target: int,
    seed: int,
) -> tuple[list[str], dict[str, Any]]:
    import random

    valid_rows = [
        row for row in status_rows if row.get("status") == "valid"
    ]

    if len(valid_rows) < target:
        raise ValueError(
            f"Only {len(valid_rows)} valid profiles are available; "
            f"target is {target}."
        )

    quotas = calculate_group_quotas(valid_rows, target)
    randomizer = random.Random(seed)

    rows_by_group: dict[str, deque[dict[str, Any]]] = defaultdict(deque)

    for group in sorted(quotas):
        group_rows = [
            row
            for row in valid_rows
            if normalize(row.get("source_group")) == group
        ]
        randomizer.shuffle(group_rows)
        rows_by_group[group] = deque(group_rows)

    selected: list[dict[str, Any]] = []

    for group in sorted(quotas):
        for _ in range(quotas[group]):
            if not rows_by_group[group]:
                break
            selected.append(rows_by_group[group].popleft())

    if len(selected) < target:
        remaining_rows = [
            row
            for group_rows in rows_by_group.values()
            for row in group_rows
        ]
        randomizer.shuffle(remaining_rows)
        selected.extend(remaining_rows[: target - len(selected)])

    if len(selected) != target:
        raise ValueError(
            f"Final valid selection produced {len(selected)} profiles, "
            f"expected {target}."
        )

    selected.sort(
        key=lambda row: (
            normalize(row.get("source_group")),
            normalize(row.get("candidate_id")),
        )
    )

    selected_player_ids = [
        normalize(row.get("player_id")) for row in selected
    ]

    return selected_player_ids, {
        "target": target,
        "available_valid_profiles": len(valid_rows),
        "group_quotas": quotas,
        "selected_by_group": dict(
            sorted(
                Counter(
                    normalize(row.get("source_group"))
                    for row in selected
                ).items()
            )
        ),
        "selection_strategy": (
            "reproducible stratified selection by sampling source group, "
            "with redistribution when a group has insufficient valid profiles"
        ),
        "seed": seed,
    }


def output_paths(source: str) -> dict[str, Path]:
    prefix = f"final_{source}"

    return {
        "all_games": (
            PROJECT_ROOT / "data" / "raw" / f"{prefix}_player_games_all.csv"
        ),
        "all_status": (
            PROJECT_ROOT
            / "data"
            / "interim"
            / f"{prefix}_player_library_status_all.csv"
        ),
        "selected_games": (
            PROJECT_ROOT
            / "data"
            / "raw"
            / f"{prefix}_player_games_selected.csv"
        ),
        "selected_status": (
            PROJECT_ROOT
            / "data"
            / "interim"
            / f"{prefix}_player_library_status_selected.csv"
        ),
        "summary": (
            PROJECT_ROOT
            / "reports"
            / "metrics"
            / f"06_{source}_library_summary.json"
        ),
    }


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        target = load_target(args.plan, args.source)
        _, candidate_rows = read_csv(args.input)
        ensure_candidate_fields(candidate_rows, args.source)

        base = import_base_module(args.base_script)
        api_key = base.load_api_key()
        candidates = build_candidates(base, candidate_rows)
        session = base.create_http_session()

        game_rows, status_rows = base.collect_player_games(
            candidates=candidates,
            session=session,
            api_key=api_key,
            request_delay=args.request_delay,
            timeout=args.timeout,
            minimum_games=args.minimum_games,
            minimum_played_games=args.minimum_played_games,
        )

        status_rows, game_rows = attach_provenance(
            status_rows=status_rows,
            game_rows=game_rows,
            candidate_rows=candidate_rows,
            source=args.source,
        )

        selected_player_ids, selection = select_valid_profiles(
            status_rows=status_rows,
            target=target,
            seed=args.seed,
        )
        selected_set = set(selected_player_ids)

        final_id_by_player = {
            player_id: f"{args.source}_final_{index:04d}"
            for index, player_id in enumerate(
                selected_player_ids,
                start=1,
            )
        }
        selection_order = {
            player_id: index
            for index, player_id in enumerate(
                selected_player_ids,
                start=1,
            )
        }

        for row in status_rows:
            player_id = normalize(row.get("player_id"))
            row["selected_for_final_dataset"] = (
                player_id in selected_set
            )
            row["final_player_id"] = final_id_by_player.get(
                player_id,
                "",
            )
            row["selection_order"] = selection_order.get(player_id, "")

        for row in game_rows:
            player_id = normalize(row.get("player_id"))
            row["selected_for_final_dataset"] = (
                player_id in selected_set
            )
            row["final_player_id"] = final_id_by_player.get(
                player_id,
                "",
            )
            row["selection_order"] = selection_order.get(player_id, "")

        selected_status = [
            row
            for row in status_rows
            if normalize(row.get("player_id")) in selected_set
        ]
        selected_games = [
            row
            for row in game_rows
            if normalize(row.get("player_id")) in selected_set
        ]

        paths = output_paths(args.source)

        status_fields = (
            list(base.STATUS_FIELDNAMES)
            + PROVENANCE_FIELDS
            + SELECTION_FIELDS
        )
        game_fields = (
            list(base.GAME_FIELDNAMES)
            + PROVENANCE_FIELDS
            + SELECTION_FIELDS
        )

        write_csv(paths["all_status"], status_fields, status_rows)
        write_csv(paths["all_games"], game_fields, game_rows)
        write_csv(
            paths["selected_status"],
            status_fields,
            selected_status,
        )
        write_csv(
            paths["selected_games"],
            game_fields,
            selected_games,
        )

        status_counts = Counter(
            normalize(row.get("status")) for row in status_rows
        )
        valid_count = status_counts.get("valid", 0)
        selected_game_appids = {
            safe_int(row.get("appid")) for row in selected_games
        }
        selected_playtime = sum(
            safe_int(row.get("playtime_forever_minutes"))
            for row in selected_games
        )

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "06_fetch_and_select_final_libraries",
            "sampling_source": args.source,
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "target_valid_profiles": target,
                "request_delay": args.request_delay,
                "timeout": args.timeout,
                "minimum_games": args.minimum_games,
                "minimum_played_games": args.minimum_played_games,
                "seed": args.seed,
            },
            "candidate_profiles_loaded": len(candidate_rows),
            "profiles_attempted": len(status_rows),
            "status_counts": dict(sorted(status_counts.items())),
            "valid_profiles_available": valid_count,
            "valid_yield_rate": round(
                valid_count / len(status_rows),
                6,
            )
            if status_rows
            else 0.0,
            "selected_valid_profiles": len(selected_status),
            "selection": selection,
            "all_player_game_rows": len(game_rows),
            "selected_player_game_rows": len(selected_games),
            "selected_unique_appids": len(selected_game_appids),
            "selected_total_playtime_minutes": selected_playtime,
            "selected_total_playtime_hours": round(
                selected_playtime / 60,
                3,
            ),
            "outputs": {
                key: str(path)
                for key, path in paths.items()
                if key != "summary"
            },
            "methodological_note": (
                "All candidates are processed before selection. Exactly the "
                "planned number of valid profiles is then selected in a "
                "reproducible stratified procedure. Sampling origin is kept "
                "for audit and does not define the player target."
            ),
        }

        write_json(paths["summary"], summary)

    except PermissionError as error:
        LOGGER.error("%s", error)
        return 3
    except (
        OSError,
        ValueError,
        ImportError,
    ) as error:
        LOGGER.exception("Final library collection failed: %s", error)
        return 1

    LOGGER.info(
        "Final %s libraries completed: valid=%d, selected=%d.",
        args.source,
        valid_count,
        len(selected_status),
    )
    LOGGER.info("Summary: %s", paths["summary"])
    return 0


if __name__ == "__main__":
    sys.exit(main())