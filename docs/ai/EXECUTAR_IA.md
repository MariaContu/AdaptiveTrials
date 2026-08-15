Executando a IA do Adaptive Trials

Guia rápido para iniciar o fluxo Steam → IA → API → recomendação.

1. Ativar o ambiente Python

Na raiz do projeto:

source .venv/bin/activate

2. Iniciar o serviço de IA

cd ai-model

uvicorn \
  --app-dir src \
  "10a_inference_api:app" \
  --host 127.0.0.1 \
  --port 8001

O terminal deve permanecer aberto enquanto a API .NET estiver utilizando a IA.

Verificar se a IA está ativa

Em outro terminal:

curl http://127.0.0.1:8001/health

Resposta esperada:

{
  "status": "ok",
  "model": "macro_model_optimized",
  "featureCount": 40,
  "featureSet": "full_ratios",
  "trainingProfiles": 686
}

3. Iniciar a API .NET

Em outro terminal, a partir da raiz do projeto:

cd api/src/AdaptiveTrials.Api
dotnet run

A URL local utilizada atualmente é:

http://localhost:5277

4. Fluxo adaptativo

Com os dois serviços ativos:

POST /api/steam/import — importa o SteamID e gera o perfil pela IA.

POST /api/sessions com mode = 2 — cria uma sessão adaptativa.

POST /api/recommendations/next — solicita uma missão.

POST /api/sessions/{sessionId}/events — registra o resultado da missão.

POST /api/recommendations/next — solicita uma nova recomendação já considerando comportamento.

Repetir eventos e recomendações até o final da sessão.

Observação

A IA utiliza internamente:

combat

exploration

strategic_reasoning

Na API e no jogo, strategic_reasoning é mapeado para Puzzle.