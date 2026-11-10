"""09e - Fair V1 vs V2 benchmark with identical ML procedures.

Corrected version:
- V1 feature list is loaded from the frozen final model bundle
  models/final/macro_model_optimized.joblib
- V2 feature list is loaded from config/model_features_recency_v2.json

Both versions use:
- exactly the same train/test players;
- exactly the same target labels;
- exactly the same estimators;
- exactly the same resampling strategies;
- repeated stratified 5-fold CV with 3 repetitions;
- untouched held-out test set.

The historical final V1 model is NOT overwritten.
"""

from __future__ import annotations

import argparse
import json
import warnings
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

import joblib
import numpy as np
import pandas as pd

from imblearn.over_sampling import SMOTE
from imblearn.pipeline import Pipeline as ImbPipeline
from imblearn.under_sampling import RandomUnderSampler

from sklearn.base import BaseEstimator
from sklearn.metrics import (
    accuracy_score,
    balanced_accuracy_score,
    classification_report,
    confusion_matrix,
    f1_score,
    precision_score,
    recall_score,
)
from sklearn.model_selection import (
    RepeatedStratifiedKFold,
    cross_validate,
)
from sklearn.naive_bayes import GaussianNB
from sklearn.neighbors import KNeighborsClassifier
from sklearn.preprocessing import StandardScaler
from sklearn.tree import DecisionTreeClassifier
from sklearn.ensemble import RandomForestClassifier


ROOT = Path(__file__).resolve().parents[1]

DEFAULT_V1_TRAIN = ROOT / "data/modeling/macro_train.csv"
DEFAULT_V1_TEST = ROOT / "data/modeling/macro_test.csv"

DEFAULT_V2_TRAIN = (
    ROOT / "data/modeling/macro_train_recency_v2.csv"
)
DEFAULT_V2_TEST = (
    ROOT / "data/modeling/macro_test_recency_v2.csv"
)

DEFAULT_V1_MODEL = (
    ROOT / "models/final/macro_model_optimized.joblib"
)
DEFAULT_V2_CONFIG = (
    ROOT / "config/model_features_recency_v2.json"
)

DEFAULT_OUTPUT = (
    ROOT / "reports/metrics/09e_v1_v2_recency_model_comparison.csv"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/"
    "09e_v1_v2_recency_model_comparison_summary.json"
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

ID_CANDIDATES = (
    "final_player_id",
    "player_id",
)

TARGET_CANDIDATES = (
    "target_macro",
    "macro_target",
    "target",
    "resolved_target",
    "target_category",
)


V1_RATIO_FEATURES = [
    "game_share_combat",
    "game_share_exploration",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "played_game_share_strategic_reasoning",
]


def reconstruct_v1_ratio_features(df: pd.DataFrame) -> pd.DataFrame:
    """Rebuild the six optimized V1 ratio features from base counts."""
    result = df.copy()

    required = [
        "categorized_games",
        "categorized_played_games",
        "games_combat",
        "games_exploration",
        "games_strategic_reasoning",
        "played_games_combat",
        "played_games_exploration",
        "played_games_strategic_reasoning",
    ]

    missing = [
        column
        for column in required
        if column not in result.columns
    ]

    if missing:
        raise ValueError(
            "Cannot reconstruct V1 optimized ratio features. "
            f"Missing base columns: {missing}"
        )

    def ratio(numerator: pd.Series, denominator: pd.Series) -> pd.Series:
        numerator = pd.to_numeric(
            numerator,
            errors="coerce",
        ).fillna(0.0)

        denominator = pd.to_numeric(
            denominator,
            errors="coerce",
        ).fillna(0.0)

        output = pd.Series(
            0.0,
            index=result.index,
            dtype=float,
        )

        mask = denominator > 0

        output.loc[mask] = (
            numerator.loc[mask]
            / denominator.loc[mask]
        )

        return output

    result["game_share_combat"] = ratio(
        result["games_combat"],
        result["categorized_games"],
    )
    result["game_share_exploration"] = ratio(
        result["games_exploration"],
        result["categorized_games"],
    )
    result["game_share_strategic_reasoning"] = ratio(
        result["games_strategic_reasoning"],
        result["categorized_games"],
    )

    result["played_game_share_combat"] = ratio(
        result["played_games_combat"],
        result["categorized_played_games"],
    )
    result["played_game_share_exploration"] = ratio(
        result["played_games_exploration"],
        result["categorized_played_games"],
    )
    result["played_game_share_strategic_reasoning"] = ratio(
        result["played_games_strategic_reasoning"],
        result["categorized_played_games"],
    )

    return result



@dataclass(frozen=True)
class Candidate:
    algorithm: str
    strategy: str
    builder: Callable[[], BaseEstimator]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--v1-train",
        type=Path,
        default=DEFAULT_V1_TRAIN,
    )
    parser.add_argument(
        "--v1-test",
        type=Path,
        default=DEFAULT_V1_TEST,
    )
    parser.add_argument(
        "--v2-train",
        type=Path,
        default=DEFAULT_V2_TRAIN,
    )
    parser.add_argument(
        "--v2-test",
        type=Path,
        default=DEFAULT_V2_TEST,
    )
    parser.add_argument(
        "--v1-model",
        type=Path,
        default=DEFAULT_V1_MODEL,
    )
    parser.add_argument(
        "--v2-config",
        type=Path,
        default=DEFAULT_V2_CONFIG,
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=DEFAULT_OUTPUT,
    )
    parser.add_argument(
        "--summary",
        type=Path,
        default=DEFAULT_SUMMARY,
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
        f"Could not detect {label}. Tried {list(candidates)}."
    )


def load_v1_feature_list(model_path: Path) -> list[str]:
    if not model_path.exists():
        raise FileNotFoundError(model_path)

    bundle = joblib.load(model_path)

    if not isinstance(bundle, dict):
        raise ValueError(
            "Unexpected V1 final model artifact format."
        )

    features = bundle.get("features")

    if not isinstance(features, list) or not features:
        raise ValueError(
            "V1 final model artifact does not contain a valid "
            "'features' list."
        )

    return [str(feature) for feature in features]


def load_v2_feature_list(config_path: Path) -> list[str]:
    if not config_path.exists():
        raise FileNotFoundError(config_path)

    config = json.loads(
        config_path.read_text(encoding="utf-8")
    )

    features = config.get("v2", {}).get("features")

    if not isinstance(features, list) or not features:
        raise ValueError(
            "Could not find V2 feature list under config['v2']['features']."
        )

    return [str(feature) for feature in features]


def safe_numeric_matrix(
    df: pd.DataFrame,
    features: list[str],
) -> pd.DataFrame:
    missing = [
        feature
        for feature in features
        if feature not in df.columns
    ]

    if missing:
        raise ValueError(
            f"Dataset missing model features: {missing}"
        )

    X = df[features].apply(
        pd.to_numeric,
        errors="coerce",
    )

    if X.isna().any().any():
        bad = X.columns[
            X.isna().any()
        ].tolist()

        raise ValueError(
            "Non-numeric or missing values found in: "
            + ", ".join(bad)
        )

    if not np.isfinite(X.to_numpy()).all():
        raise ValueError(
            "Infinite values found in model matrix."
        )

    return X


def build_knn() -> BaseEstimator:
    return KNeighborsClassifier(
        n_neighbors=5,
        weights="distance",
        metric="minkowski",
        p=2,
    )


def build_dt(
    class_weight: str | None = None,
) -> BaseEstimator:
    return DecisionTreeClassifier(
        random_state=RANDOM_STATE,
        class_weight=class_weight,
    )


def build_rf(
    class_weight: str | None = None,
) -> BaseEstimator:
    return RandomForestClassifier(
        n_estimators=200,
        random_state=RANDOM_STATE,
        class_weight=class_weight,
        n_jobs=-1,
    )


def build_nb() -> BaseEstimator:
    return GaussianNB()


def make_pipeline(
    algorithm: str,
    strategy: str,
    estimator: BaseEstimator,
) -> BaseEstimator:
    steps: list[tuple[str, Any]] = []

    if algorithm in {"KNN", "GaussianNB"}:
        steps.append(
            ("scaler", StandardScaler())
        )

    if strategy in {
        "smote",
        "smote_under",
    }:
        steps.append(
            (
                "smote",
                SMOTE(
                    random_state=RANDOM_STATE,
                    k_neighbors=SMOTE_K,
                ),
            )
        )

    if strategy == "smote_under":
        def under_strategy(y):
            labels, counts = np.unique(
                y,
                return_counts=True,
            )
            target = max(
                1,
                int(max(counts) * 0.80),
            )

            return {
                label: min(
                    int(count),
                    target,
                )
                for label, count in zip(
                    labels,
                    counts,
                )
            }

        steps.append(
            (
                "under",
                RandomUnderSampler(
                    random_state=RANDOM_STATE,
                    sampling_strategy=under_strategy,
                ),
            )
        )

    steps.append(
        ("model", estimator)
    )

    return ImbPipeline(steps)


def candidates() -> list[Candidate]:
    return [
        Candidate(
            "KNN",
            "original",
            lambda: make_pipeline(
                "KNN", "original", build_knn()
            ),
        ),
        Candidate(
            "KNN",
            "smote",
            lambda: make_pipeline(
                "KNN", "smote", build_knn()
            ),
        ),
        Candidate(
            "KNN",
            "smote_under",
            lambda: make_pipeline(
                "KNN", "smote_under", build_knn()
            ),
        ),
        Candidate(
            "DecisionTree",
            "original",
            lambda: make_pipeline(
                "DecisionTree", "original", build_dt()
            ),
        ),
        Candidate(
            "DecisionTree",
            "class_weight",
            lambda: make_pipeline(
                "DecisionTree",
                "class_weight",
                build_dt("balanced"),
            ),
        ),
        Candidate(
            "DecisionTree",
            "smote",
            lambda: make_pipeline(
                "DecisionTree", "smote", build_dt()
            ),
        ),
        Candidate(
            "DecisionTree",
            "smote_under",
            lambda: make_pipeline(
                "DecisionTree", "smote_under", build_dt()
            ),
        ),
        Candidate(
            "RandomForest",
            "original",
            lambda: make_pipeline(
                "RandomForest", "original", build_rf()
            ),
        ),
        Candidate(
            "RandomForest",
            "class_weight",
            lambda: make_pipeline(
                "RandomForest",
                "class_weight",
                build_rf("balanced"),
            ),
        ),
        Candidate(
            "RandomForest",
            "smote",
            lambda: make_pipeline(
                "RandomForest", "smote", build_rf()
            ),
        ),
        Candidate(
            "RandomForest",
            "smote_under",
            lambda: make_pipeline(
                "RandomForest", "smote_under", build_rf()
            ),
        ),
        Candidate(
            "GaussianNB",
            "original",
            lambda: make_pipeline(
                "GaussianNB", "original", build_nb()
            ),
        ),
        Candidate(
            "GaussianNB",
            "smote",
            lambda: make_pipeline(
                "GaussianNB", "smote", build_nb()
            ),
        ),
        Candidate(
            "GaussianNB",
            "smote_under",
            lambda: make_pipeline(
                "GaussianNB", "smote_under", build_nb()
            ),
        ),
    ]


def evaluate_candidate(
    version: str,
    feature_count: int,
    candidate: Candidate,
    X_train: pd.DataFrame,
    y_train: pd.Series,
    X_test: pd.DataFrame,
    y_test: pd.Series,
) -> tuple[dict[str, Any], dict[str, Any]]:
    model = candidate.builder()

    cv = RepeatedStratifiedKFold(
        n_splits=CV_SPLITS,
        n_repeats=CV_REPEATS,
        random_state=RANDOM_STATE,
    )

    scoring = {
        "accuracy": "accuracy",
        "balanced_accuracy": "balanced_accuracy",
        "macro_f1": "f1_macro",
        "macro_precision": "precision_macro",
        "macro_recall": "recall_macro",
    }

    with warnings.catch_warnings():
        warnings.simplefilter("ignore")

        cv_result = cross_validate(
            model,
            X_train,
            y_train,
            cv=cv,
            scoring=scoring,
            n_jobs=-1,
            return_train_score=False,
            error_score="raise",
        )

    model.fit(
        X_train,
        y_train,
    )

    prediction = model.predict(X_test)

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

    row = {
        "version": version,
        "feature_count": feature_count,
        "algorithm": candidate.algorithm,
        "strategy": candidate.strategy,
        "cv_macro_f1_mean": float(
            np.mean(cv_result["test_macro_f1"])
        ),
        "cv_macro_f1_std": float(
            np.std(cv_result["test_macro_f1"])
        ),
        "cv_accuracy_mean": float(
            np.mean(cv_result["test_accuracy"])
        ),
        "cv_balanced_accuracy_mean": float(
            np.mean(cv_result["test_balanced_accuracy"])
        ),
        "cv_macro_precision_mean": float(
            np.mean(cv_result["test_macro_precision"])
        ),
        "cv_macro_recall_mean": float(
            np.mean(cv_result["test_macro_recall"])
        ),
        "test_macro_f1": float(
            f1_score(
                y_test,
                prediction,
                average="macro",
                zero_division=0,
            )
        ),
        "test_accuracy": float(
            accuracy_score(
                y_test,
                prediction,
            )
        ),
        "test_balanced_accuracy": float(
            balanced_accuracy_score(
                y_test,
                prediction,
            )
        ),
        "test_macro_precision": float(
            precision_score(
                y_test,
                prediction,
                average="macro",
                zero_division=0,
            )
        ),
        "test_macro_recall": float(
            recall_score(
                y_test,
                prediction,
                average="macro",
                zero_division=0,
            )
        ),
        "test_combat_f1": float(
            report["combat"]["f1-score"]
        ),
        "test_exploration_f1": float(
            report["exploration"]["f1-score"]
        ),
        "test_strategic_f1": float(
            report["strategic_reasoning"]["f1-score"]
        ),
        "test_strategic_recall": float(
            report["strategic_reasoning"]["recall"]
        ),
    }

    detail = {
        "version": version,
        "algorithm": candidate.algorithm,
        "strategy": candidate.strategy,
        "confusion_matrix_labels": CLASS_ORDER,
        "confusion_matrix": matrix,
        "classification_report": report,
    }

    return row, detail


def validate_split_parity(
    v1_train: pd.DataFrame,
    v1_test: pd.DataFrame,
    v2_train: pd.DataFrame,
    v2_test: pd.DataFrame,
) -> dict[str, bool]:
    v1_train_id = detect_column(
        v1_train,
        ID_CANDIDATES,
        "V1 train ID",
    )
    v1_test_id = detect_column(
        v1_test,
        ID_CANDIDATES,
        "V1 test ID",
    )
    v2_train_id = detect_column(
        v2_train,
        ID_CANDIDATES,
        "V2 train ID",
    )
    v2_test_id = detect_column(
        v2_test,
        ID_CANDIDATES,
        "V2 test ID",
    )

    v1_train_target = detect_column(
        v1_train,
        TARGET_CANDIDATES,
        "V1 train target",
    )
    v1_test_target = detect_column(
        v1_test,
        TARGET_CANDIDATES,
        "V1 test target",
    )
    v2_train_target = detect_column(
        v2_train,
        TARGET_CANDIDATES,
        "V2 train target",
    )
    v2_test_target = detect_column(
        v2_test,
        TARGET_CANDIDATES,
        "V2 test target",
    )

    return {
        "train_ids_identical": (
            v1_train[v1_train_id].astype(str).tolist()
            == v2_train[v2_train_id].astype(str).tolist()
        ),
        "test_ids_identical": (
            v1_test[v1_test_id].astype(str).tolist()
            == v2_test[v2_test_id].astype(str).tolist()
        ),
        "train_targets_identical": (
            v1_train[v1_train_target].astype(str).tolist()
            == v2_train[v2_train_target].astype(str).tolist()
        ),
        "test_targets_identical": (
            v1_test[v1_test_target].astype(str).tolist()
            == v2_test[v2_test_target].astype(str).tolist()
        ),
    }


def prepare_dataset(
    train: pd.DataFrame,
    test: pd.DataFrame,
    features: list[str],
    reconstruct_v1_ratios: bool = False,
):
    if reconstruct_v1_ratios:
        train = reconstruct_v1_ratio_features(train)
        test = reconstruct_v1_ratio_features(test)

    target_train = detect_column(
        train,
        TARGET_CANDIDATES,
        "train target",
    )
    target_test = detect_column(
        test,
        TARGET_CANDIDATES,
        "test target",
    )

    X_train = safe_numeric_matrix(
        train,
        features,
    )
    X_test = safe_numeric_matrix(
        test,
        features,
    )

    y_train = train[target_train].astype(str)
    y_test = test[target_test].astype(str)

    return X_train, y_train, X_test, y_test


def main() -> int:
    args = parse_args()

    v1_train = pd.read_csv(args.v1_train)
    v1_test = pd.read_csv(args.v1_test)
    v2_train = pd.read_csv(args.v2_train)
    v2_test = pd.read_csv(args.v2_test)

    v1_features = load_v1_feature_list(
        args.v1_model
    )
    v2_features = load_v2_feature_list(
        args.v2_config
    )

    parity = validate_split_parity(
        v1_train,
        v1_test,
        v2_train,
        v2_test,
    )

    if not all(parity.values()):
        raise ValueError(
            f"V1/V2 split parity failed: {parity}"
        )

    datasets = {
        "v1": prepare_dataset(
            v1_train,
            v1_test,
            v1_features,
            reconstruct_v1_ratios=True,
        ),
        "v2_recency": prepare_dataset(
            v2_train,
            v2_test,
            v2_features,
        ),
    }

    feature_counts = {
        "v1": len(v1_features),
        "v2_recency": len(v2_features),
    }

    rows: list[dict[str, Any]] = []
    details: list[dict[str, Any]] = []

    candidate_list = candidates()
    total_runs = len(datasets) * len(candidate_list)
    current = 0

    print("\n=== V1 vs V2 RECENCY FAIR BENCHMARK ===")
    print(f"V1 features: {len(v1_features)}")
    print(f"V2 features: {len(v2_features)}")
    print(f"Candidates per version: {len(candidate_list)}")
    print(f"Total evaluations: {total_runs}")
    print(f"CV: {CV_SPLITS} folds x {CV_REPEATS} repeats")

    for version, dataset in datasets.items():
        X_train, y_train, X_test, y_test = dataset

        print(f"\n--- {version} ---")

        for candidate in candidate_list:
            current += 1

            print(
                f"[{current:02d}/{total_runs:02d}] "
                f"{candidate.algorithm} + {candidate.strategy}"
            )

            row, detail = evaluate_candidate(
                version=version,
                feature_count=feature_counts[version],
                candidate=candidate,
                X_train=X_train,
                y_train=y_train,
                X_test=X_test,
                y_test=y_test,
            )

            rows.append(row)
            details.append(detail)

    results = pd.DataFrame(rows)

    results = results.sort_values(
        ["version", "cv_macro_f1_mean"],
        ascending=[True, False],
    ).reset_index(drop=True)

    args.output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    results.to_csv(
        args.output,
        index=False,
    )

    best_by_version: dict[str, Any] = {}

    for version in datasets:
        subset = results.loc[
            results["version"] == version
        ].sort_values(
            "cv_macro_f1_mean",
            ascending=False,
        )

        best = subset.iloc[0]

        best_by_version[version] = {
            "algorithm": str(best["algorithm"]),
            "strategy": str(best["strategy"]),
            "feature_count": int(best["feature_count"]),
            "cv_macro_f1_mean": float(
                best["cv_macro_f1_mean"]
            ),
            "cv_macro_f1_std": float(
                best["cv_macro_f1_std"]
            ),
            "cv_balanced_accuracy_mean": float(
                best["cv_balanced_accuracy_mean"]
            ),
            "test_macro_f1": float(
                best["test_macro_f1"]
            ),
            "test_balanced_accuracy": float(
                best["test_balanced_accuracy"]
            ),
            "test_strategic_f1": float(
                best["test_strategic_f1"]
            ),
            "test_strategic_recall": float(
                best["test_strategic_recall"]
            ),
        }

    v1_best = best_by_version["v1"]
    v2_best = best_by_version["v2_recency"]

    summary = {
        "stage": "09e_v1_v2_recency_model_comparison",
        "comparison_design": {
            "same_train_test_membership": True,
            "same_targets": True,
            "same_algorithms_and_strategies": True,
            "v1_features_source": str(args.v1_model),
            "v2_features_source": str(args.v2_config),
            "cv": {
                "type": "RepeatedStratifiedKFold",
                "splits": CV_SPLITS,
                "repeats": CV_REPEATS,
                "random_state": RANDOM_STATE,
            },
            "test_set_untouched_by_resampling": True,
            "selection_metric": "cv_macro_f1_mean",
        },
        "split_parity": parity,
        "feature_counts": feature_counts,
        "candidate_count_per_version": len(candidate_list),
        "total_evaluations": total_runs,
        "best_by_version": best_by_version,
        "best_v2_minus_best_v1": {
            "cv_macro_f1": (
                v2_best["cv_macro_f1_mean"]
                - v1_best["cv_macro_f1_mean"]
            ),
            "cv_balanced_accuracy": (
                v2_best["cv_balanced_accuracy_mean"]
                - v1_best["cv_balanced_accuracy_mean"]
            ),
            "test_macro_f1": (
                v2_best["test_macro_f1"]
                - v1_best["test_macro_f1"]
            ),
            "test_balanced_accuracy": (
                v2_best["test_balanced_accuracy"]
                - v1_best["test_balanced_accuracy"]
            ),
            "test_strategic_f1": (
                v2_best["test_strategic_f1"]
                - v1_best["test_strategic_f1"]
            ),
            "test_strategic_recall": (
                v2_best["test_strategic_recall"]
                - v1_best["test_strategic_recall"]
            ),
        },
        "details": details,
        "outputs": {
            "comparison_csv": str(args.output),
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

    print(
        "\n=== BEST BY VERSION (selected by CV macro F1) ==="
    )

    for version, best in best_by_version.items():
        print(
            f"\n{version}: "
            f"{best['algorithm']} + {best['strategy']}"
        )
        print(
            "  CV macro F1: "
            f"{best['cv_macro_f1_mean']:.6f} "
            f"± {best['cv_macro_f1_std']:.6f}"
        )
        print(
            "  CV balanced accuracy: "
            f"{best['cv_balanced_accuracy_mean']:.6f}"
        )
        print(
            "  Test macro F1: "
            f"{best['test_macro_f1']:.6f}"
        )
        print(
            "  Test balanced accuracy: "
            f"{best['test_balanced_accuracy']:.6f}"
        )
        print(
            "  Test strategic F1: "
            f"{best['test_strategic_f1']:.6f}"
        )
        print(
            "  Test strategic recall: "
            f"{best['test_strategic_recall']:.6f}"
        )

    delta = summary["best_v2_minus_best_v1"]

    print("\n=== V2 - V1 ===")
    print(
        "CV macro F1 delta: "
        f"{delta['cv_macro_f1']:+.6f}"
    )
    print(
        "CV balanced accuracy delta: "
        f"{delta['cv_balanced_accuracy']:+.6f}"
    )
    print(
        "Test macro F1 delta: "
        f"{delta['test_macro_f1']:+.6f}"
    )
    print(
        "Test strategic F1 delta: "
        f"{delta['test_strategic_f1']:+.6f}"
    )

    print(f"\nSaved comparison: {args.output}")
    print(f"Saved summary: {args.summary}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())