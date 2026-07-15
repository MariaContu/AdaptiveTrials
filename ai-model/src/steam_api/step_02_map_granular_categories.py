import pandas as pd

from src.steam_api.config import (
    GRANULAR_CATEGORY_TAGS,
)
from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)
from src.steam_api.step_03_map_categories import (
    identify_mission_categories,
    split_tags,
)


def identify_granular_category(
    genres: object,
    steamspy_tags: object,
    macro_category: str,
) -> str | None:
    """Identifica a subcategoria prioritária de uma macrocategoria."""

    game_tags = split_tags(genres)
    game_tags.update(
        split_tags(steamspy_tags)
    )

    subcategories = GRANULAR_CATEGORY_TAGS[
        macro_category
    ]

    for subcategory, category_tags in (
        subcategories.items()
    ):
        normalized_tags = {
            tag.lower()
            for tag in category_tags
        }

        if game_tags.intersection(
            normalized_tags
        ):
            return subcategory

    return None


def map_granular_categories(
    metadata_dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Mapeia cada jogo para uma subcategoria por macrocategoria."""

    dataframe = metadata_dataframe.copy()

    print("\n" + "=" * 60)
    print("Mapeamento das categorias granulares")
    print("=" * 60)

    dataframe["mission_categories"] = (
        dataframe.apply(
            lambda row: identify_mission_categories(
                genres=row["genres"],
                steamspy_tags=row["steamspy_tags"],
            ),
            axis=1,
        )
    )

    print(
        f"AppIDs analisados: "
        f"{len(dataframe)}"
    )

    for macro_category, subcategories in (
        GRANULAR_CATEGORY_TAGS.items()
    ):
        granular_column = (
            f"{macro_category}_subcategory"
        )

        dataframe[granular_column] = (
            dataframe.apply(
                lambda row: (
                    identify_granular_category(
                        genres=row["genres"],
                        steamspy_tags=row[
                            "steamspy_tags"
                        ],
                        macro_category=macro_category,
                    )
                    if macro_category
                    in row["mission_categories"]
                    else None
                ),
                axis=1,
            )
        )

        macro_games = dataframe[
            dataframe["mission_categories"]
            .apply(
                lambda categories: (
                    macro_category in categories
                )
            )
        ]

        granular_games = macro_games[
            macro_games[granular_column]
            .notna()
        ]

        games_without_subcategory = (
            macro_games[
                macro_games[granular_column]
                .isna()
            ]
        )

        coverage = (
            len(granular_games)
            / len(macro_games)
            * 100
            if len(macro_games) > 0
            else 0
        )

        print("\n" + "-" * 60)
        print(
            f"Macrocategoria: "
            f"{macro_category}"
        )
        print("-" * 60)

        print(
            f"Jogos na macrocategoria: "
            f"{len(macro_games)}"
        )

        print(
            f"Jogos com subcategoria: "
            f"{len(granular_games)}"
        )

        print(
            "Jogos sem subcategoria granular: "
            f"{len(games_without_subcategory)}"
        )

        print(
            f"Cobertura granular: "
            f"{coverage:.2f}%"
        )

        print(
            "\nDistribuição das subcategorias:"
        )

        distribution = (
            granular_games[granular_column]
            .value_counts()
        )

        for subcategory in subcategories:
            subcategory_count = int(
                distribution.get(
                    subcategory,
                    0,
                )
            )

            percentage = (
                subcategory_count
                / len(granular_games)
                * 100
                if len(granular_games) > 0
                else 0
            )

            print(
                f"{subcategory}: "
                f"{subcategory_count} jogos "
                f"({percentage:.2f}%)"
            )

        if not games_without_subcategory.empty:
            print(
                "\nExemplos sem subcategoria granular:"
            )

            for _, row in (
                games_without_subcategory
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
        "\nMapeamento granular "
        "concluído com sucesso."
    )

    return dataframe


def main() -> None:
    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    map_granular_categories(
        metadata_dataframe
    )


if __name__ == "__main__":
    main()