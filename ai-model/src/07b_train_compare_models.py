"""Train and compare Adaptive Trials classification models.

Evaluates:
- K-Nearest Neighbors
- Decision Tree
- Random Forest
- Gaussian Naive Bayes

For each supported task, compares:
- original training distribution;
- SMOTE;
- SMOTE followed by undersampling;
- class weights for compatible tree models.

All preprocessing and resampling occur inside imbalanced-learn pipelines.
The held-out test set is never resampled.
"""

from __future__ import annotations

import argparse
import csv
import json
import logging
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
from imblearn.over_sampling import SMOTE
from imblearn.pipeline import Pipeline
from imblearn.under_sampling import RandomUnderSampler
from sklearn.base import clone
from sklearn.compose import ColumnTransformer
from sklearn.metrics import (
    accuracy_score,
    balanced_accuracy_score,
    classification_report,
    confusion_matrix,
    f1_score,
    precision_score,
    recall_score,
)
from sklearn.model_selection import RepeatedStratifiedKFold, cross_validate
from sklearn.naive_bayes import GaussianNB
from sklearn.neighbors import KNeighborsClassifier
from sklearn.preprocessing import StandardScaler
from sklearn.tree import DecisionTreeClassifier
from sklearn.ensemble import RandomForestClassifier


ROOT = Path(__file__).resolve().parents[1]

DEFAULT_FEATURE_CONFIG = ROOT / "config/model_features_v1.json"
DEFAULT_OUTPUT = ROOT / "reports/metrics/07_model_comparison_results.csv"
DEFAULT_SUMMARY = ROOT / "reports/metrics/07_model_comparison_summary.json"
DEFAULT_CONFUSIONS = ROOT / "reports/metrics/07_confusion_matrices.json"
DEFAULT_MODELS_DIR = ROOT / "models/candidates"

LOGGER = logging.getLogger("model-comparison")

TASKS = {
    "macro": {
        "train": ROOT / "data/modeling/macro_train.csv",
        "test": ROOT / "data/modeling/macro_test.csv",
        "target": "macro_target",
        "labels": ["combat", "exploration", "strategic_reasoning"],
    },
    "exploration_granular": {
        "train": ROOT / "data/modeling/exploration_granular_train.csv",
        "test": ROOT / "data/modeling/exploration_granular_test.csv",
        "target": "exploration_subgroup_target",
        "labels": [
            "investigation",
            "narrative_exploration",
            "open_world",
        ],
    },
}

RESULT_FIELDS = [
    "task",
    "model",
    "strategy",
    "cv_macro_f1_mean",
    "cv_macro_f1_std",
    "cv_accuracy_mean",
    "cv_accuracy_std",
    "cv_balanced_accuracy_mean",
    "cv_balanced_accuracy_std",
    "test_macro_f1",
    "test_weighted_f1",
    "test_accuracy",
    "test_balanced_accuracy",
    "test_macro_precision",
    "test_macro_recall",
    "train_size",
    "test_size",
    "train_class_counts_json",
    "test_class_counts_json",
    "model_path",
]


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s | %(levelname)s | %(message)s",
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Train and compare the four classification algorithms."
    )
    parser.add_argument(
        "--feature-config",
        type=Path,
        default=DEFAULT_FEATURE_CONFIG,
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument(
        "--confusions-output",
        type=Path,
        default=DEFAULT_CONFUSIONS,
    )
    parser.add_argument(
        "--models-dir",
        type=Path,
        default=DEFAULT_MODELS_DIR,
    )
    parser.add_argument("--random-state", type=int, default=42)
    parser.add_argument("--cv-splits", type=int, default=5)
    parser.add_argument("--cv-repeats", type=int, default=3)
    parser.add_argument("--n-jobs", type=int, default=-1)
    return parser.parse_args()


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)
    with path.open("r", encoding="utf-8") as file:
        return json.load(file)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)
    temporary.replace(path)


def write_csv(
    path: Path,
    fieldnames: list[str],
    rows: list[dict[str, Any]],
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    temporary.replace(path)


def class_counts(values: pd.Series) -> dict[str, int]:
    return {
        str(label): int(count)
        for label, count in sorted(Counter(values).items())
    }


def model_definitions(random_state: int) -> dict[str, Any]:
    return {
        "knn": KNeighborsClassifier(
            n_neighbors=7,
            weights="distance",
            metric="minkowski",
            p=2,
        ),
        "decision_tree": DecisionTreeClassifier(
            random_state=random_state,
            max_depth=None,
            min_samples_leaf=2,
        ),
        "random_forest": RandomForestClassifier(
            n_estimators=300,
            random_state=random_state,
            min_samples_leaf=2,
            n_jobs=-1,
        ),
        "naive_bayes": GaussianNB(),
    }


def strategies_for_model(model_name: str) -> list[str]:
    base = ["original", "smote", "smote_under"]
    if model_name in {"decision_tree", "random_forest"}:
        base.append("class_weight")
    return base


def build_pipeline(
    model_name: str,
    model: Any,
    strategy: str,
    feature_names: list[str],
    y_train: pd.Series,
    random_state: int,
) -> Pipeline:
    steps: list[tuple[str, Any]] = []

    if model_name in {"knn", "naive_bayes"}:
        steps.append(("scaler", StandardScaler()))

    fitted_model = clone(model)

    if strategy == "class_weight":
        fitted_model.set_params(class_weight="balanced")

    minority_count = int(y_train.value_counts().min())
    smote_neighbors = max(1, min(5, minority_count - 1))

    if strategy in {"smote", "smote_under"}:
        steps.append(
            (
                "smote",
                SMOTE(
                    random_state=random_state,
                    k_neighbors=smote_neighbors,
                ),
            )
        )

    if strategy == "smote_under":
        counts = y_train.value_counts()
        target_size = int(np.median(counts.values))
        sampling_strategy = {
            str(label): target_size
            for label, count in counts.items()
            if count > target_size
        }
        if sampling_strategy:
            steps.append(
                (
                    "undersampler",
                    RandomUnderSampler(
                        sampling_strategy=sampling_strategy,
                        random_state=random_state,
                    ),
                )
            )

    steps.append(("model", fitted_model))
    return Pipeline(steps)


def evaluate_configuration(
    task_name: str,
    task_config: dict[str, Any],
    feature_names: list[str],
    model_name: str,
    model: Any,
    strategy: str,
    args: argparse.Namespace,
) -> tuple[dict[str, Any], dict[str, Any]]:
    train_df = pd.read_csv(task_config["train"])
    test_df = pd.read_csv(task_config["test"])

    target = task_config["target"]
    labels = task_config["labels"]

    missing_train = set(feature_names + [target]) - set(train_df.columns)
    missing_test = set(feature_names + [target]) - set(test_df.columns)
    if missing_train or missing_test:
        raise ValueError(
            f"{task_name} is missing columns. "
            f"Train={sorted(missing_train)}, Test={sorted(missing_test)}"
        )

    X_train = train_df[feature_names].astype(float)
    y_train = train_df[target].astype(str)
    X_test = test_df[feature_names].astype(float)
    y_test = test_df[target].astype(str)

    pipeline = build_pipeline(
        model_name=model_name,
        model=model,
        strategy=strategy,
        feature_names=feature_names,
        y_train=y_train,
        random_state=args.random_state,
    )

    cv = RepeatedStratifiedKFold(
        n_splits=args.cv_splits,
        n_repeats=args.cv_repeats,
        random_state=args.random_state,
    )

    scoring = {
        "macro_f1": "f1_macro",
        "accuracy": "accuracy",
        "balanced_accuracy": "balanced_accuracy",
    }

    cv_result = cross_validate(
        pipeline,
        X_train,
        y_train,
        scoring=scoring,
        cv=cv,
        n_jobs=args.n_jobs,
        error_score="raise",
    )

    pipeline.fit(X_train, y_train)
    predictions = pipeline.predict(X_test)

    args.models_dir.mkdir(parents=True, exist_ok=True)
    model_path = (
        args.models_dir
        / f"{task_name}__{model_name}__{strategy}.joblib"
    )
    joblib.dump(pipeline, model_path)

    report = classification_report(
        y_test,
        predictions,
        labels=labels,
        output_dict=True,
        zero_division=0,
    )

    matrix = confusion_matrix(
        y_test,
        predictions,
        labels=labels,
    ).tolist()

    result = {
        "task": task_name,
        "model": model_name,
        "strategy": strategy,
        "cv_macro_f1_mean": round(
            float(np.mean(cv_result["test_macro_f1"])),
            6,
        ),
        "cv_macro_f1_std": round(
            float(np.std(cv_result["test_macro_f1"])),
            6,
        ),
        "cv_accuracy_mean": round(
            float(np.mean(cv_result["test_accuracy"])),
            6,
        ),
        "cv_accuracy_std": round(
            float(np.std(cv_result["test_accuracy"])),
            6,
        ),
        "cv_balanced_accuracy_mean": round(
            float(np.mean(cv_result["test_balanced_accuracy"])),
            6,
        ),
        "cv_balanced_accuracy_std": round(
            float(np.std(cv_result["test_balanced_accuracy"])),
            6,
        ),
        "test_macro_f1": round(
            f1_score(
                y_test,
                predictions,
                average="macro",
                zero_division=0,
            ),
            6,
        ),
        "test_weighted_f1": round(
            f1_score(
                y_test,
                predictions,
                average="weighted",
                zero_division=0,
            ),
            6,
        ),
        "test_accuracy": round(
            accuracy_score(y_test, predictions),
            6,
        ),
        "test_balanced_accuracy": round(
            balanced_accuracy_score(y_test, predictions),
            6,
        ),
        "test_macro_precision": round(
            precision_score(
                y_test,
                predictions,
                average="macro",
                zero_division=0,
            ),
            6,
        ),
        "test_macro_recall": round(
            recall_score(
                y_test,
                predictions,
                average="macro",
                zero_division=0,
            ),
            6,
        ),
        "train_size": len(train_df),
        "test_size": len(test_df),
        "train_class_counts_json": json.dumps(
            class_counts(y_train),
            ensure_ascii=False,
            sort_keys=True,
        ),
        "test_class_counts_json": json.dumps(
            class_counts(y_test),
            ensure_ascii=False,
            sort_keys=True,
        ),
        "model_path": str(model_path),
    }

    detail = {
        "labels": labels,
        "confusion_matrix": matrix,
        "classification_report": report,
    }

    return result, detail


def main() -> int:
    configure_logging()
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        feature_config = read_json(args.feature_config)
        feature_names = feature_config.get("feature_fields", [])
        if not feature_names:
            raise ValueError("Feature configuration contains no features.")

        definitions = model_definitions(args.random_state)

        results: list[dict[str, Any]] = []
        confusion_payload: dict[str, Any] = {}

        for task_name, task_config in TASKS.items():
            if not task_config["train"].exists():
                raise FileNotFoundError(task_config["train"])
            if not task_config["test"].exists():
                raise FileNotFoundError(task_config["test"])

            confusion_payload[task_name] = {}

            for model_name, model in definitions.items():
                confusion_payload[task_name][model_name] = {}

                for strategy in strategies_for_model(model_name):
                    LOGGER.info(
                        "Evaluating task=%s model=%s strategy=%s",
                        task_name,
                        model_name,
                        strategy,
                    )

                    result, detail = evaluate_configuration(
                        task_name=task_name,
                        task_config=task_config,
                        feature_names=feature_names,
                        model_name=model_name,
                        model=model,
                        strategy=strategy,
                        args=args,
                    )
                    results.append(result)
                    confusion_payload[task_name][model_name][
                        strategy
                    ] = detail

        results.sort(
            key=lambda row: (
                row["task"],
                -float(row["test_macro_f1"]),
                -float(row["cv_macro_f1_mean"]),
                row["model"],
                row["strategy"],
            )
        )

        best_by_task: dict[str, Any] = {}
        for task_name in TASKS:
            task_results = [
                row for row in results if row["task"] == task_name
            ]
            best_by_task[task_name] = max(
                task_results,
                key=lambda row: (
                    float(row["test_macro_f1"]),
                    float(row["cv_macro_f1_mean"]),
                    -float(row["cv_macro_f1_std"]),
                ),
            )

        finished_at = datetime.now(timezone.utc)

        summary = {
            "pipeline_stage": "07_train_and_compare_models",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round(
                (finished_at - started_at).total_seconds(),
                3,
            ),
            "parameters": {
                "random_state": args.random_state,
                "cv_splits": args.cv_splits,
                "cv_repeats": args.cv_repeats,
                "test_set_resampled": False,
                "feature_count": len(feature_names),
                "selection_metric": "test_macro_f1",
            },
            "evaluated_models": list(definitions),
            "evaluated_strategies": {
                "knn": strategies_for_model("knn"),
                "decision_tree": strategies_for_model(
                    "decision_tree"
                ),
                "random_forest": strategies_for_model(
                    "random_forest"
                ),
                "naive_bayes": strategies_for_model(
                    "naive_bayes"
                ),
            },
            "configuration_count": len(results),
            "best_by_task": best_by_task,
            "methodological_note": (
                "Cross-validation is performed only on the original training "
                "partition. Scaling, SMOTE and undersampling are fitted inside "
                "each fold through imbalanced-learn pipelines. The held-out "
                "test partition retains its original class distribution."
            ),
            "outputs": {
                "comparison_csv": str(args.output),
                "confusion_matrices": str(args.confusions_output),
                "candidate_models_directory": str(args.models_dir),
            },
        }

        write_csv(args.output, RESULT_FIELDS, results)
        write_json(args.confusions_output, confusion_payload)
        write_json(args.summary_output, summary)

    except (OSError, ValueError) as error:
        LOGGER.exception("Model comparison failed: %s", error)
        return 1

    LOGGER.info(
        "Model comparison completed: configurations=%d.",
        len(results),
    )
    LOGGER.info("Summary: %s", args.summary_output)
    return 0


if __name__ == "__main__":
    sys.exit(main())