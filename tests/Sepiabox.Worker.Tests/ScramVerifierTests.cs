using Sepiabox.Catalog;

namespace Sepiabox.Worker.Tests;

public sealed class ScramVerifierTests
{
    [Fact]
    public void Verifier_has_postgres_format_and_does_not_contain_the_password()
    {
        var verifier = ScramVerifier.Create("Abcdefghijklmnop0123456789");

        Assert.Matches(@"^SCRAM-SHA-256\$4096:[A-Za-z0-9+/]{22}==\$[A-Za-z0-9+/]{43}=:[A-Za-z0-9+/]{43}=\z", verifier);
        Assert.DoesNotContain("Abcdefghijklmnop0123456789", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void Verifier_uses_a_new_salt_each_time()
    {
        Assert.NotEqual(ScramVerifier.Create("Abcdefghijklmnop0123456789"), ScramVerifier.Create("Abcdefghijklmnop0123456789"));
    }

    [Fact]
    public void Verifier_matches_an_independent_computation_for_a_known_salt()
    {
        // Computed with Python's hashlib/hmac for this password and salt: the same RFC 5802 steps.
        var salt = Convert.FromBase64String("c2VwaWFib3gtc2FsdC0xNg==");

        var verifier = ScramVerifier.Create("pencil", salt);

        Assert.Equal(Expected, verifier);
    }

    private const string Expected = "SCRAM-SHA-256$4096:c2VwaWFib3gtc2FsdC0xNg==$q79h3p3kjbLRpRRIWy2Iyfk/bu45fRzIVE15sBTw62o=:plnmHJTT4BD/xUvNZVVj3Ao2M1ws3A1Yhl3AK3TuxpU=";
}
