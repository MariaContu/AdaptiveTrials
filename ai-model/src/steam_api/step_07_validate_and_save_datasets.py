import json
from pathlib import Path

import numpy as np
import pandas as pd

from src.steam_api.config import (
    METRICS_DIR,
    PROCESSED_DATA_DIR,
)
from src.steam_api.step_03_build_combat_dataset import (
    build_combat_dataset,
)
from src.steam_api.step_04_build_exploration_dataset import (
    build_exploration_dataset,
)
from src.steam_api.step_05_build_puzzle_dataset import (
    build_puzzle_dataset,
)


DATASET_CONFIGS = {
    "combat": {
        "builder": build_combat_dataset,
        "subcategories": [
            "combat_shooter",
            "combat_fighting",
            "combat_action",
        ],
        "output_file": (
            PROCESSED_DATA_DIR
            / "steam_api_combat_profiles.csv"
        ),
    },
    "exploration": {
        "builder": build_exploration_dataset,
        "subcategories": [
            "exploration_rpg",
            "exploration_open_world",
            "exploration_adventure",
        ],
        "output_file": (
            PROCESSED_DATA_DIR
            / "steam_api_exploration_profiles.csv"
        ),
    },
    "puzzle": {
        "builder": build_puzzle_dataset,
        "subcategories": [
            "puzzle_narrative",
            "puzzle_spatial",
            "puzzle_logic",
        ],
        "output_file": (
            PROCESSED_DATA_DIR
            / "steam_api_puzzle_profiles.csv"
        ),
    },
}

SUMMARY_FILE = (
    METRICS_DIR
    / "granular_datasets_summary.json"
)


def get_model_features(
    subcategories: list[str],
) -> list[str]:
    """Retorna as features utilizadas no treinamento granular."""

    features: list[str] = []

    for subcategory in subcategories:
        features.append(
            f"hours_{subcategory}"
        )

    for subcategory in subcategories:
        features.append(
            f"games_{subcategory}"
        )

    features.extend(
        [
            "total_playtime",
            "num_games",
            "avg_playtime_per_game",
            "diversity",
            "entropy",
            "dominance",
            "second_max",
            "gap",
        ]
    )

    return features


def validate_dataset(
    dataset_name: str,
    dataframe: pd.DataFrame,
    subcategories: list[str],
) -> dict[str, object]:
    """Valida um dataset granular."""

    model_features = get_model_features(
        subcategories
    )

    required_columns = (
        ["steamid"]
        + subcategories
        + model_features
        + ["target"]
    )

    missing_columns = [
        column
        for column in required_columns
        if column not in dataframe.columns
    ]

    if missing_columns:
        raise ValueError(
            f"{dataset_name}: colunas ausentes: "
            + ", ".join(missing_columns)
        )

    invalid_targets = sorted(
        set(
            dataframe["target"].dropna()
        )
        - set(subcategories)
    )

    if invalid_targets:
        raise ValueError(
            f"{dataset_name}: targets inválidos: "
            + ", ".join(invalid_targets)
        )

    missing_values = int(
        dataframe[
            required_columns
        ]
        .isna()
        .sum()
        .sum()
    )

    numeric_columns = (
        subcategories
        + model_features
    )

    numeric_values = (
        dataframe[
            numeric_columns
        ]
        .to_numpy(
            dtype=float
        )
    )

    infinite_values = int(
        np.isinf(
            numeric_values
        )
        .sum()
    )

    duplicated_rows = int(
        dataframe.duplicated().sum()
    )

    duplicated_steamids = int(
        dataframe[
            "steamid"
        ]
        .duplicated()
        .sum()
    )

    negative_values = int(
        dataframe[
            model_features
        ]
        .lt(0)
        .sum()
        .sum()
    )

    proportion_sums = (
        dataframe[
            subcategories
        ]
        .sum(
            axis=1
        )
    )

    invalid_proportion_sums = int(
    (
        ~np.isclose(
            proportion_sums.to_numpy(
                dtype=float
            ),
            1.0,
        )
    ).sum()
    )

    target_distribution = (
        dataframe[
            "target"
        ]
        .value_counts()
        .reindex(
            subcategories,
            fill_value=0,
        )
    )

    print("\n" + "-" * 60)
    print(
        f"Validação do dataset: "
        f"{dataset_name}"
    )
    print("-" * 60)

    print(
        f"Colunas obrigatórias: "
        f"{len(required_columns)}"
    )

    print(
        f"Valores ausentes: "
        f"{missing_values}"
    )

    print(
        f"Valores infinitos: "
        f"{infinite_values}"
    )

    print(
        f"Linhas duplicadas: "
        f"{duplicated_rows}"
    )

    print(
        f"SteamIDs duplicados: "
        f"{duplicated_steamids}"
    )

    print(
        f"Valores negativos nas features: "
        f"{negative_values}"
    )

    print(
        "Perfis com soma das proporções "
        f"diferente de 1: "
        f"{invalid_proportion_sums}"
    )

    print(
        f"Perfis finais: "
        f"{len(dataframe)}"
    )

    print(
        f"Features disponíveis para o modelo: "
        f"{len(model_features)}"
    )

    print(
        "\nDistribuição do target:"
    )

    for subcategory, count in (
        target_distribution.items()
    ):
        percentage = (
            count
            / len(dataframe)
            * 100
            if len(dataframe) > 0
            else 0
        )

        print(
            f"{subcategory}: "
            f"{int(count)} perfis "
            f"({percentage:.2f}%)"
        )

    validation_errors = (
        missing_values
        + infinite_values
        + duplicated_rows
        + duplicated_steamids
        + negative_values
        + invalid_proportion_sums
    )

    if validation_errors > 0:
        raise ValueError(
            f"{dataset_name}: "
            "o dataset apresentou inconsistências."
        )

    print(
        "\nDataset validado com sucesso."
    )

    return {
        "dataset": dataset_name,
        "profiles": len(dataframe),
        "features": len(model_features),
        "subcategories": subcategories,
        "target_distribution": {
            subcategory: int(count)
            for subcategory, count
            in target_distribution.items()
        },
        "missing_values": missing_values,
        "infinite_values": infinite_values,
        "duplicated_rows": duplicated_rows,
        "duplicated_steamids": duplicated_steamids,
        "negative_values": negative_values,
        "invalid_proportion_sums": (
            invalid_proportion_sums
        ),
    }


def validate_and_save_datasets() -> None:
    """Constrói, valida e salva os datasets granulares."""

    print("\n" + "=" * 60)
    print(
        "Validação e persistência "
        "dos datasets granulares"
    )
    print("=" * 60)

    PROCESSED_DATA_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    METRICS_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    summaries: dict[
        str,
        dict[str, object],
    ] = {}

    for dataset_name, config in (
        DATASET_CONFIGS.items()
    ):
        builder = config["builder"]
        subcategories = config[
            "subcategories"
        ]
        output_file = config[
            "output_file"
        ]

        dataframe = builder()

        summary = validate_dataset(
            dataset_name=dataset_name,
            dataframe=dataframe,
            subcategories=subcategories,
        )

        dataframe.to_csv(
            output_file,
            index=False,
        )

        summary[
            "output_file"
        ] = str(
            Path(output_file).resolve()
        )

        summaries[
            dataset_name
        ] = summary

        print(
            "\nDataset salvo em: "
            f"{Path(output_file).resolve()}"
        )

    with open(
        SUMMARY_FILE,
        "w",
        encoding="utf-8",
    ) as summary_file:
        json.dump(
            summaries,
            summary_file,
            ensure_ascii=False,
            indent=4,
        )

    print("\n" + "=" * 60)
    print(
        "Persistência concluída"
    )
    print("=" * 60)

    print(
        "Resumo salvo em: "
        f"{Path(SUMMARY_FILE).resolve()}"
    )


def main() -> None:
    validate_and_save_datasets()


if __name__ == "__main__":
    main()