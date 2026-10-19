# Sepiabox

**Ask your photos.** Sepiabox brings a family's photos and videos together from external drives, Google Photos, Google Drive, OneDrive and phones, understands each one once, and then finds any moment when you ask.

> "How many weddings did we go to this year?"
> "Old photos of me with Fabio"
> "The video where Lucas sings happy birthday"

## Status

Early development. Sepiabox is being built and tested by one family before anyone else uses it. There is no public instance, no sign-up and no stable release yet.

## How it works

1. **Gather.** Import an external drive or connect cloud accounts. The same photo stored in three places becomes one.
2. **Understand.** On arrival, each photo gets its date, place, people and occasion. You only name the people.
3. **Ask.** Questions become filters over that metadata (`person ∋ Ana · city = Lisbon · month = 12`), so answering never re-reads the photos.

Heavy processing of a large archive runs on your own computer with `sepiabox-ingest`; the cloud only stores files and imports the results.

## Try it: scan a drive

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Sepiabox.Ingest -- scan "E:\Photos" --report scan.json
```

`scan` reads a folder or drive without changing anything and reports:

- how many photos, videos and other files it holds, and how much space they take
- the year each photo and video was taken, from its own metadata
- exact duplicates, and how much space they waste
- files that could not be read, an early sign of a failing drive

`--report` also writes every duplicate and problem to a JSON file. `--no-duplicates` skips duplicate detection for a faster first look.

## Stack

- .NET: ASP.NET Core with Blazor for the web app, .NET workers for the pipeline
- ONNX Runtime for faces and CLIP embeddings, FFmpeg and Magick.NET for media
- PostgreSQL with pgvector and PostGIS
- Azure: Container Apps, Blob Storage, Service Bus, Event Grid, Key Vault
- Claude Haiku (via Microsoft Foundry) turns free-text questions into filters; it only receives the question text, never the photos
- .NET Aspire for local development

## Privacy principles

- Your photos are yours: full export of originals and metadata at any time.
- Face recognition is opt-in and never leaves your account.
- Originals are stored in Europe.

## License

Sepiabox is licensed under the [GNU Affero General Public License v3.0](LICENSE). If you run a modified version as a network service, you must make its source code available to its users.

Place names use data from [GeoNames](https://www.geonames.org/), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

## Contributing

Not open to outside contributions yet. Issues with ideas and bug reports are welcome.
