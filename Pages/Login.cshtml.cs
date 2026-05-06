using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyHomePage.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MyHomePage.Pages;

[IgnoreAntiforgeryToken]
public class LoginModel : PageModel
{
    private readonly ICredentialRepository _credentials;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(ICredentialRepository credentials, ILogger<LoginModel> logger)
    {
        _credentials = credentials;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync(string? returnUrl = null)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        _logger.LogInformation("Login POST received from {IP}",
            HttpContext.Connection.RemoteIpAddress);

        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ",
                ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            _logger.LogWarning("Login ModelState invalid: {Errors} from {IP}",
                errors, HttpContext.Connection.RemoteIpAddress);
            return Page();
        }

        _logger.LogInformation("Validating credentials for {Email} from {IP}",
            Input.Email, HttpContext.Connection.RemoteIpAddress);

        if (_credentials.ValidateCredentials(Input.Email, Input.Password))
        {
            _logger.LogInformation("Credentials validated successfully for {Email} from {IP}",
                Input.Email, HttpContext.Connection.RemoteIpAddress);

            try
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Email, Input.Email),
                    new(ClaimTypes.Name, Input.Email)
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var properties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                };

                _logger.LogDebug("Creating authentication principal for {Email}", Input.Email);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    properties);

                _logger.LogInformation("User {Email} signed in successfully, redirecting to home",
                    Input.Email);

                return LocalRedirect("/?loginSuccess=true");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SignInAsync for {Email} from {IP}",
                    Input.Email, HttpContext.Connection.RemoteIpAddress);
                ModelState.AddModelError(string.Empty, "An error occurred during login.");
                return Page();
            }
        }

        _logger.LogWarning("Failed login attempt - invalid credentials for {Email} from {IP}",
            Input.Email, HttpContext.Connection.RemoteIpAddress);

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return Page();
    }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
