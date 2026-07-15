import json

import pandas as pd

from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)

GAMES_TO_ANALYZE = [
    "AdVenture Capitalist",
    "Batman: Arkham Knight",
    "Escape the Backrooms",
    "Auto Clicker",
    "Disco Elysium - The Final Cut",
    "Persona 4 Golden",
]
def parse_weighted_tags(value: object) -> dict[str, int]:
    """Converte o JSON das tags ponderadas em dicionário."""

    if pd.isna(value):
        return {}

    try:
        parsed_value = json.loads(str(value))
    except json.JSONDecodeError:
        return {}

    if not isinstance(parsed_value, dict):
        return {}

    return {
        str(tag): int(weight)
        for tag, weight in parsed_value.items()
    }


def analyze_tag_weights() -> None:
    """Exibe os pesos das tags de jogos selecionados."""

    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    required_column = "steamspy_tags_weighted"

    if required_column not in metadata_dataframe.columns:
        raise ValueError(
            "A coluna steamspy_tags_weighted não foi encontrada. "
            "Execute novamente a coleta de metadados."
        )

    print("\n" + "=" * 60)
    print("Análise dos pesos das tags")
    print("=" * 60)

    for game_name in GAMES_TO_ANALYZE:
        matching_games = metadata_dataframe[
            metadata_dataframe["name"]
            .astype(str)
            .str.casefold()
            .eq(game_name.casefold())
        ]

        print(f"\n{game_name}")
        print("-" * 60)

        if matching_games.empty:
            print("Jogo não encontrado.")
            continue

        game = matching_games.iloc[0]

        print(f"AppID: {game['appid']}")
        print(f"Gêneros: {game['genres']}")

        weighted_tags = parse_weighted_tags(
            game[required_column]
        )

        if not weighted_tags:
            print("Nenhuma tag ponderada disponível.")
            continue

        sorted_tags = sorted(
            weighted_tags.items(),
            key=lambda item: item[1],
            reverse=True,
        )

        for tag, weight in sorted_tags[:20]:
            print(f"{tag}: {weight}")


def main() -> None:
    analyze_tag_weights()


if __name__ == "__main__":
    main()