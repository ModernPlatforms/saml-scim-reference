# HTTPS Setup Guide

## Why HTTPS is Required

SAML authentication **requires HTTPS** because:
- SAML assertions contain sensitive authentication tokens
- Identity providers (Azure AD, Okta) require HTTPS callback URLs
- Browser security policies enforce HTTPS for authentication flows
- Production deployments must use HTTPS

## Local Development Setup

### 1. Trust the ASP.NET Core Development Certificate

```powershell
# Trust the HTTPS development certificate
dotnet dev-certs https --trust
```

This creates and trusts a self-signed certificate for local HTTPS development.

### 2. Run with HTTPS

```powershell
cd c:\repos\saml-scim-reference\src\SamlScimReference.Web
dotnet run --launch-profile https
```

The application will start at:
- **HTTPS**: https://localhost:7132 (use this for SAML)
- **HTTP**: http://localhost:5202 (redirects to HTTPS)

### 3. Update Azure AD Configuration

In your Azure AD Enterprise Application SAML settings:

**Basic SAML Configuration:**
- **Identifier (Entity ID)**: `https://localhost:7132/saml`
- **Reply URL**: `https://localhost:7132/Saml2/Acs`
- **Sign-on URL**: `https://localhost:7132/`

**SCIM Provisioning:**
- **Tenant URL**: `https://localhost:7132/scim/v2`
- **Secret Token**: `your-secret-scim-token-change-this`

### 4. Test SCIM with HTTPS

```powershell
$headers = @{
    "Authorization" = "Bearer your-secret-scim-token-change-this"
    "Content-Type" = "application/json"
}

$body = @{
    schemas = @("urn:ietf:params:scim:schemas:core:2.0:User")
    userName = "testuser"
    emails = @(@{value = "test@example.com"; primary = $true})
    name = @{givenName = "Test"; familyName = "User"}
    active = $true
} | ConvertTo-Json

# Note: Use HTTPS URL
Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users" -Method POST -Headers $headers -Body $body
```

## Docker with HTTPS (Optional)

For Docker with HTTPS, you need to mount certificates:

```powershell
# Export the dev certificate
dotnet dev-certs https -ep $env:USERPROFILE\.aspnet\https\aspnetapp.pfx -p YourSecurePassword

# Set environment variable
$env:ASPNETCORE_Kestrel__Certificates__Default__Password="YourSecurePassword"
$env:ASPNETCORE_Kestrel__Certificates__Default__Path="/https/aspnetapp.pfx"

# Run with volume mount
docker run -p 5001:443 -p 5000:80 `
  -e ASPNETCORE_URLS="https://+;http://+" `
  -e ASPNETCORE_HTTPS_PORT=5001 `
  -e ASPNETCORE_Kestrel__Certificates__Default__Password="YourSecurePassword" `
  -e ASPNETCORE_Kestrel__Certificates__Default__Path=/https/aspnetapp.pfx `
  -v $env:USERPROFILE\.aspnet\https:/https:ro `
  saml-scim-reference
```

**Simpler: Use HTTP in Docker (for testing only)**

```powershell
# For local Docker testing without HTTPS
docker-compose up --build

# Access at: http://localhost:5000
# Note: SAML won't work without HTTPS in production
```

## Azure Container Apps (Production)

Azure Container Apps automatically provides HTTPS:

- Container Apps ingress provides TLS termination
- Your app runs HTTP internally (port 8080)
- Public endpoint is HTTPS: `https://your-app.azurecontainerapps.io`

Update your deployed app's SAML configuration:
- **Entity ID**: `https://your-app.azurecontainerapps.io/saml`
- **Reply URL**: `https://your-app.azurecontainerapps.io/Saml2/Acs`
- **SCIM URL**: `https://your-app.azurecontainerapps.io/scim/v2`

## Troubleshooting

### "Unable to configure HTTPS endpoint"

```powershell
# Regenerate and trust certificate
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Certificate Trust Prompt

On Windows, you'll see a security warning when running `--trust`. Click **Yes** to trust the certificate.

### Browser Certificate Warning

First time accessing `https://localhost:7132`, you may see a certificate warning. This is normal for self-signed certificates:
- **Chrome/Edge**: Click "Advanced" → "Proceed to localhost (unsafe)"
- **Firefox**: Click "Advanced" → "Accept the Risk and Continue"

### SCIM 401 with HTTPS

Ensure you're using the correct URL scheme:
```powershell
# ✅ Correct
Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users" ...

# ❌ Wrong
Invoke-RestMethod -Uri "http://localhost:5202/scim/v2/Users" ...
```

### SAML Redirect Issues

If SAML redirects fail:
1. Verify Entity ID and Reply URL use HTTPS
2. Check Azure AD configuration matches exactly
3. Clear browser cookies and try again

## Quick Reference

### Development URLs (HTTPS)
- **Application**: https://localhost:7132
- **SAML Login**: https://localhost:7132/saml/login
- **SCIM API**: https://localhost:7132/scim/v2
- **Health Check**: https://localhost:7132/health

### Alternative HTTP Port (redirects to HTTPS)
- http://localhost:5202 → redirects to https://localhost:7132

### Run Commands

```powershell
# Standard HTTPS run
dotnet run

# Explicit HTTPS profile
dotnet run --launch-profile https

# HTTP only (for testing, SAML won't work)
dotnet run --launch-profile http --no-launch-profile
```
