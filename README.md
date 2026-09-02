# Ultra Marathon Mod (UMM) 1.1 - Nexus review source

This repository contains the complete source and build inputs for the current **Ultra Marathon Mod (UMM) 1.1** installer for **XCOM: Enemy Within + Long War + Long War 1.1**.

It is prepared specifically for Nexus Mods manual security review.

## Review entry points

- [`BUILDING.md`](BUILDING.md) - exact build requirements and one-command reproducible build.
- [`NEXUS_REVIEW_BUILD.md`](NEXUS_REVIEW_BUILD.md) - exact executable identity and review notes.
- [`SECURITY_REVIEW.md`](SECURITY_REVIEW.md) - security-relevant installer behavior and embedded tooling.

## Repository contents

- `SetupFrontend/UMMSetup/` - complete WinForms installer frontend source.
- `InstallerCore/UMMInstallerCore/` - complete installer core source, including preflight validation, backup/rollback, local-baseline text transforms, runtime INI handling, binary delta application and map reconstruction.
- `SetupFrontend/UMMSetup/Resources/RuntimePayload/` - exact runtime payload embedded in the reviewed installer.
- `SetupFrontend/UMMSetup/Resources/UPKUtilsSource/` - browsable corresponding source for the bundled UPKUtils 7.3 tools.
- `SetupFrontend/UMMSetup/Resources/UPKUtils-v7.3-source.zip` - archive copy of the same upstream source revision.
- installer artwork, audio, notices, binary patch data, map patch scripts and declarative configuration transform recipes required to reproduce the reviewed installer.

## Game files

The repository does **not** contain complete vanilla XCOM/Firaxis package files. UMM applies validated patch data and local-baseline transformations to a user-supplied supported installation.

The installer requires an existing Long War 1.1 installation at runtime, but no XCOM installation is required to compile the installer.

## Current review executable

Filename: `Ultra Marathon Mod Setup.exe`

Size: `147427014` bytes

SHA-256: `D86387ADDE5B8C09DA28FB8950F7176E1DF836C8D88B2D0E1750C389BCCD85A4`

The build script fails if the produced executable is not byte-for-byte identical to this identity.
