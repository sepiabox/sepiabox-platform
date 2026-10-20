using System.Collections.Frozen;
using Microsoft.Extensions.Primitives;

namespace Sepiabox.Web.Auth;

/// <summary>
/// The e-mails allowed into Sepiabox (D-006). Sign-in only proves who someone is; this list decides
/// whether they get in. An empty list lets no one in (D-048).
/// </summary>
public sealed partial class AccessList
{
    public const string ConfigurationKey = "Access:Emails";

    // Key Vault holds the list as one secret, so a single value may carry several e-mails.
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    // Replaced whole on a reload, never changed in place, so readers always see one complete list.
    private volatile FrozenSet<string> _emails;

    public AccessList(IEnumerable<string?> emails) => _emails = Parse(emails);

    public int Count => _emails.Count;

    /// <summary>
    /// Reads <c>Access:Emails</c> either as an array (<c>Access:Emails:0</c>, ...) or as one value with the
    /// e-mails separated by commas, semicolons or white space, and reads it again whenever the configuration
    /// reloads. Environment variables and Container Apps secrets never reload: a change there takes effect
    /// with the next restart or revision (docs/operations.md).
    /// </summary>
    public static AccessList From(IConfiguration configuration, ILogger<AccessList> logger)
    {
        var access = new AccessList(Read(configuration));
        ChangeToken.OnChange(configuration.GetReloadToken, () =>
        {
            var emails = Parse(Read(configuration));
            if (emails.Count == 0 && access.Count > 0)
            {
                LogEmptied(logger, ConfigurationKey);
            }

            access._emails = emails;
        });
        return access;
    }

    public bool Allows(string? email) =>
        !string.IsNullOrWhiteSpace(email) && _emails.Contains(Normalize(email));

    private static IEnumerable<string?> Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationKey);
        return [section.Value, .. section.GetChildren().Select(child => child.Value)];
    }

    private static FrozenSet<string> Parse(IEnumerable<string?> emails) =>
        emails
            .SelectMany(value => (value ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            .Select(Normalize)
            .ToFrozenSet(StringComparer.Ordinal);

    // Providers and people type addresses with any casing; the comparison ignores it and stray spaces.
    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Key} was reloaded empty: nobody can sign in any more.")]
    private static partial void LogEmptied(ILogger logger, string key);
}
