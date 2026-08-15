"""Fit the final Adaptive Trials macro model after model selection.

The modeling configuration is already frozen:
- feature set: baseline 34 + 6 count-ratio features;
- Random Forest with tuned hyperparameters;
- SMOTE with k_neighbors=3;
- random_state=42.

This script refits the selected configuration on all 686 resolved macro profiles
(train + former held-out test) for deployment. It does not generate new
performance estimates; evaluation evidence comes from stages 07b-07e.
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from imblearn.over_sampling import SMOTE
from imblearn.pipeline import Pipeline
from sklearn.ensemble import RandomForestClassifier

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_TRAIN = ROOT / "data/modeling/macro_train.csv"
DEFAULT_TEST = ROOT / "data/modeling/macro_test.csv"
DEFAULT_FEATURE_CONFIG = ROOT / "config/model_features_v1.json"
DEFAULT_MODEL_OUTPUT = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_METADATA_OUTPUT = ROOT / "models/final/macro_model_optimized_metadata.json"

TARGET = "macro_target"
RATIO_FEATURES = [
    "game_share_combat",
    "game_share_exploration",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "played_game_share_strategic_reasoning",
]

LOGGER = logging.getLogger("fit-final-macro-model")


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--train", type=Path, default=DEFAULT_TRAIN)
    parser.add_argument("--test", type=Path, default=DEFAULT_TEST)
    parser.add_argument("--feature-config", type=Path, default=DEFAULT_FEATURE_CONFIG)
    parser.add_argument("--model-output", type=Path, default=DEFAULT_MODEL_OUTPUT)
    parser.add_argument("--metadata-output", type=Path, default=DEFAULT_METADATA_OUTPUT)
    parser.add_argument("--random-state", type=int, default=42)
    return parser.parse_args()


def add_ratio_features(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    categorized_games = out["categorized_games"].replace(0, np.nan)
    categorized_played = out["categorized_played_games"].replace(0, np.nan)

    out["game_share_combat"] = (
        out["games_combat"] / categorized_games
    ).fillna(0.0)
    out["game_share_exploration"] = (
        out["games_exploration"] / categorized_games
    ).fillna(0.0)
    out["game_share_strategic_reasoning"] = (
        out["games_strategic_reasoning"] / categorized_games
    ).fillna(0.0)

    out["played_game_share_combat"] = (
        out["played_games_combat"] / categorized_played
    ).fillna(0.0)
    out["played_game_share_exploration"] = (
        out["played_games_exploration"] / categorized_played
    ).fillna(0.0)
    out["played_game_share_strategic_reasoning"] = (
        out["played_games_strategic_reasoning"] / categorized_played
    ).fillna(0.0)

    return out


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    args = parse_args()
    started = datetime.now(timezone.utc)

    try:
        train = pd.read_csv(args.train)
        test = pd.read_csv(args.test)
        combined = add_ratio_features(
            pd.concat([train, test], ignore_index=True)
        )

        with args.feature_config.open("r", encoding="utf-8") as file:
            baseline_features = json.load(file)["feature_fields"]

        features = baseline_features + RATIO_FEATURES

        X = combined[features].astype(float)
        y = combined[TARGET].astype(str)

        pipeline = Pipeline([
            (
                "smote",
                SMOTE(
                    random_state=args.random_state,
                    k_neighbors=3,
                ),
            ),
            (
                "model",
                RandomForestClassifier(
                    n_estimators=200,
                    max_depth=10,
                    min_samples_split=2,
                    min_samples_leaf=2,
                    max_features="sqrt",
                    random_state=args.random_state,
                    n_jobs=-1,
                ),
            ),
        ])

        pipeline.fit(X, y)

        args.model_output.parent.mkdir(parents=True, exist_ok=True)
        joblib.dump(
            {
                "pipeline": pipeline,
                "feature_set": "full_ratios",
                "features": features,
                "target": TARGET,
                "classes": sorted(y.unique().tolist()),
                "random_state": args.random_state,
            },
            args.model_output,
        )

        metadata = {
            "pipeline_stage": "07f_fit_final_macro_model",
            "started_at": started.isoformat(),
            "finished_at": datetime.now(timezone.utc).isoformat(),
            "model_role": "deployment_model",
            "evaluation_note": (
                "This model is refitted on all 686 resolved macro profiles only "
                "after the modeling configuration was frozen. No performance "
                "metric is computed from this refit. Reported evaluation metrics "
                "come from stages 07b through 07e."
            ),
            "training_profiles": int(len(combined)),
            "class_counts": {
                str(label): int(count)
                for label, count in sorted(Counter(y).items())
            },
            "feature_set": "full_ratios",
            "feature_count": len(features),
            "features": features,
            "smote": {
                "k_neighbors": 3,
                "random_state": args.random_state,
            },
            "random_forest": {
                "n_estimators": 200,
                "max_depth": 10,
                "min_samples_split": 2,
                "min_samples_leaf": 2,
                "max_features": "sqrt",
                "random_state": args.random_state,
            },
            "model_output": str(args.model_output),
            "selection_evidence": {
                "baseline_seed_macro_f1_mean": 0.6151783298972864,
                "optimized_seed_macro_f1_mean": 0.6526192790754323,
                "baseline_seed_strategic_f1_mean": 0.43162348925204963,
                "optimized_seed_strategic_f1_mean": 0.5070853121651198,
                "baseline_seed_strategic_recall_mean": 0.4427272727272727,
                "optimized_seed_strategic_recall_mean": 0.5636363636363636,
            },
        }

        with args.metadata_output.open("w", encoding="utf-8") as file:
            json.dump(metadata, file, ensure_ascii=False, indent=2)

    except Exception as error:
        LOGGER.exception("Final macro model fit failed: %s", error)
        return 1

    LOGGER.info("Final deployment model fitted with %d profiles.", len(combined))
    LOGGER.info("Model: %s", args.model_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())