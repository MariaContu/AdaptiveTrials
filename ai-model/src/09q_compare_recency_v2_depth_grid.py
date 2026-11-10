"""Experimento exploratório de profundidade da Random Forest recency_v2.

Execute a partir de ai-model:
    python src/09q_compare_recency_v2_depth_grid.py

Compara profundidades 3..10 com a referência 12. Seleciona candidato pela
validação cruzada apenas no TREINO; usa o TESTE histórico para descrição,
não como validação independente nem para escolher hiperparâmetros.
Não substitui o modelo final, não salva dados individuais de jogadores.
"""
from __future__ import annotations

import argparse
import json
import hashlib
import platform
from pathlib import Path

import sklearn

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
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
from sklearn.model_selection import StratifiedKFold, cross_validate

ROOT = Path(__file__).resolve().parents[1]
CLASSES = ["combat", "exploration", "strategic_reasoning"]
LABELS = ["Combate", "Exploração", "Quebra-cabeça"]
DEPTHS = [*range(3, 11), 12]
METRICS = ["accuracy", "balanced_accuracy", "macro_f1", "weighted_f1"]


def read_inputs(root: Path):
    train_path = root / "data/modeling/macro_train_recency_v2.csv"
    test_path = root / "data/modeling/macro_test_recency_v2.csv"
    metadata_path = root / "models/final/macro_model_recency_v2_metadata.json"
    train = pd.read_csv(train_path)
    test = pd.read_csv(test_path)
    meta = json.loads(metadata_path.read_text(encoding="utf-8"))
    if meta["model"].get("strategy") != "class_weight":
        raise ValueError("O modelo de referência não usa a estratégia class_weight.")
    params = dict(meta["model"]["rf_params"])
    if params.get("max_depth") != 12 or int(params.get("n_estimators", 0)) != 200:
        raise ValueError("Referência esperada: Random Forest com 200 árvores, max_depth=12.")
    features = list(meta["features"]["feature_names"])
    if len(features) != 49 or len(set(features)) != len(features):
        raise ValueError("Esperadas 49 características distintas no metadata.")
    if len(train) != 548 or len(test) != 138:
        raise ValueError(f"Split diferente do histórico: treino={len(train)}, teste={len(test)}.")
    for name, df in (("treino", train), ("teste", test)):
        missing = sorted(set(features + ["macro_target"]) - set(df.columns))
        if missing:
            raise ValueError(f"Colunas ausentes no {name}: {missing}")
        unknown = set(df["macro_target"].astype(str)) - set(CLASSES)
        if unknown:
            raise ValueError(f"Classes inesperadas no {name}: {sorted(unknown)}")
    if "final_player_id" in train and "final_player_id" in test:
        if set(train["final_player_id"]) & set(test["final_player_id"]):
            raise ValueError("Há jogadores presentes simultaneamente em treino e teste.")
    x_train = train.loc[:, features].apply(pd.to_numeric, errors="coerce").astype(float)
    x_test = test.loc[:, features].apply(pd.to_numeric, errors="coerce").astype(float)
    if not np.isfinite(x_train.to_numpy()).all() or not np.isfinite(x_test.to_numpy()).all():
        raise ValueError("Há características NaN/infinito.")
    return (x_train, train["macro_target"].astype(str),
            x_test, test["macro_target"].astype(str), params)


def new_model(params: dict, depth: int, seed: int):
    return RandomForestClassifier(
        **{**params, "max_depth": depth, "random_state": seed},
        class_weight="balanced", n_jobs=-1,
    )


def evaluate(model, x, y):
    predictions = model.predict(x)
    report = classification_report(
        y, predictions, labels=CLASSES, output_dict=True, zero_division=0
    )
    return {
        "accuracy": float(accuracy_score(y, predictions)),
        "balanced_accuracy": float(balanced_accuracy_score(y, predictions)),
        "macro_f1": float(f1_score(y, predictions, average="macro", zero_division=0)),
        "weighted_f1": float(f1_score(y, predictions, average="weighted", zero_division=0)),
        "by_class": {
            cls: {k: float(report[cls][k]) for k in ("precision", "recall", "f1-score", "support")}
            for cls in CLASSES
        },
        "confusion_matrix": confusion_matrix(y, predictions, labels=CLASSES).tolist(),
        "observed_max_depth": int(max(tree.get_depth() for tree in model.estimators_)),
        "mean_tree_depth": float(np.mean([tree.get_depth() for tree in model.estimators_])),
    }


def save_chart(rows: list[dict], output: Path):
    depths = [r["depth"] for r in rows]
    fig, axes = plt.subplots(1, 2, figsize=(13, 4.8))
    for metric, label in (("cv_macro_f1_mean", "F1 macro (validação cruzada)"),
                          ("cv_balanced_accuracy_mean", "Acurácia balanceada (validação cruzada)")):
        axes[0].plot(depths, [r[metric] for r in rows], marker="o", label=label)
    axes[0].set_title("Validação cruzada — somente treino")
    axes[0].set_xlabel("Profundidade máxima")
    axes[0].set_ylabel("Pontuação")
    axes[0].set_xticks(depths)
    axes[0].grid(alpha=0.25)
    axes[0].legend(fontsize=8)
    for metric, label in (("test_macro_f1", "F1 macro"),
                          ("test_balanced_accuracy", "Acurácia balanceada"),
                          ("test_accuracy", "Acurácia")):
        axes[1].plot(depths, [r[metric] for r in rows], marker="o", label=label)
    axes[1].set_title("Teste histórico — comparação exploratória")
    axes[1].set_xlabel("Profundidade máxima")
    axes[1].set_ylabel("Pontuação")
    axes[1].set_xticks(depths)
    axes[1].grid(alpha=0.25)
    axes[1].legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(output, dpi=160, bbox_inches="tight")
    plt.close(fig)


def save_class_chart(results: dict, output: Path):
    fig, ax = plt.subplots(figsize=(11, 5))
    for cls, label in zip(CLASSES, LABELS):
        ax.plot(DEPTHS, [results[str(d)]["test"]["by_class"][cls]["f1-score"]
                             for d in DEPTHS], marker="o", label=label)
    ax.set(title="F1 por categoria — teste histórico (exploratório)",
           xlabel="Profundidade máxima", ylabel="F1-score")
    ax.set_xticks(DEPTHS)
    ax.set_ylim(0, 1)
    ax.grid(alpha=0.25)
    ax.legend()
    fig.tight_layout()
    fig.savefig(output, dpi=160, bbox_inches="tight")
    plt.close(fig)


def save_confusion(matrix, title, output):
    m = np.asarray(matrix, dtype=int)
    fig, ax = plt.subplots(figsize=(6, 5))
    ax.imshow(m, cmap="Blues")
    ax.set_xticks(range(3), LABELS, rotation=25, ha="right")
    ax.set_yticks(range(3), LABELS)
    ax.set_xlabel("Classe prevista")
    ax.set_ylabel("Classe real")
    ax.set_title(title)
    for i in range(3):
        for j in range(3):
            ax.text(j, i, str(m[i, j]), ha="center", va="center", color="black")
    fig.tight_layout()
    fig.savefig(output, dpi=160, bbox_inches="tight")
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--folds", type=int, default=5)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    if args.folds < 2:
        raise ValueError("Use ao menos 2 folds.")
    root = args.root.resolve()
    x_train, y_train, x_test, y_test, params = read_inputs(root)
    if y_train.value_counts().min() < args.folds:
        raise ValueError("Há uma categoria com menos amostras que o número de folds.")
    cv = StratifiedKFold(n_splits=args.folds, shuffle=True, random_state=args.seed)
    scoring = {"accuracy": "accuracy", "balanced_accuracy": "balanced_accuracy",
               "macro_f1": "f1_macro", "weighted_f1": "f1_weighted"}
    results, rows = {}, []
    print(f"Treino: {len(y_train)} | teste histórico: {len(y_test)} | características: {x_train.shape[1]}")
    for depth in DEPTHS:
        model = new_model(params, depth, args.seed)
        scores = cross_validate(model, x_train, y_train, scoring=scoring, cv=cv, n_jobs=1)
        cv_summary = {}
        for metric in METRICS:
            vals = scores[f"test_{metric}"]
            cv_summary[metric] = {"mean": float(np.mean(vals)), "std": float(np.std(vals))}
        model.fit(x_train, y_train)
        test = evaluate(model, x_test, y_test)
        if test["observed_max_depth"] > depth:
            raise AssertionError("Árvore excedeu a profundidade máxima configurada.")
        results[str(depth)] = {"cv": cv_summary, "test": test}
        row = {"depth": depth}
        row.update({f"cv_{m}_mean": cv_summary[m]["mean"] for m in METRICS})
        row.update({f"cv_{m}_std": cv_summary[m]["std"] for m in METRICS})
        row.update({f"test_{m}": test[m] for m in METRICS})
        for cls in CLASSES:
            for metric in ("precision", "recall", "f1-score"):
                row[f"test_{cls}_{metric.replace('-', '_')}"] = test["by_class"][cls][metric]
        rows.append(row)
        print(f"{depth:>2} | CV F1 macro {row['cv_macro_f1_mean']:.4f} ± {row['cv_macro_f1_std']:.4f} | "
              f"teste F1 macro {row['test_macro_f1']:.4f} | "
              f"ac. balanceada {row['test_balanced_accuracy']:.4f}")
    # Seleção baseada SOMENTE no treino, nunca no conjunto de teste histórico.
    candidates = [r for r in rows if r["depth"] != 12]
    best = max(candidates, key=lambda r: (r["cv_macro_f1_mean"],
                                           r["cv_balanced_accuracy_mean"], -r["depth"]))
    metric_dir = root / "reports/metrics"
    figure_dir = root / "reports/figures"
    metric_dir.mkdir(parents=True, exist_ok=True)
    figure_dir.mkdir(parents=True, exist_ok=True)
    output = {
        "experiment": "09q_recency_v2_depth_grid_3_to_10_vs_12",
        "status": "exploratory_not_deployed",
        "selection_method": "highest_mean_cv_macro_f1_on_training_only",
        "caution": "O teste histórico já foi consultado em experimentos anteriores; NÃO é uma validação independente. A profundidade candidata é escolhida apenas por CV no treino.",
        "train_rows": len(y_train), "test_rows": len(y_test),
        "feature_count": x_train.shape[1], "n_estimators": 200,
        "environment": {
            "python": platform.python_version(),
            "scikit_learn": sklearn.__version__,
            "numpy": np.__version__,
            "pandas": pd.__version__,
        },
        "input_sha256": {
            name: hashlib.sha256((root / relative).read_bytes()).hexdigest()
            for name, relative in {
                "train": "data/modeling/macro_train_recency_v2.csv",
                "test": "data/modeling/macro_test_recency_v2.csv",
                "metadata": "models/final/macro_model_recency_v2_metadata.json",
            }.items()
        },
        "folds": args.folds, "seed": args.seed, "class_order": CLASSES,
        "candidate_depth_cv_only": int(best["depth"]),
        "models": results,
    }
    (metric_dir / "09q_depth_grid_results.json").write_text(
        json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    pd.DataFrame(rows).to_csv(metric_dir / "09q_depth_grid_summary.csv", index=False)
    save_chart(rows, figure_dir / "09q_depth_grid_comparison.png")
    save_class_chart(results, figure_dir / "09q_f1_por_categoria.png")
    for depth in DEPTHS:
        save_confusion(results[str(depth)]["test"]["confusion_matrix"],
                       f"Matriz de confusão — profundidade {depth}",
                       figure_dir / f"09q_matriz_confusao_depth_{depth}.png")
    print(f"Candidato por validação cruzada no treino: profundidade {best['depth']}")
    print(f"Relatórios: {metric_dir / '09q_depth_grid_results.json'}")
    print(f"Figuras: {figure_dir}")
    print("Modelo final em produção: NÃO ALTERADO")


if __name__ == "__main__":
    main()
