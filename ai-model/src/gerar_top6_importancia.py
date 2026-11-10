import matplotlib.pyplot as plt

# Top 6 atributos da Random Forest V2
features = [
    "played_game_share_strategic_reasoning",
    "game_share_strategic_reasoning",
    "played_game_share_combat",
    "played_game_share_exploration",
    "game_share_combat",
    "game_share_exploration"
]

# Valores aproximados do gráfico original
importances = [0.087, 0.078, 0.070, 0.068, 0.056, 0.051]

# Inverter para o maior ficar no topo no gráfico horizontal
features_plot = features[::-1]
importances_plot = importances[::-1]

plt.figure(figsize=(10, 5.5))
bars = plt.barh(features_plot, importances_plot)

plt.title("Random Forest V2 — Top 6 atributos por importância", fontsize=16)
plt.xlabel("Importância relativa", fontsize=12)
plt.ylabel("Atributo", fontsize=12)

# Colocar os valores ao lado das barras
for i, v in enumerate(importances_plot):
    plt.text(v + 0.001, i, f"{v:.3f}", va="center", fontsize=10)

plt.xlim(0, 0.095)
plt.tight_layout()

# Salva em PNG com boa qualidade para slide
plt.savefig("top6_importancia_rf_v2.png", dpi=300, bbox_inches="tight")
plt.show()