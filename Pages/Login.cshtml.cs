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
        if (!ModelState.IsValid)
            return Page();

        if (_credentials.ValidateCredentials(Input.Email, Input.Password))
        {
            _logger.LogInformation("Login successful for {Email} from {IP}",
                Input.Email, HttpContext.Connection.RemoteIpAddress);

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

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                properties);

            return LocalRedirect("/?loginSuccess=true");
        }

        _logger.LogWarning("Failed login attempt for {Email} from {IP}",
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
