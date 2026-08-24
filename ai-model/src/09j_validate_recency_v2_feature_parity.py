"""09j - Validate reconstruction parity for final recency V2 features.\n\nUses 1e-6 numeric tolerance to account for CSV decimal serialization/rounding.\n"""

from __future__ import annotations
import argparse, json, math
from pathlib import Path
from typing import Any
import joblib
import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GAMES = ROOT / "data/interim/final_player_games_1000.csv"
DEFAULT_MAPPING = ROOT / "data/interim/final_game_category_scores_v2.csv"
DEFAULT_STATUS = ROOT / "data/interim/final_player_library_status_1000.csv"
DEFAULT_PROFILES = ROOT / "data/processed/final_player_profiles_recency_v2.csv"
DEFAULT_MODEL = ROOT / "models/final/macro_model_recency_v2.joblib"
DEFAULT_SUMMARY = ROOT / "reports/metrics/09j_recency_v2_feature_parity.json"

MACRO_CATEGORIES = ("combat", "exploration", "strategic_reasoning")
SUBGROUPS = (
    "shooter", "melee", "action_other",
    "open_world", "narrative_exploration", "investigation",
    "logic_puzzle", "planning_management",
    "strategy_decision", "observation_deduction",
)
PLAYER_ID_CANDIDATES = ("final_player_id", "player_id")
SUBGROUP_COLUMN_CANDIDATES = (
    "assigned_subgroup", "subgroup", "dominant_subgroup"
)

def parse_args():
    p = argparse.ArgumentParser()
    p.add_argument("--player-id", type=str, default=None)
    p.add_argument("--games", type=Path, default=DEFAULT_GAMES)
    p.add_argument("--mapping", type=Path, default=DEFAULT_MAPPING)
    p.add_argument("--status", type=Path, default=DEFAULT_STATUS)
    p.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    p.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    p.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    return p.parse_args()

def detect_column(df, candidates, label):
    for c in candidates:
        if c in df.columns:
            return c
    raise ValueError(f"Could not detect {label}. Tried: {list(candidates)}")

def safe_float(v: Any) -> float:
    try:
        if pd.isna(v):
            return 0.0
        x = float(v)
        return x if math.isfinite(x) else 0.0
    except (TypeError, ValueError):
        return 0.0

def safe_divide(a, b):
    return float(a / b) if b > 0 else 0.0

def normalize_subgroup(v):
    if pd.isna(v):
        return ""
    text = str(v).strip().lower()
    aliases = {
        "shooting": "shooter", "tiro": "shooter",
        "melee_combat": "melee",
        "other_action": "action_other",
        "mundo_aberto": "open_world",
        "narrative": "narrative_exploration",
        "logic_and_puzzle": "logic_puzzle",
        "planning_and_management": "planning_management",
        "strategy_and_decision": "strategy_decision",
        "observation_and_deduction": "observation_deduction",
    }
    return aliases.get(text, text)

def main():
    args = parse_args()
    games = pd.read_csv(args.games)
    mapping = pd.read_csv(args.mapping)
    status = pd.read_csv(args.status)
    profiles = pd.read_csv(args.profiles)

    bundle = joblib.load(args.model)
    if not isinstance(bundle, dict):
        raise ValueError("Unexpected V2 model bundle format.")
    features = bundle.get("features")
    if not isinstance(features, list) or not features:
        raise ValueError("V2 model bundle has no valid feature list.")

    game_pid = detect_column(games, PLAYER_ID_CANDIDATES, "games player ID")
    status_pid = detect_column(status, PLAYER_ID_CANDIDATES, "status player ID")
    profile_pid = detect_column(profiles, PLAYER_ID_CANDIDATES, "profile player ID")

    if "appid" not in games.columns:
        raise ValueError("Games dataset has no appid column.")
    for candidate in ("playtime_forever_minutes", "playtime_minutes", "playtime_forever"):
        if candidate in games.columns:
            lifetime_col = candidate
            break
    else:
        raise ValueError("Could not detect lifetime playtime column.")

    if "playtime_2weeks_minutes" not in games.columns:
        raise ValueError("Games dataset has no playtime_2weeks_minutes column.")
    if "assigned_category" not in mapping.columns:
        raise ValueError("Mapping has no assigned_category column.")

    subgroup_col = detect_column(mapping, SUBGROUP_COLUMN_CANDIDATES, "mapping subgroup")
    if "source_appid" not in status.columns:
        raise ValueError("Status dataset has no source_appid.")

    for df, col in ((games, game_pid), (status, status_pid), (profiles, profile_pid)):
        df[col] = df[col].astype(str)

    games["appid"] = pd.to_numeric(games["appid"], errors="coerce").fillna(0).astype(int)
    mapping["appid"] = pd.to_numeric(mapping["appid"], errors="coerce").fillna(0).astype(int)
    mapping["assigned_category"] = mapping["assigned_category"].fillna("").astype(str).str.strip()
    mapping["_normalized_subgroup"] = mapping[subgroup_col].apply(normalize_subgroup)
    mapping_small = mapping[
        ["appid", "assigned_category", "_normalized_subgroup"]
    ].drop_duplicates("appid")

    player_id = str(args.player_id) if args.player_id else str(profiles.iloc[0][profile_pid])

    official_rows = profiles.loc[profiles[profile_pid] == player_id]
    if official_rows.empty:
        raise ValueError(f"Player {player_id!r} not found in official V2 profiles.")
    official = official_rows.iloc[0]

    player_games = games.loc[games[game_pid] == player_id].copy()
    if player_games.empty:
        raise ValueError(f"Player {player_id!r} has no game rows.")

    status_rows = status.loc[status[status_pid] == player_id]
    if status_rows.empty:
        raise ValueError(f"Player {player_id!r} not found in status file.")

    source_appid = int(pd.to_numeric(
        pd.Series([status_rows.iloc[0]["source_appid"]]), errors="coerce"
    ).fillna(-1).iloc[0])
    source_rows_before = int((player_games["appid"] == source_appid).sum())
    if source_appid > 0:
        player_games = player_games.loc[player_games["appid"] != source_appid].copy()

    player_games = player_games.merge(mapping_small, on="appid", how="left", validate="many_to_one")
    player_games["assigned_category"] = player_games["assigned_category"].fillna("").astype(str).str.strip()
    player_games["_normalized_subgroup"] = player_games["_normalized_subgroup"].fillna("").astype(str)

    player_games["_lifetime"] = pd.to_numeric(
        player_games[lifetime_col], errors="coerce"
    ).fillna(0.0).clip(lower=0.0)
    player_games["_recent"] = pd.to_numeric(
        player_games["playtime_2weeks_minutes"], errors="coerce"
    ).fillna(0.0).clip(lower=0.0)
    player_games["_played"] = player_games["_lifetime"] > 0
    player_games["_categorized"] = player_games["assigned_category"].isin(MACRO_CATEGORIES)

    vector = {}
    total_library_games = len(player_games)
    total_played_games = int(player_games["_played"].sum())
    total_playtime = float(player_games["_lifetime"].sum())
    categorized = player_games.loc[player_games["_categorized"]]
    categorized_played = categorized.loc[categorized["_played"]]
    categorized_games = len(categorized)
    categorized_played_games = len(categorized_played)
    categorized_playtime = float(categorized["_lifetime"].sum())

    vector["total_library_games"] = float(total_library_games)
    vector["total_played_games"] = float(total_played_games)
    vector["total_playtime_minutes"] = total_playtime
    vector["categorized_games"] = float(categorized_games)
    vector["categorized_played_games"] = float(categorized_played_games)
    vector["category_game_coverage"] = safe_divide(categorized_games, total_library_games)
    vector["category_playtime_coverage"] = safe_divide(categorized_playtime, total_playtime)
    vector["avg_playtime_per_played_game_minutes"] = safe_divide(total_playtime, total_played_games)

    for category in MACRO_CATEGORIES:
        subset = player_games.loc[player_games["assigned_category"] == category]
        vector[f"games_{category}"] = float(len(subset))
        vector[f"played_games_{category}"] = float(subset["_played"].sum())

    for subgroup in SUBGROUPS:
        subset = player_games.loc[player_games["_normalized_subgroup"] == subgroup]
        vector[f"games_{subgroup}"] = float(len(subset))
        vector[f"played_games_{subgroup}"] = float(subset["_played"].sum())

    for category in MACRO_CATEGORIES:
        vector[f"game_share_{category}"] = safe_divide(
            vector[f"games_{category}"], categorized_games
        )
        vector[f"played_game_share_{category}"] = safe_divide(
            vector[f"played_games_{category}"], categorized_played_games
        )

    recent_rows = player_games.loc[player_games["_recent"] > 0]
    recent_categorized = recent_rows.loc[recent_rows["_categorized"]]
    recent_total = float(recent_rows["_recent"].sum())
    recent_cat_minutes = float(recent_categorized["_recent"].sum())

    vector["has_recent_activity"] = float(recent_total > 0)
    vector["recent_total_playtime_minutes"] = recent_total
    vector["recent_active_games"] = float(len(recent_rows))
    vector["recent_categorized_playtime_minutes"] = recent_cat_minutes
    vector["recent_categorized_active_games"] = float(len(recent_categorized))
    vector["recent_category_playtime_coverage"] = safe_divide(recent_cat_minutes, recent_total)

    for category in MACRO_CATEGORIES:
        vector[f"recent_active_games_{category}"] = float(
            len(recent_categorized.loc[recent_categorized["assigned_category"] == category])
        )

    missing = [f for f in features if f not in vector]
    if missing:
        raise ValueError(f"Reconstruction did not produce all model features: {missing}")

    comparisons, mismatches = [], []
    for feature in features:
        if feature not in official.index:
            raise ValueError(f"Official profile missing feature {feature!r}.")
        rebuilt = safe_float(vector[feature])
        expected = safe_float(official[feature])
        diff = abs(rebuilt - expected)
        tol = max(1e-6, 1e-6 * max(1.0, abs(expected)))
        match = diff <= tol
        item = {
            "feature": feature,
            "rebuilt": rebuilt,
            "expected": expected,
            "absolute_difference": diff,
            "tolerance": tol,
            "match": bool(match),
        }
        comparisons.append(item)
        if not match:
            mismatches.append(item)

    summary = {
        "stage": "09j_recency_v2_feature_parity",
        "player_id": player_id,
        "feature_set": bundle.get("feature_set"),
        "feature_count": len(features),
        "source_game_excluded": True,
        "source_appid": source_appid,
        "source_game_rows_removed": source_rows_before,
        "features_compared": len(comparisons),
        "mismatch_count": len(mismatches),
        "parity": len(mismatches) == 0,
        "mismatches": mismatches,
        "comparisons": comparisons,
    }

    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

    print("\n=== RECENCY V2 FEATURE PARITY ===")
    print(f"Player: {player_id}")
    print(f"Feature set: {bundle.get('feature_set')}")
    print(f"Features compared: {len(comparisons)}")
    print(f"Source game rows removed: {source_rows_before}")
    print(f"Mismatches: {len(mismatches)}")
    print(f"Parity: {len(mismatches) == 0}")

    if mismatches:
        print("\nMismatched features:")
        for item in mismatches:
            print(
                f"  {item['feature']}: rebuilt={item['rebuilt']:.12f} | "
                f"expected={item['expected']:.12f} | diff={item['absolute_difference']:.12f}"
            )

    print(f"\nSaved summary: {args.summary}")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())