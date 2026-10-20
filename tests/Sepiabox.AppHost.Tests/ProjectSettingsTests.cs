using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Sepiabox.AppHost.Tests;

/// <summary>Project settings the published apps depend on: their container images and logging.</summary>
public sealed class ProjectSettingsTests
{
    [Fact]
    public void Web_image_is_the_default_aspnet_image_with_icu()
    {
        // The web formats dates with ICU's culture data (D-058); chiseled images need -extra, Alpine its packages.
        var web = Properties("src/Sepiabox.Web/Sepiabox.Web.csproj");

        Assert.Equal("mcr.microsoft.com/dotnet/aspnet:10.0", web["ContainerBaseImage"]);
        Assert.Equal("false", web["InvariantGlobalization"]);
        Assert.False(web.ContainsKey("ContainerFamily"));
    }

    [Fact]
    public void Worker_can_be_built_into_the_migrate_job_image()
    {
        var worker = Properties("src/Sepiabox.Worker/Sepiabox.Worker.csproj");

        Assert.Equal("true", worker["EnableSdkContainerSupport"]);
    }

    [Fact]
    public void Web_does_not_log_every_sql_command()
    {
        // At Information, EF logs each query: in Azure that would fill the workspace's 0.15 GB daily cap (S-031).
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "src/Sepiabox.Web/appsettings.json")))!;

        Assert.Equal(
            "Warning", settings["Logging"]!["LogLevel"]!["Microsoft.EntityFrameworkCore.Database.Command"]!.GetValue<string>());
    }

    private static Dictionary<string, string> Properties(string project) =>
        XDocument.Load(Path.Combine(Repository.Root, project))
            .Descendants("PropertyGroup")
            .Elements()
            // Conditional definitions apply only in some builds (the web's models image in a deploy, S-056);
            // AzureWebModelTests evaluates those with MSBuild itself.
            .Where(e => e.Attribute("Condition") is null)
            // As MSBuild reads them, the last definition of a property wins.
            .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
}
