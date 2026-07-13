# Execução Local da API — Adaptive Trials

Este documento descreve como executar localmente a API backend do projeto **Adaptive Trials**, desenvolvida para o TCC II. A API é responsável por intermediar a comunicação entre o protótipo em Godot, a persistência dos dados, os perfis de jogador, os eventos comportamentais e o sistema de recomendação.

## 1. Tecnologias utilizadas

- .NET Web API
- C#
- Entity Framework Core
- SQLite
- Swagger/OpenAPI
- Godot Engine, futuramente integrado por HTTP/JSON

## 2. Estrutura atual do projeto

A estrutura recomendada para o repositório é:

```txt
AdaptiveTrials/
├── api/
│   └── AdaptiveTrials.Api/
├── game/
├── ai-model/
├── docs/
│   └── execucao-local-api.md
└── README.md
```

No momento, a pasta `api/` concentra a implementação da API backend. As pastas `game/` e `ai-model/` serão utilizadas posteriormente para o protótipo Godot e para o modelo de recomendação.

## 3. Estrutura da API

A API foi organizada em camadas:

```txt
AdaptiveTrials.Api/
├── src/
│   ├── AdaptiveTrials.Api/
│   ├── AdaptiveTrials.Application/
│   ├── AdaptiveTrials.Domain/
│   └── AdaptiveTrials.Infrastructure/
└── AdaptiveTrials.sln
```

Função de cada camada:

- `AdaptiveTrials.Api`: controllers, configuração da aplicação, Swagger e injeção de dependências.
- `AdaptiveTrials.Application`: DTOs e interfaces de serviços.
- `AdaptiveTrials.Domain`: entidades e enums do domínio.
- `AdaptiveTrials.Infrastructure`: acesso ao banco, `AppDbContext` e implementação dos serviços.

## 4. Pré-requisitos

Antes de executar a API, é necessário ter instalado:

- .NET SDK 8 ou superior
- Git
- Visual Studio Code, Visual Studio ou Rider
- Extensão opcional para visualizar SQLite, como SQLite Viewer no VS Code

Para verificar a instalação do .NET:

```bash
dotnet --version
```

## 5. Clonar o repositório

```bash
git clone <URL_DO_REPOSITORIO>
cd AdaptiveTrials
```

Caso a API esteja dentro da pasta `api/`, acesse:

```bash
cd api/AdaptiveTrials.Api
```

Se a API ainda estiver diretamente na raiz do repositório atual, execute os comandos a partir da pasta onde está o arquivo `.sln`.

## 6. Restaurar dependências

Na pasta onde está o arquivo `AdaptiveTrials.sln`, execute:

```bash
dotnet restore
```

## 7. Configuração do banco SQLite

A API utiliza SQLite como banco local. A connection string fica em:

```txt
src/AdaptiveTrials.Api/appsettings.json
```

Exemplo:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=adaptive_trials.db"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

O arquivo `adaptive_trials.db` é gerado localmente após a aplicação das migrations.

## 8. Instalar ou atualizar a ferramenta de migrations

Caso ainda não tenha o `dotnet-ef` instalado:

```bash
dotnet tool install --global dotnet-ef
```

Caso já tenha instalado:

```bash
dotnet tool update --global dotnet-ef
```

## 9. Criar ou atualizar o banco de dados

Na pasta da solution, execute:

```bash
dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

Esse comando cria o banco SQLite e aplica as migrations existentes, incluindo o catálogo inicial de missões.

## 10. Executar a API

Para compilar o projeto:

```bash
dotnet build
```

Para executar a API:

```bash
dotnet run --project src/AdaptiveTrials.Api
```

Após iniciar, o terminal exibirá a URL local da API. Normalmente será algo como:

```txt
https://localhost:xxxx
http://localhost:xxxx
```

## 11. Acessar o Swagger

Com a API em execução, acesse no navegador:

```txt
https://localhost:xxxx/swagger
```

Substitua `xxxx` pela porta exibida no terminal.

O Swagger permite testar os endpoints da API diretamente pelo navegador.

## 12. Fluxo básico de teste pelo Swagger

### 12.1. Criar uma sessão

Endpoint:

```http
POST /api/sessions
```

Body para modo controle:

```json
{
  "mode": 1
}
```

Body para modo adaptativo:

```json
{
  "mode": 2
}
```

Observação:

- `1` representa o modo controle.
- `2` representa o modo adaptativo.

A resposta retorna `sessionId` e `playerId`.

### 12.2. Consultar catálogo de missões

Endpoint:

```http
GET /api/missions
```

Esse endpoint retorna as missões iniciais previstas no GDD, separadas entre combate, exploração e quebra-cabeça.

### 12.3. Registrar preferências manuais

Endpoint:

```http
POST /api/players/{playerId}/preferences
```

Exemplo de body:

```json
{
  "combat": 4,
  "exploration": 3,
  "puzzle": 3
}
```

A API normaliza os valores para que a soma final seja igual a 1.

Importante: preferências manuais são usadas apenas pelo modo adaptativo. Sessões em modo controle ignoram perfil manual, Steam e comportamento para fins de recomendação.

### 12.4. Gerar próxima recomendação

Endpoint:

```http
POST /api/recommendations/next
```

Exemplo de body:

```json
{
  "playerId": 1,
  "sessionId": 1
}
```

No modo controle, a recomendação usa distribuição fixa e equilibrada:

```txt
combat = 1/3
exploration = 1/3
puzzle = 1/3
```

No modo adaptativo, a recomendação combina:

```txt
Pfinal = X * Pperfil + Y * Pcomportamento
```

### 12.5. Registrar evento comportamental

Endpoint:

```http
POST /api/sessions/{sessionId}/events
```

Exemplo de body:

```json
{
  "missionId": 1,
  "completionTime": 48.5,
  "failures": 1,
  "success": true,
  "persistence": 0.85
}
```

Eventos são registrados tanto no modo controle quanto no modo adaptativo. No modo controle, esses eventos servem apenas para análise posterior e não afetam a recomendação.

### 12.6. Encerrar sessão

Endpoint:

```http
POST /api/sessions/{sessionId}/end
```

Esse endpoint finaliza a sessão e preenche o campo `EndedAt`.

## 13. Endpoints principais

### Sessões

```http
POST /api/sessions
POST /api/sessions/{sessionId}/end
GET /api/sessions/{sessionId}
GET /api/sessions/{sessionId}/events
GET /api/sessions/{sessionId}/recommendations
GET /api/players/{playerId}/sessions
```

### Missões

```http
GET /api/missions
GET /api/missions/{missionId}
```

### Jogadores e perfil

```http
POST /api/players/{playerId}/preferences
GET /api/players/{playerId}/profile
```

### Steam

```http
POST /api/steam/import
```

Nesta versão, o endpoint de Steam ainda pode funcionar com perfil mockado. A integração real com a Steam Web API será implementada em etapa posterior.

### Recomendações

```http
POST /api/recommendations/next
GET /api/recommendations/behavior-distribution/{sessionId}
```

### Exportação de dados experimentais

```http
GET /api/exports/sessions
GET /api/exports/events
GET /api/exports/recommendations
```

Esses endpoints retornam dados em JSON para apoiar a análise posterior do TCC II.

## 14. Diferença entre modo controle e modo adaptativo

### Modo 1 — Controle

O modo controle não utiliza adaptação em tempo real. Seu objetivo é funcionar como base de comparação experimental.

Comportamento esperado:

- Não usa perfil manual.
- Não usa perfil Steam.
- Não usa comportamento para alterar recomendações.
- Usa distribuição fixa e equilibrada entre os tipos de missão.
- Registra eventos comportamentais apenas para análise posterior.
- Salva recomendações no banco.

### Modo 2 — Adaptativo

O modo adaptativo utiliza perfil inicial e comportamento observado para recomendar missões.

Comportamento esperado:

- Pode usar preferências manuais.
- Pode usar perfil Steam, quando integrado.
- Usa eventos comportamentais registrados na sessão.
- Calcula distribuição comportamental.
- Combina perfil e comportamento.
- Seleciona categoria de missão de forma probabilística.
- Ajusta dificuldade por regras na V1.
- Salva recomendações no banco.

## 15. Versão atual da recomendação

A API atualmente possui uma recomendação adaptativa V1 baseada em regras.

Essa versão implementa:

- Perfil manual normalizado.
- Perfil Steam mockado.
- Distribuição comportamental por sessão.
- Combinação entre perfil e comportamento.
- Seleção probabilística de categoria.
- Seleção de missão disponível.
- Ajuste de dificuldade baseado em regras.

Em etapa posterior, essa lógica será substituída ou complementada pelo modelo de recomendação treinado, mantendo a API como intermediária entre o jogo e o sistema de IA.

## 16. Verificar dados no SQLite

Caso queira conferir os dados manualmente, abra o arquivo `adaptive_trials.db` em uma extensão SQLite ou use o terminal.

Exemplos de consultas:

```sql
SELECT * FROM Players;
SELECT * FROM Sessions;
SELECT * FROM Missions;
SELECT * FROM BehaviorEvents;
SELECT * FROM Recommendations;
SELECT * FROM NormalizedProfiles;
```

Consulta para verificar recomendações por sessão:

```sql
SELECT
    Id,
    SessionId,
    MissionId,
    RecommendedType,
    RecommendedDifficulty,
    CombatProbability,
    ExplorationProbability,
    PuzzleProbability,
    ProfileWeight,
    BehaviorWeight,
    Reason,
    CreatedAt
FROM Recommendations;
```

## 17. Problemas comuns

### Erro ao executar migration

Verifique se o comando está sendo executado na pasta da solution e se os projetos foram informados corretamente:

```bash
dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

### Swagger não abre

Confirme se a API está em execução e se a URL contém `/swagger`.

Exemplo:

```txt
https://localhost:7234/swagger
```

### Banco não aparece

Confirme se o comando de migration foi executado e se a connection string aponta para:

```txt
Data Source=adaptive_trials.db
```

### Missões não aparecem

Confirme se a migration do catálogo de missões foi aplicada. O endpoint abaixo deve retornar o catálogo:

```http
GET /api/missions
```

## 18. Observações para o TCC II

Esta API representa a base operacional do protótipo do TCC II. Ela permite executar sessões, registrar comportamento, gerar recomendações e exportar dados experimentais.

A versão atual é adequada para validar o fluxo completo:

```txt
Godot → API → recomendação → Godot → evento comportamental → API → exportação/análise
```

Posteriormente, a integração com o modelo de IA e com o protótipo Godot será adicionada mantendo esse mesmo fluxo arquitetural.
