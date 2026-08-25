[CmdletBinding()]
param(
    [string]$SubscriptionId,
    [string]$TenantId,
    [string]$Location,
    [string]$EnvironmentName,
    [ValidatePattern('^[a-z]{0,5}$')]
    [string]$ResourceToken = '',
    [switch]$ForceBuild,
    [switch]$SkipProvision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'environment.ps1')
$fdeEnvironment = Get-FdeTutorEnvironment
if (-not $SubscriptionId) {
    $SubscriptionId = Get-FdeTutorSetting $fdeEnvironment 'subscriptionId'
}
if (-not $TenantId) {
    $TenantId = Get-FdeTutorSetting $fdeEnvironment 'tenantId'
}
if (-not $Location) {
    $Location = Get-FdeTutorSetting $fdeEnvironment 'location'
}
if (-not $EnvironmentName) {
    $EnvironmentName = Get-FdeTutorSetting $fdeEnvironment 'environmentName'
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$stateRoot = Join-Path $repositoryRoot '.azure'
$templatePath = Join-Path $PSScriptRoot 'main.bicep'
$baseParametersPath = Join-Path $PSScriptRoot 'main.parameters.json'
$effectiveEnvironmentName = "$EnvironmentName$ResourceToken"
$deploymentName = "fde-tutor-$effectiveEnvironmentName"
$localParametersPath = Join-Path $stateRoot "$deploymentName.parameters.json"
$deploymentOutputsPath = Join-Path $stateRoot "$deploymentName.outputs.json"
$entraStatePath = Join-Path $stateRoot "$deploymentName.entra.json"
$imageTagPath = Join-Path $stateRoot "$deploymentName.image-tag.txt"
$postgresPasswordPath = Join-Path $stateRoot "$deploymentName.postgres-password.txt"
$appRolesPath = Join-Path $repositoryRoot 'infra\identity\entra-app-roles.json'

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host "[Azure evidence] $Message" -ForegroundColor Cyan
}

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
    if ([string]::IsNullOrWhiteSpace(($output -join ''))) {
        return $null
    }

    return ($output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Invoke-AzNoOutput {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & az @Arguments --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

function Get-OrCreateApplication {
    param([Parameter(Mandatory)][string]$DisplayName)

    $applications = @(
        Invoke-AzJson -Arguments @(
            'ad',
            'app',
            'list',
            '--filter',
            "displayName eq '$DisplayName'"
        )
    )
    if ($applications.Count -gt 1) {
        throw "More than one Entra application is named '$DisplayName'."
    }
    if ($applications.Count -eq 1) {
        return $applications[0]
    }

    return Invoke-AzJson -Arguments @(
        'ad',
        'app',
        'create',
        '--display-name',
        $DisplayName,
        '--sign-in-audience',
        'AzureADMyOrg'
    )
}

function Get-OrCreateServicePrincipal {
    param([Parameter(Mandatory)][string]$AppId)

    $servicePrincipals = @(
        Invoke-AzJson -Arguments @(
            'ad',
            'sp',
            'list',
            '--filter',
            "appId eq '$AppId'"
        )
    )
    if ($servicePrincipals.Count -gt 1) {
        throw "More than one service principal has appId '$AppId'."
    }
    if ($servicePrincipals.Count -eq 1) {
        return $servicePrincipals[0]
    }

    return Invoke-AzJson -Arguments @('ad', 'sp', 'create', '--id', $AppId)
}

function Write-ProtectedJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )

    $json = $Value | ConvertTo-Json -Depth 30
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false))
    & icacls $Path /inheritance:r /grant:r "${env:USERDOMAIN}\${env:USERNAME}:(R,W)" |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restrict local state file '$Path'."
    }
}

New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
Set-Location $repositoryRoot

Write-Step 'Selecting the target subscription and registering providers.'
Invoke-AzNoOutput -Arguments @('account', 'set', '--subscription', $SubscriptionId)
foreach ($provider in @(
    'Microsoft.App',
    'Microsoft.ContainerRegistry',
    'Microsoft.DBforPostgreSQL',
    'Microsoft.KeyVault',
    'Microsoft.ManagedIdentity',
    'Microsoft.OperationalInsights'
)) {
    Invoke-AzNoOutput -Arguments @(
        'provider',
        'register',
        '--namespace',
        $provider,
        '--wait'
    )
}

$signedInUser = Invoke-AzJson -Arguments @('ad', 'signed-in-user', 'show')
if (-not $signedInUser.id) {
    throw 'The signed-in Azure CLI user does not expose an Entra object ID.'
}

if (Test-Path $postgresPasswordPath) {
    $postgresPassword = (Get-Content $postgresPasswordPath -Raw).Trim()
} else {
    $randomBytes = New-Object byte[] 24
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($randomBytes)
    } finally {
        $random.Dispose()
    }
    $postgresPassword =
        (($randomBytes | ForEach-Object { $_.ToString('X2') }) -join '') + 'aA1!'
    Set-Content -Path $postgresPasswordPath -Value $postgresPassword -NoNewline
    & icacls $postgresPasswordPath /inheritance:r /grant:r "${env:USERDOMAIN}\${env:USERNAME}:(R,W)" |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restrict '$postgresPasswordPath'."
    }
}

$parameters = Get-Content $baseParametersPath -Raw | ConvertFrom-Json
$parameters.parameters.environmentName.value = $effectiveEnvironmentName
$parameters.parameters.location.value = $Location
$parameters.parameters.tenantId.value = $TenantId
$parameters.parameters |
    Add-Member -NotePropertyName deploymentPrincipalObjectId -NotePropertyValue @{
        value = $signedInUser.id
    } -Force
$parameters.parameters |
    Add-Member -NotePropertyName postgresAdministratorPassword -NotePropertyValue @{
        value = $postgresPassword
    } -Force
Write-ProtectedJson -Path $localParametersPath -Value $parameters

if ($SkipProvision) {
    if (-not (Test-Path $deploymentOutputsPath)) {
        throw "Cannot skip provisioning because '$deploymentOutputsPath' is missing."
    }
    Write-Step 'Reusing the previously validated Azure resource outputs.'
    $safeOutputs = Get-Content $deploymentOutputsPath -Raw | ConvertFrom-Json
} else {
    Write-Step 'Running subscription-scope Bicep what-if.'
    & az deployment sub what-if `
        --name $deploymentName `
        --location $Location `
        --template-file $templatePath `
        --parameters "@$localParametersPath" `
        --result-format ResourceIdOnly `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'Azure what-if failed.'
    }

    Write-Step 'Provisioning the isolated Azure resources.'
    $deployment = Invoke-AzJson -Arguments @(
        'deployment',
        'sub',
        'create',
        '--name',
        $deploymentName,
        '--location',
        $Location,
        '--template-file',
        $templatePath,
        '--parameters',
        "@$localParametersPath"
    )
    $outputs = $deployment.properties.outputs
    $safeOutputs = [ordered]@{}
    foreach ($property in $outputs.PSObject.Properties) {
        $safeOutputs[$property.Name] = $property.Value.value
    }
    Write-ProtectedJson -Path $deploymentOutputsPath -Value $safeOutputs
}

$resourceGroupName = $safeOutputs.resourceGroupName
$containerAppName = $safeOutputs.containerAppName
$containerAppFqdn = $safeOutputs.containerAppFqdn
$containerRegistryName = $safeOutputs.containerRegistryName
$containerRegistryLoginServer = $safeOutputs.containerRegistryLoginServer
$keyVaultName = $safeOutputs.keyVaultName
$managedIdentityResourceId = $safeOutputs.managedIdentityResourceId

Write-Step 'Creating or updating the Venture Maven Entra applications.'
$applicationSuffix = $resourceGroupName.Substring(
    [Math]::Max(0, $resourceGroupName.Length - 8))
$apiApplication = Get-OrCreateApplication -DisplayName "FDE Tutor Technical API $applicationSuffix"
$spaApplication = Get-OrCreateApplication -DisplayName "FDE Tutor Technical SPA $applicationSuffix"
Write-Step 'Resolved API and SPA application registrations.'
$existingApi = Invoke-AzJson -Arguments @('ad', 'app', 'show', '--id', $apiApplication.appId)
$scope = @($existingApi.api.oauth2PermissionScopes |
    Where-Object { $_.value -eq 'access_as_user' } |
    Select-Object -First 1)
$scopeId = if ($scope.Count -eq 1) {
    $scope[0].id
} else {
    [guid]::NewGuid().ToString()
}

$appRoles = (Get-Content $appRolesPath -Raw | ConvertFrom-Json).appRoles
$apiPatchPath = Join-Path $stateRoot "$deploymentName.api-patch.json"
$apiPatch = @{
    api = @{
        oauth2PermissionScopes = @(
            @{
                adminConsentDescription = 'Use the FDE Tutor technical evidence API as the signed-in user.'
                adminConsentDisplayName = 'Use FDE Tutor technical evidence API'
                id = $scopeId
                isEnabled = $true
                type = 'User'
                userConsentDescription = 'Use the FDE Tutor technical evidence API.'
                userConsentDisplayName = 'Use FDE Tutor technical evidence API'
                value = 'access_as_user'
            }
        )
        requestedAccessTokenVersion = 2
    }
    appRoles = $appRoles
    identifierUris = @("api://$($apiApplication.appId)")
}
Write-ProtectedJson -Path $apiPatchPath -Value $apiPatch
Invoke-AzNoOutput -Arguments @(
    'rest',
    '--method',
    'PATCH',
    '--uri',
    "https://graph.microsoft.com/v1.0/applications/$($apiApplication.id)",
    '--body',
    "@$apiPatchPath",
    '--headers',
    'Content-Type=application/json'
)
Write-Step 'Updated API scope and app roles.'

$azureCliAppId = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
$requiredPreAuthorizedClients = @($spaApplication.appId, $azureCliAppId)
$existingPreAuthorizedClients = @(
    $existingApi.api.preAuthorizedApplications |
        Select-Object -ExpandProperty appId
)
if (@(
    $requiredPreAuthorizedClients |
        Where-Object { $_ -notin $existingPreAuthorizedClients }
).Count -gt 0) {
    $apiPatch.api['preAuthorizedApplications'] = @(
        @{
            appId = $spaApplication.appId
            delegatedPermissionIds = @($scopeId)
        }
        @{
            appId = $azureCliAppId
            delegatedPermissionIds = @($scopeId)
        }
    )
    Write-ProtectedJson -Path $apiPatchPath -Value $apiPatch
    Invoke-AzNoOutput -Arguments @(
        'rest',
        '--method',
        'PATCH',
        '--uri',
        "https://graph.microsoft.com/v1.0/applications/$($apiApplication.id)",
        '--body',
        "@$apiPatchPath",
        '--headers',
        'Content-Type=application/json'
    )
    Write-Step 'Updated API pre-authorized clients.'
} else {
    Write-Step 'API pre-authorized clients already match.'
}

$spaPatchPath = Join-Path $stateRoot "$deploymentName.spa-patch.json"
$existingSpa = Invoke-AzJson -Arguments @('ad', 'app', 'show', '--id', $spaApplication.appId)
$spaPatch = @{
    requiredResourceAccess = @(
        @{
            resourceAppId = $apiApplication.appId
            resourceAccess = @(
                @{
                    id = $scopeId
                    type = 'Scope'
                }
            )
        }
    )
    spa = @{
        redirectUris = @("https://$containerAppFqdn")
    }
}
$spaRedirectUri = "https://$containerAppFqdn"
$spaHasRedirect = @($existingSpa.spa.redirectUris) -contains $spaRedirectUri
$spaHasScope = @(
    $existingSpa.requiredResourceAccess |
        Where-Object {
            $_.resourceAppId -eq $apiApplication.appId -and
            @($_.resourceAccess.id) -contains $scopeId
        }
).Count -gt 0
if (-not $spaHasRedirect -or -not $spaHasScope) {
    Write-ProtectedJson -Path $spaPatchPath -Value $spaPatch
    Invoke-AzNoOutput -Arguments @(
        'rest',
        '--method',
        'PATCH',
        '--uri',
        "https://graph.microsoft.com/v1.0/applications/$($spaApplication.id)",
        '--body',
        "@$spaPatchPath",
        '--headers',
        'Content-Type=application/json'
    )
    Write-Step 'Updated SPA redirect and delegated API permission.'
} else {
    Write-Step 'SPA redirect and delegated API permission already match.'
}

$apiServicePrincipal = Get-OrCreateServicePrincipal -AppId $apiApplication.appId
$null = Get-OrCreateServicePrincipal -AppId $spaApplication.appId
Write-Step 'Resolved API and SPA enterprise applications.'
Invoke-AzNoOutput -Arguments @(
    'rest',
    '--method',
    'PATCH',
    '--uri',
    "https://graph.microsoft.com/v1.0/servicePrincipals/$($apiServicePrincipal.id)",
    '--body',
    '{"appRoleAssignmentRequired":true}',
    '--headers',
    'Content-Type=application/json'
)
Write-Step 'Enforced enterprise-application assignment.'

$assignments = @(
    (Invoke-AzJson -Arguments @(
        'rest',
        '--method',
        'GET',
        '--uri',
        "https://graph.microsoft.com/v1.0/servicePrincipals/$($apiServicePrincipal.id)/appRoleAssignedTo"
    )).value
)
foreach ($roleValue in @('Learner', 'Administrator')) {
    $role = @($appRoles | Where-Object { $_.value -eq $roleValue })
    if ($role.Count -ne 1) {
        throw "Expected one '$roleValue' app role."
    }
    $exists = $assignments | Where-Object {
        $_.principalId -eq $signedInUser.id -and $_.appRoleId -eq $role[0].id
    }
    if (-not $exists) {
        $assignmentPath = Join-Path $stateRoot "$deploymentName.$roleValue-assignment.json"
        Write-ProtectedJson -Path $assignmentPath -Value @{
            appRoleId = $role[0].id
            principalId = $signedInUser.id
            resourceId = $apiServicePrincipal.id
        }
        Invoke-AzNoOutput -Arguments @(
            'rest',
            '--method',
            'POST',
            '--uri',
            "https://graph.microsoft.com/v1.0/servicePrincipals/$($apiServicePrincipal.id)/appRoleAssignedTo",
            '--body',
            "@$assignmentPath",
            '--headers',
            'Content-Type=application/json'
        )
    }
}
Write-Step 'Verified Learner and Administrator assignments for the technical tester.'

Write-ProtectedJson -Path $entraStatePath -Value @{
    apiAppId = $apiApplication.appId
    apiObjectId = $apiApplication.id
    apiServicePrincipalId = $apiServicePrincipal.id
    scope = "api://$($apiApplication.appId)/access_as_user"
    scopeId = $scopeId
    spaAppId = $spaApplication.appId
    spaObjectId = $spaApplication.id
    tenantId = $TenantId
}

Write-Step 'Building the container remotely in Azure Container Registry.'
$imageTag = if (-not $ForceBuild -and (Test-Path $imageTagPath)) {
    (Get-Content $imageTagPath -Raw).Trim()
} else {
    $newTag = "$(git rev-parse --short HEAD)-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Set-Content -Path $imageTagPath -Value $newTag -NoNewline
    $newTag
}
$existingTag = ''
$repositoryFound = & az acr repository list `
    --name $containerRegistryName `
    --query "contains(@, 'fde-tutor')" `
    --output tsv `
    --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'The container registry repositories could not be listed.'
}
if (($repositoryFound -join '').Trim() -eq 'true') {
    $tagFound = & az acr repository show-tags `
        --name $containerRegistryName `
        --repository fde-tutor `
        --query "contains(@, '$imageTag')" `
        --output tsv `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'The FDE Tutor image tags could not be listed.'
    }
    if (($tagFound -join '').Trim() -eq 'true') {
        $existingTag = $imageTag
    }
}
if ([string]::IsNullOrWhiteSpace($existingTag)) {
    & az acr build `
        --registry $containerRegistryName `
        --image "fde-tutor:$imageTag" `
        --file (Join-Path $repositoryRoot 'Dockerfile') `
        --build-arg 'VITE_AUTH_MODE=entra' `
        --build-arg "VITE_ENTRA_CLIENT_ID=$($spaApplication.appId)" `
        --build-arg "VITE_ENTRA_TENANT_ID=$TenantId" `
        --build-arg "VITE_ENTRA_API_SCOPE=api://$($apiApplication.appId)/access_as_user" `
        --build-arg "VITE_ENTRA_REDIRECT_URI=https://$containerAppFqdn" `
        --no-logs `
        $repositoryRoot `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'The remote ACR build failed.'
    }
} else {
    Write-Step "Reusing existing image tag $imageTag."
}

Write-Step 'Binding the Key Vault secret and deploying the application revision.'
$secretId = & az keyvault secret show `
    --vault-name $keyVaultName `
    --name postgres-connection `
    --query id `
    --output tsv `
    --only-show-errors
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($secretId)) {
    throw 'The PostgreSQL Key Vault secret could not be resolved.'
}

Invoke-AzNoOutput -Arguments @(
    'containerapp',
    'secret',
    'set',
    '--name',
    $containerAppName,
    '--resource-group',
    $resourceGroupName,
    '--secrets',
    "postgres-connection=keyvaultref:$secretId,identityref:$managedIdentityResourceId"
)
Invoke-AzNoOutput -Arguments @(
    'containerapp',
    'ingress',
    'update',
    '--name',
    $containerAppName,
    '--resource-group',
    $resourceGroupName,
    '--target-port',
    '8080',
    '--transport',
    'auto',
    '--allow-insecure',
    'false'
)
Invoke-AzNoOutput -Arguments @(
    'containerapp',
    'update',
    '--name',
    $containerAppName,
    '--resource-group',
    $resourceGroupName,
    '--image',
    "$containerRegistryLoginServer/fde-tutor:$imageTag",
    '--revision-suffix',
    (Get-Date -Format 'yyyyMMddHHmmss'),
    '--set-env-vars',
    'ASPNETCORE_ENVIRONMENT=TechnicalEvidence',
    'Authentication__Mode=Entra',
    "Authentication__AllowedTenantId=$TenantId",
    'AzureAd__Instance=https://login.microsoftonline.com/',
    "AzureAd__TenantId=$TenantId",
    "AzureAd__ClientId=$($apiApplication.appId)",
    'Persistence__Provider=Postgres',
    'ConnectionStrings__FdeTutor=secretref:postgres-connection',
    'Database__ApplyMigrations=true',
    'Database__MigrationsRoot=/app/migrations',
    'Projection__Enabled=true',
    'Projection__BatchSize=100',
    'Projection__IdleDelayMilliseconds=1000',
    'Deployment__EvidenceOnly=true',
    'ContentPackage__Root=/app/content-package',
    'Logging__LogLevel__Microsoft.IdentityModel=Warning'
)

Write-Step 'Waiting for database-backed application readiness.'
$readyUri = "https://$containerAppFqdn/health/ready"
$deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
$ready = $false
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    try {
        $response = Invoke-RestMethod -Uri $readyUri -TimeoutSec 15
        if ($response.status -eq 'ready') {
            $ready = $true
            break
        }
    }
    catch [System.Net.WebException] {
        Start-Sleep -Seconds 10
    }
}
if (-not $ready) {
    throw "The deployed application did not become ready. Inspect '$containerAppName'."
}

Write-Step 'Deployment is ready.'
Write-Host "Application: https://$containerAppFqdn" -ForegroundColor Green
Write-Host "Resource group: $resourceGroupName"
Write-Host 'Boundary: Venture Maven technical evidence only; pilot enrolment remains prohibited.'
