using System.Diagnostics;
using System.Reflection;
using UMMInstallerCore;

namespace UMMSetup;

internal sealed class SetupForm : Form
{
    private static readonly Color Surface = Color.FromArgb(10, 13, 25);
    private static readonly Color SurfaceRaised = Color.FromArgb(16, 21, 39);
    private static readonly Color Accent = Color.FromArgb(27, 184, 192);
    private static readonly Color AccentHover = Color.FromArgb(39, 214, 218);
    private static readonly Color ActionGold = Color.FromArgb(190, 149, 40);
    private static readonly Color ActionGoldHover = Color.FromArgb(220, 177, 51);
    private static readonly Color ActionGoldDown = Color.FromArgb(151, 116, 28);
    private static readonly Color Border = Color.FromArgb(37, 69, 93);
    private static readonly Color TextPrimary = Color.FromArgb(234, 240, 244);
    private static readonly Color TextMuted = Color.FromArgb(124, 143, 160);

    private readonly Image _installerArt;
    private readonly MemoryStream _installerAudioStream;
    private readonly System.Media.SoundPlayer _installerAudioPlayer;

    private readonly TextBox _gameRoot = new();
    private readonly TextBox _documentsRoot = new();
    private readonly TextBox _backupRoot = new();
    private readonly RichTextBox _log = new();
    private readonly Label _state = new() { AutoSize = true, Text = "Waiting for validation." };
    private readonly Label _baseline = new() { AutoSize = true, Text = "Not validated" };
    private readonly ThemedProgressBar _progress = new();

    private readonly Button _validate = new() { Text = "VALIDATE" };
    private readonly Button _install = new() { Text = "INSTALL UMM", Enabled = false };
    private readonly Button _launch = new() { Text = "LAUNCH XCOM", Enabled = false };
    private readonly Button _openBackup = new() { Text = "OPEN BACKUP", Enabled = false };
    private readonly Button _audioButton = new() { Text = "AUDIO ON" };
    private readonly Button _closeButton = new() { Text = "×" };
    private readonly List<Button> _pathBrowseButtons = new();

    private string? _lastBackupDirectory;
    private bool _busy;
    private bool _audioEnabled = true;
    private bool _dragging;
    private Point _dragOffset;

    public SetupForm()
    {
        Text = "Ultra Marathon Mod Setup | Narzelith";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1180, 700);
        MinimumSize = new Size(1180, 700);
        MaximumSize = new Size(1180, 700);
        MaximizeBox = false;
        Icon = LoadEmbeddedIcon();
        BackColor = Color.Black;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;

        _installerArt = LoadEmbeddedArt();
        BackgroundImage = _installerArt;
        BackgroundImageLayout = ImageLayout.Zoom;

        _installerAudioStream = LoadEmbeddedAudio();
        _installerAudioPlayer = new System.Media.SoundPlayer(_installerAudioStream);
        _installerAudioPlayer.Load();

        _gameRoot.Text = SteamLocator.FindXcomRoot() ?? string.Empty;
        _documentsRoot.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _backupRoot.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Narzelith",
            "Ultra Marathon Mod",
            "Backups");

        Controls.Add(BuildInstallerCard());
        ConfigureWindowChrome();

        MouseDown += BeginWindowDrag;
        MouseMove += ContinueWindowDrag;
        MouseUp += EndWindowDrag;

        _validate.Click += async (_, _) => await ValidateAsync();
        _install.Click += async (_, _) => await InstallAsync();
        _launch.Click += (_, _) => LaunchGame();
        _openBackup.Click += (_, _) => OpenBackup();
        _audioButton.Click += (_, _) => ToggleAudio();
        _closeButton.Click += (_, _) => Close();

        Shown += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_gameRoot.Text))
                await ValidateAsync();
            else
                Append("XCOM EW Steam installation was not auto-detected. Select the XCom-Enemy-Unknown folder.");
        };

        FormClosing += (_, e) =>
        {
            if (!_busy)
                return;
            e.Cancel = true;
            System.Media.SystemSounds.Beep.Play();
        };

        FormClosed += (_, _) =>
        {
            _installerAudioPlayer.Stop();
            _installerAudioPlayer.Dispose();
            _installerAudioStream.Dispose();
            _installerArt.Dispose();
        };

        _installerAudioPlayer.PlayLooping();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            CreateParams createParams = base.CreateParams;
            createParams.ClassStyle |= CsDropShadow;
            return createParams;
        }
    }

    private Control BuildInstallerCard()
    {
        BorderedPanel card = new()
        {
            Location = new Point(30, 344),
            Size = new Size(760, 338),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            BackColor = Surface,
            BorderColor = Border
        };

        Panel accentLine = new()
        {
            Dock = DockStyle.Top,
            Height = 3,
            BackColor = Accent
        };
        card.Controls.Add(accentLine);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15, 10, 15, 10),
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 93));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        Label title = new()
        {
            AutoSize = true,
            Text = "ULTRA MARATHON MOD",
            ForeColor = Color.FromArgb(150, 229, 230),
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            Padding = new Padding(0, 1, 0, 0)
        };
        layout.Controls.Add(title, 0, 0);

        layout.Controls.Add(BuildPathGrid(), 0, 1);
        layout.Controls.Add(BuildStateRow(), 0, 2);
        layout.Controls.Add(BuildActionRow(), 0, 3);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.DetectUrls = false;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Color.FromArgb(7, 10, 19);
        _log.ForeColor = Color.FromArgb(132, 159, 176);
        _log.Font = new Font("Consolas", 8F);
        _log.Margin = new Padding(0, 3, 0, 3);
        _log.Text = "UMM installer ready. Clean XCOM EW + Long War + Long War 1.1 required." + Environment.NewLine;
        layout.Controls.Add(_log, 0, 4);

        layout.Controls.Add(BuildUtilityRow(), 0, 5);

        Label footer = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Hash-gated baseline • local package reconstruction • verified backup • automatic rollback",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(100, 128, 148),
            Font = new Font("Segoe UI", 7.5F),
            Padding = new Padding(0, 2, 0, 0)
        };
        layout.Controls.Add(footer, 0, 6);

        card.Controls.Add(layout);
        layout.BringToFront();
        accentLine.BringToFront();
        return card;
    }

    private Control BuildPathGrid()
    {
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));

        AddPathRow(grid, 0, "GAME ROOT", _gameRoot, "Select the XCom-Enemy-Unknown folder");
        AddPathRow(grid, 1, "DOCUMENTS", _documentsRoot, "Select your Documents folder");
        AddPathRow(grid, 2, "BACKUPS", _backupRoot, "Select the backup folder");
        return grid;
    }

    private void AddPathRow(TableLayoutPanel grid, int row, string labelText, TextBox box, string browseTitle)
    {
        Label label = new()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = labelText,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(100, 128, 148),
            Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold),
            Margin = Padding.Empty
        };

        StyleTextBox(box);
        box.Margin = new Padding(0, 3, 8, 3);
        box.TextChanged += (_, _) => InvalidateValidationAfterPathEdit();

        Button browse = new() { Text = "BROWSE" };
        StyleButton(browse, false);
        browse.Margin = new Padding(0, 3, 0, 3);
        browse.Click += (_, _) => BrowseFolder(box, browseTitle);
        _pathBrowseButtons.Add(browse);

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(box, 1, row);
        grid.Controls.Add(browse, 2, row);
    }

    private Control BuildStateRow()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 2, 0, 2),
            BackColor = SurfaceRaised,
            Padding = new Padding(10, 0, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));

        panel.Controls.Add(MakeMetaLabel("BASELINE"), 0, 0);
        _baseline.ForeColor = TextMuted;
        _baseline.BackColor = SurfaceRaised;
        _baseline.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_baseline, 1, 0);

        _state.ForeColor = TextPrimary;
        _state.BackColor = SurfaceRaised;
        _state.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_state, 2, 0);

        StyleButton(_audioButton, false);
        _audioButton.Dock = DockStyle.None;
        _audioButton.Size = new Size(84, 27);
        _audioButton.Anchor = AnchorStyles.Right;
        _audioButton.Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold);
        _audioButton.Margin = Padding.Empty;
        panel.Controls.Add(_audioButton, 3, 0);
        return panel;
    }

    private Control BuildActionRow()
    {
        TableLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));

        StyleButton(_install, true);
        StyleButton(_validate, false);
        StyleButton(_launch, false);
        StyleButton(_openBackup, false);

        _install.Margin = new Padding(0, 3, 8, 3);
        _validate.Margin = new Padding(0, 3, 8, 3);
        _launch.Margin = new Padding(0, 3, 8, 3);
        _openBackup.Margin = new Padding(0, 3, 0, 3);

        actions.Controls.Add(_install, 0, 0);
        actions.Controls.Add(_validate, 1, 0);
        actions.Controls.Add(_launch, 2, 0);
        actions.Controls.Add(_openBackup, 3, 0);
        return actions;
    }

    private Control BuildUtilityRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ThemedUtilityButton readme = new() { Text = "README" };
        ThemedUtilityButton notices = new() { Text = "THIRD-PARTY" };
        readme.Configure(SurfaceRaised, Border, Color.FromArgb(20, 49, 67), TextPrimary);
        notices.Configure(SurfaceRaised, Border, Color.FromArgb(20, 49, 67), TextPrimary);
        readme.Margin = new Padding(0, 3, 7, 3);
        notices.Margin = new Padding(0, 3, 7, 3);
        readme.Click += (_, _) => ShowText("Ultra Marathon Mod installer", EmbeddedPayload.ReadReadme());
        notices.Click += (_, _) => ShowThirdPartyNotices();

        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(4, 8, 0, 8);
        _progress.Configure(SurfaceRaised, Border, Accent, AccentHover);

        row.Controls.Add(readme, 0, 0);
        row.Controls.Add(notices, 1, 0);
        row.Controls.Add(_progress, 2, 0);
        return row;
    }

    private void ConfigureWindowChrome()
    {
        _closeButton.SetBounds(ClientSize.Width - 42, 10, 32, 28);
        _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _closeButton.AutoSize = false;
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.FlatAppearance.MouseOverBackColor = Accent;
        _closeButton.FlatAppearance.MouseDownBackColor = AccentHover;
        _closeButton.BackColor = Color.FromArgb(8, 11, 22);
        _closeButton.ForeColor = Color.FromArgb(214, 229, 235);
        _closeButton.Font = new Font("Segoe UI", 14F, FontStyle.Regular);
        _closeButton.TextAlign = ContentAlignment.MiddleCenter;
        _closeButton.TabStop = false;
        _closeButton.UseVisualStyleBackColor = false;
        Controls.Add(_closeButton);
        _closeButton.BringToFront();

        Paint += (_, e) =>
        {
            using Pen pen = new(Accent);
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        };
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = true;
        _dragOffset = e.Location;
        Capture = true;
    }

    private void ContinueWindowDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;
        Point mouse = MousePosition;
        Location = new Point(mouse.X - _dragOffset.X, mouse.Y - _dragOffset.Y);
    }

    private void EndWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = false;
        Capture = false;
    }

    private async Task ValidateAsync()
    {
        if (_busy)
            return;

        SetBusy(true, "Validating Long War 1.1 baseline...");
        _install.Enabled = false;
        try
        {
            string game = Path.GetFullPath(_gameRoot.Text.Trim());
            string docs = Path.GetFullPath(_documentsRoot.Text.Trim());
            await Task.Run(() =>
            {
                using PayloadSession payload = EmbeddedPayload.ExtractRuntimePayload();
                InstallerOrchestrator.ValidatePreflight(payload.ManifestPath, payload.StageRoot, game, docs);
            });

            Append("Preflight PASS: clean Long War 1.1 baseline and installer payload hashes verified.");
            _baseline.Text = "PASS";
            _baseline.ForeColor = AccentHover;
            _state.Text = "Ready to install";
            _install.Enabled = true;
        }
        catch (Exception ex)
        {
            Append("Preflight FAIL: " + DeepestMessage(ex));
            _baseline.Text = "FAIL";
            _baseline.ForeColor = Color.FromArgb(236, 82, 100);
            _state.Text = "Baseline validation failed";
        }
        finally
        {
            SetBusy(false, _state.Text);
        }
    }

    private async Task InstallAsync()
    {
        if (_busy)
            return;

        SetBusy(true, "Installing and verifying Ultra Marathon Mod...");
        _install.Enabled = false;
        _launch.Enabled = false;
        _openBackup.Enabled = false;
        try
        {
            string game = Path.GetFullPath(_gameRoot.Text.Trim());
            string docs = Path.GetFullPath(_documentsRoot.Text.Trim());
            string backups = Path.GetFullPath(_backupRoot.Text.Trim());
            string backupDirectory = await Task.Run(() =>
            {
                using PayloadSession payload = EmbeddedPayload.ExtractRuntimePayload();
                return InstallerOrchestrator.Install(payload.ManifestPath, payload.StageRoot, game, docs, backups);
            });

            _lastBackupDirectory = backupDirectory;
            Append("INSTALL PASS: every installer-owned target passed post-validation.");
            Append("Backup: " + backupDirectory);
            _baseline.Text = "PASS";
            _baseline.ForeColor = AccentHover;
            _state.Text = "Installation complete";
            _launch.Enabled = File.Exists(Path.Combine(game, "XEW", "Binaries", "Win32", "XComEW.exe"));
            _openBackup.Enabled = Directory.Exists(backupDirectory);
            MessageBox.Show(
                this,
                "Ultra Marathon Mod installed successfully.\n\nA versioned backup was created and final validation passed.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Append("INSTALL FAIL: " + DeepestMessage(ex));
            _state.Text = "Installation failed - rollback executed when modifications had begun";
            MessageBox.Show(this, DeepestMessage(ex), "Installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _state.Text);
        }
    }

    private void SetBusy(bool busy, string text)
    {
        _busy = busy;
        _validate.Enabled = !busy;
        _gameRoot.Enabled = !busy;
        _documentsRoot.Enabled = !busy;
        _backupRoot.Enabled = !busy;
        foreach (Button browse in _pathBrowseButtons)
            browse.Enabled = !busy;
        _audioButton.Enabled = !busy;
        _progress.SetBusy(busy);
        if (!busy)
            _progress.SetValue(_baseline.Text.Equals("PASS", StringComparison.OrdinalIgnoreCase) ? 100 : 0);
        _state.Text = text;
        UseWaitCursor = busy;
    }

    private void BrowseFolder(TextBox target, string description)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(target.Text))
            dialog.SelectedPath = target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    private void LaunchGame()
    {
        string exe = Path.Combine(_gameRoot.Text.Trim(), "XEW", "Binaries", "Win32", "XComEW.exe");
        if (!File.Exists(exe))
        {
            MessageBox.Show(this, "XComEW.exe was not found at the selected game root.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true
        });
    }

    private void OpenBackup()
    {
        if (!string.IsNullOrWhiteSpace(_lastBackupDirectory) && Directory.Exists(_lastBackupDirectory))
            Process.Start(new ProcessStartInfo(_lastBackupDirectory) { UseShellExecute = true });
    }

    private void ToggleAudio()
    {
        _audioEnabled = !_audioEnabled;
        if (_audioEnabled)
        {
            _installerAudioPlayer.PlayLooping();
            _audioButton.Text = "AUDIO ON";
            _audioButton.ForeColor = TextPrimary;
        }
        else
        {
            _installerAudioPlayer.Stop();
            _audioButton.Text = "AUDIO OFF";
            _audioButton.ForeColor = TextMuted;
        }
    }

    private void InvalidateValidationAfterPathEdit()
    {
        if (_busy)
            return;
        _baseline.Text = "Not validated";
        _baseline.ForeColor = TextMuted;
        _state.Text = "Path changed. Validate again.";
        _install.Enabled = false;
        _progress.SetValue(0);
    }

    private void ShowThirdPartyNotices()
    {
        using Form form = BuildTextDialog("Third-party notices", EmbeddedPayload.ReadThirdPartyNotices(), new Size(780, 600));
        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            BackColor = Surface
        };
        Button close = new() { Text = "CLOSE", AutoSize = false, Size = new Size(90, 28) };
        Button source = new() { Text = "EXPORT UPKUTILS SOURCE", AutoSize = false, Size = new Size(190, 28) };
        StyleButton(close, false);
        StyleButton(source, false);
        close.Click += (_, _) => form.Close();
        source.Click += (_, _) => ExportSource();
        footer.Controls.Add(close);
        footer.Controls.Add(source);
        form.Controls.Add(footer);
        footer.BringToFront();
        form.ShowDialog(this);
    }

    private void ExportSource()
    {
        using SaveFileDialog dialog = new()
        {
            FileName = "UPKUtils-v7.3-source.zip",
            Filter = "ZIP archive (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            EmbeddedPayload.ExportThirdPartySource(dialog.FileName);
            MessageBox.Show(this, "UPKUtils 7.3 source archive exported successfully.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, DeepestMessage(ex), "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowText(string title, string text)
    {
        using Form form = BuildTextDialog(title, text, new Size(780, 560));
        form.ShowDialog(this);
    }

    private static Form BuildTextDialog(string title, string text, Size size)
    {
        Form form = new()
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Size = size,
            BackColor = Surface,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9F)
        };
        TextBox box = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Text = text,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(7, 10, 19),
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        form.Controls.Add(box);
        return form;
    }

    private void Append(string text)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static string DeepestMessage(Exception ex)
    {
        Exception current = ex;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }

    private static Icon LoadEmbeddedIcon()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UMMSetup.Resources.UMMFlank.ico")
            ?? throw new InvalidDataException("Embedded UMM application icon is missing.");
        using Icon source = new(stream);
        return (Icon)source.Clone();
    }

    private static Image LoadEmbeddedArt()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UMMSetup.Resources.InstallerHero.png")
            ?? throw new InvalidDataException("Embedded UMM installer hero artwork is missing.");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static MemoryStream LoadEmbeddedAudio()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UMMSetup.Resources.InstallerAudio.wav")
            ?? throw new InvalidDataException("Embedded UMM installer audio is missing.");
        MemoryStream memory = new();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static Label MakeMetaLabel(string text) => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(100, 128, 148),
        Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold),
        Margin = Padding.Empty
    };

    private static void StyleTextBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Color.FromArgb(7, 11, 21);
        box.ForeColor = TextPrimary;
        box.Font = new Font("Segoe UI", 8.5F);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Dock = DockStyle.Fill;
        button.Height = 32;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? ActionGoldHover : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? ActionGoldHover : Color.FromArgb(20, 49, 67);
        button.FlatAppearance.MouseDownBackColor = primary ? ActionGoldDown : Color.FromArgb(17, 73, 89);
        button.BackColor = primary ? ActionGold : Color.FromArgb(14, 20, 37);
        button.ForeColor = primary ? Surface : TextPrimary;
        button.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    private sealed class ThemedProgressBar : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private Color _trackColor = SurfaceRaised;
        private Color _borderColor = Border;
        private Color _fillColor = Accent;
        private Color _highlightColor = AccentHover;
        private int _value;
        private bool _busy;
        private int _marqueeOffset;

        public ThemedProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _timer = new System.Windows.Forms.Timer { Interval = 35 };
            _timer.Tick += (_, _) =>
            {
                _marqueeOffset += 8;
                Invalidate();
            };
        }

        public void Configure(Color trackColor, Color borderColor, Color fillColor, Color highlightColor)
        {
            _trackColor = trackColor;
            _borderColor = borderColor;
            _fillColor = fillColor;
            _highlightColor = highlightColor;
            Invalidate();
        }

        public void SetValue(int value)
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            _timer.Enabled = busy;
            if (!busy)
                _marqueeOffset = 0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(_trackColor);
            Rectangle inner = ClientRectangle;
            inner.Inflate(-1, -1);
            if (inner.Width > 0 && inner.Height > 0)
            {
                if (_busy)
                {
                    int segmentWidth = Math.Max(28, inner.Width / 4);
                    int travel = inner.Width + segmentWidth;
                    int x = inner.Left + (_marqueeOffset % travel) - segmentWidth;
                    Rectangle segment = new(x, inner.Top, segmentWidth, inner.Height);
                    using SolidBrush brush = new(_highlightColor);
                    e.Graphics.FillRectangle(brush, Rectangle.Intersect(inner, segment));
                }
                else if (_value > 0)
                {
                    int fillWidth = Math.Max(1, inner.Width * _value / 100);
                    using SolidBrush brush = new(_fillColor);
                    e.Graphics.FillRectangle(brush, new Rectangle(inner.Left, inner.Top, fillWidth, inner.Height));
                }
            }

            using Pen pen = new(_borderColor);
            Rectangle borderRect = ClientRectangle;
            borderRect.Width -= 1;
            borderRect.Height -= 1;
            e.Graphics.DrawRectangle(pen, borderRect);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ThemedUtilityButton : Control
    {
        private Color _normalColor = SurfaceRaised;
        private Color _borderColor = Border;
        private Color _hoverColor = Color.FromArgb(20, 49, 67);
        private Color _textColor = TextPrimary;
        private bool _hovered;
        private bool _pressed;

        public ThemedUtilityButton()
        {
            Dock = DockStyle.Fill;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 7F, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Configure(Color normalColor, Color borderColor, Color hoverColor, Color textColor)
        {
            _normalColor = normalColor;
            _borderColor = borderColor;
            _hoverColor = hoverColor;
            _textColor = textColor;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = _hovered ? _hoverColor : _normalColor;
            if (_pressed)
                fill = Color.FromArgb(Math.Max(0, fill.R - 10), Math.Max(0, fill.G - 10), Math.Max(0, fill.B - 10));

            e.Graphics.Clear(fill);
            Rectangle rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            using Pen pen = new(_borderColor);
            e.Graphics.DrawRectangle(pen, rect);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, _textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    private sealed class BorderedPanel : Panel
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Border;

        public BorderedPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using Pen pen = new(BorderColor);
            Rectangle rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}
