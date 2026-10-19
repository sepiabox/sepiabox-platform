## S-009 — Navegar pela biblioteca numa linha do tempo
**Épico:** App web
**Depende de:** S-008
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Primeira vez que a família vê as fotos do HD no navegador. A web mostra miniaturas via URLs SAS de curta duração, nunca expondo o armazenamento.

### Âmbito
- Página `/`: grelha de miniaturas agrupada por mês (mais recente primeiro), assets sem data num grupo "Sem data" no fim, paginação de 200.
- Página `/fotos/{id}`: pré-visualização 1280, data, câmara, dimensões, fontes, ligação para o original.
- `SasUrlFactory`: SAS de leitura para `thumb-256`/`preview-1280` válida 15 min; original 5 min. Azure: SAS de delegação de utilizador (identidade gerida). Azurite: SAS com chave da conta.
- Layout desktop (largura mínima ~1024 px), cores e tipos de `docs/architecture.md` (Identidade visual), textos em português.

### Fora de âmbito
- Login (S-010), filtros (S-013).

### Critérios de aceitação
- [ ] Dado 3 assets em meses diferentes, quando abro `/`, então vejo 3 grupos por mês em ordem decrescente.
- [ ] Dado um asset sem data, então aparece em "Sem data" no fim.
- [ ] Dado uma miniatura, então o seu URL é SAS com expiração ≤ 15 min e só permissão de leitura.
- [ ] Dado `/fotos/{id}` inexistente, então a resposta é 404.

### Notas técnicas
- `Sepiabox.Web/Components/Pages/Timeline.razor`, `PhotoDetail.razor`, `Services/SasUrlFactory.cs`, `Services/LibraryQueries.cs`.
- Testes de componentes com bUnit; consultas com PostgreSQL de teste.
- Decisões: D-004, D-018.

### Testes esperados
- Unitários: `SasUrlFactory` (expiração, permissões). bUnit: agrupamento e "Sem data". Integração: 404 e consulta paginada.
