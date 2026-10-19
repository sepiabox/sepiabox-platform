namespace Sepiabox.Pipeline.Tests;

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sepiabox-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, byte[] content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
