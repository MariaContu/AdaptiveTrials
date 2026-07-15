import re

import pandas as pd

from src.steam_api.config import (
    MISSION_CATEGORY_TAGS,
)

from collections import Counter

from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)



def print_unmapped_tags_summary(
    uncategorized_games: pd.DataFrame,
) -> None:
    """Exibe as tags e gêneros mais comuns entre jogos não categorizados."""

    unmapped_values: Counter[str] = Counter()

    for _, row in uncategorized_games.iterrows():
        game_tags = split_tags(
            row["genres"]
        )

        game_tags.update(
            split_tags(
                row["steamspy_tags"]
            )
        )

        unmapped_values.update(game_tags)

    print(
        "\nTags e gêneros mais frequentes "
        "entre jogos sem categoria:"
    )

    for tag, count in unmapped_values.most_common(30):
        print(
            f"{tag}: {count}"
        )

def split_tags(value: object) -> set[str]:
    """Separa gêneros e tags em valores normalizados."""

    if pd.isna(value):
        return set()

    return {
        tag.strip().lower()
        for tag in re.split(r"[;,]", str(value))
        if tag.strip()
    }

def identify_mission_categories(
    genres: object,
    steamspy_tags: object,
) -> list[str]:
    """Identifica as categorias de missão associadas a um jogo."""

    game_tags = split_tags(genres)
    game_tags.update(
        split_tags(steamspy_tags)
    )

    identified_categories = []

    for category, category_tags in (
        MISSION_CATEGORY_TAGS.items()
    ):
        normalized_category_tags = {
            tag.lower()
            for tag in category_tags
        }

        if game_tags.intersection(
            normalized_category_tags
        ):
            identified_categories.append(
                category
            )

    return identified_categories


def map_app_categories(
    metadata_dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Mapeia os AppIDs para categorias de missão."""

    categorized_dataframe = (
        metadata_dataframe.copy()
    )

    print("\n" + "=" * 60)
    print("Mapeamento das categorias dos AppIDs")
    print("=" * 60)

    categorized_dataframe[
        "mission_categories"
    ] = categorized_dataframe.apply(
        lambda row: identify_mission_categories(
            genres=row["genres"],
            steamspy_tags=row["steamspy_tags"],
        ),
        axis=1,
    )

    categorized_dataframe[
        "category_count"
    ] = categorized_dataframe[
        "mission_categories"
    ].apply(len)

    total_games = len(
        categorized_dataframe
    )

    categorized_games = (
        categorized_dataframe[
            categorized_dataframe[
                "category_count"
            ] > 0
        ]
    )

    uncategorized_games = (
        categorized_dataframe[
            categorized_dataframe[
                "category_count"
            ] == 0
        ]
    )

    print(
        f"AppIDs analisados: "
        f"{total_games}"
    )

    print(
        f"Jogos categorizados: "
        f"{len(categorized_games)}"
    )

    print(
        f"Jogos sem categoria: "
        f"{len(uncategorized_games)}"
    )

    coverage = (
        len(categorized_games)
        / total_games
        * 100
        if total_games > 0
        else 0
    )

    print_unmapped_tags_summary(
        uncategorized_games
    )

    print(
        f"Cobertura da categorização: "
        f"{coverage:.2f}%"
    )

    print(
        "\nQuantidade de categorias por jogo:"
    )

    print(
        categorized_dataframe[
            "category_count"
        ]
        .value_counts()
        .sort_index()
        .to_string()
    )

    print(
        "\nJogos associados a cada categoria:"
    )

    for category in MISSION_CATEGORY_TAGS:
        category_count = int(
            categorized_dataframe[
                "mission_categories"
            ]
            .apply(
                lambda categories: (
                    category in categories
                )
            )
            .sum()
        )

        print(
            f"{category}: "
            f"{category_count}"
        )

    if not uncategorized_games.empty:
        print(
            "\nExemplos de jogos sem categoria:"
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

    print(
        "\nMapeamento de categorias "
        "concluído com sucesso."
    )

    return categorized_dataframe


def main() -> None:
    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    map_app_categories(
        metadata_dataframe
    )


if __name__ == "__main__":
    main()