param(
    [string]$PackagePath = "Packages/com.jaimecamacho.discovernotes"
)

$ErrorActionPreference = "Stop"

function Fail([string]$Message)
{
    throw $Message
}

if (!(Test-Path $PackagePath))
{
    Fail "Package path not found: $PackagePath"
}

$packageJsonPath = Join-Path $PackagePath "package.json"
$packageReadmePath = Join-Path $PackagePath "README.md"
$rootReadmePath = "README.md"
$rootChangelogPath = "CHANGELOG.md"

if (!(Test-Path $packageJsonPath))
{
    Fail "Missing package.json at $packageJsonPath"
}

$package = Get-Content -Raw -Path $packageJsonPath | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($package.name))
{
    Fail "package.json is missing 'name'."
}

if ($package.name -notmatch '^com\.[a-z0-9\-]+(\.[a-z0-9\-]+)+$')
{
    Fail "Package name '$($package.name)' is not a valid Unity reverse-domain package name."
}

if ([string]::IsNullOrWhiteSpace($package.version))
{
    Fail "package.json is missing 'version'."
}

if ($package.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')
{
    Fail "Package version '$($package.version)' is not valid semver."
}

if (!(Test-Path $rootReadmePath))
{
    Fail "Missing root README.md."
}

if (!(Test-Path $rootChangelogPath))
{
    Fail "Missing root CHANGELOG.md."
}

if (!(Test-Path $packageReadmePath))
{
    Fail "Missing package README.md. Run scripts/Prepare-UpmRelease.ps1."
}

$rootReadme = Get-Content -Raw -Path $rootReadmePath
$packageReadme = Get-Content -Raw -Path $packageReadmePath

if ($rootReadme -ne $packageReadme)
{
    Fail "Package README.md is not synced with root README.md. Run scripts/Prepare-UpmRelease.ps1."
}

$changelog = Get-Content -Raw -Path $rootChangelogPath
$versionHeader = "## [$($package.version)]"
if ($changelog -notmatch [regex]::Escape($versionHeader))
{
    Fail "CHANGELOG.md does not contain an entry for version $($package.version)."
}

Write-Host "UPM package validation passed for $($package.name) $($package.version)"
