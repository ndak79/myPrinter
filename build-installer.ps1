param(
    [string]$Version = "",
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ChildPath([string]$Root, [string]$Child) {
    $combined = [System.IO.Path]::GetFullPath((Join-Path $Root $Child))
    $rootFull = [System.IO.Path]::GetFullPath($Root)
    $rootPrefix = $rootFull.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (-not (
            $combined.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase) -or
            $combined.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase))) {
        throw "Resolved path escapes repository root: $combined"
    }

    return $combined
}

function Get-DotnetCli {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $localDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet-codex-10\dotnet.exe"
        if (Test-Path -LiteralPath $localDotnet) {
            $candidates += $localDotnet
        }
    }

    $pathDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Source -First 1
    if ($pathDotnet) {
        $candidates += $pathDotnet
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and ($sdks | Where-Object { $_ -match '^10\.' })) {
            return $candidate
        }
    }

    throw ".NET SDK 10.x is required to build Smart Printer. Install .NET 10 SDK or place dotnet.exe at %LOCALAPPDATA%\Microsoft\dotnet-codex-10\dotnet.exe."
}

function Get-NextInstallerVersion([string]$DistDir) {
    $baseVersion = [version]"1.0.0"
    if (Test-Path -LiteralPath $DistDir) {
        $versions = Get-ChildItem -LiteralPath $DistDir -Filter "smartPrinter-setup-*.exe" -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                $name = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
                $raw = $name -replace '^smartPrinter-setup-', ''
                $parsed = $null
                if ([version]::TryParse($raw, [ref]$parsed)) { $parsed }
            } |
            Sort-Object

        if ($versions) {
            $baseVersion = $versions[-1]
        }
    }

    return "{0}.{1}.{2}" -f $baseVersion.Major, $baseVersion.Minor, ($baseVersion.Build + 1)
}

function Run-Tests([string]$DotnetPath, [string]$RepoRoot) {
    Write-Host "[2/4] Running verification tests..."

    & $DotnetPath test (Join-Path $RepoRoot "backend.Tests\backend.Tests.csproj") `
        -c Release --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw "backend.Tests failed." }

    & $DotnetPath test (Join-Path $RepoRoot "desktop.Tests\desktop.Tests.csproj") `
        -c Release --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw "desktop.Tests failed." }
}

$RepoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$PublishDir = Resolve-ChildPath $RepoRoot "publish"
$DistDir = Resolve-ChildPath $RepoRoot "dist"
$IssPath = Resolve-ChildPath $RepoRoot "installer\myPrinter.iss"
$IconPath = Resolve-ChildPath $RepoRoot "desktop\app.ico"
$DotnetPath = Get-DotnetCli

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-NextInstallerVersion $DistDir
}

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][A-Za-z0-9.]+)?$') {
    throw "Version '$Version' must match semantic version format, for example 1.0.1."
}

Write-Host "Building installer version $Version"

Write-Host "[1/4] Cleaning publish and dist output..."
if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
if (Test-Path -LiteralPath $DistDir) { Remove-Item -LiteralPath $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Path $DistDir | Out-Null

if (-not $SkipTests.IsPresent) {
    Run-Tests $DotnetPath $RepoRoot
}
else {
    Write-Warning "Skipping tests because -SkipTests was supplied."
}

Write-Host "[3/4] Publishing desktop app..."
& $DotnetPath publish (Join-Path $RepoRoot "desktop\MyPrinter.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    /p:PublishSingleFile=true `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$requiredFiles = @(
    (Join-Path $PublishDir "MyPrinter.exe"),
    (Join-Path $PublishDir "appsettings.json"),
    (Join-Path $PublishDir "frontend\index.html")
)

if (-not (Test-Path -LiteralPath $IconPath)) {
    throw "Installer icon is missing: $IconPath"
}

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Publish output is missing required file: $file"
    }
}

$frontendTestsSegment = "frontend\tests"
$frontendArtifacts = Get-ChildItem -LiteralPath (Join-Path $PublishDir "frontend") -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name.EndsWith(".backup", [System.StringComparison]::OrdinalIgnoreCase) -or
        $_.Name -eq "_fix_guide.js" -or
        $_.FullName.IndexOf($frontendTestsSegment, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    }
if ($frontendArtifacts) {
    $artifactList = ($frontendArtifacts | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
    throw "Publish output contains frontend backup/dev artifacts:$([Environment]::NewLine)$artifactList"
}

Write-Host "[4/4] Resolving Inno Setup compiler..."
$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

$isccPath = $isccCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1
if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

Write-Host "Building installer..."
& $isccPath `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$PublishDir" `
    "/DOutputDir=$DistDir" `
    "/DAppIconFile=$IconPath" `
    $IssPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

Write-Host ""
Write-Host "Installer ready in $DistDir"
