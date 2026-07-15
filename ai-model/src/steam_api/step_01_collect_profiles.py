import json
import time
from typing import Any

import pandas as pd
import requests

from src.steam_api.config import (
    COLLECTION_SUMMARY_FILE,
    MAX_REQUEST_RETRIES,
    METRICS_DIR,
    OWNED_GAMES_FILE,
    RAW_DATA_DIR,
    REQUEST_DELAY_SECONDS,
    REQUEST_TIMEOUT_SECONDS,
    REVIEWS_PER_PAGE,
    SEED_APP_IDS,
    STEAM_API_KEY,
    TARGET_VALID_PROFILES,
)


REVIEWS_URL = (
    "https://store.steampowered.com/appreviews/{appid}"
)

OWNED_GAMES_URL = (
    "https://api.steampowered.com/"
    "IPlayerService/GetOwnedGames/v0001/"
)


def validate_configuration() -> None:
    """Valida as configurações necessárias para a coleta."""

    if not STEAM_API_KEY:
        raise ValueError(
            "STEAM_API_KEY não encontrada. "
            "Adicione a chave no arquivo .env."
        )


def request_json(
    url: str,
    params: dict[str, Any],
) -> dict[str, Any] | None:
    """Executa uma requisição com retry para falhas temporárias."""

    for attempt in range(1, MAX_REQUEST_RETRIES + 1):
        try:
            response = requests.get(
                url,
                params=params,
                timeout=REQUEST_TIMEOUT_SECONDS,
            )

            if response.status_code == 429:
                wait_seconds = attempt * 5

                print(
                    "Rate limit recebido. "
                    f"Aguardando {wait_seconds}s."
                )

                time.sleep(wait_seconds)
                continue

            if response.status_code >= 500:
                wait_seconds = attempt * 2

                print(
                    f"Erro HTTP {response.status_code}. "
                    f"Nova tentativa em {wait_seconds}s."
                )

                time.sleep(wait_seconds)
                continue

            response.raise_for_status()

            return response.json()

        except (
            requests.RequestException,
            ValueError,
        ) as error:
            print(
                f"Falha na requisição "
                f"(tentativa {attempt}/{MAX_REQUEST_RETRIES}): "
                f"{error}"
            )

            if attempt < MAX_REQUEST_RETRIES:
                time.sleep(attempt * 2)

    return None


def fetch_review_authors(
    appid: int,
    cursor: str,
) -> tuple[list[str], str | None, int]:
    """Obtém SteamIDs dos autores de reviews públicas."""

    response = request_json(
        url=REVIEWS_URL.format(appid=appid),
        params={
            "json": 1,
            "filter": "recent",
            "language": "all",
            "review_type": "all",
            "purchase_type": "all",
            "num_per_page": REVIEWS_PER_PAGE,
            "cursor": cursor,
        },
    )

    if not response:
        return [], None, 0

    reviews = response.get("reviews", [])

    steam_ids = []

    for review in reviews:
        steam_id = (
            review
            .get("author", {})
            .get("steamid")
        )

        if steam_id:
            steam_ids.append(str(steam_id))

    next_cursor = response.get("cursor")

    return steam_ids, next_cursor, len(reviews)


def fetch_owned_games(
    steam_id: str,
) -> list[dict[str, Any]] | None:
    """Consulta a biblioteca pública de um usuário."""

    response = request_json(
        url=OWNED_GAMES_URL,
        params={
            "key": STEAM_API_KEY,
            "steamid": steam_id,
            "include_appinfo": False,
            "include_played_free_games": True,
            "format": "json",
        },
    )

    if response is None:
        return None

    player_response = response.get("response", {})

    games = player_response.get("games")

    if games is None:
        return None

    return games


def collect_valid_profiles() -> tuple[
    pd.DataFrame,
    dict[str, Any],
]:
    """Coleta bibliotecas até atingir a quantidade de perfis válidos."""

    validate_configuration()

    print("\n" + "=" * 60)
    print("Coleta experimental via Steam API")
    print("=" * 60)

    tested_steam_ids: set[str] = set()

    collected_profiles: set[str] = set()

    owned_games_records: list[dict[str, Any]] = []

    total_reviews = 0
    total_steam_ids_found = 0
    inaccessible_profiles = 0
    empty_profiles = 0

    for appid in SEED_APP_IDS:
        if (
            len(collected_profiles)
            >= TARGET_VALID_PROFILES
        ):
            break

        print(f"\nConsultando reviews do AppID {appid}")

        cursor = "*"

        while (
            len(collected_profiles)
            < TARGET_VALID_PROFILES
        ):
            steam_ids, next_cursor, review_count = (
                fetch_review_authors(
                    appid=appid,
                    cursor=cursor,
                )
            )

            if review_count == 0:
                print(
                    "Nenhuma review adicional encontrada."
                )
                break

            total_reviews += review_count
            total_steam_ids_found += len(steam_ids)

            print(
                f"Reviews processadas: {total_reviews} | "
                f"Perfis válidos: "
                f"{len(collected_profiles)}/"
                f"{TARGET_VALID_PROFILES}"
            )

            for steam_id in steam_ids:
                if (
                    len(collected_profiles)
                    >= TARGET_VALID_PROFILES
                ):
                    break

                if steam_id in tested_steam_ids:
                    continue

                tested_steam_ids.add(steam_id)

                games = fetch_owned_games(
                    steam_id=steam_id
                )

                time.sleep(REQUEST_DELAY_SECONDS)

                if games is None:
                    inaccessible_profiles += 1
                    continue

                played_games = [
                    game
                    for game in games
                    if game.get(
                        "playtime_forever",
                        0,
                    ) > 0
                ]

                if not played_games:
                    empty_profiles += 1
                    continue

                collected_profiles.add(steam_id)

                for game in played_games:
                    owned_games_records.append(
                        {
                            "steamid": steam_id,
                            "appid": game["appid"],
                            "playtime_forever": game.get(
                                "playtime_forever",
                                0,
                            ),
                        }
                    )

                print(
                    f"Perfil válido coletado: "
                    f"{len(collected_profiles)}/"
                    f"{TARGET_VALID_PROFILES} | "
                    f"Jogos jogados: "
                    f"{len(played_games)}"
                )

            if not next_cursor:
                break

            if next_cursor == cursor:
                print(
                    "Cursor repetido. "
                    "Paginação encerrada."
                )
                break

            cursor = next_cursor

            time.sleep(REQUEST_DELAY_SECONDS)

    dataframe = pd.DataFrame(
        owned_games_records
    )

    summary = {
        "target_valid_profiles": (
            TARGET_VALID_PROFILES
        ),
        "seed_app_ids": SEED_APP_IDS,
        "reviews_processed": total_reviews,
        "steam_ids_found": total_steam_ids_found,
        "unique_steam_ids_tested": len(
            tested_steam_ids
        ),
        "accessible_profiles": len(
            collected_profiles
        ),
        "inaccessible_profiles": (
            inaccessible_profiles
        ),
        "profiles_without_played_games": (
            empty_profiles
        ),
        "owned_game_records": len(dataframe),
        "unique_appids": (
            int(dataframe["appid"].nunique())
            if not dataframe.empty
            else 0
        ),
    }

    return dataframe, summary


def save_collection_results(
    dataframe: pd.DataFrame,
    summary: dict[str, Any],
) -> None:
    """Salva os dados coletados e o resumo do experimento."""

    RAW_DATA_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    METRICS_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    dataframe.to_csv(
        OWNED_GAMES_FILE,
        index=False,
    )

    COLLECTION_SUMMARY_FILE.write_text(
        json.dumps(
            summary,
            indent=4,
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )


def print_collection_summary(
    summary: dict[str, Any],
) -> None:
    """Exibe o resumo final da coleta."""

    print("\n" + "=" * 60)
    print("Resumo da coleta experimental")
    print("=" * 60)

    print(
        f"Reviews processadas: "
        f"{summary['reviews_processed']}"
    )

    print(
        f"SteamIDs encontrados: "
        f"{summary['steam_ids_found']}"
    )

    print(
        f"SteamIDs únicos testados: "
        f"{summary['unique_steam_ids_tested']}"
    )

    print(
        f"Bibliotecas acessíveis: "
        f"{summary['accessible_profiles']}"
    )

    print(
        f"Bibliotecas inacessíveis: "
        f"{summary['inaccessible_profiles']}"
    )

    print(
        f"Perfis sem jogos com playtime: "
        f"{summary['profiles_without_played_games']}"
    )

    print(
        f"Registros de jogos coletados: "
        f"{summary['owned_game_records']}"
    )

    print(
        f"AppIDs únicos coletados: "
        f"{summary['unique_appids']}"
    )

    print(
        f"\nArquivo de dados: "
        f"{OWNED_GAMES_FILE}"
    )

    print(
        f"Resumo salvo em: "
        f"{COLLECTION_SUMMARY_FILE}"
    )


def main() -> None:
    dataframe, summary = collect_valid_profiles()

    save_collection_results(
        dataframe=dataframe,
        summary=summary,
    )

    print_collection_summary(
        summary=summary,
    )


if __name__ == "__main__":
    main()