XCOM: Enemy Within - Ultra Marathon Mod Installer v1.0

Required baseline:
1. XCOM: Enemy Within
2. Long War
3. Long War 1.1
4. Ultra Marathon Mod installer

The installer validates the supported Long War 1.1 game-side baseline before writing UMM changes.

Installation architecture:
- game-side INI/localization files are installed from the curated UMM release payload;
- XComGame.upk, XComStrategyGame.upk and MiniModsCollection.u are patched locally on the user's own installation from exact clean Long War 1.1 hashes;
- five UMM cleaner maps are built locally from the user's Long War cleaner templates using exact map patch recipes;
- Documents runtime configuration applies only the audited UMM runtime bodies while preserving IniVersion metadata and explicitly preserving local graphics/input state;
- every touched existing target is backed up before the first write;
- any failure after backup triggers automatic rollback.

The installer payload intentionally contains no full modified .upk or .u game package.

Release validation status:
- clean baseline gate: PASS
- end-to-end manifest-driven install: PASS
- independent 128-entry Gold/policy audit: PASS
- late-failure automatic rollback byte/hash audit: PASS
- self-contained Windows x64 installer core: PASS

Backups are stored under the installer-selected/user-local backup root and are not part of the game directory.
