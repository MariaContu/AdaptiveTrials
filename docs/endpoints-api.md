# Documentação dos Endpoints da API — Adaptive Trials

Este documento apresenta os principais endpoints da API backend do projeto **Adaptive Trials**, desenvolvida para intermediar a comunicação entre o protótipo em Godot, a persistência dos dados, os perfis dos jogadores e o sistema de recomendação de missões.

A API utiliza comunicação HTTP com dados em formato JSON. Durante o desenvolvimento, os endpoints podem ser testados pela interface do Swagger.

## Base URL local

```txt
https://localhost:<porta>
```

ou, dependendo da configuração local:

```txt
http://localhost:<porta>
```

A porta exata aparece no terminal ao executar a API com:

```bash
dotnet run --project src/AdaptiveTrials.Api
```

## Observação sobre modos de jogo

A API trabalha com dois modos principais de sessão:

```txt
1 = Control
2 = Adaptive
```

No modo **Control**, a recomendação utiliza uma distribuição fixa e equilibrada entre combate, exploração e quebra-cabeça. Esse modo não utiliza perfil manual, perfil Steam ou comportamento da sessão para adaptar recomendações.

No modo **Adaptive**, a recomendação utiliza o perfil inicial do jogador, dados comportamentais coletados durante a sessão, seleção probabilística de missão e ajuste de dificuldade baseado em regras na versão atual da API.

---

# 1. Sessões

## Criar sessão

```http
POST /api/sessions
```

Cria uma nova sessão de jogo. Caso nenhum `playerId` seja informado, a API cria um jogador anônimo para os testes iniciais.

### Request

```json
{
  "mode": 2
}
```

Também é possível informar um jogador existente:

```json
{
  "playerId": 1,
  "mode": 2
}
```

### Response

```json
{
  "sessionId": 1,
  "playerId": 1,
  "mode": 2,
  "status": 1,
  "startedAt": "2026-07-12T20:00:00Z"
}
```

---

## Encerrar sessão

```http
POST /api/sessions/{sessionId}/end
```

Encerra uma sessão iniciada, atualizando seu status para `Finished`.

### Response

```json
{
  "sessionId": 1,
  "status": 2,
  "startedAt": "2026-07-12T20:00:00Z",
  "endedAt": "2026-07-12T20:25:00Z"
}
```

---

## Consultar sessão

```http
GET /api/sessions/{sessionId}
```

Retorna um resumo da sessão, incluindo modo, status, início, fim, total de eventos e total de recomendações.

### Response

```json
{
  "sessionId": 1,
  "playerId": 1,
  "mode": 2,
  "status": 1,
  "startedAt": "2026-07-12T20:00:00Z",
  "endedAt": null,
  "totalEvents": 3,
  "totalRecommendations": 4
}
```

---

## Consultar eventos de uma sessão

```http
GET /api/sessions/{sessionId}/events
```

Retorna os eventos comportamentais registrados durante uma sessão.

### Response

```json
[
  {
    "eventId": 1,
    "sessionId": 1,
    "missionId": 10,
    "missionType": 2,
    "template": "Encontrar Objetos",
    "difficulty": 3,
    "completionTime": 75,
    "failures": 1,
    "success": true,
    "persistence": 0.8,
    "createdAt": "2026-07-12T20:10:00Z"
  }
]
```

---

## Consultar recomendações de uma sessão

```http
GET /api/sessions/{sessionId}/recommendations
```

Retorna as recomendações geradas durante uma sessão.

### Response

```json
[
  {
    "recommendationId": 1,
    "sessionId": 1,
    "missionId": 10,
    "missionName": "Exploração Média",
    "recommendedType": 2,
    "recommendedDifficulty": 3,
    "combatProbability": 0.25,
    "explorationProbability": 0.55,
    "puzzleProbability": 0.2,
    "profileWeight": 0.8,
    "behaviorWeight": 0.2,
    "reason": "Adaptive recommendation using probabilistic mission type selection and rule-based difficulty adjustment.",
    "createdAt": "2026-07-12T20:09:00Z"
  }
]
```

---

# 2. Missões

## Listar missões disponíveis

```http
GET /api/missions
```

Retorna o catálogo inicial de missões cadastradas na API.

### Response

```json
[
  {
    "id": 1,
    "name": "Caça Simples",
    "type": 1,
    "template": "Eliminar Alvo",
    "difficulty": 1,
    "parametersJson": "{\"enemies\":3}",
    "description": "Derrotar pequenos inimigos."
  }
]
```

---

## Consultar missão por ID

```http
GET /api/missions/{missionId}
```

Retorna uma missão específica do catálogo.

### Response

```json
{
  "id": 1,
  "name": "Caça Simples",
  "type": 1,
  "template": "Eliminar Alvo",
  "difficulty": 1,
  "parametersJson": "{\"enemies\":3}",
  "description": "Derrotar pequenos inimigos."
}
```

---

# 3. Eventos comportamentais

## Registrar evento comportamental

```http
POST /api/sessions/{sessionId}/events
```

Registra o resultado da execução de uma missão durante uma sessão.

### Request

```json
{
  "missionId": 10,
  "completionTime": 75,
  "failures": 1,
  "success": true,
  "persistence": 0.8
}
```

### Response

```json
{
  "eventId": 1,
  "sessionId": 1,
  "missionId": 10,
  "missionType": 2,
  "template": "Encontrar Objetos",
  "difficulty": 3,
  "completionTime": 75,
  "failures": 1,
  "success": true,
  "persistence": 0.8,
  "createdAt": "2026-07-12T20:10:00Z"
}
```

### Observação

O jogo envia apenas o `missionId` e os dados de desempenho. A API busca automaticamente o tipo, o template e a dificuldade da missão no catálogo, evitando inconsistência nos dados.

---

# 4. Jogadores e perfis

## Registrar preferências manuais

```http
POST /api/players/{playerId}/preferences
```

Registra preferências manuais do jogador quando não forem utilizados dados da Steam.

### Request

```json
{
  "combat": 4,
  "exploration": 3,
  "puzzle": 3
}
```

A API normaliza automaticamente os valores para que a soma final seja igual a 1.

### Response

```json
{
  "playerId": 1,
  "source": "manual",
  "combat": 0.4,
  "exploration": 0.3,
  "puzzle": 0.3,
  "createdAt": "2026-07-12T20:05:00Z"
}
```

---

## Consultar perfil consolidado

```http
GET /api/players/{playerId}/profile
```

Retorna o perfil normalizado do jogador.

### Response

```json
{
  "playerId": 1,
  "source": "manual",
  "combat": 0.4,
  "exploration": 0.3,
  "puzzle": 0.3,
  "createdAt": "2026-07-12T20:05:00Z"
}
```

---

## Consultar sessões de um jogador

```http
GET /api/players/{playerId}/sessions
```

Retorna as sessões associadas a um jogador, permitindo identificar quais sessões foram jogadas em modo controle e quais foram jogadas em modo adaptativo.

### Response

```json
[
  {
    "sessionId": 1,
    "mode": 1,
    "status": 2,
    "startedAt": "2026-07-12T19:00:00Z",
    "endedAt": "2026-07-12T19:20:00Z",
    "totalEvents": 6,
    "totalRecommendations": 6
  },
  {
    "sessionId": 2,
    "mode": 2,
    "status": 2,
    "startedAt": "2026-07-12T19:30:00Z",
    "endedAt": "2026-07-12T19:55:00Z",
    "totalEvents": 6,
    "totalRecommendations": 6
  }
]
```

---

# 5. Steam

## Importar perfil Steam mockado

```http
POST /api/steam/import
```

Importa um perfil Steam simulado. Esta versão ainda não realiza chamada real para a Steam Web API; ela prepara o fluxo para a futura integração real.

### Request

```json
{
  "playerId": 1,
  "steamId": "76561198000000000"
}
```

### Response

```json
{
  "playerId": 1,
  "steamId": "76561198000000000",
  "source": "steam_mock",
  "combat": 0.6,
  "exploration": 0.25,
  "puzzle": 0.15,
  "totalPlaytime": 420,
  "numGames": 18,
  "gamesCombat": 10,
  "gamesExploration": 5,
  "gamesPuzzle": 3,
  "hoursCombat": 260,
  "hoursExploration": 110,
  "hoursPuzzle": 50,
  "createdAt": "2026-07-12T20:05:00Z"
}
```

### Observação

Na versão atual, o endpoint utiliza dados mockados. A integração real com a Steam Web API será implementada posteriormente.

---

# 6. Recomendações

## Solicitar próxima recomendação

```http
POST /api/recommendations/next
```

Gera a próxima recomendação de missão para uma sessão.

### Request

```json
{
  "playerId": 1,
  "sessionId": 2
}
```

### Response — modo adaptativo

```json
{
  "recommendationId": 1,
  "recommendedMissionId": 10,
  "missionName": "Exploração Média",
  "recommendedType": 2,
  "template": "Encontrar Objetos",
  "difficulty": 3,
  "targetDifficulty": 3,
  "profileProbabilities": {
    "combat": 0.4,
    "exploration": 0.3,
    "puzzle": 0.3
  },
  "behaviorProbabilities": {
    "combat": 0.33,
    "exploration": 0.33,
    "puzzle": 0.33
  },
  "finalProbabilities": {
    "combat": 0.38,
    "exploration": 0.31,
    "puzzle": 0.31
  },
  "weights": {
    "profile": 0.8,
    "behavior": 0.2
  },
  "reason": "Adaptive recommendation using probabilistic mission type selection and rule-based difficulty adjustment."
}
```

### Response — modo controle

```json
{
  "recommendationId": 1,
  "recommendedMissionId": 1,
  "missionName": "Caça Simples",
  "recommendedType": 1,
  "template": "Eliminar Alvo",
  "difficulty": 1,
  "targetDifficulty": 1,
  "profileProbabilities": {
    "combat": 0.3333333333333333,
    "exploration": 0.3333333333333333,
    "puzzle": 0.3333333333333333
  },
  "behaviorProbabilities": {
    "combat": 0.3333333333333333,
    "exploration": 0.3333333333333333,
    "puzzle": 0.3333333333333333
  },
  "finalProbabilities": {
    "combat": 0.3333333333333333,
    "exploration": 0.3333333333333333,
    "puzzle": 0.3333333333333333
  },
  "weights": {
    "profile": 0,
    "behavior": 0
  },
  "reason": "Control mode recommendation using fixed balanced distribution."
}
```

### Observações

No modo controle, a recomendação não utiliza perfil nem comportamento para adaptar o resultado. No modo adaptativo, a API combina perfil inicial e distribuição comportamental, utilizando a fórmula:

```txt
Pfinal = X * Pperfil + Y * Pcomportamento
```

---

## Consultar distribuição comportamental

```http
GET /api/recommendations/behavior-distribution/{sessionId}
```

Retorna a distribuição comportamental calculada a partir dos eventos registrados em uma sessão.

### Response

```json
{
  "combat": 0.45,
  "exploration": 0.35,
  "puzzle": 0.2,
  "totalEvents": 5
}
```

---

# 7. Exportação de dados experimentais

## Exportar sessões

```http
GET /api/exports/sessions
```

Retorna as sessões registradas em formato organizado para análise posterior.

---

## Exportar eventos

```http
GET /api/exports/events
```

Retorna os eventos comportamentais registrados durante as sessões.

---

## Exportar recomendações

```http
GET /api/exports/recommendations
```

Retorna as recomendações geradas durante as sessões.

---

# 8. Códigos de enumeração

## GameMode

```txt
1 = Control
2 = Adaptive
```

## SessionStatus

```txt
1 = Started
2 = Finished
3 = Abandoned
```

## MissionType

```txt
1 = Combat
2 = Exploration
3 = Puzzle
```

---

# 9. Fluxo recomendado para teste manual

## Fluxo controle

```txt
1. Criar sessão com mode = 1
2. Solicitar recomendação
3. Executar missão no jogo
4. Registrar evento
5. Repetir até concluir a sessão
6. Encerrar sessão
7. Exportar dados
```

## Fluxo adaptativo

```txt
1. Criar sessão com mode = 2
2. Registrar preferências manuais ou importar Steam
3. Solicitar recomendação
4. Executar missão no jogo
5. Registrar evento
6. Solicitar nova recomendação
7. Repetir até concluir a sessão
8. Encerrar sessão
9. Exportar dados
```

---

# 10. Observações para o TCC II

A API atual possui uma recomendação adaptativa V1 baseada em regras. Essa versão valida o fluxo completo entre jogo, API, banco de dados e recomendação.

Posteriormente, a lógica rule-based poderá ser substituída ou complementada pelo modelo de IA treinado, mantendo a API como camada intermediária entre o protótipo em Godot e o sistema de recomendação.
