using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Data;
using System.Security.Claims;

namespace SamlScimReference.Web.Features.Auth;

public class UserProvisioningMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserProvisioningMiddleware> _logger;

    public UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        // Skip for non-authenticated requests or specific paths
        if (context.User.Identity?.IsAuthenticated != true || 
            context.Request.Path.StartsWithSegments("/login") ||
            context.Request.Path.StartsWithSegments("/login-success") ||
            context.Request.Path.StartsWithSegments("/access-denied") ||
            context.Request.Path.StartsWithSegments("/saml") ||
            context.Request.Path.StartsWithSegments("/Saml2") ||
            context.Request.Path.StartsWithSegments("/scim") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/_blazor") ||
            context.Request.Path.StartsWithSegments("/_framework") ||
            context.Request.Path.StartsWithSegments("/lib") ||
            context.Request.Path.StartsWithSegments("/css") ||
            context.Request.Path.StartsWithSegments("/js") ||
            context.Request.Path.Value?.Contains("blazor") == true ||
            context.Request.Path.Value?.Contains(".css") == true ||
            context.Request.Path.Value?.Contains(".js") == true ||
            context.Request.Path.Value?.Contains(".map") == true)
        {
            await _next(context);
            return;
        }

        _logger.LogInformation("UserProvisioningMiddleware checking user for path: {Path}", context.Request.Path);

        // Collect all possible identifiers from claims
        var possibleIdentifiers = new List<string>();
        
        var emailClaim = context.User.FindFirst(ClaimTypes.Email) 
            ?? context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
            ?? context.User.FindFirst("email");
        if (emailClaim != null) possibleIdentifiers.Add(emailClaim.Value.ToLowerInvariant());
        
        var upnClaim = context.User.FindFirst(ClaimTypes.Upn)
            ?? context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")
            ?? context.User.FindFirst("preferred_username");
        if (upnClaim != null) possibleIdentifiers.Add(upnClaim.Value.ToLowerInvariant());
        
        var nameIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
        if (nameIdClaim != null) possibleIdentifiers.Add(nameIdClaim.Value.ToLowerInvariant());

        if (!possibleIdentifiers.Any())
        {
            _logger.LogWarning("No email, UPN, or name identifier claim found for authenticated user at path {Path}", context.Request.Path);
            await context.SignOutAsync("cookies");
            context.Response.Redirect("/access-denied");
            return;
        }

        _logger.LogInformation("Checking SCIM database for user with identifiers: {Identifiers}", string.Join(", ", possibleIdentifiers));
        
        // Try to find user by any of the identifiers
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email != null && possibleIdentifiers.Contains(u.Email.ToLower()));

        if (user == null || !user.Active)
        {
            _logger.LogWarning("User with identifiers {Identifiers} not found or inactive in SCIM database", string.Join(", ", possibleIdentifiers));
            await context.SignOutAsync("cookies");
            context.Response.Redirect("/access-denied");
            return;
        }
        
        _logger.LogInformation("User {Email} ({UserId}) validated successfully", user.Email, user.Id);

        // Update last login (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = context.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var userToUpdate = await db.Users.FindAsync(user.Id);
                if (userToUpdate != null)
                {
                    userToUpdate.LastLoginAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update last login for user {UserId}", user.Id);
            }
        });

        await _next(context);
    }
}
