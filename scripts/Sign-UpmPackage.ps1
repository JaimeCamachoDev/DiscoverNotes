param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,

    [string]$ProjectPath = ".",
    [string]$PackagePath = "Packages/com.jaimecamacho.discovernotes",
    [string]$OutputDirectory = "ReleaseArtifacts",
    [string]$CloudOrganization = $env:UNITY_CLOUD_ORGANIZATION,
    [string]$Username = $env:UNITY_USERNAME,
    [string]$Password = $env:UNITY_PASSWORD
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $UnityPath))
{
    throw "Unity executable not found: $UnityPath"
}

if (!(Test-Path $PackagePath))
{
    throw "Package path not found: $PackagePath"
}

$packageJsonPath = Join-Path $PackagePath "package.json"
$package = Get-Content -Raw -Path $packageJsonPath | ConvertFrom-Json
$packageName = $package.name
$version = $package.version

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$resolvedProjectPath = (Resolve-Path $ProjectPath).Path
$resolvedPackagePath = (Resolve-Path $PackagePath).Path
$resolvedOutputDirectory = (Resolve-Path $OutputDirectory).Path
$logFile = Join-Path $resolvedOutputDirectory "sign-upm.log"

if (Test-Path $logFile)
{
    Remove-Item -Path $logFile -Force
}

$arguments = @(
    "-batchmode",
    "-quit",
    "-projectPath", $resolvedProjectPath,
    "-upmPack", $resolvedPackagePath, $resolvedOutputDirectory,
    "-logfile", $logFile
)

if (![string]::IsNullOrWhiteSpace($CloudOrganization))
{
    $arguments += @("-cloudOrganization", $CloudOrganization)
}

if (![string]::IsNullOrWhiteSpace($Username))
{
    $arguments += @("-username", $Username)
}

if (![string]::IsNullOrWhiteSpace($Password))
{
    $arguments += @("-password", $Password)
}

Write-Host "Signing $packageName $version with Unity"
Write-Host "$UnityPath $($arguments -join ' ')"

& $UnityPath @arguments
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0)
{
    if (Test-Path $logFile)
    {
        Write-Host "Unity log tail:"
        Get-Content -Path $logFile -Tail 80
    }

    throw "Unity signing/export failed with exit code $exitCode"
}

$expectedFile = Join-Path $resolvedOutputDirectory "$packageName-$version.tgz"
if (!(Test-Path $expectedFile))
{
    throw "Signed tarball not found at $expectedFile"
}

Write-Host "Signed package exported to $expectedFile"
