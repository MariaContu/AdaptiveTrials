import json

import numpy as np
import pandas as pd

from src.steam_api.config import (
    FINAL_DATASET_FILE,
    FINAL_DATASET_SUMMARY_FILE,
    MODEL_FEATURES,
    PROCESSED_DATA_DIR,
    PROFILE_COLUMNS,
    TARGET_CLASSES,
)
from src.steam_api.step_04_aggregate_profiles import (
    aggregate_profiles,
)
from src.steam_api.step_05_generate_features_and_target import (
    generate_features_and_analyze_target,
)


def validate_dataset(
    dataframe: pd.DataFrame,
) -> dict[str, object]:
    """Valida o dataset final antes do salvamento."""

    print("\n" + "=" * 60)
    print("Validação do dataset final")
    print("=" * 60)

    required_columns = (
        ["steamid"]
        + PROFILE_COLUMNS
        + MODEL_FEATURES
        + ["target"]
    )

    missing_columns = [
        column
        for column in required_columns
        if column not in dataframe.columns
    ]

    if missing_columns:
        raise ValueError(
            "Colunas obrigatórias ausentes: "
            f"{missing_columns}"
        )

    print(
        f"Colunas obrigatórias validadas: "
        f"{len(required_columns)}"
    )

    invalid_targets = (
        set(dataframe["target"].unique())
        - set(TARGET_CLASSES)
    )

    if invalid_targets:
        raise ValueError(
            "Classes inválidas encontradas: "
            f"{invalid_targets}"
        )

    missing_values = int(
        dataframe[required_columns]
        .isna()
        .sum()
        .sum()
    )

    infinite_values = int(
        np.isinf(
            dataframe[MODEL_FEATURES]
            .to_numpy(dtype=float)
        ).sum()
    )

    duplicated_rows = int(
        dataframe.duplicated().sum()
    )

    duplicated_steamids = int(
        dataframe["steamid"]
        .duplicated()
        .sum()
    )

    negative_values = int(
        (
            dataframe[MODEL_FEATURES] < 0
        )
        .sum()
        .sum()
    )

    proportion_sums = dataframe[
        PROFILE_COLUMNS
    ].sum(axis=1)

    invalid_proportion_sums = int(
        (
            ~np.isclose(
                proportion_sums,
                1.0,
            )
        ).sum()
    )

    print(f"Valores ausentes: {missing_values}")
    print(f"Valores infinitos: {infinite_values}")
    print(f"Linhas duplicadas: {duplicated_rows}")
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

    if missing_values > 0:
        raise ValueError(
            "O dataset possui valores ausentes."
        )

    if infinite_values > 0:
        raise ValueError(
            "O dataset possui valores infinitos."
        )

    if duplicated_steamids > 0:
        raise ValueError(
            "O dataset possui SteamIDs duplicados."
        )

    if negative_values > 0:
        raise ValueError(
            "O dataset possui features negativas."
        )

    if invalid_proportion_sums > 0:
        raise ValueError(
            "Existem proporções cuja soma "
            "é diferente de 1."
        )

    target_distribution = (
        dataframe["target"]
        .value_counts()
        .to_dict()
    )

    summary = {
        "profiles": len(dataframe),
        "model_features": len(MODEL_FEATURES),
        "profile_columns": PROFILE_COLUMNS,
        "target_classes": TARGET_CLASSES,
        "target_distribution": {
            str(key): int(value)
            for key, value
            in target_distribution.items()
        },
        "missing_values": missing_values,
        "infinite_values": infinite_values,
        "duplicated_rows": duplicated_rows,
        "duplicated_steamids": (
            duplicated_steamids
        ),
        "negative_feature_values": (
            negative_values
        ),
        "invalid_proportion_sums": (
            invalid_proportion_sums
        ),
    }

    print(
        f"\nPerfis finais: "
        f"{len(dataframe)}"
    )

    print(
        f"Features disponíveis para o modelo: "
        f"{len(MODEL_FEATURES)}"
    )

    print("\nDataset final validado com sucesso.")

    return summary


def save_dataset(
    dataframe: pd.DataFrame,
    summary: dict[str, object],
) -> None:
    """Salva o dataset final e seu resumo."""

    PROCESSED_DATA_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    FINAL_DATASET_SUMMARY_FILE.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    columns_to_save = (
        ["steamid"]
        + PROFILE_COLUMNS
        + MODEL_FEATURES
        + ["target"]
    )

    dataframe[
        columns_to_save
    ].to_csv(
        FINAL_DATASET_FILE,
        index=False,
    )

    FINAL_DATASET_SUMMARY_FILE.write_text(
        json.dumps(
            summary,
            indent=4,
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )

    print("\n" + "=" * 60)
    print("Persistência do dataset final")
    print("=" * 60)

    print(
        f"Dataset salvo em: "
        f"{FINAL_DATASET_FILE}"
    )

    print(
        f"Resumo salvo em: "
        f"{FINAL_DATASET_SUMMARY_FILE}"
    )

    print(
        f"Registros salvos: "
        f"{len(dataframe)}"
    )


def main() -> None:
    profiles_dataframe = aggregate_profiles()

    final_dataframe = (
        generate_features_and_analyze_target(
            profiles_dataframe
        )
    )

    summary = validate_dataset(
        final_dataframe
    )

    save_dataset(
        dataframe=final_dataframe,
        summary=summary,
    )


if __name__ == "__main__":
    main()