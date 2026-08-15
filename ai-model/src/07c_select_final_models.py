"""Select final model candidates using repeated cross-validation.

The held-out test set is reported, but repeated-CV macro F1 is the primary
selection criterion. The macro task is the deployment candidate; exploration
granular remains a complementary experiment.
"""
from __future__ import annotations

import argparse
import csv
import json
import logging
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import joblib
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RESULTS = ROOT / "reports/metrics/07_model_comparison_results.csv"
DEFAULT_CONFUSIONS = ROOT / "reports/metrics/07_confusion_matrices.json"
DEFAULT_FEATURES = ROOT / "config/model_features_v1.json"
DEFAULT_SUMMARY = ROOT / "reports/metrics/07_final_model_selection_summary.json"
DEFAULT_IMPORTANCE = ROOT / "reports/metrics/07_feature_importance.csv"
DEFAULT_FINAL_DIR = ROOT / "models/final"

LOGGER = logging.getLogger("final-model-selection")
IMPORTANCE_FIELDS = ["task", "model", "strategy", "rank", "feature", "importance"]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results", type=Path, default=DEFAULT_RESULTS)
    parser.add_argument("--confusions", type=Path, default=DEFAULT_CONFUSIONS)
    parser.add_argument("--features", type=Path, default=DEFAULT_FEATURES)
    parser.add_argument("--summary-output", type=Path, default=DEFAULT_SUMMARY)
    parser.add_argument("--importance-output", type=Path, default=DEFAULT_IMPORTANCE)
    parser.add_argument("--final-dir", type=Path, default=DEFAULT_FINAL_DIR)
    return parser.parse_args()


def read_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as file:
        return json.load(file)


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8") as file:
        json.dump(payload, file, ensure_ascii=False, indent=2)
    temp.replace(path)


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=IMPORTANCE_FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    temp.replace(path)


def select_by_cv(task_df: pd.DataFrame) -> pd.Series:
    return task_df.sort_values(
        ["cv_macro_f1_mean", "cv_macro_f1_std", "cv_balanced_accuracy_mean", "test_macro_f1"],
        ascending=[False, True, False, False],
    ).iloc[0]


def extract_estimator(pipeline: Any) -> Any:
    if hasattr(pipeline, "named_steps") and "model" in pipeline.named_steps:
        return pipeline.named_steps["model"]
    return pipeline


def serializable_metrics(row: pd.Series) -> dict[str, Any]:
    fields = [
        "model", "strategy", "cv_macro_f1_mean", "cv_macro_f1_std",
        "cv_accuracy_mean", "cv_balanced_accuracy_mean", "test_macro_f1",
        "test_weighted_f1", "test_accuracy", "test_balanced_accuracy",
        "test_macro_precision", "test_macro_recall", "train_size", "test_size",
    ]
    output: dict[str, Any] = {}
    for field in fields:
        value = row[field]
        output[field] = value.item() if hasattr(value, "item") else value
    return output


def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(message)s")
    args = parse_args()
    started_at = datetime.now(timezone.utc)

    try:
        results = pd.read_csv(args.results)
        confusions = read_json(args.confusions)
        feature_names = read_json(args.features)["feature_fields"]

        selections: dict[str, Any] = {}
        importance_rows: list[dict[str, Any]] = []

        for task in sorted(results["task"].unique()):
            selected = select_by_cv(results[results["task"] == task].copy())
            model_path = Path(str(selected["model_path"]))
            pipeline = joblib.load(model_path)
            estimator = extract_estimator(pipeline)

            args.final_dir.mkdir(parents=True, exist_ok=True)
            final_path = args.final_dir / f"{task}_model.joblib"
            shutil.copy2(model_path, final_path)

            task_importance: list[dict[str, Any]] = []
            if hasattr(estimator, "feature_importances_"):
                pairs = sorted(
                    zip(feature_names, estimator.feature_importances_),
                    key=lambda item: (-float(item[1]), item[0]),
                )
                task_importance = [
                    {
                        "task": task,
                        "model": str(selected["model"]),
                        "strategy": str(selected["strategy"]),
                        "rank": rank,
                        "feature": feature,
                        "importance": round(float(importance), 8),
                    }
                    for rank, (feature, importance) in enumerate(pairs, start=1)
                ]
                importance_rows.extend(task_importance)

            detail = confusions[task][str(selected["model"])][str(selected["strategy"])]
            selections[task] = {
                "selection_policy": "highest repeated-CV macro F1",
                "selected_configuration": serializable_metrics(selected),
                "cv_test_macro_f1_difference": round(
                    float(selected["test_macro_f1"]) - float(selected["cv_macro_f1_mean"]), 6
                ),
                "labels": detail["labels"],
                "confusion_matrix": detail["confusion_matrix"],
                "classification_report": detail["classification_report"],
                "final_model_path": str(final_path),
                "top_10_features": task_importance[:10],
            }

        finished_at = datetime.now(timezone.utc)
        summary = {
            "pipeline_stage": "07_select_final_models",
            "started_at": started_at.isoformat(),
            "finished_at": finished_at.isoformat(),
            "duration_seconds": round((finished_at - started_at).total_seconds(), 3),
            "primary_modeling_task": "macro",
            "selections": selections,
            "final_decision": {
                "production_candidate": selections["macro"]["final_model_path"],
                "production_task": "macro",
                "complementary_experiment": selections["exploration_granular"]["final_model_path"],
                "exploration_status": "complementary_only_due_to_small_sample_and_validation_test_variability",
            },
            "methodological_note": (
                "Repeated stratified cross-validation macro F1 is the primary selector. "
                "The held-out test set is used only for final reporting."
            ),
        }
        write_csv(args.importance_output, importance_rows)
        write_json(args.summary_output, summary)

    except Exception as error:
        LOGGER.exception("Final model selection failed: %s", error)
        return 1

    LOGGER.info("Final selections created for %d tasks.", len(selections))
    return 0


if __name__ == "__main__":
    sys.exit(main())