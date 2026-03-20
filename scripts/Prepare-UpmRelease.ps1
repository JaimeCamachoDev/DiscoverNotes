param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$PackagePath = "Packages/com.jaimecamacho.discovernotes",
    [switch]$CreateChangelogEntry,
    [string[]]$ChangelogNotes
)

$ErrorActionPreference = "Stop"

function Convert-ChangelogSectionToUpm([string]$Section)
{
    $lines = $Section -split "`r?`n"
    $converted = New-Object System.Collections.Generic.List[string]

    foreach ($line in $lines)
    {
        if ($line -match '^\s*-\s+(.*)$')
        {
            $converted.Add("- $($Matches[1])")
            continue
        }

        if ($line -match '^###\s+(.*)$')
        {
            $converted.Add("")
            $converted.Add($Matches[1].ToUpperInvariant())
            continue
        }

        if ($line -match '^##\s+')
        {
            continue
        }

        $converted.Add($line)
    }

    return ($converted -join "`n").Trim()
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

function Add-ChangelogSection([string]$Path, [string]$TargetVersion, [string[]]$Notes)
{
    $content = Get-Content -Raw -Path $Path
    $date = Get-Date -Format "yyyy-MM-dd"

    if ($null -eq $Notes -or $Notes.Count -eq 0)
    {
        $Notes = @(
            "Describe the main change in this release.",
            "Describe any packaging or workflow change.",
            "Describe any Unity-facing impact for users."
        )
    }

    $bulletBlock = ($Notes | ForEach-Object { "- $_" }) -join "`r`n"
    $newSection = "## [$TargetVersion] - $date`r`n`r`n$bulletBlock`r`n`r`n"

    $marker = "The format is based on Keep a Changelog and this project follows Semantic Versioning."
    $index = $content.IndexOf($marker)
    if ($index -lt 0)
    {
        throw "Failed to locate changelog header marker."
    }

    $insertAt = $index + $marker.Length
    $updatedContent = $content.Insert($insertAt, "`r`n`r`n$newSection")
    Set-Content -Path $Path -Value $updatedContent -Encoding UTF8
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')
{
    throw "Version '$Version' is not valid semver."
}

$packageJsonPath = Join-Path $PackagePath "package.json"
$packageReadmePath = Join-Path $PackagePath "README.md"
$rootReadmePath = "README.md"
$rootChangelogPath = "CHANGELOG.md"
$rootLicensePath = "LICENSE"

if (!(Test-Path $packageJsonPath))
{
    throw "Missing package.json at $packageJsonPath"
}

if (!(Test-Path $rootReadmePath) -or !(Test-Path $rootChangelogPath) -or !(Test-Path $rootLicensePath))
{
    throw "README.md, CHANGELOG.md and LICENSE must exist at the repository root."
}

$package = Get-Content -Raw -Path $packageJsonPath | ConvertFrom-Json
$package.version = $Version

try
{
    $null = Get-ChangelogSection -Path $rootChangelogPath -TargetVersion $Version
}
catch
{
    if (!$CreateChangelogEntry)
    {
        throw
    }

    Add-ChangelogSection -Path $rootChangelogPath -TargetVersion $Version -Notes $ChangelogNotes
}

$changelogSection = Get-ChangelogSection -Path $rootChangelogPath -TargetVersion $Version
$upmChangelog = Convert-ChangelogSectionToUpm -Section $changelogSection

if ($null -eq $package.PSObject.Properties["_upm"])
{
    $package | Add-Member -MemberType NoteProperty -Name "_upm" -Value ([pscustomobject]@{})
}

$package._upm | Add-Member -Force -MemberType NoteProperty -Name "changelog" -Value $upmChangelog

$package | ConvertTo-Json -Depth 10 | Set-Content -Path $packageJsonPath -Encoding UTF8

Copy-Item -Path $rootReadmePath -Destination $packageReadmePath -Force
Copy-Item -Path $rootChangelogPath -Destination (Join-Path $PackagePath "CHANGELOG.md") -Force
Copy-Item -Path $rootLicensePath -Destination (Join-Path $PackagePath "LICENSE") -Force

Write-Host "Prepared UPM release $Version"
Write-Host "Next step: export a signed .tgz from Unity 6.3 using scripts/Sign-UpmPackage.ps1"
