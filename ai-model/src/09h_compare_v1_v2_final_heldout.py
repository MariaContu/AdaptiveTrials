"""09h - Final held-out comparison: V1 final configuration vs V2 recency candidate.

Purpose
-------
Perform the final controlled comparison on the untouched held-out test set.

Important:
- V1 deployment artifact is NOT evaluated directly because it was later refit
  on all 686 resolved profiles for deployment.
- Instead, the frozen V1 configuration is reconstructed and fitted only on
  the original 548-player training split.
- V2 candidate is also reconstructed/fitted only on the same 548-player train.
- Both are evaluated on the same untouched 138-player test split.

Outputs
-------
- reports/metrics/09h_v1_v2_final_heldout_comparison.json
- reports/metrics/09h_v1_v2_test_predictions.csv
"""

from __future__ import annotations

import argparse
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

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_TRAIN = ROOT / "data/modeling/macro_train_recency_v2.csv"
DEFAULT_TEST = ROOT / "data/modeling/macro_test_recency_v2.csv"
DEFAULT_V1_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_V2_MODEL = (
    ROOT / "models/experimental/macro_model_recency_v2_candidate.joblib"
)

DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/09h_v1_v2_final_heldout_comparison.json"
)
DEFAULT_PREDICTIONS = (
    ROOT / "reports/metrics/09h_v1_v2_test_predictions.csv"
)

RANDOM_STATE = 42

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

ID_CANDIDATES = (
    "final_player_id",
    "player_id",
)


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--train", type=Path, default=DEFAULT_TRAIN)
    p.add_argument("--test", type=Path, default=DEFAULT_TEST)
    p.add_argument("--v1-model", type=Path, default=DEFAULT_V1_MODEL)
    p.add_argument("--v2-model", type=Path, default=DEFAULT_V2_MODEL)
    p.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    p.add_argument(
        "--predictions",
        type=Path,
        default=DEFAULT_PREDICTIONS,
    )
    return p.parse_args()


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


def load_bundle(path: Path, label: str) -> dict[str, Any]:
    bundle = joblib.load(path)

    if not isinstance(bundle, dict):
        raise ValueError(f"Unexpected {label} bundle format.")

    features = bundle.get("features")
    if not isinstance(features, list) or not features:
        raise ValueError(f"{label} bundle has no feature list.")

    return bundle


def numeric_matrix(
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
            f"Dataset missing features: {missing}"
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
            f"Invalid/missing numeric values in: {bad}"
        )

    if not np.isfinite(X.to_numpy()).all():
        raise ValueError("Infinite values found.")

    return X


def build_v1() -> ImbPipeline:
    return ImbPipeline(
        [
            (
                "smote",
                SMOTE(
                    k_neighbors=3,
                    random_state=RANDOM_STATE,
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
                    random_state=RANDOM_STATE,
                    n_jobs=-1,
                ),
            ),
        ]
    )


def build_v2(
    bundle: dict[str, Any],
) -> ImbPipeline:
    strategy = bundle.get("strategy")
    params = bundle.get("rf_params")

    if strategy != "class_weight":
        raise ValueError(
            "Current V2 candidate is expected to use class_weight."
        )

    if not isinstance(params, dict):
        raise ValueError(
            "V2 candidate has no rf_params metadata."
        )

    model = RandomForestClassifier(
        n_estimators=int(params["n_estimators"]),
        max_depth=(
            None
            if params.get("max_depth") is None
            else int(params["max_depth"])
        ),
        min_samples_split=int(
            params["min_samples_split"]
        ),
        min_samples_leaf=int(
            params["min_samples_leaf"]
        ),
        max_features=str(
            params["max_features"]
        ),
        class_weight="balanced",
        random_state=RANDOM_STATE,
        n_jobs=-1,
    )

    return ImbPipeline(
        [("model", model)]
    )


def evaluate(
    y_true: pd.Series,
    prediction: np.ndarray,
) -> dict[str, Any]:
    report = classification_report(
        y_true,
        prediction,
        labels=CLASS_ORDER,
        output_dict=True,
        zero_division=0,
    )

    matrix = confusion_matrix(
        y_true,
        prediction,
        labels=CLASS_ORDER,
    ).tolist()

    return {
        "accuracy": float(
            accuracy_score(
                y_true,
                prediction,
            )
        ),
        "balanced_accuracy": float(
            balanced_accuracy_score(
                y_true,
                prediction,
            )
        ),
        "macro_f1": float(
            f1_score(
                y_true,
                prediction,
                average="macro",
                zero_division=0,
            )
        ),
        "combat_f1": float(
            report["combat"]["f1-score"]
        ),
        "exploration_f1": float(
            report["exploration"]["f1-score"]
        ),
        "strategic_f1": float(
            report["strategic_reasoning"]["f1-score"]
        ),
        "combat_recall": float(
            report["combat"]["recall"]
        ),
        "exploration_recall": float(
            report["exploration"]["recall"]
        ),
        "strategic_recall": float(
            report["strategic_reasoning"]["recall"]
        ),
        "classification_report": report,
        "confusion_matrix_labels": CLASS_ORDER,
        "confusion_matrix": matrix,
    }


def main() -> int:
    args = parse_args()

    train = pd.read_csv(args.train)
    test = pd.read_csv(args.test)

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

    if target_train != target_test:
        raise ValueError(
            "Train/test target columns differ."
        )

    player_id = detect_column(
        test,
        ID_CANDIDATES,
        "test player ID",
    )

    y_train = train[target_train].astype(str)
    y_test = test[target_test].astype(str)

    v1_bundle = load_bundle(
        args.v1_model,
        "V1",
    )
    v2_bundle = load_bundle(
        args.v2_model,
        "V2",
    )

    v1_features = [
        str(x)
        for x in v1_bundle["features"]
    ]
    v2_features = [
        str(x)
        for x in v2_bundle["features"]
    ]

    X1_train = numeric_matrix(
        train,
        v1_features,
    )
    X1_test = numeric_matrix(
        test,
        v1_features,
    )

    X2_train = numeric_matrix(
        train,
        v2_features,
    )
    X2_test = numeric_matrix(
        test,
        v2_features,
    )

    model_v1 = build_v1()
    model_v2 = build_v2(v2_bundle)

    model_v1.fit(
        X1_train,
        y_train,
    )
    model_v2.fit(
        X2_train,
        y_train,
    )

    pred_v1 = model_v1.predict(
        X1_test
    )
    pred_v2 = model_v2.predict(
        X2_test
    )

    metrics_v1 = evaluate(
        y_test,
        pred_v1,
    )
    metrics_v2 = evaluate(
        y_test,
        pred_v2,
    )

    correct_v1 = (
        pred_v1 == y_test.to_numpy()
    )
    correct_v2 = (
        pred_v2 == y_test.to_numpy()
    )

    both_correct = int(
        np.sum(correct_v1 & correct_v2)
    )
    both_wrong = int(
        np.sum(~correct_v1 & ~correct_v2)
    )
    v1_only_correct = int(
        np.sum(correct_v1 & ~correct_v2)
    )
    v2_only_correct = int(
        np.sum(~correct_v1 & correct_v2)
    )

    disagreement_count = int(
        np.sum(pred_v1 != pred_v2)
    )

    prediction_df = pd.DataFrame(
        {
            "final_player_id": (
                test[player_id].astype(str)
            ),
            "actual_target": y_test,
            "v1_prediction": pred_v1,
            "v2_prediction": pred_v2,
            "v1_correct": correct_v1.astype(int),
            "v2_correct": correct_v2.astype(int),
            "predictions_differ": (
                pred_v1 != pred_v2
            ).astype(int),
        }
    )

    args.predictions.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    prediction_df.to_csv(
        args.predictions,
        index=False,
    )

    delta = {
        "accuracy": (
            metrics_v2["accuracy"]
            - metrics_v1["accuracy"]
        ),
        "balanced_accuracy": (
            metrics_v2["balanced_accuracy"]
            - metrics_v1["balanced_accuracy"]
        ),
        "macro_f1": (
            metrics_v2["macro_f1"]
            - metrics_v1["macro_f1"]
        ),
        "combat_f1": (
            metrics_v2["combat_f1"]
            - metrics_v1["combat_f1"]
        ),
        "exploration_f1": (
            metrics_v2["exploration_f1"]
            - metrics_v1["exploration_f1"]
        ),
        "strategic_f1": (
            metrics_v2["strategic_f1"]
            - metrics_v1["strategic_f1"]
        ),
        "strategic_recall": (
            metrics_v2["strategic_recall"]
            - metrics_v1["strategic_recall"]
        ),
    }

    summary = {
        "stage": "09h_final_heldout_comparison",
        "design": {
            "train_rows": int(len(train)),
            "test_rows": int(len(test)),
            "same_train_split": True,
            "same_test_split": True,
            "same_targets": True,
            "v1_deployment_artifact_directly_tested": False,
            "reason": (
                "The deployment V1 artifact was refit on all resolved "
                "profiles after model selection. The held-out comparison "
                "therefore reconstructs the frozen V1 configuration and "
                "fits it only on the original training split."
            ),
        },
        "v1": {
            "feature_count": len(v1_features),
            "strategy": "SMOTE",
            "metrics": metrics_v1,
        },
        "v2": {
            "feature_count": len(v2_features),
            "feature_set": v2_bundle.get(
                "feature_set"
            ),
            "strategy": v2_bundle.get(
                "strategy"
            ),
            "rf_params": v2_bundle.get(
                "rf_params"
            ),
            "metrics": metrics_v2,
        },
        "delta_v2_minus_v1": delta,
        "paired_prediction_comparison": {
            "both_correct": both_correct,
            "both_wrong": both_wrong,
            "v1_only_correct": v1_only_correct,
            "v2_only_correct": v2_only_correct,
            "prediction_disagreement_count": disagreement_count,
            "prediction_disagreement_rate": float(
                disagreement_count / len(test)
            ),
        },
        "outputs": {
            "predictions_csv": str(
                args.predictions
            ),
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
        "\n=== FINAL HELD-OUT COMPARISON ==="
    )

    print("\nV1 FINAL CONFIGURATION")
    print(
        f"Macro F1: {metrics_v1['macro_f1']:.6f}"
    )
    print(
        "Balanced accuracy: "
        f"{metrics_v1['balanced_accuracy']:.6f}"
    )
    print(
        f"Accuracy: {metrics_v1['accuracy']:.6f}"
    )
    print(
        "Combat F1: "
        f"{metrics_v1['combat_f1']:.6f}"
    )
    print(
        "Exploration F1: "
        f"{metrics_v1['exploration_f1']:.6f}"
    )
    print(
        "Strategic F1: "
        f"{metrics_v1['strategic_f1']:.6f}"
    )
    print(
        "Strategic recall: "
        f"{metrics_v1['strategic_recall']:.6f}"
    )

    print("\nV2 RECENCY CANDIDATE")
    print(
        f"Macro F1: {metrics_v2['macro_f1']:.6f}"
    )
    print(
        "Balanced accuracy: "
        f"{metrics_v2['balanced_accuracy']:.6f}"
    )
    print(
        f"Accuracy: {metrics_v2['accuracy']:.6f}"
    )
    print(
        "Combat F1: "
        f"{metrics_v2['combat_f1']:.6f}"
    )
    print(
        "Exploration F1: "
        f"{metrics_v2['exploration_f1']:.6f}"
    )
    print(
        "Strategic F1: "
        f"{metrics_v2['strategic_f1']:.6f}"
    )
    print(
        "Strategic recall: "
        f"{metrics_v2['strategic_recall']:.6f}"
    )

    print("\n=== V2 - V1 ===")
    print(
        f"Macro F1 delta: {delta['macro_f1']:+.6f}"
    )
    print(
        "Balanced accuracy delta: "
        f"{delta['balanced_accuracy']:+.6f}"
    )
    print(
        f"Accuracy delta: {delta['accuracy']:+.6f}"
    )
    print(
        "Strategic F1 delta: "
        f"{delta['strategic_f1']:+.6f}"
    )
    print(
        "Strategic recall delta: "
        f"{delta['strategic_recall']:+.6f}"
    )

    print("\n=== PAIRED PREDICTIONS ===")
    print(
        f"Both correct: {both_correct}"
    )
    print(
        f"Both wrong: {both_wrong}"
    )
    print(
        f"V1 only correct: {v1_only_correct}"
    )
    print(
        f"V2 only correct: {v2_only_correct}"
    )
    print(
        "Prediction disagreements: "
        f"{disagreement_count}/{len(test)} "
        f"({disagreement_count / len(test):.4f})"
    )

    print(
        f"\nSaved summary: {args.summary}"
    )
    print(
        f"Saved predictions: {args.predictions}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())