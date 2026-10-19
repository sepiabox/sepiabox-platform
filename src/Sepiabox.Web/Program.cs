using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sepiabox.Catalog;
using Sepiabox.Web.Components;
using Sepiabox.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Both integrations also register health checks, so /health covers the database and blob storage.
// EF uses this same data source, so the app and its health check share one pool.
builder.AddNpgsqlDataSource("catalog", configureDataSourceBuilder: dataSource => dataSource.UseNetTopologySuite());
builder.AddAzureBlobServiceClient("blobs");

// Pages render on the server per request, so each query gets its own short-lived context.
builder.Services.AddDbContextFactory<CatalogDbContext>((services, options) =>
    CatalogDbContext.Configure(options, services.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISasUrlFactory, SasUrlFactory>();
builder.Services.AddScoped<ILibraryQueries, LibraryQueries>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapMediaEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

/// <summary>Visible to the integration tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
