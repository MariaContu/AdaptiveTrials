"""09m - Export interpretability artifacts for final recency V2 Random Forest.

Generates:
- reports/figures/final_recency_v2_random_forest_tree_example.png
- reports/figures/final_recency_v2_random_forest_feature_importance.png
- reports/metrics/09m_recency_v2_interpretability_summary.json

Notes:
- The tree figure shows ONE representative estimator from the 200-tree forest.
- The tree is truncated to depth 3 for readability.
- Feature importance uses the full Random Forest model.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import joblib
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.tree import plot_tree

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_MODEL = ROOT / "models/final/macro_model_recency_v2.joblib"
DEFAULT_TREE = (
    ROOT / "reports/figures/"
    "final_recency_v2_random_forest_tree_example.png"
)
DEFAULT_IMPORTANCE = (
    ROOT / "reports/figures/"
    "final_recency_v2_random_forest_feature_importance.png"
)
DEFAULT_SUMMARY = (
    ROOT / "reports/metrics/"
    "09m_recency_v2_interpretability_summary.json"
)

TOP_N_FEATURES = 20
TREE_INDEX = 0
TREE_MAX_DEPTH = 3


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    p.add_argument("--tree-output", type=Path, default=DEFAULT_TREE)
    p.add_argument(
        "--importance-output",
        type=Path,
        default=DEFAULT_IMPORTANCE,
    )
    p.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    p.add_argument("--top-n", type=int, default=TOP_N_FEATURES)
    p.add_argument("--tree-index", type=int, default=TREE_INDEX)
    p.add_argument("--tree-depth", type=int, default=TREE_MAX_DEPTH)
    return p.parse_args()


def load_bundle(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)

    bundle = joblib.load(path)

    if not isinstance(bundle, dict):
        raise ValueError("Unexpected final V2 model bundle format.")

    required = ("pipeline", "features", "classes")

    missing = [
        key
        for key in required
        if key not in bundle
    ]

    if missing:
        raise ValueError(
            f"Final V2 model bundle missing keys: {missing}"
        )

    return bundle


def extract_rf(model_object: Any) -> RandomForestClassifier:
    if isinstance(model_object, RandomForestClassifier):
        return model_object

    # Compatibility in case future model bundles wrap the estimator
    # in a sklearn/imblearn pipeline.
    if hasattr(model_object, "named_steps"):
        for key in ("model", "classifier", "rf"):
            estimator = model_object.named_steps.get(key)
            if isinstance(estimator, RandomForestClassifier):
                return estimator

    raise ValueError(
        "Could not extract RandomForestClassifier from final V2 bundle."
    )


def main() -> int:
    args = parse_args()

    bundle = load_bundle(args.model)

    features = [
        str(value)
        for value in bundle["features"]
    ]

    classes = [
        str(value)
        for value in bundle["classes"]
    ]

    rf = extract_rf(bundle["pipeline"])

    if len(features) != int(rf.n_features_in_):
        raise ValueError(
            "Feature-list length does not match fitted Random Forest: "
            f"{len(features)} vs {rf.n_features_in_}"
        )

    if not rf.estimators_:
        raise ValueError("Random Forest has no fitted estimators.")

    if args.tree_index < 0 or args.tree_index >= len(rf.estimators_):
        raise ValueError(
            f"tree-index must be between 0 and {len(rf.estimators_) - 1}."
        )

    # ----------------------------
    # Representative tree
    # ----------------------------
    estimator = rf.estimators_[args.tree_index]

    args.tree_output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    plt.figure(
        figsize=(22, 12)
    )

    plot_tree(
        estimator,
        feature_names=features,
        class_names=classes,
        filled=True,
        rounded=True,
        impurity=True,
        proportion=False,
        precision=3,
        max_depth=args.tree_depth,
        fontsize=8,
    )

    plt.title(
        "Random Forest V2 — árvore representativa "
        f"(estimador {args.tree_index}, profundidade exibida ≤ {args.tree_depth})"
    )

    plt.tight_layout()

    plt.savefig(
        args.tree_output,
        dpi=200,
        bbox_inches="tight",
    )

    plt.close()

    # ----------------------------
    # Feature importance
    # ----------------------------
    importances = np.asarray(
        rf.feature_importances_,
        dtype=float,
    )

    importance_df = pd.DataFrame(
        {
            "feature": features,
            "importance": importances,
        }
    ).sort_values(
        "importance",
        ascending=False,
    ).reset_index(drop=True)

    top_n = max(
        1,
        min(
            int(args.top_n),
            len(importance_df),
        ),
    )

    top = (
        importance_df
        .head(top_n)
        .sort_values(
            "importance",
            ascending=True,
        )
    )

    args.importance_output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    plt.figure(
        figsize=(11, 8)
    )

    plt.barh(
        top["feature"],
        top["importance"],
    )

    plt.xlabel(
        "Importância relativa"
    )

    plt.ylabel(
        "Feature"
    )

    plt.title(
        f"Random Forest V2 — Top {top_n} features por importância"
    )

    plt.tight_layout()

    plt.savefig(
        args.importance_output,
        dpi=200,
        bbox_inches="tight",
    )

    plt.close()

    recency_features = [
        feature
        for feature in features
        if feature.startswith("recent_")
        or feature == "has_recent_activity"
    ]

    recency_importance = float(
        importance_df.loc[
            importance_df["feature"].isin(recency_features),
            "importance",
        ].sum()
    )

    top_20_records = [
        {
            "rank": int(index + 1),
            "feature": str(row["feature"]),
            "importance": float(row["importance"]),
            "is_recency_feature": bool(
                str(row["feature"]) in recency_features
            ),
        }
        for index, (_, row)
        in enumerate(
            importance_df.head(20).iterrows()
        )
    ]

    summary = {
        "stage": "09m_recency_v2_interpretability",
        "model": str(args.model),
        "model_version": bundle.get(
            "version",
            "recency_v2",
        ),
        "feature_set": bundle.get(
            "feature_set",
        ),
        "feature_count": len(features),
        "tree_count": len(rf.estimators_),
        "tree_example": {
            "estimator_index": int(args.tree_index),
            "displayed_max_depth": int(args.tree_depth),
            "note": (
                "The tree figure represents only one estimator "
                "from the Random Forest and is truncated for readability."
            ),
        },
        "feature_importance": {
            "method": "RandomForestClassifier.feature_importances_",
            "recency_feature_count": len(recency_features),
            "recency_importance_sum": recency_importance,
            "top_20": top_20_records,
        },
        "outputs": {
            "tree_figure": str(args.tree_output),
            "feature_importance_figure": str(args.importance_output),
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
        "\n=== RECENCY V2 INTERPRETABILITY EXPORT ==="
    )
    print(
        f"Model version: {summary['model_version']}"
    )
    print(
        f"Feature set: {summary['feature_set']}"
    )
    print(
        f"Feature count: {summary['feature_count']}"
    )
    print(
        f"Trees in forest: {summary['tree_count']}"
    )
    print(
        f"Recency features: {len(recency_features)}"
    )
    print(
        "Combined recency feature importance: "
        f"{recency_importance:.6f}"
    )

    print(
        "\nTop 10 features:"
    )

    for i, (_, row) in enumerate(
        importance_df.head(10).iterrows(),
        start=1,
    ):
        marker = (
            " [RECENCY]"
            if str(row["feature"]) in recency_features
            else ""
        )

        print(
            f"  {i:02d}. "
            f"{row['feature']}: "
            f"{float(row['importance']):.6f}"
            f"{marker}"
        )

    print(
        f"\nSaved tree: {args.tree_output}"
    )
    print(
        f"Saved feature importance: {args.importance_output}"
    )
    print(
        f"Saved summary: {args.summary}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())