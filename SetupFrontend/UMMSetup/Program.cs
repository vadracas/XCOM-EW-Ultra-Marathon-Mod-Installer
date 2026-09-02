using UMMInstallerCore;

namespace UMMSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--headless-install", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("--headless-preflight", StringComparison.OrdinalIgnoreCase)))
            return HeadlessRunner.Run(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
        return 0;
    }
}

internal static class HeadlessRunner
{
    public static int Run(string[] args)
    {
        string? resultFile = null;
        try
        {
            Dictionary<string, string> values = Parse(args);
            string mode = args.Any(a => a.Equals("--headless-install", StringComparison.OrdinalIgnoreCase))
                ? "install"
                : "preflight";

            string gameRoot = Required(values, "--game-root");
            string documentsRoot = Required(values, "--documents-root");
            string backupRoot = values.TryGetValue("--backup-root", out string? b)
                ? b
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Narzelith", "Ultra Marathon Mod", "Backups");
            resultFile = values.TryGetValue("--result-file", out string? r) ? r : null;

            using PayloadSession payload = EmbeddedPayload.ExtractRuntimePayload();
            InstallerOrchestrator.ValidatePreflight(payload.ManifestPath, payload.StageRoot, gameRoot, documentsRoot);

            string details;
            if (mode == "install")
            {
                string backupDirectory = InstallerOrchestrator.Install(payload.ManifestPath, payload.StageRoot, gameRoot, documentsRoot, backupRoot);
                details = $"MODE=INSTALL{Environment.NewLine}PREFLIGHT=PASS{Environment.NewLine}INSTALL=PASS{Environment.NewLine}BACKUP_DIR={backupDirectory}{Environment.NewLine}";
            }
            else
            {
                details = $"MODE=PREFLIGHT{Environment.NewLine}PREFLIGHT=PASS{Environment.NewLine}";
            }

            WriteResult(resultFile, details);
            return 0;
        }
        catch (Exception ex)
        {
            WriteResult(resultFile, $"RESULT=FAIL{Environment.NewLine}{ex}{Environment.NewLine}");
            return 10;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
                args[i].Equals("--headless-install", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("--headless-preflight", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= args.Length)
                throw new ArgumentException($"Missing value for {args[i]}");
            result[args[i]] = args[++i];
        }
        return result;
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value)
            : throw new ArgumentException($"Missing required argument {key}");

    private static void WriteResult(string? resultFile, string text)
    {
        if (string.IsNullOrWhiteSpace(resultFile))
            return;
        string full = Path.GetFullPath(resultFile);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, text);
    }
}
