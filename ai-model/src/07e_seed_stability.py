"""Compare baseline and optimized macro models across multiple random seeds.

This script evaluates two fixed configurations using repeated stratified CV only:
1) baseline Random Forest + SMOTE, feature set baseline_34;
2) optimized Random Forest + SMOTE, feature set full_ratios.

No held-out test metrics are used for selection in this stage.

Outputs:
- reports/metrics/07e_seed_stability_results.csv
- reports/metrics/07e_seed_stability_summary.json
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from imblearn.over_sampling import SMOTE
from imblearn.pipeline import Pipeline
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import make_scorer, f1_score, recall_score
from sklearn.model_selection import RepeatedStratifiedKFold, cross_validate

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_TRAIN = ROOT / "data/modeling/macro_train.csv"
DEFAULT_FEATURE_CONFIG = ROOT / "config/model_features_v1.json"
DEFAULT_OUTPUT = ROOT / "reports/metrics/07e_seed_stability_results.csv"
DEFAULT_SUMMARY = ROOT / "reports/metrics/07e_seed_stability_summary.json"

TARGET = "macro_target"
SEEDS = [11, 21, 42, 73, 101]

GLOBAL_FEATURES = [
    "total_library_games",
    "total_played_games",
    "total_playtime_minutes",
    "categorized_games",
    "categorized_played_games",
    "category_game_coverage",
    "category_playtime_coverage",
    "avg_playtime_per_played_game_minutes",
]

MACRO_COUNT_FEATURES = [
    "games_combat",
    "played_games_combat",
    "games_exploration",
    "played_games_exploration",
    "games_strategic_reasoning",
    "played_games_strategic_reasoning",
]

RATIO_FEATURES = [
    "game_share_combat",
    "game_share_exploration",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "played_game_share_strategic_reasoning",
]

LOGGER = logging.getLogger("seed-stability")


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--train", type=Path, default=DEFAULT_TRAIN)
    parser.add_argument("--feature-config", type=Path, default=DEFAULT_FEATURE_CONFIG)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--cv-splits", type=int, default=5)
    parser.add_argument("--cv-repeats", type=int, default=3)
    parser.add_argument("--n-jobs", type=int, default=-1)
    return parser.parse_args()


def add_ratio_features(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    cg = out["categorized_games"].replace(0, np.nan)
    cpg = out["categorized_played_games"].replace(0, np.nan)

    out["game_share_combat"] = (out["games_combat"] / cg).fillna(0.0)
    out["game_share_exploration"] = (out["games_exploration"] / cg).fillna(0.0)
    out["game_share_strategic_reasoning"] = (
        out["games_strategic_reasoning"] / cg
    ).fillna(0.0)

    out["played_game_share_combat"] = (
        out["played_games_combat"] / cpg
    ).fillna(0.0)
    out["played_game_share_exploration"] = (
        out["played_games_exploration"] / cpg
    ).fillna(0.0)
    out["played_game_share_strategic_reasoning"] = (
        out["played_games_strategic_reasoning"] / cpg
    ).fillna(0.0)

    return out


def build_pipeline(name: str, seed: int) -> Pipeline:
    if name == "baseline":
        return Pipeline([
            ("smote", SMOTE(random_state=seed, k_neighbors=5)),
            ("model", RandomForestClassifier(
                n_estimators=300,
                min_samples_leaf=2,
                random_state=seed,
                n_jobs=-1,
            )),
        ])

    if name == "optimized":
        return Pipeline([
            ("smote", SMOTE(random_state=seed, k_neighbors=3)),
            ("model", RandomForestClassifier(
                n_estimators=200,
                max_depth=10,
                min_samples_split=2,
                min_samples_leaf=2,
                max_features="sqrt",
                random_state=seed,
                n_jobs=-1,
            )),
        ])

    raise ValueError(name)


def summarize_group(group: pd.DataFrame) -> dict[str, Any]:
    return {
        "seed_count": int(group["seed"].nunique()),
        "macro_f1_mean_across_seeds": float(group["cv_macro_f1_mean"].mean()),
        "macro_f1_std_across_seeds": float(group["cv_macro_f1_mean"].std(ddof=0)),
        "balanced_accuracy_mean_across_seeds": float(
            group["cv_balanced_accuracy_mean"].mean()
        ),
        "strategic_f1_mean_across_seeds": float(
            group["cv_strategic_f1_mean"].mean()
        ),
        "strategic_recall_mean_across_seeds": float(
            group["cv_strategic_recall_mean"].mean()
        ),
        "best_seed_macro_f1": float(group["cv_macro_f1_mean"].max()),
        "worst_seed_macro_f1": float(group["cv_macro_f1_mean"].min()),
    }


def main() -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )
    args = parse_args()
    started = datetime.now(timezone.utc)

    try:
        train = add_ratio_features(pd.read_csv(args.train))
        with args.feature_config.open("r", encoding="utf-8") as file:
            baseline_features = json.load(file)["feature_fields"]

        feature_sets = {
            "baseline": baseline_features,
            "optimized": baseline_features + RATIO_FEATURES,
        }

        y = train[TARGET].astype(str)

        strategic_f1 = make_scorer(
            f1_score,
            labels=["strategic_reasoning"],
            average=None,
            zero_division=0,
        )
        strategic_recall = make_scorer(
            recall_score,
            labels=["strategic_reasoning"],
            average=None,
            zero_division=0,
        )

        rows = []

        for config_name in ["baseline", "optimized"]:
            for seed in SEEDS:
                LOGGER.info(
                    "Evaluating config=%s seed=%d",
                    config_name,
                    seed,
                )

                pipeline = build_pipeline(config_name, seed)
                cv = RepeatedStratifiedKFold(
                    n_splits=args.cv_splits,
                    n_repeats=args.cv_repeats,
                    random_state=seed,
                )

                scores = cross_validate(
                    pipeline,
                    train[feature_sets[config_name]].astype(float),
                    y,
                    cv=cv,
                    scoring={
                        "macro_f1": "f1_macro",
                        "balanced_accuracy": "balanced_accuracy",
                        "strategic_f1": strategic_f1,
                        "strategic_recall": strategic_recall,
                    },
                    n_jobs=args.n_jobs,
                    error_score="raise",
                )

                # custom scorers with average=None return arrays per fold in some
                # sklearn versions; flatten defensively.
                strategic_f1_values = np.array(scores["test_strategic_f1"], dtype=float).reshape(-1)
                strategic_recall_values = np.array(scores["test_strategic_recall"], dtype=float).reshape(-1)

                rows.append({
                    "configuration": config_name,
                    "seed": seed,
                    "feature_count": len(feature_sets[config_name]),
                    "cv_macro_f1_mean": float(np.mean(scores["test_macro_f1"])),
                    "cv_macro_f1_std": float(np.std(scores["test_macro_f1"])),
                    "cv_balanced_accuracy_mean": float(
                        np.mean(scores["test_balanced_accuracy"])
                    ),
                    "cv_strategic_f1_mean": float(np.mean(strategic_f1_values)),
                    "cv_strategic_recall_mean": float(
                        np.mean(strategic_recall_values)
                    ),
                })

        results = pd.DataFrame(rows)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        results.to_csv(args.output, index=False)

        summary_by_config = {
            name: summarize_group(results[results["configuration"] == name])
            for name in ["baseline", "optimized"]
        }

        winner = max(
            summary_by_config,
            key=lambda name: (
                summary_by_config[name]["macro_f1_mean_across_seeds"],
                summary_by_config[name]["strategic_f1_mean_across_seeds"],
                -summary_by_config[name]["macro_f1_std_across_seeds"],
            ),
        )

        summary = {
            "pipeline_stage": "07e_seed_stability",
            "started_at": started.isoformat(),
            "finished_at": datetime.now(timezone.utc).isoformat(),
            "seeds": SEEDS,
            "selection_policy": (
                "highest mean repeated-CV macro F1 across seeds; "
                "strategic F1 and lower variability used as secondary criteria"
            ),
            "configurations": {
                "baseline": {
                    "features": "baseline_34",
                    "random_forest": {
                        "n_estimators": 300,
                        "min_samples_leaf": 2,
                    },
                    "smote_k_neighbors": 5,
                },
                "optimized": {
                    "features": "full_ratios",
                    "random_forest": {
                        "n_estimators": 200,
                        "max_depth": 10,
                        "min_samples_split": 2,
                        "min_samples_leaf": 2,
                        "max_features": "sqrt",
                    },
                    "smote_k_neighbors": 3,
                },
            },
            "summary_by_configuration": summary_by_config,
            "recommended_configuration": winner,
            "methodological_note": (
                "This stage does not use the held-out test set. "
                "Only the training partition is evaluated via repeated "
                "stratified cross-validation across multiple random seeds."
            ),
            "outputs": {
                "seed_results": str(args.output),
            },
        }

        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        with args.summary_output.open("w", encoding="utf-8") as file:
            json.dump(summary, file, ensure_ascii=False, indent=2)

    except Exception as error:
        LOGGER.exception("Seed stability analysis failed: %s", error)
        return 1

    LOGGER.info("Seed stability analysis completed.")
    LOGGER.info("Recommended configuration: %s", winner)
    return 0


if __name__ == "__main__":
    sys.exit(main())