"""Build player-level preference profiles from playtime and mapped game categories."""

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

ROOT = Path(__file__).resolve().parents[1]
GAMES = ROOT / "data/raw/player_games_raw.csv"
STATUS = ROOT / "data/interim/player_library_status.csv"
CATEGORIES_FILE = ROOT / "data/interim/game_category_scores.csv"
OUTPUT = ROOT / "data/interim/player_profiles.csv"
SUMMARY = ROOT / "reports/metrics/05_player_profile_summary.json"
CATEGORIES = ("combat", "exploration", "puzzle")
LOGGER = logging.getLogger("player-profile-builder")

FIELDS = [
    "player_id", "status", "total_library_games", "played_library_games",
    "total_playtime_minutes", "total_playtime_hours", "categorized_games",
    "categorized_played_games", "categorized_playtime_minutes",
    "categorized_playtime_hours", "uncategorized_games", "ambiguous_games",
    "excluded_games", "unmapped_games", "game_coverage_rate",
    "played_game_coverage_rate", "playtime_coverage_rate", "games_combat",
    "games_exploration", "games_puzzle", "hours_combat", "hours_exploration",
    "hours_puzzle", "game_share_combat", "game_share_exploration",
    "game_share_puzzle", "playtime_share_combat", "playtime_share_exploration",
    "playtime_share_puzzle", "dominant_category_by_playtime",
    "dominant_category_by_games", "second_category_by_playtime",
    "playtime_dominance", "playtime_second_max", "playtime_gap",
    "game_dominance", "game_second_max", "game_gap", "playtime_entropy",
    "game_entropy", "playtime_diversity", "game_diversity", "target",
    "target_reason"
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--games-input", type=Path, default=GAMES)
    parser.add_argument("--status-input", type=Path, default=STATUS)
    parser.add_argument("--category-input", type=Path, default=CATEGORIES_FILE)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=SUMMARY)
    parser.add_argument("--minimum-playtime-coverage", type=float, default=0.50)
    parser.add_argument("--minimum-target-dominance", type=float, default=0.40)
    parser.add_argument("--minimum-target-gap", type=float, default=0.10)
    args = parser.parse_args()
    for name in (
        "minimum_playtime_coverage", "minimum_target_dominance",
        "minimum_target_gap"
    ):
        value = getattr(args, name)
        if not 0 <= value <= 1:
            parser.error(f"--{name.replace('_', '-')} must be between 0 and 1")
    return args


def text(value: Any) -> str:
    return "" if value is None else str(value).strip()


def integer(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def load_valid_players(path: Path) -> set[str]:
    players: set[str] = set()
    with path.open(encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        required = {"player_id", "status"}
        if reader.fieldnames is None or not required.issubset(reader.fieldnames):
            raise ValueError(f"Status CSV must contain {sorted(required)}")
        for row in reader:
            if text(row.get("status")) == "valid":
                player_id = text(row.get("player_id"))
                if player_id:
                    players.add(player_id)
    if not players:
        raise ValueError("No valid players found")
    return players


def load_categories(path: Path) -> dict[int, str]:
    mapping: dict[int, str] = {}
    with path.open(encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        required = {"appid", "assigned_category"}
        if reader.fieldnames is None or not required.issubset(reader.fieldnames):
            raise ValueError(f"Category CSV must contain {sorted(required)}")
        for row in reader:
            appid = integer(row.get("appid"))
            if appid > 0:
                mapping[appid] = text(row.get("assigned_category"))
    if not mapping:
        raise ValueError("No category mappings found")
    return mapping


def entropy(shares: dict[str, float]) -> float:
    return -sum(value * math.log(value, 2) for value in shares.values() if value > 0)


def diversity(value: float) -> float:
    return value / math.log(len(CATEGORIES), 2)


def ranking(shares: dict[str, float]) -> list[tuple[str, float]]:
    return sorted(shares.items(), key=lambda item: (-item[1], item[0]))


def determine_target(
    shares: dict[str, float], coverage: float,
    min_coverage: float, min_dominance: float, min_gap: float
) -> tuple[str, str]:
    ordered = ranking(shares)
    top_category, top = ordered[0]
    second = ordered[1][1]
    if coverage < min_coverage:
        return "unresolved", "insufficient_playtime_coverage"
    if top < min_dominance:
        return "unresolved", "insufficient_dominance"
    if top - second < min_gap:
        return "unresolved", "insufficient_gap"
    return top_category, "dominant_playtime_share"


def atomic_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    temporary.replace(path)


def atomic_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)


def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")
    args = parse_args()
    try:
        players = load_valid_players(args.status_input)
        game_categories = load_categories(args.category_input)
        data: dict[str, dict[str, Any]] = {
            player: {
                "total": 0, "played": 0, "minutes": 0, "categorized": 0,
                "categorized_played": 0, "categorized_minutes": 0,
                "uncategorized": 0, "ambiguous": 0, "excluded": 0,
                "unmapped": 0, "games": Counter(), "playtime": Counter()
            }
            for player in players
        }

        with args.games_input.open(encoding="utf-8", newline="") as file:
            reader = csv.DictReader(file)
            required = {"player_id", "appid", "playtime_forever_minutes"}
            if reader.fieldnames is None or not required.issubset(reader.fieldnames):
                raise ValueError(f"Games CSV must contain {sorted(required)}")
            for row in reader:
                player = text(row.get("player_id"))
                if player not in data:
                    continue
                appid = integer(row.get("appid"))
                minutes = max(integer(row.get("playtime_forever_minutes")), 0)
                current = data[player]
                current["total"] += 1
                current["minutes"] += minutes
                if minutes > 0:
                    current["played"] += 1
                assigned = game_categories.get(appid)
                if assigned is None:
                    current["unmapped"] += 1
                elif assigned in CATEGORIES:
                    current["categorized"] += 1
                    current["games"][assigned] += 1
                    current["playtime"][assigned] += minutes
                    current["categorized_minutes"] += minutes
                    if minutes > 0:
                        current["categorized_played"] += 1
                elif assigned == "ambiguous":
                    current["ambiguous"] += 1
                elif assigned == "uncategorized":
                    current["uncategorized"] += 1
                else:
                    current["excluded"] += 1

        rows: list[dict[str, Any]] = []
        targets: Counter[str] = Counter()
        reasons: Counter[str] = Counter()
        game_coverages: list[float] = []
        played_coverages: list[float] = []
        playtime_coverages: list[float] = []

        for player in sorted(data):
            current = data[player]
            total = current["total"]
            played = current["played"]
            total_minutes = current["minutes"]
            categorized = current["categorized"]
            categorized_played = current["categorized_played"]
            categorized_minutes = current["categorized_minutes"]

            game_coverage = categorized / total if total else 0.0
            played_coverage = categorized_played / played if played else 0.0
            playtime_coverage = categorized_minutes / total_minutes if total_minutes else 0.0

            counts = {category: current["games"].get(category, 0) for category in CATEGORIES}
            minutes = {category: current["playtime"].get(category, 0) for category in CATEGORIES}
            game_shares = {
                category: counts[category] / categorized if categorized else 0.0
                for category in CATEGORIES
            }
            playtime_shares = {
                category: minutes[category] / categorized_minutes if categorized_minutes else 0.0
                for category in CATEGORIES
            }

            ranked_playtime = ranking(playtime_shares)
            ranked_games = ranking(game_shares)
            playtime_entropy = entropy(playtime_shares)
            game_entropy = entropy(game_shares)
            target, reason = determine_target(
                playtime_shares, playtime_coverage,
                args.minimum_playtime_coverage,
                args.minimum_target_dominance,
                args.minimum_target_gap,
            )
            targets[target] += 1
            reasons[reason] += 1
            game_coverages.append(game_coverage)
            played_coverages.append(played_coverage)
            playtime_coverages.append(playtime_coverage)

            rows.append({
                "player_id": player, "status": "valid",
                "total_library_games": total, "played_library_games": played,
                "total_playtime_minutes": total_minutes,
                "total_playtime_hours": round(total_minutes / 60, 6),
                "categorized_games": categorized,
                "categorized_played_games": categorized_played,
                "categorized_playtime_minutes": categorized_minutes,
                "categorized_playtime_hours": round(categorized_minutes / 60, 6),
                "uncategorized_games": current["uncategorized"],
                "ambiguous_games": current["ambiguous"],
                "excluded_games": current["excluded"],
                "unmapped_games": current["unmapped"],
                "game_coverage_rate": round(game_coverage, 6),
                "played_game_coverage_rate": round(played_coverage, 6),
                "playtime_coverage_rate": round(playtime_coverage, 6),
                "games_combat": counts["combat"],
                "games_exploration": counts["exploration"],
                "games_puzzle": counts["puzzle"],
                "hours_combat": round(minutes["combat"] / 60, 6),
                "hours_exploration": round(minutes["exploration"] / 60, 6),
                "hours_puzzle": round(minutes["puzzle"] / 60, 6),
                "game_share_combat": round(game_shares["combat"], 6),
                "game_share_exploration": round(game_shares["exploration"], 6),
                "game_share_puzzle": round(game_shares["puzzle"], 6),
                "playtime_share_combat": round(playtime_shares["combat"], 6),
                "playtime_share_exploration": round(playtime_shares["exploration"], 6),
                "playtime_share_puzzle": round(playtime_shares["puzzle"], 6),
                "dominant_category_by_playtime": ranked_playtime[0][0],
                "dominant_category_by_games": ranked_games[0][0],
                "second_category_by_playtime": ranked_playtime[1][0],
                "playtime_dominance": round(ranked_playtime[0][1], 6),
                "playtime_second_max": round(ranked_playtime[1][1], 6),
                "playtime_gap": round(ranked_playtime[0][1] - ranked_playtime[1][1], 6),
                "game_dominance": round(ranked_games[0][1], 6),
                "game_second_max": round(ranked_games[1][1], 6),
                "game_gap": round(ranked_games[0][1] - ranked_games[1][1], 6),
                "playtime_entropy": round(playtime_entropy, 6),
                "game_entropy": round(game_entropy, 6),
                "playtime_diversity": round(diversity(playtime_entropy), 6),
                "game_diversity": round(diversity(game_entropy), 6),
                "target": target, "target_reason": reason,
            })

        atomic_csv(args.output, rows)
        summary = {
            "pipeline_stage": "05_build_player_profiles",
            "parameters": {
                "minimum_playtime_coverage": args.minimum_playtime_coverage,
                "minimum_target_dominance": args.minimum_target_dominance,
                "minimum_target_gap": args.minimum_target_gap,
            },
            "valid_players_loaded": len(players),
            "profiles_generated": len(rows),
            "target_counts": dict(sorted(targets.items())),
            "target_reason_counts": dict(sorted(reasons.items())),
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
                "The provisional target uses categorized playtime shares. "
                "Players with insufficient coverage, dominance or score gap remain unresolved."
            ),
        }
        atomic_json(args.summary_output, summary)
    except (OSError, ValueError) as error:
        LOGGER.exception("Player profile building failed: %s", error)
        return 1

    LOGGER.info("Player profile building completed: profiles=%d.", len(rows))
    LOGGER.info("Output: %s", args.output)
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())