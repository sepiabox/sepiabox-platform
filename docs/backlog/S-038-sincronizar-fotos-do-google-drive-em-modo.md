## S-038 — Sincronizar fotos do Google Drive em modo referência
**Épico:** Fontes na nuvem
**Depende de:** S-037, S-036
**Prioridade:** Média
**Tamanho:** M

### Contexto
Com a conta ligada, as fotos do Drive aparecem ao lado das do HD, sem duplicar originais (D-023).

### Âmbito
- Job agendado diário `sepiabox-worker sync google-drive`: primeira execução lista tudo; seguintes usam a API de changes (`startPageToken` guardado).
- Cada ficheiro novo é descarregado para `incoming` com metadados (fonte google-drive, fileId, caminho, modo referência).
- Ficheiro removido no Drive → fonte marcada como removida.
- Mesmo conteúdo já vindo do HD → só ganha uma fonte (sem asset novo).
- Detalhe da foto: "Abrir original" descarrega do Drive através do servidor.

### Fora de âmbito
- Google Photos (API restrita).

### Critérios de aceitação
- [ ] Dado um cliente falso com 5 ficheiros, quando a primeira sincronização corre, então 5 blobs entram em `incoming`.
- [ ] Dado uma segunda sincronização com 1 alteração, então só 1 blob entra.
- [ ] Dado um ficheiro removido, então a fonte fica marcada como removida.
- [ ] Dado conteúdo igual a um asset do HD, então o asset ganha uma fonte e não é duplicado.

### Notas técnicas
- Cron do Container Apps Job no AppHost.
- Decisões: D-023, D-024.

### Testes esperados
- Integração (Azurite + PostgreSQL) com cliente Drive falso.
