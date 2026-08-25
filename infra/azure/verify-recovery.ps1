[CmdletBinding()]
param(
    [string]$ResourceGroupName,
    [string]$ContainerAppName,
    [string]$ContainerAppFqdn,
    [string]$KeyVaultName,
    [string]$SourceServerName,
    [string]$RestoredServerName,
    [string]$SessionId,
    [string]$ApiScope,
    [string]$TenantId,
    [switch]$KeepRestoredServer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'environment.ps1')
$fdeEnvironment = Get-FdeTutorEnvironment
if (-not $ResourceGroupName) {
    $ResourceGroupName = Get-FdeTutorSetting $fdeEnvironment 'resourceGroupName'
}
if (-not $ContainerAppName) {
    $ContainerAppName = Get-FdeTutorSetting $fdeEnvironment 'containerAppName'
}
if (-not $ContainerAppFqdn) {
    $ContainerAppFqdn =
        ([Uri](Get-FdeTutorSetting $fdeEnvironment 'applicationUrl')).Host
}
if (-not $KeyVaultName) {
    $KeyVaultName = Get-FdeTutorSetting $fdeEnvironment 'keyVaultName'
}
if (-not $SourceServerName) {
    $SourceServerName = Get-FdeTutorSetting $fdeEnvironment 'postgresServerName'
}
if (-not $RestoredServerName) {
    $RestoredServerName =
        Get-FdeTutorSetting $fdeEnvironment 'restoredPostgresServerName'
}
if (-not $SessionId) {
    $SessionId = Get-FdeTutorSetting $fdeEnvironment 'evidenceSessionId'
}
if (-not $ApiScope) {
    $ApiScope = Get-FdeTutorSetting $fdeEnvironment 'apiScope'
}
if (-not $TenantId) {
    $TenantId = Get-FdeTutorSetting $fdeEnvironment 'tenantId'
}

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$passwordPath = Join-Path $repositoryRoot '.azure\fde-tutor-dev.postgres-password.txt'
$connectionFile = Join-Path $repositoryRoot '.azure\recovery-connection.tmp'
$baseUri = "https://$ContainerAppFqdn"

function Invoke-AzNoOutput {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & az @Arguments --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

function Set-DatabaseSecret {
    param(
        [Parameter(Mandatory)][string]$ServerName,
        [Parameter(Mandatory)][string]$Password
    )

    $connection =
        "Host=$ServerName.postgres.database.azure.com;Port=5432;" +
        "Database=fdetutor;Username=fdetutoradmin;Password=$Password;" +
        "SSL Mode=Require;Trust Server Certificate=false"
    [System.IO.File]::WriteAllText(
        $connectionFile,
        $connection,
        [System.Text.UTF8Encoding]::new($false))
    Invoke-AzNoOutput -Arguments @(
        'keyvault',
        'secret',
        'set',
        '--vault-name',
        $KeyVaultName,
        '--name',
        'postgres-connection',
        '--file',
        $connectionFile
    )
    Remove-Item $connectionFile -Force
}

function Restart-And-Wait {
    $revisionToken = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    Invoke-AzNoOutput -Arguments @(
        'containerapp',
        'update',
        '--name',
        $ContainerAppName,
        '--resource-group',
        $ResourceGroupName,
        '--set-env-vars',
        "RecoveryProbe__Timestamp=$revisionToken"
    )

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
    while ([DateTimeOffset]::UtcNow -lt $deadline)
    {
        try
        {
            $response = Invoke-RestMethod -Uri "$baseUri/health/ready" -TimeoutSec 15
            if ($response.status -eq 'ready')
            {
                return
            }
        }
        catch [System.Net.WebException]
        {
            Start-Sleep -Seconds 10
        }
    }

    throw 'The Container App did not become ready after database switching.'
}

function Get-DeployedSessionState {
    $token = & az account get-access-token `
        --tenant $TenantId `
        --scope $ApiScope `
        --query accessToken `
        --output tsv `
        --only-show-errors
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token))
    {
        throw 'An access token for the technical API could not be acquired.'
    }

    return Invoke-RestMethod `
        -Uri "$baseUri/api/v1/s083/sessions/$SessionId" `
        -Headers @{ Authorization = "Bearer $token" } `
        -TimeoutSec 30
}

if (-not (Test-Path $passwordPath))
{
    throw "The protected PostgreSQL password file '$passwordPath' is missing."
}

$password = (Get-Content $passwordPath -Raw).Trim()
$switchedToRestore = $false
try
{
    Invoke-AzNoOutput -Arguments @(
        'postgres',
        'flexible-server',
        'firewall-rule',
        'create',
        '--resource-group',
        $ResourceGroupName,
        '--name',
        $RestoredServerName,
        '--rule-name',
        'AllowAzureServices',
        '--start-ip-address',
        '0.0.0.0',
        '--end-ip-address',
        '0.0.0.0'
    )
    Set-DatabaseSecret -ServerName $RestoredServerName -Password $password
    $switchedToRestore = $true
    Restart-And-Wait
    $restoredState = Get-DeployedSessionState
    if ($restoredState.policy.state -ne 'Complete' -or
        $restoredState.projectionVersion -ne 12 -or
        $restoredState.timeline.Count -ne 12)
    {
        throw 'The restored database did not reproduce the completed S083 state.'
    }

    Write-Host (
        "Restored state verified: state={0}, projectionVersion={1}, events={2}" -f
        $restoredState.policy.state,
        $restoredState.projectionVersion,
        $restoredState.timeline.Count)
}
finally
{
    if ($switchedToRestore)
    {
        Set-DatabaseSecret -ServerName $SourceServerName -Password $password
        Restart-And-Wait
        $sourceState = Get-DeployedSessionState
        if ($sourceState.policy.state -ne 'Complete')
        {
            throw 'The source database was not restored as the active application database.'
        }
    }
    if (Test-Path $connectionFile)
    {
        Remove-Item $connectionFile -Force
    }
}

if (-not $KeepRestoredServer)
{
    Invoke-AzNoOutput -Arguments @(
        'postgres',
        'flexible-server',
        'delete',
        '--resource-group',
        $ResourceGroupName,
        '--name',
        $RestoredServerName,
        '--yes'
    )
}

Write-Host 'Point-in-time recovery verification completed and the source database is active.'
