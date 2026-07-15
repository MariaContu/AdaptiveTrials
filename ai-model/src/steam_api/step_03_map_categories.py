import json
import re

import pandas as pd

from src.steam_api.config import (
    MISSION_CATEGORY_TAGS,
)
from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)


TOP_RELEVANT_TAGS = 10


def normalize_text(value: object) -> str:
    """Normaliza um texto para comparação entre tags."""

    return (
        str(value)
        .strip()
        .casefold()
    )


def split_tags(value: object) -> set[str]:
    """Separa gêneros ou tags delimitados por vírgula ou ponto e vírgula."""

    if pd.isna(value):
        return set()

    values = re.split(
        r"[;,]",
        str(value),
    )

    return {
        normalize_text(item)
        for item in values
        if str(item).strip()
    }


def parse_weighted_tags(
    value: object,
) -> dict[str, int]:
    """Converte as tags ponderadas armazenadas em JSON."""

    if pd.isna(value):
        return {}

    try:
        parsed_value = json.loads(
            str(value)
        )
    except (
        json.JSONDecodeError,
        TypeError,
        ValueError,
    ):
        return {}

    if not isinstance(
        parsed_value,
        dict,
    ):
        return {}

    weighted_tags: dict[str, int] = {}

    for tag, weight in parsed_value.items():
        try:
            weighted_tags[
                normalize_text(tag)
            ] = int(weight)
        except (
            TypeError,
            ValueError,
        ):
            continue

    return weighted_tags


def get_relevant_game_tags(
    genres: object,
    steamspy_tags: object,
    steamspy_tags_weighted: object,
    top_n: int = TOP_RELEVANT_TAGS,
) -> set[str]:
    """
    Retorna as tags ponderadas mais relevantes.

    Quando as tags ponderadas não estão disponíveis,
    utiliza gêneros e tags simples como fallback.
    """

    weighted_tags = parse_weighted_tags(
        steamspy_tags_weighted
    )

    if weighted_tags:
        ordered_tags = sorted(
            weighted_tags.items(),
            key=lambda item: item[1],
            reverse=True,
        )

        return {
            tag
            for tag, _ in ordered_tags[
                :top_n
            ]
        }

    fallback_tags = split_tags(
        genres
    )

    fallback_tags.update(
        split_tags(
            steamspy_tags
        )
    )

    return fallback_tags


def identify_mission_categories(
    genres: object,
    steamspy_tags: object,
    steamspy_tags_weighted: object,
) -> list[str]:
    """Identifica as macrocategorias relacionadas ao jogo."""

    game_tags = get_relevant_game_tags(
        genres=genres,
        steamspy_tags=steamspy_tags,
        steamspy_tags_weighted=(
            steamspy_tags_weighted
        ),
    )

    identified_categories: list[str] = []

    for (
        category,
        category_tags,
    ) in MISSION_CATEGORY_TAGS.items():
        normalized_category_tags = {
            normalize_text(tag)
            for tag in category_tags
        }

        if game_tags.intersection(
            normalized_category_tags
        ):
            identified_categories.append(
                category
            )

    return identified_categories


def map_mission_categories(
    metadata_dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Mapeia os jogos para combate, exploração e puzzle."""

    dataframe = (
        metadata_dataframe.copy()
    )

    required_columns = {
        "appid",
        "name",
        "genres",
        "steamspy_tags",
        "steamspy_tags_weighted",
    }

    missing_columns = (
        required_columns
        - set(dataframe.columns)
    )

    if missing_columns:
        raise ValueError(
            "Colunas obrigatórias ausentes: "
            + ", ".join(
                sorted(missing_columns)
            )
        )

    dataframe[
        "mission_categories"
    ] = dataframe.apply(
        lambda row: (
            identify_mission_categories(
                genres=row["genres"],
                steamspy_tags=(
                    row["steamspy_tags"]
                ),
                steamspy_tags_weighted=(
                    row[
                        "steamspy_tags_weighted"
                    ]
                ),
            )
        ),
        axis=1,
    )

    for category in MISSION_CATEGORY_TAGS:
        dataframe[category] = (
            dataframe[
                "mission_categories"
            ]
            .apply(
                lambda categories: (
                    category in categories
                )
            )
        )

    dataframe[
        "category_count"
    ] = dataframe[
        "mission_categories"
    ].apply(len)

    return dataframe


def print_category_summary(
    dataframe: pd.DataFrame,
) -> None:
    """Exibe o resumo do mapeamento das macrocategorias."""

    analyzed_count = len(
        dataframe
    )

    categorized_count = int(
        dataframe[
            "category_count"
        ]
        .gt(0)
        .sum()
    )

    uncategorized_count = (
        analyzed_count
        - categorized_count
    )

    coverage = (
        categorized_count
        / analyzed_count
        * 100
        if analyzed_count > 0
        else 0
    )

    print("\n" + "=" * 60)
    print(
        "Mapeamento das categorias "
        "dos AppIDs"
    )
    print("=" * 60)

    print(
        f"AppIDs analisados: "
        f"{analyzed_count}"
    )

    print(
        f"Jogos categorizados: "
        f"{categorized_count}"
    )

    print(
        f"Jogos sem categoria: "
        f"{uncategorized_count}"
    )

    print(
        f"\nCobertura da categorização: "
        f"{coverage:.2f}%"
    )

    print(
        "\nQuantidade de categorias "
        "por jogo:"
    )

    print(
        dataframe[
            "category_count"
        ]
        .value_counts()
        .sort_index()
        .to_string()
    )

    print(
        "\nJogos associados "
        "a cada categoria:"
    )

    for category in MISSION_CATEGORY_TAGS:
        category_count = int(
            dataframe[
                category
            ].sum()
        )

        print(
            f"{category}: "
            f"{category_count}"
        )

    uncategorized_games = (
        dataframe[
            dataframe[
                "category_count"
            ]
            .eq(0)
        ]
    )

    print(
        "\nExemplos de jogos "
        "sem categoria:"
    )

    for _, row in (
        uncategorized_games
        .head(10)
        .iterrows()
    ):
        print(
            f"- {row['appid']} | "
            f"{row['name']} | "
            f"genres={row['genres']} | "
            f"tags={row['steamspy_tags']}"
        )


def main() -> None:
    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    categorized_dataframe = (
        map_mission_categories(
            metadata_dataframe
        )
    )

    print_category_summary(
        categorized_dataframe
    )

    print(
        "\nMapeamento de categorias "
        "concluído com sucesso."
    )


if __name__ == "__main__":
    main()