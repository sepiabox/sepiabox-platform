## S-022 — Desligar o reconhecimento facial e apagar dados de rostos
**Épico:** Pessoas
**Depende de:** S-019
**Prioridade:** Média
**Tamanho:** S

### Contexto
Rostos são dados biométricos (LGPD/GDPR). A família tem de poder desligar e apagar.

### Âmbito
- `/definicoes`: interruptor de reconhecimento facial da biblioteca (confirmação no próprio ecrã).
- Desligar apaga todas as linhas de `faces` e `people`; com reconhecimento desligado, a importação ignora rostos dos manifestos.
- Por pessoa: "Apagar dados de rosto" remove a pessoa e os seus rostos (as fotos ficam).

### Fora de âmbito
- Encriptação por utilizador.

### Critérios de aceitação
- [ ] Dado reconhecimento desligado, então `faces` e `people` ficam vazias.
- [ ] Dado reconhecimento desligado, quando importo um manifesto com rostos, então nenhum rosto é gravado.
- [ ] Dado "Apagar dados de rosto" numa pessoa, então os seus rostos desaparecem e as fotos continuam.

### Notas técnicas
- Tabela `settings` (chave/valor).

### Testes esperados
- Integração (PostgreSQL): apagamento e importação com reconhecimento desligado.
