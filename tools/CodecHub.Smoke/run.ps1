[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'CodecHub.Smoke.csproj'

$sdk = (& dotnet --version).Trim()
Write-Host "SDK: $sdk"

& dotnet run -p $project -c $Configuration
exit $LASTEXITCODE
