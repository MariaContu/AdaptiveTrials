import numpy as np
import pandas as pd

from src.steam_api.step_04_aggregate_profiles import (
    PROFILE_COLUMNS,
    aggregate_profiles,
)


def calculate_entropy(
    proportions: np.ndarray,
) -> float:
    """Calcula a entropia das proporções do perfil."""

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


def identify_tied_profiles(
    dataframe: pd.DataFrame,
) -> pd.Series:
    """Identifica empates na maior proporção do perfil."""

    proportions = dataframe[
        PROFILE_COLUMNS
    ].to_numpy(dtype=float)

    maximum_values = proportions.max(axis=1)

    maximum_counts = np.isclose(
        proportions,
        maximum_values[:, None],
    ).sum(axis=1)

    return pd.Series(
        maximum_counts > 1,
        index=dataframe.index,
    )


def generate_features_and_analyze_target(
    profiles_dataframe: pd.DataFrame,
) -> pd.DataFrame:
    """Gera features estatísticas e analisa o target."""

    dataframe = profiles_dataframe.copy()

    print("\n" + "=" * 60)
    print("Geração das features e análise do target")
    print("=" * 60)

    proportions = dataframe[
        PROFILE_COLUMNS
    ]

    dataframe["diversity"] = (
        proportions.gt(0).sum(axis=1)
    )

    dataframe["entropy"] = proportions.apply(
        lambda row: calculate_entropy(
            row.to_numpy(dtype=float)
        ),
        axis=1,
    )

    sorted_proportions = np.sort(
        proportions.to_numpy(dtype=float),
        axis=1,
    )

    dataframe["dominance"] = (
        sorted_proportions[:, -1]
    )

    dataframe["second_max"] = (
        sorted_proportions[:, -2]
    )

    dataframe["gap"] = (
        dataframe["dominance"]
        - dataframe["second_max"]
    )

    feature_columns = [
        "diversity",
        "entropy",
        "dominance",
        "second_max",
        "gap",
    ]

    print(f"Perfis recebidos: {len(dataframe)}")

    print("\nResumo das features:")

    print(
        dataframe[
            feature_columns
        ]
        .describe()
        .round(4)
        .to_string()
    )

    tied_profiles = identify_tied_profiles(
        dataframe
    )

    tied_count = int(
        tied_profiles.sum()
    )

    tied_percentage = (
        tied_count
        / len(dataframe)
        * 100
    )

    print(
        "\nPerfis com empate na maior proporção: "
        f"{tied_count} "
        f"({tied_percentage:.2f}%)"
    )

    if tied_count > 0:
        print("\nExemplos de perfis empatados:")

        print(
            dataframe.loc[
                tied_profiles,
                PROFILE_COLUMNS,
            ]
            .head(10)
            .round(4)
            .to_string(index=False)
        )

    valid_profiles = dataframe[
        ~tied_profiles
    ].copy()

    valid_profiles["target"] = (
        valid_profiles[
            PROFILE_COLUMNS
        ]
        .idxmax(axis=1)
    )

    print("\nDistribuição do target:")

    target_distribution = (
        valid_profiles["target"]
        .value_counts()
    )

    for category in PROFILE_COLUMNS:
        count = int(
            target_distribution.get(
                category,
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
            f"{category}: "
            f"{count} perfis "
            f"({percentage:.2f}%)"
        )

    print(
        "\nPerfis mantidos após tratamento "
        f"de empates: {len(valid_profiles)}"
    )

    return valid_profiles


def main() -> None:
    profiles_dataframe = aggregate_profiles()

    generate_features_and_analyze_target(
        profiles_dataframe
    )


if __name__ == "__main__":
    main()