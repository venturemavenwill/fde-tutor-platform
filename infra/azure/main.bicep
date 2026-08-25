targetScope = 'subscription'

@description('Azure region for the isolated technical evidence environment.')
param location string = 'westus3'

@description('Short environment name used to derive deterministic resource names.')
@minLength(2)
@maxLength(12)
param environmentName string = 'dev'

@description('Microsoft Entra tenant used only for this technical evidence deployment.')
param tenantId string

@description('PostgreSQL administrator login used by the technical migration runner.')
param postgresAdministratorLogin string = 'fdetutoradmin'

@description('Object ID of the deployment principal that may administer technical Key Vault secrets.')
param deploymentPrincipalObjectId string

@description('PostgreSQL administrator password. It is stored in Key Vault and never output.')
@secure()
param postgresAdministratorPassword string

var resourceToken = uniqueString(subscription().id, location, environmentName)
var resourceGroupName = 'azrg${resourceToken}'
var tags = {
  application: 'fde-tutor'
  environment: environmentName
  purpose: 'phase-1-technical-evidence'
  repository: 'fde-tutor-platform'
  dataClassification: 'synthetic-only'
  pilotAuthorized: 'false'
  technicalTenantId: tenantId
  fdeLifecycle: 'started'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

resource postgresStopRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(subscription().id, 'fde-tutor-postgres-stop')
  properties: {
    roleName: 'FDE Tutor PostgreSQL Stop Operator'
    description: 'Read and stop only the technical PostgreSQL server when the FDE Tutor app is deallocated.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.DBforPostgreSQL/flexibleServers/read'
          'Microsoft.DBforPostgreSQL/flexibleServers/stop/action'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
    assignableScopes: [
      resourceGroup.id
    ]
  }
}

module resources './resources.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    deploymentPrincipalObjectId: deploymentPrincipalObjectId
    postgresAdministratorLogin: postgresAdministratorLogin
    postgresAdministratorPassword: postgresAdministratorPassword
    postgresStopRoleDefinitionId: postgresStopRole.id
    resourceToken: resourceToken
    tags: tags
  }
}

output resourceGroupName string = resourceGroupName
output containerAppName string = resources.outputs.containerAppName
output containerAppFqdn string = resources.outputs.containerAppFqdn
output containerAppResourceId string = resources.outputs.containerAppResourceId
output containerRegistryName string = resources.outputs.containerRegistryName
output containerRegistryLoginServer string = resources.outputs.containerRegistryLoginServer
output keyVaultName string = resources.outputs.keyVaultName
output managedIdentityClientId string = resources.outputs.managedIdentityClientId
output managedIdentityResourceId string = resources.outputs.managedIdentityResourceId
output postgresServerName string = resources.outputs.postgresServerName
output lifecycleJobName string = resources.outputs.lifecycleJobName
