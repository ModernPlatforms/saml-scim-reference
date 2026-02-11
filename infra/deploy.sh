#!/bin/bash

# Azure deployment script for SAML/SCIM Reference App

set -e

# Variables
RESOURCE_GROUP="rg-saml-scim-reference"
LOCATION="eastus"
APP_NAME="saml-scim-ref"
ENVIRONMENT="dev"

echo "🚀 Deploying SAML/SCIM Reference Application to Azure..."

# Login to Azure (if not already logged in)
echo "📝 Checking Azure login..."
az account show > /dev/null 2>&1 || az login

# Create resource group
echo "📦 Creating resource group..."
az group create --name $RESOURCE_GROUP --location $LOCATION

# Deploy infrastructure
echo "🏗️  Deploying infrastructure with Bicep..."
DEPLOYMENT_OUTPUT=$(az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json \
  --query properties.outputs \
  --output json)

# Extract outputs
ACR_LOGIN_SERVER=$(echo $DEPLOYMENT_OUTPUT | jq -r '.containerRegistryLoginServer.value')
ACR_NAME=$(echo $DEPLOYMENT_OUTPUT | jq -r '.containerRegistryName.value')
APP_FQDN=$(echo $DEPLOYMENT_OUTPUT | jq -r '.containerAppFQDN.value')

echo "✅ Infrastructure deployed successfully!"
echo ""
echo "📋 Deployment Information:"
echo "   Resource Group: $RESOURCE_GROUP"
echo "   Container Registry: $ACR_LOGIN_SERVER"
echo "   App URL: https://$APP_FQDN"
echo ""
echo "🐳 Next steps:"
echo "   1. Build and push container image:"
echo "      az acr login --name $ACR_NAME"
echo "      docker build -t $ACR_LOGIN_SERVER/saml-scim-reference:latest ."
echo "      docker push $ACR_LOGIN_SERVER/saml-scim-reference:latest"
echo ""
echo "   2. Update Container App to use the new image"
echo "   3. Configure your SAML IdP with Entity ID and Return URL"
echo "   4. Configure SCIM provisioning with endpoint: https://$APP_FQDN/scim/v2"
echo ""
