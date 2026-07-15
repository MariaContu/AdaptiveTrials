import pandas as pd

from src.steam_api.config import (
    OWNED_GAMES_FILE,
    PROFILE_COLUMNS,
)
from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)
from src.steam_api.step_03_map_categories import (
    map_mission_categories,
)


def distribute_category_values(
    dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Distribui o playtime entre as categorias identificadas."""

    distributed_dataframe = dataframe.copy()

    for category in PROFILE_COLUMNS:
        belongs_to_category = (
            distributed_dataframe["mission_categories"]
            .apply(
                lambda categories: (
                    category in categories
                )
            )
        )

        distributed_dataframe[
            f"hours_{category}"
        ] = 0.0

        distributed_dataframe.loc[
            belongs_to_category,
            f"hours_{category}",
        ] = (
            distributed_dataframe.loc[
                belongs_to_category,
                "playtime_forever",
            ]
            / 60
            / distributed_dataframe.loc[
                belongs_to_category,
                "category_count",
            ]
        )

        distributed_dataframe[
            f"games_{category}"
        ] = 0.0

        distributed_dataframe.loc[
            belongs_to_category,
            f"games_{category}",
        ] = (
            1
            / distributed_dataframe.loc[
                belongs_to_category,
                "category_count",
            ]
        )

    return distributed_dataframe


def aggregate_profiles() -> pd.DataFrame:
    """Agrega as bibliotecas coletadas em perfis por usuário."""

    print("\n" + "=" * 60)
    print("Agregação dos perfis coletados pela Steam API")
    print("=" * 60)

    owned_games_dataframe = pd.read_csv(
        OWNED_GAMES_FILE
    )

    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    categorized_metadata = map_mission_categories(
        metadata_dataframe
    )

    categorized_metadata = categorized_metadata[
        categorized_metadata["category_count"] > 0
    ].copy()

    merged_dataframe = owned_games_dataframe.merge(
        categorized_metadata[
            [
                "appid",
                "mission_categories",
                "category_count",
            ]
        ],
        on="appid",
        how="inner",
    )

    print(
        f"Registros de jogos recebidos: "
        f"{len(owned_games_dataframe)}"
    )

    print(
        f"Registros com categoria válida: "
        f"{len(merged_dataframe)}"
    )

    print(
        f"Perfis antes da categorização: "
        f"{owned_games_dataframe['steamid'].nunique()}"
    )

    print(
        f"Perfis com jogos categorizados: "
        f"{merged_dataframe['steamid'].nunique()}"
    )

    distributed_dataframe = (
        distribute_category_values(
            merged_dataframe
        )
    )

    hours_columns = [
        f"hours_{category}"
        for category in PROFILE_COLUMNS
    ]

    games_columns = [
        f"games_{category}"
        for category in PROFILE_COLUMNS
    ]

    aggregation_rules = {
        column: "sum"
        for column in (
            hours_columns
            + games_columns
        )
    }

    profiles_dataframe = (
        distributed_dataframe
        .groupby(
            "steamid",
            as_index=False,
        )
        .agg(aggregation_rules)
    )

    profiles_dataframe["total_playtime"] = (
        profiles_dataframe[
            hours_columns
        ].sum(axis=1)
    )

    profiles_dataframe["num_games"] = (
        profiles_dataframe[
            games_columns
        ].sum(axis=1)
    )

    profiles_dataframe[
        "avg_playtime_per_game"
    ] = (
        profiles_dataframe["total_playtime"]
        / profiles_dataframe["num_games"]
    )

    total_hours = profiles_dataframe[
        hours_columns
    ].sum(axis=1)

    for category in PROFILE_COLUMNS:
        profiles_dataframe[category] = (
            profiles_dataframe[
                f"hours_{category}"
            ]
            / total_hours
        )

    profiles_dataframe[
        "dominant_category"
    ] = (
        profiles_dataframe[
            PROFILE_COLUMNS
        ]
        .idxmax(axis=1)
    )

    print("\nHoras totais por categoria:")

    for category in PROFILE_COLUMNS:
        print(
            f"{category}: "
            f"{profiles_dataframe[f'hours_{category}'].sum():.2f}"
        )

    print(
        "\nDistribuição das categorias dominantes:"
    )

    dominant_distribution = (
        profiles_dataframe[
            "dominant_category"
        ]
        .value_counts()
    )

    for category in PROFILE_COLUMNS:
        category_count = int(
            dominant_distribution.get(
                category,
                0,
            )
        )

        percentage = (
            category_count
            / len(profiles_dataframe)
            * 100
        )

        print(
            f"{category}: "
            f"{category_count} perfis "
            f"({percentage:.2f}%)"
        )

    print("\nProporção média por categoria:")

    for category in PROFILE_COLUMNS:
        print(
            f"{category}: "
            f"média="
            f"{profiles_dataframe[category].mean() * 100:.2f}% | "
            f"mediana="
            f"{profiles_dataframe[category].median() * 100:.2f}%"
        )

    print(
        f"\nPerfis gerados: "
        f"{len(profiles_dataframe)}"
    )

    return profiles_dataframe


def main() -> None:
    aggregate_profiles()


if __name__ == "__main__":
    main()