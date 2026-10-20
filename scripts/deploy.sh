#!/usr/bin/env bash
# Deploys Sepiabox to Azure (S-032) in the only safe order: the infrastructure, the migrate job's new
# image, one run of `sepiabox-worker migrate` against the cloud database, and only when it succeeded the
# import jobs (S-033), which expect the new schema and never migrate (D-063), the GeoNames job (S-059), which
# writes the new schema too, and the web's new revision.
# A failed or stuck migration stops here and the running jobs and web are left as they were.
#
# Needs azd, the Azure CLI with the containerapp extension, the .NET SDK and Docker, signed in to both
# CLIs, and the azd environment `sepiabox` in francecentral (docs/operations.md).
set -euo pipefail

readonly environment_name="sepiabox"
readonly location="francecentral"
readonly resource_group="rg-sepiabox"
readonly postgres_server="sepiabox-pg"
readonly migrate_job="sepiabox-migrate"
readonly service_bus="sepiabox-bus"
readonly queue="manifests"
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

# After the €45 shutdown (S-030) the server is stopped and provisioning would fail half-way. Before the
# first deploy neither the group nor the server exists; any other failure of these queries (no login, no
# network) stops the script rather than skipping the check.
# Assigned first: set -e does not apply to a command substitution inside an if condition.
group_exists=$(az group exists --name "$resource_group")
if [[ "$group_exists" != "true" && "$group_exists" != "false" ]]; then
  echo "Could not tell whether $resource_group exists (az group exists printed '$group_exists')." >&2
  exit 1
fi
if [[ "$group_exists" == "true" ]]; then
  postgres_state=$(az postgres flexible-server list --resource-group "$resource_group" \
    --query "[?name=='$postgres_server'].state | [0]" --output tsv)
  if [[ -n "$postgres_state" && "$postgres_state" != "Ready" ]]; then
    echo "$postgres_server is $postgres_state (the budget shutdown?). Resume it and wait until it is Ready: sepiabox-infra/scripts/resume.sh" >&2
    exit 1
  fi
fi

# Lets azure.yaml's predeploy hook through.
export SEPIABOX_DEPLOY_SCRIPT=1

azd provision --no-prompt

# From here until the import jobs run the new image, the queue keeps every upload's events but nothing
# receives them, so no import runs old code against a new schema or the reverse (D-063). Provisioning
# comes first because it creates the queue and would set it active again. On the way out the queue is
# made active again unless the migration succeeded and the import jobs were not updated: a failed
# migration leaves the old schema, which the old import image expects, but after a successful one the
# old image must not receive.
resume_command="az servicebus queue update --resource-group $resource_group --namespace-name $service_bus --name $queue --status Active"
queue_paused=false
migrated=false
imports_deployed=false
resume_queue() {
  if [[ "$queue_paused" != true ]]; then
    return 0
  fi
  if az servicebus queue update --resource-group "$resource_group" --namespace-name "$service_bus" \
    --name "$queue" --status Active --output none; then
    queue_paused=false
    echo "Queue $queue receives again."
    return 0
  fi
  echo "Could not make the queue receive again; run: $resume_command" >&2
  return 1
}
on_exit() {
  if [[ "$queue_paused" != true ]]; then
    return
  fi
  if [[ "$migrated" == true && "$imports_deployed" != true ]]; then
    echo "The import jobs were not updated, so the queue $queue is still paused. Run this script again, or resume it once the import jobs run the new image: $resume_command" >&2
    return
  fi
  resume_queue || true
}
trap on_exit EXIT
# Set first: making an active queue active again is harmless, a paused one left paused is not.
queue_paused=true
az servicebus queue update --resource-group "$resource_group" --namespace-name "$service_bus" \
  --name "$queue" --status ReceiveDisabled --output none

azd deploy migrate --no-prompt

execution=$(az containerapp job start --name "$migrate_job" --resource-group "$resource_group" --query name --output tsv)
if [[ -z "$execution" ]]; then
  echo "Starting $migrate_job returned no execution name; the web was not deployed." >&2
  exit 1
fi
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
    echo "$migrate_job execution $execution did not finish in ${migrate_timeout_seconds}s (last status: $status); the web was not deployed." >&2
    stop=(az containerapp job stop --name "$migrate_job" --resource-group "$resource_group" --job-execution-name "$execution")
    if "${stop[@]}" >/dev/null 2>&1; then
      echo "The execution was stopped." >&2
    else
      echo "Stopping the execution failed; stop it by hand: ${stop[*]}" >&2
    fi
    echo "The migration may be partly applied: check its logs, then run this script again." >&2
    exit 1
  fi
  sleep "$poll_seconds"
done
echo "Migrations applied."
migrated=true

azd deploy import --no-prompt
azd deploy import-all --no-prompt
imports_deployed=true
# Manual (S-059): started by hand only, but never with an image older than the schema. After the import
# jobs, so that if this deploy fails the queue still receives again with the new import image.
azd deploy geonames --no-prompt
# Never exit 0 with the queue paused.
resume_queue || exit 1

# The web's base image with the CLIP text model (S-056): built only when models.json or the ASP.NET Core image
# changed, then the web's publish builds on it (src/Sepiabox.Web/Sepiabox.Web.csproj, ContainerBaseImage).
SEPIABOX_WEB_BASE_IMAGE=$(scripts/web-models.sh)
export SEPIABOX_WEB_BASE_IMAGE
azd deploy web --no-prompt
