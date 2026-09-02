using System.Security.Cryptography;

namespace UMMInstallerCore;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
                return Usage();

            switch (args[0].ToLowerInvariant())
            {
                case "sha256":
                    if (args.Length != 2)
                        return Usage();
                    Console.WriteLine(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))));
                    return 0;

                case "apply-delta":
                    if (args.Length != 6)
                        return Usage();

                    string source = Path.GetFullPath(args[1]);
                    string patch = Path.GetFullPath(args[2]);
                    string output = Path.GetFullPath(args[3]);
                    string expectedSourceSha256 = NormalizeSha256(args[4]);
                    string expectedOutputSha256 = NormalizeSha256(args[5]);

                    byte[] sourceBytes = File.ReadAllBytes(source);
                    string sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes));
                    if (!sourceSha256.Equals(expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Source SHA-256 mismatch: {sourceSha256}");

                    byte[] result = GitBinaryPatch.ApplyForwardDelta(sourceBytes, patch);
                    string outputSha256 = Convert.ToHexString(SHA256.HashData(result));
                    if (!outputSha256.Equals(expectedOutputSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Output SHA-256 mismatch: {outputSha256}");

                    string? directory = Path.GetDirectoryName(output);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    string temp = output + ".ummtmp." + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.WriteAllBytes(temp, result);
                        File.Move(temp, output, true);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }

                    Console.WriteLine($"SOURCE_SHA256={sourceSha256}");
                    Console.WriteLine($"OUTPUT_SHA256={outputSha256}");
                    Console.WriteLine("VALIDATION=PASS");
                    return 0;

                case "apply-runtime-body":
                    if (args.Length != 4)
                        return Usage();

                    string runtimeTarget = Path.GetFullPath(args[1]);
                    string bodyFile = Path.GetFullPath(args[2]);
                    string outputEncoding = args[3];
                    string normalizedBodySha256 = RuntimeIniPatch.ApplyBodyPreservingIniVersion(runtimeTarget, bodyFile, outputEncoding);
                    Console.WriteLine($"NORMALIZED_BODY_SHA256={normalizedBodySha256}");
                    Console.WriteLine("VALIDATION=PASS");
                    return 0;

                case "install":
                    if (args.Length != 6)
                        return Usage();

                    string backupDirectory = InstallerOrchestrator.Install(
                        args[1], args[2], args[3], args[4], args[5]);
                    Console.WriteLine($"BACKUP_DIR={backupDirectory}");
                    Console.WriteLine("INSTALL=PASS");
                    return 0;

                default:
                    return Usage();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR={ex.Message}");
            return 10;
        }
    }

    private static string NormalizeSha256(string value)
    {
        string normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.");
        return normalized.ToUpperInvariant();
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  UMMInstallerCore sha256 <file>");
        Console.Error.WriteLine("  UMMInstallerCore apply-delta <source> <patch> <output> <expected-source-sha256> <expected-output-sha256>");
        Console.Error.WriteLine("  UMMInstallerCore apply-runtime-body <target-ini> <gold-body-utf8> <output-encoding>");
        Console.Error.WriteLine("  UMMInstallerCore install <manifest.json> <stage-root> <game-root> <documents-root> <backup-root>");
        return 2;
    }
}
