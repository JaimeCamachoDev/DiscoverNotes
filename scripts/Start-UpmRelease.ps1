param(
    [string]$ProjectRoot,
    [string]$Version,
    [switch]$ManualSign,
    [switch]$CreateLocalWrapper
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot))
{
    $ProjectRoot = Read-Host "Project root path"
}

if (!(Test-Path $ProjectRoot))
{
    throw "Project root not found: $ProjectRoot"
}

$resolvedProjectRoot = (Resolve-Path $ProjectRoot).Path
$releaseScript = Join-Path $resolvedProjectRoot "scripts\Release-DiscoverNotes.ps1"
$manualWrapper = Join-Path $resolvedProjectRoot "Release-DiscoverNotes-ManualSign.bat"
$normalWrapper = Join-Path $resolvedProjectRoot "Release-DiscoverNotes.bat"

if (!(Test-Path $releaseScript))
{
    throw "Release script not found at $releaseScript"
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    $Version = Read-Host "Version to release"
}

if ($CreateLocalWrapper -and !(Test-Path $manualWrapper))
{
    $wrapperContent = @"
@echo off
setlocal
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File ".\scripts\Release-DiscoverNotes.ps1" -ManualSign %*
set EXIT_CODE=%ERRORLEVEL%
echo.
if not "%EXIT_CODE%"=="0" (
  echo Release failed with exit code %EXIT_CODE%.
) else (
  echo Release finished successfully.
)
pause
endlocal
"@
    Set-Content -Path $manualWrapper -Value $wrapperContent -Encoding ASCII
}

Push-Location $resolvedProjectRoot
try
{
    if ($ManualSign -and (Test-Path $manualWrapper))
    {
        & $manualWrapper -Version $Version
        exit $LASTEXITCODE
    }

    if (!$ManualSign -and (Test-Path $normalWrapper))
    {
        & $normalWrapper -Version $Version
        exit $LASTEXITCODE
    }

    $arguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $releaseScript,
        "-Version", $Version
    )

    if ($ManualSign)
    {
        $arguments += "-ManualSign"
    }

    & powershell @arguments
    exit $LASTEXITCODE
}
finally
{
    Pop-Location
}
