[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$ExitAfterReady,
    [ValidateRange(10, 600)]
    [int]$StartupTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$apiProject = Join-Path $repositoryRoot 'apps\platform-api\FdeTutor.Api.csproj'
$dependencyMarker = Join-Path $repositoryRoot 'node_modules\.package-lock.json'
$apiUrl = 'http://localhost:5080'
$apiReadyUrl = "$apiUrl/health/ready"
$webUrl = 'http://127.0.0.1:5173'
$logDirectory = Join-Path (
    [System.IO.Path]::GetTempPath()) "fde-tutor-platform-launcher-$PID"
$apiOutputLog = Join-Path $logDirectory 'api.stdout.log'
$apiErrorLog = Join-Path $logDirectory 'api.stderr.log'
$webOutputLog = Join-Path $logDirectory 'web.stdout.log'
$webErrorLog = Join-Path $logDirectory 'web.stderr.log'

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host "[FDE Tutor] $Message" -ForegroundColor Cyan
}

function Get-RequiredCommand {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -CommandType Application -ErrorAction Stop
    return $command.Source
}

function Get-ToolVersion {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $output = [string](& $Command @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "$DisplayName returned exit code $LASTEXITCODE."
    }

    $versionText = $output.Trim().TrimStart('v')
    $version = [version]'0.0'
    if (-not [version]::TryParse($versionText, [ref]$version)) {
        throw "$DisplayName returned an unrecognized version: '$versionText'."
    }

    return $version
}

function Assert-MinimumVersion {
    param(
        [Parameter(Mandatory)][version]$Actual,
        [Parameter(Mandatory)][version]$Minimum,
        [Parameter(Mandatory)][string]$DisplayName
    )

    if ($Actual -lt $Minimum) {
        throw "$DisplayName $Minimum or later is required; found $Actual."
    }
}

function Test-TcpPort {
    param([Parameter(Mandatory)][int]$Port)

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connection = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
        if (-not $connection.AsyncWaitHandle.WaitOne(300)) {
            return $false
        }

        $client.EndConnect($connection)
        return $client.Connected
    }
    catch [System.Net.Sockets.SocketException] {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Get-ListenerProcessId {
    param([Parameter(Mandatory)][int]$Port)

    $processIds = @(
        Get-NetTCPConnection -State Listen |
            Where-Object { $_.LocalPort -eq $Port } |
            Select-Object -ExpandProperty OwningProcess -Unique
    )
    if ($processIds.Count -ne 1) {
        throw "Expected one listener process on port $Port; found $($processIds.Count)."
    }

    return [int]$processIds[0]
}

function Test-HttpReady {
    param([Parameter(Mandatory)][uri]$Uri)

    $request = [System.Net.HttpWebRequest]::Create($Uri)
    $request.AllowAutoRedirect = $false
    $request.Timeout = 1000
    $response = $null
    try {
        $response = [System.Net.HttpWebResponse]$request.GetResponse()
        $statusCode = [int]$response.StatusCode
        return $statusCode -ge 200 -and $statusCode -lt 400
    }
    catch [System.Net.WebException] {
        return $false
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
    }
}

function Start-LoggedProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$StandardOutput,
        [Parameter(Mandatory)][string]$StandardError
    )

    return Start-Process `
        -FilePath $FilePath `
        -ArgumentList $Arguments `
        -WorkingDirectory $repositoryRoot `
        -RedirectStandardOutput $StandardOutput `
        -RedirectStandardError $StandardError `
        -PassThru
}

function Get-LogSummary {
    param(
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string[]]$Paths
    )

    $lines = @()
    foreach ($path in $Paths) {
        if (Test-Path $path) {
            $lines += Get-Content $path -Tail 30
        }
    }

    if ($lines.Count -eq 0) {
        return "$DisplayName produced no log output. Logs: $logDirectory"
    }

    return "$DisplayName logs:`n$($lines -join [Environment]::NewLine)"
}

function Wait-ForEndpoint {
    param(
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][uri]$Uri,
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string[]]$LogPaths
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            $summary = Get-LogSummary -DisplayName $DisplayName -Paths $LogPaths
            throw "$DisplayName exited with code $($Process.ExitCode).`n$summary"
        }

        if (Test-HttpReady -Uri $Uri) {
            return
        }

        Start-Sleep -Milliseconds 500
    }

    $summary = Get-LogSummary -DisplayName $DisplayName -Paths $LogPaths
    throw "$DisplayName was not ready within $StartupTimeoutSeconds seconds.`n$summary"
}

function Stop-ProcessTree {
    param([Parameter(Mandatory)][int]$TargetProcessId)

    $children = @(
        Get-CimInstance `
            -ClassName Win32_Process `
            -Filter "ParentProcessId = $TargetProcessId"
    )
    foreach ($child in $children) {
        Stop-ProcessTree -TargetProcessId ([int]$child.ProcessId)
    }

    $matchingProcess = @(
        Get-CimInstance `
            -ClassName Win32_Process `
            -Filter "ProcessId = $TargetProcessId"
    )
    if ($matchingProcess.Count -eq 0) {
        return
    }

    try {
        Stop-Process -Id $TargetProcessId -Force -ErrorAction Stop
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
        Write-Verbose "Process $TargetProcessId exited during launcher cleanup."
    }
}

$originalLocation = Get-Location
$apiProcess = $null
$webProcess = $null
$apiListenerProcessId = $null
$webListenerProcessId = $null

try {
    Set-Location $repositoryRoot
    Write-Step 'Checking local prerequisites.'

    $nodeCommand = Get-RequiredCommand -Name 'node.exe'
    $npmCommand = Get-RequiredCommand -Name 'npm.cmd'
    $dotnetCommand = Get-RequiredCommand -Name 'dotnet.exe'

    $nodeVersion = Get-ToolVersion `
        -Command $nodeCommand `
        -DisplayName 'Node.js' `
        -Arguments @('--version')
    $npmVersion = Get-ToolVersion `
        -Command $npmCommand `
        -DisplayName 'npm' `
        -Arguments @('--version')
    $dotnetVersion = Get-ToolVersion `
        -Command $dotnetCommand `
        -DisplayName '.NET SDK' `
        -Arguments @('--version')

    Assert-MinimumVersion `
        -Actual $nodeVersion `
        -Minimum ([version]'24.0') `
        -DisplayName 'Node.js'
    Assert-MinimumVersion `
        -Actual $npmVersion `
        -Minimum ([version]'11.0') `
        -DisplayName 'npm'
    if ($dotnetVersion.Major -ne 10) {
        throw ".NET SDK 10 is required; found $dotnetVersion."
    }

    foreach ($port in @(5080, 5173)) {
        if (Test-TcpPort -Port $port) {
            throw "Port $port is already in use. Stop the process using it and run the launcher again."
        }
    }

    if (-not (Test-Path $dependencyMarker)) {
        Write-Step 'Installing locked npm dependencies for the first launch.'
        & $npmCommand ci
        if ($LASTEXITCODE -ne 0) {
            throw "npm ci returned exit code $LASTEXITCODE."
        }
    }

    New-Item -ItemType Directory -Path $logDirectory | Out-Null
    Write-Step "Starting the API at $apiUrl."
    $apiProcess = Start-LoggedProcess `
        -FilePath $dotnetCommand `
        -Arguments @(
            'run',
            '--project',
            "`"$apiProject`"",
            '--launch-profile',
            'http'
        ) `
        -StandardOutput $apiOutputLog `
        -StandardError $apiErrorLog

    Write-Step "Starting the learner app at $webUrl."
    $webProcess = Start-LoggedProcess `
        -FilePath $npmCommand `
        -Arguments @(
            'run',
            'dev',
            '--workspace',
            '@fde-tutor/learner-web',
            '--',
            '--host',
            '127.0.0.1',
            '--port',
            '5173',
            '--strictPort'
        ) `
        -StandardOutput $webOutputLog `
        -StandardError $webErrorLog

    Write-Step 'Waiting for the API content checks to pass.'
    Wait-ForEndpoint `
        -DisplayName 'Platform API' `
        -Uri $apiReadyUrl `
        -Process $apiProcess `
        -LogPaths @($apiOutputLog, $apiErrorLog)

    Write-Step 'Waiting for the learner app to become reachable.'
    Wait-ForEndpoint `
        -DisplayName 'Learner app' `
        -Uri $webUrl `
        -Process $webProcess `
        -LogPaths @($webOutputLog, $webErrorLog)
    $apiListenerProcessId = Get-ListenerProcessId -Port 5080
    $webListenerProcessId = Get-ListenerProcessId -Port 5173

    Write-Host ''
    Write-Host 'FDE Tutor is ready for manual review.' -ForegroundColor Green
    Write-Host "Learner app: $webUrl"
    Write-Host "API health: $apiReadyUrl"
    Write-Host ''
    Write-Host 'This is DEVELOPMENT-ONLY mode with a synthetic identity and in-memory data.'
    Write-Host 'All learner progress from this launch is discarded when the launcher stops.'
    Write-Host "Process logs: $logDirectory"

    if (-not $NoBrowser) {
        Write-Step 'Opening the learner app in your default browser.'
        Start-Process $webUrl
    }

    if ($ExitAfterReady) {
        Write-Step 'Smoke-test mode is complete.'
        return
    }

    Write-Host ''
    [void](Read-Host 'Press Enter to stop the learner app and API')
}
finally {
    Write-Step 'Stopping launcher-owned processes.'
    if ($null -ne $webListenerProcessId) {
        Stop-ProcessTree -TargetProcessId $webListenerProcessId
    }
    if ($null -ne $apiListenerProcessId) {
        Stop-ProcessTree -TargetProcessId $apiListenerProcessId
    }
    if ($null -ne $webProcess) {
        Stop-ProcessTree -TargetProcessId $webProcess.Id
    }
    if ($null -ne $apiProcess) {
        Stop-ProcessTree -TargetProcessId $apiProcess.Id
    }
    Set-Location $originalLocation
    Start-Sleep -Milliseconds 500
    foreach ($port in @(5080, 5173)) {
        if (Test-TcpPort -Port $port) {
            throw "Launcher cleanup did not release port $port."
        }
    }
}
