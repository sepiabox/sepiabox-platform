# Arquitetura do Sepiabox

Resumo da arquitetura decidida para a primeira versão. Decisões individuais, com alternativas e motivos, estão em [decisions.md](decisions.md). Passos que exigem uma pessoa estão em [operations.md](operations.md).

## O produto

O Sepiabox junta as fotos e vídeos de uma família (HD externo, Google Drive, OneDrive e, mais tarde, telemóveis) num só lugar e encontra qualquer momento quando se faz uma pergunta. Cada ficheiro é interpretado **uma única vez, na entrada**; a pesquisa só filtra os metadados guardados e nunca volta a ler as fotos.

Exemplos de perguntas que o sistema tem de responder:

- "Fotos minhas com a Ana e o Pedro juntos" (pessoas)
- "Fotos de Lisboa em dezembro", "O que fazia neste dia há cinco anos?" (tempo e lugar)
- "Fotos na neve", "Mais fotos parecidas com esta" (semelhança visual)
- "Quantos casamentos fomos este ano?" (ocasiões)
- "Quantas fotos do HD já estavam no Google Drive?" (organização)

Utilizadores na v1: **duas pessoas** (um casal), com uma **biblioteca partilhada**. Não há registo aberto.

## Visão geral

```
PC com o HD ──sepiabox-ingest──► Blob Storage ──Event Grid──► Service Bus ──► Worker (Container Apps job) ──► PostgreSQL
 (processa localmente)            originals (Cold)                               importa manifestos               catálogo + pgvector + PostGIS
                                  derivatives (Hot)
                                  manifests                                                                            ▲
                                                                                                                       │ SQL + kNN
Navegador ──HTTPS──► Web (Blazor, Container Apps) ── login Google/Microsoft + lista de acesso ──────────────────────────┘
                         │
                         └── pergunta em texto ──► Claude Haiku 5.5 (Microsoft Foundry) ──► filtros (DSL JSON)
```

## Componentes (.NET 10)

| Projeto | Onde corre | Responsabilidade |
|---|---|---|
| `Sepiabox.Pipeline` | biblioteca | Estágios de processamento: classificação, hash, metadados, miniaturas, vídeo, embeddings, rostos. Não sabe de onde vem o ficheiro. |
| `Sepiabox.Ingest` (`sepiabox-ingest`) | PC do utilizador | `scan`, `process`, `upload`, `models download`. Processa o HD localmente e envia só o que é novo. |
| `Sepiabox.Catalog` | biblioteca | EF Core + Npgsql: esquema, migrações, consultas. |
| `Sepiabox.Worker` (`sepiabox-worker`) | Container Apps job | Importa manifestos, processa ficheiros que chegam pela nuvem, agrupa eventos, sincroniza fontes na nuvem. |
| `Sepiabox.Web` | Container Apps | Blazor (interactive server): login, linha do tempo, pesquisa, pessoas, ocasiões, perguntas. |
| `Sepiabox.AppHost` / `Sepiabox.ServiceDefaults` | PC (dev) / azd | .NET Aspire: ambiente local (PostgreSQL + Azurite) e descrição da infraestrutura para `azd`. |

## Fluxo de ingestão

1. `sepiabox-ingest scan <pasta>`: só lê; conta fotos/vídeos, anos, duplicados, ficheiros ilegíveis.
2. `sepiabox-ingest process <pasta> --out <workdir>`: por ficheiro calcula SHA-256 (identidade do asset), lê EXIF/QuickTime (data, GPS, câmara, dimensões), gera miniaturas WebP, poster e versão 720p dos vídeos, embedding CLIP e, se ativado, rostos. Escreve um **manifesto JSON por asset** (schema v1, `docs/schemas/manifest.v1.json`). Estado em SQLite para retomar.
3. `sepiabox-ingest upload <workdir>`: envia originais (camada **Cold**), derivados (**Hot**) e, por último, o manifesto. Retoma onde parou.
4. Na nuvem, cada manifesto novo dispara a importação (Event Grid filtrado ao contentor `manifests` → fila → job do worker, uma execução de cada vez).

Os originais **nunca são relidos** na nuvem durante a ingestão local (ler a camada Cold custa ~US$ 0,03/GB). Ficheiros que chegam pela nuvem (Drive, OneDrive) passam pelo mesmo pipeline no worker.

## Dados

- **Blob Storage**: `originals` (Cold), `derivatives` (Hot), `manifests`, `incoming`, `exports`. Acesso público desligado; o navegador só lê por URLs SAS de curta duração geradas após login.
- **PostgreSQL Flexible (B1ms)** com **pgvector** (embeddings CLIP 512-d e de rosto 128-d, índices HNSW) e **PostGIS** (localização, distância, lugares). Tabelas principais: `assets`, `asset_sources`, `derivatives`, `asset_embeddings`, `faces`, `people`, `events`, `saved_places`, `geonames`.
- **Nomes de lugares**: GeoNames `cities500` carregado no PostgreSQL (offline, sem Azure Maps). Lugares pessoais ("Casa", "Casa da praia") são pontos com raio guardados pelo utilizador.

## Modelos de ML (ONNX Runtime, CPU por defeito, CUDA opcional)

- **CLIP ViT-B/32** (MIT): embedding de imagem (pesquisa por descrição, "parecidas", tipos de ocasião por zero-shot).
- **YuNet** (deteção de rosto, MIT) + **SFace** (embedding de rosto, Apache-2.0), da OpenCV Zoo. Rostos são **opt-in**.
- Modelos não vão para o repositório: são descarregados por `sepiabox-ingest models download` com SHA-256 fixado em `models/models.json`.

## Pesquisa

Toda a pesquisa passa por uma **DSL JSON** (`docs/schemas/search.v1.json`): filtros de tempo, lugar, pessoas, fonte, tipo, ocasião, semelhança visual e agregações. A UI de filtros e o tradutor de perguntas produzem a mesma DSL; um único serviço a executa (SQL + kNN no pgvector).

Perguntas em linguagem natural: o texto (nunca as fotos) vai para **Claude Haiku 5.5** (`claude-haiku-5-5`) no **Microsoft Foundry**, com *structured outputs* restritos ao schema da DSL. Termos visuais são traduzidos para inglês (o CLIP é inglês). Limite diário de perguntas por utilizador.

## Acesso e privacidade

- Login com **Google** e **Microsoft** (Apple virá com a app iOS). Uma **lista de e-mails** autorizados é verificada no retorno do login (sem lista, sem cookie) e uma política de autorização por defeito protege todas as rotas.
- Reconhecimento facial opcional; pode ser desligado e os dados de rosto apagados. Nenhuma inferência de categorias sensíveis (religião, saúde).
- Exportação completa a qualquer momento.

## Nuvem e custos

- Azure, região **France Central**, grupo de recursos `rg-sepiabox`. Teto: **€50/mês**; estimativa ~US$ 30–35/mês com 1 TB.
- Container Apps (web escala a zero; worker como job), PostgreSQL B1ms, Storage, Service Bus Basic, Event Grid, Key Vault, Application Insights com limite diário, Container Registry Basic.
- Proteções (repositório `sepiabox-infra`): orçamento €50 com alertas em €25/€40/previsão, alertas de anomalia, desligamento automático aos €45, Azure Policy de tipos de recurso permitidos, limite de logs.
- Deploy: `azd` a partir do AppHost; GitHub Actions com OIDC (sem segredos no repositório).

## Repositórios (organização `sepiabox`)

| Repositório | Conteúdo |
|---|---|
| `sepiabox-platform` | Este repositório: código .NET, docs, backlog |
| `sepiabox-infra` | Orçamento, alertas, desligamento, Policy (Bicep) |
| `sepiabox-site` | Site estático `sepiabox.com` (GitHub Pages) |
| `sepiabox-brand` | Logo, cores, tipografia (todos os direitos reservados) |
| `sepiabox-planning` | Privado: plano de negócio |

## Identidade visual (para a UI)

Cores: Sépia `#9A5A2C` (marca), Cianótipo `#1F4C74` (ações), Revelador `#231A14` (texto), Papel prata `#EEECE7` (fundo), Moldura `#D8D1C6` (linhas). Tipos: Bricolage Grotesque (títulos), Figtree (texto), IBM Plex Mono (filtros e metadados). Símbolo em `sepiabox-brand/assets/`. Interface em **português**; código, comentários e nomes técnicos em **inglês**. Web pensada para **desktop** (largura mínima ~1024 px); no telemóvel só precisa de abrir sem partir.

## Fases

- **MVP**: HD local → nuvem, linha do tempo, filtros por data/lugar, pesquisa visual, pessoas, login restrito, deploy com proteções de custo.
- **Fase 2**: ocasiões e tipos, perguntas em linguagem natural, Google Drive e OneDrive, organização (tremidas, rajadas), exportação.
- **Fase 3 (fora deste backlog)**: grafo de relações entre pessoas, legendas por modelo de visão, apps móveis, deteção de objetos/roupas dedicada, OCR e transcrição.
