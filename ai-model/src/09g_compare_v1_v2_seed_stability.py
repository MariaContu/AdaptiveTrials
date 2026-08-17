"""09g - Seed stability comparison: frozen V1 vs recency V2 candidate."""

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
from sklearn.metrics import make_scorer, f1_score, recall_score
from sklearn.model_selection import RepeatedStratifiedKFold, cross_validate

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_TRAIN = ROOT / "data/modeling/macro_train_recency_v2.csv"
DEFAULT_V1_MODEL = ROOT / "models/final/macro_model_optimized.joblib"
DEFAULT_V2_MODEL = ROOT / "models/experimental/macro_model_recency_v2_candidate.joblib"
DEFAULT_RESULTS = ROOT / "reports/metrics/09g_v1_v2_seed_stability.csv"
DEFAULT_SUMMARY = ROOT / "reports/metrics/09g_v1_v2_seed_stability_summary.json"

SEEDS = [11, 21, 42, 73, 101]
CV_SPLITS = 5
CV_REPEATS = 3

TARGET_CANDIDATES = (
    "target_macro",
    "macro_target",
    "target",
    "resolved_target",
    "target_category",
)


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--train", type=Path, default=DEFAULT_TRAIN)
    p.add_argument("--v1-model", type=Path, default=DEFAULT_V1_MODEL)
    p.add_argument("--v2-model", type=Path, default=DEFAULT_V2_MODEL)
    p.add_argument("--results", type=Path, default=DEFAULT_RESULTS)
    p.add_argument("--summary", type=Path, default=DEFAULT_SUMMARY)
    return p.parse_args()


def detect_target(df: pd.DataFrame) -> str:
    for col in TARGET_CANDIDATES:
        if col in df.columns:
            return col
    raise ValueError("Target column not found.")


def load_bundle(path: Path, label: str) -> dict[str, Any]:
    bundle = joblib.load(path)
    if not isinstance(bundle, dict):
        raise ValueError(f"Unexpected {label} bundle format.")
    features = bundle.get("features")
    if not isinstance(features, list) or not features:
        raise ValueError(f"{label} has no feature list.")
    return bundle


def numeric_matrix(df: pd.DataFrame, features: list[str]) -> pd.DataFrame:
    missing = [f for f in features if f not in df.columns]
    if missing:
        raise ValueError(f"Missing features: {missing}")

    X = df[features].apply(pd.to_numeric, errors="coerce")
    if X.isna().any().any():
        raise ValueError("NaN/non-numeric values found.")
    if not np.isfinite(X.to_numpy()).all():
        raise ValueError("Infinite values found.")
    return X


def build_v1(seed: int) -> ImbPipeline:
    return ImbPipeline(
        [
            (
                "smote",
                SMOTE(k_neighbors=3, random_state=seed),
            ),
            (
                "model",
                RandomForestClassifier(
                    n_estimators=200,
                    max_depth=10,
                    min_samples_split=2,
                    min_samples_leaf=2,
                    max_features="sqrt",
                    random_state=seed,
                    n_jobs=-1,
                ),
            ),
        ]
    )


def build_v2(bundle: dict[str, Any], seed: int) -> ImbPipeline:
    params = bundle.get("rf_params")
    if bundle.get("strategy") != "class_weight":
        raise ValueError("Current V2 candidate is expected to use class_weight.")
    if not isinstance(params, dict):
        raise ValueError("V2 candidate has no rf_params.")

    model = RandomForestClassifier(
        n_estimators=int(params["n_estimators"]),
        max_depth=None if params.get("max_depth") is None else int(params["max_depth"]),
        min_samples_split=int(params["min_samples_split"]),
        min_samples_leaf=int(params["min_samples_leaf"]),
        max_features=str(params["max_features"]),
        class_weight="balanced",
        random_state=seed,
        n_jobs=-1,
    )
    return ImbPipeline([("model", model)])


def scorer_f1(label: str):
    return make_scorer(
        f1_score,
        labels=[label],
        average="macro",
        zero_division=0,
    )


def scorer_recall(label: str):
    return make_scorer(
        recall_score,
        labels=[label],
        average="macro",
        zero_division=0,
    )


def evaluate(
    version: str,
    seed: int,
    pipeline: ImbPipeline,
    X: pd.DataFrame,
    y: pd.Series,
) -> dict[str, Any]:
    cv = RepeatedStratifiedKFold(
        n_splits=CV_SPLITS,
        n_repeats=CV_REPEATS,
        random_state=seed,
    )

    scoring = {
        "macro_f1": "f1_macro",
        "balanced_accuracy": "balanced_accuracy",
        "strategic_f1": scorer_f1("strategic_reasoning"),
        "strategic_recall": scorer_recall("strategic_reasoning"),
    }

    r = cross_validate(
        pipeline,
        X,
        y,
        cv=cv,
        scoring=scoring,
        n_jobs=-1,
        return_train_score=False,
        error_score="raise",
    )

    return {
        "version": version,
        "seed": seed,
        "cv_macro_f1_mean": float(np.mean(r["test_macro_f1"])),
        "cv_macro_f1_std": float(np.std(r["test_macro_f1"])),
        "cv_balanced_accuracy_mean": float(np.mean(r["test_balanced_accuracy"])),
        "cv_strategic_f1_mean": float(np.mean(r["test_strategic_f1"])),
        "cv_strategic_recall_mean": float(np.mean(r["test_strategic_recall"])),
    }


def version_summary(df: pd.DataFrame) -> dict[str, Any]:
    metrics = [
        "cv_macro_f1_mean",
        "cv_balanced_accuracy_mean",
        "cv_strategic_f1_mean",
        "cv_strategic_recall_mean",
    ]
    out = {}
    for metric in metrics:
        s = df[metric].astype(float)
        out[metric] = {
            "mean_across_seeds": float(s.mean()),
            "std_across_seeds": float(s.std(ddof=0)),
            "min_across_seeds": float(s.min()),
            "max_across_seeds": float(s.max()),
        }
    return out


def main() -> int:
    args = parse_args()

    train = pd.read_csv(args.train)
    target = detect_target(train)
    y = train[target].astype(str)

    v1_bundle = load_bundle(args.v1_model, "V1")
    v2_bundle = load_bundle(args.v2_model, "V2")

    v1_features = [str(x) for x in v1_bundle["features"]]
    v2_features = [str(x) for x in v2_bundle["features"]]

    X1 = numeric_matrix(train, v1_features)
    X2 = numeric_matrix(train, v2_features)

    print("\n=== V1 vs V2 SEED STABILITY ===")
    print(f"Train rows: {len(train)}")
    print(f"V1 features: {len(v1_features)}")
    print(f"V2 features: {len(v2_features)}")
    print(f"Seeds: {SEEDS}")
    print(f"CV per seed: {CV_SPLITS} folds x {CV_REPEATS} repeats")
    print("Held-out test used: False")

    rows = []

    for seed in SEEDS:
        print(f"\n--- seed {seed} ---")

        r1 = evaluate("v1_final", seed, build_v1(seed), X1, y)
        rows.append(r1)
        print(
            f"V1 | macro F1={r1['cv_macro_f1_mean']:.6f} | "
            f"balanced={r1['cv_balanced_accuracy_mean']:.6f} | "
            f"strategic F1={r1['cv_strategic_f1_mean']:.6f}"
        )

        r2 = evaluate(
            "v2_recency_candidate",
            seed,
            build_v2(v2_bundle, seed),
            X2,
            y,
        )
        rows.append(r2)
        print(
            f"V2 | macro F1={r2['cv_macro_f1_mean']:.6f} | "
            f"balanced={r2['cv_balanced_accuracy_mean']:.6f} | "
            f"strategic F1={r2['cv_strategic_f1_mean']:.6f}"
        )
        print(
            f"Δ  | macro F1={r2['cv_macro_f1_mean'] - r1['cv_macro_f1_mean']:+.6f} | "
            f"strategic F1={r2['cv_strategic_f1_mean'] - r1['cv_strategic_f1_mean']:+.6f}"
        )

    results = pd.DataFrame(rows)
    args.results.parent.mkdir(parents=True, exist_ok=True)
    results.to_csv(args.results, index=False)

    v1 = results.loc[results["version"] == "v1_final"].sort_values("seed").reset_index(drop=True)
    v2 = results.loc[results["version"] == "v2_recency_candidate"].sort_values("seed").reset_index(drop=True)

    s1 = version_summary(v1)
    s2 = version_summary(v2)

    deltas = {
        "macro_f1_mean_delta": float((v2["cv_macro_f1_mean"] - v1["cv_macro_f1_mean"]).mean()),
        "balanced_accuracy_mean_delta": float(
            (v2["cv_balanced_accuracy_mean"] - v1["cv_balanced_accuracy_mean"]).mean()
        ),
        "strategic_f1_mean_delta": float(
            (v2["cv_strategic_f1_mean"] - v1["cv_strategic_f1_mean"]).mean()
        ),
        "strategic_recall_mean_delta": float(
            (v2["cv_strategic_recall_mean"] - v1["cv_strategic_recall_mean"]).mean()
        ),
        "seeds_v2_macro_f1_better": int(
            (v2["cv_macro_f1_mean"] > v1["cv_macro_f1_mean"]).sum()
        ),
        "seeds_v2_strategic_f1_better": int(
            (v2["cv_strategic_f1_mean"] > v1["cv_strategic_f1_mean"]).sum()
        ),
        "seed_count": len(SEEDS),
    }

    summary = {
        "stage": "09g_v1_v2_seed_stability",
        "design": {
            "held_out_test_used": False,
            "seeds": SEEDS,
            "cv": {
                "type": "RepeatedStratifiedKFold",
                "splits": CV_SPLITS,
                "repeats": CV_REPEATS,
            },
            "v1": {
                "feature_count": len(v1_features),
                "strategy": "SMOTE",
            },
            "v2": {
                "feature_count": len(v2_features),
                "feature_set": v2_bundle.get("feature_set"),
                "strategy": v2_bundle.get("strategy"),
                "rf_params": v2_bundle.get("rf_params"),
            },
        },
        "v1_summary": s1,
        "v2_summary": s2,
        "paired_deltas_v2_minus_v1": deltas,
        "outputs": {"seed_results_csv": str(args.results)},
    }

    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(
        json.dumps(summary, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    print("\n=== STABILITY SUMMARY ===")
    print(
        "V1 mean macro F1: "
        f"{s1['cv_macro_f1_mean']['mean_across_seeds']:.6f}"
    )
    print(
        "V2 mean macro F1: "
        f"{s2['cv_macro_f1_mean']['mean_across_seeds']:.6f}"
    )
    print(f"Mean macro F1 delta: {deltas['macro_f1_mean_delta']:+.6f}")
    print(
        "V1 mean strategic F1: "
        f"{s1['cv_strategic_f1_mean']['mean_across_seeds']:.6f}"
    )
    print(
        "V2 mean strategic F1: "
        f"{s2['cv_strategic_f1_mean']['mean_across_seeds']:.6f}"
    )
    print(
        "Mean strategic F1 delta: "
        f"{deltas['strategic_f1_mean_delta']:+.6f}"
    )
    print(
        "Seeds where V2 macro F1 is better: "
        f"{deltas['seeds_v2_macro_f1_better']}/{len(SEEDS)}"
    )
    print(
        "Seeds where V2 strategic F1 is better: "
        f"{deltas['seeds_v2_strategic_f1_better']}/{len(SEEDS)}"
    )

    print(f"\nSaved results: {args.results}")
    print(f"Saved summary: {args.summary}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())