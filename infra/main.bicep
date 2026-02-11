@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name prefix for all resources')
param appName string = 'saml-scim-ref'

@description('Environment name (dev, staging, prod)')
param environmentName string = 'dev'

@description('Container image name')
param containerImage string

@description('SCIM Bearer Token')
@secure()
param scimBearerToken string

@description('SAML Entity ID')
param samlEntityId string

@description('SAML Return URL')
param samlReturnUrl string

@description('SAML IdP Entity ID')
param samlIdpEntityId string

@description('SAML IdP Metadata URL')
param samlIdpMetadataUrl string

var resourceBaseName = '${appName}-${environmentName}'
var storageAccountName = replace('${resourceBaseName}sa', '-', '')
var containerRegistryName = replace('${resourceBaseName}acr', '-', '')
var containerAppEnvName = '${resourceBaseName}-env'
var containerAppName = '${resourceBaseName}-app'
var fileShareName = 'saml-scim-data'

// Storage Account for SQLite persistence
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: take(storageAccountName, 24)
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// File Share for SQLite database
resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  parent: fileService
  name: fileShareName
  properties: {
    shareQuota: 1
    enabledProtocols: 'SMB'
  }
}

// Container Registry
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: take(containerRegistryName, 50)
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${resourceBaseName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Container Apps Environment
resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppEnvName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Storage for Container App (Azure Files mount)
resource storageForContainerApp 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: containerAppEnvironment
  name: 'samlscimdata'
  properties: {
    azureFile: {
      accountName: storageAccount.name
      accountKey: storageAccount.listKeys().keys[0].value
      shareName: fileShareName
      accessMode: 'ReadWrite'
    }
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  properties: {
    environmentId: containerAppEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      secrets: [
        {
          name: 'scim-bearer-token'
          value: scimBearerToken
        }
        {
          name: 'registry-password'
          value: containerRegistry.listCredentials().passwords[0].value
        }
      ]
      registries: contains(containerImage, 'mcr.microsoft.com') ? [] : [
        {
          server: containerRegistry.properties.loginServer
          username: containerRegistry.listCredentials().username
          passwordSecretRef: 'registry-password'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'saml-scim-app'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Database__Path'
              value: '/app/data/app.db'
            }
            {
              name: 'Saml__EntityId'
              value: samlEntityId
            }
            {
              name: 'Saml__ReturnUrl'
              value: samlReturnUrl
            }
            {
              name: 'Saml__IdpEntityId'
              value: samlIdpEntityId
            }
            {
              name: 'Saml__IdpMetadataUrl'
              value: samlIdpMetadataUrl
            }
            {
              name: 'Scim__BearerToken'
              secretRef: 'scim-bearer-token'
            }
          ]
          volumeMounts: [
            {
              volumeName: 'data-volume'
              mountPath: '/app/data'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '100'
              }
            }
          }
        ]
      }
      volumes: [
        {
          name: 'data-volume'
          storageType: 'AzureFile'
          storageName: storageForContainerApp.name
        }
      ]
    }
  }
}

output containerAppFQDN string = containerApp.properties.configuration.ingress.fqdn
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output containerRegistryName string = containerRegistry.name
output storageAccountName string = storageAccount.name
output fileShareName string = fileShareName
