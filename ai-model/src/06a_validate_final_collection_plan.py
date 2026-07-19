#!/usr/bin/env python3
"""Valida o plano versionado da coleta final do dataset."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

EXPECTED_MACRO_CATEGORIES = {"combat", "exploration", "strategic_reasoning"}
EXPECTED_STRATEGIC_SUBGROUPS = {
    "logic_puzzle",
    "planning_management",
    "strategy_decision",
    "observation_deduction",
}

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Valida metas, proporções e requisitos de granularidade da Etapa 06."
    )
    parser.add_argument(
        "--plan",
        type=Path,
        default=Path("config/final_collection_plan.json"),
    )
    return parser.parse_args()

def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(f"Plano não encontrado: {path}")
    with path.open("r", encoding="utf-8") as file:
        return json.load(file)

def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)

def close_enough(value: float, expected: float) -> bool:
    return math.isclose(value, expected, rel_tol=1e-9, abs_tol=1e-9)

def main() -> None:
    args = parse_args()
    plan = load_json(args.plan)

    total_valid = int(plan["valid_profile_target"])
    general = plan["sampling"]["general"]
    strategic = plan["sampling"]["strategic_reasoning"]

    require(total_valid > 0, "A meta total deve ser positiva.")
    require(
        int(general["valid_profile_target"]) + int(strategic["valid_profile_target"])
        == total_valid,
        "As metas válidas por origem não somam a meta total.",
    )
    require(
        close_enough(
            float(general["share_of_valid_profiles"])
            + float(strategic["share_of_valid_profiles"]),
            1.0,
        ),
        "As proporções geral/dirigida não somam 1.",
    )

    subgroup_targets = strategic["subgroup_targets"]
    require(
        set(subgroup_targets) == EXPECTED_STRATEGIC_SUBGROUPS,
        "Os subgrupos estratégicos não correspondem à definição metodológica.",
    )
    require(
        close_enough(
            sum(float(values["share"]) for values in subgroup_targets.values()),
            1.0,
        ),
        "As proporções dos subgrupos estratégicos não somam 1.",
    )
    require(
        sum(int(values["candidate_target"]) for values in subgroup_targets.values())
        == int(strategic["candidate_target"]),
        "As metas por subgrupo não somam a meta estratégica.",
    )

    granularity = plan["granularity_analysis"]
    require(
        set(granularity["preserve_macro_scores"]) == EXPECTED_MACRO_CATEGORIES,
        "As três categorias macro não estão preservadas.",
    )
    require(
        set(granularity["preserve_subgroups"]) == EXPECTED_MACRO_CATEGORIES,
        "A granularidade deve existir nas três categorias macro.",
    )

    expected_comparisons = {
        "macro_three_class_classification",
        "combat_subgroup_classification",
        "exploration_subgroup_classification",
        "strategic_reasoning_subgroup_classification",
    }
    require(
        expected_comparisons.issubset(set(granularity["required_comparisons"])),
        "As quatro avaliações de granularidade não estão definidas.",
    )
    require(
        bool(granularity["retain_continuous_scores"]),
        "As pontuações contínuas devem ser preservadas.",
    )
    require(
        bool(granularity["retain_macro_and_subgroup_targets_separately"]),
        "Targets macro e granulares devem permanecer separados.",
    )

    print("Plano da Etapa 06 validado com sucesso.")
    print(f"Meta de perfis válidos: {total_valid}")
    print(
        f"Origem: geral={general['valid_profile_target']}, "
        f"estratégica={strategic['valid_profile_target']}"
    )
    print(
        f"Candidatos: geral={general['candidate_target']}, "
        f"estratégica={strategic['candidate_target']}"
    )
    print("Metas estratégicas por subgrupo:")
    for name, values in subgroup_targets.items():
        print(
            f"  - {name}: {values['candidate_target']} "
            f"({float(values['share']):.0%})"
        )
    print("Avaliações obrigatórias de granularidade: 4")

if __name__ == "__main__":
    main()