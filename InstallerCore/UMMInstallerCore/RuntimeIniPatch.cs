using System.Security.Cryptography;
using System.Text;

namespace UMMInstallerCore;

internal static class RuntimeIniPatch
{
    static RuntimeIniPatch()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string ApplyBodyPreservingIniVersion(string targetPath, string bodyUtf8Path, string outputEncodingName)
    {
        string targetBody = File.ReadAllText(bodyUtf8Path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        string normalizedTargetBody = NormalizeNewlines(targetBody).TrimEnd('\n');
        return ApplyCanonicalBodyPreservingIniVersion(targetPath, normalizedTargetBody, outputEncodingName);
    }

    public static string ApplyCanonicalBodyPreservingIniVersion(string targetPath, string canonicalBody, string outputEncodingName)
    {
        string originalText = File.Exists(targetPath) ? DecodeAuto(File.ReadAllBytes(targetPath)) : string.Empty;
        string newline = originalText.Length == 0 ? "\r\n" : DetectNewline(originalText);
        (_, string iniVersion) = SplitIniVersion(originalText, newline);

        string normalizedTargetBody = NormalizeNewlines(canonicalBody).TrimEnd('\n');
        string normalizedIniVersion = NormalizeNewlines(iniVersion).Trim('\n');

        StringBuilder outputText = new();
        outputText.Append(normalizedTargetBody.Replace("\n", newline, StringComparison.Ordinal));
        outputText.Append(newline);

        if (normalizedIniVersion.Length != 0)
        {
            outputText.Append(newline);
            outputText.Append(normalizedIniVersion.Replace("\n", newline, StringComparison.Ordinal));
            outputText.Append(newline);
        }

        byte[] encoded = Encode(outputText.ToString(), outputEncodingName);
        string? parent = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidDataException("Runtime INI target has no parent directory.");
        Directory.CreateDirectory(parent);
        string temp = targetPath + ".ummtmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, encoded);

            string verifyText = DecodeAuto(File.ReadAllBytes(temp));
            (string verifyBody, _) = SplitIniVersion(verifyText, DetectNewline(verifyText));
            string normalizedVerifyBody = NormalizeNewlines(verifyBody).TrimEnd('\n');
            if (!normalizedVerifyBody.Equals(normalizedTargetBody, StringComparison.Ordinal))
                throw new InvalidDataException("Runtime INI body verification failed.");

            File.Move(temp, targetPath, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedTargetBody)));
    }

    public static string GetBodySha256Utf8(string targetPath)
    {
        string text = DecodeAuto(File.ReadAllBytes(targetPath));
        string newline = DetectNewline(text);
        (string body, _) = SplitIniVersion(text, newline);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }

    public static string GetCanonicalBodySha256Utf8(string targetPath)
    {
        string normalized = GetCanonicalBodyText(targetPath);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string GetCanonicalBodyText(string targetPath)
    {
        string text = DecodeAuto(File.ReadAllBytes(targetPath));
        string newline = DetectNewline(text);
        (string body, _) = SplitIniVersion(text, newline);
        return NormalizeNewlines(body).TrimEnd('\n');
    }

    private static (string Body, string IniVersion) SplitIniVersion(string text, string newline)
    {
        string normalized = NormalizeNewlines(text);
        string[] lines = normalized.Split('\n');
        List<string> body = [];
        List<string> ini = [];
        bool inIniVersion = false;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Equals("[IniVersion]", StringComparison.OrdinalIgnoreCase))
            {
                inIniVersion = true;
                ini.Add(line);
                continue;
            }

            if (inIniVersion && trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith(']'))
                inIniVersion = false;

            if (inIniVersion)
                ini.Add(line);
            else
                body.Add(line);
        }

        string bodyText = string.Join(newline, body).TrimEnd('\r', '\n') + newline;
        string iniText = ini.Count == 0 ? string.Empty : string.Join(newline, ini).TrimEnd('\r', '\n') + newline;
        return (bodyText, iniText);
    }

    private static string DetectNewline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string DecodeAuto(byte[] data)
    {
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
        return encodingName.ToLowerInvariant() switch
        {
            "utf-16le-bom" => WithPreamble(Encoding.Unicode, text),
            "utf-16be-bom" => WithPreamble(Encoding.BigEndianUnicode, text),
            "utf-8-bom" => WithPreamble(new UTF8Encoding(true), text),
            "utf-8" => new UTF8Encoding(false).GetBytes(text),
            "windows-1252" => Encoding.GetEncoding(1252).GetBytes(text),
            _ => throw new ArgumentException($"Unsupported output encoding: {encodingName}")
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
}
