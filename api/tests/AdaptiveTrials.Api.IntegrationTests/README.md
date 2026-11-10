# Testes de integração — Modo Controle

Esta suíte valida a regressão do backend do modo Controle utilizando a API real em memória, sem alterar `adaptive_trials.db`.

## Cobertura

- catálogo com 27 configurações (9 templates x 3 dificuldades);
- JSON de parâmetros válido e parâmetros obrigatórios por template;
- ausência de parâmetros legados `clues` e `distance`;
- criação de sessão Controle;
- persistência de seis eventos comportamentais (2 Combate, 2 Exploração, 2 Puzzle);
- persistência de sucesso, falhas, tempo e persistência;
- encerramento e idempotência do encerramento;
- rejeição de novos eventos após sessão finalizada;
- validação de métricas inválidas;
- garantia de que eventos do Controle não alteram o perfil normalizado;
- garantia de que sessões Controle não podem gerar recomendações adaptativas.

## Banco de teste

`AdaptiveTrialsApiFactory` substitui o SQLite da aplicação por um SQLite `:memory:`. Cada teste recebe uma instância isolada da API e o banco real do projeto não é modificado.

## Execução

Na pasta `api`:

```bash
dotnet restore
dotnet test AdaptiveTrials.slnx
```

Para executar apenas esta suíte:

```bash
dotnet test tests/AdaptiveTrials.Api.IntegrationTests/AdaptiveTrials.Api.IntegrationTests.csproj
```
