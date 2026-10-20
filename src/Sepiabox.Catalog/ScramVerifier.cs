using System.Security.Cryptography;
using System.Text;

namespace Sepiabox.Catalog;

/// <summary>
/// A PostgreSQL SCRAM-SHA-256 password verifier (RFC 5802, RFC 7677; the format PostgreSQL stores and
/// accepts in <c>PASSWORD '...'</c>): <c>SCRAM-SHA-256$&lt;iterations&gt;:&lt;salt&gt;$&lt;StoredKey&gt;:&lt;ServerKey&gt;</c>.
/// Sending the verifier instead of the password keeps the password out of the statement.
/// </summary>
public static class ScramVerifier
{
    /// <summary>PostgreSQL's own default (<c>scram_iterations</c>).</summary>
    public const int Iterations = 4096;

    private const int SaltLength = 16;

    public static string Create(string password) => Create(password, RandomNumberGenerator.GetBytes(SaltLength));

    /// <summary>
    /// The verifier for a given salt. The password is used as UTF-8 without SASLprep normalisation, which is
    /// the same for the letters and digits <see cref="CatalogAppRole"/> accepts.
    /// </summary>
    public static string Create(string password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, SHA256.HashSizeInBytes);
        var storedKey = SHA256.HashData(HMACSHA256.HashData(saltedPassword, "Client Key"u8));
        var serverKey = HMACSHA256.HashData(saltedPassword, "Server Key"u8);
        return $"SCRAM-SHA-256${Iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }
}
