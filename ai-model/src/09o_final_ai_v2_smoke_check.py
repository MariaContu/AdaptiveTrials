"""09o - Final smoke/reproducibility check for the AI V2 deployment.

This script DOES NOT train or evaluate a model.
It only validates that the frozen deployment artifacts can be loaded
consistently from a clean project checkout/environment.

Checks:
- required files exist
- final model bundle loads
- model version / feature set / feature count
- expected classes
- estimator feature count
- final mapping loads through the live-inference implementation
- .env exposes a Steam API key
- main runtime dependencies are installed
"""

from __future__ import annotations

import importlib
import importlib.util
import os
import sys
from pathlib import Path
from typing import Any

import joblib
from dotenv import load_dotenv


ROOT = Path(__file__).resolve().parents[1]

MODEL_PATH = ROOT / "models/final/macro_model_recency_v2.joblib"
METADATA_PATH = ROOT / "models/final/macro_model_recency_v2_metadata.json"
FEATURE_CONFIG_PATH = ROOT / "config/model_features_recency_v2.json"
MAPPING_PATH = ROOT / "data/interim/final_game_category_scores_v2.csv"
LIVE_INFERENCE_PATH = ROOT / "src/09l_predict_live_steamid_recency_v2.py"
API_PATH = ROOT / "src/10a_inference_api.py"
REQUIREMENTS_PATH = ROOT / "requirements.txt"

EXPECTED_VERSION = "recency_v2"
EXPECTED_FEATURE_SET = "recency_counts_49"
EXPECTED_FEATURE_COUNT = 49
EXPECTED_CLASSES = {
    "combat",
    "exploration",
    "strategic_reasoning",
}

DEPENDENCY_IMPORTS = {
    "numpy": "numpy",
    "pandas": "pandas",
    "scikit-learn": "sklearn",
    "imbalanced-learn": "imblearn",
    "joblib": "joblib",
    "requests": "requests",
    "python-dotenv": "dotenv",
    "matplotlib": "matplotlib",
    "fastapi": "fastapi",
    "uvicorn": "uvicorn",
}


def ok(message: str) -> None:
    print(f"[OK]   {message}")


def fail(message: str) -> None:
    print(f"[FAIL] {message}")
    raise RuntimeError(message)


def check_file(path: Path, label: str) -> None:
    if not path.exists():
        fail(f"{label} not found: {path}")
    ok(f"{label}: {path.relative_to(ROOT)}")


def load_module_from_path(
    module_name: str,
    path: Path,
) -> Any:
    spec = importlib.util.spec_from_file_location(
        module_name,
        path,
    )
    if spec is None or spec.loader is None:
        fail(f"Could not import module from {path}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> int:
    print("\n=== FINAL AI V2 SMOKE / REPRODUCIBILITY CHECK ===\n")

    # -------------------------------------------------
    # 1. Required deployment/reproducibility files
    # -------------------------------------------------
    required_files = [
        (MODEL_PATH, "Final V2 model"),
        (METADATA_PATH, "Final V2 metadata"),
        (FEATURE_CONFIG_PATH, "V2 feature config"),
        (MAPPING_PATH, "Final game-category mapping"),
        (LIVE_INFERENCE_PATH, "Live inference script"),
        (API_PATH, "FastAPI inference service"),
        (REQUIREMENTS_PATH, "requirements.txt"),
    ]

    for path, label in required_files:
        check_file(path, label)

    # -------------------------------------------------
    # 2. Environment / Steam key
    # -------------------------------------------------
    load_dotenv(ROOT / ".env")

    steam_key = (
        os.getenv("STEAM_API_KEY")
        or os.getenv("STEAM_WEB_API_KEY")
    )

    if not steam_key:
        fail(
            "Steam API key not found in environment "
            "(STEAM_API_KEY or STEAM_WEB_API_KEY)."
        )

    ok("Steam API key is available (value not displayed)")

    # -------------------------------------------------
    # 3. Runtime dependencies
    # -------------------------------------------------
    for package_name, import_name in DEPENDENCY_IMPORTS.items():
        try:
            importlib.import_module(import_name)
        except Exception as exc:
            fail(
                f"Dependency import failed: "
                f"{package_name} ({exc})"
            )
        ok(f"Dependency import: {package_name}")

    # -------------------------------------------------
    # 4. Final model bundle
    # -------------------------------------------------
    try:
        bundle = joblib.load(MODEL_PATH)
    except Exception as exc:
        fail(f"Could not load final V2 model: {exc}")

    if not isinstance(bundle, dict):
        fail(
            "Final V2 artifact has unexpected format "
            f"({type(bundle).__name__}); expected dict bundle."
        )

    ok("Final V2 joblib bundle loads successfully")

    required_bundle_keys = {
        "pipeline",
        "features",
        "classes",
    }

    missing_bundle_keys = (
        required_bundle_keys - set(bundle.keys())
    )

    if missing_bundle_keys:
        fail(
            "Final V2 bundle is missing required keys: "
            f"{sorted(missing_bundle_keys)}"
        )

    ok("Final V2 bundle contains required keys")

    # -------------------------------------------------
    # 5. Frozen model identity
    # -------------------------------------------------
    version = str(
        bundle.get("version", EXPECTED_VERSION)
    )

    if version != EXPECTED_VERSION:
        fail(
            f"Unexpected model version: {version}; "
            f"expected {EXPECTED_VERSION}"
        )

    ok(f"Model version: {version}")

    feature_set = str(
        bundle.get(
            "feature_set",
            EXPECTED_FEATURE_SET,
        )
    )

    if feature_set != EXPECTED_FEATURE_SET:
        fail(
            f"Unexpected feature set: {feature_set}; "
            f"expected {EXPECTED_FEATURE_SET}"
        )

    ok(f"Feature set: {feature_set}")

    features = [
        str(value)
        for value in bundle["features"]
    ]

    if len(features) != EXPECTED_FEATURE_COUNT:
        fail(
            f"Unexpected feature count: {len(features)}; "
            f"expected {EXPECTED_FEATURE_COUNT}"
        )

    if len(set(features)) != len(features):
        fail("Duplicate feature names found in final bundle")

    ok(f"Feature count: {len(features)}")
    ok("Feature names are unique")

    classes = {
        str(value)
        for value in bundle["classes"]
    }

    if classes != EXPECTED_CLASSES:
        fail(
            f"Unexpected classes: {sorted(classes)}; "
            f"expected {sorted(EXPECTED_CLASSES)}"
        )

    ok(
        "Classes: combat, exploration, strategic_reasoning"
    )

    # -------------------------------------------------
    # 6. Estimator consistency
    # -------------------------------------------------
    estimator = bundle["pipeline"]

    if not hasattr(estimator, "predict"):
        fail("Final estimator does not expose predict()")

    if not hasattr(estimator, "predict_proba"):
        fail("Final estimator does not expose predict_proba()")

    ok("Estimator exposes predict() and predict_proba()")

    fitted_feature_count = getattr(
        estimator,
        "n_features_in_",
        None,
    )

    if fitted_feature_count is not None:
        if int(fitted_feature_count) != EXPECTED_FEATURE_COUNT:
            fail(
                "Estimator n_features_in_ mismatch: "
                f"{fitted_feature_count} vs "
                f"{EXPECTED_FEATURE_COUNT}"
            )

        ok(
            f"Estimator fitted feature count: "
            f"{fitted_feature_count}"
        )
    else:
        print(
            "[WARN] Estimator does not expose n_features_in_; "
            "bundle feature count was validated."
        )

    estimator_classes = getattr(
        estimator,
        "classes_",
        None,
    )

    if estimator_classes is not None:
        estimator_classes = {
            str(value)
            for value in estimator_classes
        }

        if estimator_classes != EXPECTED_CLASSES:
            fail(
                "Estimator class set does not match bundle classes"
            )

        ok("Estimator classes match bundle classes")

    # -------------------------------------------------
    # 7. Live-inference reconstruction layer
    # -------------------------------------------------
    live = load_module_from_path(
        "recency_v2_live_smoke",
        LIVE_INFERENCE_PATH,
    )

    required_live_functions = [
        "load_model_bundle",
        "load_mapping",
        "resolve_steam_identifier",
        "fetch_owned_games",
        "build_feature_vector",
        "vector_to_frame",
    ]

    for name in required_live_functions:
        if not hasattr(live, name):
            fail(
                f"Live inference module is missing function: {name}"
            )

    ok("Live inference module exposes required functions")

    try:
        live_bundle = live.load_model_bundle(
            MODEL_PATH
        )
    except Exception as exc:
        fail(
            f"Live inference could not load final bundle: {exc}"
        )

    live_features = [
        str(value)
        for value in live_bundle["features"]
    ]

    if live_features != features:
        fail(
            "Live inference bundle feature order differs "
            "from direct artifact load"
        )

    ok("Live inference preserves the 49-feature order")

    try:
        mapping = live.load_mapping(
            MAPPING_PATH
        )
    except Exception as exc:
        fail(
            f"Final game-category mapping could not be loaded: {exc}"
        )

    if mapping is None:
        fail("Mapping loader returned None")

    try:
        mapping_size = len(mapping)
    except Exception:
        mapping_size = None

    if mapping_size is not None:
        ok(f"Final mapping loads successfully ({mapping_size} rows)")
    else:
        ok("Final mapping loads successfully")

    # -------------------------------------------------
    # 8. FastAPI module importability
    # -------------------------------------------------
    try:
        api = load_module_from_path(
            "recency_v2_api_smoke",
            API_PATH,
        )
    except Exception as exc:
        fail(f"FastAPI module import failed: {exc}")

    if not hasattr(api, "app"):
        fail("FastAPI module does not expose `app`")

    ok("FastAPI module imports and exposes `app`")

    # -------------------------------------------------
    # Final result
    # -------------------------------------------------
    print("\n=== RESULT ===")
    print("AI V2 deployment smoke check: PASS")
    print(f"Model: {MODEL_PATH.name}")
    print(f"Version: {EXPECTED_VERSION}")
    print(f"Feature set: {EXPECTED_FEATURE_SET}")
    print(f"Feature count: {EXPECTED_FEATURE_COUNT}")
    print(
        "Classes: combat / exploration / strategic_reasoning"
    )
    print(
        "\nNo model was trained or modified by this check."
    )

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"\nSmoke check failed: {exc}")
        raise SystemExit(1)
