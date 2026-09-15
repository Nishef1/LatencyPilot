# LatencyPilot Release Procedure

Status: **Owner-run release contract**  
Last updated: 2026-09-15

LatencyPilot keeps GitHub Actions **test-only**. Hosted CI runs the permanent critical suite; it does not publish the Windows App/Service, create production distributions, run GUI smoke, sign binaries, upload release assets or prove physical hardware behavior.

Release compilation, packaging and publication are explicit owner actions from a clean, up-to-date `main` checkout:

```powershell
.\scripts\Publish-Release.ps1
```

## Evidence model

A release has three separate evidence layers:

1. **Hosted Tests** — deterministic repository/source contracts on the exact commit.
2. **Owner-local Windows release build** — restore/build/publish, WinUI resource presence, launch smoke, packaging, optional signing and package hashing.
3. **Physical supported-machine validation** — Service/ETW/device behavior, mutation/recovery gates, UI/accessibility, upgrade/uninstall and representative hardware evidence.

None of these substitutes for the others.

## Prerequisites

The release machine must have:

- Windows 11 x64;
- the .NET SDK pinned by `global.json`;
- Git;
- authenticated GitHub CLI (`gh`);
- Inno Setup 6 (`ISCC.exe`);
- for signed/final releases, a current Windows SDK containing `SignTool.exe` and an accessible Authenticode code-signing certificate.

`Directory.Build.props` and `RELEASE_VERSION` must change together when the semantic product version changes. `RELEASE_REVISION` is optional build-attempt metadata and does not change the tag.

Published versions are immutable. Never reuse a published semantic version for different binaries.

## Exact publisher gates

`Publish-Release.ps1` refuses publication unless:

1. branch is exactly `main`;
2. working tree is clean;
3. local `HEAD` exactly equals fetched `origin/main`;
4. version is exact `MAJOR.MINOR.PATCH` and matches the project `Version`;
5. GitHub CLI authentication is valid;
6. the exact commit has a successful **Tests** workflow;
7. release/tag identity is unused;
8. owner-local Release restore/build/publish succeeds;
9. self-contained WinUI output contains non-empty `LatencyPilot.pri`;
10. the published App opens the expected `LatencyPilot` main window and survives the startup-smoke interval without a startup-failure report;
11. packaging completes from the explicit payload;
12. final releases are Authenticode-signed and verified.

The App launch smoke is intentionally narrow. It does not prove privileged Service, ETW, physical mutation, performance improvement or accessibility correctness.

## Signing contract

Prereleases may remain unsigned when signing credentials are intentionally unavailable. A final release may not.

Signing configuration uses:

```powershell
$env:LATENCYPILOT_SIGNING_CERT_SHA1 = '<40-hex certificate thumbprint>'
$env:LATENCYPILOT_TIMESTAMP_URL = 'https://<rfc3161-service>'
```

Then publish a signing-required prerelease with:

```powershell
.\scripts\Publish-Release.ps1 -RequireSigning
```

or a final release with:

```powershell
.\scripts\Publish-Release.ps1 -FinalRelease
```

`-FinalRelease` implicitly requires signing. The publisher signs LatencyPilot-owned App/Service PE files and the final setup EXE, then verifies their Authenticode signatures with SignTool. The signing invocation uses SHA-256 file digests and RFC 3161 timestamps with SHA-256 timestamp digests:

```text
/fd SHA256 /tr <timestamp-url> /td SHA256
```

Do not claim a release is signed from source existence alone. The owner-local release record must contain successful signing/verification evidence from the exact package build.

## Build and package flow

The owner-local publisher performs:

```text
exact-main + clean-tree + exact-green-Tests gate
→ win-x64 restore/build
→ self-contained WinUI App publish
→ optional Authenticode sign/verify of LatencyPilot-owned App files
→ WinUI .pri verification + App launch smoke
→ self-contained Service publish
→ optional Authenticode sign/verify of LatencyPilot-owned Service files
→ explicit release/support payload assembly
→ BUILD_INFO.txt
→ deterministic PAYLOAD_SHA256.txt over the canonical payload tree
→ Inno Setup build
→ optional Authenticode sign/verify of setup EXE
→ setup SHA-256 companion
→ portable ZIP + SHA-256 companion
→ GitHub prerelease or final release publication
```

Release assets are:

```text
LatencyPilot-<version>-win-x64-setup.exe
LatencyPilot-<version>-win-x64-setup.exe.sha256
LatencyPilot-<version>-win-x64-portable.zip
LatencyPilot-<version>-win-x64-portable.zip.sha256
```

`PAYLOAD_SHA256.txt` is the canonical pre-packaging payload manifest. The setup and portable archive each additionally have their own package-level SHA-256 companion, which is the integrity identity for the distributed artifact itself.

`BUILD_INFO.txt` records:

- product version and optional release revision;
- exact Git commit;
- exact successful Tests workflow run;
- release channel;
- signing mode;
- payload manifest identity;
- owner-local build mode;
- self-contained deployment mode;
- App launch-smoke result.

## Recovery-aware upgrade and uninstall

Recovery tooling must never be overwritten or removed while LatencyPilot still owns a retained/unresolved machine change.

Installer upgrades therefore run the installed Service's read-only `--check-uninstall` journal safety check **before** payload replacement. When the Service is running, setup stops it and performs the same check again after shutdown so a last-moment journal transition cannot be missed.

Portable/manual Service replacement has the same double-check in `Install-Service.ps1` before the protected `%ProgramFiles%\LatencyPilot\Service` payload is replaced.

Uninstall remains fail-closed: `Uninstall-Service.ps1` requires `LATENCYPILOT_UNINSTALL_SAFE_V1`, stops the Service, checks again, and only then removes the Service registration and protected recovery payload.

A blocked upgrade/uninstall is a safety outcome, not a packaging failure to bypass. Restore Baseline/recover the journal first.

## Diagnostics support bundle

Packaged distributions include `Export-Diagnostics.ps1`. It creates a local ZIP only; it performs no upload. The exporter reads a bounded number of recent structured App/Service log records and writes only an explicit safe-field allowlist. Rendered messages, exception text, device inventory and evidence artifacts are excluded by default.

Example:

```powershell
.\Export-Diagnostics.ps1
```

The output is support evidence, not benchmark evidence and not a substitute for the evidence JSON/verifier path.

## Publishing examples

Prerelease, default repository version:

```powershell
gh auth status
.\scripts\Publish-Release.ps1
```

Explicit matching version:

```powershell
.\scripts\Publish-Release.ps1 -Version 0.0.2
```

Signed release candidate:

```powershell
.\scripts\Publish-Release.ps1 -Version 0.0.2 -RequireSigning
```

Final release after every physical 1.0 gate is actually closed:

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.0 -FinalRelease
```

Do **not** use the final-release switch merely because source work is complete. Physical validation, signing credentials and release evidence must all exist for the exact final package.

## Stage B / physical release record

Record at minimum:

- product version and release revision;
- exact Git commit;
- exact green Tests workflow run;
- owner-local App/Service build result;
- App launch-smoke result;
- signing and signature-verification result when required;
- setup and portable SHA-256 values;
- upgrade/uninstall safety result;
- required physical-machine evidence from `PHYSICAL_VALIDATION.md` and `PHASE3_PHYSICAL_VALIDATION.md`.

A green hosted test run is never package/signing/hardware proof. An owner-local build without exact-commit green hosted tests is also not an approved release candidate.

## Failure discipline

Do not weaken branch, identity, tests, recovery, signing, startup or version gates to make publication succeed. If publication fails after artifacts are built, preserve the exact artifacts/checksums and diagnose the failing command. If a public release/tag identity has escaped, advance the semantic version rather than replacing historical binaries.
