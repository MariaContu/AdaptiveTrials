# Executar a Frente de Inteligência Artificial

Este documento descreve como configurar e executar a frente de Inteligência Artificial do **Adaptive Trials**.

A versão atualmente utilizada pelo protótipo é a **V2 do modelo macro com sinais de recência**, responsável por classificar o perfil externo do jogador entre:

- `combat`;
- `exploration`;
- `strategic_reasoning`.

O modelo final utiliza informações históricas da biblioteca Steam combinadas com sinais de atividade recente.

---

## 1. Requisitos

Recomenda-se utilizar:

- Python 3.11 ou superior;
- ambiente virtual Python;
- acesso à internet para inferência com perfis reais da Steam;
- Steam Web API Key válida.

O projeto deve ser executado a partir da pasta:

```text
ai-model/
```

---

## 2. Criar e ativar o ambiente virtual

Caso o ambiente ainda não exista:

```bash
python3 -m venv .venv
```

No macOS ou Linux:

```bash
source .venv/bin/activate
```

No Windows:

```bash
.venv\Scripts\activate
```

---

## 3. Instalar as dependências

Com o ambiente virtual ativo:

```bash
pip install -r requirements.txt
```

Principais dependências:

- pandas;
- numpy;
- scikit-learn;
- imbalanced-learn;
- joblib;
- requests;
- python-dotenv;
- matplotlib;
- fastapi;
- uvicorn.

---

## 4. Configurar a Steam Web API Key

Crie um arquivo `.env` na raiz de `ai-model/`:

```env
STEAM_API_KEY=SUA_CHAVE_AQUI
```

O arquivo `.env` não deve ser versionado no Git.

---

## 5. Modelo final utilizado

Modelo:

```text
models/final/macro_model_recency_v2.joblib
```

Metadados:

```text
models/final/macro_model_recency_v2_metadata.json
```

Configuração:

```python
RandomForestClassifier(
    n_estimators=200,
    max_depth=12,
    min_samples_split=2,
    min_samples_leaf=1,
    max_features="sqrt",
    class_weight="balanced",
    random_state=42
)
```

O modelo utiliza **49 features**.

A V1 de 40 features permanece preservada em:

```text
models/final/macro_model_optimized.joblib
```

e deve ser tratada apenas como baseline histórico.

---

## 6. Features de recência

A V2 acrescenta nove sinais de recência:

```text
has_recent_activity
recent_total_playtime_minutes
recent_active_games
recent_categorized_playtime_minutes
recent_categorized_active_games
recent_category_playtime_coverage
recent_active_games_combat
recent_active_games_exploration
recent_active_games_strategic_reasoning
```

Os tempos recentes separados diretamente por categoria e suas proporções não são utilizados como entrada supervisionada, evitando circularidade parcial com a regra de construção do target.

---

## 7. Validar a reconstrução das features

Executar:

```bash
python3 src/09j_validate_recency_v2_feature_parity.py
```

Para um perfil específico:

```bash
python3 src/09j_validate_recency_v2_feature_parity.py \
  --player-id general_final_0003
```

Resultado esperado:

```text
Features compared: 49
Mismatches: 0
Parity: True
```

---

## 8. Inferência local

Executar:

```bash
python3 src/09k_predict_local_recency_v2.py
```

Para outro perfil:

```bash
python3 src/09k_predict_local_recency_v2.py \
  --player-id general_final_0001
```

A saída contém categoria prevista, probabilidades, target histórico quando disponível e resumo de atividade recente.

---

## 9. Inferência com perfil real da Steam

Script:

```text
src/09l_predict_live_steamid_recency_v2.py
```

Aceita:

- SteamID64;
- vanity ID;
- URL `steamcommunity.com/id/...`;
- URL `steamcommunity.com/profiles/...`.

Exemplo com SteamID64:

```bash
python3 src/09l_predict_live_steamid_recency_v2.py \
  76561198293759611
```

Exemplo com vanity ID:

```bash
python3 src/09l_predict_live_steamid_recency_v2.py \
  gvk0
```

Quando necessário, o identificador personalizado é resolvido para SteamID64 antes da consulta da biblioteca.

---

## 10. Critério mínimo de cobertura

A inferência externa exige:

```text
category_playtime_coverage >= 0.70
```

A cobertura recente não bloqueia a classificação.

Caso não exista atividade nas últimas duas semanas, as features recentes ficam em zero e a inferência utiliza principalmente o histórico.

---

## 11. Executar o serviço de inferência

Com o ambiente virtual ativo e dentro de `ai-model/`:

```bash
uvicorn --app-dir src "10a_inference_api:app" \
  --host 127.0.0.1 \
  --port 8001
```

Serviço:

```text
http://127.0.0.1:8001
```

---

## 12. Endpoints

### Health check

```http
GET /health
```

Exemplo:

```text
http://127.0.0.1:8001/health
```

### Predição

```http
POST /predict
```

URL:

```text
http://127.0.0.1:8001/predict
```

Body com SteamID64:

```json
{
    "steamId": "76561198293759611"
}
```

Body com vanity ID:

```json
{
    "steamId": "gvk0"
}
```

Principais campos retornados:

```text
status
steamId
steamIdentifierInput
steamIdentifierType
predicted_category
probabilities
profile_quality
library_summary
recent_summary
feature_count
feature_set
model_version
```

---

## 13. Swagger

Swagger:

```text
http://127.0.0.1:8001/docs
```

Rotas disponíveis:

```text
GET /health
POST /predict
```

ReDoc:

```text
http://127.0.0.1:8001/redoc
```

---

## 14. Exemplo simplificado de resposta

```json
{
    "status": "ok",
    "steamId": "76561198293759611",
    "steamIdentifierType": "steamid64",
    "predicted_category": "exploration",
    "probabilities": {
        "combat": 0.0683,
        "exploration": 0.7315,
        "strategic_reasoning": 0.2002
    },
    "profile_quality": {
        "category_game_coverage": 0.7857,
        "category_playtime_coverage": 0.8472
    },
    "feature_count": 49,
    "feature_set": "recency_counts_49",
    "model_version": "recency_v2"
}
```

Os valores podem mudar de acordo com o estado atual da biblioteca do jogador.

---

## 15. Interpretabilidade

Executar:

```bash
python3 src/09m_export_recency_v2_interpretability.py
```

Arquivos gerados:

```text
reports/figures/final_recency_v2_random_forest_tree_example.png
reports/figures/final_recency_v2_random_forest_feature_importance.png
reports/metrics/09m_recency_v2_interpretability_summary.json
```

A árvore representa apenas uma das 200 árvores da Random Forest e é truncada para facilitar a leitura. O gráfico de importância utiliza a floresta completa.

---

## 16. Principais scripts da V2

```text
09b_analyze_recency_signal.py
    auditoria da atividade recente

09c_build_recency_feature_set_v2.py
    definição das features de recência

09d_prepare_recency_ml_datasets.py
    preparação dos datasets V2 com o mesmo split da V1

09e_compare_v1_v2_recency_models.py
    comparação controlada V1 x V2

09f_optimize_recency_v2_rf.py
    otimização da Random Forest V2

09g_compare_v1_v2_seed_stability.py
    análise de estabilidade entre sementes

09h_compare_v1_v2_final_heldout.py
    comparação final no conjunto de teste

09i_fit_final_recency_v2_model.py
    refit nos 686 perfis resolvidos

09j_validate_recency_v2_feature_parity.py
    validação da reconstrução das 49 features

09k_predict_local_recency_v2.py
    inferência local

09l_predict_live_steamid_recency_v2.py
    inferência ao vivo com Steam

09m_export_recency_v2_interpretability.py
    exportação de interpretabilidade

10a_inference_api.py
    serviço HTTP de inferência
```

---

## 17. Fluxo de produção

```text
Identificador Steam informado
        ↓
SteamID64 ou vanity ID
        ↓
ResolveVanityURL, quando necessário
        ↓
GetOwnedGames
        ↓
tempo histórico + playtime_2weeks
        ↓
mapeamento dos jogos
        ↓
reconstrução das 49 features
        ↓
validação da cobertura histórica
        ↓
Random Forest V2
        ↓
probabilidades:
combat
exploration
strategic_reasoning
        ↓
API do Adaptive Trials
```

---

## 18. Observações importantes

- perfis privados ou indisponíveis não podem ser classificados por meio da biblioteca Steam;
- perfis com cobertura histórica inferior a 70% não devem utilizar diretamente a inferência externa;
- ausência de atividade recente não impede a classificação;
- o arquivo `.env` nunca deve ser versionado;
- a V1 deve ser preservada como baseline, mas não é mais o modelo oficial;
- o modelo atual é `macro_model_recency_v2.joblib`;
- métricas de generalização não devem ser recalculadas sobre os 686 perfis utilizados no refit final.
