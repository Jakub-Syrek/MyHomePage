using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyHomePage.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MyHomePage.Pages;

public class LoginModel : PageModel
{
    private readonly ICredentialRepository _credentials;

    public LoginModel(ICredentialRepository credentials)
    {
        _credentials = credentials;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync(string? returnUrl = null)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (ModelState.IsValid)
        {
            if (_credentials.ValidateCredentials(Input.Email, Input.Password))
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Email, Input.Email),
                    new Claim(ClaimTypes.Name, Input.Email)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                return LocalRedirect("/?loginSuccess=true");
            }
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        }

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
