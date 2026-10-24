namespace Sepiabox.Catalog;

/// <summary>
/// A cloud service refused the token (<c>invalid_grant</c>: a Google token past the 7 days of an app in Testing mode (D-024), a Microsoft
/// one that expired or was revoked): the account has to be connected again (S-037, S-038, S-039). Shared by the web and the sync job,
/// which each throw it from their own client of the service and handle it the same way: the status becomes <c>reconnect</c>.
/// </summary>
public sealed class CloudAuthorizationException(string message, Exception? inner = null) : Exception(message, inner);
