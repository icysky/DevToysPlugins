[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'GmCrypto.Smoke.csproj'

$sdk = (& dotnet --version).Trim()
Write-Host "SDK: $sdk"
if (-not $sdk.StartsWith('10.')) {
    throw "Requires the .NET 10 SDK (see global.json). Current SDK: $sdk"
}

& dotnet run --project $project -c $Configuration
exit $LASTEXITCODE
