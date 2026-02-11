@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name prefix for all resources')
param appName string = 'saml-scim-ref'

@description('Environment name (dev, staging, prod)')
param environmentName string = 'dev'

@description('Container App Environment ID')
param containerAppEnvId string

@description('Container Registry Login Server')
param containerRegistryLoginServer string

@description('Container Registry Name')
param containerRegistryName string

@description('Storage Name for Container App')
param storageForContainerAppName string

@description('SQL Server FQDN')
param sqlServerFqdn string

@description('SQL Database Name')
param sqlDatabaseName string

@description('SQL Admin Login')
param sqlAdminLogin string

@description('SQL Admin Password')
@secure()
param sqlAdminPassword string

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

@description('Storage Account Name for Data Protection')
param storageAccountName string

@description('Storage Account Key for Data Protection')
@secure()
param storageAccountKey string

@description('Container Image Tag')
param containerImageTag string = 'latest'

var resourceBaseName = '${appName}-${environmentName}'
var containerAppName = '${resourceBaseName}-app'
var containerImage = '${containerRegistryLoginServer}/saml-scim-reference:${containerImageTag}'

// Reference existing Container Registry
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  properties: {
    environmentId: containerAppEnvId
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
        {
          name: 'sql-connection-string'
          value: 'Server=${sqlServerFqdn};Database=${sqlDatabaseName};User Id=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
        }
        {
          name: 'storage-connection-string'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storageAccountKey};EndpointSuffix=core.windows.net'
        }
      ]
      registries: [
        {
          server: containerRegistryLoginServer
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
              name: 'ConnectionStrings__DefaultConnection'
              secretRef: 'sql-connection-string'
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
            {
              name: 'AzureStorage__ConnectionString'
              secretRef: 'storage-connection-string'
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
      }
    }
  }
}

output containerAppFQDN string = containerApp.properties.configuration.ingress.fqdn
output containerAppName string = containerApp.name
