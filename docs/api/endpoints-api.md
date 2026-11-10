# Endpoints — API Backend

Este documento resume os principais endpoints da API Backend do **Adaptive Trials**.

---

## 1. Sessions

### Criar sessão

```http
POST /api/sessions
```

Cria uma nova sessão de jogo. Quando nenhum `playerId` é informado, a API cria um jogador anônimo.

Exemplo:

```json
{
  "playerId": 1,
  "mode": 2
}
```

Modos:

```text
1 = Control
2 = Adaptive
```

---

### Encerrar sessão

```http
POST /api/sessions/{sessionId}/end
```

Finaliza uma sessão existente, atualizando seu status para `Finished`.

---

### Consultar sessão

```http
GET /api/sessions/{sessionId}
```

Retorna os dados resumidos de uma sessão.

---

### Consultar eventos da sessão

```http
GET /api/sessions/{sessionId}/events
```

Retorna os eventos comportamentais registrados em uma sessão.

---

### Consultar recomendações da sessão

```http
GET /api/sessions/{sessionId}/recommendations
```

Retorna as recomendações geradas durante uma sessão.

---

## 2. Missions

### Listar missões

```http
GET /api/missions
```

Retorna o catálogo de missões disponíveis.

---

### Consultar missão

```http
GET /api/missions/{missionId}
```

Retorna os detalhes de uma missão específica.

---

## 3. Behavior Events

### Registrar evento comportamental

```http
POST /api/sessions/{sessionId}/events
```

Registra o resultado de uma missão executada pelo jogador.

Exemplo:

```json
{
  "missionId": 16,
  "completionTime": 42.5,
  "failures": 1,
  "success": true,
  "persistence": 0.8
}
```

A API busca automaticamente o tipo, template e dificuldade da missão com base no `missionId`.

---

## 4. Players

### Consultar perfil do jogador

```http
GET /api/players/{playerId}/profile
```

Retorna o perfil normalizado do jogador.

---

### Registrar preferências manuais

```http
POST /api/players/{playerId}/preferences
```

Registra preferências manuais quando o jogador não utiliza SteamID ou quando não há perfil externo disponível.

Exemplo:

```json
{
  "combat": 4,
  "exploration": 3,
  "puzzle": 2
}
```

A API normaliza automaticamente os valores para soma 1.

---

### Listar sessões do jogador

```http
GET /api/players/{playerId}/sessions
```

Retorna o histórico de sessões associadas ao jogador.

---

## 5. Steam / IA

### Importar perfil externo

```http
POST /api/steam/import
```

Recebe um SteamID e tenta gerar um perfil externo por meio do serviço de IA.

Exemplo:

```json
{
  "playerId": 1,
  "steamId": "76561198293759611"
}
```

Com a IA ativa, resultado esperado:

```json
{
  "source": "steam_ai",
  "combat": 0.05828407224958949,
  "exploration": 0.7764778325123152,
  "puzzle": 0.16523809523809527
}
```

Com a IA desligada e fallback ativo, resultado esperado:

```json
{
  "source": "steam_mock"
}
```

Observação: o serviço de IA utiliza a categoria `strategic_reasoning`. A API converte essa categoria para `puzzle`, mantendo compatibilidade com o jogo e o catálogo de missões.

---

## 6. Recommendations

### Solicitar próxima recomendação

```http
POST /api/recommendations/next
```

Gera a próxima recomendação de missão para uma sessão.

Exemplo:

```json
{
  "playerId": 1,
  "sessionId": 1
}
```

A resposta contém:

- missão recomendada;
- tipo recomendado;
- dificuldade;
- dificuldade-alvo;
- probabilidades do perfil;
- probabilidades comportamentais;
- probabilidades finais;
- pesos utilizados;
- justificativa da recomendação.

No modo adaptativo, a recomendação utiliza:

```text
Pfinal = X * Pperfil + Y * Pcomportamento
```

No modo controle, a recomendação utiliza distribuição fixa e equilibrada, sem adaptação por perfil ou comportamento.

---

### Consultar distribuição comportamental

```http
GET /api/recommendations/behavior-distribution/{sessionId}
```

Retorna a distribuição comportamental calculada a partir dos eventos registrados na sessão.

---

## 7. Exports

### Exportar sessões

```http
GET /api/exports/sessions
```

Retorna sessões registradas em formato JSON.

---

### Exportar eventos

```http
GET /api/exports/events
```

Retorna eventos comportamentais registrados em formato JSON.

---

### Exportar recomendações

```http
GET /api/exports/recommendations
```

Retorna recomendações geradas em formato JSON.
