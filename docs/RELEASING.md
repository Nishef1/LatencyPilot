# LatencyPilot Release Procedure

Status: **Owner-run release contract**  
Last updated: 2026-09-13

LatencyPilot intentionally keeps GitHub Actions **test-only**. GitHub-hosted runners do not build or publish production distributions.

Release packaging and publication are explicit owner actions from a clean, up-to-date `main` checkout using:

```powershell
.\scripts\Publish-Release.ps1
```

The script builds the Windows distributions locally and publishes the GitHub prerelease through the authenticated GitHub CLI account.

## Why this model

The repository separates two concerns:

- GitHub Actions supplies reproducible automated correctness evidence for the permanent critical test suite.
- The repository owner explicitly builds and publishes release artifacts from the exact tested `main` revision.

This avoids an implicit cloud-build/release pipeline while preserving a hard requirement that the release commit itself has green CI evidence.

## Prerequisites

The release machine must have:

- Windows 11 x64;
- the .NET SDK version pinned by `global.json`;
- Git;
- GitHub CLI (`gh`) authenticated to the repository owner account;
- Inno Setup 6 (`ISCC.exe`).

Before publishing, update `Directory.Build.props` and `RELEASE_VERSION` together when changing the product version. `RELEASE_REVISION` is optional release-attempt/build metadata for the same semantic product version; it does not change the Git tag name.

## Safety checks performed by the publisher

`Publish-Release.ps1` refuses to publish unless:

1. the current branch is exactly `main`;
2. the working tree is clean;
3. local `HEAD` exactly matches `origin/main` after fetch;
4. the requested version has exactly `MAJOR.MINOR.PATCH` form;
5. the requested version matches the project `Version` property;
6. GitHub CLI authentication is available;
7. a successful `Tests` workflow run exists for the exact release commit;
8. an existing release/tag is not being replaced unless `-ReplaceExisting` was explicitly supplied.

These checks are release gates, not convenience warnings.

## Build and package outputs

The script performs the release-oriented build locally:

```text
dotnet restore win-x64 graph
→ Release solution build
→ self-contained WinUI App publish
→ verify LatencyPilot.pri
→ self-contained Service publish
→ prepare version/build/validation/diagnostics metadata
→ build Inno Setup EXE
→ build portable ZIP
→ calculate SHA-256 companions
→ publish GitHub prerelease
```

The output assets are:

```text
LatencyPilot-<version>-win-x64-setup.exe
LatencyPilot-<version>-win-x64-setup.exe.sha256
LatencyPilot-<version>-win-x64-portable.zip
LatencyPilot-<version>-win-x64-portable.zip.sha256
```

`BUILD_INFO.txt` records the exact commit, `RELEASE_REVISION`, matching GitHub Actions Tests run and `build_mode=owner-local`.

## Publishing a new version

From a clean checkout at the latest `main`:

```powershell
gh auth status
.\scripts\Publish-Release.ps1
```

The script uses `RELEASE_VERSION` by default. A version can be supplied explicitly only when it still matches the repository project version:

```powershell
.\scripts\Publish-Release.ps1 -Version 0.0.1
```

The publisher creates the missing tag through `gh release create --target <exact-commit>` rather than relying on a Git tag push from a GitHub Actions token.

## Replacing an existing prerelease

Replacement is destructive and therefore requires an explicit switch:

```powershell
.\scripts\Publish-Release.ps1 -ReplaceExisting
```

Without that switch, an existing release or tag is a hard error.

When replacement is explicitly requested, the script removes the existing GitHub prerelease/tag first and creates the replacement only after the new local setup, portable archive and checksums have already been built successfully.

Do not use replacement for ordinary version progression. Prefer a new semantic version when the published artifact has already become meaningful external evidence.

## Relation to Stage B physical validation

A Stage B validation record must refer to the exact package it tested. Record at minimum:

- product version;
- release revision when present;
- Git commit from `BUILD_INFO.txt`;
- Tests workflow run from `BUILD_INFO.txt`;
- setup/portable SHA-256;
- physical-machine evidence required by `PHYSICAL_VALIDATION.md`.

A green GitHub Actions test run alone does not prove packaging, ETW correctness on the target PC or hardware-level validity. Conversely, a locally built release without green CI for the exact commit is not an approved LatencyPilot release candidate.

## Failure discipline

If publication fails after local artifacts are built, preserve the local artifacts/checksums and inspect the exact failing command. Do not weaken tests, version checks, branch checks or release identity checks merely to make publication pass.

If a replacement attempt has already removed the previous prerelease/tag, restore publication only from an artifact set whose commit/version/build metadata is known. Do not attach unrelated binaries to a tag merely to make the Releases page look populated.
