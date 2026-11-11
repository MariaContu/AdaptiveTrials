# Adaptive Trials — TCC II

Protótipo de jogo com recomendação híbrida de missões, desenvolvido em Godot/C#, com API .NET/SQLite e serviço de inferência Python/FastAPI. A Random Forest V2 estima o perfil inicial usando 49 atributos de uma biblioteca pública da Steam; a API combina esse perfil com eventos registrados durante sessões de seis missões.

## Estrutura

- `game/`: projeto Godot/C# com cenas, missões e testes isolados;
- `api/`: backend ASP.NET Core, testes de integração e persistência SQLite;
- `ai-model/`: scripts de dados, artefato de classificação e serviço FastAPI;
- `docs/`: diário técnico, contratos e instruções adicionais;
- `artifacts/validation/adaptive_trials_entrega.db`: banco **anonimizado** de evidências das sessões documentadas no TCC.

## Pré-requisitos

- SDK .NET compatível com a solução (`api/AdaptiveTrials.slnx`), com suporte ao ASP.NET Core;
- Godot compatível com o projeto `game/project.godot` (edição com suporte a .NET/C#);
- Python 3.11+ e dependências de `ai-model/requirements.txt`;
- Steam Web API Key própria, quando for utilizar a importação ao vivo de perfis públicos.

## Execução dos serviços

Terminal A — inferência:

```bash
cd ai-model
python3 -m venv .venv
source .venv/bin/activate  # Windows: .venv\\Scripts\\activate
pip install -r requirements.txt
```

Crie `ai-model/.env` com `STEAM_API_KEY=...` (nunca versionar a chave) e inicie:

```bash
uvicorn --app-dir src "10a_inference_api:app" --host 127.0.0.1 --port 8001
```

Terminal B — API, a partir da pasta que contém o banco SQLite:

```bash
cd api/src/AdaptiveTrials.Api
dotnet run --launch-profile http
```

O backend utiliza `http://127.0.0.1:8001` para inferência e é configurado para atender em `http://localhost:5277`. Abra `game/project.godot` no Godot, mantenha a URL de `game/config/api_settings.json` consistente e execute o projeto. As conexões com a Steam exigem acesso à Internet e biblioteca pública.

## Testes e conferência

```bash
cd api
dotnet restore
dotnet test AdaptiveTrials.slnx
```

```bash
cd ai-model
python3 -m pytest tests -q
python3 src/09o_final_ai_v2_smoke_check.py
```

O smoke de IA exige chave Steam presente no ambiente; ele verifica artefatos locais e não comprova o funcionamento da Steam na rede. O arquivo `ai-model/data/interim/final_game_category_scores_v2.csv` e o modelo `ai-model/models/final/macro_model_recency_v2.joblib` acompanham este repositório. Os datasets intermediários completos de treinamento **não estão todos distribuídos**, portanto essa cópia não garante a reprodução integral da coleta original e do treinamento de modelos.

## Dados de validação e privacidade

A versão preparada para entrega preserva as sessões **16 (Controle)** e **18 (Adaptativo)** e o caso histórico **13**. O arquivo `artifacts/validation/adaptive_trials_entrega.db` contém os mesmos registros da cópia localizada em `api/src/AdaptiveTrials.Api/adaptive_trials.db`, porém sem valores em `Players.SteamId`. O TCC apresenta um apêndice com as missões e probabilidades da execução.

**Atenção:** antes de adicionar novos registros de jogadores ou gravar novas sessões, faça uma cópia local. O `.db` operacional está versionado por decisão de entrega; não faça commits futuros com identificadores ou informações pessoais adicionados durante testes.

A exportação compilada do jogo não acompanha este snapshot de código e deve ser fornecida separadamente quando exigida pela disciplina. Um ZIP obtido do GitHub não inclui o histórico `.git` e não permite verificar, sozinho, o commit efetivamente entregue.
