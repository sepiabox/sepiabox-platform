using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Web.Components;
using Sepiabox.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Both integrations also register health checks, so /health covers the database and blob storage.
builder.AddNpgsqlDataSource("catalog");
builder.AddAzureBlobServiceClient("blobs");

// Pages render on the server per request, so each query gets its own short-lived context.
builder.Services.AddDbContextFactory<CatalogDbContext>((services, options) => CatalogDbContext.Configure(
    options,
    services.GetRequiredService<IConfiguration>().GetConnectionString("catalog")
        ?? throw new InvalidOperationException("The catalog connection string is missing.")));
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

/// <summary>Visible to the integration tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
