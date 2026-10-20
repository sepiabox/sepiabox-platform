namespace Sepiabox.Ingest;

/// <summary>
/// Reports on the calling thread. <see cref="Progress{T}"/> posts each report to the thread pool, so lines
/// could arrive out of order or after the summary that follows them.
/// </summary>
internal sealed class ConsoleProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
