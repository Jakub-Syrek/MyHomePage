using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MyHomePage.Abstractions;

namespace MyHomePage.Pages;

/// <summary>
/// Step 1 of the password-reset flow. Always responds with the same
/// "if an account exists…" notice regardless of whether the email is
/// known so we don't turn this endpoint into an account-enumeration
/// oracle. Rate-limited under the same partition as <c>/login</c>
/// because the cost profile is identical (one POST per IP per click).
/// </summary>
[EnableRateLimiting("login")]
public sealed class ForgotPasswordModel : PageModel
{
    private readonly ICredentialRepository _credentials;
    private readonly IPasswordResetTokenStore _tokens;
    private readonly IEmailSender _email;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        ICredentialRepository credentials,
        IPasswordResetTokenStore tokens,
        IEmailSender email,
        ILogger<ForgotPasswordModel> logger)
    {
        _credentials = credentials;
        _tokens = tokens;
        _email = email;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Set after a successful POST so the view collapses the form
    /// and shows the same generic confirmation regardless of outcome.</summary>
    public bool WasSubmitted { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();

        _logger.LogInformation(
            "Password-reset requested for {Email} from {IP}",
            Input.Email, HttpContext.Connection.RemoteIpAddress);

        // Always claim success — never reveal whether the email is known.
        WasSubmitted = true;

        if (!_credentials.HasAccount(Input.Email))
        {
            _logger.LogWarning(
                "Password-reset for unknown email {Email} from {IP} — silently dropped",
                Input.Email, HttpContext.Connection.RemoteIpAddress);
            return Page();
        }

        // 32 bytes ≈ 256 bits of entropy, URL-safe Base64 so the link
        // stays readable. We email the plaintext, persist only the hash.
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = HashToken(rawToken);
        var record = new PasswordResetToken(
            tokenHash, Input.Email, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null);
        await _tokens.SaveAsync(record, cancellationToken);

        var resetLink =
            $"{Request.Scheme}://{Request.Host}{Url.Page("/ResetPassword", values: new { token = rawToken })}";
        var body =
            $"Hi,\n\nA password reset was requested for this account. " +
            $"Open the link below within one hour to set a new password:\n\n{resetLink}\n\n" +
            "If you didn't request this, you can safely ignore this email.";
        await _email.SendAsync(Input.Email, "Reset your My Home Page password", body, cancellationToken);

        return Page();
    }

    private static string HashToken(string raw)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }
}
