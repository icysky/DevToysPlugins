<#
.SYNOPSIS
    Rebuild, package and reinstall every DevToys plugin in this repository.

.DESCRIPTION
    Steps:
      1. Verify the .NET 10 SDK (global.json pins 10.0.403).
      2. Force-stop DevToys if it is running (it locks plugin DLLs under Plugins\<id>).
      3. Build the whole solution (skip with -SkipBuild).
      4. Run each plugin's pack.ps1 -Install:
           - pack into <repo>\artifacts\*.nupkg
           - delete %LOCALAPPDATA%\DevToys-preview\Plugins\<PackageId> and extract again.
         The installed version is NOT compared: reinstalling the same version always happens.
      5. Optionally run the smoke test suites (-Smoke) and start DevToys again (-Start).

    NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less scripts as ANSI,
    so any non-ASCII text here would be garbled (see repository README).

.PARAMETER Configuration
    Build configuration: Debug or Release (default Release).

.PARAMETER SkipBuild
    Skip the solution build. Each pack.ps1 still builds its own plugin.

.PARAMETER Smoke
    Run tools\*.Smoke suites before packaging.

.PARAMETER Start
    Start DevToys after the plugins are installed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy-all.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\deploy-all.ps1 -Smoke -Start
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipBuild,

    [switch]$Smoke,

    [switch]$Start
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Join-Path $root 'DevToysPlugins.slnx'
$devToysExe = Join-Path $env:LOCALAPPDATA 'Programs\DevToys Preview\DevToys.exe'
$pluginsRoot = Join-Path $env:LOCALAPPDATA 'DevToys-preview\Plugins'
$smokeSuites = @('GmCrypto.Smoke', 'ChineseIpsum.Smoke', 'GoogleAuthenticator.Smoke')
# PackageId == project directory name == installed folder name
$plugins = @(
    'GmCrypto.DevToys',
    'MyCert.DevToys',
    'GoogleAuthenticator.DevToys',
    'ChineseIpsum.DevToys'
)

function Write-Step {
    param([string]$Text)
    Write-Host ''
    Write-Host "=== $Text" -ForegroundColor Cyan
}

function Stop-DevToysIfRunning {
    Write-Step 'Stopping DevToys (if running)'

    if (-not (Test-Path $devToysExe)) {
        Write-Warning "DevToys.exe not found at: $devToysExe (plugin installation still works)"
    }

    $processes = @()
    try {
        $processes = @(Get-Process -Name 'DevToys' -ErrorAction SilentlyContinue)
    }
    catch {
        $processes = @()
    }

    if ($processes.Count -eq 0) {
        Write-Host '  DevToys is not running.'
        return
    }

    foreach ($process in $processes) {
        $exePath = '<unknown>'
        try {
            $exePath = $process.Path
        }
        catch {
            $exePath = '<access denied>'
        }

        Write-Host ("  Killing PID {0}: {1}" -f $process.Id, $exePath)
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            Write-Warning ("  Failed to kill PID {0}: {1}" -f $process.Id, $_.Exception.Message)
        }
    }

    # Wait until the process really exits, otherwise plugin DLLs stay locked.
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 250
        $stillRunning = @(Get-Process -Name 'DevToys' -ErrorAction SilentlyContinue)
        if ($stillRunning.Count -eq 0) {
            break
        }
    }

    if (@(Get-Process -Name 'DevToys' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'DevToys is still running. Close it manually and run this script again.'
    }

    Write-Host '  DevToys stopped.'
}

function Get-InstalledPluginInfo {
    param([string]$PluginId)

    $destination = Join-Path $pluginsRoot $PluginId
    $version = '<unknown>'
    $dllTime = '<none>'

    $nuspec = Get-ChildItem $destination -Filter '*.nuspec' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($nuspec) {
        $match = Select-String -Path $nuspec.FullName -Pattern '<version>([^<]+)</version>' |
            Select-Object -First 1
        if ($match) {
            $version = $match.Matches[0].Groups[1].Value
        }
    }

    $dll = Get-ChildItem (Join-Path $destination 'lib') -Recurse -Filter "$PluginId.dll" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($dll) {
        $dllTime = $dll.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
    }

    return [pscustomobject]@{
        Plugin   = $PluginId
        Version  = $version
        DllTime  = $dllTime
        Directory = $destination
    }
}

$startedAt = Get-Date

# ---------------------------------------------------------------- 1. SDK

Write-Step 'Checking .NET SDK'
$sdk = (& dotnet --version).Trim()
Write-Host "  dotnet: $sdk"
if (-not $sdk.StartsWith('10.')) {
    throw "Requires the .NET 10 SDK (see global.json). Current SDK: $sdk"
}

# ---------------------------------------------------------------- 2. stop DevToys

Stop-DevToysIfRunning

# ---------------------------------------------------------------- 3. build

if ($SkipBuild) {
    Write-Step 'Solution build skipped (-SkipBuild)'
}
else {
    Write-Step "Building solution ($Configuration)"
    & dotnet build $solutionPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

# ---------------------------------------------------------------- 4. smoke (optional)

if ($Smoke) {
    Write-Step "Running smoke tests ($Configuration)"
    foreach ($suite in $smokeSuites) {
        $runner = Join-Path $root "tools\$suite\run.ps1"
        if (-not (Test-Path $runner)) {
            Write-Warning "  Missing runner: $runner"
            continue
        }

        Write-Host "  -> $suite"
        & powershell -NoProfile -ExecutionPolicy Bypass -File $runner -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "$suite failed with exit code $LASTEXITCODE"
        }
    }
}

# ---------------------------------------------------------------- 5. pack + install

$results = @()
foreach ($plugin in $plugins) {
    Write-Step "Packing and installing $plugin"

    $packScript = Join-Path $root "src\$plugin\pack.ps1"
    if (-not (Test-Path $packScript)) {
        throw "Missing pack script: $packScript"
    }

    # pack.ps1 always wipes the target folder before extracting, so the same
    # version is reinstalled without any version comparison.
    & powershell -NoProfile -ExecutionPolicy Bypass -File $packScript -Configuration $Configuration -Install
    if ($LASTEXITCODE -ne 0) {
        throw "$plugin pack/install failed with exit code $LASTEXITCODE"
    }

    $results += Get-InstalledPluginInfo -PluginId $plugin
}

# ---------------------------------------------------------------- 6. summary

Write-Step 'Summary'
$results | Format-Table -AutoSize -Property Plugin, Version, DllTime

$elapsed = (Get-Date) - $startedAt
Write-Host ("Reinstalled {0} plugin(s) into: {1}" -f $results.Count, $pluginsRoot)
Write-Host ("Elapsed: {0:hh\:mm\:ss}" -f $elapsed)

if ($Start) {
    if (Test-Path $devToysExe) {
        Write-Step 'Starting DevToys'
        Start-Process $devToysExe
    }
    else {
        Write-Warning "Cannot start DevToys, executable not found: $devToysExe"
    }
}
else {
    Write-Host ''
    Write-Host 'Start DevToys to load the new plugins.'
}
