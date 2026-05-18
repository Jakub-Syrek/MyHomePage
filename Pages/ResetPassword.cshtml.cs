using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MyHomePage.Abstractions;

namespace MyHomePage.Pages;

/// <summary>
/// Step 2 of the password-reset flow. Consumes a one-time token issued
/// by <see cref="ForgotPasswordModel"/>, validates it (not expired, not
/// already used, hash matches), and rewrites the stored credential via
/// <see cref="ICredentialRepository.ResetPasswordAsync"/>. Same
/// rate-limit policy as <c>/login</c> because the token-guessing cost
/// profile is identical.
/// </summary>
[EnableRateLimiting("login")]
public sealed class ResetPasswordModel : PageModel
{
    private readonly IPasswordResetTokenStore _tokens;
    private readonly ICredentialRepository _credentials;
    private readonly ILogger<ResetPasswordModel> _logger;

    public ResetPasswordModel(
        IPasswordResetTokenStore tokens,
        ICredentialRepository credentials,
        ILogger<ResetPasswordModel> logger)
    {
        _tokens = tokens;
        _credentials = credentials;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsCompleted { get; private set; }
    public bool ManualUpdateRequired { get; private set; }
    public string? NewHash { get; private set; }

    public IActionResult OnGet(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ModelState.AddModelError(string.Empty, "Missing or invalid reset link.");
            return Page();
        }
        Input.Token = token;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();

        if (Input.Password != Input.ConfirmPassword)
        {
            ModelState.AddModelError(string.Empty, "The two passwords don't match.");
            return Page();
        }

        if (Input.Password.Length < 8)
        {
            ModelState.AddModelError(string.Empty,
                "Password must be at least 8 characters — 12+ is strongly recommended.");
            return Page();
        }

        var tokenHash = HashToken(Input.Token);
        var record = await _tokens.FindActiveAsync(tokenHash, cancellationToken);
        if (record is null)
        {
            _logger.LogWarning(
                "Password reset attempted with invalid/expired token from {IP}",
                HttpContext.Connection.RemoteIpAddress);
            ModelState.AddModelError(string.Empty,
                "This reset link has expired or has already been used. Request a fresh one.");
            return Page();
        }

        var result = await _credentials.ResetPasswordAsync(record.Email, Input.Password, cancellationToken);
        switch (result.Outcome)
        {
            case PasswordResetOutcome.Updated:
                await _tokens.MarkUsedAsync(tokenHash, cancellationToken);
                IsCompleted = true;
                _logger.LogInformation(
                    "Password successfully reset for {Email} from {IP}",
                    record.Email, HttpContext.Connection.RemoteIpAddress);
                return Page();

            case PasswordResetOutcome.ManualUpdateRequired:
                await _tokens.MarkUsedAsync(tokenHash, cancellationToken);
                ManualUpdateRequired = true;
                NewHash = result.NewBCryptHash;
                _logger.LogInformation(
                    "Password reset for {Email} requires manual env-var update",
                    record.Email);
                return Page();

            default:
                ModelState.AddModelError(string.Empty,
                    "Could not update the password — the account no longer exists. Contact the operator.");
                return Page();
        }
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
        public string Token { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }
}
