# Azure deployment script for SAML/SCIM Reference App

# Error handling
$ErrorActionPreference = "Stop"

# Variables
$RESOURCE_GROUP = "rg-saml-scim-reference"
$LOCATION = "australiaeast"

Write-Host "🚀 Deploying SAML/SCIM Reference Application to Azure..." -ForegroundColor Cyan
Write-Host ""

# Login to Azure (if not already logged in)
Write-Host "📝 Checking Azure login..." -ForegroundColor Yellow
try {
    az account show 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Not logged in. Initiating login..." -ForegroundColor Yellow
        az login
    }
} catch {
    Write-Host "Not logged in. Initiating login..." -ForegroundColor Yellow
    az login
}

# Create resource group
Write-Host "📦 Creating resource group..." -ForegroundColor Yellow
az group create --name $RESOURCE_GROUP --location $LOCATION
Write-Host ""

# ============================================================================
# PHASE 1: Deploy Infrastructure (ACR, Storage, Container Apps Environment)
# ============================================================================
Write-Host "🏗️  Phase 1: Deploying base infrastructure (ACR, Storage, Environment)..." -ForegroundColor Cyan
$infraOutput = az deployment group create `
    --resource-group $RESOURCE_GROUP `
    --template-file infra/main-infra.bicep `
    --parameters infra/main-infra.parameters.json `
    --query properties.outputs `
    --output json | ConvertFrom-Json

# Extract infrastructure outputs
$ACR_LOGIN_SERVER = $infraOutput.containerRegistryLoginServer.value
$ACR_NAME = $infraOutput.containerRegistryName.value
$CONTAINER_APP_ENV_ID = $infraOutput.containerAppEnvId.value
$STORAGE_FOR_APP_NAME = $infraOutput.storageForContainerAppName.value
$SQL_SERVER_FQDN = $infraOutput.sqlServerFqdn.value
$SQL_DATABASE_NAME = $infraOutput.sqlDatabaseName.value
$STORAGE_ACCOUNT_NAME = $infraOutput.storageAccountName.value

# Get storage account key
$STORAGE_ACCOUNT_KEY = (az storage account keys list `
    --resource-group $RESOURCE_GROUP `
    --account-name $STORAGE_ACCOUNT_NAME `
    --query "[0].value" `
    --output tsv)

Write-Host "✅ Base infrastructure deployed!" -ForegroundColor Green
Write-Host "   Container Registry: $ACR_LOGIN_SERVER" -ForegroundColor Gray
Write-Host ""

# ============================================================================
# PHASE 2: Build and Push Docker Image
# ============================================================================
Write-Host "🐳 Phase 2: Building and pushing Docker image..." -ForegroundColor Cyan

# Generate unique image tag
$IMAGE_TAG = Get-Date -Format "yyyyMMdd-HHmmss"
$IMAGE_NAME = "${ACR_LOGIN_SERVER}/saml-scim-reference:${IMAGE_TAG}"

Write-Host "   Image tag: $IMAGE_TAG" -ForegroundColor Gray

# Login to ACR
Write-Host "   Logging into ACR..." -ForegroundColor Gray
az acr login --name $ACR_NAME

# Build image
Write-Host "   Building Docker image..." -ForegroundColor Gray
docker build -t $IMAGE_NAME -t ${ACR_LOGIN_SERVER}/saml-scim-reference:latest .

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Docker build failed!" -ForegroundColor Red
    exit 1
}

# Push image
Write-Host "   Pushing image to ACR..." -ForegroundColor Gray
docker push $IMAGE_NAME
docker push ${ACR_LOGIN_SERVER}/saml-scim-reference:latest

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Docker push failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Docker image built and pushed successfully!" -ForegroundColor Green
Write-Host ""

# ============================================================================
# PHASE 3: Deploy Container App
# ============================================================================
Write-Host "🚀 Phase 3: Deploying Container App..." -ForegroundColor Cyan

$appOutput = az deployment group create `
    --resource-group $RESOURCE_GROUP `
    --template-file infra/main-app.bicep `
    --parameters infra/main-app.parameters.json `
    --parameters containerAppEnvId=$CONTAINER_APP_ENV_ID `
    --parameters containerRegistryLoginServer=$ACR_LOGIN_SERVER `
    --parameters containerRegistryName=$ACR_NAME `
    --parameters storageForContainerAppName=$STORAGE_FOR_APP_NAME `
    --parameters sqlServerFqdn=$SQL_SERVER_FQDN `
    --parameters sqlDatabaseName=$SQL_DATABASE_NAME `
    --parameters storageAccountName=$STORAGE_ACCOUNT_NAME `
    --parameters storageAccountKey=$STORAGE_ACCOUNT_KEY `
    --parameters containerImageTag=$IMAGE_TAG `
    --query properties.outputs `
    --output json | ConvertFrom-Json

$APP_FQDN = $appOutput.containerAppFQDN.value

Write-Host ""
Write-Host "✅ Deployment Complete!" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "📋 Deployment Information:" -ForegroundColor Cyan
Write-Host "   Resource Group:      $RESOURCE_GROUP"
Write-Host "   Container Registry:  $ACR_LOGIN_SERVER"
Write-Host "   SQL Server:          $SQL_SERVER_FQDN"
Write-Host "   SQL Database:        $SQL_DATABASE_NAME"
Write-Host "   App URL:             https://$APP_FQDN" -ForegroundColor Green
Write-Host ""
Write-Host "🔐 Next Steps - Configure Identity Provider:" -ForegroundColor Yellow
Write-Host ""
Write-Host "   SAML Entity ID:  https://$APP_FQDN/saml"
Write-Host "   SAML Reply URL:  https://$APP_FQDN/Saml2/Acs"
Write-Host "   SCIM Endpoint:   https://$APP_FQDN/scim/v2"
Write-Host ""
Write-Host "Visit your app: " -NoNewline
Write-Host "https://$APP_FQDN" -ForegroundColor Green
Write-Host ""
