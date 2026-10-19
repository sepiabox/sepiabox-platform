## S-033 — Importar manifestos automaticamente quando chegam
**Épico:** Nuvem e custos
**Depende de:** S-032, S-008
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Cada manifesto enviado pelo PC tem de ser importado sem intervenção e sem risco de laço infinito ou custo descontrolado (D-008).

### Âmbito
- Service Bus Basic com fila `manifests` (`maxDeliveryCount` 5, dead-letter).
- Event Grid: tópico de sistema da conta de Storage; subscrição `BlobCreated` filtrada por `subjectBeginsWith: /blobServices/default/containers/manifests/` → fila.
- Worker como Container Apps Job por evento (KEDA `azure-servicebus`): paralelismo 1, uma execução de cada vez, timeout 30 min, 1 vCPU / 2 GiB; comando `import --from-queue` processa mensagens até a fila esvaziar.

### Fora de âmbito
- Processar ficheiros sem manifesto (S-036).

### Critérios de aceitação
- [ ] Dado um payload BlobCreated do Event Grid, então o worker extrai o nome do manifesto.
- [ ] Dado um evento de outro contentor, então é ignorado.
- [ ] Dado o Bicep gerado, então a subscrição tem o filtro `subjectBeginsWith` e a fila tem `maxDeliveryCount` 5.
- [ ] Dado o Bicep gerado, então o job tem paralelismo 1.

### Notas técnicas
- `Azure.Messaging.ServiceBus`. Recursos adicionados via Aspire (`AddAzureServiceBus`) e `ConfigureInfrastructure` para o Event Grid.

### Testes esperados
- Unitários: parsing do evento, filtro. Teste de geração de infraestrutura.
