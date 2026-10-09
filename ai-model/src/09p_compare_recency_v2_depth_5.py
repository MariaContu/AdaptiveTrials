"""Experimento exploratorio: Random Forest recency_v2 profundidade 12 versus 5.

Executar a partir de ai-model:
    python src/09p_compare_recency_v2_depth_5.py

Reutiliza o split historico (548 treino / 138 teste) e os 49 atributos
originais. Nao substitui o modelo de producao nem executa selecao de modelo.
O conjunto de teste ja foi utilizado nas comparacoes anteriores (09h),
portanto esta analise e exploratoria, nao uma nova validacao independente.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestClassifier
from sklearn.metrics import (
    accuracy_score,
    balanced_accuracy_score,
    classification_report,
    confusion_matrix,
    f1_score,
)

ROOT = Path(__file__).resolve().parents[1]
CLASS_ORDER = ["combat", "exploration", "strategic_reasoning"]


def numeric_matrix(df: pd.DataFrame, features: list[str]) -> pd.DataFrame:
    missing = sorted(set(features) - set(df.columns))
    if missing:
        raise ValueError(f"Colunas ausentes: {missing}")
    x = df.loc[:, features].apply(pd.to_numeric, errors="coerce").astype(float)
    if not np.isfinite(x.to_numpy()).all():
        raise ValueError("Caracteristicas invalidas (NaN ou infinito).")
    return x


def metrics(model: RandomForestClassifier, x: pd.DataFrame, y: pd.Series) -> dict:
    pred = model.predict(x)
    depths = np.array([tree.get_depth() for tree in model.estimators_])
    paths = model.decision_path(x)[0]
    # decision_path conta nos (raiz + descendentes); numero de decisoes = nos - 1
    decisions = np.diff(paths.indptr) - len(model.estimators_)
    return {
        "accuracy": float(accuracy_score(y, pred)),
        "balanced_accuracy": float(balanced_accuracy_score(y, pred)),
        "macro_f1": float(f1_score(y, pred, average="macro", zero_division=0)),
        "weighted_f1": float(f1_score(y, pred, average="weighted", zero_division=0)),
        "classification_report": classification_report(
            y, pred, labels=CLASS_ORDER, output_dict=True, zero_division=0
        ),
        "confusion_matrix": confusion_matrix(y, pred, labels=CLASS_ORDER).tolist(),
        "tree_max_depth_observed": int(depths.max()),
        "tree_mean_depth": float(depths.mean()),
        "mean_decisions_per_tree_per_test_sample": float(decisions.mean() / len(model.estimators_)),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--train", type=Path, default=ROOT / "data/modeling/macro_train_recency_v2.csv")
    parser.add_argument("--test", type=Path, default=ROOT / "data/modeling/macro_test_recency_v2.csv")
    parser.add_argument("--metadata", type=Path, default=ROOT / "models/final/macro_model_recency_v2_metadata.json")
    parser.add_argument("--output", type=Path, default=ROOT / "reports/metrics/09p_depth_5_comparison.json")
    parser.add_argument("--save-depth5", action="store_true", help="Salva artefato experimental separado em models/experimental")
    args = parser.parse_args()
    train = pd.read_csv(args.train)
    test = pd.read_csv(args.test)
    bundle = json.loads(args.metadata.read_text(encoding="utf-8"))
    if bundle["model"].get("strategy") != "class_weight":
        raise ValueError("O candidato recency_v2 esperado deve usar class_weight.")
    params = dict(bundle["model"]["rf_params"])
    if params.get("max_depth") != 12 or int(params.get("n_estimators", 0)) != 200:
        raise ValueError("A configuracao de referencia nao corresponde a 200 arvores, profundidade 12.")
    features = list(bundle["features"]["feature_names"])
    if len(features) != 49:
        raise ValueError("O conjunto de caracteristicas de referencia nao tem 49 colunas.")
    if "macro_target" not in train or "macro_target" not in test:
        raise ValueError("Coluna macro_target ausente.")
    x_train = numeric_matrix(train, features)
    x_test = numeric_matrix(test, features)
    y_train = train["macro_target"].astype(str)
    y_test = test["macro_target"].astype(str)
    if len(train) != 548 or len(test) != 138:
        raise ValueError("Split diferente do historico 09h: esperado 548/138.")
    if "final_player_id" in train and "final_player_id" in test:
        if set(train["final_player_id"]) & set(test["final_player_id"]):
            raise ValueError("Ha jogadores presentes simultaneamente no treino e no teste.")
    results = {}
    fitted_depth5 = None
    for depth in (12, 5):
        rf = RandomForestClassifier(
            **{**params, "max_depth": depth},
            class_weight="balanced",
            random_state=42,
            n_jobs=-1,
        )
        rf.fit(x_train, y_train)
        results[str(depth)] = metrics(rf, x_test, y_test)
        if results[str(depth)]["tree_max_depth_observed"] > depth:
            raise AssertionError("Arvore excedeu a profundidade configurada.")
        if depth == 5:
            fitted_depth5 = rf
    comparison = {
        "experiment": "09p_recency_v2_max_depth_12_vs_5",
        "purpose": "exploratory_comparison_not_independent_holdout",
        "reference": "09h_final_heldout_comparison",
        "note": "Exploratorio: split historico ja utilizado em selecao/avaliacao; metricas podem diferir do relatorio 09h conforme versoes/artefatos.",
        "train_rows": len(train),
        "test_rows": len(test),
        "features": len(features),
        "n_estimators": 200,
        "random_state": 42,
        "class_order": CLASS_ORDER,
        "models": {"depth_12": results["12"], "depth_5": results["5"]},
        "delta_depth5_minus_depth12": {
            metric: results["5"][metric] - results["12"][metric]
            for metric in ("accuracy", "balanced_accuracy", "macro_f1", "weighted_f1")
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(comparison, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Amostras: treino={len(train)}, teste={len(test)}, caracteristicas={len(features)}")
    print("Metrica              profundidade 12   profundidade 5   delta")
    for metric in ("accuracy", "balanced_accuracy", "macro_f1", "weighted_f1"):
        a, b = results["12"][metric], results["5"][metric]
        print(f"{metric:<20} {a:>8.4f}          {b:>8.4f}       {b-a:+.4f}")
    print(f"Relatorio: {args.output}")
    if args.save_depth5:
        output_model = ROOT / "models/experimental/macro_model_recency_v2_depth5_experimental.joblib"
        output_model.parent.mkdir(parents=True, exist_ok=True)
        joblib.dump({
            "pipeline": fitted_depth5,
            "features": features,
            "feature_set": "recency_counts_49",
            "classes": list(fitted_depth5.classes_),
            "strategy": "class_weight",
            "rf_params": {**params, "max_depth": 5},
            "random_state": 42,
            "status": "experimental_not_deployed",
        }, output_model)
        print(f"Modelo experimental: {output_model}")


if __name__ == "__main__":
    main()
