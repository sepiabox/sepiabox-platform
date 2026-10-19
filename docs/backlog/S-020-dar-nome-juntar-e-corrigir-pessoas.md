## S-020 — Dar nome, juntar e corrigir pessoas
**Épico:** Pessoas
**Depende de:** S-019
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O modelo agrupa; só a família sabe quem é quem. As correções manuais nunca podem ser desfeitas pela atribuição automática.

### Âmbito
- `/pessoas/{id}`: renomear (nomes únicos sem distinção de maiúsculas), "Sou eu" (liga o e-mail do utilizador atual; um por utilizador), juntar com outra pessoa, retirar um rosto ("não é esta pessoa", marca `excluded` para esta pessoa), marcar "não é um rosto".
- Atribuição automática ignora pares rosto/pessoa excluídos e rostos `manual`.

### Fora de âmbito
- Relações entre pessoas (fase 3).

### Critérios de aceitação
- [ ] Dado duas pessoas juntas, então os rostos passam para uma e as contagens somam.
- [ ] Dado um rosto retirado de uma pessoa, quando reimporto, então não volta para ela.
- [ ] Dado "Sou eu" por dois utilizadores na mesma pessoa, então o segundo é recusado.
- [ ] Dado um nome já usado por outra pessoa, então a mudança é recusada com mensagem.

### Notas técnicas
- Operações em transação; recalcular centróides.

### Testes esperados
- Integração (PostgreSQL): juntar, excluir, unicidade. bUnit: formulários.
