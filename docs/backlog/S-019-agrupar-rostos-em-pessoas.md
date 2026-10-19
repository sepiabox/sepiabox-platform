## S-019 — Agrupar rostos em pessoas
**Épico:** Pessoas
**Depende de:** S-018, S-010
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Rostos parecidos tornam-se uma pessoa sem nome; a família só dá nomes (curadoria).

### Âmbito
- Tabelas `people` (id, name nulo, user_email nulo, face_count, centroid `vector(128)`) e `faces` (id, asset_id, box, score, embedding `vector(128)`, person_id, assignment `auto|manual|excluded`).
- Na importação: cosseno com o centróide mais próximo ≥ 0,40 → atribui; senão nova pessoa. Recalcula centróide e contagem.
- Página `/pessoas`: pessoas por número de rostos, recorte do rosto a partir da miniatura (CSS com a caixa relativa), "Sem nome" quando aplicável.

### Fora de âmbito
- Renomear e juntar (S-020).

### Critérios de aceitação
- [ ] Dado dois rostos com embeddings quase iguais, então ficam na mesma pessoa.
- [ ] Dado dois rostos distantes, então ficam em pessoas diferentes.
- [ ] Dado o mesmo manifesto importado duas vezes, então os rostos não duplicam.
- [ ] Dado a página `/pessoas`, então as pessoas aparecem por ordem decrescente de rostos.

### Notas técnicas
- `Sepiabox.Catalog/People/FaceAssigner.cs`. Limiar em configuração.
- Decisões: D-016, D-018.

### Testes esperados
- Unitários: atribuição com vetores sintéticos. Integração: importação idempotente. bUnit: ordenação.
