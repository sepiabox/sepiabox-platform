## S-037 — Ligar uma conta Google Drive
**Épico:** Fontes na nuvem
**Depende de:** S-010
**Prioridade:** Média
**Tamanho:** M

### Contexto
Muitas fotos da família estão no Google Drive. Primeiro passo: ligar a conta e guardar o token com segurança.

### Âmbito
- Página `/fontes`: "Ligar Google Drive" (OAuth incremental, scope `drive.readonly`, acesso offline).
- Tabela `connected_sources` (id, type, account_email, encrypted_refresh_token, status, last_sync_at); token protegido com ASP.NET Data Protection (chaves em Blob, protegidas por chave do Key Vault em produção).
- Contagem de fotos e vídeos encontrados (`files.list` com `mimeType` imagem/vídeo), sem importar.
- Desligar apaga o token. Token expirado (modo Testing, 7 dias) → estado "Reconectar".

### Fora de âmbito
- Sincronização (S-038).

### Critérios de aceitação
- [ ] Dado o callback OAuth, então o token é gravado encriptado (o valor na base não contém o token em claro).
- [ ] Dado um cliente Drive falso com 12 imagens e 3 vídeos, então a página mostra 12 e 3.
- [ ] Dado `invalid_grant`, então o estado passa a "Reconectar".
- [ ] Dado "Desligar", então o token é apagado.

### Notas técnicas
- Pacote `Google.Apis.Drive.v3`. Interface `IDriveClient` para testes.
- Decisões: D-024.

### Testes esperados
- Unitários: proteção do token, estados. Integração: callback e desligar com cliente falso.
