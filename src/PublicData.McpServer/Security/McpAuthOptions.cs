using Microsoft.IdentityModel.Tokens;

namespace PublicData.McpServer.Security;

/// <summary>
/// The "Mcp:Auth" section of the HTTP mode. Issuer and audience are not secrets and have defaults; the key only
/// comes from a file (<see cref="SigningKeyFile"/>). stdio mode does not use any of this: the client is the
/// parent process.
/// </summary>
public sealed class McpAuthOptions
{
    public const string SectionName = "Mcp:Auth";
    public const string DefaultIssuer = "public-data-api";
    public const string DefaultAudience = "public-data-mcp";

    public string Issuer { get; set; } = DefaultIssuer;

    public string Audience { get; set; } = DefaultAudience;

    /// <summary>Path to the base64 key shared with the API (/run/secrets/mcp_jwt_key in Docker Compose).</summary>
    public string? SigningKeyFile { get; set; }

    /// <summary>Local development only: serves /mcp without a token. Without it and without a key, the server does not start.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// The validation the Bearer handler runs on every /mcp request. A token from another issuer, for another
    /// audience, expired (beyond one minute of clock skew), signed with another key or with another algorithm
    /// gets 401 before reaching the MCP handler.
    /// </summary>
    internal static TokenValidationParameters CreateValidationParameters(McpAuthOptions options, byte[] key) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        // Only what the API signs with: rules out "alg": "none" and algorithm confusion.
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };
}
