"""Testes offline: não exigem Steam, API .NET nem serviço Python em execução."""
import sys
from pathlib import Path
from xml.etree import ElementTree

import numpy as np
from sklearn.ensemble import RandomForestClassifier

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from inference_tree_visualization import extract_representative_path, render_tree_svg


def test_arvore_real_e_svg(tmp_path):
    x = np.array([
        [0.1, 0.2], [0.2, 0.8], [0.3, 0.2], [0.4, 0.9],
        [0.6, 0.1], [0.7, 0.7], [0.8, 0.2], [0.9, 0.8],
    ])
    y = np.array(["combat", "exploration", "combat", "exploration",
                  "combat", "exploration", "combat", "exploration"])
    model = RandomForestClassifier(n_estimators=7, max_depth=3, random_state=42).fit(x, y)
    sample = np.array([[0.33, 0.77]])
    result = extract_representative_path(model, sample, ["share_combat", "share_exploration"])
    assert result["quantidade_arvores"] == 7
    assert result["classe_floresta"] == model.predict(sample)[0]
    assert result["decisoes"]
    estimator = model.estimators_[result["arvore_indice"]]
    assert result["folha_no"] == int(estimator.apply(sample)[0])
    for decision in result["decisoes"]:
        assert (decision["valor"] <= decision["limite"]) == (decision["ramo"] == "Sim")
    path = render_tree_svg(result, tmp_path / "arvore.svg")
    root = ElementTree.parse(path).getroot()
    assert root.tag.endswith("svg")
    assert "Random Forest" in path.read_text(encoding="utf-8")
