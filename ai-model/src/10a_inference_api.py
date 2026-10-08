"""10a - FastAPI inference service for final recency V2 model.

Endpoints
---------
GET /health
POST /steam/profile
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
import hashlib
import json
import logging
import os
from datetime import datetime, timezone
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
import requests


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

# Registro técnico opcional para demonstrações do TCC.
# Nunca incluir chave da Steam, identificador bruto ou lista de jogos.
TRACE_FILE = ROOT / "logs" / "inference_trace.jsonl"
LOGGER = logging.getLogger(__name__)


def save_inference_trace(
    steam_id: str,
    model_version: str,
    feature_set: str,
    features: list[str],
    vector: Any,
    coverage: float,
    predicted_category: str,
    probabilities: dict[str, float],
    model: Any,
) -> None:
    """Registra entradas e saídas observáveis; não representa raciocínio interno."""
    if os.getenv("AI_TRACE_ENABLED", "0").strip().lower() not in {"1", "true", "yes"}:
        return

    # Importâncias globais não são explicações locais de uma predição.
    estimator = getattr(model, "steps", None)
    if estimator:
        estimator = model.steps[-1][1]
    else:
        estimator = model
    importances = getattr(estimator, "feature_importances_", None)
    global_importances = []
    if importances is not None and len(importances) == len(features):
        global_importances = [
            {"caracteristica": name, "importancia_global": round(float(value), 6)}
            for name, value in sorted(
                zip(features, importances),
                key=lambda pair: float(pair[1]), reverse=True
            )[:10]
        ]

    feature_values = {}
    for name in features:
        value = vector.get(name) if hasattr(vector, "get") else vector[name]
        try:
            feature_values[name] = round(float(value), 6)
        except (TypeError, ValueError):
            # Não registrar valores textuais potencialmente identificáveis.
            continue

    record = {
        "registrado_em_utc": datetime.now(timezone.utc).isoformat(),
        "steam_id_hash": hashlib.sha256(steam_id.encode("utf-8")).hexdigest()[:16],
        "modelo_versao": str(model_version),
        "conjunto_caracteristicas": str(feature_set),
        "etapas": [
            "SteamID resolvido e biblioteca consultada",
            "Jogos convertidos em características numéricas",
            "Cobertura mínima das categorias validada",
            "Modelo treinado executou predict e predict_proba",
            "Categoria prevista e probabilidades registradas",
        ],
        "cobertura_horas_categorizadas": round(coverage, 6),
        "quantidade_caracteristicas": len(features),
        "caracteristicas_entrada": feature_values,
        "categoria_prevista": predicted_category,
        "probabilidades": {k: round(float(v), 6) for k, v in probabilities.items()},
        "importancias_globais_modelo": global_importances,
        "aviso": (
            "Registro de etapas, entradas e saídas observáveis; "
            "importâncias globais não explicam individualmente esta previsão."
        ),
    }
    try:
        TRACE_FILE.parent.mkdir(parents=True, exist_ok=True)
        with TRACE_FILE.open("a", encoding="utf-8") as file:
            file.write(json.dumps(record, ensure_ascii=False, allow_nan=False) + "\n")
    except (OSError, ValueError) as exc:
        # Nunca impedir uma predição válida por falha na gravação do registro.
        LOGGER.warning("Não foi possível gravar o registro de inferência: %s", exc)



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


@app.post("/steam/profile")
def steam_profile(
    request: PredictRequest,
) -> dict[str, Any]:
    if (
        state.live is None
        or state.steam_api_key is None
    ):
        raise HTTPException(
            status_code=503,
            detail="Inference service is not ready.",
        )

    supplied_identifier = request.steamId.strip()

    try:
        steam_id, _ = state.live.resolve_steam_identifier(
            identifier=supplied_identifier,
            api_key=state.steam_api_key,
            timeout=REQUEST_TIMEOUT,
        )

        response = requests.get(
            "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/",
            params={
                "key": state.steam_api_key,
                "steamids": steam_id,
            },
            timeout=REQUEST_TIMEOUT,
        )
        response.raise_for_status()

        players = response.json().get("response", {}).get("players", [])

        if not players:
            raise HTTPException(
                status_code=404,
                detail="Steam profile was not found.",
            )

        player = players[0]

        return {
            "status": "ok",
            "steamId": str(player.get("steamid", steam_id)),
            "personaName": str(player.get("personaname", "")),
            "avatarFull": str(player.get("avatarfull", "")),
            "countryCode": str(player.get("loccountrycode", "")),
            "profileUrl": str(player.get("profileurl", "")),
            "communityVisibilityState": int(
                player.get("communityvisibilitystate", 0)
            ),
        }

    except HTTPException:
        raise
    except ValueError as exc:
        raise HTTPException(
            status_code=400,
            detail=str(exc),
        ) from exc
    except requests.RequestException as exc:
        raise HTTPException(
            status_code=502,
            detail=f"Steam profile lookup failed: {exc}",
        ) from exc


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

        save_inference_trace(
            steam_id=steam_id,
            model_version=str(bundle.get("version", "recency_v2")),
            feature_set=str(bundle.get("feature_set", "não informado")),
            features=features,
            vector=vector,
            coverage=historical_coverage,
            predicted_category=predicted_category,
            probabilities=probabilities,
            model=model,
        )

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