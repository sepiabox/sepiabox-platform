namespace Sepiabox.AppHost.Tests;

internal static class Repository
{
    /// <summary>The checkout's root folder, the one with <c>Sepiabox.slnx</c>.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepiabox.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Sepiabox.slnx not found above " + AppContext.BaseDirectory);
    }
}
