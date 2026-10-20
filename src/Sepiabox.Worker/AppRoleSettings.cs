using Microsoft.Extensions.Configuration;
using Sepiabox.Catalog;

namespace Sepiabox.Worker;

/// <summary>
/// The application role <c>migrate</c> creates for the web (S-058, D-062): <c>Catalog:AppRole:Name</c> and
/// <c>Catalog:AppRole:Password</c>, set only in Azure. Locally neither is set and the web uses the
/// container's administrator.
/// </summary>
public sealed record AppRoleSettings(string Name, string Password)
{
    public const string NameKey = "Catalog:AppRole:Name";
    public const string PasswordKey = "Catalog:AppRole:Password";

    /// <summary>The settings, or null when neither is set; one without the other is a mistake.</summary>
    public static AppRoleSettings? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var name = configuration[NameKey];
        var password = configuration[PasswordKey];
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(password))
        {
            return null;
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException($"Set both {NameKey} and {PasswordKey}, or neither.");
        }

        CatalogAppRole.Validate(name, password);
        return new AppRoleSettings(name, password);
    }
}
