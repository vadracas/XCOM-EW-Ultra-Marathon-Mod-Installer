using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UMMInstallerCore;

internal static class InstallerOrchestrator
{
    private const string RuntimeApplyPolicy = "APPLY_GOLD_BODY_PRESERVE_INIVERSION";
    private const string RuntimeLocalTransformPolicy = "APPLY_LOCAL_BASELINE_TRANSFORM_PRESERVE_INIVERSION";
    private const string ThirdPartyRelativeRoot = "ThirdParty/UPKUtils_v7.3";
    private const string GameExecutableRelativePath = @"XEW\Binaries\Win32\XComEW.exe";

    public static void ValidatePreflight(string manifestPath, string stageRoot, string gameRoot, string documentsRoot)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        stageRoot = Path.GetFullPath(stageRoot);
        gameRoot = Path.GetFullPath(gameRoot);
        documentsRoot = Path.GetFullPath(documentsRoot);

        ReleaseManifest manifest = LoadManifest(manifestPath);
        if (!Directory.Exists(stageRoot))
            throw new DirectoryNotFoundException($"Stage root not found: {stageRoot}");
        if (!Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException($"Game root not found: {gameRoot}");
        if (!Directory.Exists(documentsRoot))
            throw new DirectoryNotFoundException($"Documents root not found: {documentsRoot}");

        Preflight(manifest, stageRoot, gameRoot, documentsRoot);
    }

    public static string Install(string manifestPath, string stageRoot, string gameRoot, string documentsRoot, string backupRoot)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        stageRoot = Path.GetFullPath(stageRoot);
        gameRoot = Path.GetFullPath(gameRoot);
        documentsRoot = Path.GetFullPath(documentsRoot);
        backupRoot = Path.GetFullPath(backupRoot);

        ReleaseManifest manifest = LoadManifest(manifestPath);
        ValidateRoots(stageRoot, gameRoot, documentsRoot, backupRoot);
        Preflight(manifest, stageRoot, gameRoot, documentsRoot);

        string backupDirectory = CreateUniqueBackupDirectory(backupRoot, manifest.ReleaseId);
        InstallLogger logger = new(Path.Combine(backupDirectory, "INSTALL.log"));
        BackupManifest backup = CreateBackup(manifest, gameRoot, documentsRoot, backupDirectory, logger);
        WriteBackupManifest(backup, backupDirectory);

        logger.Write($"INSTALL_BEGIN release={manifest.ReleaseId}");
        logger.Write($"MANIFEST_SHA256={Sha256File(manifestPath)}");

        try
        {
            ApplyDirectPayload(manifest, stageRoot, gameRoot, documentsRoot, logger);
            ApplyBaselineTextTransforms(manifest, stageRoot, gameRoot, documentsRoot, logger);
            ApplyRuntimePolicies(manifest, stageRoot, documentsRoot, logger);
            ApplyBinaryDeltas(manifest, stageRoot, gameRoot, logger);
            BuildMaps(manifest, stageRoot, gameRoot, logger);
            PostValidate(manifest, gameRoot, documentsRoot, logger);

            File.WriteAllText(Path.Combine(backupDirectory, "INSTALL_SUCCESS.txt"),
                $"Release={manifest.ReleaseId}{Environment.NewLine}CompletedUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}",
                new UTF8Encoding(false));
            logger.Write("INSTALL_SUCCESS");
            return backupDirectory;
        }
        catch (Exception installException)
        {
            logger.Write($"INSTALL_FAILURE type={installException.GetType().Name} message={installException.Message}");
            try
            {
                Rollback(backup, backupDirectory, logger);
                logger.Write("ROLLBACK_SUCCESS");
            }
            catch (Exception rollbackException)
            {
                logger.Write($"ROLLBACK_FAILURE type={rollbackException.GetType().Name} message={rollbackException.Message}");
                throw new AggregateException("Installation failed and rollback also failed.", installException, rollbackException);
            }

            throw new InvalidOperationException("Installation failed; all touched targets were rolled back.", installException);
        }
    }

    private static ReleaseManifest LoadManifest(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Release manifest not found.", path);

        ReleaseManifest? manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null)
            throw new InvalidDataException("Release manifest deserialized to null.");
        if (manifest.SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported release manifest schema: {manifest.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.ReleaseId))
            throw new InvalidDataException("Release manifest has no releaseId.");
        if (manifest.Baseline.StrictGameScopeFiles != manifest.Baseline.GameFiles.Count)
            throw new InvalidDataException("strictGameScopeFiles does not match gameFiles count.");
        return manifest;
    }

    private static void ValidateRoots(string stageRoot, string gameRoot, string documentsRoot, string backupRoot)
    {
        if (!Directory.Exists(stageRoot))
            throw new DirectoryNotFoundException($"Stage root not found: {stageRoot}");
        if (!Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException($"Game root not found: {gameRoot}");
        if (!Directory.Exists(documentsRoot))
            throw new DirectoryNotFoundException($"Documents root not found: {documentsRoot}");
        Directory.CreateDirectory(backupRoot);
    }

    private static void RequireGameExecutable(string gameRoot)
    {
        string gameExecutable = ResolveUnderRoot(gameRoot, GameExecutableRelativePath);
        if (!File.Exists(gameExecutable))
            throw new FileNotFoundException($"XCOM: Enemy Within executable is missing: {GameExecutableRelativePath}", gameExecutable);
        if (new FileInfo(gameExecutable).Length <= 0)
            throw new InvalidDataException($"XCOM: Enemy Within executable is empty: {GameExecutableRelativePath}");
    }

    private static void RequireGameNotRunning()
    {
        Process[] processes = Process.GetProcessesByName("XComEW");
        try
        {
            if (processes.Length != 0)
                throw new InvalidOperationException("XCOM: Enemy Within is running. Close the game before validating or installing Ultra Marathon Mod.");
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    private static void Preflight(ReleaseManifest manifest, string stageRoot, string gameRoot, string documentsRoot)
    {
        RequireGameNotRunning();
        RequireGameExecutable(gameRoot);

        foreach (BaselineGameFile entry in manifest.Baseline.GameFiles)
            RequireFile(gameRoot, entry.Path, entry.Bytes, entry.Sha256, "clean LW1.1 baseline");

        foreach (DirectPayloadEntry entry in manifest.DirectPayload)
        {
            string stage = ResolveUnderRoot(stageRoot, entry.StagePath);
            RequireExactFile(stage, entry.Bytes, entry.Sha256, "direct payload");
        }

        foreach (BaselineTextTransformEntry entry in manifest.BaselineTextTransforms)
        {
            string spec = ResolveUnderRoot(stageRoot, entry.TransformSpecStagePath);
            RequireExactFile(spec, entry.TransformSpecBytes, entry.TransformSpecSha256, "baseline transform spec");
            string target = ResolveUnderRoot(ScopeRoot(entry.Scope, gameRoot, documentsRoot), entry.Path);
            BaselineTextTransformer.ValidateInput(entry, spec, target);
        }

        foreach (RuntimePolicyEntry entry in manifest.RuntimePolicies.Where(x => x.Policy == RuntimeApplyPolicy))
        {
            if (string.IsNullOrWhiteSpace(entry.BodyStagePath) || entry.BodyBytes is null ||
                string.IsNullOrWhiteSpace(entry.BodySha256) || string.IsNullOrWhiteSpace(entry.CanonicalBodySha256Utf8))
                throw new InvalidDataException($"Runtime apply entry is incomplete: {entry.Path}");

            string runtimeTarget = ResolveUnderRoot(documentsRoot, entry.Path);
            if (!File.Exists(runtimeTarget))
                throw new FileNotFoundException($"Required Long War runtime config is missing: {entry.Path}", runtimeTarget);

            string body = ResolveUnderRoot(stageRoot, entry.BodyStagePath);
            RequireExactFile(body, entry.BodyBytes.Value, entry.BodySha256, "runtime body payload");
        }

        foreach (BinaryDeltaEntry entry in manifest.BinaryDeltas)
        {
            RequireFile(gameRoot, entry.Path, entry.SourceBytes, entry.SourceSha256, "binary delta source");
            string patch = ResolveUnderRoot(stageRoot, entry.PatchStagePath);
            RequireExactFile(patch, entry.PatchBytes, entry.PatchSha256, "binary delta patch");
        }

        foreach (MapBuildEntry entry in manifest.MapBuilds)
        {
            RequireFile(gameRoot, entry.SourcePath, entry.SourceBytes, entry.SourceSha256, "map source template");
            foreach (MapPatchScriptEntry patch in entry.PatchScripts)
            {
                string stagePatch = ResolveUnderRoot(stageRoot, patch.StagePath);
                RequireExactFile(stagePatch, patch.Bytes, patch.Sha256, "map patch script");
            }

            string target = ResolveUnderRoot(gameRoot, entry.TargetPath);
            if (File.Exists(target) && !Sha256File(target).Equals(entry.TargetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected pre-existing map target: {entry.TargetPath}");
        }

        foreach (ThirdPartyToolEntry tool in manifest.RequiredThirdPartyTools)
        {
            string path = ResolveUnderRoot(stageRoot, Path.Combine(ThirdPartyRelativeRoot, tool.Name));
            RequireExactFile(path, tool.Bytes, tool.Sha256, "third-party tool");
        }

        // Most Documents files remain intentionally ungated because runtime metadata/user-local state may differ.
        // Baseline-transform runtime targets are the exception: their canonical body is exact-gated above.
        _ = documentsRoot;
    }

    private static BackupManifest CreateBackup(ReleaseManifest manifest, string gameRoot, string documentsRoot,
        string backupDirectory, InstallLogger logger)
    {
        BackupManifest backup = new()
        {
            ReleaseId = manifest.ReleaseId,
            CreatedUtc = DateTimeOffset.UtcNow,
            GameRoot = gameRoot,
            DocumentsRoot = documentsRoot
        };

        Dictionary<string, (string Scope, string RelativePath)> targets = new(StringComparer.OrdinalIgnoreCase);
        void Add(string scope, string relativePath)
        {
            string key = scope + "|" + relativePath.Replace('/', '\\');
            targets[key] = (scope, relativePath);
        }

        foreach (DirectPayloadEntry entry in manifest.DirectPayload)
            Add(entry.Scope, entry.Path);
        foreach (BaselineTextTransformEntry entry in manifest.BaselineTextTransforms)
            Add(entry.Scope, entry.Path);
        foreach (RuntimePolicyEntry entry in manifest.RuntimePolicies.Where(x => x.Policy == RuntimeApplyPolicy))
            Add("DOCUMENTS_ROOT", entry.Path);
        foreach (BinaryDeltaEntry entry in manifest.BinaryDeltas)
            Add("GAME_ROOT", entry.Path);
        foreach (MapBuildEntry entry in manifest.MapBuilds)
            Add("GAME_ROOT", entry.TargetPath);

        foreach ((string scope, string relativePath) in targets.Values.OrderBy(x => x.Scope).ThenBy(x => x.RelativePath))
        {
            string scopeRoot = ScopeRoot(scope, gameRoot, documentsRoot);
            string target = ResolveUnderRoot(scopeRoot, relativePath);
            BackupEntry entry = new()
            {
                Scope = scope,
                RelativePath = relativePath,
                Existed = File.Exists(target)
            };

            if (entry.Existed)
            {
                FileInfo info = new(target);
                entry.OriginalBytes = info.Length;
                entry.OriginalSha256 = Sha256File(target);
                entry.BackupRelativePath = Path.Combine("Files", scope, NormalizeRelativeForFilesystem(relativePath));
                string backupPath = ResolveUnderRoot(backupDirectory, entry.BackupRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(target, backupPath, overwrite: false);
                logger.Write($"BACKUP scope={scope} path={relativePath} sha256={entry.OriginalSha256}");
            }
            else
            {
                logger.Write($"BACKUP_NEW_TARGET scope={scope} path={relativePath}");
            }

            backup.Entries.Add(entry);
        }

        return backup;
    }

    private static void ApplyDirectPayload(ReleaseManifest manifest, string stageRoot, string gameRoot,
        string documentsRoot, InstallLogger logger)
    {
        foreach (DirectPayloadEntry entry in manifest.DirectPayload)
        {
            string source = ResolveUnderRoot(stageRoot, entry.StagePath);
            string target = ResolveUnderRoot(ScopeRoot(entry.Scope, gameRoot, documentsRoot), entry.Path);
            AtomicCopy(source, target);
            RequireExactFile(target, entry.Bytes, entry.Sha256, "installed direct payload");
            logger.Write($"PAYLOAD_APPLIED scope={entry.Scope} path={entry.Path} sha256={entry.Sha256}");
        }
    }

    private static void ApplyBaselineTextTransforms(ReleaseManifest manifest, string stageRoot, string gameRoot,
        string documentsRoot, InstallLogger logger)
    {
        foreach (BaselineTextTransformEntry entry in manifest.BaselineTextTransforms)
        {
            string spec = ResolveUnderRoot(stageRoot, entry.TransformSpecStagePath);
            string target = ResolveUnderRoot(ScopeRoot(entry.Scope, gameRoot, documentsRoot), entry.Path);
            string installedHash = BaselineTextTransformer.Apply(entry, spec, target);
            logger.Write($"BASELINE_TRANSFORM_APPLIED scope={entry.Scope} path={entry.Path} outputHash={installedHash}");
        }
    }

    private static void ApplyRuntimePolicies(ReleaseManifest manifest, string stageRoot, string documentsRoot,
        InstallLogger logger)
    {
        foreach (RuntimePolicyEntry entry in manifest.RuntimePolicies)
        {
            if (entry.Policy == RuntimeLocalTransformPolicy)
            {
                logger.Write($"RUNTIME_TRANSFORM_HANDLED path={entry.Path}");
                continue;
            }
            if (entry.Policy != RuntimeApplyPolicy)
            {
                logger.Write($"RUNTIME_PRESERVED policy={entry.Policy} path={entry.Path}");
                continue;
            }

            string target = ResolveUnderRoot(documentsRoot, entry.Path);
            string body = ResolveUnderRoot(stageRoot, entry.BodyStagePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string bodySha = RuntimeIniPatch.ApplyBodyPreservingIniVersion(target, body, entry.GoldEncoding);
            string installedBodySha = RuntimeIniPatch.GetCanonicalBodySha256Utf8(target);
            if (!bodySha.Equals(entry.CanonicalBodySha256Utf8, StringComparison.OrdinalIgnoreCase) ||
                !installedBodySha.Equals(entry.CanonicalBodySha256Utf8, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Runtime body post-write canonical hash mismatch: {entry.Path}");
            logger.Write($"RUNTIME_APPLIED path={entry.Path} canonicalBodySha={installedBodySha}");
        }
    }

    private static void ApplyBinaryDeltas(ReleaseManifest manifest, string stageRoot, string gameRoot,
        InstallLogger logger)
    {
        foreach (BinaryDeltaEntry entry in manifest.BinaryDeltas)
        {
            string target = ResolveUnderRoot(gameRoot, entry.Path);
            RequireExactFile(target, entry.SourceBytes, entry.SourceSha256, "binary delta source immediately before patch");
            string patch = ResolveUnderRoot(stageRoot, entry.PatchStagePath);
            byte[] result = GitBinaryPatch.ApplyForwardDelta(File.ReadAllBytes(target), patch);
            string resultSha = Sha256Bytes(result);
            if (result.LongLength != entry.TargetBytes || !resultSha.Equals(entry.TargetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Binary delta result mismatch before write: {entry.Path}");
            AtomicWrite(target, result);
            RequireExactFile(target, entry.TargetBytes, entry.TargetSha256, "installed binary delta target");
            logger.Write($"BINARY_DELTA_APPLIED path={entry.Path} sha256={entry.TargetSha256}");
        }
    }

    private static void BuildMaps(ReleaseManifest manifest, string stageRoot, string gameRoot, InstallLogger logger)
    {
        string decompress = ResolveUnderRoot(stageRoot, Path.Combine(ThirdPartyRelativeRoot, "DecompressLZO.exe"));
        string patchUpk = ResolveUnderRoot(stageRoot, Path.Combine(ThirdPartyRelativeRoot, "PatchUPK.exe"));
        string workRoot = Path.Combine(Path.GetTempPath(), "UMMInstaller_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            foreach (MapBuildEntry entry in manifest.MapBuilds)
            {
                string mapWork = Path.Combine(workRoot, Path.GetFileNameWithoutExtension(entry.TargetPath));
                Directory.CreateDirectory(mapWork);
                string source = ResolveUnderRoot(gameRoot, entry.SourcePath);
                string localTarget = Path.Combine(mapWork, Path.GetFileName(entry.TargetPath));

                RunTool(decompress, [source, localTarget], mapWork, logger);
                foreach (MapPatchScriptEntry script in entry.PatchScripts)
                {
                    string stagedScript = ResolveUnderRoot(stageRoot, script.StagePath);
                    string localScript = Path.Combine(mapWork, Path.GetFileName(script.StagePath));
                    File.Copy(stagedScript, localScript, overwrite: true);
                    RunTool(patchUpk, [localScript, mapWork], mapWork, logger);
                }

                RequireExactFile(localTarget, entry.TargetBytes, entry.TargetSha256, "locally built map");
                string target = ResolveUnderRoot(gameRoot, entry.TargetPath);
                AtomicCopy(localTarget, target);
                RequireExactFile(target, entry.TargetBytes, entry.TargetSha256, "installed map");
                logger.Write($"MAP_BUILT path={entry.TargetPath} sha256={entry.TargetSha256}");
            }
        }
        finally
        {
            try { Directory.Delete(workRoot, recursive: true); }
            catch { logger.Write($"TEMP_CLEANUP_WARNING path={workRoot}"); }
        }
    }

    private static void PostValidate(ReleaseManifest manifest, string gameRoot, string documentsRoot, InstallLogger logger)
    {
        int exact = 0;
        int runtime = 0;
        int unchanged = 0;

        foreach (DirectPayloadEntry entry in manifest.DirectPayload)
        {
            string target = ResolveUnderRoot(ScopeRoot(entry.Scope, gameRoot, documentsRoot), entry.Path);
            RequireExactFile(target, entry.Bytes, entry.Sha256, "postvalidation direct payload");
            exact++;
        }

        foreach (BaselineTextTransformEntry entry in manifest.BaselineTextTransforms)
        {
            string target = ResolveUnderRoot(ScopeRoot(entry.Scope, gameRoot, documentsRoot), entry.Path);
            BaselineTextTransformer.PostValidate(entry, target);
            if (entry.Scope.Equals("DOCUMENTS_ROOT", StringComparison.OrdinalIgnoreCase)) runtime++;
            else exact++;
        }

        foreach (BinaryDeltaEntry entry in manifest.BinaryDeltas)
        {
            RequireFile(gameRoot, entry.Path, entry.TargetBytes, entry.TargetSha256, "postvalidation binary target");
            exact++;
        }

        foreach (MapBuildEntry entry in manifest.MapBuilds)
        {
            RequireFile(gameRoot, entry.TargetPath, entry.TargetBytes, entry.TargetSha256, "postvalidation map target");
            exact++;
        }

        foreach (RuntimePolicyEntry entry in manifest.RuntimePolicies.Where(x => x.Policy == RuntimeApplyPolicy))
        {
            string target = ResolveUnderRoot(documentsRoot, entry.Path);
            string hash = RuntimeIniPatch.GetCanonicalBodySha256Utf8(target);
            if (!hash.Equals(entry.CanonicalBodySha256Utf8, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Postvalidation runtime canonical body mismatch: {entry.Path}");
            runtime++;
        }

        foreach (BaselineGameFile entry in manifest.Baseline.GameFiles.Where(x => x.ClassName == "UNCHANGED_FROM_LW11"))
        {
            RequireFile(gameRoot, entry.Path, entry.Bytes, entry.Sha256, "postvalidation untouched clean file");
            unchanged++;
        }

        logger.Write($"POSTVALIDATION_PASS exactTargets={exact} runtimeTargets={runtime} untouchedCleanFiles={unchanged}");
    }

    private static void Rollback(BackupManifest backup, string backupDirectory, InstallLogger logger)
    {
        foreach (BackupEntry entry in backup.Entries.AsEnumerable().Reverse())
        {
            string root = ScopeRoot(entry.Scope, backup.GameRoot, backup.DocumentsRoot);
            string target = ResolveUnderRoot(root, entry.RelativePath);
            if (entry.Existed)
            {
                string source = ResolveUnderRoot(backupDirectory, entry.BackupRelativePath!);
                AtomicCopy(source, target);
                RequireExactFile(target, entry.OriginalBytes, entry.OriginalSha256!, "rollback restore");
                logger.Write($"ROLLBACK_RESTORED scope={entry.Scope} path={entry.RelativePath}");
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
                logger.Write($"ROLLBACK_DELETED_NEW scope={entry.Scope} path={entry.RelativePath}");
            }
        }
    }

    private static void RunTool(string executable, IEnumerable<string> arguments, string workingDirectory, InstallLogger logger)
    {
        ProcessStartInfo psi = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start tool: {executable}");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Tool failed ({Path.GetFileName(executable)} exit {process.ExitCode}): {stdout} {stderr}".Trim());
        if (!string.IsNullOrWhiteSpace(stdout)) logger.Write($"TOOL_STDOUT tool={Path.GetFileName(executable)} text={OneLine(stdout)}");
        if (!string.IsNullOrWhiteSpace(stderr)) logger.Write($"TOOL_STDERR tool={Path.GetFileName(executable)} text={OneLine(stderr)}");
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string CreateUniqueBackupDirectory(string backupRoot, string releaseId)
    {
        string safeRelease = string.Concat(releaseId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        for (int i = 0; i < 100; i++)
        {
            string suffix = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff") + (i == 0 ? string.Empty : $"_{i}");
            string candidate = ResolveUnderRoot(backupRoot, safeRelease + "_" + suffix);
            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
        }
        throw new IOException("Could not allocate a unique backup directory.");
    }

    private static void WriteBackupManifest(BackupManifest backup, string backupDirectory)
    {
        string json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(backupDirectory, "BACKUP_MANIFEST.json"), json, new UTF8Encoding(false));
    }

    private static void RequireFile(string root, string relativePath, long bytes, string sha256, string context) =>
        RequireExactFile(ResolveUnderRoot(root, relativePath), bytes, sha256, context);

    private static void RequireExactFile(string path, long bytes, string sha256, string context)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing {context} file: {path}", path);
        FileInfo info = new(path);
        if (info.Length != bytes)
            throw new InvalidDataException($"{context} byte length mismatch: {path} ({info.Length} != {bytes})");
        string actual = Sha256File(path);
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} SHA-256 mismatch: {path} ({actual} != {sha256})");
    }

    private static string ScopeRoot(string scope, string gameRoot, string documentsRoot) => scope switch
    {
        "GAME_ROOT" => gameRoot,
        "DOCUMENTS_ROOT" => documentsRoot,
        _ => throw new InvalidDataException($"Unknown installer scope: {scope}")
    };

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Unsafe relative path: {relativePath}");
        string normalizedRelative = NormalizeRelativeForFilesystem(relativePath);
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelative));
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes installer root: {relativePath}");
        return full;
    }

    private static string NormalizeRelativeForFilesystem(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static void AtomicCopy(string source, string target) => AtomicWrite(target, File.ReadAllBytes(source));

    private static void AtomicWrite(string target, byte[] data)
    {
        string? directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidDataException($"Target has no parent directory: {target}");
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, ".ummtmp_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(temp, data);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    internal static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sha256Bytes(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private sealed class InstallLogger
    {
        private readonly string _path;
        private readonly object _gate = new();
        public InstallLogger(string path) => _path = path;
        public void Write(string message)
        {
            string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            lock (_gate) File.AppendAllText(_path, line, new UTF8Encoding(false));
        }
    }
}
