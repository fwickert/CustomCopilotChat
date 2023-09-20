/*
Copyright (c) Microsoft. All rights reserved.
Licensed under the MIT license. See LICENSE file in the project root for full license information.

Bicep template for deploying CopilotChat Azure resources.
*/

@description('Name for the deployment consisting of alphanumeric characters or dashes (\'-\')')
param name string = 'copichat'

@description('SKU for the Azure App Service plan')
@allowed([ 'B1', 'S1', 'S2', 'S3', 'P1V3', 'P2V3', 'I1V2', 'I2V2' ])
param webAppServiceSku string = 'B1'

@description('Location of package to deploy as the web service')
#disable-next-line no-hardcoded-env-urls
param packageUri string = 'https://aka.ms/copilotchat/webapi/latest'

@description('Underlying AI service')
@allowed([
  'AzureOpenAI'
  'OpenAI'
])
param aiService string = 'AzureOpenAI'

@description('Model to use for chat completions')
param completionModel string = 'gpt-35-turbo'

@description('Model to use for text embeddings')
param embeddingModel string = 'text-embedding-ada-002'

@description('Completion model the task planner should use')
param plannerModel string = 'gpt-35-turbo'

@description('Azure OpenAI endpoint to use (Azure OpenAI only)')
param aiEndpoint string = ''

@secure()
@description('Azure OpenAI or OpenAI API key')
param aiApiKey string = ''

@description('Azure AD client ID for the backend web API')
param webApiClientId string = ''

@description('Azure AD tenant ID for authenticating users')
param azureAdTenantId string = ''

@description('Azure AD cloud instance for authenticating users')
param azureAdInstance string = environment().authentication.loginEndpoint

@description('Whether to deploy a new Azure OpenAI instance')
param deployNewAzureOpenAI bool = false

@description('Whether to deploy Cosmos DB for persistent chat storage')
param deployCosmosDB bool = true

@description('What method to use to persist embeddings')
@allowed([
  'Volatile'
  'AzureCognitiveSearch'
  'Qdrant'
  'Postgres'
])
param memoryStore string = 'Volatile'

@description('Whether to deploy Azure Speech Services to enable input by voice')
param deploySpeechServices bool = true

@description('Whether to deploy the backend Web API package')
param deployWebApiPackage bool = true

@description('Region for the resources')
param location string = resourceGroup().location

@description('Region for the webapp frontend')
param webappLocation string = 'westus2'

@description('Hash of the resource group ID')
var rgIdHash = uniqueString(resourceGroup().id)

@description('Deployment name unique to resource group')
var uniqueName = '${name}-${rgIdHash}'

@description('Name of the Azure Storage file share to create')
var storageFileShareName = 'aciqdrantshare'

@description('PostgreSQL admin password')
@secure()
param sqlAdminPassword string = newGuid()

resource openAI 'Microsoft.CognitiveServices/accounts@2022-12-01' = if (deployNewAzureOpenAI) {
  name: 'ai-${uniqueName}'
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: toLower(uniqueName)
  }
}

resource openAI_completionModel 'Microsoft.CognitiveServices/accounts/deployments@2022-12-01' = if (deployNewAzureOpenAI) {
  parent: openAI
  name: completionModel
  properties: {
    model: {
      format: 'OpenAI'
      name: completionModel
    }
    scaleSettings: {
      scaleType: 'Standard'
    }
  }
}

resource openAI_embeddingModel 'Microsoft.CognitiveServices/accounts/deployments@2022-12-01' = if (deployNewAzureOpenAI) {
  parent: openAI
  name: embeddingModel
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingModel
    }
    scaleSettings: {
      scaleType: 'Standard'
    }
  }
  dependsOn: [// This "dependency" is to create models sequentially because the resource
    openAI_completionModel // provider does not support parallel creation of models properly.
  ]
}

resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: 'asp-${uniqueName}-webapi'
  location: location
  kind: 'app'
  sku: {
    name: webAppServiceSku
  }
}

resource appServiceWeb 'Microsoft.Web/sites@2022-09-01' = {
  name: 'app-${uniqueName}-webapi'
  location: location
  kind: 'app'
  tags: {
    skweb: '1'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    virtualNetworkSubnetId: virtualNetwork.properties.subnets[0].id
    siteConfig: {
      healthCheckPath: '/healthz'
    }
  }
}

resource appServiceWebConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: appServiceWeb
  name: 'web'
  properties: {
    alwaysOn: false
    cors: {
      allowedOrigins: [
        'http://localhost:3000'
        'https://localhost:3000'
      ]
      supportCredentials: true
    }
    detailedErrorLoggingEnabled: true
    minTlsVersion: '1.2'
    netFrameworkVersion: 'v6.0'
    use32BitWorkerProcess: false
    vnetRouteAllEnabled: true
    webSocketsEnabled: true
    appSettings: [
      {
        name: 'AIService:Type'
        value: aiService
      }
      {
        name: 'AIService:Endpoint'
        value: deployNewAzureOpenAI ? openAI.properties.endpoint : aiEndpoint
      }
      {
        name: 'AIService:Key'
        value: deployNewAzureOpenAI ? openAI.listKeys().key1 : aiApiKey
      }
      {
        name: 'AIService:Models:Completion'
        value: completionModel
      }
      {
        name: 'AIService:Models:Embedding'
        value: embeddingModel
      }
      {
        name: 'AIService:Models:Planner'
        value: plannerModel
      }
      {
        name: 'Authentication:Type'
        value: 'AzureAd'
      }
      {
        name: 'Authentication:AzureAd:Instance'
        value: azureAdInstance
      }
      {
        name: 'Authentication:AzureAd:TenantId'
        value: azureAdTenantId
      }
      {
        name: 'Authentication:AzureAd:ClientId'
        value: webApiClientId
      }
      {
        name: 'Authentication:AzureAd:Scopes'
        value: 'access_as_user'
      }
      {
        name: 'ChatStore:Type'
        value: deployCosmosDB ? 'cosmos' : 'volatile'
      }
      {
        name: 'ChatStore:Cosmos:Database'
        value: 'CopilotChat'
      }
      {
        name: 'ChatStore:Cosmos:ChatSessionsContainer'
        value: 'chatsessions'
      }
      {
        name: 'ChatStore:Cosmos:ChatMessagesContainer'
        value: 'chatmessages'
      }
      {
        name: 'ChatStore:Cosmos:ChatMemorySourcesContainer'
        value: 'chatmemorysources'
      }
      {
        name: 'ChatStore:Cosmos:ChatParticipantsContainer'
        value: 'chatparticipants'
      }
      {
        name: 'ChatStore:Cosmos:ConnectionString'
        value: deployCosmosDB ? cosmosAccount.listConnectionStrings().connectionStrings[0].connectionString : ''
      }
      {
        name: 'MemoryStore:Type'
        value: memoryStore
      }
      {
        name: 'MemoryStore:Qdrant:Host'
        value: memoryStore == 'Qdrant' ? 'https://${appServiceQdrant.properties.defaultHostName}' : ''
      }
      {
        name: 'MemoryStore:Qdrant:Port'
        value: '443'
      }
      {
        name: 'MemoryStore:AzureCognitiveSearch:UseVectorSearch'
        value: 'true'
      }
      {
        name: 'MemoryStore:AzureCognitiveSearch:Endpoint'
        value: memoryStore == 'AzureCognitiveSearch' ? 'https://${azureCognitiveSearch.name}.search.windows.net' : ''
      }
      {
        name: 'MemoryStore:AzureCognitiveSearch:Key'
        value: memoryStore == 'AzureCognitiveSearch' ? azureCognitiveSearch.listAdminKeys().primaryKey : ''
      }
      {
        name: 'MemoryStore:Postgres:ConnectionString'
        value: memoryStore == 'Postgres' ? 'Host=${postgreServerGroup.properties.serverNames[0].fullyQualifiedDomainName}:5432;Username=citus;Password=${sqlAdminPassword};Database=citus' : ''
      }
      {
        name: 'AzureSpeech:Region'
        value: location
      }
      {
        name: 'AzureSpeech:Key'
        value: deploySpeechServices ? speechAccount.listKeys().key1 : ''
      }
      {
        name: 'AllowedOrigins'
        value: '[*]' // Defer list of allowed origins to the Azure service app's CORS configuration
      }
      {
        name: 'Kestrel:Endpoints:Https:Url'
        value: 'https://localhost:443'
      }
      {
        name: 'Logging:LogLevel:Default'
        value: 'Warning'
      }
      {
        name: 'Logging:LogLevel:CopilotChat.WebApi'
        value: 'Warning'
      }
      {
        name: 'Logging:LogLevel:Microsoft.SemanticKernel'
        value: 'Warning'
      }
      {
        name: 'Logging:LogLevel:Microsoft.AspNetCore.Hosting'
        value: 'Warning'
      }
      {
        name: 'Logging:LogLevel:Microsoft.Hosting.Lifetimel'
        value: 'Warning'
      }
      {
        name: 'ApplicationInsights:ConnectionString'
        value: appInsights.properties.ConnectionString
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights.properties.ConnectionString
      }
      {
        name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
        value: '~2'
      }
    ]
  }
}

resource appServiceWebDeploy 'Microsoft.Web/sites/extensions@2022-09-01' = if (deployWebApiPackage) {
  name: 'MSDeploy'
  kind: 'string'
  parent: appServiceWeb
  properties: {
    packageUri: packageUri
  }
  dependsOn: [
    appServiceWebConfig
  ]
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appins-${uniqueName}'
  location: location
  kind: 'string'
  tags: {
    displayName: 'AppInsight'
  }
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

resource appInsightExtension 'Microsoft.Web/sites/siteextensions@2022-09-01' = {
  parent: appServiceWeb
  name: 'Microsoft.ApplicationInsights.AzureWebSites'
  dependsOn: [ appServiceWebConfig ]
}

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'la-${uniqueName}'
  location: location
  tags: {
    displayName: 'Log Analytics'
  }
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 90
    features: {
      searchVersion: 1
      legacy: 0
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2022-09-01' = if (memoryStore == 'Qdrant') {
  name: 'st${rgIdHash}' // Not using full unique name to avoid hitting 24 char limit
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
  resource fileservices 'fileServices' = {
    name: 'default'
    resource share 'shares' = {
      name: storageFileShareName
    }
  }
}

resource appServicePlanQdrant 'Microsoft.Web/serverfarms@2022-03-01' = if (memoryStore == 'Qdrant') {
  name: 'asp-${uniqueName}-qdrant'
  location: location
  kind: 'linux'
  sku: {
    name: 'P1v3'
  }
  properties: {
    reserved: true
  }
}

resource appServiceQdrant 'Microsoft.Web/sites@2022-09-01' = if (memoryStore == 'Qdrant') {
  name: 'app-${uniqueName}-qdrant'
  location: location
  kind: 'app,linux,container'
  properties: {
    serverFarmId: appServicePlanQdrant.id
    httpsOnly: true
    reserved: true
    clientCertMode: 'Required'
    virtualNetworkSubnetId: virtualNetwork.properties.subnets[1].id
    siteConfig: {
      numberOfWorkers: 1
      linuxFxVersion: 'DOCKER|qdrant/qdrant:latest'
      alwaysOn: true
      vnetRouteAllEnabled: true
      ipSecurityRestrictions: [
        {
          vnetSubnetResourceId: virtualNetwork.properties.subnets[0].id
          action: 'Allow'
          priority: 300
          name: 'Allow front vnet'
        }
        {
          ipAddress: 'Any'
          action: 'Deny'
          priority: 2147483647
          name: 'Deny all'
        }
      ]
      azureStorageAccounts: {
        aciqdrantshare: {
          type: 'AzureFiles'
          accountName: memoryStore == 'Qdrant' ? storage.name : 'notdeployed'
          shareName: storageFileShareName
          mountPath: '/qdrant/storage'
          accessKey: memoryStore == 'Qdrant' ? storage.listKeys().keys[0].value : ''
        }
      }
    }
  }
}

resource azureCognitiveSearch 'Microsoft.Search/searchServices@2022-09-01' = if (memoryStore == 'AzureCognitiveSearch') {
  name: 'acs-${uniqueName}'
  location: location
  sku: {
    name: 'basic'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
  }
}

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2021-05-01' = {
  name: 'vnet-${uniqueName}'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'webSubnet'
        properties: {
          addressPrefix: '10.0.1.0/24'
          networkSecurityGroup: {
            id: webNsg.id
          }
          serviceEndpoints: [
            {
              service: 'Microsoft.Web'
              locations: [
                '*'
              ]
            }
          ]
          delegations: [
            {
              name: 'delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverfarms'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
      {
        name: 'qdrantSubnet'
        properties: {
          addressPrefix: '10.0.2.0/24'
          networkSecurityGroup: {
            id: qdrantNsg.id
          }
          serviceEndpoints: [
            {
              service: 'Microsoft.Web'
              locations: [
                '*'
              ]
            }
          ]
          delegations: [
            {
              name: 'delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverfarms'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
      {
        name: 'postgresSubnet'
        properties: {
          addressPrefix: '10.0.3.0/24'
          serviceEndpoints: []
          delegations: []
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

resource webNsg 'Microsoft.Network/networkSecurityGroups@2022-11-01' = {
  name: 'nsg-${uniqueName}-webapi'
  location: location
  properties: {
    securityRules: [
      {
        name: 'AllowAnyHTTPSInbound'
        properties: {
          protocol: 'TCP'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
          access: 'Allow'
          priority: 100
          direction: 'Inbound'
        }
      }
    ]
  }
}

resource qdrantNsg 'Microsoft.Network/networkSecurityGroups@2022-11-01' = {
  name: 'nsg-${uniqueName}-qdrant'
  location: location
  properties: {
    securityRules: []
  }
}

resource webSubnetConnection 'Microsoft.Web/sites/virtualNetworkConnections@2022-09-01' = {
  parent: appServiceWeb
  name: 'webSubnetConnection'
  properties: {
    vnetResourceId: virtualNetwork.properties.subnets[0].id
    isSwift: true
  }
}

resource qdrantSubnetConnection 'Microsoft.Web/sites/virtualNetworkConnections@2022-09-01' = if (memoryStore == 'Qdrant') {
  parent: appServiceQdrant
  name: 'qdrantSubnetConnection'
  properties: {
    vnetResourceId: virtualNetwork.properties.subnets[1].id
    isSwift: true
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = if (deployCosmosDB) {
  name: toLower('cosmos-${uniqueName}')
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [ {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    databaseAccountOfferType: 'Standard'
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = if (deployCosmosDB) {
  parent: cosmosAccount
  name: 'CopilotChat'
  properties: {
    resource: {
      id: 'CopilotChat'
    }
  }
}

resource messageContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = if (deployCosmosDB) {
  parent: cosmosDatabase
  name: 'chatmessages'
  properties: {
    resource: {
      id: 'chatmessages'
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      partitionKey: {
        paths: [
          '/chatId'
        ]
        kind: 'Hash'
        version: 2
      }
    }
  }
}

resource sessionContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = if (deployCosmosDB) {
  parent: cosmosDatabase
  name: 'chatsessions'
  properties: {
    resource: {
      id: 'chatsessions'
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
        version: 2
      }
    }
  }
}

resource participantContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = if (deployCosmosDB) {
  parent: cosmosDatabase
  name: 'chatparticipants'
  properties: {
    resource: {
      id: 'chatparticipants'
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      partitionKey: {
        paths: [
          '/userId'
        ]
        kind: 'Hash'
        version: 2
      }
    }
  }
}

resource memorySourcesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = if (deployCosmosDB) {
  parent: cosmosDatabase
  name: 'chatmemorysources'
  properties: {
    resource: {
      id: 'chatmemorysources'
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      partitionKey: {
        paths: [
          '/chatId'
        ]
        kind: 'Hash'
        version: 2
      }
    }
  }
}

resource postgreServerGroup 'Microsoft.DBforPostgreSQL/serverGroupsv2@2022-11-08' = if (memoryStore == 'Postgres') {
  name: 'pg-${uniqueName}'
  location: location
  properties: {
    postgresqlVersion: '15'
    administratorLoginPassword: sqlAdminPassword
    enableHa: false
    coordinatorVCores: 1
    coordinatorServerEdition: 'BurstableMemoryOptimized'
    coordinatorStorageQuotaInMb: 32768
    nodeVCores: 4
    nodeCount: 0
    nodeStorageQuotaInMb: 524288
    nodeEnablePublicIpAccess: false
  }
}

resource postgresDNSZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (memoryStore == 'Postgres') {
  name: 'privatelink.postgres.cosmos.azure.com'
  location: 'global'
}

resource postgresPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-04-01' = if (memoryStore == 'Postgres') {
  name: 'pg-${uniqueName}-pe'
  location: location
  properties: {
    subnet: {
      id: virtualNetwork.properties.subnets[2].id
    }
    privateLinkServiceConnections: [
      {
        name: 'postgres'
        properties: {
          privateLinkServiceId: postgreServerGroup.id
          groupIds: [
            'coordinator'
          ]
        }
      }
    ]
  }
}

resource postgresVirtualNetworkLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (memoryStore == 'Postgres') {
  parent: postgresDNSZone
  name: 'pg-${uniqueName}-vnl'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: virtualNetwork.id
    }
    registrationEnabled: true
  }
}

resource postgresPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-04-01' = if (memoryStore == 'Postgres') {
  #disable-next-line use-parent-property
  name: '${postgresPrivateEndpoint.name}/default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'postgres'
        properties: {
          privateDnsZoneId: postgresDNSZone.id
        }
      }
    ]
  }
}

resource speechAccount 'Microsoft.CognitiveServices/accounts@2022-12-01' = if (deploySpeechServices) {
  name: 'cog-${uniqueName}'
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'SpeechServices'
  identity: {
    type: 'None'
  }
  properties: {
    customSubDomainName: 'cog-${uniqueName}'
    networkAcls: {
      defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2022-09-01' = {
  name: 'swa-${uniqueName}'
  location: webappLocation
  properties: {
    provider: 'None'
  }
  sku: {
    name: 'Free'
    tier: 'Free'
  }
}

output webappUrl string = staticWebApp.properties.defaultHostname
output webappName string = staticWebApp.name
output webapiUrl string = appServiceWeb.properties.defaultHostName
output webapiName string = appServiceWeb.name
