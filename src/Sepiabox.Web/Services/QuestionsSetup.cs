using Sepiabox.Search.Questions;

namespace Sepiabox.Web.Services;

public static class QuestionsSetup
{
    /// <summary>
    /// <c>Anthropic:ApiKey</c> (user secrets locally, Key Vault's <c>Anthropic--ApiKey</c> in Azure, D-052), and
    /// optionally <c>Anthropic:Model</c> and <c>Anthropic:BaseUrl</c>.
    /// </summary>
    public static AnthropicQuestionOptions Options(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(AnthropicQuestionOptions.Section);
        return new AnthropicQuestionOptions
        {
            ApiKey = section["ApiKey"],
            Model = string.IsNullOrWhiteSpace(section["Model"]) ? AnthropicQuestionOptions.DefaultModel : section["Model"]!.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(section["BaseUrl"]) ? AnthropicQuestionOptions.DefaultBaseUrl : section["BaseUrl"]!.Trim(),
        };
    }
}

/// <summary>
/// The Anthropic translator for the configuration as it is now: Key Vault is read again every few minutes (D-051), so
/// a key added, changed or removed there takes effect without a restart. A new client is made only when the
/// settings change.
/// </summary>
public sealed class ConfiguredQuestionTranslator(IConfiguration configuration, ILogger<AnthropicQuestionTranslator> log)
    : IQuestionTranslator, IDisposable
{
    private readonly Lock _lock = new();
    private (AnthropicQuestionOptions Options, AnthropicQuestionTranslator Translator)? _current;

    public bool IsConfigured => Current().IsConfigured;

    public Task<QuestionTranslation> TranslateAsync(string question, QuestionContext context, CancellationToken cancellationToken = default) =>
        Current().TranslateAsync(question, context, cancellationToken);

    public void Dispose()
    {
        lock (_lock)
        {
            _current?.Translator.Dispose();
            _current = null;
        }
    }

    private AnthropicQuestionTranslator Current()
    {
        var options = QuestionsSetup.Options(configuration);
        lock (_lock)
        {
            if (_current is { } current && current.Options == options)
            {
                return current.Translator;
            }

            // The previous client may still be answering a question; it is left to the garbage collector.
            var translator = new AnthropicQuestionTranslator(options, QuestionService.Validate, log);
            _current = (options, translator);
            return translator;
        }
    }
}
