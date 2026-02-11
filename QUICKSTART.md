# Quick Start Guide

## Run Locally (5 minutes)

### 0. Trust HTTPS Certificate (Required)

```powershell
# Trust the development certificate for HTTPS
dotnet dev-certs https --trust
```

### 1. Start the Application

```powershell
cd c:\repos\saml-scim-reference\src\SamlScimReference.Web
dotnet run
```

Application will start at: **https://localhost:7132** (HTTPS required for SAML)

### 2. Create a Test User via SCIM

```powershell
$headers = @{
    "Authorization" = "Bearer your-secret-scim-token-change-this"
    "Content-Type" = "application/json"
}

$body = @{
    schemas = @("urn:ietf:params:scim:schemas:core:2.0:User")
    userName = "testuser"
    emails = @(
        @{
            value = "testuser@example.com"
            primary = $true
        }
    )
    name = @{
        givenName = "Test"
        familyName = "User"
    }
    active = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users" -Method POST -Headers $headers -Body $body
```

### 3. Verify User Was Created

```powershell
$headers = @{
    "Authorization" = "Bearer your-secret-scim-token-change-this"
}

Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users" -Method GET -Headers $headers
```

### 4. Create an Admin User

```powershell
$headers = @{
    "Authorization" = "Bearer your-secret-scim-token-change-this"
    "Content-Type" = "application/json"
}

$body = @{
    schemas = @("urn:ietf:params:scim:schemas:core:2.0:User")
    userName = "admin"
    emails = @(
        @{
            value = "admin@example.com"
            primary = $true
        }
    )
    name = @{
        givenName = "Admin"
        familyName = "User"
    }
    active = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users" -Method POST -Headers $headers -Body $body
```

## Configure Azure AD (For SAML)

### 1. Create Enterprise Application

1. Go to Azure Portal → Microsoft Entra ID → Enterprise Applications
2. Click "New application" → "Create your own application"
3. Name: "SAML SCIM Reference"
4. Select "Integrate any other application you don't find in the gallery"

### 2. Configure SAML SSO

1. Click "Single sign-on" → Select "SAML"
2. Edit "Basic SAML Configuration":
   - **Identifier (Entity ID)**: `https://localhost:7132/saml`
   - **Reply URL**: `https://localhost:7132/Saml2/Acs`
   - **Sign-on URL**: `https://localhost:7132/`

3. Edit "Attributes & Claims":
   - Ensure email claim exists: `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress` → `user.mail`
   - Add role claim:
     - Name: `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`
     - Source: Single value
     - Value: `Admin` (for admin users)

4. Download "Federation Metadata XML"

### 3. Update appsettings.json

Get your Tenant ID from Azure AD overview page, then update:

```json
{
  "Saml": {
    "EntityId": "https://localhost:7132/saml",
    "ReturnUrl": "https://localhost:7132/",
    "IdpEntityId": "https://sts.windows.net/YOUR-TENANT-ID/",
    "IdpMetadataUrl": "https://login.microsoftonline.com/YOUR-TENANT-ID/federationmetadata/2007-06/federationmetadata.xml"
  }
}
```

### 4. Assign Users

1. Go to Enterprise Application → Users and groups
2. Add assignment → Select user
3. For admin users, ensure they have "Admin" role claim configured

### 5. Configure SCIM Provisioning

1. In Enterprise Application, click "Provisioning"
2. Set Provisioning Mode to "Automatic"
3. Admin Credentials:
   - **Tenant URL**: `https://localhost:7132/scim/v2`
   - **Secret Token**: `your-secret-scim-token-change-this`
4. Test Connection
5. Settings:
   - Scope: "Sync only assigned users and groups"
   - Provisioning Status: On
6. Save and start provisioning

## Test the Complete Flow

### 1. Check Provisioned Users (Admin Page)

Since you may not have SAML fully configured yet, you can test the admin page after creating users via SCIM:

Navigate to: https://localhost:7132/admin

Note: This will redirect to login if SAML isn't configured. Once SAML is working, users with Admin role can access this.

### 2. Test SAML Login

1. Navigate to https://localhost:7132
2. Click "Sign In with SAML"
3. You'll be redirected to Azure AD
4. Sign in with a user that:
   - Is assigned to the Enterprise Application
   - Has been provisioned via SCIM (or manually created via API)
5. You should be redirected back and see your profile

### 3. Test Access Denied

1. Try to log in with a user that exists in Azure AD
2. But has NOT been provisioned via SCIM
3. You should see the "Access Denied - User Not Provisioned" page

## Docker Quick Start

### Build and Run

```powershell
cd c:\repos\saml-scim-reference
docker-compose up --build
```

### Create Test User in Docker

```powershell
$headers = @{
    "Authorization" = "Bearer your-secret-scim-token-change-this"
    "Content-Type" = "application/json"
}

$body = @{
    schemas = @("urn:ietf:params:scim:schemas:core:2.0:User")
    userName = "dockertest"
    emails = @(@{value = "docker@example.com"; primary = $true})
    name = @{givenName = "Docker"; familyName = "Test"}
    active = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/scim/v2/Users" -Method POST -Headers $headers -Body $body
```

## Troubleshooting

### Database File Not Found

```powershell
# Check if data directory exists
Test-Path "c:\repos\saml-scim-reference\src\SamlScimReference.Web\data"

# Create it if needed
New-Item -ItemType Directory -Path "c:\repos\saml-scim-reference\src\SamlScimReference.Web\data" -Force
```

### SCIM 401 Unauthorized

Ensure the Bearer token matches exactly:
- In request: `Bearer your-secret-scim-token-change-this`
- In appsettings.json: `"BearerToken": "your-secret-scim-token-change-this"`

### SAML Redirect Loop

1. Check that Entity ID and Return URL match in:
   - appsettings.json
   - Azure AD Enterprise Application SAML configuration
2. Ensure Reply URL includes `/Saml2/Acs`

### User Not Found After SAML Login

1. Check that the user's email in Azure AD matches the email in SCIM database
2. View all users via SCIM API to verify:
   ```powershell
   $headers = @{"Authorization" = "Bearer your-secret-scim-token-change-this"}
   Invoke-RestMethod -Uri "http://localhost:5000/scim/v2/Users" -Headers $headers
   ```

## Next Steps

1. Configure SAML with your identity provider
2. Enable SCIM provisioning
3. Test complete authentication flow
4. Try the admin interface
5. Deploy to Azure using Bicep templates

## Useful Commands

### List All Users
```powershell
$headers = @{"Authorization" = "Bearer your-secret-scim-token-change-this"}
Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users" -Headers $headers
```

### Get Specific User
```powershell
$headers = @{"Authorization" = "Bearer your-secret-scim-token-change-this"}
$userId = "USER-ID-HERE"
Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users/$userId" -Headers $headers
```

### Delete User
```powershell
$headers = @{"Authorization" = "Bearer your-secret-scim-token-change-this"}
$userId = "USER-ID-HERE"
Invoke-RestMethod -Uri "https://localhost:7132/scim/v2/Users/$userId" -Method DELETE -Headers $headers
```

### View Database Directly
```powershell
# Install SQLite tools
# Then connect to database:
sqlite3 c:\repos\saml-scim-reference\src\SamlScimReference.Web\data\app.db

# View users:
SELECT * FROM Users;

# Exit:
.quit
```
