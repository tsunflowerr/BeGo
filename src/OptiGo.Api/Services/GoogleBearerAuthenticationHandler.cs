using System.Security.Claims;
using System.Text.Encodings.Web;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OptiGo.Api.Services;

public class GoogleBearerAuthenticationOptions : AuthenticationSchemeOptions
{
    public string[] ClientIds { get; set; } = [];
}

public class GoogleBearerAuthenticationHandler : AuthenticationHandler<GoogleBearerAuthenticationOptions>
{
    public const string SchemeName = "GoogleBearer";

    public GoogleBearerAuthenticationHandler(
        IOptionsMonitor<GoogleBearerAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ResolveBearerToken();
        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        try
        {
            var validationSettings = new GoogleJsonWebSignature.ValidationSettings();
            if (Options.ClientIds.Length > 0)
            {
                validationSettings.Audience = Options.ClientIds;
            }

            var payload = await GoogleJsonWebSignature.ValidateAsync(token, validationSettings);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, payload.Subject),
                new(ClaimTypes.Name, payload.Name ?? payload.Email ?? payload.Subject)
            };

            if (!string.IsNullOrWhiteSpace(payload.Email))
                claims.Add(new Claim(ClaimTypes.Email, payload.Email));

            if (!string.IsNullOrWhiteSpace(payload.Picture))
                claims.Add(new Claim("picture", payload.Picture));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Google bearer token validation failed.");
            return AuthenticateResult.Fail("Invalid Google bearer token.");
        }
    }

    private string? ResolveBearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        if (Request.Path.StartsWithSegments("/hubs/session") &&
            Request.Query.TryGetValue("access_token", out var accessToken))
        {
            return accessToken.ToString();
        }

        return null;
    }
}
