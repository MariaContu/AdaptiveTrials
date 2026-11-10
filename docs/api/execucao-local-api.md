# Execução Local — API Backend

Este documento descreve como configurar e executar localmente a API Backend do projeto **Adaptive Trials**.

---

## 1. Requisitos

Recomenda-se utilizar:

- .NET SDK instalado;
- SQLite;
- terminal com acesso à pasta do projeto;
- serviço de IA em execução apenas para testar o fluxo `steam_ai`.

A API deve ser executada a partir da pasta:

```text
api/
```

---

## 2. Estrutura esperada

```text
AdaptiveTrials/
├── api/
│   └── src/
│       ├── AdaptiveTrials.Api/
│       ├── AdaptiveTrials.Application/
│       ├── AdaptiveTrials.Domain/
│       └── AdaptiveTrials.Infrastructure/
├── ai-model/
├── game/
└── docs/
```

---

## 3. Configuração da API

No arquivo:

```text
api/src/AdaptiveTrials.Api/appsettings.json
```

confirmar a connection string do SQLite:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=adaptive_trials.db"
  }
}
```

Também confirmar a configuração do serviço de IA:

```json
{
  "AiService": {
    "BaseUrl": "http://127.0.0.1:8001",
    "UseFallbackWhenUnavailable": true
  }
}
```

`UseFallbackWhenUnavailable = true` permite que a API utilize o perfil `steam_mock` quando o serviço de IA estiver desligado.

---

## 4. Restaurar dependências

Dentro da pasta `api/`:

```bash
dotnet restore
```

---

## 5. Compilar o projeto

```bash
dotnet build
```

Resultado esperado:

```text
Build succeeded.
```

---

## 6. Criar ou atualizar banco local

Caso o banco ainda não exista ou existam migrations pendentes:

```bash
dotnet ef database update \
  --project src/AdaptiveTrials.Infrastructure \
  --startup-project src/AdaptiveTrials.Api
```

Resultado esperado:

```text
Done.
```

---

## 7. Executar a API

```bash
dotnet run --project src/AdaptiveTrials.Api
```

Resultado esperado:

```text
Now listening on: http://localhost:5277
```

A porta pode variar conforme o ambiente local.

---

## 8. Acessar Swagger

Com a API rodando, acessar:

```text
http://localhost:5277/swagger
```

Rotas principais disponíveis:

- `POST /api/sessions`;
- `POST /api/sessions/{sessionId}/end`;
- `GET /api/missions`;
- `POST /api/sessions/{sessionId}/events`;
- `POST /api/players/{playerId}/preferences`;
- `POST /api/steam/import`;
- `POST /api/recommendations/next`;
- `GET /api/exports/sessions`;
- `GET /api/exports/events`;
- `GET /api/exports/recommendations`.

---

## 9. Executar com IA ativa

Para testar o fluxo real da IA, o serviço FastAPI deve estar rodando na porta `8001`.

Na pasta `ai-model/`:

```bash
uvicorn --app-dir src "10a_inference_api:app" \
  --host 127.0.0.1 \
  --port 8001
```

Resultado esperado:

```text
Application startup complete.
Uvicorn running on http://127.0.0.1:8001
```

Depois, na API .NET, testar:

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

---

## 10. Executar com IA desligada

Para testar fallback, parar o serviço de IA e manter a API .NET rodando.

Executar novamente:

```http
POST /api/steam/import
```

Resultado esperado:

```json
{
  "source": "steam_mock"
}
```

Esse comportamento confirma que a API continua funcional mesmo sem o serviço de IA ativo.
