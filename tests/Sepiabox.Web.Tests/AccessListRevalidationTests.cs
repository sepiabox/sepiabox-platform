using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Web.Auth;

namespace Sepiabox.Web.Tests;

public sealed class AccessListRevalidationTests
{
    [Fact]
    public void Ends_a_circuit_whose_user_is_no_longer_on_the_list()
    {
        var revalidation = new AccessListRevalidation(NullLoggerFactory.Instance, new AccessList([TestUsers.Other]));

        Assert.False(revalidation.IsStillAllowed(User(TestUsers.Allowed)));
        Assert.True(revalidation.IsStillAllowed(User("BRUNO@example.com")));
        Assert.False(revalidation.IsStillAllowed(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Equal(TimeSpan.FromMinutes(5), AccessListRevalidation.Interval);
    }

    private static ClaimsPrincipal User(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "test"));
}
