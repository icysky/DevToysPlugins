[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'GoogleAuthenticator.Smoke.csproj'

$sdk = (& dotnet --version).Trim()
Write-Host "SDK: $sdk"
$sdkMajor = 0
if ($sdk -match '^(\d+)\.') { $sdkMajor = [int]$Matches[1] }
if ($sdkMajor -lt 10) {
    throw "Requires the .NET 10 SDK or newer (see global.json). Current SDK: $sdk"
}

& dotnet run --project $project -c $Configuration
exit $LASTEXITCODE
