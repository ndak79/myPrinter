param(
    [string]$Version = "1.0.0",
    [string]$ServerUrl = "",
    [string]$ProductId = "prod_smartprinter",
    [switch]$AllowInsecureHttp
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PublishDir = Join-Path $RepoRoot "publish"
$DistDir = Join-Path $RepoRoot "dist"
$IssPath = Join-Path $RepoRoot "installer\myPrinter.iss"
$TemplateConfigPath = Join-Path $RepoRoot "desktop\smartprinter.appsettings.json"

Write-Host "[1/4] Cleaning publish and dist output..."
if (Test-Path $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
if (Test-Path $DistDir) { Remove-Item -LiteralPath $DistDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Path $DistDir | Out-Null

Write-Host "[2/4] Publishing desktop app..."
dotnet publish (Join-Path $RepoRoot "desktop\MyPrinter.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    /p:PublishSingleFile=true `
    -o $PublishDir

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

    Set-Content -LiteralPath $runtimeConfigPath -Value $runtimeConfig
}
elseif (-not (Test-Path $runtimeConfigPath) -and (Test-Path $TemplateConfigPath)) {
    Copy-Item -LiteralPath $TemplateConfigPath -Destination $runtimeConfigPath
}

if (-not (Test-Path $runtimeConfigPath)) {
    throw "Publish output is missing smartprinter.appsettings.json."
}

$runtimeConfig = Get-Content -Raw $runtimeConfigPath | ConvertFrom-Json
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

if (($resolvedServerUrl -notmatch '^https://') -and -not $resolvedAllowInsecure) {
    throw "Non-HTTPS Activation.ServerUrl requires AllowInsecureHttp=true."
}

if (($resolvedServerUrl -notmatch '^https://') -and $resolvedAllowInsecure) {
    Write-Warning "Building installer with insecure activation transport: $resolvedServerUrl"
}

$requiredFiles = @(
    (Join-Path $PublishDir "MyPrinter.exe"),
    (Join-Path $PublishDir "appsettings.json"),
    (Join-Path $PublishDir "smartprinter.appsettings.json"),
    (Join-Path $PublishDir "Activation\license_keyset_prod_smartprinter.json"),
    (Join-Path $PublishDir "frontend\index.html")
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Publish output is missing required file: $file"
    }
}

Write-Host "[3/4] Resolving Inno Setup compiler..."
$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)

$isccPath = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 or add ISCC.exe to PATH."
}

Write-Host "[4/4] Building installer..."
& $isccPath `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$PublishDir" `
    "/DOutputDir=$DistDir" `
    $IssPath

Write-Host ""
Write-Host "Installer ready in $DistDir"
