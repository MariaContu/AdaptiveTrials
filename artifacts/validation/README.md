# Banco SQLite - evidências finais do Adaptive Trials

Este diretório contém uma cópia do banco da API preparada para inspeção acadêmica.

- `adaptive_trials_entrega.db`: 18 sessões, 70 eventos, 54 recomendações, 27 missões e 12 perfis normalizados.
- Sessão 16: Controle, encerrada, 6 eventos, 0 recomendações, distribuição 2/2/2.
- Sessão 18: Adaptativo, encerrada, 6 eventos, 6 recomendações, distribuição combate/exploração/puzzle = 1/4/1.
- Sessão 13: histórico de teste com falha seguida de redução de dificuldade.

A coluna `Players.SteamId` foi zerada na cópia de entrega. Os valores originais e o banco de trabalho não devem ser colocados em repositórios públicos sem autorização. Uma operação `VACUUM` recriou o arquivo após a remoção dos valores.

Os registros de execução incluem sessões históricas de desenvolvimento e não equivalem a uma suíte automatizada.

Consultas de auditoria (SQLite CLI):

```sql
PRAGMA integrity_check;
PRAGMA foreign_key_check;
SELECT Id, Mode, Status, StartedAt, EndedAt FROM Sessions WHERE Id IN (16,18);
SELECT SessionId, COUNT(*) AS eventos FROM BehaviorEvents WHERE SessionId IN (16,18) GROUP BY SessionId;
SELECT SessionId, COUNT(*) AS recomendacoes FROM Recommendations WHERE SessionId IN (16,18) GROUP BY SessionId;
SELECT SessionId, MissionType, COUNT(*) AS quantidade FROM BehaviorEvents WHERE SessionId IN (16,18) GROUP BY SessionId, MissionType;
```

Para novas execuções, use uma cópia de trabalho separada: não altere o arquivo da evidência submetida.
