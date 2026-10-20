using System.Xml.Linq;

namespace Sepiabox.AppHost.Tests;

/// <summary>The images azd builds with the SDK's container support for the published projects.</summary>
public sealed class ContainerImageTests
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

    private static Dictionary<string, string> Properties(string project) =>
        XDocument.Load(Path.Combine(Repository.Root, project))
            .Descendants("PropertyGroup")
            .Elements()
            // As MSBuild reads them, the last definition of a property wins.
            .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
}
