param(
    [string]$Version,
    [string]$UnityPath = "E:\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe",
    [string]$CloudOrganization = "7972342390247",
    [string]$PackagePath = "Packages/com.jaimecamacho.discovernotes",
    [string]$OutputDirectory = "ReleaseArtifacts",
    [string[]]$ChangelogNotes,
    [switch]$SkipPrepare,
    [switch]$SkipSign,
    [switch]$ManualSign,
    [switch]$SkipPublish,
    [switch]$CreateGitCommit,
    [switch]$CreateGitTag,
    [switch]$PushGitTag,
    [switch]$NoPrompt,
    [switch]$AllowPlaceholderChangelog
)

$ErrorActionPreference = "Stop"

function Require-Command([string]$CommandName)
{
    $null = Get-Command $CommandName -ErrorAction Stop
}

function Get-PackageVersion([string]$PackageJsonPath)
{
    $package = Get-Content -Raw -Path $PackageJsonPath | ConvertFrom-Json
    return $package.version
}

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

function Get-ChangelogSection([string]$Path, [string]$TargetVersion)
{
    $content = Get-Content -Raw -Path $Path
    $pattern = "(?ms)^## \[$([regex]::Escape($TargetVersion))\].*?(?=^## \[|\z)"
    $match = [regex]::Match($content, $pattern)
    if (!$match.Success)
    {
        throw "CHANGELOG.md does not contain a section for version $TargetVersion"
    }

    return $match.Value.Trim()
}

function Set-ChangelogSection([string]$Path, [string]$TargetVersion, [string[]]$Notes)
{
    $content = Get-Content -Raw -Path $Path
    $pattern = "(?ms)^## \[$([regex]::Escape($TargetVersion))\].*?(?=^## \[|\z)"
    $match = [regex]::Match($content, $pattern)
    if (!$match.Success)
    {
        throw "CHANGELOG.md does not contain a section for version $TargetVersion"
    }

    $date = Get-Date -Format "yyyy-MM-dd"
    $bulletBlock = ($Notes | ForEach-Object { "- $_" }) -join "`r`n"
    $replacement = "## [$TargetVersion] - $date`r`n`r`n$bulletBlock`r`n`r`n"
    $updatedContent = $content.Substring(0, $match.Index) + $replacement + $content.Substring($match.Index + $match.Length)
    Set-Content -Path $Path -Value $updatedContent -Encoding UTF8
}

function Test-ChangelogHasPlaceholderText([string]$Section)
{
    return (
        $Section -match "Describe the main change in this release\." -or
        $Section -match "Describe any packaging or workflow change\." -or
        $Section -match "Describe any Unity-facing impact for users\."
    )
}

function Prompt-ChangelogNotes()
{
    $notes = New-Object System.Collections.Generic.List[string]

    foreach ($index in 1..3)
    {
        do
        {
            $value = Read-Host "Changelog note $index"
        }
        while ([string]::IsNullOrWhiteSpace($value))

        $notes.Add($value.Trim())
    }

    return $notes.ToArray()
}

function Test-VersionAlreadyPublished([string]$PackageName, [string]$TargetVersion)
{
    $publishedVersion = & npm.cmd view $PackageName version --registry=https://registry.npmjs.org/ 2>$null
    if ($LASTEXITCODE -ne 0)
    {
        return $false
    }

    return ($publishedVersion.Trim() -eq $TargetVersion)
}

function ConvertTo-PlainText([securestring]$SecureValue)
{
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try
    {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally
    {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Wait-ForSignedTarball([string]$TarballPath, [string]$Version)
{
    Write-Host ""
    Write-Host "Manual signing required."
    Write-Host "1. Open the project in Unity 6.3."
    Write-Host "2. Export the signed package tarball for version $Version."
    Write-Host "3. Save it to: $TarballPath"
    Write-Host "4. Return here and press ENTER to continue."
    Start-Process explorer.exe (Resolve-Path (Split-Path $TarballPath -Parent)).Path | Out-Null
    Read-Host | Out-Null

    if (!(Test-Path $TarballPath))
    {
        throw "Expected tarball not found after manual export: $TarballPath"
    }
}

Require-Command powershell
Require-Command npm.cmd
Require-Command git

$packageJsonPath = Join-Path $PackagePath "package.json"
if (!(Test-Path $packageJsonPath))
{
    throw "Missing package.json at $packageJsonPath"
}

$package = Get-Content -Raw -Path $packageJsonPath | ConvertFrom-Json
$packageName = $package.name

if ([string]::IsNullOrWhiteSpace($Version))
{
    $Version = Read-Host "Version to release"
}

$Version = Normalize-VersionInput -InputVersion $Version

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')
{
    throw "Version '$Version' is not valid semver."
}

if (!$SkipPrepare)
{
    Write-Host "Preparing release $Version"
    $prepareArguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", ".\scripts\Prepare-UpmRelease.ps1",
        "-Version", $Version,
        "-PackagePath", $PackagePath,
        "-CreateChangelogEntry"
    )

    if ($null -ne $ChangelogNotes -and $ChangelogNotes.Count -gt 0)
    {
        $prepareArguments += @("-ChangelogNotes")
        $prepareArguments += $ChangelogNotes
    }

    & powershell @prepareArguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "Prepare-UpmRelease.ps1 failed with exit code $LASTEXITCODE"
    }
}

$preparedVersion = Get-PackageVersion -PackageJsonPath $packageJsonPath
if ($preparedVersion -ne $Version)
{
    throw "package.json version is '$preparedVersion', expected '$Version'."
}

$changelogSection = Get-ChangelogSection -Path "CHANGELOG.md" -TargetVersion $Version
if (!$AllowPlaceholderChangelog -and (Test-ChangelogHasPlaceholderText -Section $changelogSection))
{
    if ($NoPrompt)
    {
        throw "CHANGELOG.md still contains placeholder text for version $Version."
    }

    Write-Host ""
    Write-Host "CHANGELOG.md contains placeholder text for version $Version."
    Write-Host "Enter three release notes to continue."

    $enteredNotes = Prompt-ChangelogNotes
    Set-ChangelogSection -Path "CHANGELOG.md" -TargetVersion $Version -Notes $enteredNotes

    & powershell -ExecutionPolicy Bypass -File ".\scripts\Prepare-UpmRelease.ps1" `
        -Version $Version `
        -PackagePath $PackagePath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Prepare-UpmRelease.ps1 failed while refreshing changelog sync."
    }

    $changelogSection = Get-ChangelogSection -Path "CHANGELOG.md" -TargetVersion $Version
    if (Test-ChangelogHasPlaceholderText -Section $changelogSection)
    {
        throw "Failed to replace placeholder changelog text for version $Version."
    }
}

$tarballPath = Join-Path $OutputDirectory "$packageName-$Version.tgz"

if (!$SkipSign)
{
    if ($ManualSign)
    {
        Wait-ForSignedTarball -TarballPath $tarballPath -Version $Version
    }
    else
    {
        if ([string]::IsNullOrWhiteSpace($env:UNITY_USERNAME) -and !$NoPrompt)
        {
            $env:UNITY_USERNAME = Read-Host "Unity username"
        }

        if ([string]::IsNullOrWhiteSpace($env:UNITY_PASSWORD) -and !$NoPrompt)
        {
            $securePassword = Read-Host "Unity password" -AsSecureString
            $env:UNITY_PASSWORD = ConvertTo-PlainText $securePassword
        }

        Write-Host "Signing package with Unity"
        powershell -ExecutionPolicy Bypass -File ".\scripts\Sign-UpmPackage.ps1" `
            -UnityPath $UnityPath `
            -PackagePath $PackagePath `
            -OutputDirectory $OutputDirectory `
            -CloudOrganization $CloudOrganization `
            -Username $env:UNITY_USERNAME `
            -Password $env:UNITY_PASSWORD
        if ($LASTEXITCODE -ne 0)
        {
            throw "Sign-UpmPackage.ps1 failed with exit code $LASTEXITCODE"
        }
    }
}

if (!(Test-Path $tarballPath))
{
    throw "Expected tarball not found: $tarballPath"
}

if (!$SkipPublish)
{
    $whoami = & npm.cmd whoami --registry=https://registry.npmjs.org/ 2>$null
    if ($LASTEXITCODE -ne 0)
    {
        throw "npm is not authenticated. Run 'npm.cmd login --registry=https://registry.npmjs.org/' first."
    }

    if (Test-VersionAlreadyPublished -PackageName $packageName -TargetVersion $Version)
    {
        throw "$packageName@$Version is already published."
    }

    Write-Host "Publishing tarball to npm as $whoami"
    powershell -ExecutionPolicy Bypass -File ".\scripts\Publish-UpmPackage.ps1" -TarballPath $tarballPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Publish-UpmPackage.ps1 failed with exit code $LASTEXITCODE"
    }
}

$tagName = "v$Version"
if ($CreateGitCommit)
{
    & git add CHANGELOG.md README.md RELEASING.md $PackagePath scripts Release-DiscoverNotes.bat Release-DiscoverNotes-ManualSign.bat .github/workflows/publish.yml .gitignore
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to stage release files."
    }

    & git commit -m "Release $Version"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to create release commit for $Version"
    }
}

if ($CreateGitTag)
{
    $existingTag = & git tag --list $tagName
    if (![string]::IsNullOrWhiteSpace($existingTag))
    {
        throw "Git tag $tagName already exists."
    }

    & git tag $tagName
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to create git tag $tagName"
    }
}

if ($PushGitTag)
{
    & git push origin $tagName
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to push git tag $tagName"
    }
}

Write-Host ""
Write-Host "Release flow completed"
Write-Host "Version: $Version"
Write-Host "Tarball: $((Resolve-Path $tarballPath).Path)"
