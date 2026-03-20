param(
    [string]$Version,
    [string]$OutputDirectory = "ReleaseArtifacts",
    [string]$PackagePath = "Packages/com.jaimecamacho.discovernotes",
    [switch]$LoginIfNeeded
)

$ErrorActionPreference = "Stop"

function Normalize-VersionInput([string]$InputVersion)
{
    if ([string]::IsNullOrWhiteSpace($InputVersion))
    {
        return $InputVersion
    }

    $normalized = $InputVersion.Trim()
    $normalized = $normalized -replace '^(?i)version\s+', ''
    return $normalized.Trim()
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    $Version = Read-Host "Version to publish"
}

$Version = Normalize-VersionInput -InputVersion $Version

$packageJsonPath = Join-Path $PackagePath "package.json"
if (!(Test-Path $packageJsonPath))
{
    throw "Missing package.json at $packageJsonPath"
}

$package = Get-Content -Raw -Path $packageJsonPath | ConvertFrom-Json
$packageName = $package.name
$tarballPath = Join-Path $OutputDirectory "$packageName-$Version.tgz"

$whoami = & npm.cmd whoami --registry=https://registry.npmjs.org/ 2>$null
if ($LASTEXITCODE -ne 0)
{
    if (!$LoginIfNeeded)
    {
        throw "npm is not authenticated. Run 'npm.cmd login --registry=https://registry.npmjs.org/' first or rerun with -LoginIfNeeded."
    }

    Write-Host "npm login required"
    & npm.cmd login --registry=https://registry.npmjs.org/
    if ($LASTEXITCODE -ne 0)
    {
        throw "npm login failed."
    }

    $whoami = & npm.cmd whoami --registry=https://registry.npmjs.org/ 2>$null
    if ($LASTEXITCODE -ne 0)
    {
        throw "npm authentication still failed after login."
    }
}

Write-Host "Authenticated as $whoami"
Write-Host "Publishing tarball for $packageName $Version"

& powershell -ExecutionPolicy Bypass -File ".\scripts\Publish-UpmPackage.ps1" -TarballPath $tarballPath
if ($LASTEXITCODE -ne 0)
{
    throw "Publish-UpmPackage.ps1 failed with exit code $LASTEXITCODE"
}
