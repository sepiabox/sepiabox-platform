# Backlog do Sepiabox

Backlog executável por agentes autónomos. Cada story é uma fatia vertical, pequena (até ~400 linhas alteradas), depende só de stories anteriores e traz todo o contexto necessário. Fontes: a conversa de desenho do produto e [`docs/`](..). Decisões tomadas por defeito estão em [`docs/decisions.md`](../decisions.md).

## Como usar

1. Escolher a primeira story `todo` na ordem abaixo cujas dependências estejam todas `done`.
2. Mudar o estado para `doing` nesta tabela, no mesmo PR da implementação.
3. Implementar até todos os critérios de aceitação e testes esperados passarem, com o CI verde.
4. Mudar o estado para `done` no PR. Se faltar um passo humano ([`docs/operations.md`](../operations.md)), marcar `blocked` e dizer qual.

Estados: `todo` · `doing` · `blocked` · `done`.

## Ordem de execução

### Sprint 1 — Fundação e primeira leitura do HD

**Incremento demonstrável:** `sepiabox-ingest process` gera manifestos, miniaturas e versões 720p de uma pasta real; `dotnet run --project src/Sepiabox.AppHost` arranca PostgreSQL e Azurite.

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-001 | [Preparar a fundação do repositório](S-001-preparar-a-fundacao-do-repositorio.md) | Fundação | — | Alta | M | `doing` |
| S-002 | [Ler um disco e relatar o que contém](S-002-ler-um-disco-e-relatar-o-que.md) | Ingestão local | S-001 | Alta | M | `doing` |
| S-003 | [Arrancar o ambiente local com .NET Aspire](S-003-arrancar-o-ambiente-local-com-net-aspire.md) | Fundação | S-001 | Alta | M | `todo` |
| S-004 | [Descrever cada foto e vídeo num manifesto](S-004-descrever-cada-foto-e-video-num-manifesto.md) | Ingestão local | S-002 | Alta | M | `todo` |
| S-005 | [Gerar miniaturas das fotos](S-005-gerar-miniaturas-das-fotos.md) | Ingestão local | S-004 | Alta | S | `todo` |
| S-006 | [Gerar capas e versões 720p dos vídeos](S-006-gerar-capas-e-versoes-720p-dos-videos.md) | Ingestão local | S-004 | Alta | M | `todo` |

### Sprint 2 — Ver as fotos no navegador

**Incremento demonstrável:** Depois de `upload`, entrar localmente com Google ou Microsoft e percorrer as fotos e vídeos do HD por data; e-mails fora da lista são recusados.

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-007 | [Enviar originais, derivados e manifestos para o armazenamento](S-007-enviar-originais-derivados-e-manifestos-para-o.md) | Ingestão local | S-003, S-004 | Alta | M | `todo` |
| S-008 | [Importar manifestos para o catálogo](S-008-importar-manifestos-para-o-catalogo.md) | Catálogo na nuvem | S-003, S-007 | Alta | M | `todo` |
| S-009 | [Navegar pela biblioteca numa linha do tempo](S-009-navegar-pela-biblioteca-numa-linha-do-tempo.md) | App web | S-008 | Alta | M | `todo` |
| S-010 | [Entrar com Google ou Microsoft e permitir só a lista de acesso](S-010-entrar-com-google-ou-microsoft-e-permitir.md) | App web | S-009 | Alta | M | `todo` |
| S-011 | [Ver vídeos na página de detalhe](S-011-ver-videos-na-pagina-de-detalhe.md) | App web | S-006, S-010 | Média | S | `todo` |

### Sprint 3 — Encontrar por data, lugar e semelhança

**Incremento demonstrável:** Filtrar "Lisboa, dezembro de 2019", pesquisar "praia ao pôr do sol" e abrir "mais parecidas com esta".

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-012 | [Dar nome aos lugares com GeoNames](S-012-dar-nome-aos-lugares-com-geonames.md) | Catálogo na nuvem | S-008 | Alta | M | `todo` |
| S-013 | [Filtrar a biblioteca com a DSL de pesquisa v1](S-013-filtrar-a-biblioteca-com-a-dsl-de.md) | Pesquisa | S-012 | Alta | M | `todo` |
| S-014 | [Guardar lugares pessoais e filtrar por eles](S-014-guardar-lugares-pessoais-e-filtrar-por-eles.md) | Pesquisa | S-013 | Média | S | `todo` |
| S-015 | [Calcular embeddings CLIP no processamento](S-015-calcular-embeddings-clip-no-processamento.md) | Pesquisa | S-005, S-008 | Alta | M | `todo` |
| S-016 | [Pesquisar fotos por descrição](S-016-pesquisar-fotos-por-descricao.md) | Pesquisa | S-013, S-015 | Alta | M | `todo` |
| S-017 | [Mostrar mais fotos parecidas com esta](S-017-mostrar-mais-fotos-parecidas-com-esta.md) | Pesquisa | S-015, S-013 | Média | S | `todo` |

### Sprint 4 — Pessoas (opt-in)

**Incremento demonstrável:** Dar nome a grupos de rostos e filtrar "fotos minhas com a Ana e o Pedro"; desligar o reconhecimento apaga os dados de rosto.

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-018 | [Detetar rostos no processamento](S-018-detetar-rostos-no-processamento.md) | Pessoas | S-005, S-015 | Alta | M | `todo` |
| S-019 | [Agrupar rostos em pessoas](S-019-agrupar-rostos-em-pessoas.md) | Pessoas | S-018, S-010 | Alta | M | `todo` |
| S-020 | [Dar nome, juntar e corrigir pessoas](S-020-dar-nome-juntar-e-corrigir-pessoas.md) | Pessoas | S-019 | Alta | M | `todo` |
| S-021 | [Filtrar por pessoas](S-021-filtrar-por-pessoas.md) | Pessoas | S-020, S-013 | Alta | S | `todo` |
| S-022 | [Desligar o reconhecimento facial e apagar dados de rostos](S-022-desligar-o-reconhecimento-facial-e-apagar-dados.md) | Pessoas | S-019 | Média | S | `todo` |

### Sprint 5 — Ocasiões e perguntas

**Incremento demonstrável:** Perguntar "Quantos casamentos fomos em 2026?" e receber o número com as ocasiões; ver fotos tremidas, rajadas e o espaço por fonte.

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-023 | [Agrupar fotos em ocasiões](S-023-agrupar-fotos-em-ocasioes.md) | Ocasiões | S-012 | Média | M | `todo` |
| S-024 | [Classificar ocasiões por tipo](S-024-classificar-ocasioes-por-tipo.md) | Ocasiões | S-023, S-016 | Média | M | `todo` |
| S-025 | [Fazer perguntas em português com o Claude Haiku](S-025-fazer-perguntas-em-portugues-com-o-claude.md) | Perguntas | S-016, S-021, S-024, S-014 | Média | M | `todo` |
| S-026 | [Responder a perguntas de contagem](S-026-responder-a-perguntas-de-contagem.md) | Perguntas | S-025 | Média | M | `todo` |
| S-027 | [Encontrar fotos tremidas e rajadas](S-027-encontrar-fotos-tremidas-e-rajadas.md) | Organização | S-005, S-013 | Baixa | M | `todo` |
| S-028 | [Ver o espaço e as fontes do acervo](S-028-ver-o-espaco-e-as-fontes-do.md) | Organização | S-026 | Baixa | S | `todo` |

### Sprint 6 — Na nuvem com custos sob controlo

**Incremento demonstrável:** A app corre em `app.sepiabox.com`, publicada pelo GitHub Actions, com orçamento, alertas e desligamento automático aos €45 ativos.

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-029 | [Proteger o orçamento com alertas](S-029-proteger-o-orcamento-com-alertas.md) | Nuvem e custos | — | Alta | S | `todo` |
| S-030 | [Desligar tudo automaticamente aos €45](S-030-desligar-tudo-automaticamente-aos-45.md) | Nuvem e custos | S-029 | Alta | M | `todo` |
| S-031 | [Restringir tipos de recurso e limitar registos](S-031-restringir-tipos-de-recurso-e-limitar-registos.md) | Nuvem e custos | S-029 | Alta | S | `todo` |
| S-032 | [Publicar a app no Azure com azd](S-032-publicar-a-app-no-azure-com-azd.md) | Nuvem e custos | S-010, S-031 | Alta | M | `todo` |
| S-033 | [Importar manifestos automaticamente quando chegam](S-033-importar-manifestos-automaticamente-quando-chegam.md) | Nuvem e custos | S-032, S-008 | Alta | M | `todo` |
| S-034 | [Publicar a partir do GitHub Actions com OIDC](S-034-publicar-a-partir-do-github-actions-com.md) | Nuvem e custos | S-032 | Média | S | `todo` |
| S-035 | [Servir a app em app.sepiabox.com](S-035-servir-a-app-em-app-sepiabox-com.md) | Nuvem e custos | S-032 | Baixa | S | `todo` |

### Sprint 7 — Fontes na nuvem, privacidade e site

**Incremento demonstrável:** Fotos do Google Drive e OneDrive aparecem na linha do tempo; exportação completa num zip; `sepiabox.com` publicado.

| Story | Título | Épico | Depende de | Prioridade | Tamanho | Estado |
|---|---|---|---|---|---|---|
| S-036 | [Processar na nuvem ficheiros que chegam sem manifesto](S-036-processar-na-nuvem-ficheiros-que-chegam-sem.md) | Fontes na nuvem | S-033, S-006, S-015 | Média | M | `todo` |
| S-037 | [Ligar uma conta Google Drive](S-037-ligar-uma-conta-google-drive.md) | Fontes na nuvem | S-010 | Média | M | `todo` |
| S-038 | [Sincronizar fotos do Google Drive em modo referência](S-038-sincronizar-fotos-do-google-drive-em-modo.md) | Fontes na nuvem | S-037, S-036 | Média | M | `todo` |
| S-039 | [Ligar e sincronizar o OneDrive](S-039-ligar-e-sincronizar-o-onedrive.md) | Fontes na nuvem | S-036, S-010 | Média | M | `todo` |
| S-040 | [Exportar todos os dados](S-040-exportar-todos-os-dados.md) | Privacidade | S-020, S-010 | Média | S | `todo` |
| S-041 | [Encontrar fotos parecidas com uma região escolhida](S-041-encontrar-fotos-parecidas-com-uma-regiao-escolhida.md) | Pesquisa | S-017, S-023 | Baixa | S | `todo` |
| S-042 | [Publicar o site de apresentação em sepiabox.com](S-042-publicar-o-site-de-apresentacao-em-sepiabox.md) | Site | — | Baixa | S | `todo` |

## Épicos

| Épico | Stories |
|---|---|
| Fundação | S-001, S-003 |
| Ingestão local | S-002, S-004, S-005, S-006, S-007 |
| Catálogo na nuvem | S-008, S-012 |
| App web | S-009, S-010, S-011 |
| Pesquisa | S-013, S-014, S-015, S-016, S-017, S-041 |
| Pessoas | S-018, S-019, S-020, S-021, S-022 |
| Ocasiões | S-023, S-024 |
| Perguntas | S-025, S-026 |
| Organização | S-027, S-028 |
| Nuvem e custos | S-029, S-030, S-031, S-032, S-033, S-034, S-035 |
| Fontes na nuvem | S-036, S-037, S-038, S-039 |
| Privacidade | S-040 |
| Site | S-042 |

## Validação

- **Dependências:** todas apontam para stories com número menor e num sprint igual ou anterior; não há ciclos nem dependências para a frente (verificado por script ao gerar este índice).
- **Tamanho:** 14 stories S, 28 M, nenhuma L.
- **Sprints:** 7 sprints, entre 5 e 7 stories cada.
- **Decisões cobertas:** D-001 a D-024 de [`decisions.md`](../decisions.md) são aplicadas: cada uma é citada nas notas técnicas de pelo menos uma story, exceto a D-021, que é aplicada por este próprio backlog.
- **S-001 e S-002** estão `doing`: o PR #1 do `sepiabox-platform` (comando `scan`) já entrega a maior parte das duas; falta o que cada story pede além disso (lint, `CLAUDE.md`, `docs/architecture.md` ligado no README).

### Discutido na sessão mas fora deste backlog

| Tema | Motivo |
|---|---|
| Google Photos (Picker API / Takeout com JSON auxiliares) | A Library API deixou de dar acesso à biblioteca inteira; o Takeout funciona como pasta local e pode ser lido pelo `process` depois de extraído. Fica para quando houver uso real. |
| Dropbox e iCloud | Não usados pela família hoje; o modelo de fontes (S-037 a S-039) permite acrescentar depois. |
| Apps móveis e login com Apple | Fase 3; o login Apple só faz sentido com a app iOS (D-006). |
| Deteção dedicada de objetos e roupas ("vestido vermelho") | Na v1 coberto pela pesquisa visual CLIP (S-016, S-041); um detetor dedicado é fase 3. |
| OCR e transcrição de vídeo | Fase 3; custo de processamento sem pergunta-alvo na v1. |
| Grafo de relações e legendas por modelo de visão ("o filho do Paulo") | Fase 3; exige relações entre pessoas e um modelo de visão generativo. |
| Encriptação por utilizador (crypto-shredding) | Necessária só para o produto multi-família; dois utilizadores numa biblioteca partilhada (D-018). |
| Plano de negócio, preços e faturação | Vive no repositório privado `sepiabox-planning`; não há produto vendável na v1. |
| Logótipo-palavra em curvas e lista de espera do site | Precisa de ferramenta de design / backend; o site da S-042 usa o símbolo e o nome em texto, sem formulário. |
| Passos humanos (subscrição, OIDC, clientes OAuth, DNS, Foundry) | Não automatizáveis; listados em [`operations.md`](../operations.md) e referidos nas stories que dependem deles. |

## Resumo

- **Épicos:** 13
- **Sprints:** 7
- **Stories:** 42
- **Caminho crítico (13 stories):** S-001 → S-002 → S-004 → S-007 → S-008 → S-015 → S-018 → S-019 → S-020 → S-021 → S-025 → S-026 → S-028
