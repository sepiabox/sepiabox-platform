using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Sepiabox.Web.Auth;

/// <summary>
/// Checked on every request, not only at sign-in, so someone removed from the list loses access even
/// with a cookie that is still valid.
/// </summary>
public sealed class AccessListRequirement : IAuthorizationRequirement;

public sealed class AccessListHandler(AccessList access) : AuthorizationHandler<AccessListRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AccessListRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && access.Allows(context.User.FindFirstValue(ClaimTypes.Email)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
