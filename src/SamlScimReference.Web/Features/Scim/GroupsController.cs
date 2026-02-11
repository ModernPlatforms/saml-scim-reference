using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Data;

namespace SamlScimReference.Web.Features.Scim;

[ApiController]
[Route("scim/v2/[controller]")]
public class GroupsController : ControllerBase
{
    private readonly AppDbContext _context;

    public GroupsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetGroups([FromQuery] int? startIndex, [FromQuery] int? count)
    {
        var start = startIndex ?? 1;
        var itemsPerPage = count ?? 100;

        var totalResults = await _context.Groups.CountAsync();

        var groups = await _context.Groups
            .Include(g => g.UserGroups)
            .ThenInclude(ug => ug.User)
            .OrderBy(g => g.CreatedAt)
            .Skip(start - 1)
            .Take(itemsPerPage)
            .ToListAsync();

        var scimGroups = groups.Select(MapToScimGroup).ToList();

        return Ok(new ScimListResponse<ScimGroup>
        {
            TotalResults = totalResults,
            StartIndex = start,
            ItemsPerPage = scimGroups.Count,
            Resources = scimGroups
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetGroup(string id)
    {
        var group = await _context.Groups
            .Include(g => g.UserGroups)
            .ThenInclude(ug => ug.User)
            .FirstOrDefaultAsync(g => g.Id == id);
        
        if (group == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "Group not found" });
        }

        return Ok(MapToScimGroup(group));
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] ScimGroup scimGroup)
    {
        var group = new Group
        {
            DisplayName = scimGroup.DisplayName,
            ExternalId = scimGroup.ExternalId
        };

        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        // Add members if provided
        if (scimGroup.Members != null && scimGroup.Members.Any())
        {
            foreach (var member in scimGroup.Members)
            {
                var user = await _context.Users.FindAsync(member.Value);
                if (user != null)
                {
                    _context.UserGroups.Add(new UserGroup
                    {
                        UserId = user.Id,
                        GroupId = group.Id
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        var createdGroup = await _context.Groups
            .Include(g => g.UserGroups)
            .ThenInclude(ug => ug.User)
            .FirstAsync(g => g.Id == group.Id);

        return Created($"/scim/v2/Groups/{group.Id}", MapToScimGroup(createdGroup));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateGroup(string id, [FromBody] ScimGroup scimGroup)
    {
        var group = await _context.Groups
            .Include(g => g.UserGroups)
            .FirstOrDefaultAsync(g => g.Id == id);
        
        if (group == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "Group not found" });
        }

        group.DisplayName = scimGroup.DisplayName;
        group.ExternalId = scimGroup.ExternalId;
        group.UpdatedAt = DateTime.UtcNow;

        // Update members - remove all and re-add
        _context.UserGroups.RemoveRange(group.UserGroups);
        
        if (scimGroup.Members != null)
        {
            foreach (var member in scimGroup.Members)
            {
                var user = await _context.Users.FindAsync(member.Value);
                if (user != null)
                {
                    _context.UserGroups.Add(new UserGroup
                    {
                        UserId = user.Id,
                        GroupId = group.Id
                    });
                }
            }
        }

        await _context.SaveChangesAsync();

        var updatedGroup = await _context.Groups
            .Include(g => g.UserGroups)
            .ThenInclude(ug => ug.User)
            .FirstAsync(g => g.Id == id);

        return Ok(MapToScimGroup(updatedGroup));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> PatchGroup(string id, [FromBody] ScimPatchRequest patchRequest)
    {
        var group = await _context.Groups
            .Include(g => g.UserGroups)
            .FirstOrDefaultAsync(g => g.Id == id);
        
        if (group == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "Group not found" });
        }

        foreach (var operation in patchRequest.Operations ?? new List<ScimPatchOperation>())
        {
            var op = operation.Op?.ToLower();

            if (op == "add" && operation.Path?.ToLower() == "members")
            {
                // Add members to group
                if (operation.Value != null)
                {
                    var members = ParseMembers(operation.Value);
                    foreach (var member in members)
                    {
                        if (!group.UserGroups.Any(ug => ug.UserId == member.Value))
                        {
                            var user = await _context.Users.FindAsync(member.Value);
                            if (user != null)
                            {
                                _context.UserGroups.Add(new UserGroup
                                {
                                    UserId = user.Id,
                                    GroupId = group.Id
                                });
                            }
                        }
                    }
                }
            }
            else if (op == "remove" && operation.Path?.ToLower().StartsWith("members") == true)
            {
                // Remove members from group
                // Path format: members[value eq "userId"]
                var userId = ExtractUserIdFromPath(operation.Path);
                if (!string.IsNullOrEmpty(userId))
                {
                    var userGroup = group.UserGroups.FirstOrDefault(ug => ug.UserId == userId);
                    if (userGroup != null)
                    {
                        _context.UserGroups.Remove(userGroup);
                    }
                }
            }
            else if (op == "replace")
            {
                // Handle displayName updates
                if (operation.Path?.ToLower() == "displayname" && operation.Value is string displayName)
                {
                    group.DisplayName = displayName;
                }
                else if (operation.Value != null)
                {
                    var jsonElement = (System.Text.Json.JsonElement)operation.Value;
                    if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (jsonElement.TryGetProperty("displayName", out var displayNameProp))
                        {
                            group.DisplayName = displayNameProp.GetString() ?? group.DisplayName;
                        }
                        if (jsonElement.TryGetProperty("members", out var membersProp))
                        {
                            // Replace all members
                            _context.UserGroups.RemoveRange(group.UserGroups);
                            var members = ParseMembers(membersProp);
                            foreach (var member in members)
                            {
                                var user = await _context.Users.FindAsync(member.Value);
                                if (user != null)
                                {
                                    _context.UserGroups.Add(new UserGroup
                                    {
                                        UserId = user.Id,
                                        GroupId = group.Id
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        group.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updatedGroup = await _context.Groups
            .Include(g => g.UserGroups)
            .ThenInclude(ug => ug.User)
            .FirstAsync(g => g.Id == id);

        return Ok(MapToScimGroup(updatedGroup));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(string id)
    {
        var group = await _context.Groups.FindAsync(id);
        
        if (group == null)
        {
            return NotFound(new ScimError { Status = "404", Detail = "Group not found" });
        }

        _context.Groups.Remove(group);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private List<ScimGroupMember> ParseMembers(object value)
    {
        var members = new List<ScimGroupMember>();
        
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in jsonElement.EnumerateArray())
                {
                    if (item.TryGetProperty("value", out var valueProp))
                    {
                        members.Add(new ScimGroupMember
                        {
                            Value = valueProp.GetString() ?? "",
                            Display = item.TryGetProperty("display", out var displayProp) 
                                ? displayProp.GetString() ?? "" 
                                : ""
                        });
                    }
                }
            }
        }
        
        return members;
    }

    private string? ExtractUserIdFromPath(string path)
    {
        // Extract userId from path like: members[value eq "userId"]
        var match = System.Text.RegularExpressions.Regex.Match(path, @"members\[value eq ""([^""]+)""\]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private ScimGroup MapToScimGroup(Group group)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        
        return new ScimGroup
        {
            Id = group.Id,
            ExternalId = group.ExternalId,
            DisplayName = group.DisplayName,
            Members = group.UserGroups?.Select(ug => new ScimGroupMember
            {
                Value = ug.User.Id,
                Display = ug.User.UserName
            }).ToList(),
            Meta = new ScimMeta
            {
                ResourceType = "Group",
                Created = group.CreatedAt,
                LastModified = group.UpdatedAt,
                Location = $"{baseUrl}/scim/v2/Groups/{group.Id}"
            }
        };
    }
}
