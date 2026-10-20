using System.Collections.Frozen;

namespace Sepiabox.Web.Auth;

/// <summary>
/// The e-mails allowed into Sepiabox (D-006). Sign-in only proves who someone is; this list decides
/// whether they get in. An empty list lets no one in (D-048).
/// </summary>
public sealed class AccessList
{
    public const string ConfigurationKey = "Access:Emails";

    // Key Vault holds the list as one secret, so a single value may carry several e-mails.
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private readonly FrozenSet<string> _emails;

    public AccessList(IEnumerable<string?> emails) =>
        _emails = emails
            .SelectMany(value => (value ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            .Select(Normalize)
            .ToFrozenSet(StringComparer.Ordinal);

    public int Count => _emails.Count;

    /// <summary>
    /// Reads <c>Access:Emails</c> either as an array (<c>Access:Emails:0</c>, ...) or as one value with the
    /// e-mails separated by commas, semicolons or white space.
    /// </summary>
    public static AccessList From(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationKey);
        return new AccessList([section.Value, .. section.GetChildren().Select(child => child.Value)]);
    }

    public bool Allows(string? email) =>
        !string.IsNullOrWhiteSpace(email) && _emails.Contains(Normalize(email));

    // Providers and people type addresses with any casing; the comparison ignores it and stray spaces.
    private static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
