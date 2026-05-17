param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][A-Za-z0-9.]+)?$')]
    [string]$Version = "1.0.0",
    [string]$ServerUrl = "",
    [string]$ProductId = "prod_smartprinter",
    [switch]$AllowInsecureHttp,
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ChildPath([string]$Root, [string]$Child) {
    $combined = [System.IO.Path]::GetFullPath((Join-Path $Root $Child))
    $rootFull = [System.IO.Path]::GetFullPath($Root)
    $rootPrefix = $rootFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not ($combined.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase) -or $combined.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase))) {
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

    $pathDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if ($pathDotnet) {
        $candidates += $pathDotnet
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and ($sdks | Where-Object { $_ -match '^10\.' })) {
            return $candidate
        }
    }

    throw ".NET SDK 10.x is required to build this product. Install .NET 10 SDK or place dotnet.exe at %LOCALAPPDATA%\Microsoft\dotnet-codex-10\dotnet.exe."
}

function Convert-Base64UrlToBytes([string]$Value) {
    $padded = $Value.Replace('-', '+').Replace('_', '/')
    $padded += "=" * ((4 - ($padded.Length % 4)) % 4)
    return [Convert]::FromBase64String($padded)
}

function Validate-Keyset([string]$KeysetPath, [string]$ExpectedProductId) {
    if (-not (Test-Path -LiteralPath $KeysetPath)) {
        throw "Publish output is missing activation keyset: $KeysetPath"
    }

    $parsedEntries = Get-Content -Raw -LiteralPath $KeysetPath | ConvertFrom-Json
    $entries = @($parsedEntries | ForEach-Object { $_ })
    if ($entries.Count -eq 0) {
        throw "Activation keyset must contain at least one entry."
    }

    $kids = @()
    foreach ($entry in $entries) {
        $kid = [string]$entry.kid
        $key = [string]$entry.key
        if ([string]::IsNullOrWhiteSpace($kid) -or [string]::IsNullOrWhiteSpace($key)) {
            throw "Each activation keyset entry must contain non-empty kid and key fields."
        }
        if (-not $kid.StartsWith("$ExpectedProductId`_", [System.StringComparison]::Ordinal)) {
            throw "Activation keyset kid '$kid' does not match product '$ExpectedProductId'."
        }
        if ($key -notmatch '^[A-Za-z0-9_-]+$') {
            throw "Activation keyset key for '$kid' is not base64url."
        }
        $rawKey = Convert-Base64UrlToBytes $key
        if ($rawKey.Length -ne 32) {
            throw "Activation keyset key for '$kid' must decode to a 32-byte Ed25519 public key."
        }
        $kids += $kid
    }

    if ($ExpectedProductId -eq "prod_smartprinter") {
        foreach ($requiredKid in @("prod_smartprinter_v1", "prod_smartprinter_v2")) {
            if ($kids -notcontains $requiredKid) {
                throw "Activation keyset for prod_smartprinter must include '$requiredKid'."
            }
        }
    }
}

function Run-Tests([string]$DotnetPath, [string]$RepoRoot) {
    Write-Host "[2/5] Running verification tests..."
    & $DotnetPath test (Join-Path $RepoRoot "backend.Tests\backend.Tests.csproj") -c Release --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw "backend.Tests failed." }

    & $DotnetPath test (Join-Path $RepoRoot "desktop.Tests\desktop.Tests.csproj") -c Release --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) { throw "desktop.Tests failed." }
}

$RepoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$PublishDir = Resolve-ChildPath $RepoRoot "publish"
$DistDir = Resolve-ChildPath $RepoRoot "dist"
$IssPath = Resolve-ChildPath $RepoRoot "installer\myPrinter.iss"
$TemplateConfigPath = Resolve-ChildPath $RepoRoot "desktop\smartprinter.appsettings.json"
$DotnetPath = Get-DotnetCli

Write-Host "[1/5] Cleaning publish and dist output..."
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

Write-Host "[3/5] Publishing desktop app..."
& $DotnetPath publish (Join-Path $RepoRoot "desktop\MyPrinter.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    /p:PublishSingleFile=true `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$runtimeConfigPath = Join-Path $PublishDir "smartprinter.appsettings.json"
if ($ServerUrl) {
    if (($ServerUrl -notmatch '^https://') -and -not $AllowInsecureHttp.IsPresent) {
        throw "Non-HTTPS ServerUrl requires -AllowInsecureHttp."
    }

    $runtimeConfig = @{
        Activation = @{
            ServerUrl = $ServerUrl
            ProductId = $ProductId
            AllowInsecureHttp = $AllowInsecureHttp.IsPresent
        }
    } | ConvertTo-Json -Depth 3

    Set-Content -LiteralPath $runtimeConfigPath -Value $runtimeConfig -Encoding UTF8
}
elseif (-not (Test-Path -LiteralPath $runtimeConfigPath) -and (Test-Path -LiteralPath $TemplateConfigPath)) {
    Copy-Item -LiteralPath $TemplateConfigPath -Destination $runtimeConfigPath
}

if (-not (Test-Path -LiteralPath $runtimeConfigPath)) {
    throw "Publish output is missing smartprinter.appsettings.json."
}

$runtimeConfig = Get-Content -Raw -LiteralPath $runtimeConfigPath | ConvertFrom-Json
if (-not $runtimeConfig.Activation) {
    throw "smartprinter.appsettings.json is missing the Activation section."
}

$resolvedServerUrl = [string]$runtimeConfig.Activation.ServerUrl
$resolvedProductId = [string]$runtimeConfig.Activation.ProductId
$resolvedAllowInsecure = [bool]$runtimeConfig.Activation.AllowInsecureHttp

if ([string]::IsNullOrWhiteSpace($resolvedServerUrl) -or $resolvedServerUrl -match 'your-activation-server') {
    throw "Installer config still contains placeholder Activation.ServerUrl. Pass -ServerUrl with the real activation base URL."
}

if ([string]::IsNullOrWhiteSpace($resolvedProductId)) {
    throw "smartprinter.appsettings.json is missing Activation.ProductId."
}

if ($resolvedProductId -ne $ProductId) {
    throw "Published Activation.ProductId '$resolvedProductId' does not match requested product '$ProductId'."
}

if (($resolvedServerUrl -notmatch '^https://') -and -not $resolvedAllowInsecure) {
    throw "Non-HTTPS Activation.ServerUrl requires AllowInsecureHttp=true."
}

if (($resolvedServerUrl -notmatch '^https://') -and $resolvedAllowInsecure) {
    Write-Warning "Building installer with insecure activation transport: $resolvedServerUrl"
}

$keysetPath = Join-Path $PublishDir "Activation\license_keyset_$resolvedProductId.json"
$keysetFileName = Split-Path -Leaf $keysetPath
Validate-Keyset $keysetPath $resolvedProductId

$requiredFiles = @(
    (Join-Path $PublishDir "MyPrinter.exe"),
    (Join-Path $PublishDir "appsettings.json"),
    $runtimeConfigPath,
    $keysetPath,
    (Join-Path $PublishDir "frontend\index.html")
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Publish output is missing required file: $file"
    }
}

$frontendArtifacts = Get-ChildItem -LiteralPath (Join-Path $PublishDir "frontend") -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name.EndsWith(".backup", [System.StringComparison]::OrdinalIgnoreCase) -or $_.Name -eq "_fix_guide.js" }
if ($frontendArtifacts) {
    $artifactList = ($frontendArtifacts | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
    throw "Publish output contains frontend backup/dev artifacts:$([Environment]::NewLine)$artifactList"
}

Write-Host "[4/5] Resolving Inno Setup compiler..."
$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

$isccPath = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

Write-Host "[5/5] Building installer..."
& $isccPath `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$PublishDir" `
    "/DOutputDir=$DistDir" `
    "/DKeysetFileName=$keysetFileName" `
    $IssPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

Write-Host ""
Write-Host "Installer ready in $DistDir"
