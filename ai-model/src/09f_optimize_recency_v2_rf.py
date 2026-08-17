"""09f - Optimize Random Forest recency model V2.

Goal
----
Optimize the V2 recency formulation without touching the held-out test set
during model selection.

Selection is based ONLY on repeated stratified CV macro F1 over the training
set. The held-out test set is evaluated once, after the best configuration
has been selected.

Feature sets
------------
- v1_40: historical baseline (control)
- recency_global_46: V1 + 6 general recency/coverage signals
- recency_counts_49: previous + 3 category-specific recent active-game counts
- recency_full_safe_52: previous + 3 recent active-game share features

Strategies
----------
- class_weight
- SMOTE

Hyperparameter search
---------------------
Focused around the frozen V1 final Random Forest:
- n_estimators = 200
- max_depth = [8, 10, 12, None]
- min_samples_split = [2, 5]
- min_samples_leaf = [1, 2, 4]
- max_features = "sqrt"

Total: 4 feature sets x 2 strategies x 24 RF configurations = 192 configs.

Outputs
-------
- reports/metrics/09f_recency_v2_rf_optimization.csv
- reports/metrics/09f_recency_v2_rf_optimization_summary.json
- models/experimental/macro_model_recency_v2_candidate.joblib
"""

from __future__ import annotations

import argparse
import itertools
import json
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd

from imblearn.over_sampling import SMOTE
from imblearn.pipeline import Pipeline as ImbPipeline

from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import (
    accuracy_score,
    balanced_accuracy_score,
    classification_report,
    confusion_matrix,
    f1_score,
)
from sklearn.model_selection import (
    RepeatedStratifiedKFold,
    cross_validate,
)

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_TRAIN = ROOT / "data/modeling/macro_train_recency_v2.csv"
DEFAULT_TEST = ROOT / "data/modeling/macro_test_recency_v2.csv"
DEFAULT_V1_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_V2_CONFIG = ROOT / "config/model_features_recency_v2.json"

DEFAULT_RESULTS = (
    ROOT / "reports/metrics/09f_recency_v2_rf_optimization.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/09f_recency_v2_rf_optimization_summary.json"
)
DEFAULT_MODEL_OUT = (
    ROOT / "models/experimental/macro_model_recency_v2_candidate.joblib"
)

RANDOM_STATE = 42
CV_SPLITS = 5
CV_REPEATS = 3
SMOTE_K = 3

CLASS_ORDER = [
    "combat",
    "exploration",
    "strategic_reasoning",
]

TARGET_CANDIDATES = (
    "target_macro",
    "macro_target",
    "target",
    "resolved_target",
    "target_category",
)

GLOBAL_RECENCY = [
    "has_recent_activity",
    "recent_total_playtime_minutes",
    "recent_active_games",
    "recent_categorized_playtime_minutes",
    "recent_categorized_active_games",
    "recent_category_playtime_coverage",
]

RECENT_CATEGORY_COUNTS = [
    "recent_active_games_combat",
    "recent_active_games_exploration",
    "recent_active_games_strategic_reasoning",
]

RECENT_CATEGORY_SHARES = [
    "recent_active_game_share_combat",
    "recent_active_game_share_exploration",
    "recent_active_game_share_strategic_reasoning",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--train", type=Path, default=DEFAULT_TRAIN)
    parser.add_argument("--test", type=Path, default=DEFAULT_TEST)
    parser.add_argument("--v1-model", type=Path, default=DEFAULT_V1_MODEL)
    parser.add_argument("--v2-config", type=Path, default=DEFAULT_V2_CONFIG)
    parser.add_argument("--results", type=Path, default=DEFAULT_RESULTS)
    parser.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--model-output", type=Path, default=DEFAULT_MODEL_OUT)
    return parser.parse_args()


def detect_target(df: pd.DataFrame) -> str:
    for candidate in TARGET_CANDIDATES:
        if candidate in df.columns:
            return candidate
    raise ValueError(
        f"Could not detect target column. Tried {list(TARGET_CANDIDATES)}."
    )


def load_v1_features(path: Path) -> list[str]:
    bundle = joblib.load(path)
    if not isinstance(bundle, dict):
        raise ValueError("Unexpected V1 model bundle format.")
    features = bundle.get("features")
    if not isinstance(features, list) or not features:
        raise ValueError("V1 model bundle has no valid 'features' list.")
    return [str(x) for x in features]


def load_v2_features(path: Path) -> list[str]:
    config = json.loads(path.read_text(encoding="utf-8"))
    features = config.get("v2", {}).get("features")
    if not isinstance(features, list) or not features:
        raise ValueError("V2 config has no valid feature list.")
    return [str(x) for x in features]


def numeric_matrix(
    df: pd.DataFrame,
    features: list[str],
) -> pd.DataFrame:
    missing = [f for f in features if f not in df.columns]
    if missing:
        raise ValueError(f"Missing features: {missing}")

    X = df[features].apply(pd.to_numeric, errors="coerce")
    if X.isna().any().any():
        bad = X.columns[X.isna().any()].tolist()
        raise ValueError(f"Invalid/missing values in: {bad}")

    if not np.isfinite(X.to_numpy()).all():
        raise ValueError("Infinite values found.")

    return X


def build_feature_sets(
    v1_features: list[str],
    v2_features: list[str],
) -> dict[str, list[str]]:
    sets = {
        "v1_40": list(v1_features),
        "recency_global_46": [
            *v1_features,
            *GLOBAL_RECENCY,
        ],
        "recency_counts_49": [
            *v1_features,
            *GLOBAL_RECENCY,
            *RECENT_CATEGORY_COUNTS,
        ],
        "recency_full_safe_52": [
            *v1_features,
            *GLOBAL_RECENCY,
            *RECENT_CATEGORY_COUNTS,
            *RECENT_CATEGORY_SHARES,
        ],
    }

    for name, features in sets.items():
        duplicates = [
            f for f in set(features)
            if features.count(f) > 1
        ]
        if duplicates:
            raise ValueError(
                f"Duplicate features in {name}: {duplicates}"
            )

        missing_from_v2 = [
            f for f in features if f not in v2_features
        ]
        if missing_from_v2:
            raise ValueError(
                f"{name} uses features absent from V2 config: "
                f"{missing_from_v2}"
            )

    return sets


def build_pipeline(
    strategy: str,
    params: dict[str, Any],
) -> ImbPipeline:
    steps: list[tuple[str, Any]] = []

    if strategy == "smote":
        steps.append(
            (
                "smote",
                SMOTE(
                    random_state=RANDOM_STATE,
                    k_neighbors=SMOTE_K,
                ),
            )
        )
        class_weight = None
    elif strategy == "class_weight":
        class_weight = "balanced"
    else:
        raise ValueError(f"Unknown strategy: {strategy}")

    model = RandomForestClassifier(
        n_estimators=params["n_estimators"],
        max_depth=params["max_depth"],
        min_samples_split=params["min_samples_split"],
        min_samples_leaf=params["min_samples_leaf"],
        max_features=params["max_features"],
        class_weight=class_weight,
        random_state=RANDOM_STATE,
        n_jobs=-1,
    )

    steps.append(("model", model))
    return ImbPipeline(steps)


def parameter_grid() -> list[dict[str, Any]]:
    values = {
        "n_estimators": [200],
        "max_depth": [8, 10, 12, None],
        "min_samples_split": [2, 5],
        "min_samples_leaf": [1, 2, 4],
        "max_features": ["sqrt"],
    }

    keys = list(values.keys())
    configs = []

    for combination in itertools.product(
        *(values[key] for key in keys)
    ):
        configs.append(dict(zip(keys, combination)))

    return configs


def main() -> int:
    args = parse_args()

    train = pd.read_csv(args.train)
    test = pd.read_csv(args.test)

    target_train = detect_target(train)
    target_test = detect_target(test)

    if target_train != target_test:
        raise ValueError(
            f"Train/test target mismatch: {target_train} vs {target_test}"
        )

    target = target_train

    v1_features = load_v1_features(args.v1_model)
    v2_features = load_v2_features(args.v2_config)

    feature_sets = build_feature_sets(
        v1_features,
        v2_features,
    )

    y_train = train[target].astype(str)
    y_test = test[target].astype(str)

    grids = parameter_grid()
    strategies = ["class_weight", "smote"]

    total = (
        len(feature_sets)
        * len(strategies)
        * len(grids)
    )

    print("\n=== RECENCY V2 RF OPTIMIZATION ===")
    print(f"Train rows: {len(train)}")
    print(f"Test rows: {len(test)}")
    print(f"Feature sets: {len(feature_sets)}")
    print(f"Strategies: {len(strategies)}")
    print(f"RF parameter configs: {len(grids)}")
    print(f"Total configurations: {total}")
    print(
        f"CV: {CV_SPLITS} folds x {CV_REPEATS} repeats"
    )
    print(
        "Selection: CV macro F1 only; test remains untouched "
        "until final candidate selection."
    )

    cv = RepeatedStratifiedKFold(
        n_splits=CV_SPLITS,
        n_repeats=CV_REPEATS,
        random_state=RANDOM_STATE,
    )

    scoring = {
        "macro_f1": "f1_macro",
        "balanced_accuracy": "balanced_accuracy",
    }

    rows: list[dict[str, Any]] = []
    current = 0

    for set_name, features in feature_sets.items():
        X_train = numeric_matrix(train, features)

        print(
            f"\n--- {set_name} ({len(features)} features) ---"
        )

        for strategy in strategies:
            for params in grids:
                current += 1

                pipeline = build_pipeline(
                    strategy,
                    params,
                )

                result = cross_validate(
                    pipeline,
                    X_train,
                    y_train,
                    cv=cv,
                    scoring=scoring,
                    n_jobs=-1,
                    return_train_score=False,
                    error_score="raise",
                )

                row = {
                    "feature_set": set_name,
                    "feature_count": len(features),
                    "strategy": strategy,
                    **params,
                    "cv_macro_f1_mean": float(
                        np.mean(result["test_macro_f1"])
                    ),
                    "cv_macro_f1_std": float(
                        np.std(result["test_macro_f1"])
                    ),
                    "cv_balanced_accuracy_mean": float(
                        np.mean(
                            result["test_balanced_accuracy"]
                        )
                    ),
                    "cv_balanced_accuracy_std": float(
                        np.std(
                            result["test_balanced_accuracy"]
                        )
                    ),
                }

                rows.append(row)

                if (
                    current == 1
                    or current % 12 == 0
                    or current == total
                ):
                    print(
                        f"[{current:03d}/{total:03d}] "
                        f"{strategy} | "
                        f"depth={params['max_depth']} | "
                        f"split={params['min_samples_split']} | "
                        f"leaf={params['min_samples_leaf']} | "
                        f"CV F1={row['cv_macro_f1_mean']:.4f}"
                    )

    results = pd.DataFrame(rows)
    results = results.sort_values(
        [
            "cv_macro_f1_mean",
            "cv_balanced_accuracy_mean",
        ],
        ascending=[False, False],
    ).reset_index(drop=True)

    best = results.iloc[0].to_dict()

    best_feature_set = str(best["feature_set"])
    best_features = feature_sets[best_feature_set]
    best_strategy = str(best["strategy"])

    best_params = {
        "n_estimators": int(best["n_estimators"]),
        "max_depth": (
            None
            if pd.isna(best["max_depth"])
            else int(best["max_depth"])
        ),
        "min_samples_split": int(best["min_samples_split"]),
        "min_samples_leaf": int(best["min_samples_leaf"]),
        "max_features": str(best["max_features"]),
    }

    final_pipeline = build_pipeline(
        best_strategy,
        best_params,
    )

    X_train_best = numeric_matrix(
        train,
        best_features,
    )
    X_test_best = numeric_matrix(
        test,
        best_features,
    )

    final_pipeline.fit(
        X_train_best,
        y_train,
    )

    prediction = final_pipeline.predict(
        X_test_best
    )

    report = classification_report(
        y_test,
        prediction,
        labels=CLASS_ORDER,
        output_dict=True,
        zero_division=0,
    )

    matrix = confusion_matrix(
        y_test,
        prediction,
        labels=CLASS_ORDER,
    ).tolist()

    test_metrics = {
        "macro_f1": float(
            f1_score(
                y_test,
                prediction,
                average="macro",
                zero_division=0,
            )
        ),
        "accuracy": float(
            accuracy_score(
                y_test,
                prediction,
            )
        ),
        "balanced_accuracy": float(
            balanced_accuracy_score(
                y_test,
                prediction,
            )
        ),
        "strategic_f1": float(
            report["strategic_reasoning"]["f1-score"]
        ),
        "strategic_recall": float(
            report["strategic_reasoning"]["recall"]
        ),
        "classification_report": report,
        "confusion_matrix_labels": CLASS_ORDER,
        "confusion_matrix": matrix,
    }

    args.results.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    results.to_csv(
        args.results,
        index=False,
    )

    candidate_bundle = {
        "pipeline": final_pipeline,
        "features": best_features,
        "feature_set": best_feature_set,
        "classes": CLASS_ORDER,
        "random_state": RANDOM_STATE,
        "strategy": best_strategy,
        "rf_params": best_params,
        "status": "experimental_candidate_not_deployment",
    }

    args.model_output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    joblib.dump(
        candidate_bundle,
        args.model_output,
    )

    top10 = []
    for _, row in results.head(10).iterrows():
        top10.append(
            {
                "feature_set": str(row["feature_set"]),
                "feature_count": int(row["feature_count"]),
                "strategy": str(row["strategy"]),
                "n_estimators": int(row["n_estimators"]),
                "max_depth": (
                    None
                    if pd.isna(row["max_depth"])
                    else int(row["max_depth"])
                ),
                "min_samples_split": int(
                    row["min_samples_split"]
                ),
                "min_samples_leaf": int(
                    row["min_samples_leaf"]
                ),
                "max_features": str(
                    row["max_features"]
                ),
                "cv_macro_f1_mean": float(
                    row["cv_macro_f1_mean"]
                ),
                "cv_macro_f1_std": float(
                    row["cv_macro_f1_std"]
                ),
                "cv_balanced_accuracy_mean": float(
                    row["cv_balanced_accuracy_mean"]
                ),
            }
        )

    summary = {
        "stage": "09f_recency_v2_rf_optimization",
        "selection_policy": {
            "metric": "cv_macro_f1_mean",
            "test_used_for_selection": False,
            "cv": {
                "type": "RepeatedStratifiedKFold",
                "splits": CV_SPLITS,
                "repeats": CV_REPEATS,
                "random_state": RANDOM_STATE,
            },
        },
        "search": {
            "feature_sets": {
                key: len(value)
                for key, value in feature_sets.items()
            },
            "strategies": strategies,
            "rf_parameter_configs": len(grids),
            "total_configurations": total,
        },
        "best_cv_candidate": {
            "feature_set": best_feature_set,
            "feature_count": len(best_features),
            "strategy": best_strategy,
            "rf_params": best_params,
            "cv_macro_f1_mean": float(
                best["cv_macro_f1_mean"]
            ),
            "cv_macro_f1_std": float(
                best["cv_macro_f1_std"]
            ),
            "cv_balanced_accuracy_mean": float(
                best["cv_balanced_accuracy_mean"]
            ),
        },
        "held_out_test_after_selection": test_metrics,
        "top_10_cv_candidates": top10,
        "outputs": {
            "results_csv": str(args.results),
            "candidate_model": str(args.model_output),
        },
    }

    args.summary.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    args.summary.write_text(
        json.dumps(
            summary,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print("\n=== BEST V2 CANDIDATE BY CV ===")
    print(f"Feature set: {best_feature_set}")
    print(f"Feature count: {len(best_features)}")
    print(f"Strategy: {best_strategy}")
    print(f"RF params: {best_params}")
    print(
        "CV macro F1: "
        f"{best['cv_macro_f1_mean']:.6f} "
        f"± {best['cv_macro_f1_std']:.6f}"
    )
    print(
        "CV balanced accuracy: "
        f"{best['cv_balanced_accuracy_mean']:.6f}"
    )

    print("\n=== HELD-OUT TEST (after selection) ===")
    print(
        f"Macro F1: {test_metrics['macro_f1']:.6f}"
    )
    print(
        f"Accuracy: {test_metrics['accuracy']:.6f}"
    )
    print(
        "Balanced accuracy: "
        f"{test_metrics['balanced_accuracy']:.6f}"
    )
    print(
        "Strategic F1: "
        f"{test_metrics['strategic_f1']:.6f}"
    )
    print(
        "Strategic recall: "
        f"{test_metrics['strategic_recall']:.6f}"
    )

    print(f"\nSaved results: {args.results}")
    print(f"Saved summary: {args.summary}")
    print(
        "Saved EXPERIMENTAL candidate: "
        f"{args.model_output}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())