"""10a - FastAPI inference service for final recency V2 model.

Endpoints
---------
GET /health
POST /predict

POST /predict input:
{
  "steamId": "7656119..."
}

or:
{
  "steamId": "customVanityName"
}

The API keeps the previous backend-facing fields and adds recency metadata.

Run
---
uvicorn --app-dir src "10a_inference_api:app" --host 127.0.0.1 --port 8001
"""

from __future__ import annotations

import importlib.util
import os
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field


ROOT = Path(__file__).resolve().parents[1]

LIVE_SCRIPT = (
    ROOT / "src/09l_predict_live_steamid_recency_v2.py"
)

MODEL_PATH = (
    ROOT / "models/final/macro_model_recency_v2.joblib"
)

MAPPING_PATH = (
    ROOT / "data/interim/final_game_category_scores_v2.csv"
)

MIN_COVERAGE = 0.70
REQUEST_TIMEOUT = 30.0


def load_live_module():
    if not LIVE_SCRIPT.exists():
        raise FileNotFoundError(
            f"Live inference script not found: {LIVE_SCRIPT}"
        )

    spec = importlib.util.spec_from_file_location(
        "recency_v2_live_inference",
        LIVE_SCRIPT,
    )

    if spec is None or spec.loader is None:
        raise RuntimeError(
            "Could not create import specification "
            "for live V2 inference module."
        )

    module = importlib.util.module_from_spec(
        spec
    )

    spec.loader.exec_module(
        module
    )

    return module


class PredictRequest(BaseModel):
    steamId: str = Field(
        ...,
        min_length=1,
        description=(
            "SteamID64, Steam vanity name, "
            "or Steam Community profile URL."
        ),
    )


class AppState:
    live: Any = None
    model_bundle: dict[str, Any] | None = None
    mapping: Any = None
    steam_api_key: str | None = None


state = AppState()


@asynccontextmanager
async def lifespan(app: FastAPI):
    load_dotenv(
        ROOT / ".env"
    )

    api_key = (
        os.getenv(
            "STEAM_API_KEY"
        )
        or os.getenv(
            "STEAM_WEB_API_KEY"
        )
    )

    if not api_key:
        raise RuntimeError(
            "Steam API key not found. "
            "Set STEAM_API_KEY in ai-model/.env."
        )

    live = load_live_module()

    model_bundle = live.load_model_bundle(
        MODEL_PATH
    )

    mapping = live.load_mapping(
        MAPPING_PATH
    )

    state.live = live
    state.model_bundle = model_bundle
    state.mapping = mapping
    state.steam_api_key = api_key

    yield

    state.live = None
    state.model_bundle = None
    state.mapping = None
    state.steam_api_key = None


app = FastAPI(
    title="Adaptive Trials AI Inference API",
    version="2.0.0",
    description=(
        "Serves the final recency-aware macro recommendation "
        "model for Adaptive Trials."
    ),
    lifespan=lifespan,
)


@app.get("/health")
def health() -> dict[str, Any]:
    if (
        state.live is None
        or state.model_bundle is None
        or state.mapping is None
        or state.steam_api_key is None
    ):
        raise HTTPException(
            status_code=503,
            detail="Inference service is not ready.",
        )

    bundle = state.model_bundle

    return {
        "status": "ok",
        "model": "macro_model_recency_v2",
        "modelVersion": bundle.get(
            "version",
            "recency_v2",
        ),
        "featureCount": len(
            bundle["features"]
        ),
        "featureSet": bundle.get(
            "feature_set"
        ),
        "trainingProfiles": bundle.get(
            "training_profiles",
            686,
        ),
        "strategy": bundle.get(
            "strategy",
            "class_weight",
        ),
        "supportsVanityIdentifiers": True,
        "supportsRecentActivity": True,
    }


@app.post("/predict")
def predict(
    request: PredictRequest,
) -> dict[str, Any]:
    if (
        state.live is None
        or state.model_bundle is None
        or state.mapping is None
        or state.steam_api_key is None
    ):
        raise HTTPException(
            status_code=503,
            detail="Inference service is not ready.",
        )

    live = state.live
    bundle = state.model_bundle

    supplied_identifier = (
        request.steamId.strip()
    )

    try:
        (
            steam_id,
            identifier_type,
        ) = live.resolve_steam_identifier(
            identifier=supplied_identifier,
            api_key=state.steam_api_key,
            timeout=REQUEST_TIMEOUT,
        )

        games = live.fetch_owned_games(
            steam_id=steam_id,
            api_key=state.steam_api_key,
            timeout=REQUEST_TIMEOUT,
        )

        (
            vector,
            library_summary,
            recent_summary,
        ) = live.build_feature_vector(
            games=games,
            mapping=state.mapping,
        )

        historical_coverage = float(
            vector[
                "category_playtime_coverage"
            ]
        )

        if (
            historical_coverage
            < MIN_COVERAGE
        ):
            raise HTTPException(
                status_code=422,
                detail={
                    "code": (
                        "INSUFFICIENT_PROFILE_COVERAGE"
                    ),
                    "message": (
                        "Steam profile does not have "
                        "enough categorized historical "
                        "playtime for model inference."
                    ),
                    "categoryPlaytimeCoverage": (
                        historical_coverage
                    ),
                    "minimumRequiredCoverage": (
                        MIN_COVERAGE
                    ),
                },
            )

        features = [
            str(feature)
            for feature in bundle[
                "features"
            ]
        ]

        X = live.vector_to_frame(
            vector,
            features,
        )

        model = bundle[
            "pipeline"
        ]

        predicted_category = str(
            model.predict(
                X
            )[0]
        )

        raw_probabilities = (
            model.predict_proba(
                X
            )[0]
        )

        model_classes = [
            str(value)
            for value in model.classes_
        ]

        probabilities = {
            label: float(
                probability
            )
            for (
                label,
                probability,
            ) in zip(
                model_classes,
                raw_probabilities,
            )
        }

        return {
            "status": "ok",

            # Keep original backend-facing SteamID field.
            "steamId": steam_id,

            # Additional identifier metadata.
            "steamIdentifierInput": (
                supplied_identifier
            ),
            "steamIdentifierType": (
                identifier_type
            ),

            "predicted_category": (
                predicted_category
            ),
            "probabilities": (
                probabilities
            ),

            "profile_quality": {
                "minimum_required_playtime_coverage": (
                    MIN_COVERAGE
                ),
                "category_game_coverage": float(
                    vector[
                        "category_game_coverage"
                    ]
                ),
                "category_playtime_coverage": (
                    historical_coverage
                ),
            },

            "library_summary": (
                library_summary
            ),

            "recent_summary": (
                recent_summary
            ),

            "feature_count": len(
                features
            ),
            "feature_set": bundle.get(
                "feature_set"
            ),
            "model_version": bundle.get(
                "version",
                "recency_v2",
            ),
        }

    except HTTPException:
        raise

    except ValueError as exc:
        raise HTTPException(
            status_code=400,
            detail=str(exc),
        ) from exc

    except Exception as exc:
        raise HTTPException(
            status_code=502,
            detail=(
                "Steam/model inference failed: "
                f"{exc}"
            ),
        ) from exc