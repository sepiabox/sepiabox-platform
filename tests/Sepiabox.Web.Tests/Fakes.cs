using Azure;
using Microsoft.Extensions.Logging;
using Sepiabox.Web.Services;
using Sepiabox.Web.Sources;

namespace Sepiabox.Web.Tests;

/// <summary>A clock that stands still, so SAS expiry times can be asserted exactly.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A clock a test moves forward by hand.</summary>
internal sealed class MovableTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// An in-memory library. Items are split into pages of <see cref="PageSize"/>; the last requested page
/// is recorded so tests can see how a page number from the URL was clamped.
/// </summary>
internal sealed class FakeLibrary(params TimelineItem[] items) : ILibraryQueries
{
    public int PageSize { get; init; } = LibraryQueries.PageSize;

    public Dictionary<string, AssetDetail> Assets { get; } = [];

    public int? RequestedPage { get; private set; }

    /// <summary>Thrown by the timeline query, to reach the error page.</summary>
    public Exception? TimelineFailure { get; init; }

    public Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default)
    {
        if (TimelineFailure is not null)
        {
            return Task.FromException<TimelinePage>(TimelineFailure);
        }

        RequestedPage = page;
        var slice = items.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        return Task.FromResult(new TimelinePage(page, slice, items.Length > page * PageSize));
    }

    /// <summary>The occasions, newest first as the real query returns them (S-023); split into pages of <see cref="OccasionPageSize"/>.</summary>
    public List<OccasionSummary> Occasions { get; } = [];

    public int OccasionPageSize { get; init; } = LibraryQueries.OccasionsPageSize;

    public int? RequestedOccasionsPage { get; private set; }

    public string? RequestedOccasionType { get; private set; }

    public Task<OccasionsPage> GetOccasionsPageAsync(int page, string? type = null, CancellationToken cancellationToken = default)
    {
        (RequestedOccasionsPage, RequestedOccasionType) = (page, type);
        var matching = Occasions.Where(o => type is null || o.Type == type).ToList();
        var slice = matching.Skip((page - 1) * OccasionPageSize).Take(OccasionPageSize).ToList();
        return Task.FromResult(new OccasionsPage(page, slice, matching.Count > page * OccasionPageSize));
    }

    /// <summary>The occasion of each asset, for the results grouped by occasion (S-041).</summary>
    public Dictionary<string, OccasionSummary> OccasionOf { get; } = [];

    public Task<IReadOnlyDictionary<string, OccasionSummary>> GetOccasionsOfAsync(
        IReadOnlyCollection<string> assetIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, OccasionSummary>>(
            assetIds.Where(OccasionOf.ContainsKey).ToDictionary(id => id, id => OccasionOf[id]));

    public Task<OccasionSummary?> GetOccasionAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Occasions.FirstOrDefault(o => o.Id == id));

    public Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.GetValueOrDefault(id));

    public Task<string?> GetDerivativeBlobPathAsync(string id, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.ContainsKey(id) ? $"{id}/{name}{(DerivativeNames.IsVideo(name) ? ".mp4" : ".webp")}" : null);

    public Task<OriginalFile?> GetOriginalAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.TryGetValue(id, out var asset) ? new OriginalFile("IMG_0001.jpg", "image/jpeg", asset.OriginalStored) : null);

    /// <summary>What the filter bar offers.</summary>
    public FilterOptions Options { get; init; } = new([2021, 2019], ["Lisbon", "Porto"], ["HD da Ana"]);

    /// <summary>The searches asked for, in order; each returns <see cref="SearchResults"/>.</summary>
    public List<Sepiabox.Catalog.Search.SearchQuery> Searches { get; } = [];

    public SearchPage SearchResults { get; init; } = new([], false);

    /// <summary>Thrown by every search, such as the text model being unavailable.</summary>
    public Exception? SearchFailure { get; init; }

    /// <summary>What every count returns; null, as for a count with a description.</summary>
    public Sepiabox.Catalog.Search.CountResult? CountResult { get; init; }

    /// <summary>The counts asked for, in order.</summary>
    public List<Sepiabox.Catalog.Search.SearchQuery> Counts { get; } = [];

    public Task<Sepiabox.Catalog.Search.CountResult?> CountAsync(Sepiabox.Catalog.Search.SearchQuery query, CancellationToken cancellationToken = default)
    {
        Counts.Add(query);
        return Task.FromResult(CountResult);
    }

    public Task<SearchPage> SearchAsync(Sepiabox.Catalog.Search.SearchQuery query, CancellationToken cancellationToken = default)
    {
        Searches.Add(query);
        return SearchFailure is null ? Task.FromResult(SearchResults) : Task.FromException<SearchPage>(SearchFailure);
    }

    public Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Options);

    /// <summary>What the storage page shows (S-028).</summary>
    public StorageReport Storage { get; set; } = new([], 0, 0, 0, 0, [], []);

    public Task<StorageReport> GetStorageAsync(CancellationToken cancellationToken = default) => Task.FromResult(Storage);

    /// <summary>The people page's people, as the query returns them.</summary>
    public List<PersonSummary> People { get; init; } = [];

    public Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersonSummary>>(People);

    /// <summary>The person pages, by id.</summary>
    public Dictionary<long, PersonDetail> Persons { get; } = [];

    public Task<PersonDetail?> GetPersonAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Persons.GetValueOrDefault(id));
}

/// <summary>Records the corrections asked for and answers each with <see cref="Outcome"/>.</summary>
internal sealed class FakePeopleEditor : IPeopleEditor
{
    public Sepiabox.Catalog.People.CurationOutcome Outcome { get; set; } = Sepiabox.Catalog.People.CurationOutcome.Done;

    public List<string> Calls { get; } = [];

    public Task<Sepiabox.Catalog.People.CurationOutcome> RenameAsync(long personId, string? name, CancellationToken cancellationToken = default) =>
        Record($"rename {personId} {name}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> LinkUserAsync(long personId, string email, CancellationToken cancellationToken = default) =>
        Record($"link {personId} {email}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> UnlinkUserAsync(long personId, string email, CancellationToken cancellationToken = default) =>
        Record($"unlink {personId} {email}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> MergeAsync(long personId, long otherId, CancellationToken cancellationToken = default) =>
        Record($"merge {personId} {otherId}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> NotThisPersonAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        Record($"not-this-person {personId} {faceId}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> NotAFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        Record($"not-a-face {personId} {faceId}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> ReturnFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        Record($"return {personId} {faceId}");

    public Task<Sepiabox.Catalog.People.CurationOutcome> MoveFaceAsync(long personId, long faceId, long? targetId, CancellationToken cancellationToken = default) =>
        Record($"move {personId} {faceId} {targetId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "new"}");

    private Task<Sepiabox.Catalog.People.CurationOutcome> Record(string call)
    {
        Calls.Add(call);
        return Task.FromResult(Outcome);
    }
}

/// <summary>
/// Signing that fails the ways Azure can: storage refuses the delegation key, or the managed identity
/// cannot get a token at all.
/// </summary>
internal sealed class FailingLinks(Exception? failure = null) : ISasUrlFactory
{
    private readonly Exception _failure = failure ?? new RequestFailedException(403, "AuthorizationPermissionMismatch");

    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromException<Uri>(_failure);

    public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromException<Uri>(_failure);

    public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) =>
        Task.FromException<Uri>(_failure);
}

internal static class Ids
{
    /// <summary>A 64-character lowercase hex id, like a SHA-256, made unique by <paramref name="n"/>.</summary>
    public static string Of(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class Details
{
    public static AssetDetail Photo(string id, DateTime? capturedAt = null, string? previewName = DerivativeNames.Preview) =>
        new(id, AssetKinds.Photo, capturedAt, "Apple", "iPhone 15", 4000, 3000, previewName,
            [new AssetSourceDetail("local", "HD Ana", "2019/praia.jpg")], DurationSeconds: null, HasProxy: false);

    /// <summary>A 3-second video; by default it has its poster and its 720p version.</summary>
    public static AssetDetail Video(string id, bool hasProxy = true, string? posterName = DerivativeNames.Poster) =>
        new(id, AssetKinds.Video, new DateTime(2020, 8, 9, 18, 5, 0), "JVC", "GZ-MG330", 1024, 576, posterName,
            [new AssetSourceDetail("local", "HD Ana", "2020/MOV001.MOD")], DurationSeconds: 3, HasProxy: hasProxy);
}

/// <summary>Keeps what was logged, so a test can check a warning.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>
/// A stand-in for the CLIP text encoder (S-016): unit tests never need the real model. Without a function it
/// returns a fixed unit vector; unavailable, it behaves like a web without the text model.
/// </summary>
internal sealed class FakeQueryEmbedder(bool available = true, Func<string, float[]>? embed = null) : Sepiabox.Catalog.Search.IQueryEmbedder
{
    public List<string> Texts { get; } = [];

    public bool IsAvailable => available;

    public float[] Embed(string text)
    {
        if (!available)
        {
            throw new Sepiabox.Catalog.Search.VisualSearchUnavailableException("No text model in this test.");
        }

        Texts.Add(text);
        return embed?.Invoke(text) ?? Axis(0);
    }

    /// <summary>A 512-value unit vector along <paramref name="axis"/>.</summary>
    public static float[] Axis(int axis, float weight = 1, int other = -1, float otherWeight = 0)
    {
        var vector = new float[512];
        vector[axis] = weight;
        if (other >= 0)
        {
            vector[other] = otherWeight;
        }

        var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        return [.. vector.Select(v => v / norm)];
    }
}

/// <summary>A translator that answers every question with <see cref="Json"/>, or with <see cref="Outcome"/>.</summary>
internal sealed class FakeTranslator : Sepiabox.Search.Questions.IQuestionTranslator
{
    public bool IsConfigured { get; init; } = true;

    public string Json { get; init; } = """{"filters": {}}""";

    public Sepiabox.Search.Questions.QuestionOutcome Outcome { get; init; } = Sepiabox.Search.Questions.QuestionOutcome.Translated;

    public Sepiabox.Search.Questions.TokenUsage Usage { get; init; } = new(100, 20, 80, 0);

    /// <summary>The questions and contexts it was given, in order.</summary>
    public List<(string Question, Sepiabox.Search.Questions.QuestionContext Context)> Calls { get; } = [];

    public Task<Sepiabox.Search.Questions.QuestionTranslation> TranslateAsync(
        string question, Sepiabox.Search.Questions.QuestionContext context, CancellationToken cancellationToken = default)
    {
        lock (Calls)
        {
            Calls.Add((question, context));
        }

        return Task.FromResult(new Sepiabox.Search.Questions.QuestionTranslation(
            Outcome, Outcome == Sepiabox.Search.Questions.QuestionOutcome.Translated ? Json : null, Usage, 1));
    }
}

/// <summary>The daily question counter in memory.</summary>
internal sealed class FakeQuestionQuota : IQuestionQuota
{
    public Dictionary<(string UserKey, DateOnly Day), (int Questions, Sepiabox.Search.Questions.TokenUsage Usage)> Days { get; } = [];

    public Task<bool> TryTakeAsync(string userKey, DateOnly day, int limit, CancellationToken cancellationToken = default)
    {
        lock (Days)
        {
            var (questions, usage) = Days.GetValueOrDefault((userKey, day), (0, Sepiabox.Search.Questions.TokenUsage.None));
            if (questions >= limit)
            {
                return Task.FromResult(false);
            }

            Days[(userKey, day)] = (questions + 1, usage);
            return Task.FromResult(true);
        }
    }

    public Task GiveBackAsync(string userKey, DateOnly day, CancellationToken cancellationToken = default)
    {
        lock (Days)
        {
            var (questions, usage) = Days[(userKey, day)];
            Days[(userKey, day)] = (questions - 1, usage);
            return Task.CompletedTask;
        }
    }

    public Task RecordAsync(string userKey, DateOnly day, Sepiabox.Search.Questions.TokenUsage usage, CancellationToken cancellationToken = default)
    {
        lock (Days)
        {
            var (questions, total) = Days[(userKey, day)];
            Days[(userKey, day)] = (questions, total.Add(usage));
            return Task.CompletedTask;
        }
    }
}

internal sealed class FakeFaceSettings : IFaceSettings
{
    public Sepiabox.Catalog.People.FaceRecognitionState State { get; set; } = Sepiabox.Catalog.People.FaceRecognitionState.Default;

    public List<Sepiabox.Catalog.FaceDataDeletion> History { get; } = [];

    public bool PersonFound { get; set; } = true;

    /// <summary>Refuses to turn on whatever the state says, as the catalog does when an erase began meanwhile.</summary>
    public bool RefuseOn { get; set; }

    public List<string> Calls { get; } = [];

    public Task<FaceSettingsView> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new FaceSettingsView(State, History));

    public Task TurnOffAsync(string email, CancellationToken cancellationToken = default)
    {
        Calls.Add($"off {email}");
        State = new(false, DateTimeOffset.UnixEpoch, null);
        return Task.CompletedTask;
    }

    public Task<bool> TurnOnAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("on");
        if (RefuseOn || State.ManifestsPending)
        {
            return Task.FromResult(false);
        }

        State = State with { On = true };
        return Task.FromResult(true);
    }

    public Task<bool> DeletePersonAsync(long personId, string email, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete {personId} {email}");
        return Task.FromResult(PersonFound);
    }
}

internal sealed class FakeSavedPlaces : ISavedPlaces
{
    public List<SavedPlaceSummary> Places { get; } = [];

    public Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome Outcome { get; set; } = Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome.Done;

    public List<string> Calls { get; } = [];

    public Task<IReadOnlyList<SavedPlaceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SavedPlaceSummary>>([.. Places]);

    public Task<Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome> CreateAsync(
        string? name, double latitude, double longitude, int radiusM, bool isHome, CancellationToken cancellationToken = default) =>
        Record(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"create {name} {latitude} {longitude} {radiusM} {isHome}"));

    public Task<Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome> UpdateAsync(
        long id, string? name, int? radiusM, bool isHome, CancellationToken cancellationToken = default) =>
        Record(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"update {id} {name} {radiusM?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "keep"} {isHome}"));

    public Task<Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        Record(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"delete {id}"));

    private Task<Sepiabox.Catalog.SavedPlaces.SavedPlaceOutcome> Record(string call)
    {
        Calls.Add(call);
        return Task.FromResult(Outcome);
    }
}

/// <summary>An in-memory list of connected accounts (S-037), recording what the page and the endpoints ask of it.</summary>
internal sealed class FakeConnectedSources : IConnectedSources
{
    public bool CanConnectGoogleDrive { get; init; } = true;

    public List<ConnectedSourceSummary> Sources { get; } = [];

    public SourceOutcome Completes { get; init; } = SourceOutcome.Done;

    public SourceOutcome Counts { get; init; } = SourceOutcome.Done;

    /// <summary>The codes and redirect addresses connections were completed with.</summary>
    public List<(string Code, string RedirectUri)> Completed { get; } = [];

    public List<long> Counted { get; } = [];

    public List<long> Disconnected { get; } = [];

    public Task<IReadOnlyList<ConnectedSourceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConnectedSourceSummary>>([.. Sources]);

    public Uri GoogleDriveAuthorizationUri(string redirectUri, string state) =>
        new($"https://accounts.example.com/authorize?redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}");

    public Task<SourceOutcome> CompleteGoogleDriveConnectionAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        Completed.Add((code, redirectUri));
        return Task.FromResult(Completes);
    }

    public Task<SourceOutcome> CountAsync(long id, CancellationToken cancellationToken = default)
    {
        Counted.Add(id);
        return Task.FromResult(Counts);
    }

    public Task<SourceOutcome> DisconnectAsync(long id, CancellationToken cancellationToken = default)
    {
        Disconnected.Add(id);
        return Task.FromResult(SourceOutcome.Done);
    }
}

/// <summary>A Drive that never reaches Google (S-037): a fixed account and count, or the failure a test sets.</summary>
internal sealed class FakeDrive : IDriveClient, IDriveAuthorization
{
    public bool IsConfigured { get; init; } = true;

    public string Email { get; set; } = "Ana@example.com";

    public DriveMediaCount Media { get; set; } = new(12, 3);

    /// <summary>When set, every call with a token throws what it returns (an invalid grant, or Google being down).</summary>
    public Func<Exception?>? Failure { get; set; }

    /// <summary>The refresh token a code is exchanged for; null makes the exchange fail like a refused code.</summary>
    public string? RefreshToken { get; set; } = "1//refresh-token-in-clear-text";

    public List<string> TokensUsed { get; } = [];

    public List<string> Revoked { get; } = [];

    public Exception? RevokeFailure { get; set; }

    public Uri AuthorizationUri(string redirectUri, string state) => new($"https://accounts.example.com/authorize?state={state}");

    public Task<string> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) =>
        RefreshToken is { } token ? Task.FromResult(token) : Task.FromException<string>(new DriveAuthorizationException("refused"));

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        Revoked.Add(refreshToken);
        return RevokeFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    public Task<string> GetAccountEmailAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        TokensUsed.Add(refreshToken);
        return Failure?.Invoke() is { } failure ? Task.FromException<string>(failure) : Task.FromResult(Email);
    }

    public Task<DriveMediaCount> CountMediaAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        TokensUsed.Add(refreshToken);
        return Failure?.Invoke() is { } failure ? Task.FromException<DriveMediaCount>(failure) : Task.FromResult(Media);
    }

    /// <summary>The content of each file by id, for "Open original" (S-038); a file not here is gone from the Drive.</summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task<DriveFileDownload?> OpenFileAsync(string refreshToken, string fileId, CancellationToken cancellationToken = default)
    {
        TokensUsed.Add(refreshToken);
        if (Failure?.Invoke() is { } failure)
        {
            return Task.FromException<DriveFileDownload?>(failure);
        }

        return Task.FromResult<DriveFileDownload?>(Files.TryGetValue(fileId, out var bytes)
            ? new DriveFileDownload(new MemoryStream(bytes), "Praia 2019.jpg", "image/jpeg", bytes.Length)
            : null);
    }
}
