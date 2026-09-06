# Integração API Backend + Inteligência Artificial

Este documento descreve a integração entre a API Backend em .NET e o serviço de Inteligência Artificial do projeto **Adaptive Trials**.

---

## 1. Visão geral

A API Backend atua como intermediária entre o jogo em Godot, o banco de dados SQLite e o serviço de IA.

Fluxo geral:

```text
Godot
  ↓
API .NET
  ↓
Serviço IA / FastAPI
  ↓
Modelo Random Forest V2
  ↓
API .NET
  ↓
Godot
```

O jogo não chama a IA diretamente. Toda comunicação externa passa pela API, preservando a separação entre protótipo, persistência e inferência.

---

## 2. Serviço de IA

O serviço de IA é executado na pasta `ai-model/` com o comando:

```bash
uvicorn --app-dir src "10a_inference_api:app" \
  --host 127.0.0.1 \
  --port 8001
```

Endereço local:

```text
http://127.0.0.1:8001
```

Endpoints principais:

```http
GET /health
POST /predict
```

---

## 3. Configuração da API

Na API .NET, a integração é configurada em:

```text
api/src/AdaptiveTrials.Api/appsettings.json
```

Configuração:

```json
{
  "AiService": {
    "BaseUrl": "http://127.0.0.1:8001",
    "UseFallbackWhenUnavailable": true
  }
}
```

---

## 4. Endpoint usado na API

A integração é acionada pelo endpoint:

```http
POST /api/steam/import
```

Body:

```json
{
  "playerId": 1,
  "steamId": "76561198293759611"
}
```

A API recebe o `SteamId`, chama o serviço de IA e salva o resultado em `NormalizedProfile`.

---

## 5. Resposta esperada da IA

Exemplo simplificado de resposta do serviço FastAPI:

```json
{
  "status": "ok",
  "steamId": "76561198293759611",
  "predicted_category": "exploration",
  "probabilities": {
    "combat": 0.05828407224958949,
    "exploration": 0.7764778325123152,
    "strategic_reasoning": 0.16523809523809527
  },
  "library_summary": {
    "num_games": 141,
    "num_played_games": 129,
    "total_playtime_hours": 1961.8666666666666,
    "games_combat": 13,
    "games_exploration": 55,
    "games_strategic_reasoning": 42,
    "hours_combat": 102.41666666666667,
    "hours_exploration": 1205.4833333333333,
    "hours_strategic_reasoning": 342.73333333333335
  },
  "feature_count": 49,
  "feature_set": "recency_counts_49",
  "model_version": "recency_v2"
}
```

---

## 6. Conversão de categorias

O modelo de IA utiliza a categoria:

```text
strategic_reasoning
```

A API e o jogo utilizam:

```text
puzzle
```

Por isso, a API realiza a conversão:

```text
strategic_reasoning → puzzle
```

Essa decisão mantém a compatibilidade com o catálogo de missões e com o vocabulário definido para o protótipo em Godot.

---

## 7. Dados salvos no NormalizedProfile

Quando a resposta da IA é válida, a API salva o perfil com:

```text
source = steam_ai
```

Campos principais:

- `Combat`;
- `Exploration`;
- `Puzzle`;
- `TotalPlaytime`;
- `NumGames`;
- `GamesCombat`;
- `GamesExploration`;
- `GamesPuzzle`;
- `HoursCombat`;
- `HoursExploration`;
- `HoursPuzzle`;
- `Diversity`;
- `Entropy`;
- `Dominance`;
- `SecondMax`;
- `Gap`.

---

## 8. Fallback

Caso o serviço de IA esteja indisponível, a API utiliza fallback quando a configuração abaixo está ativa:

```json
{
  "UseFallbackWhenUnavailable": true
}
```

Nesse caso, a API gera um perfil simulado com:

```text
source = steam_mock
```

Esse fallback foi mantido para garantir que o protótipo continue funcionando localmente mesmo quando o serviço de IA não estiver em execução.

---

## 9. Uso na recomendação

Após o perfil ser salvo, o endpoint de recomendação utiliza o `NormalizedProfile` como `Pperfil`.

Endpoint:

```http
POST /api/recommendations/next
```

Exemplo de resposta validada:

```json
{
  "profileProbabilities": {
    "combat": 0.05828407224958949,
    "exploration": 0.7764778325123152,
    "puzzle": 0.16523809523809527
  },
  "behaviorProbabilities": {
    "combat": 0.3333333333333333,
    "exploration": 0.3333333333333333,
    "puzzle": 0.3333333333333333
  },
  "weights": {
    "profile": 0.8,
    "behavior": 0.2
  }
}
```

Esse resultado confirma que a recomendação adaptativa utiliza o perfil retornado pela IA.

---

## 10. Modo controle

O modo controle permanece isolado da IA.

Em sessões `GameMode.Control`, a API:

- não utiliza perfil manual;
- não utiliza perfil Steam/IA;
- não utiliza comportamento para adaptar em tempo real;
- utiliza distribuição fixa e equilibrada;
- continua registrando eventos apenas para análise posterior.

Essa separação preserva o papel metodológico do modo controle no protótipo.
