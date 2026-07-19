"""Build player profiles using combat, exploration and strategic reasoning."""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import statistics
import sys
from collections import Counter
from pathlib import Path
from typing import Any

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GAMES_INPUT = PROJECT_ROOT / "data" / "raw" / "player_games_raw.csv"
DEFAULT_STATUS_INPUT = PROJECT_ROOT / "data" / "interim" / "player_library_status.csv"
DEFAULT_CATEGORY_INPUT = PROJECT_ROOT / "data" / "interim" / "game_category_scores_v2.csv"
DEFAULT_OUTPUT = PROJECT_ROOT / "data" / "interim" / "player_profiles_v2.csv"
DEFAULT_SUMMARY = PROJECT_ROOT / "reports" / "metrics" / "05b_player_profile_summary.json"

LOGGER = logging.getLogger("revised-player-profile-builder")
CATEGORIES = ("combat", "exploration", "strategic_reasoning")


def configure_logging() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build revised player profiles.")
    parser.add_argument("--games-input", type=Path, default=DEFAULT_GAMES_INPUT)
    parser.add_argument("--status-input", type=Path, default=DEFAULT_STATUS_INPUT)
    parser.add_argument("--category-input", type=Path, default=DEFAULT_CATEGORY_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--minimum-playtime-coverage", type=float, default=0.50)
    parser.add_argument("--minimum-target-dominance", type=float, default=0.40)
    parser.add_argument("--minimum-target-gap", type=float, default=0.10)
    args = parser.parse_args()
    for field in ("minimum_playtime_coverage", "minimum_target_dominance", "minimum_target_gap"):
        value = getattr(args, field)
        if not 0 <= value <= 1:
            parser.error(f"--{field.replace('_', '-')} must be between 0 and 1.")
    return args


def normalize(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def load_valid_players(path: Path) -> set[str]:
    players: set[str] = set()
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None or not {"player_id", "status"}.issubset(reader.fieldnames):
            raise ValueError("Status CSV must contain player_id and status.")
        for row in reader:
            if normalize(row.get("status")) == "valid":
                player_id = normalize(row.get("player_id"))
                if player_id:
                    players.add(player_id)
    if not players:
        raise ValueError("No valid players found.")
    return players


def load_categories(path: Path) -> dict[int, str]:
    mapping: dict[int, str] = {}
    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None or not {"appid", "assigned_category"}.issubset(reader.fieldnames):
            raise ValueError("Category CSV must contain appid and assigned_category.")
        for row in reader:
            appid = safe_int(row.get("appid"))
            if appid > 0:
                mapping[appid] = normalize(row.get("assigned_category"))
    if not mapping:
        raise ValueError("No category mappings found.")
    return mapping


def entropy(shares: dict[str, float]) -> float:
    return -sum(share * math.log(share, 2) for share in shares.values() if share > 0)


def diversity(entropy_value: float) -> float:
    maximum = math.log(len(CATEGORIES), 2)
    return entropy_value / maximum if maximum else 0.0


def rank(shares: dict[str, float]) -> list[tuple[str, float]]:
    return sorted(shares.items(), key=lambda item: (-item[1], item[0]))


def determine_target(shares: dict[str, float], coverage: float, minimum_coverage: float,
                     minimum_dominance: float, minimum_gap: float) -> tuple[str, str]:
    ordered = rank(shares)
    top_category, top_share = ordered[0]
    second_share = ordered[1][1]
    gap = top_share - second_share
    if coverage < minimum_coverage:
        return "unresolved", "insufficient_playtime_coverage"
    if top_share < minimum_dominance:
        return "unresolved", "insufficient_dominance"
    if gap < minimum_gap:
        return "unresolved", "insufficient_gap"
    return top_category, "dominant_playtime_share"


def write_csv_atomic(path: Path, rows: list[dict[str, Any]], fields: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
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
    try:
        valid_players = load_valid_players(args.status_input)
        category_by_appid = load_categories(args.category_input)
        profiles = {
            player_id: {
                "total_library_games": 0,
                "played_library_games": 0,
                "total_playtime_minutes": 0,
                "categorized_games": 0,
                "categorized_played_games": 0,
                "categorized_playtime_minutes": 0,
                "uncategorized_games": 0,
                "ambiguous_games": 0,
                "excluded_games": 0,
                "unmapped_games": 0,
                "games_by_category": Counter(),
                "playtime_by_category": Counter(),
            }
            for player_id in valid_players
        }

        with args.games_input.open("r", encoding="utf-8", newline="") as file:
            reader = csv.DictReader(file)
            playtime_field = next((name for name in (
                "playtime_forever", "playtime_forever_minutes", "playtime_minutes"
            ) if reader.fieldnames and name in reader.fieldnames), None)
            if reader.fieldnames is None or not {"player_id", "appid"}.issubset(reader.fieldnames) or playtime_field is None:
                raise ValueError("Games CSV must contain player_id, appid and a playtime field.")
            for row in reader:
                player_id = normalize(row.get("player_id"))
                if player_id not in profiles:
                    continue
                appid = safe_int(row.get("appid"))
                playtime = max(safe_int(row.get(playtime_field)), 0)
                profile = profiles[player_id]
                profile["total_library_games"] += 1
                profile["total_playtime_minutes"] += playtime
                if playtime > 0:
                    profile["played_library_games"] += 1
                assigned = category_by_appid.get(appid)
                if assigned is None:
                    profile["unmapped_games"] += 1
                elif assigned in CATEGORIES:
                    profile["categorized_games"] += 1
                    profile["games_by_category"][assigned] += 1
                    profile["playtime_by_category"][assigned] += playtime
                    profile["categorized_playtime_minutes"] += playtime
                    if playtime > 0:
                        profile["categorized_played_games"] += 1
                elif assigned == "ambiguous":
                    profile["ambiguous_games"] += 1
                elif assigned == "uncategorized":
                    profile["uncategorized_games"] += 1
                else:
                    profile["excluded_games"] += 1

        rows: list[dict[str, Any]] = []
        target_counts: Counter[str] = Counter()
        reason_counts: Counter[str] = Counter()
        playtime_coverages: list[float] = []
        game_coverages: list[float] = []
        played_coverages: list[float] = []

        for player_id in sorted(profiles):
            p = profiles[player_id]
            total_games = p["total_library_games"]
            played_games = p["played_library_games"]
            total_playtime = p["total_playtime_minutes"]
            categorized_games = p["categorized_games"]
            categorized_played = p["categorized_played_games"]
            categorized_playtime = p["categorized_playtime_minutes"]

            game_coverage = categorized_games / total_games if total_games else 0.0
            played_coverage = categorized_played / played_games if played_games else 0.0
            playtime_coverage = categorized_playtime / total_playtime if total_playtime else 0.0

            game_counts = {c: int(p["games_by_category"].get(c, 0)) for c in CATEGORIES}
            playtime_minutes = {c: int(p["playtime_by_category"].get(c, 0)) for c in CATEGORIES}
            game_shares = {c: game_counts[c] / categorized_games if categorized_games else 0.0 for c in CATEGORIES}
            playtime_shares = {c: playtime_minutes[c] / categorized_playtime if categorized_playtime else 0.0 for c in CATEGORIES}

            ranked_time = rank(playtime_shares)
            ranked_games = rank(game_shares)
            playtime_dom, playtime_second = ranked_time[0][1], ranked_time[1][1]
            game_dom, game_second = ranked_games[0][1], ranked_games[1][1]
            playtime_entropy = entropy(playtime_shares)
            game_entropy = entropy(game_shares)

            target, reason = determine_target(
                playtime_shares, playtime_coverage,
                args.minimum_playtime_coverage,
                args.minimum_target_dominance,
                args.minimum_target_gap,
            )
            target_counts[target] += 1
            reason_counts[reason] += 1
            playtime_coverages.append(playtime_coverage)
            game_coverages.append(game_coverage)
            played_coverages.append(played_coverage)

            rows.append({
                "player_id": player_id,
                "status": "valid",
                "total_library_games": total_games,
                "played_library_games": played_games,
                "total_playtime_minutes": total_playtime,
                "total_playtime_hours": round(total_playtime / 60, 6),
                "categorized_games": categorized_games,
                "categorized_played_games": categorized_played,
                "categorized_playtime_minutes": categorized_playtime,
                "categorized_playtime_hours": round(categorized_playtime / 60, 6),
                "uncategorized_games": p["uncategorized_games"],
                "ambiguous_games": p["ambiguous_games"],
                "excluded_games": p["excluded_games"],
                "unmapped_games": p["unmapped_games"],
                "game_coverage_rate": round(game_coverage, 6),
                "played_game_coverage_rate": round(played_coverage, 6),
                "playtime_coverage_rate": round(playtime_coverage, 6),
                "games_combat": game_counts["combat"],
                "games_exploration": game_counts["exploration"],
                "games_strategic_reasoning": game_counts["strategic_reasoning"],
                "hours_combat": round(playtime_minutes["combat"] / 60, 6),
                "hours_exploration": round(playtime_minutes["exploration"] / 60, 6),
                "hours_strategic_reasoning": round(playtime_minutes["strategic_reasoning"] / 60, 6),
                "game_share_combat": round(game_shares["combat"], 6),
                "game_share_exploration": round(game_shares["exploration"], 6),
                "game_share_strategic_reasoning": round(game_shares["strategic_reasoning"], 6),
                "playtime_share_combat": round(playtime_shares["combat"], 6),
                "playtime_share_exploration": round(playtime_shares["exploration"], 6),
                "playtime_share_strategic_reasoning": round(playtime_shares["strategic_reasoning"], 6),
                "dominant_category_by_playtime": ranked_time[0][0],
                "dominant_category_by_games": ranked_games[0][0],
                "second_category_by_playtime": ranked_time[1][0],
                "playtime_dominance": round(playtime_dom, 6),
                "playtime_second_max": round(playtime_second, 6),
                "playtime_gap": round(playtime_dom - playtime_second, 6),
                "game_dominance": round(game_dom, 6),
                "game_second_max": round(game_second, 6),
                "game_gap": round(game_dom - game_second, 6),
                "playtime_entropy": round(playtime_entropy, 6),
                "game_entropy": round(game_entropy, 6),
                "playtime_diversity": round(diversity(playtime_entropy), 6),
                "game_diversity": round(diversity(game_entropy), 6),
                "target": target,
                "target_reason": reason,
            })

        fields = list(rows[0].keys()) if rows else []
        write_csv_atomic(args.output, rows, fields)
        summary = {
            "pipeline_stage": "05b_build_revised_player_profiles",
            "category_mapping": "combat_exploration_strategic_reasoning",
            "parameters": {
                "minimum_playtime_coverage": args.minimum_playtime_coverage,
                "minimum_target_dominance": args.minimum_target_dominance,
                "minimum_target_gap": args.minimum_target_gap,
            },
            "valid_players_loaded": len(valid_players),
            "profiles_generated": len(rows),
            "target_counts": dict(sorted(target_counts.items())),
            "target_reason_counts": dict(sorted(reason_counts.items())),
            "coverage": {
                "mean_game_coverage_rate": round(statistics.mean(game_coverages), 6),
                "median_game_coverage_rate": round(statistics.median(game_coverages), 6),
                "mean_played_game_coverage_rate": round(statistics.mean(played_coverages), 6),
                "median_played_game_coverage_rate": round(statistics.median(played_coverages), 6),
                "mean_playtime_coverage_rate": round(statistics.mean(playtime_coverages), 6),
                "median_playtime_coverage_rate": round(statistics.median(playtime_coverages), 6),
                "minimum_playtime_coverage_rate": round(min(playtime_coverages), 6),
                "maximum_playtime_coverage_rate": round(max(playtime_coverages), 6),
            },
            "methodological_note": (
                "The revised target uses categorized playtime shares for combat, exploration "
                "and strategic reasoning. Profiles below coverage, dominance or gap thresholds "
                "remain unresolved."
            ),
        }
        write_json_atomic(args.summary_output, summary)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        LOGGER.exception("Revised profile building failed: %s", error)
        return 1

    LOGGER.info("Revised profile building completed: profiles=%d.", len(rows))
    LOGGER.info("Output: %s", args.output)
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())