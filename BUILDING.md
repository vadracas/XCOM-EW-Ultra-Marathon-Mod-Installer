# Building Ultra Marathon Mod Setup

This repository is the canonical source snapshot for the current UMM 1.1 Nexus review build.

## Requirements

- Windows 10 or Windows 11 x64
- .NET SDK **9.0.308**
- PowerShell 5.1 or newer

`global.json` pins the SDK to 9.0.308 with roll-forward disabled because the self-contained .NET runtime pack is part of byte-for-byte reproducibility.

No XCOM installation is required to compile the installer. XCOM: Enemy Within + Long War + Long War 1.1 are required only to execute an actual installation.

## One-command review build

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-review.ps1
```

The script:

1. verifies that .NET SDK 9.0.308 is active;
2. verifies that the corresponding UPKUtils source and required license files are present;
3. removes stale `bin` / `obj` build output;
4. restores the .NET project;
5. publishes the Windows x64 self-contained single-file installer;
6. calculates the output byte length and SHA-256;
7. fails unless the output is byte-for-byte identical to the current Nexus review executable.

Expected output:

`SetupFrontend\UMMSetup\bin\Release\net9.0-windows\win-x64\publish\Ultra Marathon Mod Setup.exe`

Expected identity:

- bytes: `147427014`
- SHA-256: `D86387ADDE5B8C09DA28FB8950F7176E1DF836C8D88B2D0E1750C389BCCD85A4`

## Manual build

From the repository root:

```powershell
dotnet restore .\SetupFrontend\UMMSetup\UMMSetup.csproj

dotnet publish .\SetupFrontend\UMMSetup\UMMSetup.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:IncludeSourceRevisionInInformationalVersion=false
```

The project is configured as:

- `net9.0-windows`
- Windows Forms
- `win-x64`
- self-contained
- single-file executable
- trimming disabled
- single-file compression disabled
- native libraries included for self-extraction
- deterministic managed build
- debug symbols disabled

## Why `IncludeSourceRevisionInInformationalVersion=false` is required

The canonical reviewed executable is built with `AssemblyInformationalVersion = 1.0.0`.

When the .NET SDK builds a project from inside a Git repository, it normally appends `+<Git commit SHA>` to `AssemblyInformationalVersion`. That metadata-only change alters the managed assembly and therefore changes the final self-contained single-file bundle hash and potentially its aligned byte length.

The review build therefore explicitly passes:

```text
-p:IncludeSourceRevisionInInformationalVersion=false
```

This does not change installer behavior. It prevents repository metadata from changing the binary that Nexus is reviewing.

## UPKUtils source

The installer uses `PatchUPK.exe` and `DecompressLZO.exe` from UPKUtils 7.3.

The corresponding upstream source revision is:

`f4a7149cb25389b3d3e0b4ee1bf4bfefd7b02c29`

The source is included directly at:

`SetupFrontend\UMMSetup\Resources\UPKUtilsSource\`

and also as:

`SetupFrontend\UMMSetup\Resources\UPKUtils-v7.3-source.zip`

The project excludes nested Git metadata from embedded source resources. Required license files are included in the source tree and notices are documented in `THIRD_PARTY_NOTICES.txt`.

## Reproducibility requirement

For Nexus review, the repository build and the uploaded Nexus executable are considered matching only if both byte length and SHA-256 match exactly.

A successful `build-review.ps1` run prints:

`BYTE_IDENTICAL_REVIEW_BUILD=PASS`
