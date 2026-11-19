[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Output
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $projectDir)

if (-not $Output) {
    $Output = Join-Path $repoRoot "publish\ColorSchemer"
}

& dotnet publish (Join-Path $projectDir 'ColorSchemer.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 单文件发布只需要 exe
Remove-Item (Join-Path $Output '*.pdb') -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Output 'ColorSchemer.exe'

if (-not (Test-Path $exe)) {
    throw "ColorSchemer.exe was not produced in $Output"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Artifact: $exe ($sizeMb MB)"
