using System.Security.Claims;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Endpoints;

/// <summary>
/// Minimal-API endpoints implementing the WebAuthn ceremonies (registration
/// and authentication) on top of the existing cookie authentication.
///
/// Flow:
///   - Registration is only allowed for users who have already signed in with
///     email + password (the cookie is the only way to associate a newly
///     created passkey with an identity).
///   - Login is anonymous: the assertion proves possession of a private key
///     bound to a previously registered public key, after which the standard
///     cookie principal is issued.
/// </summary>
public static class PasskeyEndpoints
{
    private const string RegistrationSessionKey = "fido2.attestationOptions";
    private const string AssertionSessionKey = "fido2.assertionOptions";

    /// <summary>Registers every <c>/auth/passkey/*</c> route on the supplied builder.</summary>
    /// <param name="endpoints">Route builder.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapPasskeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth/passkey");

        group.MapPost("/register/begin", RegisterBeginAsync).RequireAuthorization();
        group.MapPost("/register/complete", RegisterCompleteAsync).RequireAuthorization();

        group.MapPost("/login/begin", LoginBeginAsync).AllowAnonymous();
        group.MapPost("/login/complete", LoginCompleteAsync).AllowAnonymous();

        group.MapGet("/list", ListAsync).RequireAuthorization();
        group.MapDelete("/{credentialId}", DeleteAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterBeginAsync(
        HttpContext context,
        [FromServices] IFido2 fido2,
        [FromServices] IPasskeyStore store,
        CancellationToken cancellationToken)
    {
        var email = ResolveEmail(context.User);
        if (string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        var existing = await store.GetByEmailAsync(email, cancellationToken);
        var excludeCredentials = existing
            .Select(c => new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(c.CredentialId)))
            .ToList();

        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                DisplayName = email,
                Name = email,
                Id = Encoding.UTF8.GetBytes(email),
            },
            ExcludeCredentials = excludeCredentials,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        context.Session.SetString(RegistrationSessionKey, options.ToJson());
        return Results.Text(options.ToJson(), "application/json");
    }

    private static async Task<IResult> RegisterCompleteAsync(
        HttpContext context,
        [FromServices] IFido2 fido2,
        [FromServices] IPasskeyStore store,
        [FromBody] PasskeyRegistrationCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var email = ResolveEmail(context.User);
        if (string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        var optionsJson = context.Session.GetString(RegistrationSessionKey);
        if (string.IsNullOrEmpty(optionsJson))
        {
            return Results.BadRequest(new { error = "No registration ceremony in progress." });
        }

        var originalOptions = CredentialCreateOptions.FromJson(optionsJson);

        IsCredentialIdUniqueToUserAsyncDelegate isUnique = async (param, ct) =>
        {
            var byId = await store.GetByCredentialIdAsync(WebEncoders.Base64UrlEncode(param.CredentialId), ct);
            return byId is null;
        };

        RegisteredPublicKeyCredential registered;
        try
        {
            registered = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = request.AttestationResponse,
                OriginalOptions = originalOptions,
                IsCredentialIdUniqueToUserCallback = isUnique,
            }, cancellationToken);
        }
        catch (Fido2VerificationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var transports = registered.Transports?.Select(t => t.ToString()).ToList()
            ?? new List<string>();

        var credential = new PasskeyCredential(
            UserEmail: email,
            UserHandle: WebEncoders.Base64UrlEncode(registered.User.Id),
            CredentialId: WebEncoders.Base64UrlEncode(registered.Id),
            PublicKey: WebEncoders.Base64UrlEncode(registered.PublicKey),
            SignatureCounter: registered.SignCount,
            AaGuid: registered.AaGuid,
            Nickname: string.IsNullOrWhiteSpace(request.Nickname)
                ? $"Passkey ({DateTime.UtcNow:yyyy-MM-dd})"
                : request.Nickname.Trim(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastUsedAtUtc: null,
            Transports: transports);

        await store.AddAsync(credential, cancellationToken);
        context.Session.Remove(RegistrationSessionKey);

        return Results.Ok(ToDescriptor(credential));
    }

    private static async Task<IResult> LoginBeginAsync(
        HttpContext context,
        [FromServices] IFido2 fido2,
        [FromServices] IPasskeyStore store,
        [FromBody] PasskeyLoginBeginRequest request,
        CancellationToken cancellationToken)
    {
        var allowed = new List<PublicKeyCredentialDescriptor>();
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var creds = await store.GetByEmailAsync(request.Email.Trim(), cancellationToken);
            allowed.AddRange(creds.Select(c =>
                new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(c.CredentialId))));
        }

        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowed,
            UserVerification = UserVerificationRequirement.Preferred,
        });

        context.Session.SetString(AssertionSessionKey, options.ToJson());
        return Results.Text(options.ToJson(), "application/json");
    }

    private static async Task<IResult> LoginCompleteAsync(
        HttpContext context,
        [FromServices] IFido2 fido2,
        [FromServices] IPasskeyStore store,
        [FromBody] PasskeyLoginCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var optionsJson = context.Session.GetString(AssertionSessionKey);
        if (string.IsNullOrEmpty(optionsJson))
        {
            return Results.BadRequest(new { error = "No login ceremony in progress." });
        }

        var originalOptions = AssertionOptions.FromJson(optionsJson);

        var credentialId = WebEncoders.Base64UrlEncode(request.AssertionResponse.RawId);
        var stored = await store.GetByCredentialIdAsync(credentialId, cancellationToken);
        if (stored is null)
        {
            return Results.BadRequest(new { error = "Unknown credential." });
        }

        IsUserHandleOwnerOfCredentialIdAsync isOwner = async (param, ct) =>
        {
            var owners = await store.GetByUserHandleAsync(WebEncoders.Base64UrlEncode(param.UserHandle), ct);
            return owners.Any(c => c.CredentialId == WebEncoders.Base64UrlEncode(param.CredentialId));
        };

        VerifyAssertionResult result;
        try
        {
            result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.AssertionResponse,
                OriginalOptions = originalOptions,
                StoredPublicKey = WebEncoders.Base64UrlDecode(stored.PublicKey),
                StoredSignatureCounter = stored.SignatureCounter,
                IsUserHandleOwnerOfCredentialIdCallback = isOwner,
            }, cancellationToken);
        }
        catch (Fido2VerificationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var updated = stored with
        {
            SignatureCounter = result.SignCount,
            LastUsedAtUtc = DateTimeOffset.UtcNow,
        };
        await store.UpdateAsync(updated, cancellationToken);
        context.Session.Remove(AssertionSessionKey);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, stored.UserEmail),
            new Claim(ClaimTypes.Name, stored.UserEmail),
            new Claim("amr", "passkey"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
        };

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);

        return Results.Ok(new { ok = true, email = stored.UserEmail });
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        [FromServices] IPasskeyStore store,
        CancellationToken cancellationToken)
    {
        var email = ResolveEmail(context.User);
        if (string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        var creds = await store.GetByEmailAsync(email, cancellationToken);
        var descriptors = creds.Select(ToDescriptor).ToList();
        return Results.Ok(descriptors);
    }

    private static PasskeyDescriptor ToDescriptor(PasskeyCredential credential) => new(
        credential.CredentialId,
        credential.Nickname,
        PasskeyTypeFormatter.Describe(credential.Transports),
        credential.CreatedAtUtc,
        credential.LastUsedAtUtc);

    private static async Task<IResult> DeleteAsync(
        string credentialId,
        HttpContext context,
        [FromServices] IPasskeyStore store,
        CancellationToken cancellationToken)
    {
        var email = ResolveEmail(context.User);
        if (string.IsNullOrEmpty(email))
        {
            return Results.Unauthorized();
        }

        var existing = await store.GetByCredentialIdAsync(credentialId, cancellationToken);
        if (existing is null)
        {
            return Results.NoContent();
        }

        if (!string.Equals(existing.UserEmail, email, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        await store.DeleteAsync(credentialId, cancellationToken);
        return Results.NoContent();
    }

    private static string? ResolveEmail(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue(ClaimTypes.Name);

    /// <summary>Body of <c>POST /auth/passkey/register/complete</c>.</summary>
    /// <param name="AttestationResponse">Authenticator attestation produced by <c>navigator.credentials.create()</c>.</param>
    /// <param name="Nickname">User-friendly label for the new credential.</param>
    public sealed record PasskeyRegistrationCompleteRequest(
        AuthenticatorAttestationRawResponse AttestationResponse,
        string? Nickname);

    /// <summary>Body of <c>POST /auth/passkey/login/begin</c>.</summary>
    /// <param name="Email">
    /// Optional. When supplied, restricts the assertion challenge to credentials
    /// registered against that email; otherwise the authenticator may surface
    /// any resident credential it holds.
    /// </param>
    public sealed record PasskeyLoginBeginRequest(string? Email);

    /// <summary>Body of <c>POST /auth/passkey/login/complete</c>.</summary>
    /// <param name="AssertionResponse">Authenticator assertion produced by <c>navigator.credentials.get()</c>.</param>
    public sealed record PasskeyLoginCompleteRequest(
        AuthenticatorAssertionRawResponse AssertionResponse);
}
