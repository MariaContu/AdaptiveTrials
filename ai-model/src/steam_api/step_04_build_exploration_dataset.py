import numpy as np
import pandas as pd

from src.steam_api.config import (
    GRANULAR_CATEGORY_TAGS,
    OWNED_GAMES_FILE,
)
from src.steam_api.step_02_collect_app_metadata import (
    APP_METADATA_FILE,
)
from src.steam_api.step_02_map_granular_categories import (
    map_granular_categories,
)


MACRO_CATEGORY = "exploration"

SUBCATEGORIES = list(
    GRANULAR_CATEGORY_TAGS[
        MACRO_CATEGORY
    ].keys()
)


def calculate_entropy(
    proportions: np.ndarray,
) -> float:
    """Calcula a entropia das proporções granulares."""

    valid_proportions = proportions[
        proportions > 0
    ]

    if len(valid_proportions) == 0:
        return 0.0

    return float(
        -np.sum(
            valid_proportions
            * np.log2(valid_proportions)
        )
    )


def print_exploration_analysis(
    merged_dataframe: pd.DataFrame,
    profiles_dataframe: pd.DataFrame,
) -> None:
    """Exibe informações sobre as subcategorias de exploração."""

    granular_column = (
        "exploration_subcategory"
    )

    print("\n" + "=" * 60)
    print(
        "Análise das subcategorias "
        "de exploração"
    )
    print("=" * 60)

    print(
        "\nRegistros por subcategoria:"
    )

    print(
        merged_dataframe[
            granular_column
        ]
        .value_counts()
        .to_string()
    )

    print(
        "\nHoras totais por subcategoria:"
    )

    hours_distribution = (
        merged_dataframe
        .groupby(
            granular_column
        )["hours"]
        .sum()
        .sort_values(
            ascending=False
        )
    )

    total_hours = (
        hours_distribution.sum()
    )

    for subcategory, hours in (
        hours_distribution.items()
    ):
        percentage = (
            hours / total_hours * 100
            if total_hours > 0
            else 0
        )

        print(
            f"{subcategory}: "
            f"{hours:.2f} horas "
            f"({percentage:.2f}%)"
        )

    print(
        "\nMediana de horas por perfil:"
    )

    for subcategory in SUBCATEGORIES:
        column = (
            f"hours_{subcategory}"
        )

        print(
            f"{subcategory}: "
            f"{profiles_dataframe[column].median():.2f}"
        )

    print(
        "\nPerfis sem participação "
        "na subcategoria:"
    )

    for subcategory in SUBCATEGORIES:
        column = (
            f"hours_{subcategory}"
        )

        count = int(
            profiles_dataframe[
                column
            ]
            .eq(0)
            .sum()
        )

        percentage = (
            count
            / len(profiles_dataframe)
            * 100
        )

        print(
            f"{subcategory}: "
            f"{count} perfis "
            f"({percentage:.2f}%)"
        )

    print(
        "\nTop jogos por playtime "
        "em cada subcategoria:"
    )

    for subcategory in SUBCATEGORIES:
        print(
            f"\n{subcategory}:"
        )

        top_games = (
            merged_dataframe[
                merged_dataframe[
                    granular_column
                ]
                == subcategory
            ]
            .groupby(
                [
                    "appid",
                    "name",
                ],
                as_index=False,
            )["hours"]
            .sum()
            .sort_values(
                "hours",
                ascending=False,
            )
            .head(10)
        )

        print(
            top_games.to_string(
                index=False
            )
        )


def build_exploration_dataset() -> pd.DataFrame:
    """Constrói o dataset granular de exploração."""

    print("\n" + "=" * 60)
    print(
        "Construção do dataset granular "
        "de exploração"
    )
    print("=" * 60)

    owned_games_dataframe = pd.read_csv(
        OWNED_GAMES_FILE
    )

    metadata_dataframe = pd.read_csv(
        APP_METADATA_FILE
    )

    categorized_metadata = (
        map_granular_categories(
            metadata_dataframe
        )
    )

    granular_column = (
        f"{MACRO_CATEGORY}_subcategory"
    )

    exploration_metadata = (
        categorized_metadata[
            categorized_metadata[
                granular_column
            ].notna()
        ][
            [
                "appid",
                "name",
                granular_column,
            ]
        ]
        .copy()
    )

    merged_dataframe = (
        owned_games_dataframe.merge(
            exploration_metadata,
            on="appid",
            how="inner",
        )
    )

    merged_dataframe[
        "hours"
    ] = (
        merged_dataframe[
            "playtime_forever"
        ]
        / 60
    )

    print(
        "Registros de jogos de exploração: "
        f"{len(merged_dataframe)}"
    )

    print(
        "Perfis com jogos de exploração: "
        f"{merged_dataframe['steamid'].nunique()}"
    )

    hours_by_subcategory = (
        merged_dataframe
        .pivot_table(
            index="steamid",
            columns=granular_column,
            values="hours",
            aggfunc="sum",
            fill_value=0,
        )
        .reindex(
            columns=SUBCATEGORIES,
            fill_value=0,
        )
    )

    games_by_subcategory = (
        merged_dataframe
        .pivot_table(
            index="steamid",
            columns=granular_column,
            values="appid",
            aggfunc="count",
            fill_value=0,
        )
        .reindex(
            columns=SUBCATEGORIES,
            fill_value=0,
        )
    )

    profiles_dataframe = pd.DataFrame(
        index=hours_by_subcategory.index
    )

    for subcategory in SUBCATEGORIES:
        profiles_dataframe[
            f"hours_{subcategory}"
        ] = (
            hours_by_subcategory[
                subcategory
            ]
        )

        profiles_dataframe[
            f"games_{subcategory}"
        ] = (
            games_by_subcategory[
                subcategory
            ]
        )

    profiles_dataframe[
        "total_playtime"
    ] = (
        hours_by_subcategory.sum(
            axis=1
        )
    )

    profiles_dataframe[
        "num_games"
    ] = (
        games_by_subcategory.sum(
            axis=1
        )
    )

    profiles_dataframe[
        "avg_playtime_per_game"
    ] = (
        profiles_dataframe[
            "total_playtime"
        ]
        / profiles_dataframe[
            "num_games"
        ]
    )

    total_hours = (
        hours_by_subcategory.sum(
            axis=1
        )
    )

    for subcategory in SUBCATEGORIES:
        profiles_dataframe[
            subcategory
        ] = (
            hours_by_subcategory[
                subcategory
            ]
            / total_hours
        )

    proportions = (
        profiles_dataframe[
            SUBCATEGORIES
        ]
    )

    profiles_dataframe[
        "diversity"
    ] = (
        proportions.gt(0).sum(
            axis=1
        )
    )

    profiles_dataframe[
        "entropy"
    ] = (
        proportions.apply(
            lambda row: calculate_entropy(
                row.to_numpy(
                    dtype=float
                )
            ),
            axis=1,
        )
    )

    sorted_proportions = np.sort(
        proportions.to_numpy(
            dtype=float
        ),
        axis=1,
    )

    profiles_dataframe[
        "dominance"
    ] = (
        sorted_proportions[:, -1]
    )

    profiles_dataframe[
        "second_max"
    ] = (
        sorted_proportions[:, -2]
    )

    profiles_dataframe[
        "gap"
    ] = (
        profiles_dataframe[
            "dominance"
        ]
        - profiles_dataframe[
            "second_max"
        ]
    )

    print_exploration_analysis(
        merged_dataframe=merged_dataframe,
        profiles_dataframe=profiles_dataframe,
    )

    maximum_values = (
        proportions.max(
            axis=1
        )
    )

    tied_profiles = (
        np.isclose(
            proportions.to_numpy(
                dtype=float
            ),
            maximum_values.to_numpy()[
                :, None
            ],
        )
        .sum(
            axis=1
        )
        > 1
    )

    tied_count = int(
        tied_profiles.sum()
    )

    tied_percentage = (
        tied_count
        / len(profiles_dataframe)
        * 100
    )

    print(
        "\nPerfis com empate "
        "na maior proporção: "
        f"{tied_count} "
        f"({tied_percentage:.2f}%)"
    )

    valid_profiles = (
        profiles_dataframe[
            ~tied_profiles
        ]
        .copy()
    )

    valid_profiles[
        "target"
    ] = (
        valid_profiles[
            SUBCATEGORIES
        ]
        .idxmax(
            axis=1
        )
    )

    print(
        "\nDistribuição do target:"
    )

    distribution = (
        valid_profiles[
            "target"
        ]
        .value_counts()
    )

    for subcategory in SUBCATEGORIES:
        count = int(
            distribution.get(
                subcategory,
                0,
            )
        )

        percentage = (
            count
            / len(valid_profiles)
            * 100
            if len(valid_profiles) > 0
            else 0
        )

        print(
            f"{subcategory}: "
            f"{count} perfis "
            f"({percentage:.2f}%)"
        )

    print(
        f"\nPerfis válidos: "
        f"{len(valid_profiles)}"
    )

    return (
        valid_profiles
        .reset_index()
    )


def main() -> None:
    build_exploration_dataset()


if __name__ == "__main__":
    main()