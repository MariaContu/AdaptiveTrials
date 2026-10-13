# Issue 10C — Validação comportamental e regressão da sessão 14

## Objetivo

Verificar se a distribuição comportamental é determinada por desempenho e não pela mera frequência com que a própria política de recomendação expõe o jogador a uma categoria. Esta etapa **não altera** o algoritmo de produção, o modelo de IA, as regras da V1 nem o banco de dados existente.

## Evidências examinadas

- ZIP do repositório enviado em 10/10/2026, incluindo `RecommendationService.cs` e cópia do banco SQLite.
- Sessão 14 (modo 2): 6 recomendações, 6 eventos, 5 explorações e 1 combate; perfil armazenado: combate `0.515521`, exploração `0.439241`, puzzle `0.045238`.
- Distribuição comportamental informada pela API durante a análise anterior: combate `0.048118`, exploração `0.936530`, puzzle `0.015352`.
- O `RecommendationService.cs` **atual** utiliza média de desempenho por categoria, prior neutro `0.65` com peso `2.0`, e valor neutro para categorias não experimentadas.

## Diagnóstico

Os registros persistidos da sessão 14 **não correspondem ao cálculo comportamental da versão atual do código**. O primeiro sorteio coincide com o cálculo atual (comportamento inicial neutro). Já na segunda recomendação, a distribuição implícita nos valores registrados é aproximadamente 4,6% combate, 90,8% exploração e 4,6% puzzle, o que não é compatível com a suavização presente no código enviado.

Hipótese mais plausível: a sessão foi produzida por um processo da API iniciado antes da correção do cálculo comportamental. Não é possível provar apenas pelo banco se foi um processo antigo, outro binário ou outra versão do código. O histórico armazenado não deve ser reescrito.

## Reprodução offline da lógica atual com os eventos da sessão 14

| Após N eventos | Combate (comportamento) | Exploração (comportamento) | Puzzle (comportamento) |
|---:|---:|---:|---:|
| 0 | 33,33% | 33,33% | 33,33% |
| 1 | 31,48% | 37,05% | 31,48% |
| 3 | 30,17% | 39,66% | 30,17% |
| 5 | 27,87% | 38,77% | 33,36% |
| 6 | 27,40% | 39,80% | 32,80% |

Após os seis eventos, uma nova combinação **hipotética** com pesos de 30% perfil e 70% comportamento produziria aproximadamente 34,65% combate, 41,04% exploração e 24,32% puzzle. Esse cálculo hipotético **não representa uma sétima recomendação real** da sessão 14, que foi encerrada.

## Testes automatizados adicionados

`BehaviorDistributionRegressionTests.cs` contém três testes de integração (SQLite em memória):

1. Uma exploração bem-sucedida não torna exploração quase certa e mantém categorias não observadas com probabilidade positiva.
2. Reprodução exata dos seis eventos da sessão 14, conferindo a distribuição suavizada e os pesos do perfil/comportamento.
3. Falha em exploração reduz o escore comportamental da categoria em comparação com sucesso, sem zerá-la.

## Execução

Na raiz do repositório:

```powershell
dotnet test api/AdaptiveTrials.slnx --filter FullyQualifiedName~BehaviorDistributionRegressionTests
```

Em seguida:

```powershell
dotnet test api/AdaptiveTrials.slnx
```

> Este pacote foi preparado por análise estática e reprodução numérica em Python. Não foi compilado em .NET neste ambiente; os testes devem ser executados na máquina de desenvolvimento.

## Próxima validação em runtime

1. Encerrar todas as instâncias antigas da API; reiniciar explicitamente a partir do código atualizado.
2. Criar **nova** sessão V2 com o mesmo perfil ou preferências equivalentes.
3. Após a primeira exploração, consultar `/api/recommendations/behavior-distribution/{novaSessionId}`. Espera-se aproximadamente 31,48% combate, 37,05% exploração, 31,48% puzzle para evento de sucesso com métricas idênticas às da sessão 14.
4. Conferir `profileProbabilities`, `behaviorProbabilities`, `finalProbabilities` e `weights` na resposta de cada `POST /api/recommendations/next`.
5. Guardar os registros da nova sessão como evidência da correção e **não** reescrever a sessão 14.

## Limitações

Os resultados mostram consistência algorítmica, não preferência subjetiva real do jogador. Sucesso e rapidez podem refletir dificuldade da missão, familiaridade ou balanceamento; isso deve ser descrito como limitação no TCC. A seleção continua probabilística, sem cotas por categoria na V2. Não foi avaliada a distribuição estatística dos sorteios em grande amostra nesta etapa.
