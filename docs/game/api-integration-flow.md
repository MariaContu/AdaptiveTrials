# Integração Godot ↔ API Backend

## Objetivo

Este documento descreve o fluxo de integração entre o protótipo desenvolvido em Godot e a API backend do projeto **Adaptive Trials**. A API será responsável por criar sessões, registrar preferências, solicitar recomendações de missão, receber eventos comportamentais e encerrar a sessão experimental.

A integração segue a estrutura prevista para o TCC II, mantendo o jogo como cliente da API e concentrando a lógica de recomendação, persistência e adaptação no backend.

---

## Visão geral do fluxo

O fluxo principal de comunicação entre o jogo e a API será:

1. O jogador inicia o protótipo.
2. O jogador escolhe o modo de jogo:
   - Modo Controle (`mode = 1`);
   - Modo Adaptativo (`mode = 2`).
3. O jogo cria uma sessão na API.
4. Se o modo for adaptativo, o jogo solicita preferências manuais ou Steam ID.
5. O jogo solicita a próxima recomendação de missão.
6. A API retorna a missão recomendada.
7. O jogo carrega a missão correspondente.
8. Ao finalizar a missão, o jogo registra um evento comportamental na API.
9. O ciclo se repete até completar 6 missões.
10. Ao final, o jogo encerra a sessão na API.
11. O jogo exibe o `playerId`, `sessionId` e modo jogado para preenchimento do questionário.

---

## Endpoints utilizados pelo Godot

### Criar sessão

```http
POST /api/sessions
```

Request:

```json
{
  "mode": 2
}
```

Valores possíveis:

| Valor | Modo       |
| ----- | ---------- |
| 1     | Controle   |
| 2     | Adaptativo |

Response esperado:

```json
{
  "sessionId": 1,
  "playerId": 1,
  "mode": 2,
  "status": 1,
  "startedAt": "2026-07-12T20:00:00Z"
}
```

O Godot deve armazenar em memória:

- `playerId`;
- `sessionId`;
- `mode`.

Esses dados serão usados nas próximas chamadas.

---

## Fluxo do modo controle

No modo controle (`mode = 1`), o jogo **não deve** solicitar preferências manuais nem Steam ID.

O fluxo deve ser:

1. Criar sessão com `mode = 1`.
2. Solicitar recomendações normalmente.
3. Executar missões.
4. Registrar eventos comportamentais.
5. Encerrar sessão.

A API garante que sessões do modo controle:

- não utilizam perfil manual;
- não utilizam perfil Steam;
- não utilizam comportamento para adaptar recomendações;
- mantêm distribuição fixa e equilibrada entre combate, exploração e quebra-cabeça;
- registram eventos apenas para análise posterior.

---

## Fluxo do modo adaptativo

No modo adaptativo (`mode = 2`), o jogo pode utilizar preferências manuais ou Steam ID para criar o perfil inicial do jogador.

O fluxo deve ser:

1. Criar sessão com `mode = 2`.
2. Perguntar se o jogador deseja usar Steam ID.
3. Se sim, chamar `POST /api/steam/import`.
4. Se não, chamar `POST /api/players/{playerId}/preferences`.
5. Solicitar recomendações.
6. Executar missões.
7. Registrar eventos comportamentais.
8. Encerrar sessão.

---

## Registrar preferências manuais

Endpoint:

```http
POST /api/players/{playerId}/preferences
```

Request:

```json
{
  "combat": 4,
  "exploration": 3,
  "puzzle": 3
}
```

A API normaliza os valores automaticamente. Assim, o Godot pode enviar valores em escala simples, como 0 a 10, sem precisar calcular proporções.

Response esperado:

```json
{
  "playerId": 1,
  "source": "manual",
  "combat": 0.4,
  "exploration": 0.3,
  "puzzle": 0.3,
  "createdAt": "2026-07-12T20:01:00Z"
}
```

Esse endpoint deve ser chamado apenas no modo adaptativo.

---

## Importar perfil Steam

Endpoint:

```http
POST /api/steam/import
```

Request:

```json
{
  "playerId": 1,
  "steamId": "76561198000000000"
}
```

Response esperado:

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
  "createdAt": "2026-07-12T20:01:00Z"
}
```

Atualmente, esse endpoint utiliza importação mockada. Em uma versão posterior, será substituído ou complementado pela integração real com a Steam Web API.

Esse endpoint deve ser chamado apenas no modo adaptativo.

---

## Solicitar próxima recomendação

Endpoint:

```http
POST /api/recommendations/next
```

Request:

```json
{
  "playerId": 1,
  "sessionId": 1
}
```

Response esperado:

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
    "combat": 0.3333333333,
    "exploration": 0.3333333333,
    "puzzle": 0.3333333333
  },
  "finalProbabilities": {
    "combat": 0.3866666667,
    "exploration": 0.3066666667,
    "puzzle": 0.3066666667
  },
  "weights": {
    "profile": 0.8,
    "behavior": 0.2
  },
  "reason": "Adaptive recommendation using probabilistic mission type selection and rule-based difficulty adjustment."
}
```

O Godot deve utilizar principalmente os campos:

- `recommendedMissionId`;
- `recommendedType`;
- `template`;
- `difficulty`;
- `targetDifficulty`.

Os demais campos podem ser ignorados pelo jogo, mas são úteis para depuração, análise e validação do TCC.

---

## Mapeamento de tipos de missão

| Valor | Tipo          |
| ----- | ------------- |
| 1     | Combate       |
| 2     | Exploração    |
| 3     | Quebra-cabeça |

No Godot, esse valor pode ser usado para direcionar o carregamento da missão.

Exemplo:

```txt
recommendedType = 1 → carregar missão de combate
recommendedType = 2 → carregar missão de exploração
recommendedType = 3 → carregar missão de quebra-cabeça
```

---

## Mapeamento de templates

O campo `template` indica qual lógica de missão deve ser carregada pelo jogo.

### Combate

| Template        | Uso no Godot                 |
| --------------- | ---------------------------- |
| Eliminar Alvo   | Criar inimigos para derrotar |
| Sobreviver      | Criar desafio por tempo      |
| Defender Objeto | Criar objeto a proteger      |

### Exploração

| Template          | Uso no Godot                          |
| ----------------- | ------------------------------------- |
| Encontrar Objetos | Espalhar itens pelo mapa              |
| Chegar ao Destino | Definir ponto final                   |
| Evitar Inimigos   | Criar rota com inimigos ou obstáculos |

### Quebra-cabeça

| Template          | Uso no Godot                       |
| ----------------- | ---------------------------------- |
| Repetir Sequência | Criar sequência de botões/símbolos |
| Conectar Pontos   | Criar puzzle de conexão            |
| Decifrar Código   | Criar puzzle com pistas e resposta |

---

## Registrar evento comportamental

Ao final de cada missão, o Godot deve enviar um evento para a API.

Endpoint:

```http
POST /api/sessions/{sessionId}/events
```

Request:

```json
{
  "missionId": 10,
  "completionTime": 75,
  "failures": 1,
  "success": true,
  "persistence": 0.8
}
```

Campos:

| Campo            | Descrição                                    |
| ---------------- | -------------------------------------------- |
| `missionId`      | Missão executada                             |
| `completionTime` | Tempo em segundos para concluir a missão     |
| `failures`       | Quantidade de falhas durante a missão        |
| `success`        | Indica se a missão foi concluída com sucesso |
| `persistence`    | Valor entre 0 e 1 representando persistência |

Response esperado:

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
  "createdAt": "2026-07-12T20:05:00Z"
}
```

---

## Cálculo inicial de persistência no Godot

Na primeira versão, a persistência pode ser calculada de forma simples.

Sugestão:

```txt
persistence = 1.0 - min(failures * 0.2, 0.8)
```

Exemplos:

| Falhas    | Persistência |
| --------- | ------------ |
| 0         | 1.0          |
| 1         | 0.8          |
| 2         | 0.6          |
| 3         | 0.4          |
| 4 ou mais | 0.2          |

Esse cálculo pode ser refinado depois, mas já permite registrar um valor consistente para a API.

---

## Encerrar sessão

Após completar as 6 missões previstas, o Godot deve encerrar a sessão.

Endpoint:

```http
POST /api/sessions/{sessionId}/end
```

Response esperado:

```json
{
  "sessionId": 1,
  "status": 2,
  "startedAt": "2026-07-12T20:00:00Z",
  "endedAt": "2026-07-12T20:30:00Z"
}
```

---

## Ciclo de 6 missões

Cada sessão experimental deve executar 6 missões.

Pseudofluxo:

```txt
create session

if mode == adaptive:
    register manual preferences or steam profile

for missionNumber in 1..6:
    request next recommendation
    load mission by template/type/difficulty
    play mission
    collect completionTime, failures, success, persistence
    register behavior event

end session
show sessionId to participant
```

---

## Dados que o Godot deve manter em memória

Durante a sessão, o Godot deve armazenar:

| Variável             | Descrição                                |
| -------------------- | ---------------------------------------- |
| `playerId`           | Identificador do jogador criado pela API |
| `sessionId`          | Identificador da sessão atual            |
| `mode`               | Modo escolhido pelo jogador              |
| `currentMissionId`   | ID da missão atual                       |
| `currentMissionType` | Tipo da missão atual                     |
| `currentTemplate`    | Template da missão atual                 |
| `currentDifficulty`  | Dificuldade real da missão atual         |
| `missionStartTime`   | Momento em que a missão começou          |
| `failures`           | Falhas acumuladas na missão              |
| `success`            | Resultado da missão                      |

---

## Tela final e questionário

Ao final da sessão, o jogo deve exibir uma tela com as informações necessárias para o questionário.

Campos recomendados:

```txt
PlayerId: 1
SessionId: 1
Modo: Adaptativo
```

No questionário, recomenda-se solicitar:

```txt
Código do participante
ID da sessão controle
ID da sessão adaptativa
Ordem de execução dos modos
```

Isso permitirá cruzar as respostas subjetivas do questionário com os dados objetivos exportados pela API.

---

## Diferença entre modo controle e modo adaptativo

| Comportamento                  | Modo Controle | Modo Adaptativo        |
| ------------------------------ | ------------- | ---------------------- |
| Cria sessão                    | Sim           | Sim                    |
| Solicita preferências          | Não           | Sim, se não usar Steam |
| Usa Steam                      | Não           | Opcional               |
| Solicita recomendações         | Sim           | Sim                    |
| Usa perfil na recomendação     | Não           | Sim                    |
| Usa comportamento para adaptar | Não           | Sim                    |
| Registra eventos               | Sim           | Sim                    |
| Ajusta dificuldade             | Não           | Sim                    |
| Exporta dados                  | Sim           | Sim                    |

---

## Observações técnicas

- A API deve estar em execução antes de iniciar o protótipo no Godot.
- Em ambiente local, a URL base esperada é algo como `http://localhost:PORTA` ou `https://localhost:PORTA`.
- O Godot deve tratar erros de conexão com a API.
- O jogo não deve acessar diretamente o banco de dados.
- O jogo não deve implementar a lógica adaptativa internamente.
- Toda recomendação deve ser solicitada à API.
- Todo evento comportamental deve ser enviado à API.

---

## Critério de aceite da integração

A integração Godot ↔ API será considerada preparada quando:

- o fluxo de chamadas estiver documentado;
- o Godot souber quais endpoints chamar;
- os formatos de request e response estiverem definidos;
- o ciclo de 6 missões estiver especificado;
- a diferença entre modo controle e adaptativo estiver clara;
- os dados necessários para o questionário estiverem definidos.
