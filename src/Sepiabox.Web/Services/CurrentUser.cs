using System.Security.Claims;

namespace Sepiabox.Web.Services;

/// <summary>The signed-in user's e-mail address, for "me" in a search (S-021, D-018); null outside a request.</summary>
public interface ICurrentUser
{
    string? Email { get; }
}

/// <summary>The address from the request's sign-in, as the access list checked it.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public string? Email => http.HttpContext?.User.FindFirstValue(ClaimTypes.Email);
}
