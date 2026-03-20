param(
    [Parameter(Mandatory = $true)]
    [string]$TarballPath
)

$ErrorActionPreference = "Stop"

function Require-Command([string]$CommandName)
{
    $null = Get-Command $CommandName -ErrorAction Stop
}

function Read-TarEntry([string]$ArchivePath, [string]$EntryPath)
{
    $content = & tar -xOf $ArchivePath $EntryPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Failed to read '$EntryPath' from tarball '$ArchivePath'."
    }

    return $content
}

function Get-PackageEntryPath([string[]]$TarEntries, [string]$RelativePath)
{
    $candidate = "package/$RelativePath"
    if ($TarEntries -contains $candidate)
    {
        return $candidate
    }

    if ($TarEntries -contains $RelativePath)
    {
        return $RelativePath
    }

    return $null
}

Require-Command npm.cmd
Require-Command tar

if (!(Test-Path $TarballPath))
{
    throw "Tarball not found: $TarballPath"
}

$resolvedTarballPath = (Resolve-Path $TarballPath).Path
$tarEntries = & tar -tf $resolvedTarballPath
if ($LASTEXITCODE -ne 0)
{
    throw "Failed to inspect tarball contents: $resolvedTarballPath"
}

$attestationEntry = Get-PackageEntryPath -TarEntries $tarEntries -RelativePath ".attestation.p7m"
if ($null -eq $attestationEntry)
{
    throw "Tarball does not contain .attestation.p7m. Expected a Unity-signed package."
}

$packageJsonEntry = Get-PackageEntryPath -TarEntries $tarEntries -RelativePath "package.json"
if ($null -eq $packageJsonEntry)
{
    throw "Tarball does not contain package.json."
}

$packageJsonRaw = Read-TarEntry -ArchivePath $resolvedTarballPath -EntryPath $packageJsonEntry
$package = $packageJsonRaw | ConvertFrom-Json
$expectedFilename = "$($package.name)-$($package.version).tgz"
if ([System.IO.Path]::GetFileName($resolvedTarballPath) -ne $expectedFilename)
{
    throw "Tarball filename does not match package metadata. Expected '$expectedFilename'."
}

Write-Host "Checking npm authentication"
$whoami = & npm.cmd whoami --registry=https://registry.npmjs.org/
if ($LASTEXITCODE -ne 0)
{
    throw "npm authentication failed."
}

Write-Host "Authenticated as $whoami"
Write-Host "Publishing $resolvedTarballPath"

& npm.cmd publish $resolvedTarballPath --registry=https://registry.npmjs.org/
if ($LASTEXITCODE -ne 0)
{
    throw "npm publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish completed"
