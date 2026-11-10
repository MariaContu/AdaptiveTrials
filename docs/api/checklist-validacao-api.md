# Checklist de Validação — API Backend

Este documento resume os testes manuais utilizados para validar a API Backend do **Adaptive Trials**.

---

## 1. Compilação

Comando:

```bash
cd api
dotnet build
```

Resultado esperado:

```text
Build succeeded.
```

Status:

```text
[x] Validado
```

---

## 2. Swagger

Executar API:

```bash
dotnet run --project src/AdaptiveTrials.Api
```

Acessar:

```text
http://localhost:5277/swagger
```

Resultado esperado:

```text
Swagger carregado com os endpoints da API.
```

Status:

```text
[x] Validado
```

---

## 3. Catálogo de missões

Endpoint:

```http
GET /api/missions
```

Resultado esperado:

```text
22 missões retornadas.
```

Distribuição esperada:

```text
8 combate
7 exploração
7 quebra-cabeça
```

Status:

```text
[x] Validado
```

---

## 4. Criação de sessão

Endpoint:

```http
POST /api/sessions
```

Exemplo:

```json
{
  "mode": 2
}
```

Resultado esperado:

```text
Sessão criada e jogador anônimo criado quando PlayerId não é informado.
```

Status:

```text
[x] Validado
```

---

## 5. Importação com IA ativa

Pré-condição: serviço de IA rodando em `http://127.0.0.1:8001`.

Endpoint:

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

Resultado esperado:

```json
{
  "source": "steam_ai"
}
```

Também deve preencher:

- probabilidades;
- total de horas;
- número de jogos;
- jogos por categoria;
- horas por categoria.

Status:

```text
[x] Validado
```

---

## 6. Conversão de categoria da IA

Entrada da IA:

```text
strategic_reasoning
```

Representação na API:

```text
puzzle
```

Resultado esperado:

```text
Probabilidade de strategic_reasoning salva em Puzzle.
```

Status:

```text
[x] Validado
```

---

## 7. Recomendação usando perfil da IA

Endpoint:

```http
POST /api/recommendations/next
```

Body:

```json
{
  "playerId": 1,
  "sessionId": 1
}
```

Resultado esperado:

```json
{
  "profileProbabilities": {
    "combat": 0.05828407224958949,
    "exploration": 0.7764778325123152,
    "puzzle": 0.16523809523809527
  }
}
```

Status:

```text
[x] Validado
```

---

## 8. Fallback com IA desligada

Pré-condição: serviço de IA parado.

Endpoint:

```http
POST /api/steam/import
```

Resultado esperado:

```json
{
  "source": "steam_mock"
}
```

Status:

```text
[x] Validado
```

---

## 9. Modo controle

Criar sessão com modo controle:

```json
{
  "mode": 1
}
```

Solicitar recomendação:

```http
POST /api/recommendations/next
```

Resultado esperado:

```text
Distribuição fixa e equilibrada.
Pesos de perfil e comportamento iguais a zero.
Sem uso de perfil manual, Steam ou IA.
```

Status:

```text
[x] Validado
```

---

## 10. Exportação de dados

Endpoints:

```http
GET /api/exports/sessions
GET /api/exports/events
GET /api/exports/recommendations
```

Resultado esperado:

```text
Dados retornados em JSON para análise posterior.
```

Status:

```text
[x] Validado
```

---

## Resultado final

```text
[x] API compilando
[x] Swagger disponível
[x] Sessões funcionando
[x] Missões funcionando
[x] Eventos funcionando
[x] Preferências manuais funcionando
[x] Perfil normalizado funcionando
[x] Integração com IA funcionando
[x] Fallback funcionando
[x] Recomendação adaptativa funcionando
[x] Modo controle isolado
[x] Exportação funcionando
```

A API Backend está concluída funcionalmente para o MVP do TCC II.
