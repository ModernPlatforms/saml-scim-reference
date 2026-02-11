namespace SamlScimReference.Web.Features.Scim;

public class ScimListResponse<T>
{
    public string[] Schemas { get; set; } = new[] { "urn:ietf:params:scim:api:messages:2.0:ListResponse" };
    public int TotalResults { get; set; }
    public int StartIndex { get; set; } = 1;
    public int ItemsPerPage { get; set; }
    public List<T> Resources { get; set; } = new();
}

public class ScimUser
{
    public string[] Schemas { get; set; } = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" };
    public string Id { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public ScimName? Name { get; set; }
    public string? DisplayName { get; set; }
    public List<ScimEmail>? Emails { get; set; }
    public bool Active { get; set; } = true;
    public ScimMeta? Meta { get; set; }
}

public class ScimName
{
    public string? Formatted { get; set; }
    public string? FamilyName { get; set; }
    public string? GivenName { get; set; }
}

public class ScimEmail
{
    public string Value { get; set; } = string.Empty;
    public bool Primary { get; set; }
}

public class ScimMeta
{
    public string ResourceType { get; set; } = "User";
    public DateTime? Created { get; set; }
    public DateTime? LastModified { get; set; }
    public string? Location { get; set; }
}

public class ScimGroup
{
    public string[] Schemas { get; set; } = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" };
    public string Id { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<ScimGroupMember>? Members { get; set; }
    public ScimMeta? Meta { get; set; }
}

public class ScimGroupMember
{
    public string Value { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
}

public class ScimError
{
    public string[] Schemas { get; set; } = new[] { "urn:ietf:params:scim:api:messages:2.0:Error" };
    public string Status { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
