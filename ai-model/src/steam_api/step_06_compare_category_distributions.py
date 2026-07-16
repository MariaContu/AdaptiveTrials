from pathlib import Path

import pandas as pd

from src.steam_api.config import (
    FINAL_DATASET_FILE,
    METRICS_DIR,
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


COMPARISON_FILE = (
    METRICS_DIR
    / "category_distribution_comparison.csv"
)


def summarize_dataset(
    dataset_name: str,
    dataframe: pd.DataFrame,
) -> list[dict[str, object]]:
    """Gera o resumo da distribuição do target."""

    if "target" not in dataframe.columns:
        raise ValueError(
            f"O dataset {dataset_name} não possui a coluna target."
        )

    total_profiles = len(dataframe)

    distribution = (
        dataframe["target"]
        .value_counts()
    )

    records: list[dict[str, object]] = []

    for target_class, count in distribution.items():
        percentage = (
            count / total_profiles * 100
            if total_profiles > 0
            else 0
        )

        records.append(
            {
                "dataset": dataset_name,
                "target_class": target_class,
                "profiles": int(count),
                "percentage": round(
                    percentage,
                    2,
                ),
                "total_profiles": total_profiles,
            }
        )

    return records


def print_dataset_summary(
    dataset_name: str,
    dataframe: pd.DataFrame,
) -> None:
    """Exibe o resumo de um dataset."""

    distribution = (
        dataframe["target"]
        .value_counts()
    )

    total_profiles = len(dataframe)

    largest_class = int(
        distribution.max()
    )

    smallest_class = int(
        distribution.min()
    )

    imbalance_ratio = (
        largest_class / smallest_class
        if smallest_class > 0
        else float("inf")
    )

    print("\n" + "-" * 60)
    print(f"Dataset: {dataset_name}")
    print("-" * 60)

    print(
        f"Perfis válidos: "
        f"{total_profiles}"
    )

    print(
        f"Classes do target: "
        f"{len(distribution)}"
    )

    print(
        "\nDistribuição:"
    )

    for target_class, count in (
        distribution.items()
    ):
        percentage = (
            count
            / total_profiles
            * 100
        )

        print(
            f"{target_class}: "
            f"{count} perfis "
            f"({percentage:.2f}%)"
        )

    print(
        "\nMaior classe: "
        f"{largest_class} perfis"
    )

    print(
        "Menor classe: "
        f"{smallest_class} perfis"
    )

    print(
        "Razão de desbalanceamento: "
        f"{imbalance_ratio:.2f}:1"
    )


def compare_category_distributions() -> pd.DataFrame:
    """Compara os datasets agrupado e granulares."""

    print("\n" + "=" * 60)
    print(
        "Comparação das distribuições "
        "dos datasets"
    )
    print("=" * 60)

    grouped_dataframe = pd.read_csv(
        FINAL_DATASET_FILE
    )

    combat_dataframe = (
        build_combat_dataset()
    )

    exploration_dataframe = (
        build_exploration_dataset()
    )

    puzzle_dataframe = (
        build_puzzle_dataset()
    )

    datasets = {
        "grouped": grouped_dataframe,
        "combat": combat_dataframe,
        "exploration": exploration_dataframe,
        "puzzle": puzzle_dataframe,
    }

    comparison_records: list[
        dict[str, object]
    ] = []

    for dataset_name, dataframe in (
        datasets.items()
    ):
        print_dataset_summary(
            dataset_name=dataset_name,
            dataframe=dataframe,
        )

        comparison_records.extend(
            summarize_dataset(
                dataset_name=dataset_name,
                dataframe=dataframe,
            )
        )

    comparison_dataframe = pd.DataFrame(
        comparison_records
    )

    METRICS_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    comparison_dataframe.to_csv(
        COMPARISON_FILE,
        index=False,
    )

    print("\n" + "=" * 60)
    print("Resumo comparativo")
    print("=" * 60)

    pivot_table = (
        comparison_dataframe
        .pivot(
            index="target_class",
            columns="dataset",
            values="percentage",
        )
        .fillna("-")
    )

    print(
        pivot_table.to_string()
    )

    print(
        "\nComparação salva em: "
        f"{Path(COMPARISON_FILE).resolve()}"
    )

    return comparison_dataframe


def main() -> None:
    compare_category_distributions()


if __name__ == "__main__":
    main()