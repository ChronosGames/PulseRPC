using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// Optional authentication handler interface for securing RPC connections.
/// Implement this interface to add custom authentication logic to your server.
/// </summary>
/// <remarks>
/// Authentication happens in two stages:
/// 1. Connection authentication: Validates credentials when a client connects
/// 2. Request authorization: Validates access to specific service methods per request
///
/// If no authentication handler is registered, all connections and requests are allowed.
/// </remarks>
public interface IAuthenticationHandler
{
    /// <summary>
    /// Authenticates a client connection based on provided credentials.
    /// Called once per connection during handshake.
    /// </summary>
    /// <param name="connectionId">Unique identifier for the connection.</param>
    /// <param name="credentials">Authentication credentials from client (e.g., token, username/password).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result containing success status and optional claims.</returns>
    Task<AuthenticationResult> AuthenticateConnectionAsync(
        string connectionId,
        AuthenticationCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes a request to invoke a specific service method.
    /// Called before every RPC method invocation.
    /// </summary>
    /// <param name="context">Request context containing user identity and metadata.</param>
    /// <param name="serviceName">Name of the service being invoked.</param>
    /// <param name="methodName">Name of the method being invoked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authorization result indicating whether the request is allowed.</returns>
    Task<AuthorizationResult> AuthorizeRequestAsync(
        IRequestContext context,
        string serviceName,
        string methodName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authentication credentials provided by the client.
/// </summary>
public class AuthenticationCredentials
{
    /// <summary>
    /// Gets or sets the authentication scheme (e.g., "Bearer", "Basic", "ApiKey").
    /// </summary>
    public string Scheme { get; set; } = "Bearer";

    /// <summary>
    /// Gets or sets the credential token or value.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets additional authentication parameters.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>
/// Result of authentication attempt.
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// Gets or sets whether authentication succeeded.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the authenticated user's claims.
    /// </summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// Gets or sets the failure reason if authentication failed.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Creates a successful authentication result.
    /// </summary>
    public static AuthenticationResult Success(ClaimsPrincipal principal) => new()
    {
        IsAuthenticated = true,
        Principal = principal
    };

    /// <summary>
    /// Creates a failed authentication result.
    /// </summary>
    public static AuthenticationResult Failure(string reason) => new()
    {
        IsAuthenticated = false,
        FailureReason = reason
    };
}

/// <summary>
/// Result of authorization check.
/// </summary>
public class AuthorizationResult
{
    /// <summary>
    /// Gets or sets whether the request is authorized.
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// Gets or sets the denial reason if authorization failed.
    /// </summary>
    public string? DenialReason { get; set; }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Allow() => new() { IsAuthorized = true };

    /// <summary>
    /// Creates a denied authorization result.
    /// </summary>
    public static AuthorizationResult Deny(string reason) => new()
    {
        IsAuthorized = false,
        DenialReason = reason
    };
}
