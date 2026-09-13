# LatencyPilot Release Procedure

Status: **Owner-run release contract**  
Last updated: 2026-09-13

LatencyPilot intentionally keeps GitHub Actions **test-only**. GitHub-hosted runners do not build or publish production distributions.

Release packaging and publication are explicit owner actions from a clean, up-to-date `main` checkout using:

```powershell
.\scripts\Publish-Release.ps1
```

The script builds the Windows distributions locally, launch-smoke-tests the published WinUI App, and publishes the GitHub prerelease through the authenticated GitHub CLI account.

## Why this model

The repository separates two concerns:

- GitHub Actions supplies reproducible automated correctness evidence for the permanent critical test suite.
- The repository owner explicitly builds, smoke-validates and publishes release artifacts from the exact tested `main` revision.

This avoids an implicit cloud-build/release pipeline while preserving a hard requirement that the release commit itself has green CI evidence and that the actual WinUI payload can start on the owner Windows machine before publication.

## Prerequisites

The release machine must have:

- Windows 11 x64;
- the .NET SDK version pinned by `global.json`;
- Git;
- GitHub CLI (`gh`) authenticated to the repository owner account;
- Inno Setup 6 (`ISCC.exe`).

Before publishing, update `Directory.Build.props` and `RELEASE_VERSION` together when changing the product version. `RELEASE_REVISION` is optional build-attempt metadata for a semantic product version; it does not change the Git tag name.

Published semantic versions are immutable. A version remains reserved after public publication even if its release/tag is later removed from GitHub. Do not reuse that version for different binaries.

Historical note: `v0.0.1` was previously published and is therefore reserved, even though it is no longer present on the current Releases page. Current `main` advances to `0.0.2`.

## Safety checks performed by the publisher

`Publish-Release.ps1` refuses to publish unless:

1. the current branch is exactly `main`;
2. the working tree is clean;
3. local `HEAD` exactly matches `origin/main` after fetch;
4. the requested version has exactly `MAJOR.MINOR.PATCH` form;
5. the requested version matches the project `Version` property;
6. GitHub CLI authentication is available;
7. a successful `Tests` workflow run exists for the exact release commit;
8. neither the release nor remote tag already exists;
9. the self-contained WinUI publish contains a non-empty `LatencyPilot.pri`;
10. the published `LatencyPilot.exe` opens the expected `LatencyPilot` main window and remains alive for the local startup-smoke interval without writing a startup-failure report.

These checks are release gates, not convenience warnings.

The script deliberately has no replacement/delete mode. If a version has already been published, advance the semantic version instead of replacing the public artifact identity.

The App launch smoke is deliberately narrow. It catches publish/XAML/resource/startup breakage before packaging, but it does **not** prove that the privileged Service, ETW capture, baseline quality or physical hardware evidence is correct. Those remain Stage B/C owner-local validation work.

## Build and package outputs

The script performs the release-oriented build locally:

```text
dotnet restore win-x64 graph
→ Release solution build
→ self-contained WinUI App publish
→ verify LatencyPilot.pri
→ launch-smoke the published App
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

`BUILD_INFO.txt` records the exact commit, `RELEASE_REVISION`, matching GitHub Actions Tests run, `build_mode=owner-local`, and `app_launch_smoke=passed` for the packaged payload.

## Publishing a new version

From a clean checkout at the latest `main`:

```powershell
gh auth status
.\scripts\Publish-Release.ps1
```

The script uses `RELEASE_VERSION` by default. A version can be supplied explicitly only when it still matches the repository project version:

```powershell
.\scripts\Publish-Release.ps1 -Version 0.0.2
```

The publisher uses:

```text
gh release create <tag> ... --target <exact-commit>
```

GitHub CLI creates the missing tag at the specified target commit when the tag does not already exist. The script intentionally does not use `--verify-tag`, because that option requires the remote tag to exist before release creation. This avoids the earlier GitHub Actions token failure mode where a separate tag push was rejected for workflow-changing commits.

The prerelease is explicitly published with `--latest=false`; pre-alpha validation artifacts must not become the repository's latest stable release signal.

## Relation to Stage B physical validation

A Stage B validation record must refer to the exact package it tested. Record at minimum:

- product version;
- release revision when present;
- Git commit from `BUILD_INFO.txt`;
- Tests workflow run from `BUILD_INFO.txt`;
- App launch-smoke result from `BUILD_INFO.txt`;
- setup/portable SHA-256;
- physical-machine evidence required by `PHYSICAL_VALIDATION.md`.

A green GitHub Actions test run plus a successful startup smoke still does not prove ETW correctness on the target PC or hardware-level validity. Conversely, a locally built release without green CI for the exact commit is not an approved LatencyPilot release candidate.

## Failure discipline

If publication fails after local artifacts are built, preserve the local artifacts/checksums and inspect the exact failing command. Do not weaken tests, version checks, branch checks, startup checks or release identity checks merely to make publication pass.

If publication fails after a missing tag was created as part of `gh release create`, inspect the resulting repository state before retrying. Do not delete or replace an already-public artifact merely to make the next attempt convenient. Advance the version when public identity has already escaped.

Do not attach unrelated binaries to a historical tag merely to make the Releases page look populated.
