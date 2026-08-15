"""Export interpretability figures for the final Adaptive Trials Random Forest.

Outputs:
- reports/figures/final_random_forest_tree_example.png
- reports/figures/final_random_forest_feature_importance.png

The tree image represents ONE estimator from the Random Forest and should not
be interpreted as the entire model.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import joblib
import matplotlib.pyplot as plt
import pandas as pd
from sklearn.tree import plot_tree

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_TREE = ROOT / "reports/figures/final_random_forest_tree_example.png"
DEFAULT_IMPORTANCE = ROOT / "reports/figures/final_random_forest_feature_importance.png"


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL)
    parser.add_argument("--tree-output", type=Path, default=DEFAULT_TREE)
    parser.add_argument("--importance-output", type=Path, default=DEFAULT_IMPORTANCE)
    parser.add_argument("--tree-index", type=int, default=0)
    parser.add_argument("--max-depth", type=int, default=3)
    parser.add_argument("--top-features", type=int, default=15)
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    bundle = joblib.load(args.model)
    pipeline = bundle["pipeline"]
    features = bundle["features"]

    forest = pipeline.named_steps["model"]

    if args.tree_index < 0 or args.tree_index >= len(forest.estimators_):
        raise ValueError(
            f"--tree-index must be between 0 and {len(forest.estimators_) - 1}"
        )

    args.tree_output.parent.mkdir(parents=True, exist_ok=True)
    args.importance_output.parent.mkdir(parents=True, exist_ok=True)

    # One representative tree, truncated for readability.
    estimator = forest.estimators_[args.tree_index]

    plt.figure(figsize=(24, 12))
    plot_tree(
        estimator,
        feature_names=features,
        class_names=[str(c) for c in forest.classes_],
        filled=False,
        rounded=True,
        proportion=True,
        precision=3,
        max_depth=args.max_depth,
        fontsize=7,
    )
    plt.title(
        f"Random Forest — árvore {args.tree_index} "
        f"(visualização limitada a profundidade {args.max_depth})"
    )
    plt.tight_layout()
    plt.savefig(args.tree_output, dpi=200, bbox_inches="tight")
    plt.close()

    # Global feature importance across the entire forest.
    importance_df = pd.DataFrame(
        {
            "feature": features,
            "importance": forest.feature_importances_,
        }
    ).sort_values("importance", ascending=False)

    top = importance_df.head(args.top_features).sort_values(
        "importance",
        ascending=True,
    )

    plt.figure(figsize=(10, 8))
    plt.barh(top["feature"], top["importance"])
    plt.xlabel("Importância")
    plt.ylabel("Feature")
    plt.title(
        f"Random Forest — {args.top_features} features mais importantes"
    )
    plt.tight_layout()
    plt.savefig(args.importance_output, dpi=200, bbox_inches="tight")
    plt.close()

    print(f"Tree: {args.tree_output}")
    print(f"Feature importance: {args.importance_output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())