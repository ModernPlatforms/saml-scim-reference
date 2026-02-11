@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Name prefix for all resources')
param appName string = 'saml-scim-ref'

@description('Environment name (dev, stg, prod)')
param environmentName string = 'stg'

@description('SQL Server Administrator Login')
param sqlAdminLogin string = 'sqladmin'

@description('SQL Server Administrator Password')
@secure()
param sqlAdminPassword string

var resourceBaseName = '${appName}-${environmentName}'
var storageAccountName = replace('${resourceBaseName}sa', '-', '')
var containerRegistryName = replace('${resourceBaseName}acr', '-', '')
var containerAppEnvName = '${resourceBaseName}-env'
var sqlServerName = '${resourceBaseName}-sql'
var sqlDatabaseName = 'samlscimdb'
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

// Blob Service and Container for Data Protection Keys
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'dataprotection-keys'
  properties: {
    publicAccess: 'None'
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

// SQL Server (Serverless)
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// SQL Database (Serverless tier)
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648 // 2GB
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    readScale: 'Disabled'
    autoPauseDelay: 60 // Auto-pause after 60 minutes of inactivity
    minCapacity: json('0.5') // Minimum vCores when active
  }
}

// SQL Firewall Rule - Allow Azure Services
resource sqlFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output containerRegistryName string = containerRegistry.name
output storageAccountName string = storageAccount.name
output fileShareName string = fileShareName
output containerAppEnvName string = containerAppEnvironment.name
output containerAppEnvId string = containerAppEnvironment.id
output storageForContainerAppName string = storageForContainerApp.name
output sqlServerName string = sqlServer.name
output sqlDatabaseName string = sqlDatabase.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
