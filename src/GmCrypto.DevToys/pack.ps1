[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version,

    [switch]$Install,

    [switch]$NoClean
)

# NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less scripts as ANSI,
# so any non-ASCII text here would be garbled (see repository README).
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'GmCrypto.DevToys.csproj'
# src\<project> -> solution root; nupkg output goes to <root>\artifacts
$artifactDir = Join-Path (Split-Path -Parent (Split-Path -Parent $projectDir)) 'artifacts'

$sdk = (& dotnet --version).Trim()
Write-Host "SDK: $sdk"
if (-not $sdk.StartsWith('10.')) {
    throw "Requires the .NET 10 SDK (see global.json). Current SDK: $sdk"
}

if (-not $NoClean) {
    Remove-Item (Join-Path $projectDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $projectDir 'obj') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $artifactDir "GmCrypto.DevToys.*.nupkg") -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$packArgs = @('pack', $project, '-c', $Configuration, '-o', $artifactDir)
if ($Version) {
    $packArgs += "-p:Version=$Version"
}

& dotnet @packArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE"
}

$package = Get-ChildItem $artifactDir -Filter 'GmCrypto.DevToys.*.nupkg' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "No nupkg was produced in $artifactDir"
}

Write-Host "Package: $($package.FullName)"

# Validate the DevToys extension layout and metadata.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })

    $dllEntry = $entryNames |
        Where-Object { $_ -match '^lib/[^/]+/GmCrypto\.DevToys\.dll$' } |
        Select-Object -First 1

    if (-not $dllEntry) {
        throw "Package does not contain lib/<tfm>/GmCrypto.DevToys.dll"
    }

    $null = $dllEntry -match '^(lib/[^/]+)/GmCrypto\.DevToys\.dll$'
    $libPrefix = $Matches[1]

    # No third-party runtime dependencies: GB18030 uses the CodePages assembly
    # that ships inside the .NET 8 shared framework.
    $required = @(
        "$libPrefix/GmCrypto.DevToys.dll",
        'README.md'
    )

    foreach ($name in $required) {
        if ($entryNames -notcontains $name) {
            throw "Package is missing required entry: $name"
        }
    }

    $forbidden = @(
        "$libPrefix/DevToys.Api.dll",
        "$libPrefix/System.ComponentModel.Composition.dll"
    )

    foreach ($name in $forbidden) {
        if ($entryNames -contains $name) {
            throw "Package unexpectedly contains host assembly: $name"
        }
    }

    $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
    if (-not $nuspecEntry) {
        throw "Package is missing a .nuspec file"
    }

    $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
    try {
        $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    if ($nuspec -notmatch '<tags>[^<]*devtoys-app') {
        throw "Package tags must contain 'devtoys-app' for DevToys discovery"
    }

    Write-Host "Validation: OK"
}
finally {
    $zip.Dispose()
}

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA 'DevToys-preview\Plugins\GmCrypto.DevToys'

    $running = Get-Process DevToys -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping DevToys to release plugin files..."
        $running | Stop-Process -Force
        Start-Sleep -Seconds 2
    }

    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $dest)

    Write-Host "Installed to: $dest"
    Write-Host "Start DevToys and look for SM3 Hash / SM4 Cipher / SM2 Signature / SM2 Cipher under Encoders & Decoders."
}

Write-Host ""
Write-Host "Done. Package:"
Write-Host "  $($package.FullName)"
