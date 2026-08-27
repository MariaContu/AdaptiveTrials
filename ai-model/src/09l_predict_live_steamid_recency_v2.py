"""09l - Live SteamID inference with the final recency V2 macro model.

Flow
----
Steam Web API GetOwnedGames
    -> lifetime + two-week playtime
    -> final game mapping
    -> 49 deployment features
    -> final recency V2 Random Forest
    -> predict + predict_proba

Production note
---------------
No source-game exclusion is applied in live inference because a real user
entering a SteamID was not recruited through a source game.

Requirements
------------
- STEAM_API_KEY in .env
- requests
- python-dotenv

Inputs
------
- models/final/macro_model_recency_v2.joblib
- data/interim/final_game_category_scores_v2.csv

Outputs
-------
- terminal prediction
- optional JSON under reports/metrics/
"""

from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
import requests
from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_MODEL = (
    ROOT / "models/final/macro_model_recency_v2.joblib"
)
DEFAULT_MAPPING = (
    ROOT / "data/interim/final_game_category_scores_v2.csv"
)
DEFAULT_OUTPUT = (
    ROOT / "reports/metrics/09l_live_recency_v2_prediction.json"
)

STEAM_OWNED_GAMES_URL = (
    "https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/"
)

STEAM_RESOLVE_VANITY_URL = (
    "https://api.steampowered.com/ISteamUser/ResolveVanityURL/v0001/"
)

DEFAULT_TIMEOUT = 30.0
MIN_CATEGORY_PLAYTIME_COVERAGE = 0.70

MACRO_CATEGORIES = (
    "combat",
    "exploration",
    "strategic_reasoning",
)

SUBGROUPS = (
    "shooter",
    "melee",
    "action_other",
    "open_world",
    "narrative_exploration",
    "investigation",
    "logic_puzzle",
    "planning_management",
    "strategy_decision",
    "observation_deduction",
)

SUBGROUP_COLUMN_CANDIDATES = (
    "assigned_subgroup",
    "subgroup",
    "dominant_subgroup",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()

    parser.add_argument(
        "steam_id",
        type=str,
        help=(
            "SteamID64, custom Steam profile name, "
            "or Steam Community profile URL."
        ),
    )
    parser.add_argument(
        "--model",
        type=Path,
        default=DEFAULT_MODEL,
    )
    parser.add_argument(
        "--mapping",
        type=Path,
        default=DEFAULT_MAPPING,
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=DEFAULT_TIMEOUT,
    )
    parser.add_argument(
        "--min-coverage",
        type=float,
        default=MIN_CATEGORY_PLAYTIME_COVERAGE,
    )

    return parser.parse_args()


def detect_column(
    df: pd.DataFrame,
    candidates: tuple[str, ...],
    label: str,
) -> str:
    for candidate in candidates:
        if candidate in df.columns:
            return candidate

    raise ValueError(
        f"Could not detect {label}. Tried: {list(candidates)}"
    )


def safe_float(value: Any) -> float:
    try:
        if value is None or pd.isna(value):
            return 0.0

        result = float(value)

        if not math.isfinite(result):
            return 0.0

        return result

    except (TypeError, ValueError):
        return 0.0


def safe_divide(
    numerator: float,
    denominator: float,
) -> float:
    if denominator <= 0:
        return 0.0

    return float(
        numerator / denominator
    )


def normalize_subgroup(
    value: Any,
) -> str:
    if pd.isna(value):
        return ""

    text = (
        str(value)
        .strip()
        .lower()
    )

    aliases = {
        "shooting": "shooter",
        "tiro": "shooter",
        "melee_combat": "melee",
        "other_action": "action_other",
        "mundo_aberto": "open_world",
        "narrative": "narrative_exploration",
        "logic_and_puzzle": "logic_puzzle",
        "planning_and_management": "planning_management",
        "strategy_and_decision": "strategy_decision",
        "observation_and_deduction": "observation_deduction",
    }

    return aliases.get(
        text,
        text,
    )


def normalize_steam_identifier(
    value: str,
) -> str:
    """Accept SteamID64, vanity name, or a Steam Community profile URL."""
    text = str(value).strip()

    if not text:
        raise ValueError(
            "Steam identifier cannot be empty."
        )

    text = text.rstrip("/")

    # Accept:
    # https://steamcommunity.com/profiles/7656119...
    # https://steamcommunity.com/id/customname
    lowered = text.lower()

    profiles_marker = "steamcommunity.com/profiles/"
    vanity_marker = "steamcommunity.com/id/"

    if profiles_marker in lowered:
        index = lowered.index(profiles_marker)
        text = text[
            index + len(profiles_marker):
        ].split("/")[0].strip()

    elif vanity_marker in lowered:
        index = lowered.index(vanity_marker)
        text = text[
            index + len(vanity_marker):
        ].split("/")[0].strip()

    if not text:
        raise ValueError(
            "Could not extract a Steam identifier from the supplied value."
        )

    return text


def resolve_steam_identifier(
    identifier: str,
    api_key: str,
    timeout: float,
) -> tuple[str, str]:
    """Return (steamid64, input_type).

    Numeric values are treated directly as SteamID64.
    Non-numeric values are resolved through ResolveVanityURL.
    """
    normalized = normalize_steam_identifier(
        identifier
    )

    if normalized.isdigit():
        if len(normalized) < 16 or len(normalized) > 20:
            raise ValueError(
                "Numeric SteamID has an unexpected length."
            )

        return normalized, "steamid64"

    response = requests.get(
        STEAM_RESOLVE_VANITY_URL,
        params={
            "key": api_key,
            "vanityurl": normalized,
            "url_type": 1,
        },
        timeout=timeout,
    )

    response.raise_for_status()

    payload = response.json()
    data = payload.get(
        "response",
        {},
    )

    success = int(
        safe_float(
            data.get(
                "success",
                0,
            )
        )
    )

    resolved = str(
        data.get(
            "steamid",
            "",
        )
    ).strip()

    if success != 1 or not resolved.isdigit():
        message = str(
            data.get(
                "message",
                "Vanity Steam identifier could not be resolved.",
            )
        )

        raise ValueError(
            "Could not resolve Steam custom identifier "
            f"{normalized!r}: {message}"
        )

    return resolved, "vanity"

def load_model_bundle(
    path: Path,
) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(
            path
        )

    bundle = joblib.load(
        path
    )

    if not isinstance(
        bundle,
        dict,
    ):
        raise ValueError(
            "Unexpected V2 model bundle format."
        )

    required = {
        "pipeline",
        "features",
        "classes",
    }

    missing = (
        required
        - set(bundle.keys())
    )

    if missing:
        raise ValueError(
            "Final V2 model bundle missing keys: "
            f"{sorted(missing)}"
        )

    features = bundle[
        "features"
    ]

    if (
        not isinstance(features, list)
        or not features
    ):
        raise ValueError(
            "Final V2 model bundle has no valid feature list."
        )

    return bundle


def load_mapping(
    path: Path,
) -> pd.DataFrame:
    mapping = pd.read_csv(
        path
    )

    if "appid" not in mapping.columns:
        raise ValueError(
            "Mapping has no appid column."
        )

    if (
        "assigned_category"
        not in mapping.columns
    ):
        raise ValueError(
            "Mapping has no assigned_category column."
        )

    subgroup_column = detect_column(
        mapping,
        SUBGROUP_COLUMN_CANDIDATES,
        "mapping subgroup",
    )

    mapping = mapping.copy()

    mapping["appid"] = (
        pd.to_numeric(
            mapping["appid"],
            errors="coerce",
        )
        .fillna(0)
        .astype(int)
    )

    mapping[
        "assigned_category"
    ] = (
        mapping[
            "assigned_category"
        ]
        .fillna("")
        .astype(str)
        .str.strip()
    )

    mapping[
        "_normalized_subgroup"
    ] = mapping[
        subgroup_column
    ].apply(
        normalize_subgroup
    )

    return (
        mapping[
            [
                "appid",
                "assigned_category",
                "_normalized_subgroup",
            ]
        ]
        .drop_duplicates(
            subset=["appid"]
        )
        .reset_index(
            drop=True
        )
    )


def fetch_owned_games(
    steam_id: str,
    api_key: str,
    timeout: float,
) -> pd.DataFrame:
    params = {
        "key": api_key,
        "steamid": steam_id,
        "format": "json",
        "include_appinfo": 0,
        "include_played_free_games": 1,
    }

    response = requests.get(
        STEAM_OWNED_GAMES_URL,
        params=params,
        timeout=timeout,
    )

    response.raise_for_status()

    payload = response.json()

    data = payload.get(
        "response",
        {},
    )

    games = data.get(
        "games"
    )

    if games is None:
        raise ValueError(
            "Steam did not return an accessible game library. "
            "The profile may be private or unavailable."
        )

    if not games:
        raise ValueError(
            "Steam returned an empty game library."
        )

    rows = []

    for game in games:
        appid = int(
            safe_float(
                game.get(
                    "appid",
                    0,
                )
            )
        )

        if appid <= 0:
            continue

        rows.append(
            {
                "appid": appid,
                "playtime_forever_minutes": max(
                    0.0,
                    safe_float(
                        game.get(
                            "playtime_forever",
                            0,
                        )
                    ),
                ),
                "playtime_2weeks_minutes": max(
                    0.0,
                    safe_float(
                        game.get(
                            "playtime_2weeks",
                            0,
                        )
                    ),
                ),
            }
        )

    if not rows:
        raise ValueError(
            "Steam returned no usable games for this profile."
        )

    return pd.DataFrame(
        rows
    )


def build_feature_vector(
    games: pd.DataFrame,
    mapping: pd.DataFrame,
) -> tuple[
    dict[str, float],
    dict[str, Any],
    dict[str, Any],
]:
    merged = games.merge(
        mapping,
        on="appid",
        how="left",
        validate="many_to_one",
    )

    merged[
        "assigned_category"
    ] = (
        merged[
            "assigned_category"
        ]
        .fillna("")
        .astype(str)
        .str.strip()
    )

    merged[
        "_normalized_subgroup"
    ] = (
        merged[
            "_normalized_subgroup"
        ]
        .fillna("")
        .astype(str)
    )

    merged[
        "_lifetime"
    ] = (
        pd.to_numeric(
            merged[
                "playtime_forever_minutes"
            ],
            errors="coerce",
        )
        .fillna(0.0)
        .clip(lower=0.0)
    )

    merged[
        "_recent"
    ] = (
        pd.to_numeric(
            merged[
                "playtime_2weeks_minutes"
            ],
            errors="coerce",
        )
        .fillna(0.0)
        .clip(lower=0.0)
    )

    merged[
        "_played"
    ] = (
        merged[
            "_lifetime"
        ] > 0
    )

    merged[
        "_categorized"
    ] = merged[
        "assigned_category"
    ].isin(
        MACRO_CATEGORIES
    )

    vector: dict[
        str,
        float,
    ] = {}

    total_library_games = int(
        len(merged)
    )

    total_played_games = int(
        merged[
            "_played"
        ].sum()
    )

    total_playtime = float(
        merged[
            "_lifetime"
        ].sum()
    )

    categorized = merged.loc[
        merged[
            "_categorized"
        ]
    ]

    categorized_played = (
        categorized.loc[
            categorized[
                "_played"
            ]
        ]
    )

    categorized_games = int(
        len(categorized)
    )

    categorized_played_games = int(
        len(
            categorized_played
        )
    )

    categorized_playtime = float(
        categorized[
            "_lifetime"
        ].sum()
    )

    vector[
        "total_library_games"
    ] = float(
        total_library_games
    )

    vector[
        "total_played_games"
    ] = float(
        total_played_games
    )

    vector[
        "total_playtime_minutes"
    ] = total_playtime

    vector[
        "categorized_games"
    ] = float(
        categorized_games
    )

    vector[
        "categorized_played_games"
    ] = float(
        categorized_played_games
    )

    vector[
        "category_game_coverage"
    ] = safe_divide(
        categorized_games,
        total_library_games,
    )

    vector[
        "category_playtime_coverage"
    ] = safe_divide(
        categorized_playtime,
        total_playtime,
    )

    vector[
        "avg_playtime_per_played_game_minutes"
    ] = safe_divide(
        total_playtime,
        total_played_games,
    )

    for category in (
        MACRO_CATEGORIES
    ):
        subset = merged.loc[
            merged[
                "assigned_category"
            ]
            == category
        ]

        vector[
            f"games_{category}"
        ] = float(
            len(subset)
        )

        vector[
            f"played_games_{category}"
        ] = float(
            subset[
                "_played"
            ].sum()
        )

    for subgroup in SUBGROUPS:
        subset = merged.loc[
            merged[
                "_normalized_subgroup"
            ]
            == subgroup
        ]

        vector[
            f"games_{subgroup}"
        ] = float(
            len(subset)
        )

        vector[
            f"played_games_{subgroup}"
        ] = float(
            subset[
                "_played"
            ].sum()
        )

    for category in (
        MACRO_CATEGORIES
    ):
        vector[
            f"game_share_{category}"
        ] = safe_divide(
            vector[
                f"games_{category}"
            ],
            categorized_games,
        )

        vector[
            f"played_game_share_{category}"
        ] = safe_divide(
            vector[
                f"played_games_{category}"
            ],
            categorized_played_games,
        )

    recent_rows = merged.loc[
        merged[
            "_recent"
        ] > 0
    ]

    recent_categorized = (
        recent_rows.loc[
            recent_rows[
                "_categorized"
            ]
        ]
    )

    recent_total_minutes = float(
        recent_rows[
            "_recent"
        ].sum()
    )

    recent_categorized_minutes = float(
        recent_categorized[
            "_recent"
        ].sum()
    )

    vector[
        "has_recent_activity"
    ] = float(
        recent_total_minutes > 0
    )

    vector[
        "recent_total_playtime_minutes"
    ] = recent_total_minutes

    vector[
        "recent_active_games"
    ] = float(
        len(recent_rows)
    )

    vector[
        "recent_categorized_playtime_minutes"
    ] = recent_categorized_minutes

    vector[
        "recent_categorized_active_games"
    ] = float(
        len(
            recent_categorized
        )
    )

    vector[
        "recent_category_playtime_coverage"
    ] = safe_divide(
        recent_categorized_minutes,
        recent_total_minutes,
    )

    for category in (
        MACRO_CATEGORIES
    ):
        vector[
            f"recent_active_games_{category}"
        ] = float(
            len(
                recent_categorized.loc[
                    recent_categorized[
                        "assigned_category"
                    ]
                    == category
                ]
            )
        )

    library_summary = {
        "num_games": total_library_games,
        "num_played_games": (
            total_played_games
        ),
        "total_playtime_hours": (
            total_playtime
            / 60.0
        ),
        "games_combat": int(
            vector[
                "games_combat"
            ]
        ),
        "games_exploration": int(
            vector[
                "games_exploration"
            ]
        ),
        "games_strategic_reasoning": int(
            vector[
                "games_strategic_reasoning"
            ]
        ),
        "hours_combat": float(
            merged.loc[
                merged[
                    "assigned_category"
                ]
                == "combat",
                "_lifetime",
            ].sum()
            / 60.0
        ),
        "hours_exploration": float(
            merged.loc[
                merged[
                    "assigned_category"
                ]
                == "exploration",
                "_lifetime",
            ].sum()
            / 60.0
        ),
        "hours_strategic_reasoning": float(
            merged.loc[
                merged[
                    "assigned_category"
                ]
                == "strategic_reasoning",
                "_lifetime",
            ].sum()
            / 60.0
        ),
    }

    recent_summary = {
        "has_recent_activity": bool(
            recent_total_minutes > 0
        ),
        "recent_total_playtime_minutes": (
            recent_total_minutes
        ),
        "recent_total_playtime_hours": (
            recent_total_minutes
            / 60.0
        ),
        "recent_active_games": int(
            len(recent_rows)
        ),
        "recent_categorized_playtime_minutes": (
            recent_categorized_minutes
        ),
        "recent_categorized_active_games": int(
            len(
                recent_categorized
            )
        ),
        "recent_category_playtime_coverage": (
            safe_divide(
                recent_categorized_minutes,
                recent_total_minutes,
            )
        ),
        "recent_active_games_combat": int(
            vector[
                "recent_active_games_combat"
            ]
        ),
        "recent_active_games_exploration": int(
            vector[
                "recent_active_games_exploration"
            ]
        ),
        "recent_active_games_strategic_reasoning": int(
            vector[
                "recent_active_games_strategic_reasoning"
            ]
        ),
    }

    return (
        vector,
        library_summary,
        recent_summary,
    )


def vector_to_frame(
    vector: dict[str, float],
    features: list[str],
) -> pd.DataFrame:
    missing = [
        feature
        for feature in features
        if feature not in vector
    ]

    if missing:
        raise ValueError(
            "Live reconstruction did not produce "
            f"all model features: {missing}"
        )

    values = {
        feature: [
            float(
                vector[
                    feature
                ]
            )
        ]
        for feature in features
    }

    X = pd.DataFrame(
        values,
        columns=features,
    )

    if not np.isfinite(
        X.to_numpy()
    ).all():
        raise ValueError(
            "Live feature vector contains "
            "non-finite values."
        )

    return X


def main() -> int:
    args = parse_args()

    supplied_identifier = str(
        args.steam_id
    ).strip()

    load_dotenv(
        ROOT / ".env"
    )

    api_key = (
        os.getenv(
            "STEAM_API_KEY"
        )
        or os.getenv(
            "STEAM_WEB_API_KEY"
        )
    )

    if not api_key:
        raise RuntimeError(
            "Steam API key not found. "
            "Set STEAM_API_KEY in ai-model/.env."
        )

    steam_id, identifier_type = resolve_steam_identifier(
        identifier=supplied_identifier,
        api_key=api_key,
        timeout=args.timeout,
    )

    bundle = load_model_bundle(
        args.model
    )

    mapping = load_mapping(
        args.mapping
    )

    games = fetch_owned_games(
        steam_id=steam_id,
        api_key=api_key,
        timeout=args.timeout,
    )

    (
        vector,
        library_summary,
        recent_summary,
    ) = build_feature_vector(
        games=games,
        mapping=mapping,
    )

    historical_coverage = float(
        vector[
            "category_playtime_coverage"
        ]
    )

    if (
        historical_coverage
        < args.min_coverage
    ):
        raise ValueError(
            "Insufficient categorized lifetime "
            "playtime coverage for model inference: "
            f"{historical_coverage:.4f} "
            f"< {args.min_coverage:.4f}."
        )

    features = [
        str(feature)
        for feature in bundle[
            "features"
        ]
    ]

    X = vector_to_frame(
        vector,
        features,
    )

    model = bundle[
        "pipeline"
    ]

    prediction = str(
        model.predict(
            X
        )[0]
    )

    raw_probabilities = (
        model.predict_proba(
            X
        )[0]
    )

    classes = [
        str(value)
        for value in model.classes_
    ]

    probabilities = {
        label: float(
            probability
        )
        for (
            label,
            probability,
        ) in zip(
            classes,
            raw_probabilities,
        )
    }

    result = {
        "stage": (
            "09l_live_recency_v2_inference"
        ),
        "status": "ok",
        "steam_identifier_input": supplied_identifier,
        "steam_identifier_type": identifier_type,
        "steam_id": steam_id,
        "model_version": bundle.get(
            "version",
            "recency_v2",
        ),
        "feature_set": bundle.get(
            "feature_set"
        ),
        "feature_count": len(
            features
        ),
        "predicted_category": (
            prediction
        ),
        "probabilities": (
            probabilities
        ),
        "profile_quality": {
            "minimum_required_playtime_coverage": (
                args.min_coverage
            ),
            "category_game_coverage": float(
                vector[
                    "category_game_coverage"
                ]
            ),
            "category_playtime_coverage": (
                historical_coverage
            ),
        },
        "library_summary": (
            library_summary
        ),
        "recent_summary": (
            recent_summary
        ),
    }

    args.output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    args.output.write_text(
        json.dumps(
            result,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print(
        "\n=== LIVE STEAM RECENCY V2 INFERENCE ==="
    )

    print(
        f"Input: {supplied_identifier}"
    )

    print(
        f"Resolved SteamID64: {steam_id}"
    )

    print(
        f"Identifier type: {identifier_type}"
    )

    print(
        "Model version: "
        f"{result['model_version']}"
    )

    print(
        "Feature set: "
        f"{result['feature_set']}"
    )

    print(
        "Feature count: "
        f"{result['feature_count']}"
    )

    print(
        "\nLibrary:"
    )

    print(
        "  games: "
        f"{library_summary['num_games']}"
    )

    print(
        "  played games: "
        f"{library_summary['num_played_games']}"
    )

    print(
        "  total playtime hours: "
        f"{library_summary['total_playtime_hours']:.2f}"
    )

    print(
        "  game coverage: "
        f"{vector['category_game_coverage']:.6f}"
    )

    print(
        "  playtime coverage: "
        f"{historical_coverage:.6f}"
    )

    print(
        "\nRecent activity:"
    )

    print(
        "  has recent activity: "
        f"{recent_summary['has_recent_activity']}"
    )

    print(
        "  recent hours: "
        f"{recent_summary['recent_total_playtime_hours']:.2f}"
    )

    print(
        "  recent active games: "
        f"{recent_summary['recent_active_games']}"
    )

    print(
        "  categorized recent games: "
        f"{recent_summary['recent_categorized_active_games']}"
    )

    print(
        "  recent coverage: "
        f"{recent_summary['recent_category_playtime_coverage']:.6f}"
    )

    print(
        "  combat recent games: "
        f"{recent_summary['recent_active_games_combat']}"
    )

    print(
        "  exploration recent games: "
        f"{recent_summary['recent_active_games_exploration']}"
    )

    print(
        "  strategic recent games: "
        f"{recent_summary['recent_active_games_strategic_reasoning']}"
    )

    print(
        "\nPrediction:"
    )

    print(
        f"  {prediction}"
    )

    print(
        "\nProbabilities:"
    )

    for label in sorted(
        probabilities.keys()
    ):
        print(
            f"  {label}: "
            f"{probabilities[label]:.8f}"
        )

    print(
        f"\nSaved prediction: {args.output}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())