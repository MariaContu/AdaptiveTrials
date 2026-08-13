"""Optimize macro Random Forest + SMOTE without touching the held-out test until selection."""
from __future__ import annotations

import argparse
import itertools
import json
import logging
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
from imblearn.over_sampling import SMOTE
from imblearn.pipeline import Pipeline
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import accuracy_score, balanced_accuracy_score, classification_report, confusion_matrix, f1_score
from sklearn.model_selection import RepeatedStratifiedKFold, cross_validate

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TRAIN = ROOT / "data/modeling/macro_train.csv"
DEFAULT_TEST = ROOT / "data/modeling/macro_test.csv"
DEFAULT_FEATURE_CONFIG = ROOT / "config/model_features_v1.json"
DEFAULT_RESULTS = ROOT / "reports/metrics/07d_model_optimization_results.csv"
DEFAULT_SUMMARY = ROOT / "reports/metrics/07d_model_optimization_summary.json"
DEFAULT_MODEL = ROOT / "models/optimized/macro_random_forest_smote.joblib"

LOGGER = logging.getLogger("macro-model-optimization")
TARGET = "macro_target"

GLOBAL_FEATURES = [
    "total_library_games","total_played_games","total_playtime_minutes","categorized_games",
    "categorized_played_games","category_game_coverage","category_playtime_coverage",
    "avg_playtime_per_played_game_minutes",
]
MACRO_COUNT_FEATURES = [
    "games_combat","played_games_combat","games_exploration","played_games_exploration",
    "games_strategic_reasoning","played_games_strategic_reasoning",
]
RATIO_FEATURES = [
    "game_share_combat","game_share_exploration","game_share_strategic_reasoning",
    "played_game_share_combat","played_game_share_exploration","played_game_share_strategic_reasoning",
]

def parse_args():
    p = argparse.ArgumentParser()
    p.add_argument("--train", type=Path, default=DEFAULT_TRAIN)
    p.add_argument("--test", type=Path, default=DEFAULT_TEST)
    p.add_argument("--feature-config", type=Path, default=DEFAULT_FEATURE_CONFIG)
    p.add_argument("--results-output", type=Path, default=DEFAULT_RESULTS)
    p.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    p.add_argument("--model-output", type=Path, default=DEFAULT_MODEL)
    p.add_argument("--random-state", type=int, default=42)
    p.add_argument("--cv-splits", type=int, default=5)
    p.add_argument("--cv-repeats", type=int, default=3)
    p.add_argument("--n-jobs", type=int, default=-1)
    return p.parse_args()

def add_ratio_features(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    cg = out["categorized_games"].replace(0, np.nan)
    cpg = out["categorized_played_games"].replace(0, np.nan)
    out["game_share_combat"] = (out["games_combat"] / cg).fillna(0.0)
    out["game_share_exploration"] = (out["games_exploration"] / cg).fillna(0.0)
    out["game_share_strategic_reasoning"] = (out["games_strategic_reasoning"] / cg).fillna(0.0)
    out["played_game_share_combat"] = (out["played_games_combat"] / cpg).fillna(0.0)
    out["played_game_share_exploration"] = (out["played_games_exploration"] / cpg).fillna(0.0)
    out["played_game_share_strategic_reasoning"] = (out["played_games_strategic_reasoning"] / cpg).fillna(0.0)
    return out

def build_feature_sets(baseline_features: list[str]) -> dict[str, list[str]]:
    macro_only = GLOBAL_FEATURES + MACRO_COUNT_FEATURES
    return {
        "baseline_34": baseline_features,
        "macro_only": macro_only,
        "macro_ratios": macro_only + RATIO_FEATURES,
        "full_ratios": baseline_features + RATIO_FEATURES,
    }

def build_param_grid():
    combos = itertools.product(
        [200, 400],
        [None, 10, 16],
        [2, 5],
        [1, 2],
        ["sqrt", "log2"],
        [3, 5],
    )
    return [
        {
            "n_estimators": a,
            "max_depth": b,
            "min_samples_split": c,
            "min_samples_leaf": d,
            "max_features": e,
            "smote_k_neighbors": f,
        }
        for a,b,c,d,e,f in combos
    ]

def build_pipeline(params: dict[str, Any], random_state: int) -> Pipeline:
    return Pipeline([
        ("smote", SMOTE(random_state=random_state, k_neighbors=params["smote_k_neighbors"])),
        ("model", RandomForestClassifier(
            n_estimators=params["n_estimators"],
            max_depth=params["max_depth"],
            min_samples_split=params["min_samples_split"],
            min_samples_leaf=params["min_samples_leaf"],
            max_features=params["max_features"],
            random_state=random_state,
            n_jobs=-1,
        )),
    ])

def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")
    args = parse_args()
    started = datetime.now(timezone.utc)
    try:
        train = add_ratio_features(pd.read_csv(args.train))
        test = add_ratio_features(pd.read_csv(args.test))
        with args.feature_config.open("r", encoding="utf-8") as f:
            baseline = json.load(f)["feature_fields"]

        feature_sets = build_feature_sets(baseline)
        params_grid = build_param_grid()
        cv = RepeatedStratifiedKFold(
            n_splits=args.cv_splits,
            n_repeats=args.cv_repeats,
            random_state=args.random_state,
        )
        y_train = train[TARGET].astype(str)
        y_test = test[TARGET].astype(str)

        rows = []
        best = None
        total = len(feature_sets) * len(params_grid)
        done = 0

        for fs_name, features in feature_sets.items():
            X_train = train[features].astype(float)
            for params in params_grid:
                done += 1
                if done == 1 or done % 50 == 0:
                    LOGGER.info("Optimization progress: %d/%d", done, total)

                pipe = build_pipeline(params, args.random_state)
                cvres = cross_validate(
                    pipe, X_train, y_train, cv=cv,
                    scoring={"macro_f1":"f1_macro","balanced_accuracy":"balanced_accuracy"},
                    n_jobs=args.n_jobs, error_score="raise",
                )
                row = {
                    "feature_set": fs_name,
                    **params,
                    "feature_count": len(features),
                    "cv_macro_f1_mean": float(np.mean(cvres["test_macro_f1"])),
                    "cv_macro_f1_std": float(np.std(cvres["test_macro_f1"])),
                    "cv_balanced_accuracy_mean": float(np.mean(cvres["test_balanced_accuracy"])),
                }
                rows.append(row)
                key = (row["cv_macro_f1_mean"], -row["cv_macro_f1_std"], row["cv_balanced_accuracy_mean"])
                if best is None or key > (
                    best["cv_macro_f1_mean"], -best["cv_macro_f1_std"], best["cv_balanced_accuracy_mean"]
                ):
                    best = row

        assert best is not None
        best_features = feature_sets[best["feature_set"]]
        best_params = {k: best[k] for k in [
            "n_estimators","max_depth","min_samples_split","min_samples_leaf","max_features","smote_k_neighbors"
        ]}

        final_pipe = build_pipeline(best_params, args.random_state)
        final_pipe.fit(train[best_features].astype(float), y_train)
        preds = final_pipe.predict(test[best_features].astype(float))

        labels = ["combat","exploration","strategic_reasoning"]
        final_test = {
            "macro_f1": float(f1_score(y_test, preds, average="macro", zero_division=0)),
            "accuracy": float(accuracy_score(y_test, preds)),
            "balanced_accuracy": float(balanced_accuracy_score(y_test, preds)),
            "confusion_matrix": confusion_matrix(y_test, preds, labels=labels).tolist(),
            "classification_report": classification_report(
                y_test, preds, labels=labels, output_dict=True, zero_division=0
            ),
        }

        args.results_output.parent.mkdir(parents=True, exist_ok=True)
        pd.DataFrame(rows).sort_values(
            ["cv_macro_f1_mean","cv_macro_f1_std"], ascending=[False,True]
        ).to_csv(args.results_output, index=False)

        args.model_output.parent.mkdir(parents=True, exist_ok=True)
        joblib.dump({
            "pipeline": final_pipe,
            "feature_set": best["feature_set"],
            "features": best_features,
            "parameters": best_params,
        }, args.model_output)

        summary = {
            "pipeline_stage": "07d_optimize_macro_model",
            "started_at": started.isoformat(),
            "finished_at": datetime.now(timezone.utc).isoformat(),
            "selection_policy": "highest repeated-CV macro F1; held-out test evaluated once after selection",
            "search_space": {
                "feature_sets": list(feature_sets),
                "configurations_per_feature_set": len(params_grid),
                "total_configurations": total,
                "cv_splits": args.cv_splits,
                "cv_repeats": args.cv_repeats,
            },
            "baseline_reference": {
                "cv_macro_f1_mean": 0.62388,
                "cv_macro_f1_std": 0.03736,
                "test_macro_f1": 0.654276,
            },
            "best_configuration": best,
            "best_features": best_features,
            "final_test": final_test,
            "outputs": {
                "results": str(args.results_output),
                "optimized_model": str(args.model_output),
            },
        }
        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        with args.summary_output.open("w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=2)

    except Exception as e:
        LOGGER.exception("Optimization failed: %s", e)
        return 1

    LOGGER.info("Optimization completed. Best CV macro F1=%.6f", best["cv_macro_f1_mean"])
    LOGGER.info("Final held-out macro F1=%.6f", final_test["macro_f1"])
    return 0

if __name__ == "__main__":
    sys.exit(main())