@description('Resource-name token derived at subscription scope.')
param resourceToken string

@description('Azure region for all resources in this technical environment.')
param location string

@description('PostgreSQL administrator login.')
param postgresAdministratorLogin string

@description('Object ID of the deployment principal.')
param deploymentPrincipalObjectId string

@description('PostgreSQL administrator password.')
@secure()
param postgresAdministratorPassword string

@description('Resource ID of the custom role that permits read and stop on the technical PostgreSQL server.')
param postgresStopRoleDefinitionId string

@description('Common technical-evidence tags.')
param tags object

var managedIdentityName = 'azid${resourceToken}'
var registryName = 'azacr${resourceToken}'
var workspaceName = 'azlog${resourceToken}'
var environmentNameResource = 'azcae${resourceToken}'
var postgresServerName = 'azpg${resourceToken}'
var keyVaultName = 'azkv${resourceToken}'
var containerAppName = 'azapp${resourceToken}'
var lifecycleJobName = 'azjob${resourceToken}'
var databaseName = 'fdetutor'
var postgresConnectionString = 'Host=${postgresServerName}.postgres.database.azure.com;Port=5432;Database=${databaseName};Username=${postgresAdministratorLogin};Password=${postgresAdministratorPassword};SSL Mode=Require;Trust Server Certificate=false'

module managedIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.6.0' = {
  params: {
    name: managedIdentityName
    location: location
    tags: tags
  }
}

module registry 'br/public:avm/res/container-registry/registry:0.13.0' = {
  params: {
    name: registryName
    acrAdminUserEnabled: false
    acrSku: 'Basic'
    location: location
    networkRuleBypassOptions: 'None'
    networkRuleSetDefaultAction: 'Allow'
    publicNetworkAccess: 'Enabled'
    retentionPolicyStatus: 'disabled'
    roleAssignments: [
      {
        principalId: managedIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'AcrPull'
      }
    ]
    tags: tags
    zoneRedundancy: 'Disabled'
  }
}

module workspace 'br/public:avm/res/operational-insights/workspace:0.16.1' = {
  params: {
    name: workspaceName
    location: location
    dataRetention: 30
    tags: tags
  }
}

module managedEnvironment 'br/public:avm/res/app/managed-environment:0.15.0' = {
  params: {
    name: environmentNameResource
    location: location
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsWorkspaceResourceId: workspace.outputs.resourceId
    }
    peerTrafficEncryption: true
    publicNetworkAccess: 'Enabled'
    tags: tags
    zoneRedundant: false
  }
}

module postgres 'br/public:avm/res/db-for-postgre-sql/flexible-server:0.16.0' = {
  params: {
    name: postgresServerName
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    availabilityZone: -1
    backupRetentionDays: 7
    databases: [
      {
        charset: 'UTF8'
        collation: 'en_US.utf8'
        name: databaseName
      }
    ]
    enableAdvancedThreatProtection: false
    firewallRules: [
      {
        endIpAddress: '0.0.0.0'
        name: 'AllowAzureServices'
        startIpAddress: '0.0.0.0'
      }
    ]
    geoRedundantBackup: 'Disabled'
    highAvailability: 'Disabled'
    location: location
    publicNetworkAccess: 'Enabled'
    serverThreatProtection: 'Disabled'
    skuName: 'Standard_B1ms'
    storageSizeGB: 32
    tags: tags
    tier: 'Burstable'
    version: '17'
  }
}

module keyVault 'br/public:avm/res/key-vault/vault:0.14.0' = {
  params: {
    name: keyVaultName
    enablePurgeProtection: true
    enableRbacAuthorization: true
    location: location
    publicNetworkAccess: 'Enabled'
    roleAssignments: [
      {
        principalId: managedIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Key Vault Secrets User'
      }
      {
        principalId: deploymentPrincipalObjectId
        principalType: 'User'
        roleDefinitionIdOrName: 'Key Vault Secrets Officer'
      }
    ]
    secrets: [
      {
        name: 'postgres-connection'
        value: postgresConnectionString
      }
    ]
    sku: 'standard'
    softDeleteRetentionInDays: 90
    tags: tags
  }
}

module containerApp 'br/public:avm/res/app/container-app:0.23.0' = {
  params: {
    name: containerAppName
    containers: [
      {
        image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'fde-tutor'
        resources: {
          cpu: 1
          memory: '2Gi'
        }
      }
    ]
    corsPolicy: {
      allowedHeaders: [
        '*'
      ]
      allowedMethods: [
        'GET'
      ]
      allowedOrigins: [
        '*'
      ]
    }
    environmentResourceId: managedEnvironment.outputs.resourceId
    ingressAllowInsecure: false
    ingressExternal: true
    ingressTargetPort: 80
    ingressTransport: 'auto'
    location: location
    managedIdentities: {
      userAssignedResourceIds: [
        managedIdentity.outputs.resourceId
      ]
    }
    registries: [
      {
        identity: managedIdentity.outputs.resourceId
        server: registry.outputs.loginServer
      }
    ]
    roleAssignments: [
      {
        principalId: managedIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Reader'
      }
    ]
    runtime: {
      dotnet: {
        autoConfigureDataProtection: true
      }
    }
    scaleSettings: {
      maxReplicas: 1
      minReplicas: 1
    }
    tags: tags
  }
}

resource postgresExisting 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' existing = {
  name: postgresServerName
}

resource postgresStopAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: postgresExisting
  name: guid(
    postgresExisting.id,
    managedIdentityName,
    postgresStopRoleDefinitionId)
  properties: {
    principalId: managedIdentity.outputs.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: postgresStopRoleDefinitionId
  }
}

module lifecycleWatchdog 'br/public:avm/res/app/job:0.7.2' = {
  params: {
    name: lifecycleJobName
    containers: [
      {
        args: [
          '-c'
          'set -eu; az login --identity --client-id "$AZURE_CLIENT_ID" --allow-no-subscriptions --output none; az account set --subscription "$AZURE_SUBSCRIPTION_ID"; lifecycle=$(az resource show --ids "$CONTAINER_APP_ID" --api-version 2026-01-01 --query "tags.fdeLifecycle" --output tsv); ingress=$(az resource show --ids "$CONTAINER_APP_ID" --api-version 2026-01-01 --query "properties.configuration.ingress.external" --output tsv); min_replicas=$(az resource show --ids "$CONTAINER_APP_ID" --api-version 2026-01-01 --query "properties.template.scale.minReplicas" --output tsv); database_state=$(az postgres flexible-server show --ids "$POSTGRES_SERVER_ID" --query "state" --output tsv); if { [ "$lifecycle" = "stopped" ] || { [ "$ingress" != "true" ] && [ "$min_replicas" = "0" ]; }; } && [ "$database_state" != "Stopped" ] && [ "$database_state" != "Stopping" ]; then az postgres flexible-server stop --ids "$POSTGRES_SERVER_ID" --only-show-errors --output none; echo "Stopped PostgreSQL because the FDE Tutor app is marked stopped."; else echo "No action: lifecycle=$lifecycle ingress=$ingress min_replicas=$min_replicas database_state=$database_state"; fi'
        ]
        command: [
          '/bin/sh'
        ]
        env: [
          {
            name: 'AZURE_CLIENT_ID'
            value: managedIdentity.outputs.clientId
          }
          {
            name: 'AZURE_SUBSCRIPTION_ID'
            value: subscription().subscriptionId
          }
          {
            name: 'CONTAINER_APP_ID'
            value: containerApp.outputs.resourceId
          }
          {
            name: 'POSTGRES_SERVER_ID'
            value: postgresExisting.id
          }
        ]
        image: 'mcr.microsoft.com/azure-cli:2.85.0'
        name: 'postgres-lifecycle-watchdog'
        resources: {
          cpu: '0.25'
          memory: '0.5Gi'
        }
      }
    ]
    environmentResourceId: managedEnvironment.outputs.resourceId
    location: location
    managedIdentities: {
      userAssignedResourceIds: [
        managedIdentity.outputs.resourceId
      ]
    }
    replicaRetryLimit: 2
    replicaTimeout: 600
    scheduleTriggerConfig: {
      cronExpression: '0 3 * * *'
      parallelism: 1
      replicaCompletionCount: 1
    }
    tags: tags
    triggerType: 'Schedule'
  }
  dependsOn: [
    postgresStopAssignment
  ]
}

output containerAppName string = containerApp.outputs.name
output containerAppFqdn string = containerApp.outputs.fqdn
output containerAppResourceId string = containerApp.outputs.resourceId
output containerRegistryName string = registry.outputs.name
output containerRegistryLoginServer string = registry.outputs.loginServer
output keyVaultName string = keyVault.outputs.name
output managedIdentityClientId string = managedIdentity.outputs.clientId
output managedIdentityResourceId string = managedIdentity.outputs.resourceId
output postgresServerName string = postgres.outputs.name
output lifecycleJobName string = lifecycleWatchdog.outputs.name
