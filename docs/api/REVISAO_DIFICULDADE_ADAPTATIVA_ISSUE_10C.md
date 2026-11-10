# Validação e justificativa — revisão da dificuldade adaptativa (Issue 10C)

## Contexto

Na sessão adaptativa 15, a sequência registrada foi 1 (vitória), 2 (vitória), 3 (vitória), 3 (falha), 3 (vitória), 3 (vitória). Após a falha no quarto evento, o algoritmo antigo manteve o alvo em 3, pois calculava uma média de dificuldade e verificava sucesso agregado >=75% (3/4). A média temporal misturava missões de diferentes durações.

## Decisão de implementação

A dificuldade-alvo começa em 1 e passa a ser determinada pela dificuldade da **última missão executada**, usando os resultados dos dois eventos mais recentes. Uma derrota reduz um nível imediatamente (limites 1..3). Uma vitória imediatamente após derrota estabiliza o nível; vitórias consecutivas com média de falhas <=1 e persistência >=0,7 permitem subir um nível. Mesmo quando há vitória, persistência <0,4 ou pelo menos 3 falhas no último evento justificam uma redução. Não há distinção por categoria devido à amostra curta de seis eventos por sessão.

## Tempo de conclusão

A duração em segundos não é um indicador comparável entre templates sem normalização ou uma linha de base por missão. Por exemplo, sobreviver a cinco ondas tende a demorar mais do que percorrer um trajeto de exploração. A métrica continua sendo registrada e usada em outros componentes; apenas a regra de dificuldade deixou de usar limites absolutos de 90/150 segundos.

## Cobertura prevista

Os testes adicionados exercitam: início sem histórico; sucesso nos níveis 1 e 3; falha no nível 3 ou 1; sucesso lento em Sobreviver; vitória com muitas falhas ou baixa persistência; histórico equivalente à sessão 15; estabilização após derrota; retomada após duas vitórias consecutivas; vitória de desempenho intermediário.

## Evidência / estado

As 16 verificações anteriores da API foram reportadas como aprovadas antes desta revisão; isso **não** valida o novo patch. A execução da nova suíte e o teste manual ainda estão pendentes. Após validação, registrar `dotnet test` e ao menos uma sessão V2 com derrota em nível 3, seguida de recomendação de nível 2.
