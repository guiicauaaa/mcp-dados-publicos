using System.Net.Http.Headers;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PublicData.Shared;

namespace PublicData.Chat.Core.Mcp;

/// <summary>
/// Signs the short-lived HS256 tokens the MCP server requires over HTTP. One token is reused until less than a
/// minute is left, then a new one is signed, so a long session never sends an expired token.
/// </summary>
public sealed class McpTokenIssuer
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly McpAuthSettings _settings;
    private readonly SigningCredentials _credentials;
    private readonly TimeProvider _time;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly Lock _gate = new();
    private string? _token;
    private DateTimeOffset _expiresAt;

    public McpTokenIssuer(McpAuthSettings settings, byte[] key, TimeProvider? time = null)
    {
        _settings = settings;
        _credentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);
        _time = time ?? TimeProvider.System;
    }

    /// <exception cref="InvalidOperationException">The key file is missing, not base64 or shorter than 256 bits.</exception>
    public static McpTokenIssuer FromKeyFile(McpAuthSettings settings, TimeProvider? time = null) =>
        new(settings, SigningKeyFile.Read(settings.SigningKeyFile ?? throw new InvalidOperationException("Chat:Mcp:Auth:SigningKeyFile não configurado.")), time);

    public string GetToken()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_token is not null && _expiresAt - now > RenewBefore)
            {
                return _token;
            }

            _expiresAt = now + _settings.TokenLifetime;
            _token = _handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = _settings.Issuer,
                Audience = _settings.Audience,
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = _expiresAt.UtcDateTime,
                Claims = new Dictionary<string, object>
                {
                    [JwtRegisteredClaimNames.Sub] = McpConnection.ClientName,
                    [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                },
                SigningCredentials = _credentials,
            });
            return _token;
        }
    }
}

/// <summary>Puts a current token on every request of the Streamable HTTP transport.</summary>
internal sealed class BearerTokenHandler(McpTokenIssuer issuer) : DelegatingHandler(new SocketsHttpHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.GetToken());
        return base.SendAsync(request, cancellationToken);
    }
}
