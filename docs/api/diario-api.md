# Diário Técnico — API Backend

Este documento registra a evolução técnica da API Backend do projeto **Adaptive Trials**, desenvolvida como parte do TCC II. A API atua como camada intermediária entre o protótipo em Godot, a persistência local, o serviço de Inteligência Artificial e os fluxos de recomendação de missões.

---

## 01/07/2026 | Início da estruturação da API Backend

Após a conclusão do TCC I, iniciou-se a etapa de implementação técnica do TCC II. A primeira atividade definida foi a estruturação da API Backend, responsável por intermediar a comunicação entre o protótipo desenvolvido em Godot, a camada de persistência, a Steam Web API e o sistema de recomendação.

Embora a proposta inicial mencionasse a possibilidade de implementação do backend em Python, decidiu-se utilizar **.NET Web API**, considerando a familiaridade com C#, o alinhamento com o protótipo em Godot/C# e a facilidade de organização em camadas. A lógica de Inteligência Artificial foi posteriormente disponibilizada por meio de um serviço auxiliar em FastAPI, mantendo a API .NET como camada central de integração do sistema.

A API segue a estrutura planejada no Apêndice C do TCC I, mantendo endpoints para criação e encerramento de sessões, registro de eventos comportamentais, registro de preferências manuais, importação de perfil externo e solicitação da próxima recomendação de missão.

Decisão técnica inicial:

- Backend: .NET Web API;
- Banco de dados: SQLite;
- Documentação: Swagger/OpenAPI;
- Comunicação com o jogo: HTTP + JSON;
- Protótipo: Godot + C#;
- IA: serviço auxiliar em FastAPI.

---

## 02/07/2026 | Organização de branches e fluxo de sessões

Foram definidas regras de proteção para organizar o versionamento da API Backend. A branch `main` será utilizada apenas para versões estáveis, com alterações integradas por Pull Request a partir da branch `develop`. A branch `develop` concentrará as funcionalidades revisadas e integradas durante o desenvolvimento.

As branches de funcionalidade seguem o padrão `feat/*`, sendo criadas a partir de `develop`. Após a conclusão de cada funcionalidade, é aberto um Pull Request para integração. Também foram previstas regras para bloquear exclusão acidental de branches protegidas, impedir force push e exigir execução bem-sucedida do workflow de integração contínua antes do merge.

Foi criada a estrutura inicial de domínio da API, com as entidades principais previstas para o sistema: `Player`, `GameSession`, `Mission`, `BehaviorEvent`, `Recommendation` e `NormalizedProfile`.

Também foram criados os enums `GameMode`, `MissionType` e `SessionStatus`, representando os modos de jogo, categorias de missão e estados possíveis de uma sessão.

A camada de persistência foi iniciada com `AppDbContext`, utilizando Entity Framework Core e SQLite. A API foi configurada para utilizar a connection string local `adaptive_trials.db`, permitindo gerar a estrutura inicial do banco por meio de migrations.

Foi implementado o fluxo inicial de gerenciamento de sessões. A API passou a permitir criar uma nova sessão de jogo e encerrá-la posteriormente, persistindo os dados no banco SQLite.

Endpoints implementados:

- `POST /api/sessions`;
- `POST /api/sessions/{sessionId}/end`.

Foi validado que, ao criar uma sessão, a API registra um jogador anônimo quando nenhum `PlayerId` é informado. Ao encerrar a sessão, o status é atualizado para `Finished` e o campo `EndedAt` é preenchido corretamente.

---

## 07/07/2026 | Missões, eventos comportamentais e preferências manuais

Foi implementado o catálogo inicial de missões da API Backend, com base nas missões previstas no GDD do TCC I. As missões foram organizadas em três categorias principais: combate, exploração e quebra-cabeça.

Foram cadastradas 22 missões iniciais por meio de seed do Entity Framework Core:

- 8 missões de combate;
- 7 missões de exploração;
- 7 missões de quebra-cabeça.

Cada missão possui identificador, nome, tipo, template, dificuldade, parâmetros internos em JSON e descrição. Essa estrutura permite que o sistema adaptativo selecione missões concretas considerando categoria e dificuldade.

Endpoints implementados:

- `GET /api/missions`;
- `GET /api/missions/{missionId}`.

Também foi implementado o registro de eventos comportamentais da API. O endpoint `POST /api/sessions/{sessionId}/events` registra os dados produzidos durante a execução de uma missão.

O endpoint recebe identificador da missão, tempo de conclusão, número de falhas, resultado de sucesso ou falha e valor de persistência. A partir do `MissionId`, a API recupera automaticamente o tipo da missão, template e dificuldade cadastrados no catálogo, evitando inconsistência entre os dados enviados pelo jogo e os dados persistidos.

Foram adicionadas validações para sessão inexistente, sessão encerrada, missão inexistente, tempo negativo, falhas negativas e persistência fora do intervalo de 0 a 1.

Foi implementado o fluxo de registro de preferências manuais do jogador. Esse fluxo é utilizado quando o jogador não informa SteamID, não possui perfil público utilizável ou opta por não utilizar dados externos.

Endpoint implementado:

- `POST /api/players/{playerId}/preferences`.

A API valida os valores recebidos, impede preferências negativas e normaliza automaticamente as proporções para que a soma final seja igual a 1. As preferências são armazenadas no campo `ManualPreferencesJson` da entidade `Player` e também utilizadas para criar ou atualizar um `NormalizedProfile` com source `manual`.

Também foram calculadas features derivadas simples do perfil manual, como `diversity`, `entropy`, `dominance`, `second_max` e `gap`.

---

## 08/07/2026 | Perfil do jogador e recomendação adaptativa inicial

Foi implementado o endpoint de consulta do perfil consolidado do jogador.

Endpoint implementado:

- `GET /api/players/{playerId}/profile`.

Esse endpoint retorna o perfil normalizado associado ao jogador, incluindo a origem do perfil e as proporções calculadas para combate, exploração e quebra-cabeça.

Foi implementada a primeira versão do endpoint de recomendação.

Endpoint implementado:

- `POST /api/recommendations/next`.

Inicialmente, a recomendação retornava uma distribuição fixa entre as categorias, selecionava uma missão disponível no catálogo e registrava a recomendação no banco.

Em seguida, foi implementado o cálculo da distribuição comportamental do jogador a partir dos eventos registrados durante uma sessão. A distribuição considera sucesso, frequência de interação, número de falhas, tempo médio de conclusão e persistência.

Endpoint auxiliar implementado:

- `GET /api/recommendations/behavior-distribution/{sessionId}`.

Quando a sessão ainda não possui eventos, o sistema retorna uma distribuição equilibrada entre as três categorias. Quando há eventos registrados, os scores são normalizados para produzir uma distribuição probabilística válida.

Na versão adaptativa, o endpoint `POST /api/recommendations/next` passou a combinar duas fontes de informação: o perfil inicial do jogador e a distribuição comportamental calculada a partir dos eventos registrados na sessão.

A lógica implementada segue a estrutura prevista no TCC:

```text
Pfinal = X * Pperfil + Y * Pcomportamento
```

`Pperfil` é obtido a partir do `NormalizedProfile` do jogador. Quando o jogador ainda não possui perfil manual ou externo, a API utiliza uma distribuição equilibrada entre combate, exploração e quebra-cabeça. `Pcomportamento` é calculado a partir dos eventos comportamentais da sessão.

Os pesos `X` e `Y` são ajustados progressivamente conforme a quantidade de eventos da sessão. No início, o perfil possui maior influência; conforme mais eventos são registrados, o comportamento passa a ter maior peso na recomendação.

---

## 09/07/2026 | Seleção probabilística, dificuldade e importação mockada da Steam

Foi implementada a seleção probabilística de categoria e missão no endpoint de recomendação. A recomendação passou a utilizar a distribuição final calculada pelo sistema adaptativo para sortear a próxima categoria de missão.

Dessa forma, categorias com maior probabilidade tendem a ser selecionadas com maior frequência, mas as demais categorias continuam disponíveis, preservando variedade na experiência.

Também foi implementada a seleção de uma missão concreta a partir da categoria sorteada. O sistema busca missões disponíveis no catálogo inicial e tenta evitar repetição de missões já recomendadas na mesma sessão. Caso todas as missões da categoria já tenham sido utilizadas, a repetição é permitida.

Foi implementado o ajuste inicial de dificuldade com abordagem baseada em regras. O sistema calcula uma dificuldade-alvo a partir dos eventos comportamentais mais recentes da sessão, considerando taxa de sucesso, número médio de falhas, persistência média, tempo médio de conclusão e dificuldade média das missões executadas.

A dificuldade-alvo é limitada ao intervalo de 1 a 5, conforme o catálogo de missões previsto no GDD. Durante os testes, foi observado que a missão selecionada poderia apresentar dificuldade real diferente da dificuldade-alvo calculada, pois a lógica anterior priorizava evitar repetição antes de buscar a dificuldade mais próxima. A seleção foi ajustada para priorizar primeiro a proximidade com a dificuldade-alvo e, em seguida, evitar repetição entre as missões igualmente adequadas.

Foi implementada a primeira versão do fluxo de importação de perfil Steam. Nesta etapa, a integração ainda não realizava chamadas reais para a Steam Web API. Foi criado um endpoint mockado para simular o comportamento esperado do sistema final.

Endpoint implementado:

- `POST /api/steam/import`.

O endpoint recebe `PlayerId` e `SteamId`, valida a existência do jogador, armazena o `SteamId` na entidade `Player` e cria ou atualiza o `NormalizedProfile` com source `steam_mock`.

Foram também implementados endpoints de consulta para apoiar a análise dos dados coletados pelo protótipo:

- `GET /api/sessions/{sessionId}`;
- `GET /api/sessions/{sessionId}/events`;
- `GET /api/sessions/{sessionId}/recommendations`;
- `GET /api/players/{playerId}/sessions`.

Esses endpoints utilizam DTOs específicos, evitando expor diretamente as entidades do banco de dados.

---

## 12/07/2026 | Exportação de dados e correção do modo controle

Foram implementados endpoints de exportação dos dados coletados pela API Backend.

Endpoints adicionados:

- `GET /api/exports/sessions`;
- `GET /api/exports/events`;
- `GET /api/exports/recommendations`.

Esses endpoints retornam dados organizados em JSON para apoiar a análise posterior do funcionamento do protótipo. A exportação inclui sessões registradas, eventos comportamentais e recomendações geradas durante o uso do sistema.

Foram criados DTOs específicos para exportação, evitando a exposição direta das entidades do banco e removendo informações desnecessárias ou sensíveis, como SteamID e preferências manuais em formato bruto.

Também foi ajustado o comportamento do modo controle da API Backend. A partir desta alteração, sessões criadas com `GameMode.Control` não utilizam perfil manual, perfil Steam ou eventos comportamentais para adaptar a recomendação.

O modo controle passa a utilizar uma distribuição fixa e equilibrada entre combate, exploração e quebra-cabeça, preservando seu papel metodológico como modo não adaptativo. Os eventos comportamentais continuam sendo registrados em sessões de controle, mas são utilizados apenas para análise posterior, não para adaptação em tempo real.

O endpoint `POST /api/recommendations/next` agora diferencia explicitamente sessões de controle e sessões adaptativas. Sessões de controle retornam probabilidades fixas, pesos de perfil e comportamento iguais a zero e uma justificativa própria. Sessões adaptativas continuam utilizando a combinação entre perfil inicial, comportamento observado, seleção probabilística e ajuste de dificuldade baseado em regras.

---

## 13/07/2026 | Reorganização do repositório e documentação de integração

Foi definida uma nova organização para o repositório do TCC II, considerando que o projeto envolve três frentes principais de desenvolvimento: API Backend, modelo de IA e protótipo em Godot.

A estrutura do repositório foi reorganizada como monorepo:

```text
AdaptiveTrials/
├── api/
├── ai-model/
├── game/
└── docs/
```

Essa organização foi adotada para facilitar a rastreabilidade do desenvolvimento, manter a relação entre os componentes do sistema e simplificar a apresentação do projeto como uma solução integrada.

Também foi planejada a separação da documentação em áreas específicas para API, IA e jogo, permitindo registrar a evolução técnica de cada frente de forma organizada.

Foi documentado o fluxo de integração entre o protótipo desenvolvido em Godot e a API Backend. A documentação define a sequência de chamadas necessárias para criar sessões, registrar preferências manuais ou SteamID no modo adaptativo, solicitar recomendações, registrar eventos comportamentais e encerrar sessões.

Também foi detalhada a diferença entre modo controle e modo adaptativo, garantindo que o modo controle não utilize perfil, Steam ou comportamento para adaptar recomendações, mas continue registrando eventos para análise posterior.

---

## Release v1.0.0 | API funcional inicial

Foi criada a versão `v1.0.0` do repositório, marcando a primeira versão funcional da API Backend do TCC II.

Esta versão consolidou os principais fluxos necessários ao protótipo:

- criação e encerramento de sessões;
- catálogo de missões;
- registro de eventos comportamentais;
- preferências manuais;
- perfil normalizado;
- recomendação adaptativa baseada em regras;
- diferenciação entre modo controle e adaptativo;
- ajuste inicial de dificuldade;
- importação mockada da Steam;
- exportação de dados.

A versão `v1.0.0` foi utilizada como baseline da API antes da integração com o serviço de IA.

---

## Retomada da integração entre API e IA

Após a conclusão da primeira versão funcional da API e o avanço da frente de IA, foi identificado que o projeto já possuía os elementos necessários para iniciar a integração entre o backend e o modelo de recomendação.

A lógica da IA foi alterada em relação à proposta inicial. O projeto passou a utilizar um dataset próprio construído a partir de dados da Steam, com coleta de SteamIDs válidos por meio de reviews públicas de jogos. Também foi definida a comparação entre KNN, Árvore de Decisão, Random Forest e Naive Bayes.

Com base nos resultados da frente de IA, a Random Forest foi escolhida como modelo final para integração. O modelo final foi salvo como artefato e disponibilizado por meio de um serviço auxiliar em FastAPI, com endpoints de health check e predição.

A API .NET permaneceu como camada intermediária entre o jogo, a persistência e o modelo, recebendo dados do Godot, consultando o serviço de IA quando necessário, salvando o perfil normalizado e retornando recomendações ao jogo.

---

## 06/09/2026 | Integração final da API com o serviço de IA

Foi implementada a integração entre a API Backend em .NET e o serviço auxiliar de IA desenvolvido em FastAPI.

A partir desta etapa, o endpoint `POST /api/steam/import` deixou de depender apenas do perfil mockado e passou a tentar consultar o serviço de IA para obter uma predição de perfil externo a partir do SteamID informado.

O serviço de IA utiliza o modelo final selecionado na frente de IA, baseado em Random Forest V2 com sinais de recência. O endpoint de inferência roda localmente em:

```text
http://127.0.0.1:8001
```

A API envia o SteamID ao serviço de IA, recebe as probabilidades previstas para as categorias `combat`, `exploration` e `strategic_reasoning`, além de informações resumidas da biblioteca Steam do jogador.

Como o protótipo e a API utilizam a categoria `puzzle`, foi criada uma conversão explícita:

```text
strategic_reasoning → puzzle
```

Com isso, a API mantém compatibilidade com o vocabulário usado pelo jogo, pelo catálogo de missões e pela lógica adaptativa, sem alterar a nomenclatura interna do modelo de IA.

O perfil retornado pela IA é salvo em `NormalizedProfile` com source `steam_ai`. São armazenadas as probabilidades normalizadas, total de horas jogadas, número de jogos, jogos por categoria e horas por categoria.

Também foi mantido o mecanismo de fallback. Caso o serviço de IA esteja indisponível e a configuração `UseFallbackWhenUnavailable` esteja ativa, a API utiliza o fluxo `steam_mock`, preservando o funcionamento local do protótipo.

Foram validados os seguintes cenários:

- serviço de IA ativo e resposta `source = steam_ai`;
- preenchimento correto das probabilidades do perfil;
- preenchimento correto dos dados resumidos da biblioteca Steam;
- uso do perfil da IA pelo endpoint `POST /api/recommendations/next`;
- conversão correta de `strategic_reasoning` para `puzzle`;
- fallback para `steam_mock` com o serviço de IA desligado.

Com essa etapa, a API Backend foi considerada concluída para o MVP do TCC II, integrada ao modelo de IA e pronta para ser consumida pelo protótipo em Godot.

---

## Release v1.1.0 | API integrada à IA

A versão `v1.1.0` marca a finalização funcional da frente da API para o MVP do TCC II.

Esta versão inclui:

- integração da API .NET com o serviço FastAPI de IA;
- uso do modelo Random Forest V2 com sinais de recência;
- importação de perfil externo com source `steam_ai`;
- conversão `strategic_reasoning → puzzle`;
- fallback `steam_mock` quando a IA está indisponível;
- recomendação adaptativa utilizando o perfil gerado pela IA;
- preservação do modo controle como fluxo não adaptativo;
- documentação técnica atualizada.

A partir desta release, o foco do desenvolvimento passa para a integração do jogo em Godot com a API e para os testes do fluxo completo do protótipo.
