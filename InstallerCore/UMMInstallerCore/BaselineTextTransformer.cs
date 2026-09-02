using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UMMInstallerCore;

internal static class BaselineTextTransformer
{
    private const string ExactFileMode = "EXACT_FILE_SHA256";
    private const string CanonicalBodyMode = "CANONICAL_BODY_SHA256_UTF8";

    public static void ValidateInput(BaselineTextTransformEntry entry, string specPath, string targetPath)
    {
        BaselineTransformSpec spec = LoadAndValidateSpec(entry, specPath);
        if (!File.Exists(targetPath))
            throw new FileNotFoundException($"Required local-baseline transform input is missing: {entry.Path}", targetPath);

        if (entry.InputMode == ExactFileMode)
        {
            if (entry.InputBytes is null)
                throw new InvalidDataException($"Exact-file transform has no input byte length: {entry.Path}");
            FileInfo info = new(targetPath);
            if (info.Length != entry.InputBytes.Value)
                throw new InvalidDataException($"Transform input byte length mismatch: {entry.Path}");
            string hash = Sha256File(targetPath);
            if (!hash.Equals(entry.InputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Transform input SHA-256 mismatch: {entry.Path}");
        }
        else if (entry.InputMode == CanonicalBodyMode)
        {
            string hash = RuntimeIniPatch.GetCanonicalBodySha256Utf8(targetPath);
            if (!hash.Equals(entry.InputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Transform runtime input canonical body mismatch: {entry.Path}");
        }
        else
        {
            throw new InvalidDataException($"Unsupported transform input mode: {entry.InputMode}");
        }

        if (!spec.InputSha256.Equals(entry.InputSha256, StringComparison.OrdinalIgnoreCase) ||
            !spec.OutputSha256.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Transform spec identity disagrees with release manifest: {entry.Path}");
    }

    public static string Apply(BaselineTextTransformEntry entry, string specPath, string targetPath)
    {
        BaselineTransformSpec spec = LoadAndValidateSpec(entry, specPath);
        ValidateInput(entry, specPath, targetPath);

        if (entry.InputMode == ExactFileMode && entry.OutputMode == ExactFileMode)
        {
            string sourceText = DecodeAuto(File.ReadAllBytes(targetPath));
            string normalizedOutput = ApplyRecipe(sourceText, spec);
            string newline = spec.OutputNewline switch
            {
                "CRLF" => "\r\n",
                "LF" => "\n",
                _ => throw new InvalidDataException($"Unsupported transform newline: {spec.OutputNewline}")
            };
            string outputText = normalizedOutput.Replace("\n", newline, StringComparison.Ordinal);
            byte[] outputBytes = Encode(outputText, spec.OutputEncoding);
            string resultHash = Sha256Bytes(outputBytes);
            if (entry.OutputBytes is null || outputBytes.LongLength != entry.OutputBytes.Value ||
                !resultHash.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Transform output mismatch before write: {entry.Path}");

            AtomicWrite(targetPath, outputBytes);
            FileInfo info = new(targetPath);
            string installedHash = Sha256File(targetPath);
            if (info.Length != entry.OutputBytes.Value || !installedHash.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Transform output mismatch after write: {entry.Path}");
            return installedHash;
        }

        if (entry.InputMode == CanonicalBodyMode && entry.OutputMode == CanonicalBodyMode && entry.PreserveIniVersion)
        {
            string sourceBody = RuntimeIniPatch.GetCanonicalBodyText(targetPath);
            string normalizedOutput = ApplyRecipe(sourceBody, spec).TrimEnd('\n');
            string resultHash = Sha256Text(normalizedOutput);
            if (!resultHash.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Runtime transform output mismatch before write: {entry.Path}");

            string installedHash = RuntimeIniPatch.ApplyCanonicalBodyPreservingIniVersion(targetPath, normalizedOutput, entry.OutputEncoding);
            if (!installedHash.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase) ||
                !RuntimeIniPatch.GetCanonicalBodySha256Utf8(targetPath).Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Runtime transform output mismatch after write: {entry.Path}");
            return installedHash;
        }

        throw new InvalidDataException($"Unsupported transform mode combination for {entry.Path}: {entry.InputMode} -> {entry.OutputMode}");
    }

    public static void PostValidate(BaselineTextTransformEntry entry, string targetPath)
    {
        if (entry.OutputMode == ExactFileMode)
        {
            if (entry.OutputBytes is null)
                throw new InvalidDataException($"Exact-file transform has no output byte length: {entry.Path}");
            FileInfo info = new(targetPath);
            string hash = Sha256File(targetPath);
            if (info.Length != entry.OutputBytes.Value || !hash.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Postvalidation transform exact target mismatch: {entry.Path}");
            return;
        }

        if (entry.OutputMode == CanonicalBodyMode)
        {
            string hash = RuntimeIniPatch.GetCanonicalBodySha256Utf8(targetPath);
            if (!hash.Equals(entry.OutputSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Postvalidation transform runtime target mismatch: {entry.Path}");
            return;
        }

        throw new InvalidDataException($"Unsupported transform output mode: {entry.OutputMode}");
    }

    private static BaselineTransformSpec LoadAndValidateSpec(BaselineTextTransformEntry entry, string specPath)
    {
        BaselineTransformSpec? spec = JsonSerializer.Deserialize<BaselineTransformSpec>(File.ReadAllText(specPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (spec is null || spec.SchemaVersion != 1)
            throw new InvalidDataException($"Invalid baseline transform spec: {entry.Path}");
        if (!spec.Scope.Equals(entry.Scope, StringComparison.OrdinalIgnoreCase) ||
            !NormalizePath(spec.Path).Equals(NormalizePath(entry.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Transform spec target mismatch: {entry.Path}");
        if (!spec.InputMode.Equals(entry.InputMode, StringComparison.Ordinal) ||
            !spec.OutputMode.Equals(entry.OutputMode, StringComparison.Ordinal) ||
            spec.PreserveIniVersion != entry.PreserveIniVersion)
            throw new InvalidDataException($"Transform spec mode mismatch: {entry.Path}");
        if (spec.Operations.Count == 0)
            throw new InvalidDataException($"Transform spec has no operations: {entry.Path}");
        return spec;
    }

    private static string ApplyRecipe(string sourceText, BaselineTransformSpec spec)
    {
        string normalized = NormalizeNewlines(sourceText);
        string core = normalized.TrimEnd('\n');
        string[] sourceLines = core.Length == 0 ? [] : core.Split('\n');
        List<string> output = [];

        foreach (TransformOperation op in spec.Operations)
        {
            switch (op.Op)
            {
                case "COPY_LINE":
                {
                    string line = GetSourceLine(sourceLines, op.SourceLine);
                    RequireTextHash(line, op.SourceLineSha256Utf8, "COPY_LINE");
                    output.Add(line);
                    break;
                }
                case "COPY_RANGE":
                {
                    if (op.LineCount <= 0)
                        throw new InvalidDataException("COPY_RANGE lineCount must be positive.");
                    int start = op.SourceLine - 1;
                    if (start < 0 || start + op.LineCount > sourceLines.Length)
                        throw new InvalidDataException("COPY_RANGE source range is out of bounds.");
                    string[] lines = sourceLines.Skip(start).Take(op.LineCount).ToArray();
                    RequireTextHash(string.Join("\n", lines), op.SourceRangeSha256Utf8, "COPY_RANGE");
                    output.AddRange(lines);
                    break;
                }
                case "PATCH_LINE":
                {
                    string line = GetSourceLine(sourceLines, op.SourceLine);
                    RequireTextHash(line, op.SourceLineSha256Utf8, "PATCH_LINE");
                    if (op.PrefixUtf16 < 0 || op.SuffixUtf16 < 0 || op.PrefixUtf16 + op.SuffixUtf16 > line.Length)
                        throw new InvalidDataException("PATCH_LINE prefix/suffix is out of bounds.");
                    string prefix = line.Substring(0, op.PrefixUtf16);
                    string suffix = op.SuffixUtf16 == 0 ? string.Empty : line.Substring(line.Length - op.SuffixUtf16);
                    output.Add(prefix + (op.Replacement ?? string.Empty) + suffix);
                    break;
                }
                case "INSERT_OWNED":
                    output.Add(op.Text ?? string.Empty);
                    break;
                default:
                    throw new InvalidDataException($"Unknown baseline transform operation: {op.Op}");
            }
        }

        return string.Join("\n", output) + new string('\n', spec.TrailingNewlines);
    }

    private static string GetSourceLine(string[] lines, int oneBasedLine)
    {
        if (oneBasedLine <= 0 || oneBasedLine > lines.Length)
            throw new InvalidDataException($"Transform source line is out of bounds: {oneBasedLine}");
        return lines[oneBasedLine - 1];
    }

    private static void RequireTextHash(string text, string? expected, string context)
    {
        if (string.IsNullOrWhiteSpace(expected) || !Sha256Text(text).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{context} source hash mismatch.");
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string NormalizePath(string path) => path.Replace('/', '\\');

    private static string DecodeAuto(byte[] data)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(data);
        }
    }

    private static byte[] Encode(string text, string encodingName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return encodingName.ToLowerInvariant() switch
        {
            "utf-16le-bom" => WithPreamble(Encoding.Unicode, text),
            "utf-16be-bom" => WithPreamble(Encoding.BigEndianUnicode, text),
            "utf-8-bom" => WithPreamble(new UTF8Encoding(true), text),
            "utf-8" => new UTF8Encoding(false).GetBytes(text),
            "windows-1252" => Encoding.GetEncoding(1252).GetBytes(text),
            _ => throw new InvalidDataException($"Unsupported transform output encoding: {encodingName}")
        };
    }

    private static byte[] WithPreamble(Encoding encoding, string text)
    {
        byte[] preamble = encoding.GetPreamble();
        byte[] body = encoding.GetBytes(text);
        byte[] result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static void AtomicWrite(string target, byte[] data)
    {
        string? directory = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidDataException($"Transform target has no parent directory: {target}");
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, ".ummtransform_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(temp, data);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sha256Bytes(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static string Sha256Text(string text) => Sha256Bytes(Encoding.UTF8.GetBytes(text));

    private sealed class BaselineTransformSpec
    {
        public int SchemaVersion { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string InputMode { get; set; } = string.Empty;
        public string InputSha256 { get; set; } = string.Empty;
        public string OutputMode { get; set; } = string.Empty;
        public string OutputSha256 { get; set; } = string.Empty;
        public string OutputEncoding { get; set; } = string.Empty;
        public string OutputNewline { get; set; } = string.Empty;
        public int TrailingNewlines { get; set; }
        public bool PreserveIniVersion { get; set; }
        public List<TransformOperation> Operations { get; set; } = [];
    }

    private sealed class TransformOperation
    {
        public string Op { get; set; } = string.Empty;
        public int SourceLine { get; set; }
        public int LineCount { get; set; }
        public string? SourceLineSha256Utf8 { get; set; }
        public string? SourceRangeSha256Utf8 { get; set; }
        public int PrefixUtf16 { get; set; }
        public int SuffixUtf16 { get; set; }
        public string? Replacement { get; set; }
        public string? Text { get; set; }
    }
}
