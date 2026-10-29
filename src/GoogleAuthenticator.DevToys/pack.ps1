[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version,

    [switch]$Install,

    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $projectDir 'GoogleAuthenticator.DevToys.csproj'
$artifactDir = Join-Path (Split-Path -Parent (Split-Path -Parent $projectDir)) 'artifacts'
$packageId = 'GoogleAuthenticator.DevToys'

$sdk = (& dotnet --version).Trim()
Write-Host "SDK: $sdk"
if (-not $sdk.StartsWith('10.')) {
    throw "Requires the .NET 10 SDK (see global.json). Current SDK: $sdk"
}

if (-not $NoClean) {
    Remove-Item (Join-Path $projectDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $projectDir 'obj') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $artifactDir "$packageId.*.nupkg") -Force -ErrorAction SilentlyContinue
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

$package = Get-ChildItem $artifactDir -Filter "$packageId.*.nupkg" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "No nupkg was produced in $artifactDir"
}

Write-Host "Package: $($package.FullName)"

# Validate DevToys extension layout and metadata.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })

    $dllEntry = $entryNames |
        Where-Object { $_ -match "^lib/[^/]+/$([regex]::Escape($packageId))\.dll$" } |
        Select-Object -First 1

    if (-not $dllEntry) {
        throw "Package does not contain lib/<tfm>/$packageId.dll"
    }

    $null = $dllEntry -match "^(lib/[^/]+)/"
    $libPrefix = $Matches[1]

    $required = @(
        "$libPrefix/$packageId.dll",
        "$libPrefix/zxing.dll",
        'README.md'
    )

    foreach ($name in $required) {
        if ($entryNames -notcontains $name) {
            throw "Package is missing required entry: $name"
        }
    }

    $forbidden = @(
        "$libPrefix/DevToys.Api.dll",
        "$libPrefix/System.ComponentModel.Composition.dll",
        "$libPrefix/SixLabors.ImageSharp.dll"
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
    $dest = Join-Path $env:LOCALAPPDATA "DevToys-preview\Plugins\$packageId"
    $installRoot = Join-Path $env:LOCALAPPDATA 'DevToys-preview'

    # Path reminder for people running this script in a different environment.
    if (-not (Test-Path $installRoot)) {
        Write-Warning "Directory not found: $installRoot. Is DevToys Preview installed for this user?"
    }

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
    Write-Host "Start DevToys and open 'Authenticator' (group: Generators)."
}

Write-Host ""
Write-Host "Artifact: $($package.FullName)"
