# Adaptive Trials — AI Model

Esta pasta contém a frente de dados, aprendizado de máquina e inferência do **Adaptive Trials**, desenvolvida no contexto do Trabalho de Conclusão de Curso em Ciência da Computação.

O objetivo desta frente é construir um perfil externo de preferência do jogador a partir de dados públicos da Steam e disponibilizar esse perfil ao sistema híbrido de recomendação utilizado pelo protótipo.

---

## Visão geral

O Adaptive Trials trabalha com três dimensões principais de preferência:

- `combat`;
- `exploration`;
- `strategic_reasoning`.

A categoria `strategic_reasoning` representa raciocínio estratégico de forma ampla, incluindo lógica, planejamento, gerenciamento, tomada de decisão, estratégia e resolução de problemas.

No protótipo, essa dimensão é representada principalmente pelas missões identificadas tecnicamente como `Puzzle`.

A frente de Inteligência Artificial é responsável por:

- coletar e estruturar dados públicos da Steam;
- mapear jogos para as categorias do projeto;
- construir perfis de preferência dos jogadores;
- controlar possíveis fontes de viés e vazamento;
- comparar modelos supervisionados;
- selecionar e otimizar o modelo macro;
- incorporar sinais de atividade recente;
- disponibilizar inferência local e por perfil Steam;
- expor a inferência por meio de FastAPI.

---

## Dataset final

A base foi construída a partir de bibliotecas públicas da Steam.

A coleta final contém:

- 1.000 perfis válidos;
- 700 perfis provenientes da coleta geral diversificada;
- 300 perfis provenientes da coleta dirigida a raciocínio estratégico;
- 267.374 relações jogador-jogo;
- 24.484 AppIDs distintos observados nas bibliotecas;
- 13.259 AppIDs presentes no cache final de metadados;
- 96,29% de cobertura agregada por tempo de jogo.

A coleta utilizou proporção 70/30 entre amostragem geral e dirigida.

O jogo utilizado para localizar cada jogador foi tratado apenas como **ponto de entrada da coleta**, nunca como target.

---

## Dataset utilizado na modelagem macro

Para reduzir a influência do jogo utilizado como ponto de entrada, foi adotada como base principal a versão `source_excluded`, na qual esse título é removido antes da construção do perfil.

Dos 1.000 perfis válidos:

- 392 possuem target `combat`;
- 222 possuem target `exploration`;
- 72 possuem target `strategic_reasoning`;
- 314 permaneceram não resolvidos.

Assim, **686 perfis resolvidos** foram utilizados na modelagem supervisionada macro.

Distribuição:

```text
combat               392  (57,14%)
exploration          222  (32,36%)
strategic_reasoning   72  (10,50%)
```

Perfis não resolvidos foram preservados, mas não receberam target forçado.

---

## Controle de vazamento de dados

O target macro é definido com base na distribuição do tempo categorizado entre combate, exploração e raciocínio estratégico.

Por esse motivo, variáveis que permitiriam reconstruir diretamente essa regra foram removidas das entradas supervisionadas.

Entre as features excluídas estão:

- tempo por categoria;
- horas por categoria;
- proporções de tempo por categoria;
- categoria dominante;
- segunda maior categoria;
- dominância;
- diferença entre primeira e segunda categoria;
- entropia;
- informações temporais por subgrupo diretamente relacionadas ao target.

A primeira configuração controlada utilizou 34 features.

Posteriormente, seis proporções baseadas em **quantidade de jogos**, e não em tempo de jogo, foram adicionadas com segurança, formando a V1 com 40 features.

---

## Modelos avaliados

Foram comparados:

- K-Nearest Neighbors;
- Decision Tree;
- Random Forest;
- Gaussian Naive Bayes.

Também foram avaliadas diferentes estratégias de tratamento do desbalanceamento:

- dados originais;
- SMOTE;
- SMOTE combinado com undersampling;
- `class_weight`, quando suportado pelo algoritmo.

O conjunto de teste foi separado antes de qualquer balanceamento.

Durante a validação cruzada, técnicas de reamostragem foram aplicadas somente dentro dos folds de treino.

A seleção foi orientada principalmente por:

- macro F1;
- balanced accuracy;
- desempenho da classe minoritária;
- estabilidade entre sementes.

---

## V1 — baseline histórico

A primeira versão otimizada utilizou:

- Random Forest;
- 40 features;
- SMOTE;
- 200 árvores;
- `max_depth=10`;
- `min_samples_leaf=2`;
- `max_features="sqrt"`.

O artefato foi preservado em:

```text
models/final/macro_model_optimized.joblib
```

Essa versão permanece no projeto como baseline histórico de comparação.

---

## V2 — modelo macro final com recência

Após a conclusão da V1, foi avaliada a incorporação de sinais de atividade recente da Steam.

A informação recente utilizada deriva do campo `playtime_2weeks`, quando disponibilizado pela Steam.

A auditoria realizada sobre os 1.000 perfis identificou:

- 957 perfis com alguma atividade recente;
- 916 com atividade recente em jogos categorizados;
- 6.301 relações jogador-jogo recentes;
- 65,43% de cobertura agregada do tempo recente;
- concordância de 60,03% entre categoria recente dominante e target histórico nos casos comparáveis.

Esses resultados indicaram que a recência contém informação complementar ao histórico.

### Features finais

A V2 utiliza **49 features**:

- 40 features históricas da V1;
- 9 sinais de atividade recente.

As features recentes selecionadas são:

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

Tempos recentes separados diretamente por categoria e suas proporções não foram utilizados como entrada supervisionada, evitando circularidade parcial com a definição do target.

---

## Configuração final

O modelo final é:

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

Artefato:

```text
models/final/macro_model_recency_v2.joblib
```

Metadados:

```text
models/final/macro_model_recency_v2_metadata.json
```

Conjunto de features:

```text
recency_counts_49
```

---

## Avaliação da V2

A configuração final apresentou, em validação cruzada estratificada repetida:

```text
macro F1            0,6737
desvio-padrão       0,0562
balanced accuracy   0,6785
```

Na análise de estabilidade com as sementes 11, 21, 42, 73 e 101:

```text
V1 macro F1 médio   0,6490
V2 macro F1 médio   0,6652
diferença           +0,0162
```

A V2 apresentou macro F1 superior em todas as cinco sementes avaliadas.

Para raciocínio estratégico:

```text
V1 F1 médio         0,5053
V2 F1 médio         0,5193
```

A V2 também foi superior nas cinco sementes para essa classe.

---

## Avaliação final no conjunto de teste

A comparação final foi realizada sobre os mesmos 138 perfis mantidos fora do treinamento durante a seleção.

| Métrica | V1 | V2 |
|---|---:|---:|
| Accuracy | 0,6667 | **0,6884** |
| Balanced accuracy | 0,6210 | **0,6272** |
| Macro F1 | 0,5984 | **0,6260** |
| F1 combate | 0,7383 | **0,7692** |
| F1 exploração | **0,6452** | 0,6087 |
| F1 raciocínio estratégico | 0,4118 | **0,5000** |

A V2 apresentou melhora global e melhor F1 na classe minoritária.

Foi observada redução no F1 de exploração, mantida como trade-off conhecido da configuração final.

Após a escolha do modelo, a V2 foi novamente treinada utilizando os **686 perfis resolvidos**.

Esse refit possui finalidade de implantação e não é utilizado para gerar novas métricas de generalização.

---

## Interpretabilidade

A Random Forest final possui 200 árvores.

Foram gerados:

```text
reports/figures/final_recency_v2_random_forest_tree_example.png
reports/figures/final_recency_v2_random_forest_feature_importance.png
reports/metrics/09m_recency_v2_interpretability_summary.json
```

A árvore exportada representa somente uma árvore da floresta e é truncada visualmente para facilitar a leitura.

O gráfico de importância representa o modelo completo.

As dez features mais importantes da V2 foram:

```text
played_game_share_strategic_reasoning
game_share_strategic_reasoning
played_game_share_combat
played_game_share_exploration
game_share_combat
game_share_exploration
avg_playtime_per_played_game_minutes
category_playtime_coverage
recent_active_games_combat
category_game_coverage
```

As nove features relacionadas à recência representam aproximadamente **13,04% da importância total** da Random Forest.

---

## Inferência

A V2 possui três formas principais de validação e uso.

### Inferência local

```bash
python3 src/09k_predict_local_recency_v2.py
```

### Inferência com perfil real da Steam

```bash
python3 src/09l_predict_live_steamid_recency_v2.py   76561198293759611
```

Também são aceitos:

- SteamID64;
- vanity ID;
- URL `steamcommunity.com/id/...`;
- URL `steamcommunity.com/profiles/...`.

Exemplo:

```bash
python3 src/09l_predict_live_steamid_recency_v2.py   gvk0
```

Quando necessário, o identificador personalizado é resolvido para SteamID64 antes da consulta à biblioteca.

---

## Critério de cobertura para inferência externa

A classificação por perfil Steam exige:

```text
category_playtime_coverage >= 0.70
```

A cobertura recente não bloqueia a classificação.

Se não houver atividade registrada nas últimas duas semanas, as features recentes assumem zero e a inferência continua baseada principalmente no histórico.

Perfis privados ou indisponíveis não podem ser classificados a partir da biblioteca Steam.

---

## Serviço FastAPI

A inferência utilizada pela integração está disponível em:

```text
src/10a_inference_api.py
```

Executar:

```bash
uvicorn --app-dir src "10a_inference_api:app"   --host 127.0.0.1   --port 8001
```

Endpoints:

```text
GET  /health
POST /predict
```

Swagger:

```text
http://127.0.0.1:8001/docs
```

ReDoc:

```text
http://127.0.0.1:8001/redoc
```

Exemplo de requisição:

```json
{
  "steamId": "gvk0"
}
```

O campo `steamId` aceita tanto SteamID64 quanto identificador personalizado.

---

## Estrutura da pasta

```text
ai-model/
├── config/
│   ├── category_mapping.json
│   ├── category_mapping_v2.json
│   ├── final_collection_plan.json
│   ├── model_features_v1.json
│   ├── model_features_recency_v2.json
│   ├── source_games.json
│   └── strategic_reasoning_entry_games.json
├── data/
│   ├── raw/
│   ├── interim/
│   ├── processed/
│   └── modeling/
├── models/
│   ├── experimental/
│   └── final/
├── reports/
│   ├── figures/
│   └── metrics/
├── src/
├── tests/
├── .gitignore
├── README.md
└── requirements.txt
```

Os arquivos de dados, modelos e relatórios podem ser mantidos fora do versionamento conforme as regras definidas no `.gitignore`.

---

## Pipeline implementado

O pipeline atual inclui:

1. coleta inicial de SteamIDs;
2. consulta das bibliotecas públicas;
3. coleta de metadados;
4. mapeamento inicial das categorias;
5. revisão para raciocínio estratégico;
6. coleta complementar dirigida;
7. consolidação da coleta final;
8. expansão direcionada da cobertura de metadados;
9. construção dos perfis finais;
10. análise da influência do jogo de origem;
11. validação da viabilidade das classes;
12. preparação dos conjuntos de treino e teste;
13. comparação dos algoritmos;
14. otimização da V1;
15. validação de estabilidade;
16. análise e incorporação de recência;
17. comparação V1 × V2;
18. otimização da V2;
19. avaliação final em held-out;
20. refit para implantação;
21. validação da paridade das features;
22. inferência local;
23. inferência ao vivo pela Steam;
24. serviço FastAPI;
25. interpretabilidade.

---

## Scripts principais da etapa final

```text
src/07a_prepare_ml_datasets.py
src/07b_train_compare_models.py
src/07d_optimize_macro_model.py
src/07e_seed_stability.py
src/07f_fit_final_macro_model.py

src/09b_analyze_recency_signal.py
src/09c_build_recency_feature_set_v2.py
src/09d_prepare_recency_ml_datasets.py
src/09e_compare_v1_v2_recency_models.py
src/09f_optimize_recency_v2_rf.py
src/09g_compare_v1_v2_seed_stability.py
src/09h_compare_v1_v2_final_heldout.py
src/09i_fit_final_recency_v2_model.py
src/09j_validate_recency_v2_feature_parity.py
src/09k_predict_local_recency_v2.py
src/09l_predict_live_steamid_recency_v2.py
src/09m_export_recency_v2_interpretability.py

src/10a_inference_api.py
```

Scripts de versões anteriores permanecem no repositório para rastreabilidade metodológica e reprodução dos experimentos.

---

## Configuração do ambiente

Criar o ambiente virtual:

```bash
python3 -m venv .venv
```

Ativar no macOS ou Linux:

```bash
source .venv/bin/activate
```

Ativar no Windows:

```bash
.venv\Scripts\activate
```

Instalar dependências:

```bash
pip install -r requirements.txt
```

---

## Steam Web API

Crie um arquivo `.env` na raiz de `ai-model/`:

```env
STEAM_API_KEY=SUA_CHAVE_AQUI
```

O arquivo `.env` não deve ser versionado.

---

## Reprodutibilidade

O pipeline utiliza:

- scripts numerados;
- configurações versionadas;
- arquivos intermediários;
- seeds explícitas;
- separação de treino e teste antes do balanceamento;
- validação cruzada estratificada repetida;
- reamostragem apenas dentro dos folds de treino;
- artefatos e relatórios estruturados;
- preservação das versões anteriores para auditoria.

A divisão principal utiliza:

```text
random_state = 42
```

com 80% dos 686 perfis resolvidos para treino e 20% para teste.

---

## Documentação complementar

O guia de execução detalhado está em:

```text
docs/ai/EXECUTAR_IA.md
```

O diário técnico da frente está em:

```text
docs/ai/diario-ia.md
```

A documentação metodológica de construção do dataset e treinamento deve ser consultada em conjunto com os relatórios gerados em:

```text
reports/metrics/
```

---

## Estado atual

A etapa de construção, treinamento, seleção e implantação do modelo macro está **concluída para o escopo atual do TCC**.

A versão oficial utilizada pelo protótipo é:

```text
recency_v2
```

com:

```text
49 features
Random Forest
class_weight="balanced"
686 perfis no refit final
```

Novos ciclos de treinamento não são previstos no escopo atual, salvo se forem identificadas novas fontes de dados, problemas de consistência ou necessidade de reavaliação experimental.
