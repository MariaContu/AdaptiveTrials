"""09b - Audit Steam two-week recency signals for Adaptive Trials model V2.

This stage does not retrain the model. It:
- preserves the source-game exclusion used by V1;
- audits playtime_2weeks_minutes;
- creates per-player recency features;
- measures mapping coverage;
- compares recent dominant category with the historical target when available.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

import pandas as pd

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_GAMES = ROOT / "data/interim/final_player_games_1000.csv"
DEFAULT_MAPPING = ROOT / "data/interim/final_game_category_scores_v2.csv"
DEFAULT_STATUS = ROOT / "data/interim/final_player_library_status_1000.csv"
DEFAULT_PROFILES = ROOT / "data/processed/final_player_profiles_source_excluded.csv"
DEFAULT_OUTPUT = ROOT / "data/interim/player_recency_features_v2.csv"
DEFAULT_SUMMARY = ROOT / "reports/metrics/09b_recency_signal_summary.json"

MACRO_CATEGORIES = ("combat", "exploration", "strategic_reasoning")
TARGET_CANDIDATES = (
    "target_macro",
    "macro_target",
    "target",
    "resolved_target",
    "target_category",
)


def safe_int(value: Any) -> int:
    try:
        if pd.isna(value):
            return 0
        return int(float(value))
    except (TypeError, ValueError):
        return 0


def safe_divide(a: float, b: float) -> float:
    return float(a / b) if b > 0 else 0.0


def detect_target_column(df: pd.DataFrame) -> str | None:
    for column in TARGET_CANDIDATES:
        if column in df.columns:
            return column
    return None


def qstats(series: pd.Series) -> dict[str, float]:
    if series.empty:
        return {}
    s = pd.to_numeric(series, errors="coerce").fillna(0.0)
    return {
        "min": float(s.min()),
        "q25": float(s.quantile(0.25)),
        "median": float(s.median()),
        "q75": float(s.quantile(0.75)),
        "q90": float(s.quantile(0.90)),
        "max": float(s.max()),
        "mean": float(s.mean()),
    }


def require(df: pd.DataFrame, columns: set[str], name: str) -> None:
    missing = columns - set(df.columns)
    if missing:
        raise ValueError(f"{name} missing columns: {sorted(missing)}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--games", type=Path, default=DEFAULT_GAMES)
    parser.add_argument("--mapping", type=Path, default=DEFAULT_MAPPING)
    parser.add_argument("--status", type=Path, default=DEFAULT_STATUS)
    parser.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    games = pd.read_csv(args.games)
    mapping = pd.read_csv(args.mapping)
    status = pd.read_csv(args.status)
    profiles = pd.read_csv(args.profiles)

    require(
        games,
        {"final_player_id", "appid", "playtime_2weeks_minutes"},
        "games",
    )
    require(
        mapping,
        {"appid", "assigned_category"},
        "mapping",
    )
    require(
        status,
        {"final_player_id", "source_appid"},
        "status",
    )
    require(profiles, {"final_player_id"}, "profiles")

    games = games.copy()
    games["appid"] = pd.to_numeric(
        games["appid"], errors="coerce"
    ).fillna(0).astype(int)
    games["playtime_2weeks_minutes"] = pd.to_numeric(
        games["playtime_2weeks_minutes"], errors="coerce"
    ).fillna(0.0).clip(lower=0.0)

    mapping = mapping.copy()
    mapping["appid"] = pd.to_numeric(
        mapping["appid"], errors="coerce"
    ).fillna(0).astype(int)
    mapping = mapping[
        ["appid", "assigned_category"]
    ].drop_duplicates("appid")

    status = status.copy()
    status["source_appid_clean"] = status["source_appid"].apply(
        lambda x: safe_int(x) if safe_int(x) > 0 else -1
    )

    merged = games.merge(
        status[["final_player_id", "source_appid_clean"]],
        on="final_player_id",
        how="left",
        validate="many_to_one",
    )

    merged["is_source_game"] = (
        merged["appid"]
        == merged["source_appid_clean"].fillna(-1).astype(int)
    )
    source_rows_removed = int(merged["is_source_game"].sum())
    merged = merged.loc[~merged["is_source_game"]].copy()

    merged = merged.merge(
        mapping,
        on="appid",
        how="left",
        validate="many_to_one",
    )
    merged["assigned_category"] = (
        merged["assigned_category"]
        .fillna("")
        .astype(str)
        .str.strip()
    )
    merged["recent_minutes"] = merged["playtime_2weeks_minutes"]
    merged["has_recent_activity"] = merged["recent_minutes"] > 0
    merged["is_macro_categorized"] = merged[
        "assigned_category"
    ].isin(MACRO_CATEGORIES)

    target_col = detect_target_column(profiles)
    target_lookup: dict[str, str] = {}
    if target_col:
        temp = profiles[["final_player_id", target_col]].copy()
        temp[target_col] = (
            temp[target_col].fillna("").astype(str).str.strip()
        )
        target_lookup = dict(
            zip(
                temp["final_player_id"].astype(str),
                temp[target_col],
            )
        )

    player_ids = sorted(
        set(status["final_player_id"].astype(str))
        | set(profiles["final_player_id"].astype(str))
    )

    rows: list[dict[str, Any]] = []

    for player_id in player_ids:
        player = merged.loc[
            merged["final_player_id"].astype(str) == player_id
        ]
        recent = player.loc[player["has_recent_activity"]]
        categorized = recent.loc[recent["is_macro_categorized"]]

        total_recent = float(recent["recent_minutes"].sum())
        categorized_recent = float(categorized["recent_minutes"].sum())

        row: dict[str, Any] = {
            "final_player_id": player_id,
            "has_recent_activity": int(total_recent > 0),
            "recent_total_playtime_minutes": total_recent,
            "recent_active_games": int(len(recent)),
            "recent_categorized_playtime_minutes": categorized_recent,
            "recent_categorized_active_games": int(len(categorized)),
            "recent_category_playtime_coverage": safe_divide(
                categorized_recent, total_recent
            ),
        }

        for category in MACRO_CATEGORIES:
            subset = categorized.loc[
                categorized["assigned_category"] == category
            ]
            minutes = float(subset["recent_minutes"].sum())
            active_games = int(len(subset))

            row[f"recent_playtime_{category}_minutes"] = minutes
            row[f"recent_active_games_{category}"] = active_games
            row[f"recent_share_{category}"] = safe_divide(
                minutes, categorized_recent
            )
            row[f"recent_active_game_share_{category}"] = safe_divide(
                active_games, len(categorized)
            )

        shares = {
            category: float(row[f"recent_share_{category}"])
            for category in MACRO_CATEGORIES
        }

        if categorized_recent > 0:
            ordered = sorted(shares.values(), reverse=True)
            dominant = max(shares, key=shares.get)
            dominance = ordered[0]
            second = ordered[1]
            gap = dominance - second
        else:
            dominant = ""
            dominance = second = gap = 0.0

        row["recent_dominant_category"] = dominant
        row["recent_dominance"] = dominance
        row["recent_second_max"] = second
        row["recent_gap"] = gap

        historical_target = target_lookup.get(player_id, "")
        row["historical_target"] = historical_target
        row["recent_matches_historical_target"] = (
            int(
                dominant != ""
                and historical_target in MACRO_CATEGORIES
                and dominant == historical_target
            )
            if historical_target
            else ""
        )

        rows.append(row)

    recency = pd.DataFrame(rows)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    recency.to_csv(args.output, index=False)

    with_recent = recency.loc[
        recency["has_recent_activity"] == 1
    ]
    with_categorized_recent = recency.loc[
        recency["recent_categorized_playtime_minutes"] > 0
    ]
    reliable = with_recent.loc[
        with_recent["recent_category_playtime_coverage"] >= 0.70
    ]

    comparable = recency.loc[
        recency["historical_target"].isin(MACRO_CATEGORIES)
        & (recency["recent_categorized_playtime_minutes"] > 0)
    ]

    same = int(
        (
            comparable["recent_dominant_category"]
            == comparable["historical_target"]
        ).sum()
    ) if not comparable.empty else 0

    total_recent_relations = int(
        (merged["recent_minutes"] > 0).sum()
    )
    categorized_recent_relations = int(
        (
            (merged["recent_minutes"] > 0)
            & merged["is_macro_categorized"]
        ).sum()
    )

    total_recent_minutes = float(merged["recent_minutes"].sum())
    categorized_recent_minutes = float(
        merged.loc[
            merged["is_macro_categorized"], "recent_minutes"
        ].sum()
    )

    thresholds = {
        str(threshold): int(
            (
                recency["recent_total_playtime_minutes"] >= threshold
            ).sum()
        )
        for threshold in (10, 30, 60, 120, 300, 600)
    }

    summary = {
        "stage": "09b_recency_signal_audit",
        "methodology": {
            "source_game_excluded": True,
            "recency_field": "playtime_2weeks_minutes",
            "historical_target_column_detected": target_col,
            "macro_categories": list(MACRO_CATEGORIES),
        },
        "population": {
            "players": int(len(recency)),
            "source_game_rows_removed": source_rows_removed,
            "player_game_rows_after_source_exclusion": int(len(merged)),
        },
        "recent_activity": {
            "players_with_any_recent_activity": int(len(with_recent)),
            "players_with_categorized_recent_activity": int(
                len(with_categorized_recent)
            ),
            "players_with_recent_coverage_at_least_0_70": int(
                len(reliable)
            ),
            "recent_player_game_relations": total_recent_relations,
            "categorized_recent_player_game_relations": (
                categorized_recent_relations
            ),
            "aggregate_recent_playtime_minutes": total_recent_minutes,
            "aggregate_categorized_recent_playtime_minutes": (
                categorized_recent_minutes
            ),
            "aggregate_recent_playtime_coverage": safe_divide(
                categorized_recent_minutes, total_recent_minutes
            ),
            "players_by_minimum_recent_minutes": thresholds,
        },
        "distribution": {
            "recent_total_playtime_minutes": qstats(
                recency["recent_total_playtime_minutes"]
            ),
            "recent_category_playtime_coverage_among_active": qstats(
                with_recent["recent_category_playtime_coverage"]
            ),
            "recent_dominant_category_counts": {
                str(k): int(v)
                for k, v in with_categorized_recent[
                    "recent_dominant_category"
                ].value_counts().to_dict().items()
            },
        },
        "historical_comparison": {
            "comparable_players": int(len(comparable)),
            "same_dominant_category_count": same,
            "same_dominant_category_rate": (
                safe_divide(same, len(comparable))
                if len(comparable) > 0
                else None
            ),
            "different_dominant_category_count": int(
                len(comparable) - same
            ),
        },
        "outputs": {
            "player_recency_features": str(args.output),
        },
    }

    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(
        json.dumps(summary, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    print("\n=== RECENCY SIGNAL AUDIT ===")
    print(f"Players: {len(recency)}")
    print(
        f"Players with any recent activity: {len(with_recent)}"
    )
    print(
        "Players with categorized recent activity: "
        f"{len(with_categorized_recent)}"
    )
    print(
        "Players with recent coverage >= 0.70: "
        f"{len(reliable)}"
    )
    print(
        f"Recent player-game relations: {total_recent_relations}"
    )
    print(
        "Aggregate recent playtime coverage: "
        f"{summary['recent_activity']['aggregate_recent_playtime_coverage']:.4f}"
    )

    if len(comparable) > 0:
        rate = safe_divide(same, len(comparable))
        print(
            "Recent dominant == historical target: "
            f"{same}/{len(comparable)} ({rate:.4f})"
        )
    else:
        print("No historical target comparison was available.")

    print(f"\nSaved features: {args.output}")
    print(f"Saved summary: {args.summary}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())