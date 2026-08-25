# Loads the local Azure environment settings for the isolated technical-evidence
# deployment. The values identify a private environment, so they live in
# `environment.local.json`, which is git-ignored and never committed.
#
# Copy `environment.example.json` to `environment.local.json` and fill it in
# before running deploy.ps1, lifecycle.ps1, or verify-recovery.ps1.

Set-StrictMode -Version Latest

function Get-FdeTutorEnvironment {
    [CmdletBinding()]
    param(
        [string]$Path
    )

    if (-not $Path) {
        $Path = Join-Path $PSScriptRoot 'environment.local.json'
    }

    if (-not (Test-Path $Path)) {
        $example = Join-Path $PSScriptRoot 'environment.example.json'
        throw @"
The local Azure environment file is missing:
  $Path

Copy the example and fill in the values for your isolated environment:
  Copy-Item '$example' '$Path'

That file is git-ignored on purpose. It records subscription, tenant, and
resource names for a private technical-evidence environment and must never be
committed.
"@
    }

    return Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function Get-FdeTutorSetting {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Environment,
        [Parameter(Mandatory)][string]$Name
    )

    $property = $Environment.PSObject.Properties[$Name]
    if (-not $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "The setting '$Name' is missing or empty in environment.local.json."
    }

    return [string]$property.Value
}
