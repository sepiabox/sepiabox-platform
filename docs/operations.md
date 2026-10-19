# Operações

Passos que exigem uma pessoa, porque dependem de contas, consentimentos ou registos externos. As stories que dependem destes passos dizem-no nas notas técnicas; o código e os testes automáticos dessas stories não dependem deles.

## Pré-requisitos humanos (uma vez)

| Passo | Necessário para | Onde |
|---|---|---|
| Subscrição Azure com faturação em EUR e acesso de Owner ao grupo `rg-sepiabox` | S-029 em diante (deploy) | Portal Azure |
| App registration no Entra ID com credencial federada (OIDC) para `sepiabox/sepiabox-platform` e `sepiabox/sepiabox-infra`, ambiente `production` | S-029, S-034 | Portal Azure → Entra ID |
| Variáveis do GitHub `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` e ambiente `production` com aprovação manual | S-029, S-034 | GitHub → Settings |
| Cliente OAuth Google (modo Testing, os dois e-mails como test users; Drive API ativada) | S-010, S-037 | Google Cloud Console |
| App registration Microsoft para contas pessoais (login e Files.Read) | S-010, S-039 | Portal Azure → Entra ID |
| Implantação de `claude-haiku-5-5` no Microsoft Foundry e chave no Key Vault | S-025 | Microsoft Foundry |
| Registos DNS na GoDaddy: `app` (CNAME + TXT `asuid.app`) e `sepiabox.com` para o GitHub Pages | S-035, S-042 | GoDaddy |
| Lista de e-mails autorizados (`Access:Emails`) no Key Vault | S-010, S-032 | Key Vault |

## URLs de retorno OAuth

- Local: `https://localhost:<porta>/signin-google`, `/signin-microsoft`
- Produção: `https://app.sepiabox.com/signin-google`, `/signin-microsoft`

## Desligamento de emergência

Ver `sepiabox-infra/scripts/emergency-stop.sh` (desliga o tráfego da web, para o PostgreSQL e desativa a fila; mantém os dados).
