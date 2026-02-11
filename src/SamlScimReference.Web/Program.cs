using Microsoft.EntityFrameworkCore;
using SamlScimReference.Web.Components;
using SamlScimReference.Web.Data;
using SamlScimReference.Web.Features.Auth;
using SamlScimReference.Web.Features.Users;
using SamlScimReference.Web.Features.Admin;
using SamlScimReference.Web.Features.Scim;
using Sustainsys.Saml2;
using Sustainsys.Saml2.Metadata;
using Sustainsys.Saml2.AspNetCore2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Configure Data Protection to persist keys to Azure Blob Storage
var storageConnectionString = builder.Configuration["AzureStorage:ConnectionString"];
if (!string.IsNullOrEmpty(storageConnectionString))
{
    var blobServiceClient = new BlobServiceClient(storageConnectionString);
    var blobContainerClient = blobServiceClient.GetBlobContainerClient("dataprotection-keys");
    
    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(blobContainerClient.GetBlobClient("keys.xml"))
        .SetApplicationName("saml-scim-reference");
}

// Configure forwarded headers for Azure Container Apps (behind reverse proxy)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var useAzureSql = !string.IsNullOrEmpty(connectionString);

if (useAzureSql)
{
    // Use Azure SQL Database
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(connectionString));
}
else
{
    // Use SQLite for local development
    var dbPath = builder.Configuration["Database:Path"] ?? "app.db";
    var dbDirectory = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
    {
        Directory.CreateDirectory(dbDirectory);
    }

    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        var sqliteConnectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        options.UseSqlite(sqliteConnectionString, sqliteOptions =>
        {
            sqliteOptions.CommandTimeout(60);
        });
    });
}

// Add Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "cookies";
    options.DefaultSignInScheme = "cookies";
    options.DefaultChallengeScheme = "saml2";
})
.AddCookie("cookies", options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
})
.AddSaml2("saml2", options =>
{
    options.SPOptions.EntityId = new EntityId(builder.Configuration["Saml:EntityId"] ?? "http://localhost:5000/saml");
    
    // CRITICAL: Disable certificate validation - must be set BEFORE IdP creation
    options.SPOptions.ValidateCertificates = false;
    
    // Set the sign-in scheme to use cookies
    options.SignInScheme = "cookies";
    
    var idpEntityId = builder.Configuration["Saml:IdpEntityId"];
    var idpMetadataUrl = builder.Configuration["Saml:IdpMetadataUrl"];
    var idpSsoUrl = builder.Configuration["Saml:IdpSingleSignOnUrl"];
    
    if (!string.IsNullOrEmpty(idpMetadataUrl))
    {
        var idp = new IdentityProvider(new EntityId(idpEntityId ?? ""), options.SPOptions)
        {
            MetadataLocation = idpMetadataUrl,
            LoadMetadata = true,
            DisableOutboundLogoutRequests = true,
            AllowUnsolicitedAuthnResponse = true
        };
        
        options.IdentityProviders.Add(idp);
    }
    else if (!string.IsNullOrEmpty(idpSsoUrl) && !string.IsNullOrEmpty(idpEntityId))
    {
        options.IdentityProviders.Add(new IdentityProvider(new EntityId(idpEntityId), options.SPOptions)
        {
            SingleSignOnServiceUrl = new Uri(idpSsoUrl),
            AllowUnsolicitedAuthnResponse = true
        });
    }
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add services
builder.Services.AddScoped<SamlAuthenticationHandler>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddScoped<AdminService>();

// Add controllers for SCIM API
builder.Services.AddControllers();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("Starting database initialization...");
    
    try
    {
        if (useAzureSql)
        {
            logger.LogInformation("Using Azure SQL Database - applying migrations...");
            
            // Check if any migrations are pending
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogInformation("Applying {Count} pending migrations...", pendingMigrations.Count());
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied successfully");
            }
            else
            {
                logger.LogInformation("No pending migrations");
            }
        }
        else
        {
            logger.LogInformation("Using SQLite - ensuring database created...");
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("SQLite database created successfully");
        }
        
        var usersCount = await db.Users.CountAsync();
        logger.LogInformation("Database initialized with {UserCount} users", usersCount);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database");
        throw;
    }
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// SCIM authentication middleware must come before other auth
app.UseMiddleware<ScimAuthenticationMiddleware>();

app.UseAuthentication();
app.UseMiddleware<UserProvisioningMiddleware>();
app.UseAuthorization();

app.UseAntiforgery();

// Map SAML endpoints
app.MapGet("/saml/login", async context =>
{
    await context.ChallengeAsync("saml2");
});

app.MapGet("/saml/logout", async context =>
{
    // Clear authentication cookies
    await context.SignOutAsync("cookies");
    
    // Clear any browser cookies related to authentication
    context.Response.Cookies.Delete(".AspNetCore.Cookies");
    context.Response.Cookies.Delete("Saml2.RequestId");
    
    // Redirect to login page
    context.Response.Redirect("/login");
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Map API controllers for SCIM
app.MapControllers();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
