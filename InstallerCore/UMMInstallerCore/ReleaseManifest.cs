using System.Text.Json.Serialization;

namespace UMMInstallerCore;

internal sealed class ReleaseManifest
{
    public int SchemaVersion { get; set; }
    public string ReleaseId { get; set; } = string.Empty;
    public BaselineManifest Baseline { get; set; } = new();
    public List<DirectPayloadEntry> DirectPayload { get; set; } = [];
    public List<BaselineTextTransformEntry> BaselineTextTransforms { get; set; } = [];
    public List<RuntimePolicyEntry> RuntimePolicies { get; set; } = [];
    public List<BinaryDeltaEntry> BinaryDeltas { get; set; } = [];
    public List<MapBuildEntry> MapBuilds { get; set; } = [];
    public List<ThirdPartyToolEntry> RequiredThirdPartyTools { get; set; } = [];
    public ValidationPolicyManifest ValidationPolicy { get; set; } = new();
}

internal sealed class BaselineManifest
{
    public string Name { get; set; } = string.Empty;
    public int StrictGameScopeFiles { get; set; }
    public List<BaselineGameFile> GameFiles { get; set; } = [];
}

internal sealed class BaselineGameFile
{
    public string Path { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("class")]
    public string ClassName { get; set; } = string.Empty;
}

internal sealed class DirectPayloadEntry
{
    public string Scope { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string StagePath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("class")]
    public string ClassName { get; set; } = string.Empty;
}

internal sealed class BaselineTextTransformEntry
{
    public string Scope { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string TransformKind { get; set; } = string.Empty;
    public string TransformSpecStagePath { get; set; } = string.Empty;
    public long TransformSpecBytes { get; set; }
    public string TransformSpecSha256 { get; set; } = string.Empty;
    public string InputMode { get; set; } = string.Empty;
    public long? InputBytes { get; set; }
    public string InputSha256 { get; set; } = string.Empty;
    public string OutputMode { get; set; } = string.Empty;
    public long? OutputBytes { get; set; }
    public string OutputSha256 { get; set; } = string.Empty;
    public string OutputEncoding { get; set; } = string.Empty;
    public bool PreserveIniVersion { get; set; }
}

internal sealed class RuntimePolicyEntry
{
    public string Path { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public string GoldEncoding { get; set; } = string.Empty;
    public string? BodyStagePath { get; set; }
    public long? BodyBytes { get; set; }
    public string? BodySha256 { get; set; }
    public string? GoldBodySha256Utf8 { get; set; }
    public string? CanonicalBodySha256Utf8 { get; set; }
}

internal sealed class BinaryDeltaEntry
{
    public string Path { get; set; } = string.Empty;
    public string PatchStagePath { get; set; } = string.Empty;
    public long PatchBytes { get; set; }
    public string PatchSha256 { get; set; } = string.Empty;
    public long SourceBytes { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public long TargetBytes { get; set; }
    public string TargetSha256 { get; set; } = string.Empty;
}

internal sealed class MapBuildEntry
{
    public string TargetPath { get; set; } = string.Empty;
    public long TargetBytes { get; set; }
    public string TargetSha256 { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public long SourceBytes { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public List<MapPatchScriptEntry> PatchScripts { get; set; } = [];
}

internal sealed class MapPatchScriptEntry
{
    public string StagePath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class ThirdPartyToolEntry
{
    public string Name { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class ValidationPolicyManifest
{
    public int GoldMasterScopeFiles { get; set; }
    public int ExactGoldTargets { get; set; }
    public int GoldBodySemanticTargets { get; set; }
    public int NormalizedSemanticTargets { get; set; }
    public int PreserveLocalTargets { get; set; }
    public int BaselineTransformTargets { get; set; }
    public int LocalBaselineSemanticTargets { get; set; }
}

internal sealed class BackupManifest
{
    public string ReleaseId { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string GameRoot { get; set; } = string.Empty;
    public string DocumentsRoot { get; set; } = string.Empty;
    public List<BackupEntry> Entries { get; set; } = [];
}

internal sealed class BackupEntry
{
    public string Scope { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool Existed { get; set; }
    public long OriginalBytes { get; set; }
    public string? OriginalSha256 { get; set; }
    public string? BackupRelativePath { get; set; }
}
