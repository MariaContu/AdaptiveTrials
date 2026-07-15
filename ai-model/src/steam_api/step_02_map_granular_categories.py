import pandas as pd

from src.steam_api.config import (
    GRANULAR_CATEGORY_TAGS,
)
from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)
from src.steam_api.step_03_map_categories import (
    get_relevant_game_tags,
    map_mission_categories,
    normalize_text,
)


def identify_granular_category(
    genres: object,
    steamspy_tags: object,
    steamspy_tags_weighted: object,
    macro_category: str,
) -> str | None:
    """
    Identifica uma subcategoria dentro da macrocategoria.

    A ordem definida no config representa a prioridade.
    A primeira subcategoria compatível é selecionada.
    """

    game_tags = get_relevant_game_tags(
        genres=genres,
        steamspy_tags=steamspy_tags,
        steamspy_tags_weighted=(
            steamspy_tags_weighted
        ),
    )

    granular_categories = (
        GRANULAR_CATEGORY_TAGS[
            macro_category
        ]
    )

    for (
        subcategory,
        category_tags,
    ) in granular_categories.items():
        normalized_category_tags = {
            normalize_text(tag)
            for tag in category_tags
        }

        if game_tags.intersection(
            normalized_category_tags
        ):
            return subcategory

    return None


def map_granular_categories(
    metadata_dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Mapeia os jogos para suas subcategorias granulares."""

    dataframe = map_mission_categories(
        metadata_dataframe
    )

    for macro_category in (
        GRANULAR_CATEGORY_TAGS
    ):
        granular_column = (
            f"{macro_category}"
            "_subcategory"
        )

        dataframe[
            granular_column
        ] = dataframe.apply(
            lambda row: (
                identify_granular_category(
                    genres=row["genres"],
                    steamspy_tags=(
                        row[
                            "steamspy_tags"
                        ]
                    ),
                    steamspy_tags_weighted=(
                        row[
                            "steamspy_tags_weighted"
                        ]
                    ),
                    macro_category=(
                        macro_category
                    ),
                )
                if bool(
                    row[macro_category]
                )
                else None
            ),
            axis=1,
        )

    return dataframe


def print_granular_summary(
    dataframe: pd.DataFrame,
) -> None:
    """Exibe a cobertura e a distribuição granular."""

    print("\n" + "=" * 60)
    print(
        "Mapeamento das categorias "
        "granulares"
    )
    print("=" * 60)

    print(
        f"AppIDs analisados: "
        f"{len(dataframe)}"
    )

    for macro_category in (
        GRANULAR_CATEGORY_TAGS
    ):
        granular_column = (
            f"{macro_category}"
            "_subcategory"
        )

        macro_games = dataframe[
            dataframe[
                macro_category
            ]
        ]

        macro_count = len(
            macro_games
        )

        categorized_count = int(
            macro_games[
                granular_column
            ]
            .notna()
            .sum()
        )

        uncategorized_count = (
            macro_count
            - categorized_count
        )

        coverage = (
            categorized_count
            / macro_count
            * 100
            if macro_count > 0
            else 0
        )

        print(
            "\n" + "-" * 60
        )

        print(
            f"Macrocategoria: "
            f"{macro_category}"
        )

        print(
            "-" * 60
        )

        print(
            f"Jogos na macrocategoria: "
            f"{macro_count}"
        )

        print(
            f"Jogos com subcategoria: "
            f"{categorized_count}"
        )

        print(
            "Jogos sem subcategoria "
            f"granular: "
            f"{uncategorized_count}"
        )

        print(
            f"Cobertura granular: "
            f"{coverage:.2f}%"
        )

        print(
            "\nDistribuição "
            "das subcategorias:"
        )

        distribution = (
            macro_games[
                granular_column
            ]
            .value_counts()
        )

        for subcategory in (
            GRANULAR_CATEGORY_TAGS[
                macro_category
            ]
        ):
            count = int(
                distribution.get(
                    subcategory,
                    0,
                )
            )

            percentage = (
                count
                / categorized_count
                * 100
                if categorized_count > 0
                else 0
            )

            print(
                f"{subcategory}: "
                f"{count} jogos "
                f"({percentage:.2f}%)"
            )


def main() -> None:
    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    categorized_dataframe = (
        map_granular_categories(
            metadata_dataframe
        )
    )

    print_granular_summary(
        categorized_dataframe
    )

    print(
        "\nMapeamento granular "
        "concluído com sucesso."
    )


if __name__ == "__main__":
    main()