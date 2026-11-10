"""Visualização fiel de um caminho em árvore de uma Random Forest.

A floresta agrega muitas árvores; esta figura mostra uma árvore representativa,
não uma explicação causal da previsão completa.
"""
from __future__ import annotations

from html import escape
from pathlib import Path
from typing import Any

import numpy as np

LABELS = {
    "combat": "Combate",
    "exploration": "Exploração",
    "strategic_reasoning": "Quebra-cabeça",
}


def _label(value: Any) -> str:
    return LABELS.get(str(value), str(value).replace("_", " ").capitalize())


def extract_representative_path(
    model: Any,
    sample: Any,
    features: list[str],
) -> dict[str, Any]:
    """Retorna decisões reais da árvore cuja saída mais se aproxima da floresta.

    Prioriza árvores que classificam na mesma categoria que a floresta.
    Não usa importâncias globais como explicação local.
    """
    estimators = getattr(model, "estimators_", None)
    if not estimators:
        raise ValueError("O modelo não possui árvores individuais acessíveis.")
    row = np.asarray(sample, dtype=np.float32).reshape(1, -1)
    if row.shape[1] != len(features):
        raise ValueError("Quantidade de características incompatível.")

    classes = [str(c) for c in model.classes_]
    forest_probs = np.asarray(model.predict_proba(row)[0], dtype=float)
    winner = int(np.argmax(forest_probs))

    candidates = []
    for index, estimator in enumerate(estimators):
        probs = np.asarray(estimator.predict_proba(row)[0], dtype=float)
        if len(probs) != len(classes):
            raise ValueError("Classes incompatíveis entre floresta e árvore.")
        agreement = int(np.argmax(probs)) == winner
        distance = float(np.linalg.norm(probs - forest_probs))
        candidates.append((not agreement, distance, index, probs))
    _, _, chosen_index, tree_probs = min(candidates, key=lambda x: x[:3])

    estimator = estimators[chosen_index]
    tree = estimator.tree_
    node = 0
    decisions = []
    while int(tree.children_left[node]) != int(tree.children_right[node]):
        feature_index = int(tree.feature[node])
        if feature_index < 0 or feature_index >= len(features):
            raise ValueError("Índice de característica inválido na árvore.")
        value = float(row[0, feature_index])
        threshold = float(tree.threshold[node])
        if np.isnan(value):
            # Scikit-learn pode direcionar NaNs de acordo com o treinamento.
            go_left = bool(tree.missing_go_to_left[node])
        else:
            go_left = value <= threshold
        decisions.append({
            "no": int(node),
            "caracteristica": features[feature_index],
            "valor": value,
            "limite": threshold,
            "operador": "≤" if go_left else ">",
            "ramo": "Sim" if go_left else "Não",
        })
        node = int(tree.children_left[node] if go_left else tree.children_right[node])

    return {
        "tipo_modelo": type(model).__name__,
        "quantidade_arvores": len(estimators),
        "arvore_indice": chosen_index,
        "classe_floresta": classes[winner],
        "probabilidades_floresta": dict(zip(classes, map(float, forest_probs))),
        "classe_arvore": classes[int(np.argmax(tree_probs))],
        "probabilidades_arvore": dict(zip(classes, map(float, tree_probs))),
        "folha_no": node,
        "decisoes": decisions,
        "criterio_selecao": (
            "Árvore que concorda com a categoria da floresta e cuja distribuição "
            "de probabilidades é a mais próxima da distribuição agregada."
        ),
    }


def render_tree_svg(result: dict[str, Any], output_path: Path) -> Path:
    """Desenha decisões efetivamente percorridas; outros ramos ficam sinalizados."""
    decisions = result["decisoes"]
    width = 1160
    start_y = 270
    step = 143
    leaf_y = start_y + len(decisions) * step
    height = leaf_y + 240
    forest = result["probabilidades_floresta"]
    lines = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
        '<rect width="100%" height="100%" fill="#211629"/>',
        '<text x="580" y="54" text-anchor="middle" font-family="Arial,sans-serif" font-size="30" font-weight="bold" fill="#f1e5da">Caminho de decisão — perfil Steam</text>',
        '<text x="580" y="88" text-anchor="middle" font-family="Arial,sans-serif" font-size="17" fill="#cdb9cb">Uma árvore representativa da Random Forest, não a floresta inteira</text>',
        '<rect x="150" y="113" width="860" height="112" rx="18" fill="#bfa99f"/>',
        f'<text x="580" y="147" text-anchor="middle" font-family="Arial,sans-serif" font-size="23" font-weight="bold" fill="#32253b">Previsão agregada: {escape(_label(result["classe_floresta"]))}</text>',
        '<text x="580" y="180" text-anchor="middle" font-family="Arial,sans-serif" font-size="17" fill="#32253b">' +
        escape('  •  '.join(f'{_label(k)}: {v * 100:.2f}%' for k, v in forest.items())) + '</text>',
        f'<text x="580" y="207" text-anchor="middle" font-family="Arial,sans-serif" font-size="13" fill="#48374d">Árvore {result["arvore_indice"] + 1} de {result["quantidade_arvores"]} — selecionada por proximidade ao resultado da floresta</text>',
    ]
    for i, item in enumerate(decisions):
        y = start_y + i * step
        name = escape(item["caracteristica"])
        value = item["valor"]
        limit = item["limite"]
        branch = item["ramo"]
        alternative = "Não" if branch == "Sim" else "Sim"
        lines += [
            f'<rect x="285" y="{y}" width="590" height="76" rx="15" fill="#e5d7d1" stroke="#b29bb2" stroke-width="3"/>',
            f'<text x="580" y="{y+29}" text-anchor="middle" font-family="Arial,sans-serif" font-size="18" font-weight="bold" fill="#33243e">{name}</text>',
            f'<text x="580" y="{y+56}" text-anchor="middle" font-family="Arial,sans-serif" font-size="16" fill="#45374e">Valor: {value:.4f}  |  Limiar: {limit:.4f}  |  {escape(item["operador"])} limiar</text>',
            f'<path d="M875 {y+38} H905 V{y+103} H930" fill="none" stroke="#9b8499" stroke-width="2" stroke-dasharray="6 5"/>',
            f'<rect x="930" y="{y+83}" width="206" height="43" rx="10" fill="#47394c" stroke="#806b7e"/>',
            f'<text x="1033" y="{y+101}" text-anchor="middle" font-family="Arial,sans-serif" font-size="13" fill="#d7c9d3">{alternative}: outro ramo</text>',
            f'<text x="1033" y="{y+117}" text-anchor="middle" font-family="Arial,sans-serif" font-size="11" fill="#d7c9d3">não percorrido</text>',
            f'<line x1="580" y1="{y+76}" x2="580" y2="{y+step}" stroke="#91bda4" stroke-width="4"/>',
            f'<rect x="602" y="{y+91}" width="77" height="27" rx="12" fill="#3f6c59"/>',
            f'<text x="640" y="{y+110}" text-anchor="middle" font-family="Arial,sans-serif" font-size="14" font-weight="bold" fill="#ffffff">{branch}</text>',
        ]
    lines += [
        f'<rect x="285" y="{leaf_y}" width="590" height="86" rx="16" fill="#9dcbb2" stroke="#5e9479" stroke-width="3"/>',
        f'<text x="580" y="{leaf_y+32}" text-anchor="middle" font-family="Arial,sans-serif" font-size="21" font-weight="bold" fill="#203c30">Folha da árvore: {escape(_label(result["classe_arvore"]))}</text>',
        f'<text x="580" y="{leaf_y+61}" text-anchor="middle" font-family="Arial,sans-serif" font-size="15" fill="#203c30">Nó {result["folha_no"]} — {len(decisions)} decisões percorridas</text>',
        f'<text x="580" y="{leaf_y+125}" text-anchor="middle" font-family="Arial,sans-serif" font-size="15" fill="#e0cfdc">A previsão final é a média das probabilidades das {result["quantidade_arvores"]} árvores.</text>',
        f'<text x="580" y="{leaf_y+153}" text-anchor="middle" font-family="Arial,sans-serif" font-size="13" fill="#c4b1c2">Este caminho é fiel à árvore selecionada, mas não explica isoladamente toda a previsão.</text>',
        '</svg>',
    ]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text('\n'.join(lines), encoding='utf-8')
    return output_path
