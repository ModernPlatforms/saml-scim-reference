namespace SamlScimReference.Web.Features.Scim;

public class ScimAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public ScimAuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/scim"))
        {
            var expectedToken = _configuration["Scim:BearerToken"];
            
            if (string.IsNullOrEmpty(expectedToken))
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new ScimError 
                { 
                    Status = "500", 
                    Detail = "SCIM authentication not configured" 
                });
                return;
            }

            var authHeader = context.Request.Headers["Authorization"].ToString();
            
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 401;
                context.Response.Headers["WWW-Authenticate"] = "Bearer";
                await context.Response.WriteAsJsonAsync(new ScimError 
                { 
                    Status = "401", 
                    Detail = "Authentication required" 
                });
                return;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            if (token != expectedToken)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new ScimError 
                { 
                    Status = "401", 
                    Detail = "Invalid authentication token" 
                });
                return;
            }
        }

        await _next(context);
    }
}
