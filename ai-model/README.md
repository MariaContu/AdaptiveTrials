# Adaptive Trials — AI Model

Este repositório contém a frente de dados e aprendizado de máquina do projeto **Adaptive Trials**, desenvolvido no contexto do Trabalho de Conclusão de Curso II em Ciência da Computação.

## Sobre o projeto

O Adaptive Trials propõe um sistema híbrido de recomendação para adaptação de missões em jogos digitais.

O protótipo trabalha com três categorias principais de missão:

* combate;
* exploração;
* puzzle ou quebra-cabeça.

A frente de inteligência artificial tem como objetivo construir um dataset baseado em dados públicos da Steam, gerar perfis de preferência dos jogadores e comparar diferentes algoritmos de classificação.

## Objetivos

Os principais objetivos desta frente são:

* coletar SteamIDs obtidos por meio de avaliações públicas de jogos;
* consultar bibliotecas públicas e tempos de jogo;
* coletar os metadados dos jogos encontrados;
* mapear jogos para as categorias combate, exploração e puzzle;
* construir perfis agregados de jogadores;
* gerar features derivadas;
* treinar e comparar modelos supervisionados;
* analisar diferentes níveis de granularidade das categorias;
* selecionar e exportar o modelo final.

## Modelos avaliados

Os seguintes algoritmos serão comparados:

* K-Nearest Neighbors;
* Decision Tree;
* Random Forest;
* Naive Bayes.

A Decision Tree será utilizada principalmente por sua interpretabilidade, permitindo visualizar regras e analisar a importância das features. A Random Forest será avaliada como uma alternativa mais robusta, baseada na combinação de múltiplas árvores.

A escolha final será baseada nos resultados experimentais e não será definida antecipadamente.

## Estrutura do projeto

```text
ai-model/
├── config/
├── data/
│   ├── raw/
│   ├── interim/
│   └── processed/
├── models/
├── reports/
│   ├── figures/
│   └── metrics/
├── src/
├── tests/
├── .env.example
├── .gitignore
├── README.md
└── requirements.txt
```

### Diretórios

* `config/`: configurações versionadas, incluindo o futuro mapeamento das categorias;
* `data/raw/`: respostas e arquivos obtidos diretamente das fontes;
* `data/interim/`: dados limpos ou parcialmente processados;
* `data/processed/`: dataset final utilizado pelos modelos;
* `models/`: modelos e pipelines treinados;
* `reports/figures/`: gráficos, matrizes de confusão e árvores exportadas;
* `reports/metrics/`: relatórios e métricas em formatos estruturados;
* `src/`: scripts do pipeline;
* `tests/`: testes automatizados das principais regras de processamento.

## Pipeline previsto

1. coleta de SteamIDs;
2. consulta das bibliotecas públicas;
3. coleta dos metadados dos jogos;
4. mapeamento dos jogos para as categorias;
5. construção dos perfis dos jogadores;
6. geração das features e do target;
7. análise da qualidade do dataset;
8. treinamento dos modelos;
9. avaliação e comparação;
10. análise de granularidade;
11. exportação do modelo final.

## Features previstas

O dataset poderá incluir:

* proporção de combate;
* proporção de exploração;
* proporção de puzzle;
* horas por categoria;
* quantidade de jogos por categoria;
* total de horas jogadas;
* número total de jogos;
* média de horas por jogo;
* diversidade;
* entropia;
* dominância;
* segunda maior proporção;
* diferença entre a maior e a segunda maior proporção;
* classe final do perfil.

A definição exata das features será documentada e versionada durante a construção do dataset.

## Configuração do ambiente

Crie um ambiente virtual:

```bash
python -m venv .venv
```

Ative o ambiente no Windows:

```bash
.venv\Scripts\activate
```

Ative o ambiente no macOS ou Linux:

```bash
source .venv/bin/activate
```

Instale as dependências:

```bash
pip install -r requirements.txt
```

## Configuração da Steam API

Crie um arquivo `.env` na raiz do projeto:

```env
STEAM_API_KEY=your_steam_api_key
```

A chave da API não deve ser enviada ao repositório.

## Reprodutibilidade

As etapas do pipeline serão executadas por scripts numerados e independentes. Cada script deverá:

* receber configurações explícitas;
* registrar estatísticas de execução;
* evitar alterações silenciosas nos dados;
* salvar resultados intermediários;
* permitir a repetição da coleta ou processamento;
* utilizar sementes fixas nas operações aleatórias.

## Metodologia de avaliação

Os modelos serão comparados utilizando:

* accuracy;
* precision;
* recall;
* F1-score;
* macro average;
* weighted average;
* matriz de confusão;
* relatório de classificação.

Quando aplicável, também serão realizadas validação cruzada e análise da variação das métricas entre diferentes divisões dos dados.

## Análise de granularidade

Além da classificação nas três categorias principais, será analisado se a divisão em subgrupos altera significativamente os resultados.

Serão comparados:

* combate geral e subgrupos de combate;
* exploração geral e subgrupos de exploração;
* puzzle geral e subgrupos de puzzle.

Essa análise será utilizada para identificar possíveis perdas de informação ou vieses produzidos pelo agrupamento das tags da Steam em categorias mais amplas.

## Estado atual

O projeto encontra-se na etapa inicial de estruturação e definição do protocolo de coleta dos SteamIDs.
