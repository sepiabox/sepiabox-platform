## S-011 — Ver vídeos na página de detalhe
**Épico:** App web
**Depende de:** S-006, S-010
**Prioridade:** Média
**Tamanho:** S

### Contexto
Os vídeos têm capa e versão 720p (S-006); a web tem de os mostrar e tocar.

### Âmbito
- Grelha: capa do vídeo com ícone de play e duração (`m:ss`).
- Detalhe de vídeo: `<video controls>` com `src` SAS do `proxy-720p.mp4` e `poster` SAS.
- Sem proxy: mensagem "Vídeo ainda em processamento".

### Fora de âmbito
- Transcodificação na web.

### Critérios de aceitação
- [ ] Dado um asset de vídeo com proxy, quando abro o detalhe, então existe `<video>` com `src` SAS do proxy e `poster`.
- [ ] Dado um vídeo de 3 s, então o cartão mostra "0:03".
- [ ] Dado um vídeo sem proxy, então vejo "Vídeo ainda em processamento".

### Notas técnicas
- Componentes `VideoPlayer.razor`, `MediaCard.razor`.

### Testes esperados
- bUnit: renderização do vídeo, duração, mensagem sem proxy.
