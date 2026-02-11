using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Data;

namespace SamlScimReference.Web.Features.Scim;

[ApiController]
[Route("scim/v2/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] int? startIndex, [FromQuery] int? count, [FromQuery] string? filter)
    {
        try
        {
            var query = _context.Users.AsQueryable();

            // Basic filter support for userName
            if (!string.IsNullOrEmpty(filter) && filter.Contains("userName eq", StringComparison.OrdinalIgnoreCase))
            {
                var userName = ExtractFilterValue(filter);
                if (!string.IsNullOrEmpty(userName))
                {
                    query = query.Where(u => u.UserName == userName);
                }
            }

            var totalResults = await query.CountAsync();
            var start = startIndex ?? 1;
            var itemsPerPage = count ?? 100;

            var users = await query
                .OrderBy(u => u.CreatedAt)
                .Skip(start - 1)
                .Take(itemsPerPage)
                .ToListAsync();

            var scimUsers = users.Select(MapToScimUser).ToList();

            return Ok(new ScimListResponse<ScimUser>
            {
                TotalResults = totalResults,
                StartIndex = start,
                ItemsPerPage = scimUsers.Count,
                Resources = scimUsers
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" },
                detail = $"Internal server error: {ex.Message}",
                status = "500"
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        var user = await _context.Users.FindAsync(id);
        
        if (user == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "User not found" });
        }

        return Ok(MapToScimUser(user));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] ScimUser scimUser)
    {
        // Check if user already exists
        if (await _context.Users.AnyAsync(u => u.UserName == scimUser.UserName))
        {
            return Conflict(new ScimError { Status = "409", Detail = "User already exists" });
        }

        var user = new User
        {
            UserName = scimUser.UserName,
            ExternalId = scimUser.ExternalId,
            Email = scimUser.Emails?.FirstOrDefault(e => e.Primary)?.Value ?? scimUser.Emails?.FirstOrDefault()?.Value,
            GivenName = scimUser.Name?.GivenName,
            FamilyName = scimUser.Name?.FamilyName,
            Active = scimUser.Active
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var createdScimUser = MapToScimUser(user);
        
        return Created($"/scim/v2/Users/{user.Id}", createdScimUser);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] ScimUser scimUser)
    {
        var user = await _context.Users.FindAsync(id);
        
        if (user == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "User not found" });
        }

        user.UserName = scimUser.UserName;
        user.ExternalId = scimUser.ExternalId;
        user.Email = scimUser.Emails?.FirstOrDefault(e => e.Primary)?.Value ?? scimUser.Emails?.FirstOrDefault()?.Value;
        user.GivenName = scimUser.Name?.GivenName;
        user.FamilyName = scimUser.Name?.FamilyName;
        user.Active = scimUser.Active;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(MapToScimUser(user));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> PatchUser(string id, [FromBody] ScimPatchRequest patchRequest)
    {
        var user = await _context.Users.FindAsync(id);
        
        if (user == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "User not found" });
        }

        // Process each operation in the patch request
        foreach (var operation in patchRequest.Operations ?? new List<ScimPatchOperation>())
        {
            if (operation.Op?.ToLower() == "replace")
            {
                // Handle path-based updates
                if (!string.IsNullOrEmpty(operation.Path))
                {
                    var path = operation.Path.ToLower();
                    if (path == "active" && operation.Value is bool activeValue)
                    {
                        user.Active = activeValue;
                    }
                    else if (path.Contains("emails") && operation.Value is List<object> emails)
                    {
                        // Handle email updates
                        var firstEmail = emails.FirstOrDefault();
                        if (firstEmail != null)
                        {
                            var emailValue = System.Text.Json.JsonSerializer.Deserialize<ScimEmail>(
                                System.Text.Json.JsonSerializer.Serialize(firstEmail));
                            user.Email = emailValue?.Value;
                        }
                    }
                }
                // Handle value-based updates (no path specified)
                else if (operation.Value != null)
                {
                    var jsonElement = (System.Text.Json.JsonElement)operation.Value;
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (jsonElement.TryGetProperty("active", out var activeProp))
                        {
                            user.Active = activeProp.GetBoolean();
                        }
                        if (jsonElement.TryGetProperty("userName", out var userNameProp))
                        {
                            user.UserName = userNameProp.GetString() ?? user.UserName;
                        }
                        if (jsonElement.TryGetProperty("externalId", out var externalIdProp))
                        {
                            user.ExternalId = externalIdProp.GetString();
                        }
                        if (jsonElement.TryGetProperty("name", out var nameProp))
                        {
                            if (nameProp.TryGetProperty("givenName", out var givenNameProp))
                            {
                                user.GivenName = givenNameProp.GetString();
                            }
                            if (nameProp.TryGetProperty("familyName", out var familyNameProp))
                            {
                                user.FamilyName = familyNameProp.GetString();
                            }
                        }
                        if (jsonElement.TryGetProperty("emails", out var emailsProp))
                        {
                            if (emailsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                var firstEmail = emailsProp.EnumerateArray().FirstOrDefault();
                                if (firstEmail.ValueKind == System.Text.Json.JsonValueKind.Object && 
                                    firstEmail.TryGetProperty("value", out var emailValue))
                                {
                                    user.Email = emailValue.GetString();
                                }
                            }
                        }
                    }
                }
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(MapToScimUser(user));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _context.Users.FindAsync(id);
        
        if (user == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "User not found" });
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private ScimUser MapToScimUser(User user)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        
        return new ScimUser
        {
            Id = user.Id,
            ExternalId = user.ExternalId,
            UserName = user.UserName,
            DisplayName = !string.IsNullOrEmpty(user.GivenName) || !string.IsNullOrEmpty(user.FamilyName)
                ? $"{user.GivenName} {user.FamilyName}".Trim()
                : user.UserName,
            Name = new ScimName
            {
                GivenName = user.GivenName,
                FamilyName = user.FamilyName,
                Formatted = !string.IsNullOrEmpty(user.GivenName) || !string.IsNullOrEmpty(user.FamilyName)
                    ? $"{user.GivenName} {user.FamilyName}".Trim()
                    : null
            },
            Emails = string.IsNullOrEmpty(user.Email) ? null : new List<ScimEmail>
            {
                new ScimEmail { Value = user.Email, Primary = true }
            },
            Active = user.Active,
            Meta = new ScimMeta
            {
                ResourceType = "User",
                Created = user.CreatedAt,
                LastModified = user.UpdatedAt,
                Location = $"{baseUrl}/scim/v2/Users/{user.Id}"
            }
        };
    }

    private string? ExtractFilterValue(string filter)
    {
        // Simple extraction for "userName eq \"value\""
        var parts = filter.Split(new[] { " eq " }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return parts[1].Trim().Trim('"', '\'');
        }
        return null;
    }
}
