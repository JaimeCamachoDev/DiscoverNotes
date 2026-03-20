# Releasing DiscoverNotes

This repository uses a Unity 6.3 signing-first workflow for npm releases.

## Fast path

Use the manual-sign wrapper:

```powershell
.\Release-DiscoverNotes-ManualSign.bat -Version 1.2.5
```

Use the publish-only wrapper when the signed `.tgz` already exists:

```powershell
.\Publish-SignedTarball.bat -Version 1.2.5
```

Default assumptions in the wrapper:

- Unity path: `E:\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe`
- Cloud organization id: `7972342390247`
- Package path: `Packages/com.jaimecamacho.discovernotes`
- Output folder: `ReleaseArtifacts`

Optional switches:

- `-ManualSign`
- `-CreateGitTag`
- `-PushGitTag`
- `-CreateGitCommit`
- `-SkipPrepare`
- `-SkipSign`
- `-SkipPublish`

If the target version does not exist in `CHANGELOG.md`, the release script creates a new section automatically.
If `UNITY_USERNAME` or `UNITY_PASSWORD` are not set, the release script prompts for them before signing.
The manual-sign wrapper skips CLI signing and waits for you to export the `.tgz` from the Unity Editor UI.
The release script blocks publication if the changelog section still contains placeholder text.

## Why this workflow exists

Unity 6.3 shows `Missing Signature` for scoped registry packages that were published without a Unity-signed UPM tarball. A plain `npm publish` from CI is enough to distribute the package, but it does not produce the Unity signature that removes the warning.

The workflow below keeps the scoped registry catalog in Unity Package Manager and matches the practical model used by projects such as Keijiro's packages:

1. Prepare metadata in Git.
2. Export a signed `.tgz` from Unity 6.3.
3. Publish that signed tarball to npm.

## Prerequisites

- Unity 6.3 installed locally.
- npm account with publish access to `com.jaimecamacho.discovernotes`.
- `npm.cmd whoami` must work on the machine that publishes.
- Unity credentials with permission to sign for organization `7972342390247` if using CLI signing.

## Release steps

### 1. Prepare the package metadata

```powershell
./scripts/Prepare-UpmRelease.ps1 -Version 1.2.5
```

This does four things:

- updates `Packages/com.jaimecamacho.discovernotes/package.json`
- copies `README.md` into the package
- copies `CHANGELOG.md` into the package
- copies `LICENSE` into the package
- writes `_upm.changelog` into `package.json`
- creates a changelog section automatically if it does not exist yet

### 2. Commit and tag

Commit the release changes and create the Git tag:

```powershell
git add README.md CHANGELOG.md LICENSE Packages/com.jaimecamacho.discovernotes scripts RELEASING.md .github/workflows/publish.yml
git commit -m "Release 1.2.5"
git tag v1.2.5
git push origin main --tags
```

### 3. Export the signed tarball from Unity 6.3

```powershell
./scripts/Sign-UpmPackage.ps1 `
  -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" `
  -CloudOrganization "your-unity-cloud-org"
```

Optional environment variables supported by the script:

- `UNITY_CLOUD_ORGANIZATION`
- `UNITY_USERNAME`
- `UNITY_PASSWORD`

The signed tarball is written to `ReleaseArtifacts/`.

If CLI signing does not work for your account or org, export the signed tarball from the Unity Editor UI and save it into `ReleaseArtifacts/` with the expected name:

```text
ReleaseArtifacts/com.jaimecamacho.discovernotes-1.2.5.tgz
```

### 4. Publish the signed tarball to npm

```powershell
./scripts/Publish-UpmPackage.ps1 -TarballPath "ReleaseArtifacts/com.jaimecamacho.discovernotes-1.2.5.tgz"
```

The publish script validates that:

- the tarball exists,
- the tarball contains `.attestation.p7m`,
- the tarball filename matches the embedded `package.json` name/version.

### 5. All-in-one command

```powershell
.\Release-DiscoverNotes-ManualSign.bat -Version 1.2.5
```

Publish only:

```powershell
.\Publish-SignedTarball.bat -Version 1.2.5
```

Optional commit and tag creation:

```powershell
.\Release-DiscoverNotes-ManualSign.bat -Version 1.2.5 -CreateGitCommit -CreateGitTag -PushGitTag
```

## Automation split

### GitHub Actions

GitHub only validates package metadata and sync:

- `package.json` must parse
- package name must be a valid UPM reverse-domain name
- package version must be valid semver
- package README must match root README
- `CHANGELOG.md` must include the package version

### Local Unity signing

Unity signing is kept local because that is the part that produces the Unity 6.3 signature for scoped registry consumption.

## Team recommendation

For a team workflow, keep one release owner machine with:

- Unity 6.3
- npm publish rights
- working Unity account/org access

This keeps the release path deterministic and avoids each developer needing publish credentials.
