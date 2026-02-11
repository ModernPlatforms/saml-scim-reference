# SAML/SCIM Reference Application

A C# Blazor Server application demonstrating SAML authentication and SCIM 2.0 provisioning, built with vertical slice architecture. Users must be provisioned via SCIM before they can authenticate with SAML.

## Features

- **SAML Authentication**: Single Sign-On via SAML 2.0 (Sustainsys.Saml2)
- **SCIM 2.0 Provisioning**: Full user and group provisioning endpoints
- **SCIM as Source of Truth**: Only SCIM-provisioned users can log in
- **User Profile Page**: Displays user details from SCIM and current SAML claims
- **Admin Interface**: View all provisioned users (requires Admin role in SAML)
- **Vertical Slice Architecture**: Each feature is self-contained
- **SQLite Database**: Lightweight persistence (works locally and in Azure Container Apps)
- **Containerized**: Ready for Docker and Azure Container Apps

## Architecture

```
Features/
├── Auth/           - SAML authentication, login, access denied
├── Users/          - User profile display
├── Admin/          - Admin interface for viewing all users
└── Scim/           - SCIM 2.0 API endpoints (Users, Groups)

Data/
├── User.cs         - User entity
├── Group.cs        - Group entity
└── AppDbContext.cs - EF Core context
```

## Prerequisites

- .NET 9.0 SDK
- Docker (optional, for containerization)
- Azure CLI (optional, for Azure deployment)
- SAML Identity Provider (Azure AD, Okta, etc.)

## Local Development

### 1. Clone and Build

```bash
cd c:\repos\saml-scim-reference
dotnet restore
dotnet build
```

### 2. Configure SAML

Edit `src/SamlScimReference.Web/appsettings.json`:

```json
{
  "Saml": {
    "EntityId": "http://localhost:5000/saml",
    "ReturnUrl": "http://localhost:5000/",
    "IdpEntityId": "https://sts.windows.net/YOUR-TENANT-ID/",
    "IdpMetadataUrl": "https://login.microsoftonline.com/YOUR-TENANT-ID/federationmetadata/2007-06/federationmetadata.xml"
  },
  "Scim": {
    "BearerToken": "your-secret-scim-token-change-this"
  }
}
```

For Azure AD:
- Replace `YOUR-TENANT-ID` with your Azure AD tenant ID
- Configure Enterprise Application with SAML SSO
- Set Reply URL to `http://localhost:5000/Saml2/Acs`
- Add role claim "Admin" for admin users

### 3. Run Locally

```bash
cd src/SamlScimReference.Web
dotnet run
```

Navigate to: http://localhost:5000

### 4. Provision a Test User via SCIM

Before you can log in, create a user via SCIM:

```bash
curl -X POST http://localhost:5000/scim/v2/Users \
  -H "Authorization: Bearer your-secret-scim-token-change-this" \
  -H "Content-Type: application/json" \
  -d '{
    "schemas": ["urn:ietf:params:scim:schemas:core:2.0:User"],
    "userName": "testuser",
    "emails": [{"value": "testuser@example.com", "primary": true}],
    "name": {"givenName": "Test", "familyName": "User"},
    "active": true
  }'
```

Now you can log in with SAML using `testuser@example.com`.

## Admin Access

Admin access is controlled by SAML role claims, not the database. To grant a user admin access:

### For Azure AD (Production)

1. Go to **Azure Portal** > **Enterprise Applications** > Your App
2. Navigate to **Single sign-on** > **Attributes & Claims**
3. Add a new claim:
   - **Name**: `role`
   - **Source**: Select appropriate source (e.g., user.assignedroles)
   - **Value**: `Admin`
4. Assign the role to specific users/groups

### For Local Development

1. **Option 1**: Configure local SAML IdP to send Admin role claim

2. **Option 2**: Bypass authentication (development only)
   ```bash
   $env:BYPASS_AUTH="true"
   dotnet run
   ```

3. **Check user provisioning status**:
   ```bash
   .\scripts\grant-admin.ps1 -Email "user@example.com"
   ```

**Note**: Users must be provisioned via SCIM before they can log in, regardless of admin status.

## Docker

### Build and Run with Docker Compose

```bash
docker-compose up --build
```

Access at: http://localhost:5000

### Build Docker Image Manually

```bash
docker build -t saml-scim-reference .
docker run -p 5000:8080 \
  -e Saml__IdpEntityId="YOUR_IDP_ENTITY_ID" \
  -e Saml__IdpMetadataUrl="YOUR_METADATA_URL" \
  -e Scim__BearerToken="YOUR_SECRET_TOKEN" \
  -v saml-data:/app/data \
  saml-scim-reference
```

## Azure Deployment

### 1. Update Bicep Parameters

Edit `infra/main.parameters.json`:

```json
{
  "containerImage": {"value": "YOUR_ACR.azurecr.io/saml-scim-reference:latest"},
  "samlEntityId": {"value": "https://YOUR_APP.azurecontainerapps.io/saml"},
  "samlReturnUrl": {"value": "https://YOUR_APP.azurecontainerapps.io/"},
  "samlIdpEntityId": {"value": "https://sts.windows.net/YOUR-TENANT-ID/"},
  "samlIdpMetadataUrl": {"value": "https://login.microsoftonline.com/YOUR-TENANT-ID/federationmetadata/2007-06/federationmetadata.xml"},
  "scimBearerToken": {"value": "your-secret-scim-token"}
}
```

### 2. Deploy Infrastructure

```bash
# Login to Azure
az login

# Create resource group
az group create --name rg-saml-scim-reference --location eastus

# Deploy Bicep
az deployment group create \
  --resource-group rg-saml-scim-reference \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json
```

### 3. Build and Push Container

```bash
# Get ACR name from deployment output
ACR_NAME=$(az deployment group show \
  --resource-group rg-saml-scim-reference \
  --name main \
  --query properties.outputs.containerRegistryName.value -o tsv)

# Login to ACR
az acr login --name $ACR_NAME

# Build and push
docker build -t $ACR_NAME.azurecr.io/saml-scim-reference:latest .
docker push $ACR_NAME.azurecr.io/saml-scim-reference:latest
```

### 4. Update Container App

The Container App will automatically pull the new image or trigger a revision update.

## SCIM API Endpoints

### Users

- `GET /scim/v2/Users` - List all users
- `GET /scim/v2/Users/{id}` - Get user by ID
- `POST /scim/v2/Users` - Create user
- `PUT /scim/v2/Users/{id}` - Update user
- `DELETE /scim/v2/Users/{id}` - Delete user

### Groups

- `GET /scim/v2/Groups` - List all groups
- `GET /scim/v2/Groups/{id}` - Get group by ID
- `POST /scim/v2/Groups` - Create group
- `PUT /scim/v2/Groups/{id}` - Update group
- `DELETE /scim/v2/Groups/{id}` - Delete group

### Authentication

All SCIM endpoints require Bearer token authentication:

```
Authorization: Bearer your-secret-scim-token-change-this
```

## Configure Identity Provider

### Azure AD

1. Create Enterprise Application
2. Configure SAML SSO:
   - Entity ID: `https://YOUR_APP.azurecontainerapps.io/saml`
   - Reply URL: `https://YOUR_APP.azurecontainerapps.io/Saml2/Acs`
   - Sign-on URL: `https://YOUR_APP.azurecontainerapps.io/`
3. Add Claims:
   - Email: `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress`
   - Role: `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` (value: "Admin" for admins)
4. Enable SCIM Provisioning:
   - Tenant URL: `https://YOUR_APP.azurecontainerapps.io/scim/v2`
   - Secret Token: Your SCIM bearer token
   - Attribute Mappings: userName=userPrincipalName, emails[type eq "work"].value=mail

## Application Flow

1. **User Provisioning**: Admin configures SCIM provisioning in IdP → Users/Groups synced to app database
2. **User Login**: User clicks "Sign In with SAML" → Redirected to IdP → Authenticates → Returns to app
3. **Validation**: App checks if user's email exists in SCIM-provisioned users → If not found, shows access denied
4. **Profile Page**: Shows user details from database + current SAML claims
5. **Admin Access**: Users with "Admin" role claim can access `/admin` to view all users

## Project Structure

This project uses **Vertical Slice Architecture**:

- Each feature (Auth, Users, Admin, Scim) is self-contained
- All code for a feature lives in its folder (Razor pages, services, models)
- Shared data models live in `Data/`
- Configuration wired up in `Program.cs`

## Troubleshooting

### "User not provisioned" error

- Ensure the user exists in the database via SCIM
- Check that email claim from SAML matches user's email in database
- Use `/admin` page (as admin) to verify user exists

### SCIM endpoint returns 401

- Verify Bearer token in request matches `Scim:BearerToken` in configuration
- Check Authorization header format: `Bearer <token>`

### Database file not found in Azure

- Ensure Azure Files volume is mounted to `/app/data`
- Check Container App logs for permission issues
- Verify `Database__Path` environment variable is `/app/data/app.db`

## License

MIT
