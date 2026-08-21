using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SchoolPOS.Domain.Abstractions;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Autentica al Sync Agent de una escuela por la llave de <c>/api/sync/*</c>, enviada como
/// <c>Authorization: Bearer sync_&lt;id&gt;.&lt;secreto&gt;</c> — nunca una cookie, nunca una
/// contraseña de usuario. Si la llave verifica, el principal lleva el mismo claim
/// <see cref="ClaimsExtensions.SchoolIdClaim"/> que ya usan las políticas de <c>/School/*</c>,
/// así que los endpoints de sincronización pueden reusar <c>User.GetSchoolId()</c> tal cual.
/// </summary>
public sealed class SyncApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SyncApiKey";

    private readonly ISyncApiKeyService _keys;

    public SyncApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, ISyncApiKeyService keys)
        : base(options, logger, encoder)
    {
        _keys = keys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Falta el encabezado Authorization: Bearer <llave>.");

        var rawKey = header["Bearer ".Length..].Trim();
        var schoolId = await _keys.VerifyAsync(rawKey, Context.RequestAborted);
        if (schoolId is null)
            return AuthenticateResult.Fail("Llave de sincronización inválida o revocada.");

        var claims = new[] { new Claim(ClaimsExtensions.SchoolIdClaim, schoolId.Value.ToString()) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
