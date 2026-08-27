"""09i - Refit the selected recency V2 macro model on all resolved profiles.

This stage performs deployment refit only.

No new performance metric is computed here because model selection and
evaluation were completed in stages 09f, 09g and 09h.

Inputs
------
- data/processed/final_player_profiles_recency_v2.csv
- models/experimental/macro_model_recency_v2_candidate.joblib

Outputs
-------
- models/final/macro_model_recency_v2.joblib
- models/final/macro_model_recency_v2_metadata.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier

ROOT = Path(__file__).resolve().parents[1]

DEFAULT_PROFILES = (
    ROOT / "data/processed/final_player_profiles_recency_v2.csv"
)
DEFAULT_CANDIDATE = (
    ROOT / "models/experimental/macro_model_recency_v2_candidate.joblib"
)
DEFAULT_MODEL_OUT = (
    ROOT / "models/final/macro_model_recency_v2.joblib"
)
DEFAULT_METADATA_OUT = (
    ROOT / "models/final/macro_model_recency_v2_metadata.json"
)

TARGET_CANDIDATES = (
    "target_macro",
    "macro_target",
    "target",
    "resolved_target",
    "target_category",
)

VALID_CLASSES = {
    "combat",
    "exploration",
    "strategic_reasoning",
}

RANDOM_STATE = 42


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--profiles", type=Path, default=DEFAULT_PROFILES)
    p.add_argument("--candidate", type=Path, default=DEFAULT_CANDIDATE)
    p.add_argument("--model-output", type=Path, default=DEFAULT_MODEL_OUT)
    p.add_argument("--metadata-output", type=Path, default=DEFAULT_METADATA_OUT)
    return p.parse_args()


def detect_target(df: pd.DataFrame) -> str:
    for column in TARGET_CANDIDATES:
        if column in df.columns:
            return column
    raise ValueError(
        f"Could not detect target column. Tried: {list(TARGET_CANDIDATES)}"
    )


def load_candidate(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(path)

    bundle = joblib.load(path)

    if not isinstance(bundle, dict):
        raise ValueError("Unexpected candidate bundle format.")

    features = bundle.get("features")
    params = bundle.get("rf_params")

    if not isinstance(features, list) or not features:
        raise ValueError("Candidate bundle has no valid feature list.")

    if not isinstance(params, dict):
        raise ValueError("Candidate bundle has no rf_params.")

    if bundle.get("strategy") != "class_weight":
        raise ValueError(
            "Selected V2 candidate is expected to use class_weight."
        )

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
            f"Profile dataset missing features: {missing}"
        )

    X = df[features].apply(
        pd.to_numeric,
        errors="coerce",
    )

    if X.isna().any().any():
        bad = X.columns[X.isna().any()].tolist()
        raise ValueError(
            f"Invalid/missing numeric values in: {bad}"
        )

    if not np.isfinite(X.to_numpy()).all():
        raise ValueError("Infinite values found in feature matrix.")

    return X


def main() -> int:
    args = parse_args()

    profiles = pd.read_csv(args.profiles)
    candidate = load_candidate(args.candidate)

    target_column = detect_target(profiles)

    targets = (
        profiles[target_column]
        .fillna("")
        .astype(str)
        .str.strip()
    )

    resolved_mask = targets.isin(VALID_CLASSES)

    resolved = profiles.loc[resolved_mask].copy()
    y = (
        resolved[target_column]
        .astype(str)
        .reset_index(drop=True)
    )

    features = [
        str(feature)
        for feature in candidate["features"]
    ]

    X = numeric_matrix(
        resolved,
        features,
    ).reset_index(drop=True)

    params = candidate["rf_params"]

    model = RandomForestClassifier(
        n_estimators=int(params["n_estimators"]),
        max_depth=(
            None
            if params.get("max_depth") is None
            else int(params["max_depth"])
        ),
        min_samples_split=int(params["min_samples_split"]),
        min_samples_leaf=int(params["min_samples_leaf"]),
        max_features=str(params["max_features"]),
        class_weight="balanced",
        random_state=RANDOM_STATE,
        n_jobs=-1,
    )

    model.fit(X, y)

    classes = [
        str(value)
        for value in model.classes_
    ]

    class_counts = {
        str(key): int(value)
        for key, value in y.value_counts().to_dict().items()
    }

    final_bundle = {
        "pipeline": model,
        "features": features,
        "feature_set": candidate.get("feature_set"),
        "classes": classes,
        "random_state": RANDOM_STATE,
        "strategy": "class_weight",
        "rf_params": {
            "n_estimators": int(params["n_estimators"]),
            "max_depth": (
                None
                if params.get("max_depth") is None
                else int(params["max_depth"])
            ),
            "min_samples_split": int(params["min_samples_split"]),
            "min_samples_leaf": int(params["min_samples_leaf"]),
            "max_features": str(params["max_features"]),
        },
        "training_profiles": int(len(resolved)),
        "status": "final_deployment_refit",
        "version": "recency_v2",
    }

    args.model_output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    joblib.dump(
        final_bundle,
        args.model_output,
    )

    metadata = {
        "model_name": "macro_model_recency_v2",
        "version": "recency_v2",
        "purpose": "deployment_refit",
        "performance_metrics_recomputed": False,
        "reason_no_new_metrics": (
            "The final model was refit on all resolved profiles only after "
            "the configuration had been selected and evaluated in stages "
            "09f, 09g and 09h. Recomputing performance on these same profiles "
            "would not provide an unbiased estimate."
        ),
        "training": {
            "total_profile_rows": int(len(profiles)),
            "resolved_profiles_used": int(len(resolved)),
            "unresolved_profiles_excluded": int(
                len(profiles) - len(resolved)
            ),
            "class_counts": class_counts,
            "target_column": target_column,
        },
        "features": {
            "feature_count": len(features),
            "feature_set": candidate.get("feature_set"),
            "feature_names": features,
        },
        "model": {
            "algorithm": "RandomForestClassifier",
            "strategy": "class_weight",
            "class_weight": "balanced",
            "random_state": RANDOM_STATE,
            "rf_params": final_bundle["rf_params"],
            "classes": classes,
        },
        "selection_evidence": {
            "optimization_stage": "09f",
            "seed_stability_stage": "09g",
            "final_heldout_comparison_stage": "09h",
        },
        "outputs": {
            "model": str(args.model_output),
        },
    }

    args.metadata_output.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    args.metadata_output.write_text(
        json.dumps(
            metadata,
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print("\n=== FINAL RECENCY V2 REFIT ===")
    print(f"Resolved profiles used: {len(resolved)}")
    print(f"Feature count: {len(features)}")
    print(
        f"Feature set: {candidate.get('feature_set')}"
    )
    print("Strategy: class_weight")
    print(
        "RF params: "
        f"{final_bundle['rf_params']}"
    )

    print("\nClass counts:")
    for label, count in class_counts.items():
        print(f"  {label}: {count}")

    print(
        "\nNo new performance metric was computed "
        "(deployment refit only)."
    )

    print(f"\nSaved final V2 model: {args.model_output}")
    print(
        f"Saved metadata: {args.metadata_output}"
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())