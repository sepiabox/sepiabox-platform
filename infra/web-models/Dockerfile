# The web's base image (S-056, D-071): ASP.NET Core plus the CLIP text and image models (S-041, D-099), the tokenizer and models.json in one
# layer of their own. scripts/web-models.sh fills the build context with the files `models download` verified
# against their pinned SHA-256 and tags the image by models.json and the base image, so the web's releases reuse
# this layer and the registry stores the model once. The web verifies the files again when it starts.
ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
FROM ${BASE_IMAGE}
# Readable by the app's non-root user whatever the files' permissions were where they were downloaded.
# Octal, not a+rX: older BuildKit versions ignore or refuse a symbolic mode.
COPY --chmod=755 models/ /models/
