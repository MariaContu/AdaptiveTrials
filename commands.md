# Comandos Padrões — API Backend TCC II

Este arquivo reúne os comandos mais usados durante o desenvolvimento da API Backend do TCC II.

Projeto: `AdaptiveTrials.Api`  
Stack: `.NET Web API + Entity Framework Core + SQLite`

---

## 1. Rodar a API

Executar a API a partir da raiz da solução:

```bash
dotnet run --project src/AdaptiveTrials.Api
```

Depois, acessar o Swagger no navegador. Normalmente fica em um destes endereços:

```txt
https://localhost:xxxx/swagger
http://localhost:xxxx/swagger
```

O número da porta aparece no terminal quando a API inicia.

---

## 2. Build do projeto

Usar antes de testar ou commitar:

```bash
dotnet build
```

Se aparecer `Build succeeded`, está tudo certo para testar.

---

## 3. Criar uma nova branch

Fluxo padrão antes de começar uma nova tarefa:

```bash
git checkout main
git pull
git checkout -b nome-da-branch
```

Exemplo:

```bash
git checkout main
git pull
git checkout -b feat/export-csv
```

---

## 4. Verificar alterações no Git

```bash
git status
```

---

## 5. Commit e push

Depois de testar a tarefa:

```bash
git status
git add .
git commit -m "feat: descricao da tarefa"
git push origin nome-da-branch
```

Exemplo:

```bash
git add .
git commit -m "feat: add experimental data export endpoints"
git push origin feat/experimental-data-export
```

---

## 6. Instalar ou atualizar o dotnet-ef

Instalar:

```bash
dotnet tool install --global dotnet-ef
```

Atualizar:

```bash
dotnet tool update --global dotnet-ef
```

Verificar versão:

```bash
dotnet ef --version
```

---

## 7. Criar uma migration

Usar quando alguma entidade ou configuração do banco mudar.

```bash
dotnet ef migrations add NomeDaMigration --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

Exemplo:

```bash
dotnet ef migrations add InitialCreate --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

---

## 8. Aplicar migrations no banco

```bash
dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

---

## 9. Remover a última migration

Usar quando a migration foi criada errado e ainda não deve ser mantida.

```bash
dotnet ef migrations remove --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

---

## 10. Apagar o banco `.db` e recriar do zero

Usar quando os dados de teste não importam e é melhor recriar o banco limpo.

### Passo 1 — parar a API

No terminal onde a API está rodando:

```bash
Ctrl + C
```

### Passo 2 — apagar o arquivo do banco

Normalmente o banco fica em:

```txt
src/AdaptiveTrials.Api/adaptive_trials.db
```

Comando:

```bash
rm src/AdaptiveTrials.Api/adaptive_trials.db
```

Se também existirem arquivos auxiliares do SQLite:

```bash
rm src/AdaptiveTrials.Api/adaptive_trials.db-shm
rm src/AdaptiveTrials.Api/adaptive_trials.db-wal
```

Ou tudo de uma vez:

```bash
rm src/AdaptiveTrials.Api/adaptive_trials.db*
```

### Passo 3 — recriar o banco aplicando as migrations

```bash
dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

### Passo 4 — rodar a API novamente

```bash
dotnet run --project src/AdaptiveTrials.Api
```

---

## 11. Fluxo completo após apagar o `.db`

Comando padrão completo:

```bash
rm src/AdaptiveTrials.Api/adaptive_trials.db*
dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
dotnet run --project src/AdaptiveTrials.Api
```

---

## 12. Conferir dados no SQLite pelo terminal

Entrar no banco:

```bash
sqlite3 src/AdaptiveTrials.Api/adaptive_trials.db
```

Listar tabelas:

```sql
.tables
```

Consultar jogadores:

```sql
SELECT * FROM Players;
```

Consultar sessões:

```sql
SELECT * FROM Sessions;
```

Consultar missões:

```sql
SELECT * FROM Missions;
```

Consultar eventos comportamentais:

```sql
SELECT * FROM BehaviorEvents;
```

Consultar recomendações:

```sql
SELECT * FROM Recommendations;
```

Consultar perfis normalizados:

```sql
SELECT * FROM NormalizedProfiles;
```

Sair do SQLite:

```sql
.exit
```

---

## 13. Consultas úteis no SQLite

Contar missões cadastradas:

```sql
SELECT COUNT(*) FROM Missions;
```

Ver sessões com totais:

```sql
SELECT Id, PlayerId, Mode, Status, StartedAt, EndedAt
FROM Sessions;
```

Ver eventos de uma sessão:

```sql
SELECT Id, SessionId, MissionId, MissionType, Difficulty, CompletionTime, Failures, Success, Persistence
FROM BehaviorEvents
WHERE SessionId = 1;
```

Ver recomendações de uma sessão:

```sql
SELECT Id, SessionId, MissionId, RecommendedType, RecommendedDifficulty, CombatProbability, ExplorationProbability, PuzzleProbability, ProfileWeight, BehaviorWeight
FROM Recommendations
WHERE SessionId = 1;
```

Ver perfil normalizado:

```sql
SELECT PlayerId, Source, Combat, Exploration, Puzzle, TotalPlaytime, NumGames, Diversity, Entropy, Dominance, SecondMax, Gap
FROM NormalizedProfiles;
```

---

## 14. Pacotes principais do projeto

Caso precise reinstalar os pacotes do Entity Framework e SQLite:

```bash
dotnet add src/AdaptiveTrials.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/AdaptiveTrials.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/AdaptiveTrials.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/AdaptiveTrials.Api package Microsoft.EntityFrameworkCore.Design
```

---

## 15. Fluxo padrão para uma task nova

```bash
git checkout main
git pull
git checkout -b feat/nome-da-task

dotnet build
dotnet run --project src/AdaptiveTrials.Api

git status
git add .
git commit -m "feat: descricao da task"
git push origin feat/nome-da-task
```

---

## 16. Fluxo padrão quando há mudança no banco

```bash
dotnet build

dotnet ef migrations add NomeDaMigration --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api

dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api

dotnet run --project src/AdaptiveTrials.Api
```

---

## 17. Fluxo padrão quando a migration deu errado

Se a migration foi criada, mas ainda não deve ficar:

```bash
dotnet ef migrations remove --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

Depois ajustar o código e criar de novo:

```bash
dotnet ef migrations add NomeCorrigido --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api

dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
```

---

## 18. Fluxo padrão para testar a API do zero

```bash
rm src/AdaptiveTrials.Api/adaptive_trials.db*
dotnet ef database update --project src/AdaptiveTrials.Infrastructure --startup-project src/AdaptiveTrials.Api
dotnet run --project src/AdaptiveTrials.Api
```

Depois testar no Swagger:

```txt
POST /api/sessions
GET /api/missions
POST /api/players/{playerId}/preferences
POST /api/sessions/{sessionId}/events
POST /api/recommendations/next
GET /api/sessions/{sessionId}
GET /api/exports/sessions
GET /api/exports/events
GET /api/exports/recommendations
```

---

## 19. Endpoints principais já criados

### Sessões

```http
POST /api/sessions
POST /api/sessions/{sessionId}/end
GET /api/sessions/{sessionId}
GET /api/sessions/{sessionId}/events
GET /api/sessions/{sessionId}/recommendations
```

### Jogadores

```http
POST /api/players/{playerId}/preferences
GET /api/players/{playerId}/profile
GET /api/players/{playerId}/sessions
```

### Missões

```http
GET /api/missions
GET /api/missions/{id}
```

### Eventos comportamentais

```http
POST /api/sessions/{sessionId}/events
```

### Recomendações

```http
POST /api/recommendations/next
GET /api/recommendations/behavior-distribution/{sessionId}
```

### Steam mockada

```http
POST /api/steam/import
```

### Exportação experimental

```http
GET /api/exports/sessions
GET /api/exports/events
GET /api/exports/recommendations
```

---

## 20. Observação sobre a V1 da API

A versão atual da API usa uma lógica adaptativa inicial baseada em regras.

Ela serve para validar o fluxo completo:

```txt
Godot → API → banco → lógica adaptativa → API → Godot
```

Depois, quando o modelo de IA/ML estiver pronto, a lógica de recomendação poderá ser substituída ou complementada, mantendo os mesmos endpoints principais.

---

## 21. Checklist antes de considerar uma task concluída

```txt
[x] dotnet build sem erro
[x] API rodando
[x] Swagger testado
[x] Dados persistidos ou consultados corretamente
[x] SQLite conferido quando necessário
[x] Docs/diário técnico atualizado
[x] Commit criado
[x] Push feito para a branch
```
