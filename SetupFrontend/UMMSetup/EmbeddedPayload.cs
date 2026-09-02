using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace UMMSetup;

internal static class EmbeddedPayload
{
    private const string RuntimePrefix = "UMMSetup.RuntimePayload/";
    private const string SourcePrefix = "UMMSetup.UPKUtilsSource/";
    private const string RuntimeManifestRelativePath = "UMM_RELEASE_INSTALL_MANIFEST.json";
    private const string RuntimeManifestSha256 = "E1EB8AC97736BF9AA810BA2BFCC7DEB40DC181C45630905D3463B191E36B6E8E";
    private const string NoticesResource = "UMMSetup.Resources.THIRD_PARTY_NOTICES.txt";
    private const string ReadmeResource = "UMMSetup.Resources.README_RC1.txt";

    public static PayloadSession ExtractRuntimePayload()
    {
        string root = Path.Combine(Path.GetTempPath(), "Narzelith", "UMMSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            ExtractResourceTree(RuntimePrefix, root);

            string manifest = Path.Combine(root, RuntimeManifestRelativePath);
            string stage = Path.Combine(root, "Stage");
            if (!File.Exists(manifest) || !Directory.Exists(stage))
                throw new InvalidDataException("Embedded payload is structurally incomplete.");

            RequireFileSha256(manifest, RuntimeManifestSha256, "embedded runtime manifest");
            return new PayloadSession(root, stage, manifest);
        }
        catch
        {
            TryDeleteDirectory(root);
            throw;
        }
    }

    public static string ReadThirdPartyNotices() => Encoding.UTF8.GetString(ReadBytes(NoticesResource));
    public static string ReadReadme() => Encoding.UTF8.GetString(ReadBytes(ReadmeResource));

    public static void ExportThirdPartySource(string destination)
    {
        Assembly assembly = typeof(EmbeddedPayload).Assembly;
        ResourceTreeEntry[] entries = GetTreeEntries(assembly, SourcePrefix);
        if (entries.Length == 0)
            throw new InvalidDataException("Embedded UPKUtils source tree is missing.");
        if (!entries.Any(e => e.RelativePath.Equals("COPYING", StringComparison.OrdinalIgnoreCase)) ||
            !entries.Any(e => e.RelativePath.Equals("LICENSE", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Embedded UPKUtils source tree is missing required license files.");

        string full = Path.GetFullPath(destination);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using FileStream output = new(full, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: false);
        foreach (ResourceTreeEntry resource in entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            string zipPath = resource.RelativePath.Replace('\\', '/');
            ZipArchiveEntry entry = archive.CreateEntry(zipPath, CompressionLevel.Optimal);
            using Stream source = assembly.GetManifestResourceStream(resource.ResourceName)
                ?? throw new InvalidDataException($"Missing embedded resource: {resource.ResourceName}");
            using Stream target = entry.Open();
            source.CopyTo(target);
        }
    }

    private static void ExtractResourceTree(string prefix, string root)
    {
        Assembly assembly = typeof(EmbeddedPayload).Assembly;
        ResourceTreeEntry[] entries = GetTreeEntries(assembly, prefix);
        if (entries.Length == 0)
            throw new InvalidDataException($"Embedded resource tree is missing: {prefix}");

        string rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        foreach (ResourceTreeEntry resource in entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, resource.RelativePath));
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded payload contains an invalid path traversal entry.");

            string? dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using Stream source = assembly.GetManifestResourceStream(resource.ResourceName)
                ?? throw new InvalidDataException($"Missing embedded resource: {resource.ResourceName}");
            using FileStream output = new(target, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }
    }

    private static ResourceTreeEntry[] GetTreeEntries(Assembly assembly, string prefix) =>
        assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => new ResourceTreeEntry(name, NormalizeRelativePath(name[prefix.Length..])))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeRelativePath(string value)
    {
        string normalized = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            throw new InvalidDataException("Embedded resource path must be relative.");
        return normalized;
    }

    private static byte[] ReadBytes(string resourceName)
    {
        Assembly assembly = typeof(EmbeddedPayload).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing embedded resource: {resourceName}");
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void RequireFileSha256(string path, string expected, string label)
    {
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} SHA-256 mismatch: {actual}");
    }

    internal static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed record ResourceTreeEntry(string ResourceName, string RelativePath);
}

internal sealed class PayloadSession : IDisposable
{
    public string Root { get; }
    public string StageRoot { get; }
    public string ManifestPath { get; }

    public PayloadSession(string root, string stageRoot, string manifestPath)
    {
        Root = root;
        StageRoot = stageRoot;
        ManifestPath = manifestPath;
    }

    public void Dispose() => EmbeddedPayload.TryDeleteDirectory(Root);
}
