using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Sepiabox.Web.Auth;

/// <summary>
/// An interactive circuit lives on after the request that opened it, so the access list is checked again
/// every few minutes; someone removed from the list then sees the "no access" content of
/// <c>AuthorizeRouteView</c> instead of the page (D-048).
/// </summary>
public sealed class AccessListRevalidation(ILoggerFactory loggerFactory, AccessList access)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override TimeSpan RevalidationInterval => Interval;

    public bool IsStillAllowed(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && access.Allows(user.FindFirstValue(ClaimTypes.Email));

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        Task.FromResult(IsStillAllowed(authenticationState.User));
}
