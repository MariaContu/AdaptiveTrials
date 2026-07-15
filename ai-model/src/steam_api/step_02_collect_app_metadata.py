import json
import time
from typing import Any

import pandas as pd
import requests

from src.steam_api.config import (
    METRICS_DIR,
    OWNED_GAMES_FILE,
    RAW_DATA_DIR,
    REQUEST_TIMEOUT_SECONDS,
)


STEAM_SPY_URL = "https://steamspy.com/api.php"

APP_METADATA_FILE = (
    RAW_DATA_DIR
    / "steam_api_app_metadata.csv"
)

METADATA_SUMMARY_FILE = (
    METRICS_DIR
    / "steam_api_metadata_summary.json"
)

STEAM_SPY_REQUEST_DELAY_SECONDS = 1.0
MAX_REQUEST_RETRIES = 5


def serialize_weighted_tags(
    tags: object,
) -> str:
    """Preserva as tags e seus respectivos pesos."""

    if not isinstance(tags, dict):
        return "{}"

    return json.dumps(
        tags,
        ensure_ascii=False,
    )

def request_app_metadata(
    appid: int,
) -> dict[str, Any] | None:
    """Consulta os metadados de um jogo pelo AppID."""

    for attempt in range(
        1,
        MAX_REQUEST_RETRIES + 1,
    ):
        try:
            response = requests.get(
                STEAM_SPY_URL,
                params={
                    "request": "appdetails",
                    "appid": appid,
                },
                timeout=REQUEST_TIMEOUT_SECONDS,
            )

            if response.status_code == 429:
                wait_seconds = attempt * 10

                print(
                    "Rate limit recebido. "
                    f"Aguardando {wait_seconds}s."
                )

                time.sleep(wait_seconds)
                continue

            if response.status_code >= 500:
                wait_seconds = attempt * 5

                print(
                    f"Erro HTTP {response.status_code}. "
                    f"Nova tentativa em {wait_seconds}s."
                )

                time.sleep(wait_seconds)
                continue

            response.raise_for_status()

            metadata = response.json()

            if not metadata:
                return None

            return metadata

        except (
            requests.RequestException,
            ValueError,
        ) as error:
            print(
                f"Falha no AppID {appid} "
                f"(tentativa "
                f"{attempt}/{MAX_REQUEST_RETRIES}): "
                f"{error}"
            )

            if attempt < MAX_REQUEST_RETRIES:
                time.sleep(attempt * 5)

    return None


def normalize_tags(
    tags: object,
) -> str:
    """Converte as tags retornadas em uma representação textual."""

    if not isinstance(tags, dict):
        return ""

    return ";".join(
        str(tag)
        for tag in tags.keys()
    )


def collect_app_metadata() -> tuple[
    pd.DataFrame,
    dict[str, int],
]:
    """Coleta metadados dos AppIDs presentes nas bibliotecas."""

    print("\n" + "=" * 60)
    print("Coleta de metadados dos AppIDs")
    print("=" * 60)

    owned_games_dataframe = pd.read_csv(
        OWNED_GAMES_FILE
    )

    app_ids = (
        owned_games_dataframe["appid"]
        .drop_duplicates()
        .astype(int)
        .tolist()
    )

    print(
        f"AppIDs únicos recebidos: "
        f"{len(app_ids)}"
    )

    metadata_records = []

    unavailable_appids = 0
    appids_without_genres = 0
    appids_without_tags = 0

    for index, appid in enumerate(
        app_ids,
        start=1,
    ):
        metadata = request_app_metadata(
            appid=appid
        )

        if metadata is None:
            unavailable_appids += 1
            continue

        genres = str(
            metadata.get("genre", "")
        ).strip()

        tags = normalize_tags(
            metadata.get("tags")
        )

        if not genres:
            appids_without_genres += 1

        if not tags:
            appids_without_tags += 1

        metadata_records.append(
            {
                "appid": appid,
                "name": metadata.get(
                    "name",
                    "",
                ),
                "genres": genres,
                "steamspy_tags": tags,
                "steamspy_tags_weighted": (
                    serialize_weighted_tags(
                        metadata.get("tags")
                    )
                ),
            }
        )


        print(
            f"\rAppIDs processados: "
            f"{index}/{len(app_ids)} | "
            f"Metadados obtidos: "
            f"{len(metadata_records)}",
            end="",
            flush=True,
        )

        time.sleep(
            STEAM_SPY_REQUEST_DELAY_SECONDS
        )

    print()

    metadata_dataframe = pd.DataFrame(
        metadata_records
    )

    summary = {
        "unique_appids_received": len(app_ids),
        "metadata_collected": len(
            metadata_dataframe
        ),
        "unavailable_appids": (
            unavailable_appids
        ),
        "appids_without_genres": (
            appids_without_genres
        ),
        "appids_without_tags": (
            appids_without_tags
        ),
    }

    return metadata_dataframe, summary


def save_metadata_results(
    dataframe: pd.DataFrame,
    summary: dict[str, int],
) -> None:
    """Salva os metadados e o resumo da coleta."""

    RAW_DATA_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    METRICS_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    dataframe.to_csv(
        APP_METADATA_FILE,
        index=False,
    )

    METADATA_SUMMARY_FILE.write_text(
        json.dumps(
            summary,
            indent=4,
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )


def print_metadata_summary(
    summary: dict[str, int],
) -> None:
    """Exibe o resumo da coleta de metadados."""

    collected = summary[
        "metadata_collected"
    ]

    total = summary[
        "unique_appids_received"
    ]

    coverage = (
        collected / total * 100
        if total > 0
        else 0
    )

    print("\n" + "=" * 60)
    print("Resumo da coleta de metadados")
    print("=" * 60)

    print(
        f"AppIDs recebidos: "
        f"{total}"
    )

    print(
        f"Metadados obtidos: "
        f"{collected}"
    )

    print(
        f"AppIDs indisponíveis: "
        f"{summary['unavailable_appids']}"
    )

    print(
        f"AppIDs sem gêneros: "
        f"{summary['appids_without_genres']}"
    )

    print(
        f"AppIDs sem tags: "
        f"{summary['appids_without_tags']}"
    )

    print(
        f"Cobertura dos metadados: "
        f"{coverage:.2f}%"
    )


def main() -> None:
    dataframe, summary = (
        collect_app_metadata()
    )

    save_metadata_results(
        dataframe=dataframe,
        summary=summary,
    )

    print_metadata_summary(
        summary=summary,
    )


if __name__ == "__main__":
    main()