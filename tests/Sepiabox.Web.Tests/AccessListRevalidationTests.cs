using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
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

    [Fact]
    public async Task Revalidation_reports_a_removed_user_as_no_longer_valid()
    {
        var revalidation = new AccessListRevalidation(NullLoggerFactory.Instance, new AccessList([TestUsers.Other]));
        // The framework calls this protected override on its own timer; reflection avoids waiting 5 minutes.
        var validate = typeof(AccessListRevalidation).GetMethod("ValidateAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var removed = await (Task<bool>)validate.Invoke(revalidation, [new AuthenticationState(User(TestUsers.Allowed)), CancellationToken.None])!;
        var kept = await (Task<bool>)validate.Invoke(revalidation, [new AuthenticationState(User(TestUsers.Other)), CancellationToken.None])!;

        Assert.False(removed);
        Assert.True(kept);
    }

    private static ClaimsPrincipal User(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "test"));
}
