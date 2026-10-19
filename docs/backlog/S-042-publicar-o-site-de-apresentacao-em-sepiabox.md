## S-042 — Publicar o site de apresentação em sepiabox.com
**Épico:** Site
**Depende de:** nenhuma
**Prioridade:** Baixa
**Tamanho:** S

### Contexto
`sepiabox.com` apresenta o projeto enquanto a app é privada. **Repositório: `sepiabox-site`.**

### Âmbito
- Página única estática (HTML + CSS, sem framework) em português: cabeçalho com símbolo (copiar `assets/symbol.svg` de `sepiabox-brand`) e "sepiabox"; hero "Pergunte às suas fotos." com cartão de exemplo ("Quantos casamentos fomos em 2026?" → "4 casamentos este ano"); "Junta o que hoje está espalhado" (HD externo, Google Fotos, Google Drive, OneDrive, iPhone e Android em breve); "Como funciona" (Junte, Entenda, Pergunte); seis perguntas de exemplo; faixa de privacidade (fotos suas, rostos só se ligar, guardadas na Europa, código aberto); rodapé com ligação ao GitHub.
- Cores e tipos de `docs/architecture.md` do `sepiabox-platform` (Identidade visual).
- Workflow de GitHub Pages; ficheiro `CNAME` com `sepiabox.com`.

### Fora de âmbito
- Lista de espera com backend.

### Critérios de aceitação
- [ ] Dado o HTML, então `html-validate` passa no CI.
- [ ] Dado a página a 375 px de largura (Playwright), então não há deslocamento horizontal (`scrollWidth <= clientWidth`).
- [ ] Dado a página, então existem as secções `#como`, `#perguntas` e `#privacidade`.

### Notas técnicas
- DNS do domínio é passo humano (`docs/operations.md`).
- O código do site é MIT (o `LICENSE` já existe); o símbolo copiado de `sepiabox-brand` não é: o rodapé diz "Nome e logótipo Sepiabox: todos os direitos reservados" (D-010).

### Testes esperados
- CI: `html-validate` e um teste Playwright de largura móvel.
