[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'Cron.Smoke.csproj'

$sdk = (& dotnet --version).Trim()
Write-Host "SDK: $sdk"

& dotnet run --project $project -c $Configuration
exit $LASTEXITCODE
