"""HTTP inference service for Adaptive Trials.

Exposes the final optimized macro model to the .NET backend.

Endpoints:
- GET /health
- POST /predict

The prediction response contains:
- model probabilities;
- mapping coverage;
- real library summary used only for persistence/diagnostics.

The extra library summary does NOT change the 40 model features or the trained
model. It is descriptive metadata derived from the same visible Steam library.
"""

from __future__ import annotations

import importlib.util
import json
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

import joblib
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

ROOT = Path(__file__).resolve().parents[1]
LIVE_SCRIPT = ROOT / "src/08d_predict_live_steamid.py"
MODEL_PATH = ROOT / "models/final/macro_model_optimized.joblib"
METADATA_PATH = ROOT / "models/final/macro_model_optimized_metadata.json"
MAPPING_PATH = ROOT / "data/interim/final_game_category_scores_v2.csv"

MINIMUM_PLAYTIME_COVERAGE = 0.70


def load_live_module():
    spec = importlib.util.spec_from_file_location(
        "adaptive_trials_live_inference",
        LIVE_SCRIPT,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load inference module: {LIVE_SCRIPT}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


live = load_live_module()


class PredictionRequest(BaseModel):
    steamId: str = Field(min_length=1)


class ProbabilitiesResponse(BaseModel):
    combat: float
    exploration: float
    strategic_reasoning: float


class ProfileQualityResponse(BaseModel):
    total_library_games: int
    total_played_games: int
    categorized_games: int
    categorized_played_games: int
    category_game_coverage: float
    category_playtime_coverage: float
    minimum_playtime_coverage: float


class LibrarySummaryResponse(BaseModel):
    total_playtime_hours: float
    num_games: int
    games_combat: int
    games_exploration: int
    games_strategic_reasoning: int
    hours_combat: float
    hours_exploration: float
    hours_strategic_reasoning: float


class PredictionResponse(BaseModel):
    status: str
    steamId: str
    predicted_category: str
    probabilities: ProbabilitiesResponse
    profile_quality: ProfileQualityResponse
    library_summary: LibrarySummaryResponse
    feature_count: int
    feature_set: str


def build_library_summary(
    games: list[dict[str, int]],
    mapping: dict[int, dict[str, str]],
) -> dict[str, Any]:
    playtime_by_category = {
        "combat": 0,
        "exploration": 0,
        "strategic_reasoning": 0,
    }
    games_by_category = {
        "combat": 0,
        "exploration": 0,
        "strategic_reasoning": 0,
    }

    total_playtime_minutes = 0

    for game in games:
        appid = int(game["appid"])
        playtime = int(game["playtime_forever_minutes"])
        total_playtime_minutes += playtime

        mapped = mapping.get(appid)
        if mapped is None:
            continue

        category = str(mapped.get("assigned_category", "")).strip()

        if category not in games_by_category:
            continue

        games_by_category[category] += 1
        playtime_by_category[category] += playtime

    return {
        "total_playtime_hours": round(
            total_playtime_minutes / 60.0,
            6,
        ),
        "num_games": len(games),
        "games_combat": games_by_category["combat"],
        "games_exploration": games_by_category["exploration"],
        "games_strategic_reasoning": games_by_category[
            "strategic_reasoning"
        ],
        "hours_combat": round(
            playtime_by_category["combat"] / 60.0,
            6,
        ),
        "hours_exploration": round(
            playtime_by_category["exploration"] / 60.0,
            6,
        ),
        "hours_strategic_reasoning": round(
            playtime_by_category["strategic_reasoning"] / 60.0,
            6,
        ),
    }


@asynccontextmanager
async def lifespan(app: FastAPI):
    if not MODEL_PATH.exists():
        raise RuntimeError(f"Model not found: {MODEL_PATH}")
    if not MAPPING_PATH.exists():
        raise RuntimeError(f"Mapping not found: {MAPPING_PATH}")

    api_key = live.load_api_key()
    session = live.create_http_session()
    mapping = live.read_mapping(MAPPING_PATH)
    bundle = joblib.load(MODEL_PATH)

    if not isinstance(bundle, dict):
        raise RuntimeError("Unexpected final model artifact format.")

    pipeline = bundle.get("pipeline")
    features = bundle.get("features")

    if pipeline is None or not isinstance(features, list):
        raise RuntimeError("Final model bundle is incomplete.")

    metadata: dict[str, Any] = {}
    if METADATA_PATH.exists():
        with METADATA_PATH.open("r", encoding="utf-8") as file:
            metadata = json.load(file)

    app.state.api_key = api_key
    app.state.session = session
    app.state.mapping = mapping
    app.state.bundle = bundle
    app.state.pipeline = pipeline
    app.state.features = features
    app.state.metadata = metadata

    yield

    session.close()


app = FastAPI(
    title="Adaptive Trials AI Inference",
    version="1.1.0",
    lifespan=lifespan,
)


@app.get("/health")
def health() -> dict[str, Any]:
    return {
        "status": "ok",
        "model": "macro_model_optimized",
        "featureCount": len(app.state.features),
        "featureSet": app.state.bundle.get("feature_set"),
        "trainingProfiles": app.state.metadata.get("training_profiles"),
    }


@app.post("/predict", response_model=PredictionResponse)
def predict(request: PredictionRequest) -> PredictionResponse:
    steam_id = request.steamId.strip()

    if not steam_id.isdigit():
        raise HTTPException(
            status_code=400,
            detail="steamId must contain only digits.",
        )

    try:
        raw_games = live.fetch_owned_games(
            app.state.session,
            app.state.api_key,
            steam_id,
            30.0,
        )
        games = live.normalize_games(raw_games)

        features = live.build_features(
            games,
            app.state.mapping,
        )

        library_summary = build_library_summary(
            games,
            app.state.mapping,
        )

        playtime_coverage = float(
            features["category_playtime_coverage"]
        )

        quality = {
            "total_library_games": int(
                features["total_library_games"]
            ),
            "total_played_games": int(
                features["total_played_games"]
            ),
            "categorized_games": int(
                features["categorized_games"]
            ),
            "categorized_played_games": int(
                features["categorized_played_games"]
            ),
            "category_game_coverage": round(
                float(features["category_game_coverage"]),
                6,
            ),
            "category_playtime_coverage": round(
                playtime_coverage,
                6,
            ),
            "minimum_playtime_coverage": (
                MINIMUM_PLAYTIME_COVERAGE
            ),
        }

        if playtime_coverage < MINIMUM_PLAYTIME_COVERAGE:
            raise HTTPException(
                status_code=422,
                detail={
                    "code": "insufficient_mapping_coverage",
                    "message": (
                        "Categorized playtime coverage is below "
                        "the minimum threshold."
                    ),
                    "profileQuality": quality,
                },
            )

        X = live.vectorize(
            features,
            app.state.features,
        )

        pipeline = app.state.pipeline
        predicted_category = str(
            pipeline.predict(X)[0]
        )
        raw_probabilities = pipeline.predict_proba(X)[0]
        classes = live.get_classes(pipeline)

        probabilities = {
            label: round(float(probability), 8)
            for label, probability in zip(
                classes,
                raw_probabilities,
                strict=True,
            )
        }

        return PredictionResponse(
            status="ok",
            steamId=steam_id,
            predicted_category=predicted_category,
            probabilities=ProbabilitiesResponse(
                combat=probabilities["combat"],
                exploration=probabilities["exploration"],
                strategic_reasoning=probabilities[
                    "strategic_reasoning"
                ],
            ),
            profile_quality=ProfileQualityResponse(
                **quality
            ),
            library_summary=LibrarySummaryResponse(
                **library_summary
            ),
            feature_count=len(app.state.features),
            feature_set=str(
                app.state.bundle.get("feature_set")
            ),
        )

    except HTTPException:
        raise
    except PermissionError as error:
        raise HTTPException(
            status_code=502,
            detail=str(error),
        ) from error
    except ValueError as error:
        raise HTTPException(
            status_code=422,
            detail=str(error),
        ) from error
    except Exception as error:
        raise HTTPException(
            status_code=500,
            detail=f"Inference failed: {error}",
        ) from error