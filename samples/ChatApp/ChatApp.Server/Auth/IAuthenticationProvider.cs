using System.Security.Claims;
using System.Threading.Tasks;

namespace PulseRPC.Server.Authentication;

/// <summary>
/// Authentication provider interface for ChatApp sample
/// </summary>
/// <remarks>
/// This is a simplified authentication interface for the sample project.
/// Production systems should use IAuthenticationValidator from PulseRPC.Abstractions.
/// </remarks>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Authenticate credentials
    /// </summary>
    /// <param name="credentials">Credentials string</param>
    /// <returns>Authentication result</returns>
    Task<AuthenticationResult> AuthenticateAsync(string credentials);
}

/// <summary>
/// Authentication result
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// Whether authentication was successful
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Authenticated user principal
    /// </summary>
    public ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// Error message if authentication failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Create a success result
    /// </summary>
    public static AuthenticationResult Success(ClaimsPrincipal user)
    {
        return new AuthenticationResult
        {
            IsAuthenticated = true,
            User = user
        };
    }

    /// <summary>
    /// Create a failure result
    /// </summary>
    public static AuthenticationResult Fail(string errorMessage)
    {
        return new AuthenticationResult
        {
            IsAuthenticated = false,
            ErrorMessage = errorMessage
        };
    }
}
