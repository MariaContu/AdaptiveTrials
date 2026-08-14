"""Rebuild the 40 deployment features from player-game rows.

This parity stage reproduces the feature construction used for the final
source-excluded profiles, but emits only the 40 leakage-controlled features
required by the optimized macro model.

It is intended to validate that inference-time feature engineering matches
training-time feature engineering exactly before connecting to live Steam data.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import math
import sys
from collections import Counter
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_GAMES = ROOT / "data/interim/final_player_games_1000.csv"
DEFAULT_MAPPING = ROOT / "data/interim/final_game_category_scores_v2.csv"
DEFAULT_STATUS = ROOT / "data/interim/final_player_library_status_1000.csv"
DEFAULT_REFERENCE = ROOT / "data/processed/final_player_profiles_source_excluded.csv"

MACRO_CATEGORIES = ("combat", "exploration", "strategic_reasoning")
SUBGROUPS = {
    "combat": ("shooter", "melee", "action_other"),
    "exploration": (
        "open_world",
        "narrative_exploration",
        "investigation",
    ),
    "strategic_reasoning": (
        "logic_puzzle",
        "planning_management",
        "strategy_decision",
        "observation_deduction",
    ),
}

BASE_FEATURES = [
    "total_library_games",
    "total_played_games",
    "total_playtime_minutes",
    "categorized_games",
    "categorized_played_games",
    "category_game_coverage",
    "category_playtime_coverage",
    "avg_playtime_per_played_game_minutes",
    "games_combat",
    "played_games_combat",
    "games_exploration",
    "played_games_exploration",
    "games_strategic_reasoning",
    "played_games_strategic_reasoning",
    "games_shooter",
    "played_games_shooter",
    "games_melee",
    "played_games_melee",
    "games_action_other",
    "played_games_action_other",
    "games_open_world",
    "played_games_open_world",
    "games_narrative_exploration",
    "played_games_narrative_exploration",
    "games_investigation",
    "played_games_investigation",
    "games_logic_puzzle",
    "played_games_logic_puzzle",
    "games_planning_management",
    "played_games_planning_management",
    "games_strategy_decision",
    "played_games_strategy_decision",
    "games_observation_deduction",
    "played_games_observation_deduction",
]

RATIO_FEATURES = [
    "game_share_combat",
    "game_share_exploration",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "played_game_share_strategic_reasoning",
]

FEATURES = BASE_FEATURES + RATIO_FEATURES

LOGGER = logging.getLogger("rebuild-inference-features")


def norm(value: Any) -> str:
    return "" if value is None else str(value).strip()


def safe_int(value: Any) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def safe_float(value: Any) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return 0.0
    return result if math.isfinite(result) else 0.0


def read_csv(path: Path) -> tuple[list[str], list[dict[str, str]]]:
    if not path.exists():
        raise FileNotFoundError(path)

    with path.open("r", encoding="utf-8", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None:
            raise ValueError(f"CSV has no header: {path}")
        return list(reader.fieldnames), [dict(row) for row in reader]


def parse_source_appids(value: Any) -> set[int]:
    text = norm(value)
    if not text:
        return set()

    parts = [text]
    for separator in (";", "|", ","):
        if separator in text:
            parts = [part.strip() for part in text.split(separator)]
            break

    return {
        safe_int(part)
        for part in parts
        if safe_int(part) > 0
    }


def safe_divide(numerator: float, denominator: float) -> float:
    if denominator <= 0:
        return 0.0
    return float(numerator / denominator)


def load_mapping(path: Path) -> dict[int, dict[str, str]]:
    fields, rows = read_csv(path)
    required = {
        "appid",
        "assigned_category",
        "assigned_subgroup",
    }
    missing = required - set(fields)
    if missing:
        raise ValueError(f"Mapping missing columns: {sorted(missing)}")

    return {
        safe_int(row["appid"]): row
        for row in rows
        if safe_int(row.get("appid")) > 0
    }


def load_player_games(
    path: Path,
    player_id: str,
) -> list[dict[str, str]]:
    fields, rows = read_csv(path)
    required = {
        "final_player_id",
        "appid",
        "playtime_forever_minutes",
    }
    missing = required - set(fields)
    if missing:
        raise ValueError(f"Games CSV missing columns: {sorted(missing)}")

    result = [
        row
        for row in rows
        if norm(row.get("final_player_id")) == player_id
    ]

    if not result:
        raise ValueError(f"No game rows found for player: {player_id}")

    return result


def load_source_appids(path: Path, player_id: str) -> set[int]:
    fields, rows = read_csv(path)
    required = {"final_player_id", "source_appid"}
    missing = required - set(fields)
    if missing:
        raise ValueError(f"Status CSV missing columns: {sorted(missing)}")

    matches = [
        row
        for row in rows
        if norm(row.get("final_player_id")) == player_id
    ]

    if len(matches) != 1:
        raise ValueError(
            f"Expected one status row for {player_id}, found {len(matches)}"
        )

    return parse_source_appids(matches[0].get("source_appid"))


def build_features(
    game_rows: list[dict[str, str]],
    mapping: dict[int, dict[str, str]],
    excluded_appids: set[int],
) -> dict[str, float | int]:
    included = [
        row
        for row in game_rows
        if safe_int(row.get("appid")) not in excluded_appids
    ]

    total_library_games = len(included)
    total_played_games = sum(
        safe_int(row.get("playtime_forever_minutes")) > 0
        for row in included
    )
    total_playtime_minutes = sum(
        safe_int(row.get("playtime_forever_minutes"))
        for row in included
    )

    macro_games: Counter[str] = Counter()
    macro_played: Counter[str] = Counter()
    subgroup_games: Counter[str] = Counter()
    subgroup_played: Counter[str] = Counter()

    categorized_games = 0
    categorized_played_games = 0
    categorized_playtime_minutes = 0

    for row in included:
        appid = safe_int(row.get("appid"))
        playtime = safe_int(row.get("playtime_forever_minutes"))
        mapped = mapping.get(appid)

        if mapped is None:
            continue

        category = norm(mapped.get("assigned_category"))
        subgroup = norm(mapped.get("assigned_subgroup"))

        if category not in MACRO_CATEGORIES:
            continue

        categorized_games += 1
        macro_games[category] += 1

        if playtime > 0:
            categorized_played_games += 1
            macro_played[category] += 1
            categorized_playtime_minutes += playtime

        if subgroup in SUBGROUPS[category]:
            subgroup_games[subgroup] += 1
            if playtime > 0:
                subgroup_played[subgroup] += 1

    features: dict[str, float | int] = {
        "total_library_games": total_library_games,
        "total_played_games": total_played_games,
        "total_playtime_minutes": total_playtime_minutes,
        "categorized_games": categorized_games,
        "categorized_played_games": categorized_played_games,
        "category_game_coverage": round(
            safe_divide(categorized_games, total_library_games),
            6,
        ),
        "category_playtime_coverage": round(
            safe_divide(
                categorized_playtime_minutes,
                total_playtime_minutes,
            ),
            6,
        ),
        "avg_playtime_per_played_game_minutes": round(
            safe_divide(
                total_playtime_minutes,
                total_played_games,
            ),
            6,
        ),
    }

    for category in MACRO_CATEGORIES:
        features[f"games_{category}"] = macro_games[category]
        features[f"played_games_{category}"] = macro_played[category]

    for category in MACRO_CATEGORIES:
        for subgroup in SUBGROUPS[category]:
            features[f"games_{subgroup}"] = subgroup_games[subgroup]
            features[f"played_games_{subgroup}"] = subgroup_played[subgroup]

    features["game_share_combat"] = safe_divide(
        float(features["games_combat"]),
        float(features["categorized_games"]),
    )
    features["game_share_exploration"] = safe_divide(
        float(features["games_exploration"]),
        float(features["categorized_games"]),
    )
    features["game_share_strategic_reasoning"] = safe_divide(
        float(features["games_strategic_reasoning"]),
        float(features["categorized_games"]),
    )

    features["played_game_share_combat"] = safe_divide(
        float(features["played_games_combat"]),
        float(features["categorized_played_games"]),
    )
    features["played_game_share_exploration"] = safe_divide(
        float(features["played_games_exploration"]),
        float(features["categorized_played_games"]),
    )
    features["played_game_share_strategic_reasoning"] = safe_divide(
        float(features["played_games_strategic_reasoning"]),
        float(features["categorized_played_games"]),
    )

    return {feature: features[feature] for feature in FEATURES}


def compare_with_reference(
    reference_path: Path,
    player_id: str,
    rebuilt: dict[str, float | int],
) -> dict[str, Any]:
    fields, rows = read_csv(reference_path)

    if "final_player_id" not in fields:
        raise ValueError("Reference CSV does not contain final_player_id.")

    matches = [
        row
        for row in rows
        if norm(row.get("final_player_id")) == player_id
    ]

    if len(matches) != 1:
        raise ValueError(
            f"Expected one reference row for {player_id}, found {len(matches)}"
        )

    reference = matches[0]
    mismatches = []

    for feature in BASE_FEATURES:
        expected = safe_float(reference.get(feature))
        actual = float(rebuilt[feature])

        if abs(expected - actual) > 1e-6:
            mismatches.append({
                "feature": feature,
                "expected": expected,
                "actual": actual,
                "difference": actual - expected,
            })

    return {
        "reference_file": str(reference_path),
        "compared_base_features": len(BASE_FEATURES),
        "mismatch_count": len(mismatches),
        "parity_ok": len(mismatches) == 0,
        "mismatches": mismatches,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-id", required=True)
    parser.add_argument("--games", type=Path, default=DEFAULT_GAMES)
    parser.add_argument("--mapping", type=Path, default=DEFAULT_MAPPING)
    parser.add_argument("--status", type=Path, default=DEFAULT_STATUS)
    parser.add_argument("--reference", type=Path, default=DEFAULT_REFERENCE)
    parser.add_argument(
        "--include-source-game",
        action="store_true",
        help=(
            "Do not exclude the recruitment/source game. "
            "Default parity mode reproduces source_excluded training profiles."
        ),
    )
    parser.add_argument("--output", type=Path, default=None)
    return parser.parse_args()


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    args = parse_args()

    try:
        mapping = load_mapping(args.mapping)
        game_rows = load_player_games(args.games, args.player_id)

        excluded_appids: set[int] = set()
        if not args.include_source_game:
            excluded_appids = load_source_appids(
                args.status,
                args.player_id,
            )

        features = build_features(
            game_rows,
            mapping,
            excluded_appids,
        )

        parity = None
        if not args.include_source_game and args.reference.exists():
            parity = compare_with_reference(
                args.reference,
                args.player_id,
                features,
            )

        payload = {
            "player_id": args.player_id,
            "mode": (
                "full_library"
                if args.include_source_game
                else "source_excluded_parity"
            ),
            "excluded_appids": sorted(excluded_appids),
            "feature_count": len(features),
            "features": features,
            "parity": parity,
        }

        rendered = json.dumps(
            payload,
            ensure_ascii=False,
            indent=2,
        )
        print(rendered)

        if args.output is not None:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            with args.output.open("w", encoding="utf-8") as file:
                file.write(rendered + "\n")

    except Exception as error:
        LOGGER.exception("Feature reconstruction failed: %s", error)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())