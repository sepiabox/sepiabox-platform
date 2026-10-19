## S-039 — Ligar e sincronizar o OneDrive
**Épico:** Fontes na nuvem
**Depende de:** S-036, S-010
**Prioridade:** Média
**Tamanho:** M

### Contexto
O mesmo que o Google Drive, para contas Microsoft pessoais, via Microsoft Graph.

### Âmbito
- Ligar conta Microsoft com scopes `Files.Read offline_access`; token protegido como em S-037 (reutilizar `connected_sources`).
- Job diário: `delta` em `/me/drive/root` filtrando imagens e vídeos; `deltaLink` guardado; ficheiros novos para `incoming` em modo referência; removidos marcados.
- Estado "Reconectar" em `invalid_grant`.

### Fora de âmbito
- SharePoint, contas de empresa.

### Critérios de aceitação
- [ ] Dado um cliente Graph falso, quando a primeira sincronização corre, então os ficheiros de imagem/vídeo entram em `incoming` e os restantes não.
- [ ] Dado o `deltaLink`, quando a segunda sincronização corre, então só as alterações entram.
- [ ] Dado `invalid_grant`, então o estado passa a "Reconectar".

### Notas técnicas
- Pacote `Microsoft.Graph`. Interface `IOneDriveClient` para testes.

### Testes esperados
- Integração com cliente falso.
