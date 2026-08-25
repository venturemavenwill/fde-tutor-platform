[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action,
    [string]$SubscriptionId,
    [string]$ResourceGroupName,
    [string]$ContainerAppName,
    [string]$PostgresServerName,
    [string]$ApplicationUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'environment.ps1')
$fdeEnvironment = Get-FdeTutorEnvironment
if (-not $SubscriptionId) {
    $SubscriptionId = Get-FdeTutorSetting $fdeEnvironment 'subscriptionId'
}
if (-not $ResourceGroupName) {
    $ResourceGroupName = Get-FdeTutorSetting $fdeEnvironment 'resourceGroupName'
}
if (-not $ContainerAppName) {
    $ContainerAppName = Get-FdeTutorSetting $fdeEnvironment 'containerAppName'
}
if (-not $PostgresServerName) {
    $PostgresServerName = Get-FdeTutorSetting $fdeEnvironment 'postgresServerName'
}
if (-not $ApplicationUrl) {
    $ApplicationUrl = Get-FdeTutorSetting $fdeEnvironment 'applicationUrl'
}

function Invoke-AzNoOutput {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & az @Arguments --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

function Get-PostgresState {
    $state = & az postgres flexible-server show `
        --resource-group $ResourceGroupName `
        --name $PostgresServerName `
        --query state `
        --output tsv `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'The PostgreSQL state could not be read.'
    }

    return ($state -join '').Trim()
}

function Get-Revisions {
    $revisions = & az containerapp revision list `
        --resource-group $ResourceGroupName `
        --name $ContainerAppName `
        --output json `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'Container App revisions could not be read.'
    }

    $parsed = ($revisions -join [Environment]::NewLine) | ConvertFrom-Json
    foreach ($revision in @($parsed)) {
        Write-Output $revision
    }
}

function Get-ReplicaCount {
    $revisions = Get-Revisions
    $replicaCounts = @(
        $revisions | ForEach-Object { $_.properties.replicas }
    )
    $sum = ($replicaCounts | Measure-Object -Sum).Sum
    if ($null -eq $sum) {
        return 0
    }

    return [int]$sum
}

function Set-LifecycleTag {
    param([Parameter(Mandatory)][string]$Value)

    $resourceId = & az containerapp show `
        --resource-group $ResourceGroupName `
        --name $ContainerAppName `
        --query id `
        --output tsv `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'The Container App resource ID could not be read.'
    }
    Invoke-AzNoOutput -Arguments @(
        'tag',
        'update',
        '--resource-id',
        ($resourceId -join '').Trim(),
        '--operation',
        'Merge',
        '--tags',
        "fdeLifecycle=$Value"
    )
}

function Wait-ForPostgres {
    param([Parameter(Mandatory)][string]$ExpectedState)

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ((Get-PostgresState) -eq $ExpectedState) {
            return
        }
        Start-Sleep -Seconds 15
    }

    throw "PostgreSQL did not reach '$ExpectedState'."
}

function Wait-ForReadyApplication {
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-RestMethod `
                -Uri "$ApplicationUrl/health/ready" `
                -TimeoutSec 15
            if ($response.status -eq 'ready') {
                return
            }
        }
        catch [System.Net.WebException] {
            Start-Sleep -Seconds 10
        }
    }

    throw 'The Container App did not become ready.'
}

Invoke-AzNoOutput -Arguments @(
    'account',
    'set',
    '--subscription',
    $SubscriptionId
)

switch ($Action) {
    'Start' {
        $databaseState = Get-PostgresState
        if ($databaseState -eq 'Stopping') {
            Wait-ForPostgres -ExpectedState 'Stopped'
            $databaseState = 'Stopped'
        }
        if ($databaseState -eq 'Stopped') {
            Invoke-AzNoOutput -Arguments @(
                'postgres',
                'flexible-server',
                'start',
                '--resource-group',
                $ResourceGroupName,
                '--name',
                $PostgresServerName
            )
        }
        Wait-ForPostgres -ExpectedState 'Ready'
        Set-LifecycleTag -Value 'started'
        Invoke-AzNoOutput -Arguments @(
            'containerapp',
            'update',
            '--resource-group',
            $ResourceGroupName,
            '--name',
            $ContainerAppName,
            '--min-replicas',
            '1',
            '--max-replicas',
            '1'
        )
        $activeRevisions = @(Get-Revisions | Where-Object { $_.properties.active })
        if ($activeRevisions.Count -eq 0) {
            $latestRevision = Get-Revisions |
                Sort-Object { $_.properties.createdTime } -Descending |
                Select-Object -First 1
            if ($null -eq $latestRevision) {
                throw 'No Container App revision is available to activate.'
            }
            Invoke-AzNoOutput -Arguments @(
                'containerapp',
                'revision',
                'activate',
                '--resource-group',
                $ResourceGroupName,
                '--revision',
                $latestRevision.name
            )
        }
        Invoke-AzNoOutput -Arguments @(
            'containerapp',
            'ingress',
            'enable',
            '--resource-group',
            $ResourceGroupName,
            '--name',
            $ContainerAppName,
            '--type',
            'external',
            '--allow-insecure',
            'false',
            '--target-port',
            '8080',
            '--transport',
            'auto'
        )
        Wait-ForReadyApplication
        Write-Host "FDE Tutor started: $ApplicationUrl" -ForegroundColor Green
    }
    'Stop' {
        Invoke-AzNoOutput -Arguments @(
            'containerapp',
            'ingress',
            'disable',
            '--resource-group',
            $ResourceGroupName,
            '--name',
            $ContainerAppName
        )
        Invoke-AzNoOutput -Arguments @(
            'containerapp',
            'update',
            '--resource-group',
            $ResourceGroupName,
            '--name',
            $ContainerAppName,
            '--min-replicas',
            '0',
            '--max-replicas',
            '1'
        )
        foreach ($revision in @(Get-Revisions | Where-Object { $_.properties.active })) {
            Invoke-AzNoOutput -Arguments @(
                'containerapp',
                'revision',
                'deactivate',
                '--resource-group',
                $ResourceGroupName,
                '--revision',
                $revision.name
            )
        }
        $deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            if ((Get-ReplicaCount) -eq 0) {
                break
            }
            Start-Sleep -Seconds 10
        }
        if ((Get-ReplicaCount) -ne 0) {
            throw 'The Container App did not deallocate; PostgreSQL was left running.'
        }

        Set-LifecycleTag -Value 'stopped'
        $databaseState = Get-PostgresState
        if ($databaseState -eq 'Starting') {
            Wait-ForPostgres -ExpectedState 'Ready'
            $databaseState = 'Ready'
        }
        if ($databaseState -ne 'Stopped' -and $databaseState -ne 'Stopping') {
            Invoke-AzNoOutput -Arguments @(
                'postgres',
                'flexible-server',
                'stop',
                '--resource-group',
                $ResourceGroupName,
                '--name',
                $PostgresServerName
            )
        }
        Wait-ForPostgres -ExpectedState 'Stopped'
        Write-Host 'FDE Tutor and PostgreSQL are stopped.' -ForegroundColor Green
    }
    'Status' {
        $app = & az containerapp show `
            --resource-group $ResourceGroupName `
            --name $ContainerAppName `
            --query '{lifecycle:tags.fdeLifecycle,ingress:properties.configuration.ingress.external,minReplicas:properties.template.scale.minReplicas}' `
            --output json `
            --only-show-errors
        if ($LASTEXITCODE -ne 0) {
            throw 'The Container App status could not be read.'
        }
        Write-Host ($app -join [Environment]::NewLine)
        Write-Host "replicas=$(Get-ReplicaCount)"
        Write-Host "postgres=$(Get-PostgresState)"
    }
}
