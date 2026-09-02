using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace UMMSetup;

internal static class SteamLocator
{
    private const string GameFolderName = "XCom-Enemy-Unknown";
    private const string AppId = "200510";

    public static string? FindXcomRoot()
    {
        foreach (string candidate in GetCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (Directory.Exists(Path.Combine(full, "XEW", "XComGame")))
                    return full;
            }
            catch
            {
            }
        }
        return null;
    }

    private static IEnumerable<string> GetCandidates()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? uninstall = localMachine.OpenSubKey($"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App {AppId}");
            if (uninstall?.GetValue("InstallLocation") is string installLocation && !string.IsNullOrWhiteSpace(installLocation))
                yield return installLocation;
        }

        foreach (string steamRoot in GetSteamRoots())
        {
            yield return Path.Combine(steamRoot, "steamapps", "common", GameFolderName);

            string libraries = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries))
                continue;

            string text;
            try { text = File.ReadAllText(libraries); }
            catch { continue; }

            foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s*\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
            {
                string path = match.Groups["path"].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(path))
                    yield return Path.Combine(path, "steamapps", "common", GameFolderName);
            }
        }

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", GameFolderName);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", GameFolderName);
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        using RegistryKey? currentUser = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam");
        if (currentUser?.GetValue("SteamPath") is string userSteam && !string.IsNullOrWhiteSpace(userSteam))
            yield return userSteam.Replace('/', Path.DirectorySeparatorChar);

        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? steam = localMachine.OpenSubKey("SOFTWARE\\Valve\\Steam");
            if (steam?.GetValue("InstallPath") is string installPath && !string.IsNullOrWhiteSpace(installPath))
                yield return installPath;
        }
    }
}
