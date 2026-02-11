using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Data;
using System.Security.Claims;

namespace SamlScimReference.Web.Features.Auth;

public class SamlAuthenticationHandler
{
    private readonly AppDbContext _context;
    private readonly ILogger<SamlAuthenticationHandler> _logger;

    public SamlAuthenticationHandler(AppDbContext context, ILogger<SamlAuthenticationHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> ValidateAndUpdateUserAsync(ClaimsPrincipal principal)
    {
        // Extract email from SAML claims
        var emailClaim = principal.FindFirst(ClaimTypes.Email) 
            ?? principal.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
            ?? principal.FindFirst("email");

        if (emailClaim == null || string.IsNullOrEmpty(emailClaim.Value))
        {
            _logger.LogWarning("No email claim found in SAML assertion");
            return false;
        }

        var email = emailClaim.Value.ToLowerInvariant();

        // Find user by email (SCIM is source of truth)
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);

        if (user == null)
        {
            _logger.LogWarning("User with email {Email} not found in SCIM-provisioned users", email);
            return false;
        }

        if (!user.Active)
        {
            _logger.LogWarning("User {Email} is inactive", email);
            return false;
        }

        // Update last login time
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.UserGroups)
            .ThenInclude(ug => ug.Group)
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.ToLowerInvariant());
    }
}
