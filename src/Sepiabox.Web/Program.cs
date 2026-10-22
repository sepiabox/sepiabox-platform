using Azure.Storage.Blobs;
using Npgsql;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;
using Sepiabox.Storage;
using Sepiabox.Storage.Azure;
using Sepiabox.Web;
using Sepiabox.Web.Auth;
using Sepiabox.Web.Components;
using Sepiabox.Web.Localization;
using Sepiabox.Web.Services;
using Sepiabox.Web.Sources;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Before anything reads configuration, so the sign-in secrets and the access list come from Key Vault in Azure.
builder.AddSepiaboxSecrets();

// Both integrations also register health checks, so /health covers the database and blob storage.
// EF uses this same data source, so the app and its health check share one pool.
builder.AddNpgsqlDataSource("catalog", configureDataSourceBuilder: dataSource => CatalogDbContext.ConfigureDataSource(dataSource));
builder.AddAzureBlobServiceClient("blobs");
// The Azure implementation of IBlobStore (D-051), on the client Aspire registers.
builder.Services.AddSingleton<IBlobStore>(services =>
    new AzureBlobStore(services.GetRequiredService<BlobServiceClient>(), services.GetRequiredService<TimeProvider>()));

// Pages render on the server per request, so each query gets its own short-lived context.
builder.Services.AddDbContextFactory<CatalogDbContext>((services, options) =>
    CatalogDbContext.Configure(options, services.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISasUrlFactory, SasUrlFactory>();
builder.Services.AddSingleton(SearchSetup.Options(builder.Configuration));
builder.Services.AddSingleton<FilterOptionsCache>();
builder.Services.AddSingleton<IQueryEmbedder>(services => new ClipQueryEmbedder(
    SearchSetup.ModelFolder(services.GetRequiredService<IConfiguration>()),
    services.GetRequiredService<ILogger<ClipQueryEmbedder>>(),
    failuresAreErrors: builder.Environment.IsProduction()));
// S-056: Degraded in /health while the model is unusable; loaded before requests where the image ships it.
builder.Services.AddHealthChecks().AddCheck<TextModelHealthCheck>(TextModelHealthCheck.Name);
if (SearchSetup.WarmUp(builder.Configuration, builder.Environment))
{
    builder.Services.AddHostedService<TextModelWarmUp>();
}
// S-041: the region of a photo is cut from its preview and embedded here; the model is loaded on first use (D-099).
builder.Services.AddHttpClient(SignedLinkPreviewSource.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
    // The link is signed for one blob: a redirect would send the request somewhere it was not meant for.
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IPreviewSource, SignedLinkPreviewSource>();
builder.Services.AddSingleton<IRegionEmbedder>(services => new ClipRegionEmbedder(
    SearchSetup.ModelFolder(services.GetRequiredService<IConfiguration>()),
    services.GetRequiredService<IPreviewSource>(),
    services.GetRequiredService<ILogger<ClipRegionEmbedder>>()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ILibraryQueries, LibraryQueries>();
builder.Services.AddScoped<IPeopleEditor, PeopleEditor>();
builder.Services.AddScoped<IFaceSettings, FaceSettings>();
builder.Services.AddScoped<IExportRequests, ExportRequests>();
builder.Services.AddScoped<ISavedPlaces, SavedPlaces>();
builder.Services.AddScoped<IOrganization, Organization>();
// Connected cloud accounts (S-037, D-097): Google Drive with the sign-in's OAuth client; tokens protected by Data Protection.
builder.Services.AddSingleton(services => new GoogleDrive(services.GetRequiredService<SignInProviders>().Google));
builder.Services.AddSingleton<IDriveClient>(services => services.GetRequiredService<GoogleDrive>());
builder.Services.AddSingleton<IDriveAuthorization>(services => services.GetRequiredService<GoogleDrive>());
builder.Services.AddSingleton<DriveTokenProtector>();
builder.Services.AddScoped<IConnectedSources, ConnectedSources>();
// Questions (S-025): Claude through the project's own interface (D-051), with a daily limit per person.
builder.Services.AddSingleton<IQuestionTranslator, ConfiguredQuestionTranslator>();
builder.Services.AddSingleton<IQuestionQuota, CatalogQuestionQuota>();
builder.Services.AddSingleton(QuestionLimits.From(builder.Configuration));
builder.Services.AddSingleton(services => new QuestionWarmUp(
    services.GetRequiredService<IQuestionTranslator>(), services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILogger<QuestionWarmUp>>())
{
    Enabled = builder.Configuration.GetValue("Questions:WarmUp", true),
});
builder.Services.AddScoped<QuestionService>();

builder.AddSepiaboxAuth();
builder.AddSepiaboxKeyRing();

// Sent outside Development by UseHsts below, and never for localhost (D-065): a year, for this host only, not
// preloaded, so a browser that has seen the app never tries plain http for it.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});
builder.Services.AddSepiaboxLocalization();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
app.WarnAboutEphemeralKeys();

app.UseSepiaboxForwardedHeaders();
// Before the error and status code pages, so they too are in the visitor's language (D-050).
app.UseRequestLocalization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Styles, fonts and scripts hold nothing private, and the sign-in page needs them before sign-in.
app.MapStaticAssets().AllowAnonymous();
app.MapSepiaboxAuth();
app.MapLanguageSwitch();
app.MapMediaEndpoints();
app.MapExportEndpoints();
app.MapSearchEndpoints();
app.MapSourceEndpoints();
app.MapQuestionEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

/// <summary>Visible to the integration tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
