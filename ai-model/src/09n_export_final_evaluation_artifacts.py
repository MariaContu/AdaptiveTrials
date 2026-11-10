"""09n - Export final evaluation artifacts without retraining models.

Uses existing experiment outputs:
- 09h_v1_v2_test_predictions.csv (corrected held-out predictions)
- 09e_v1_v2_recency_model_comparison.csv (controlled algorithm comparison)

Generates:
- reports/figures/09n_v1_confusion_matrix.png
- reports/figures/09n_v2_confusion_matrix.png
- reports/figures/09n_algorithm_comparison_macro_f1.png
- reports/metrics/09n_v1_confusion_matrix.csv
- reports/metrics/09n_v2_confusion_matrix.csv
- reports/metrics/09n_final_algorithm_comparison.csv
- reports/metrics/09n_final_algorithm_comparison.md
- reports/metrics/09n_final_evaluation_summary.json

No model is trained by this script.
"""

from __future__ import annotations

import json
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from sklearn.metrics import confusion_matrix

ROOT = Path(__file__).resolve().parents[1]

PREDICTIONS = (
    ROOT / "reports/metrics/09h_v1_v2_test_predictions.csv"
)
MODEL_COMPARISON = (
    ROOT / "reports/metrics/09e_v1_v2_recency_model_comparison.csv"
)

FIGURES = ROOT / "reports/figures"
METRICS = ROOT / "reports/metrics"

LABELS = [
    "combat",
    "exploration",
    "strategic_reasoning",
]

DISPLAY_LABELS = [
    "Combat",
    "Exploration",
    "Strategic reasoning",
]


def save_confusion_matrix(
    y_true: pd.Series,
    y_pred: pd.Series,
    title: str,
    image_path: Path,
    csv_path: Path,
) -> list[list[int]]:
    matrix = confusion_matrix(
        y_true,
        y_pred,
        labels=LABELS,
    )

    matrix_df = pd.DataFrame(
        matrix,
        index=[f"actual_{x}" for x in LABELS],
        columns=[f"pred_{x}" for x in LABELS],
    )
    matrix_df.to_csv(csv_path)

    fig, ax = plt.subplots(figsize=(8, 6))
    image = ax.imshow(matrix)

    ax.set_xticks(np.arange(len(DISPLAY_LABELS)))
    ax.set_yticks(np.arange(len(DISPLAY_LABELS)))
    ax.set_xticklabels(DISPLAY_LABELS)
    ax.set_yticklabels(DISPLAY_LABELS)

    ax.set_xlabel("Predicted class")
    ax.set_ylabel("Actual class")
    ax.set_title(title)

    for i in range(matrix.shape[0]):
        for j in range(matrix.shape[1]):
            ax.text(
                j,
                i,
                str(matrix[i, j]),
                ha="center",
                va="center",
            )

    fig.colorbar(image, ax=ax)
    fig.tight_layout()
    fig.savefig(
        image_path,
        dpi=200,
        bbox_inches="tight",
    )
    plt.close(fig)

    return matrix.tolist()


def build_algorithm_table(
    comparison: pd.DataFrame,
) -> pd.DataFrame:
    # 09e is the controlled algorithm-comparison stage.
    # It uses the same V2 recency feature space for all algorithms.
    v2 = comparison.loc[
        comparison["version"].eq("v2_recency")
    ].copy()

    if v2.empty:
        raise ValueError(
            "No rows with version='v2_recency' found in 09e comparison."
        )

    required_algorithms = {
        "DecisionTree",
        "RandomForest",
        "KNN",
        "GaussianNB",
    }

    present = set(v2["algorithm"].astype(str))
    missing = required_algorithms - present
    if missing:
        raise ValueError(
            f"Missing algorithms in 09e comparison: {sorted(missing)}"
        )

    # Choose the best strategy for each classifier using CV macro-F1 only.
    best = (
        v2.sort_values(
            [
                "algorithm",
                "cv_macro_f1_mean",
                "cv_balanced_accuracy_mean",
            ],
            ascending=[True, False, False],
        )
        .groupby(
            "algorithm",
            as_index=False,
            sort=False,
        )
        .head(1)
        .copy()
    )

    order = {
        "RandomForest": 0,
        "KNN": 1,
        "GaussianNB": 2,
        "DecisionTree": 3,
    }

    best["_order"] = (
        best["algorithm"]
        .map(order)
        .fillna(99)
    )

    best = best.sort_values("_order").drop(columns="_order")

    columns = [
        "algorithm",
        "strategy",
        "feature_count",
        "cv_macro_f1_mean",
        "cv_macro_f1_std",
        "cv_balanced_accuracy_mean",
        "test_macro_f1",
        "test_accuracy",
        "test_balanced_accuracy",
        "test_combat_f1",
        "test_exploration_f1",
        "test_strategic_f1",
    ]

    return best[columns].reset_index(drop=True)


def save_algorithm_plot(
    table: pd.DataFrame,
    path: Path,
) -> None:
    plot_df = table.sort_values(
        "cv_macro_f1_mean",
        ascending=True,
    )

    fig, ax = plt.subplots(figsize=(9, 5))
    ax.barh(
        plot_df["algorithm"],
        plot_df["cv_macro_f1_mean"],
    )

    ax.set_xlabel("Repeated CV macro F1")
    ax.set_ylabel("Classifier")
    ax.set_title(
        "Controlled classifier comparison — V2 recency feature space"
    )

    for index, value in enumerate(
        plot_df["cv_macro_f1_mean"]
    ):
        ax.text(
            float(value) + 0.003,
            index,
            f"{float(value):.4f}",
            va="center",
        )

    ax.set_xlim(
        0,
        max(0.75, float(plot_df["cv_macro_f1_mean"].max()) + 0.08),
    )

    fig.tight_layout()
    fig.savefig(
        path,
        dpi=200,
        bbox_inches="tight",
    )
    plt.close(fig)


def markdown_table(df: pd.DataFrame) -> str:
    headers = [
        "Algorithm",
        "Strategy",
        "Features",
        "CV Macro F1",
        "CV Balanced Acc.",
        "Test Macro F1",
        "Test Accuracy",
        "Test Balanced Acc.",
        "Test Strategic F1",
    ]

    rows = []
    for _, row in df.iterrows():
        rows.append([
            str(row["algorithm"]),
            str(row["strategy"]),
            str(int(row["feature_count"])),
            f'{row["cv_macro_f1_mean"]:.4f}',
            f'{row["cv_balanced_accuracy_mean"]:.4f}',
            f'{row["test_macro_f1"]:.4f}',
            f'{row["test_accuracy"]:.4f}',
            f'{row["test_balanced_accuracy"]:.4f}',
            f'{row["test_strategic_f1"]:.4f}',
        ])

    lines = [
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join(["---"] * len(headers)) + " |",
    ]

    lines.extend(
        "| " + " | ".join(row) + " |"
        for row in rows
    )

    return "\n".join(lines) + "\n"


def main() -> int:
    FIGURES.mkdir(parents=True, exist_ok=True)
    METRICS.mkdir(parents=True, exist_ok=True)

    if not PREDICTIONS.exists():
        raise FileNotFoundError(PREDICTIONS)

    if not MODEL_COMPARISON.exists():
        raise FileNotFoundError(MODEL_COMPARISON)

    predictions = pd.read_csv(PREDICTIONS)
    comparison = pd.read_csv(MODEL_COMPARISON)

    required_prediction_columns = {
        "actual_target",
        "v1_prediction",
        "v2_prediction",
    }

    missing = required_prediction_columns - set(predictions.columns)
    if missing:
        raise ValueError(
            f"Missing columns in 09h predictions: {sorted(missing)}"
        )

    v1_matrix = save_confusion_matrix(
        predictions["actual_target"],
        predictions["v1_prediction"],
        "V1 — held-out confusion matrix",
        FIGURES / "09n_v1_confusion_matrix.png",
        METRICS / "09n_v1_confusion_matrix.csv",
    )

    v2_matrix = save_confusion_matrix(
        predictions["actual_target"],
        predictions["v2_prediction"],
        "V2 Recency — held-out confusion matrix",
        FIGURES / "09n_v2_confusion_matrix.png",
        METRICS / "09n_v2_confusion_matrix.csv",
    )

    algorithm_table = build_algorithm_table(
        comparison
    )

    algorithm_table.to_csv(
        METRICS / "09n_final_algorithm_comparison.csv",
        index=False,
    )

    (
        METRICS / "09n_final_algorithm_comparison.md"
    ).write_text(
        markdown_table(algorithm_table),
        encoding="utf-8",
    )

    save_algorithm_plot(
        algorithm_table,
        FIGURES / "09n_algorithm_comparison_macro_f1.png",
    )

    winner = algorithm_table.sort_values(
        "cv_macro_f1_mean",
        ascending=False,
    ).iloc[0]

    summary = {
        "stage": "09n_final_evaluation_artifacts",
        "note": (
            "This script exports existing evaluation results only; "
            "it does not retrain or reselect the final model."
        ),
        "heldout_profiles": int(len(predictions)),
        "labels": LABELS,
        "v1_confusion_matrix": v1_matrix,
        "v2_confusion_matrix": v2_matrix,
        "controlled_algorithm_comparison": {
            "source": str(MODEL_COMPARISON),
            "version": "v2_recency",
            "selection_rule": (
                "Best strategy per algorithm by repeated-CV macro F1. "
                "This is the common-feature-space algorithm comparison "
                "that precedes Random Forest-specific optimization."
            ),
            "winner": {
                "algorithm": str(winner["algorithm"]),
                "strategy": str(winner["strategy"]),
                "feature_count": int(winner["feature_count"]),
                "cv_macro_f1_mean": float(
                    winner["cv_macro_f1_mean"]
                ),
                "cv_balanced_accuracy_mean": float(
                    winner["cv_balanced_accuracy_mean"]
                ),
            },
        },
        "outputs": {
            "v1_confusion_matrix_figure": str(
                FIGURES / "09n_v1_confusion_matrix.png"
            ),
            "v2_confusion_matrix_figure": str(
                FIGURES / "09n_v2_confusion_matrix.png"
            ),
            "algorithm_comparison_figure": str(
                FIGURES / "09n_algorithm_comparison_macro_f1.png"
            ),
            "algorithm_comparison_csv": str(
                METRICS / "09n_final_algorithm_comparison.csv"
            ),
            "algorithm_comparison_markdown": str(
                METRICS / "09n_final_algorithm_comparison.md"
            ),
        },
    }

    summary_path = (
        METRICS / "09n_final_evaluation_summary.json"
    )

    summary_path.write_text(
        json.dumps(
            summary,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print("\n=== FINAL EVALUATION ARTIFACTS ===")

    print("\nV1 held-out confusion matrix:")
    print(
        pd.DataFrame(
            v1_matrix,
            index=DISPLAY_LABELS,
            columns=DISPLAY_LABELS,
        ).to_string()
    )

    print("\nV2 held-out confusion matrix:")
    print(
        pd.DataFrame(
            v2_matrix,
            index=DISPLAY_LABELS,
            columns=DISPLAY_LABELS,
        ).to_string()
    )

    print("\nBest configuration per classifier (selected by CV macro F1):")
    print(
        algorithm_table[
            [
                "algorithm",
                "strategy",
                "feature_count",
                "cv_macro_f1_mean",
                "cv_balanced_accuracy_mean",
                "test_macro_f1",
                "test_accuracy",
                "test_balanced_accuracy",
                "test_strategic_f1",
            ]
        ].to_string(index=False)
    )

    print("\nControlled comparison winner:")
    print(
        f"  {winner['algorithm']} + {winner['strategy']}"
    )
    print(
        f"  CV macro F1: {winner['cv_macro_f1_mean']:.6f}"
    )

    print(f"\nSaved summary: {summary_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
