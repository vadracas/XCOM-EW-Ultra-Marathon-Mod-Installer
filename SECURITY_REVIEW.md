# Security review notes

This document summarizes behavior that may be relevant when reviewing `Ultra Marathon Mod Setup.exe`.

## Network behavior

The installer contains no updater, downloader, telemetry client or remote-code retrieval path.

Installation uses local files only: the embedded runtime payload, the selected XCOM: Enemy Within / Long War 1.1 installation, the user's XCOM Long War Documents configuration directory, temporary working directories and versioned local backups.

## Process execution

The installer contains two bundled third-party command-line tools from UPKUtils 7.3:

- `DecompressLZO.exe`
- `PatchUPK.exe`

They are extracted from the embedded payload to a temporary working directory and invoked only for deterministic local UPK/map reconstruction. Their expected identities are recorded in the release manifest and validated before use.

The corresponding UPKUtils source and licenses are included in this repository.

The GUI can also launch the user's local `XComEW.exe` after installation when the user explicitly chooses to start the game.

## Filesystem behavior

The installer:

- locates or accepts the user's game path;
- validates the supported clean Long War 1.1 baseline before patching;
- reads/writes only the declared game/configuration targets and installer backup/temp directories;
- creates a backup manifest and byte-for-byte backups before modification;
- writes transformed targets atomically using temporary files;
- validates target hashes/body hashes after modification;
- restores touched targets if an installation stage fails.

## Patch formats

UMM does not contain complete modified copies of core XCOM package files. Core binary changes are represented as binary delta patch data and are applied only to exact validated source hashes.

NameList, StrategyAI and SHIVXP configuration changes use declarative local-baseline transform recipes instead of complete redistributed source files. Each recipe is itself hash-validated; referenced source lines/ranges are hash-validated; generated output is validated before and after write.

## Embedded executable content

The self-contained .NET single-file bundle necessarily contains the .NET runtime/native components required by the application, in addition to the two UPKUtils executables described above.

No other downloaded or remotely supplied executable payload is used at runtime.
