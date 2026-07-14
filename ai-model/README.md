# AI Model — Adaptive Trials

Diretório responsável pela frente de Inteligência Artificial do TCC II
**Sistema de Recomendação Híbrido Baseado em Inteligência Artificial para Adaptação em Jogos Digitais**.

## Objetivo

Treinar e avaliar modelos baseados em árvores para apoiar a escolha do modelo
de recomendação utilizado no protótipo:

- Árvore de Decisão;
- Random Forest.

A Árvore de Decisão será utilizada como modelo interpretável. A Random Forest
será avaliada como uma alternativa mais robusta contra overfitting.

## Estrutura

```text
ai-model/
├── data/
│   ├── raw/
│   └── processed/
├── models/
├── reports/
│   ├── figures/
│   └── metrics/
├── src/
│   ├── __init__.py
│   ├── config.py
│   └── main.py
├── tests/
│   └── __init__.py
├── .gitignore
├── requirements.txt
└── README.md
```

## Responsabilidade de cada pasta

- `data/raw`: dataset original recebido pela frente de IA.
- `data/processed`: dataset final preparado para treinamento.
- `models`: modelos treinados e serializados.
- `reports/figures`: matrizes de confusão, árvore e gráficos de importância.
- `reports/metrics`: métricas e relatórios de classificação.
- `src`: código-fonte de carregamento, treinamento, avaliação e exportação.
- `tests`: testes automatizados da frente de IA.

## Instalação inicial

Recomenda-se utilizar Python 3.11 ou superior.

```bash
cd ai-model
python -m venv .venv
```

No Windows:

```bash
.venv\Scripts\activate
```

No macOS ou Linux:

```bash
source .venv/bin/activate
```

Depois:

```bash
pip install -r requirements.txt
```

## Estado atual

Nesta primeira etapa foi criada somente a estrutura inicial do projeto.
O carregamento do dataset, treinamento e avaliação serão implementados nas
próximas tasks.
