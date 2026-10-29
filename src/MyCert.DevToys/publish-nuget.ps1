[CmdletBinding()]
param(
    [string]$ApiKey = $env:NUGET_API_KEY,

    [string]$Source = 'https://api.nuget.org/v3/index.json',

    [string]$Package,

    [switch]$SkipDuplicate
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactDir = Join-Path (Split-Path -Parent $projectDir) 'artifacts'

if (-not $Package) {
    $Package = Get-ChildItem $artifactDir -Filter 'MyCert.DevToys.*.nupkg' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $Package -or -not (Test-Path $Package)) {
    throw "No package found. Run pack.ps1 first, or pass -Package <path>."
}

if (-not $ApiKey) {
    $secure = Read-Host -AsSecureString 'nuget.org API key (push scope)'
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $ApiKey = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if (-not $ApiKey) {
    throw "A nuget.org API key is required."
}

Write-Host "Package: $Package"
Write-Host "Source : $Source"

$pushArgs = @('nuget', 'push', $Package, '--api-key', $ApiKey, '--source', $Source)
if ($SkipDuplicate) {
    $pushArgs += '--skip-duplicate'
}

& dotnet @pushArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet nuget push failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Published. It may take a few minutes to appear at:"
Write-Host "  https://www.nuget.org/packages/MyCert.DevToys"
