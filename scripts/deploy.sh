#!/usr/bin/env bash
# Deploys Sepiabox to Azure (S-032) in the only safe order: the infrastructure, the migrate job's new
# image, one run of `sepiabox-worker migrate` against the cloud database, and only when it succeeded the
# web's new revision. A failed or stuck migration stops here and the running web is left as it was.
#
# Needs azd, the Azure CLI with the containerapp extension, the .NET SDK and Docker, signed in to both
# CLIs, and the azd environment `sepiabox` in francecentral (docs/operations.md).
set -euo pipefail

readonly environment_name="sepiabox"
readonly location="francecentral"
readonly resource_group="rg-sepiabox"
readonly postgres_server="sepiabox-pg"
readonly migrate_job="sepiabox-migrate"
# The job's replica timeout is 10 minutes; the default leaves time for a cold start of the environment.
# Both can be overridden, which the tests use to avoid waiting.
readonly migrate_timeout_seconds="${SEPIABOX_MIGRATE_TIMEOUT_SECONDS:-900}"
readonly poll_seconds="${SEPIABOX_MIGRATE_POLL_SECONDS:-10}"

cd "$(dirname "$0")/.."

# azd names the resource group rg-<environment> and creates it in the environment's region.
if [[ "$(azd env get-value AZURE_ENV_NAME)" != "$environment_name" ]]; then
  echo "Select the azd environment '$environment_name' first: azd env select $environment_name" >&2
  exit 1
fi
if [[ "$(azd env get-value AZURE_LOCATION)" != "$location" ]]; then
  echo "The azd environment must be in $location: azd env set AZURE_LOCATION $location" >&2
  exit 1
fi
# Unset before the first provision; afterwards it must be the group the rest of this script uses.
azd_group=$(azd env get-value AZURE_RESOURCE_GROUP 2>/dev/null) || azd_group=""
if [[ -n "$azd_group" && "$azd_group" != "$resource_group" ]]; then
  echo "The azd environment deploys to $azd_group, not $resource_group." >&2
  exit 1
fi

# After the €45 shutdown (S-030) the server is stopped and provisioning would fail half-way.
postgres_state=$(az postgres flexible-server show --name "$postgres_server" --resource-group "$resource_group" \
  --query state --output tsv 2>/dev/null) || postgres_state=""
if [[ "$postgres_state" == "Stopped" ]]; then
  echo "$postgres_server is stopped (the budget shutdown?). Resume first: sepiabox-infra/scripts/resume.sh" >&2
  exit 1
fi

# Lets azure.yaml's predeploy hook through.
export SEPIABOX_DEPLOY_SCRIPT=1

azd provision --no-prompt
azd deploy migrate --no-prompt

execution=$(az containerapp job start --name "$migrate_job" --resource-group "$resource_group" --query name --output tsv)
echo "Waiting for $migrate_job execution $execution..."
deadline=$((SECONDS + migrate_timeout_seconds))
status="Unknown"
while true; do
  # A failed query (a network blip, a throttled call) is not the job's status: keep polling.
  status=$(az containerapp job execution show --name "$migrate_job" --resource-group "$resource_group" \
    --job-execution-name "$execution" --query properties.status --output tsv) || status="Unknown"
  case "$status" in
    Succeeded)
      break
      ;;
    Failed | Stopped | Degraded)
      echo "$migrate_job execution $execution ended as $status; the web was not deployed." >&2
      echo "Its logs are in the sepiabox-logs workspace (ContainerAppConsoleLogs_CL)." >&2
      exit 1
      ;;
  esac
  if ((SECONDS >= deadline)); then
    az containerapp job stop --name "$migrate_job" --resource-group "$resource_group" \
      --job-execution-name "$execution" >/dev/null 2>&1 || true
    echo "$migrate_job execution $execution did not finish in ${migrate_timeout_seconds}s (last status: $status) and was stopped; the web was not deployed." >&2
    echo "The migration may be partly applied: check its logs, then run this script again." >&2
    exit 1
  fi
  sleep "$poll_seconds"
done
echo "Migrations applied."

azd deploy web --no-prompt
