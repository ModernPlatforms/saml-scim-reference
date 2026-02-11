using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Data;

namespace SamlScimReference.Web.Features.Users;

public class UserProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> Groups { get; set; } = new();
}

public class UserProfileService
{
    private readonly AppDbContext _context;

    public UserProfileService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(string email)
    {
        var user = await _context.Users
            .Include(u => u.UserGroups)
            .ThenInclude(ug => ug.Group)
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.ToLowerInvariant());

        if (user == null)
        {
            return null;
        }

        return new UserProfileDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            GivenName = user.GivenName,
            FamilyName = user.FamilyName,
            Active = user.Active,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Groups = user.UserGroups.Select(ug => ug.Group.DisplayName).ToList()
        };
    }
}
