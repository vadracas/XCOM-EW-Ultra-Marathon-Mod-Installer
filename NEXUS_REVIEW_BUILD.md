# Nexus Mods review build

Mod: **Ultra Marathon Mod (UMM) 1.1** for XCOM: Enemy Within / Long War 1.1

Review snapshot date: 2026-09-02

## Current executable

Filename:

`Ultra Marathon Mod Setup.exe`

Exact size:

`147427014 bytes`

SHA-256:

`D86387ADDE5B8C09DA28FB8950F7176E1DF836C8D88B2D0E1750C389BCCD85A4`

This review build supersedes the earlier UMM 1.1 quarantined installer snapshots. Nexus should review the executable matching the identity above together with this repository snapshot.

## Reproducibility

The reviewed executable is built with:

- .NET SDK 9.0.308
- MSBuild supplied by that SDK
- target `win-x64`
- self-contained single-file publishing
- `IncludeSourceRevisionInInformationalVersion=false`

`BUILDING.md` documents the complete manual procedure and `build-review.ps1` performs the same build automatically and fails unless the produced executable matches the expected byte length and SHA-256.

## Runtime payload architecture

The embedded runtime payload contains:

- UMM-authored configuration and localization payload where redistribution is appropriate;
- three binary delta patches applied only to validated Long War 1.1 baseline files;
- five map reconstruction definitions with exact patch scripts;
- six fail-closed local-baseline text transform recipes for NameList, StrategyAI and SHIVXP default/runtime configuration;
- UPKUtils 7.3 runtime tools used for UPK decompression/patching;
- the release install manifest with exact source/target hashes.

The installer does not ship complete vanilla XCOM/Firaxis package files.

For the six local-baseline transforms, the installer validates the user's supported Long War 1.1 input, reconstructs the intended UMM target in memory from hashed local baseline references plus UMM changes, validates the result, writes atomically, and post-validates it. Runtime INI transformations preserve the local `[IniVersion]` block.

## Validation behavior

The installer performs clean-baseline/hash preflight checks before mutation, creates backups of touched targets, validates embedded payload identities, applies changes, post-validates results and rolls all touched targets back if installation fails.

The executable represented by this source snapshot has also been run in headless install mode against a fresh disposable clean Long War 1.1 validation tree with:

- `PREFLIGHT=PASS`
- `INSTALL=PASS`
- process exit code `0`

## Third-party tooling

The runtime includes:

- `PatchUPK.exe`
- `DecompressLZO.exe`

from UPKUtils 7.3.

Corresponding source and license files are included in this repository. Exact upstream revision:

`f4a7149cb25389b3d3e0b4ee1bf4bfefd7b02c29`
