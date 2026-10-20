#!/usr/bin/env bash
# Builds and pushes the web's base image sepiabox-web-models (S-056, D-071) when the registry does not have it
# yet, and prints its reference on stdout for the web's publish (SEPIABOX_WEB_BASE_IMAGE). The tag is a hash of
# models.json and of the ASP.NET Core base image's digest: an app release with the same models reuses the image,
# and so its model layer; a new models.json or a patched base image builds a new one.
#
# Needs azd (the selected environment's registry), the Azure CLI, Docker and the .NET SDK. Progress goes to stderr.
set -euo pipefail

readonly repository="sepiabox-web-models"
readonly base_image="mcr.microsoft.com/dotnet/aspnet:10.0"
# The files the web reads (D-070): their names are those in models/models.json.
readonly model_files=("clip-vit-b32-text.onnx" "clip-bpe-simple-vocab-16e6.txt.gz")
readonly model_names="clip-vit-b32-text,clip-bpe-vocab"

cd "$(dirname "$0")/.."

registry_endpoint=$(azd env get-value AZURE_CONTAINER_REGISTRY_ENDPOINT)
if [[ -z "$registry_endpoint" ]]; then
  echo "The azd environment has no AZURE_CONTAINER_REGISTRY_ENDPOINT; provision first." >&2
  exit 1
fi
registry_name="${registry_endpoint%%.*}"
# Also for the web's publish, which pulls this base image from the registry.
az acr login --name "$registry_name" >&2

# The base image's digest, so a patched ASP.NET Core image (monthly) gives a new tag rather than a stale base.
docker pull --quiet "$base_image" >&2
base_digest=$(docker image inspect --format '{{index .RepoDigests 0}}' "$base_image")
tag=$( (cat models/models.json; printf '%s' "$base_digest") | sha256sum | cut -c1-16)
image="$registry_endpoint/$repository:$tag"

if az acr repository show --name "$registry_name" --image "$repository:$tag" --output none >/dev/null 2>&1; then
  echo "$image is already in the registry." >&2
  echo "$image"
  exit 0
fi

models_folder="${SEPIABOX_MODELS:-$HOME/.sepiabox/models}"
# Downloads only what is missing or differs, and fails on any SHA-256 that is not the pinned one (D-044).
dotnet run --project src/Sepiabox.Ingest --configuration Release -- models download --models "$models_folder" --only "$model_names" >&2

context=$(mktemp -d)
trap 'rm -rf "$context"' EXIT
mkdir "$context/models"
for file in "${model_files[@]}"; do
  cp "$models_folder/$file" "$context/models/$file"
done
cp models/models.json "$context/models/models.json"

docker build --platform linux/amd64 --build-arg "BASE_IMAGE=$base_digest" --file infra/web-models/Dockerfile --tag "$image" "$context" >&2
docker push --quiet "$image" >&2
echo "Pushed $image." >&2
echo "$image"
