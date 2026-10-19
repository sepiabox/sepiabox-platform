## S-040 — Exportar todos os dados
**Épico:** Privacidade
**Depende de:** S-020, S-010
**Prioridade:** Média
**Tamanho:** S

### Contexto
"Suas fotos são suas": a qualquer momento, a família pode levar tudo, em formatos abertos.

### Âmbito
- `/definicoes/exportar`: pede uma exportação; o worker gera `exports/<data>.zip` com `catalog.json` (assets, fontes, pessoas com nomes, ocasiões, lugares pessoais), `originals.txt` (uma linha por original com SAS de 24 h) e `README.txt`.
- Dados de rosto (embeddings) só com a opção "incluir dados de rosto".
- Ligação de download SAS de 1 h.

### Fora de âmbito
- Cópia física dos originais para o zip.

### Critérios de aceitação
- [ ] Dado uma biblioteca semeada, então `catalog.json` contém todos os assets.
- [ ] Dado a exportação por defeito, então não contém embeddings de rosto.
- [ ] Dado `originals.txt`, então cada SAS expira em ≤ 24 h.

### Notas técnicas
- `System.IO.Compression.ZipArchive` em streaming para o blob.

### Testes esperados
- Integração (Azurite + PostgreSQL): conteúdo do zip e expirações.
