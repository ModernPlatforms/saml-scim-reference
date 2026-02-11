using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace SamlScimReference.Web.Features.Auth;

[Route("auth")]
public class SamlLoginController : Controller
{
    [HttpGet("saml-login")]
    public IActionResult Login(string? returnUrl = null)
    {
        var redirectUri = string.IsNullOrEmpty(returnUrl) 
            ? Url.Action("Success", "SamlLogin")
            : returnUrl;

        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUri
        };

        return Challenge(properties, "saml2");
    }

    [HttpGet("login-success")]
    public IActionResult Success()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect("/");
        }
        return Redirect("/login");
    }
}
