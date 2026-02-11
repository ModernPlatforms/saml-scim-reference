using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Data;
using System.Security.Claims;

namespace SamlScimReference.Web.Features.Admin;

public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? Email { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> Groups { get; set; } = new();
}

public class AdminService
{
    private readonly AppDbContext _context;
    private readonly AuthenticationStateProvider _authStateProvider;

    public AdminService(AppDbContext context, AuthenticationStateProvider authStateProvider)
    {
        _context = context;
        _authStateProvider = authStateProvider;
    }

    public async Task<bool> IsCurrentUserAdminAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // Check for Admin role claim from SAML
        var roleClaims = user.FindAll(ClaimTypes.Role)
            .Union(user.FindAll("http://schemas.microsoft.com/ws/2008/06/identity/claims/role"))
            .Union(user.FindAll("role"));

        return roleClaims.Any(c => c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<AdminUserDto>> GetAllUsersAsync()
    {
        var users = await _context.Users
            .Include(u => u.UserGroups)
            .ThenInclude(ug => ug.Group)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        return users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            UserName = u.UserName,
            ExternalId = u.ExternalId,
            Email = u.Email,
            GivenName = u.GivenName,
            FamilyName = u.FamilyName,
            Active = u.Active,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            Groups = u.UserGroups.Select(ug => ug.Group.DisplayName).ToList()
        }).ToList();
    }
}
