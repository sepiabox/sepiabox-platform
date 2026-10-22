# The worker's image for `process --from-queue` (S-036, D-107): the .NET runtime, FFmpeg, and the models the pipeline
# reads (the CLIP image model, YuNet and SFace), downloaded with `sepiabox-ingest models download`, which refuses any
# file whose SHA-256 is not the one pinned in models/models.json (D-044). Built from the repository root:
#   docker build --file infra/worker/Dockerfile --tag sepiabox-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Sepiabox.Worker/Sepiabox.Worker.csproj --configuration Release --output /app --no-self-contained
# The models are verified here, at build time, so a wrong or tampered file fails the build, not a job at run time.
RUN dotnet run --project src/Sepiabox.Ingest --configuration Release -- models download --models /models \
    --only clip-vit-b32-vision,yunet-2023mar,sface-2021dec

# ASP.NET Core, not the plain runtime: ServiceDefaults (health checks, OpenTelemetry) references Microsoft.AspNetCore.App.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ffprobe comes with the ffmpeg package.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
# Octal, not a+rX: older BuildKit versions refuse a symbolic mode. Readable by the app's non-root user.
COPY --from=build --chmod=755 /models /models
COPY --from=build /app /app
ENV Search__ModelFolder=/models
USER app
ENTRYPOINT ["dotnet", "/app/sepiabox-worker.dll"]
