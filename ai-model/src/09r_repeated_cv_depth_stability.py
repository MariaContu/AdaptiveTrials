"""Experimento 09R: estabilidade da profundidade da Random Forest recency_v2.

Executar em ai-model: python src/09r_repeated_cv_depth_stability.py
Somente treino (548 jogadores), sem ler o teste historico ou alterar o modelo final.
O protocolo de treino e os hiperparametros seguem o experimento 09Q.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import importlib
import json
import platform
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
import sklearn
from sklearn.metrics import (
    accuracy_score, balanced_accuracy_score, confusion_matrix,
    f1_score, precision_recall_fscore_support,
)
from sklearn.model_selection import RepeatedStratifiedKFold

ROOT = Path(__file__).resolve().parents[1]
CLASSES = ["combat", "exploration", "strategic_reasoning"]
LABELS = ["Combate", "Exploração", "Quebra-cabeça"]
DEPTHS = [5, 9, 10, 12]
METRICS = ["accuracy", "balanced_accuracy", "macro_f1", "weighted_f1"]
# Reutiliza a construcao exata da Random Forest do 09Q, sem mudar hiperparametros.
PREVIOUS = importlib.import_module("09q_compare_recency_v2_depth_grid")


def load_train(root: Path):
    train_path = root / "data/modeling/macro_train_recency_v2.csv"
    metadata_path = root / "models/final/macro_model_recency_v2_metadata.json"
    train = pd.read_csv(train_path)
    meta = json.loads(metadata_path.read_text(encoding="utf-8"))
    if meta["model"].get("strategy") != "class_weight":
        raise ValueError("A estrategia de treinamento precisa ser class_weight.")
    params = dict(meta["model"]["rf_params"])
    if params.get("max_depth") != 12 or int(params.get("n_estimators", 0)) != 200:
        raise ValueError("Referencia inesperada: sao exigidas 200 arvores e max_depth=12.")
    features = list(meta["features"]["feature_names"])
    if len(features) != 49 or len(set(features)) != 49 or len(train) != 548:
        raise ValueError("Esperados 548 exemplos e 49 caracteristicas distintas.")
    missing = set(features + ["macro_target", "final_player_id"]) - set(train.columns)
    if missing:
        raise ValueError(f"Colunas ausentes: {sorted(missing)}")
    if train["final_player_id"].isna().any() or train["final_player_id"].duplicated().any():
        raise ValueError("IDs de jogadores ausentes/duplicados: risco de vazamento entre folds.")
    y = train["macro_target"].astype(str).to_numpy()
    if set(y) != set(CLASSES):
        raise ValueError(f"Categorias inesperadas: {sorted(set(y))}")
    x = train.loc[:, features].apply(pd.to_numeric, errors="coerce").astype(float)
    if not np.isfinite(x.to_numpy()).all():
        raise ValueError("Caracteristicas invalidas (NaN ou infinito).")
    return x, y, params, {
        "train": hashlib.sha256(train_path.read_bytes()).hexdigest(),
        "metadata": hashlib.sha256(metadata_path.read_bytes()).hexdigest(),
    }


def metric_values(y_true, y_pred):
    precision, recall, f1, support = precision_recall_fscore_support(
        y_true, y_pred, labels=CLASSES, zero_division=0
    )
    return {
        "accuracy": float(accuracy_score(y_true, y_pred)),
        "balanced_accuracy": float(balanced_accuracy_score(y_true, y_pred)),
        "macro_f1": float(f1_score(y_true, y_pred, average="macro", zero_division=0)),
        "weighted_f1": float(f1_score(y_true, y_pred, average="weighted", zero_division=0)),
        "by_class": {
            cls: {
                "precision": float(precision[i]), "recall": float(recall[i]),
                "f1": float(f1[i]), "support": int(support[i]),
            }
            for i, cls in enumerate(CLASSES)
        },
        "confusion_matrix": confusion_matrix(y_true, y_pred, labels=CLASSES).tolist(),
    }


def evaluate_depths(x, y, params, depths, folds=5, repeats=5, seed=42):
    if folds < 2 or repeats < 1:
        raise ValueError("Exigidos folds >= 2 e repeats >= 1.")
    if min(np.unique(y, return_counts=True)[1]) < folds:
        raise ValueError("Ha uma categoria com menos amostras do que folds.")
    splitter = RepeatedStratifiedKFold(
        n_splits=folds, n_repeats=repeats, random_state=seed
    )
    splits = list(splitter.split(x, y))
    results = {}
    for depth in depths:
        fold_rows = []
        repeat_rows = []
        for repeat in range(repeats):
            oof = np.empty(len(y), dtype=object)
            counts = np.zeros(len(y), dtype=np.int8)
            for fold in range(folds):
                train_idx, valid_idx = splits[repeat * folds + fold]
                model = PREVIOUS.new_model(params, depth, seed)
                model.fit(x.iloc[train_idx], y[train_idx])
                pred = model.predict(x.iloc[valid_idx])
                oof[valid_idx] = pred
                counts[valid_idx] += 1
                scores = metric_values(y[valid_idx], pred)
                fold_rows.append({
                    "depth": depth, "repeat": repeat + 1, "fold": fold + 1,
                    **{k: scores[k] for k in METRICS},
                    **{f"{cls}_f1": scores["by_class"][cls]["f1"] for cls in CLASSES},
                })
            if not np.all(counts == 1):
                raise AssertionError("Cada amostra precisa ter uma previsao OOF por repeticao.")
            scores = metric_values(y, oof.astype(str))
            repeat_rows.append({"depth": depth, "repeat": repeat + 1, **scores})
        results[str(depth)] = {"folds": fold_rows, "repeats": repeat_rows}
    return results


def summarize(results, depths):
    summary = {}
    for depth in depths:
        r = results[str(depth)]
        entry = {"depth": depth, "metrics": {}, "by_class": {}}
        for metric in METRICS:
            vals = np.array([item[metric] for item in r["repeats"]], dtype=float)
            entry["metrics"][metric] = {
                "mean": float(vals.mean()), "std": float(vals.std(ddof=1)) if len(vals) > 1 else 0.0,
                "min": float(vals.min()), "max": float(vals.max()),
                "values_by_repeat": vals.tolist(),
            }
        for cls in CLASSES:
            entry["by_class"][cls] = {}
            for metric in ("precision", "recall", "f1"):
                vals = np.array([item["by_class"][cls][metric] for item in r["repeats"]])
                entry["by_class"][cls][metric] = {
                    "mean": float(vals.mean()),
                    "std": float(vals.std(ddof=1)) if len(vals) > 1 else 0.0,
                }
        summary[str(depth)] = entry
    return summary


def paired_differences(results, depths, baseline=12):
    base = results[str(baseline)]["repeats"]
    out = {}
    for depth in depths:
        if depth == baseline:
            continue
        by_metric = {}
        for metric in ("macro_f1", "balanced_accuracy"):
            diffs = [float(a[metric] - b[metric])
                     for a, b in zip(results[str(depth)]["repeats"], base)]
            by_metric[metric] = {
                "values_by_repeat": diffs,
                "mean": float(np.mean(diffs)),
                "min": float(min(diffs)), "max": float(max(diffs)),
                "wins": sum(d > 0 for d in diffs),
                "ties": sum(d == 0 for d in diffs),
                "losses": sum(d < 0 for d in diffs),
            }
        out[f"{depth}_vs_{baseline}"] = by_metric
    return out


def write_csv(path, rows):
    if not rows:
        return
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def save_figures(results, summary, depths, figures_dir):
    fig, ax = plt.subplots(figsize=(9, 5))
    vals = [[r["macro_f1"] for r in results[str(d)]["repeats"]] for d in depths]
    ax.boxplot(vals, tick_labels=[str(d) for d in depths])
    ax.set(title="F1 macro — validacao cruzada repetida (somente treino)",
           xlabel="Profundidade maxima", ylabel="F1 macro")
    ax.grid(axis="y", alpha=0.2)
    fig.tight_layout()
    fig.savefig(figures_dir / "09r_f1_macro_distribuicao.png", dpi=160)
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(9, 5))
    for d in depths:
        ax.plot(range(1, len(results[str(d)]["repeats"]) + 1),
                [r["macro_f1"] for r in results[str(d)]["repeats"]],
                marker="o", label=f"Profundidade {d}")
    ax.set(title="F1 macro por repeticao (predicoes fora do fold)",
           xlabel="Repeticao", ylabel="F1 macro")
    ax.set_xticks(range(1, len(results[str(depths[0])]["repeats"]) + 1))
    ax.grid(alpha=0.2)
    ax.legend()
    fig.tight_layout()
    fig.savefig(figures_dir / "09r_f1_macro_repeticoes.png", dpi=160)
    plt.close(fig)

    fig, ax = plt.subplots(figsize=(9, 5))
    for cls, label in zip(CLASSES, LABELS):
        ax.plot(depths, [summary[str(d)]["by_class"][cls]["f1"]["mean"] for d in depths],
                marker="o", label=label)
    ax.set(title="F1 por categoria — media entre repeticoes",
           xlabel="Profundidade maxima", ylabel="F1 por categoria")
    ax.set_xticks(depths)
    ax.set_ylim(0, 1)
    ax.grid(alpha=0.2)
    ax.legend()
    fig.tight_layout()
    fig.savefig(figures_dir / "09r_f1_por_categoria.png", dpi=160)
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--folds", type=int, default=5)
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    root = args.root.resolve()
    x, y, params, hashes = load_train(root)
    print(f"Experimento 09R | treino={len(y)} | caracteristicas={x.shape[1]}")
    print(f"Validacao: {args.folds} folds x {args.repeats} repeticoes | profundidades: {DEPTHS}")
    results = evaluate_depths(x, y, params, DEPTHS, args.folds, args.repeats, args.seed)
    summary = summarize(results, DEPTHS)
    paired = paired_differences(results, DEPTHS)
    for depth in DEPTHS:
        m = summary[str(depth)]["metrics"]["macro_f1"]
        b = summary[str(depth)]["metrics"]["balanced_accuracy"]
        print(f"{depth:>2} | F1 macro OOF {m['mean']:.4f} +/- {m['std']:.4f} | "
              f"ac. balanceada {b['mean']:.4f} +/- {b['std']:.4f}")
    best = max(DEPTHS, key=lambda d: (summary[str(d)]["metrics"]["macro_f1"]["mean"], -d))
    metric_dir = root / "reports/metrics"
    figure_dir = root / "reports/figures"
    metric_dir.mkdir(parents=True, exist_ok=True)
    figure_dir.mkdir(parents=True, exist_ok=True)
    output = {
        "experiment": "09r_repeated_stratified_cv_depth_stability",
        "status": "exploratory_not_deployed",
        "caution": "A selecao usa somente o treino historico; teste historico NAO acessado. "
                   "Repeticoes de CV compartilham exemplos e NAO sao observacoes independentes. "
                   "Nao interpretar variabilidade entre repeticoes como intervalo de confianca independente.",
        "protocol": {"depths": DEPTHS, "folds": args.folds, "repeats": args.repeats,
                     "seed": args.seed, "n_estimators": int(params["n_estimators"]),
                     "train_rows": len(y), "features": x.shape[1],
                     "class_order": CLASSES, "selection_metric": "mean_oof_macro_f1_by_repeat",
                     "candidate_depth_cv_only": best},
        "environment": {"python": platform.python_version(), "scikit_learn": sklearn.__version__,
                        "numpy": np.__version__, "pandas": pd.__version__},
        "input_sha256": hashes,
        "summary": summary, "paired_vs_depth12": paired,
        "per_repeat": {str(d): results[str(d)]["repeats"] for d in DEPTHS},
        "per_fold": {str(d): results[str(d)]["folds"] for d in DEPTHS},
    }
    report = metric_dir / "09r_repeated_cv_depth_stability.json"
    report.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    write_csv(metric_dir / "09r_repeated_cv_summary.csv", [
        {"depth": d, **{f"{m}_{stat}": summary[str(d)]["metrics"][m][stat]
                        for m in METRICS for stat in ("mean", "std")},
         **{f"{cls}_f1_mean": summary[str(d)]["by_class"][cls]["f1"]["mean"]
            for cls in CLASSES}} for d in DEPTHS
    ])
    write_csv(metric_dir / "09r_repeated_cv_per_fold.csv", [
        row for d in DEPTHS for row in results[str(d)]["folds"]
    ])
    write_csv(metric_dir / "09r_repeated_cv_per_repeat.csv", [
        {"depth": d, "repeat": r["repeat"], **{m: r[m] for m in METRICS},
         **{f"{cls}_f1": r["by_class"][cls]["f1"] for cls in CLASSES}}
        for d in DEPTHS for r in results[str(d)]["repeats"]
    ])
    save_figures(results, summary, DEPTHS, figure_dir)
    print(f"Candidato pela CV repetida (somente treino): profundidade {best}")
    print(f"Relatorio: {report}")
    print("Teste historico: NAO UTILIZADO | Modelo final: NAO ALTERADO")


if __name__ == "__main__":
    main()
