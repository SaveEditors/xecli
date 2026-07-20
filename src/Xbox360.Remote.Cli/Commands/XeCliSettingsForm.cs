using System.Text.Json;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

internal readonly record struct XeCliSettingsTheme(
    string Name,
    Color ShellBackground,
    Color CardBackground,
    Color FieldBackground,
    Color PrimaryText,
    Color SecondaryText,
    Color Accent,
    Color Border,
    Color Warning,
    Color Failure,
    Color Success);

internal sealed class XeCliSettingsForm : Form {
    private const int NavigationWidth = 176;
    private const int LabelColumnWidth = 190;

    private readonly CliConfig config;
    private readonly Action<string> previewTheme;
    private readonly string originalTheme;
    private readonly Dictionary<string, Panel> pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XeCliTerminalForm.TerminalButton> navigationButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Panel navigationPanel = new();
    private readonly FlowLayoutPanel navigationFlow = new();
    private readonly Label navigationSectionLabel = new();
    private readonly Panel contentHost = new();
    private readonly Panel footerPanel = new();
    private readonly Label validationLabel = new();
    private readonly ToolTip validationToolTip = new() { ShowAlways = true };

    private readonly TextBox defaultIpTextBox = CreateTextBox();
    private readonly XeCliSettingsNumeric xbdmPortInput = CreateNumericInput(1, 65535, 730);
    private readonly XeCliSettingsNumeric connectionTimeoutInput = CreateNumericInput(1000, 60000, CliPreferences.DefaultConnectionTimeoutMs, 500);
    private readonly CheckBox autoConnectCheckBox = CreateCheckBox("Connect to the default console when the terminal opens");
	private readonly CheckBox autoReconnectCheckBox = CreateCheckBox("Reconnect the terminal after an unexpected connection loss");
    private readonly XeCliSettingsNumeric reconnectAttemptsInput = CreateNumericInput(1, 10, CliPreferences.DefaultReconnectAttempts);
    private readonly XeCliSettingsNumeric reconnectDelayInput = CreateNumericInput(1, 60, CliPreferences.DefaultReconnectDelaySeconds);

    private readonly XeCliSettingsNumeric ftpPortInput = CreateNumericInput(1, 65535, 21);
    private readonly TextBox ftpUserTextBox = CreateTextBox();
    private readonly TextBox ftpPasswordTextBox = CreateTextBox();
    private readonly CheckBox showFtpPasswordCheckBox = CreateCheckBox("Show password");
    private readonly XeCliSettingsSelect ftpConflictComboBox = CreateComboBox();

    private readonly TextBox defaultLocalDirectoryTextBox = CreateTextBox();
    private readonly TextBox screenshotDirectoryTextBox = CreateTextBox();
    private readonly TextBox commandLogDirectoryTextBox = CreateTextBox();
    private readonly TextBox titleDatabasePathTextBox = CreateTextBox();
    private readonly TextBox titleDatabaseStatusTextBox = CreateReadOnlyTextBox("muted");
    private readonly XeCliSettingsSelect screenshotActionComboBox = CreateComboBox();
    private readonly TextBox storageModeTextBox = CreateReadOnlyTextBox("accent");
    private readonly TextBox configurationPathTextBox = CreateReadOnlyTextBox("muted");
    private readonly TextBox applicationDataPathTextBox = CreateReadOnlyTextBox("muted");

    private readonly XeCliSettingsSelect themeComboBox = CreateComboBox();
    private readonly XeCliSettingsSelect languageComboBox = CreateComboBox();
    private readonly CheckBox rememberWindowPlacementCheckBox = CreateCheckBox("Remember terminal size and position");
    private readonly CheckBox telemetryCheckBox = CreateCheckBox("Enable live console telemetry");
    private readonly CheckBox discordPresenceCheckBox = CreateCheckBox("Enable Discord Rich Presence when configured");
    private readonly TextBox discordClientIdTextBox = CreateTextBox();

    private readonly TextBox ghidraPathTextBox = CreateTextBox();
    private readonly TextBox ghidraJavaPathTextBox = CreateTextBox();
    private readonly TextBox ghidraProjectsPathTextBox = CreateTextBox();
    private readonly TextBox idaPathTextBox = CreateTextBox();
    private readonly TextBox idaPythonPathTextBox = CreateTextBox();
    private readonly TextBox idaUserPathTextBox = CreateTextBox();
    private readonly XeCliSettingsSelect idaBackendComboBox = CreateComboBox();
    private readonly TextBox xtafCliPathTextBox = CreateTextBox();

    private XeCliSettingsTheme theme;
    private string activePage = "General";
    private bool initializing = true;
    private bool saved;

    internal XeCliSettingsForm(
        CliConfig config,
        string currentTheme,
        Icon? ownerIcon,
        bool ownerTopMost,
        Action<string> previewTheme,
        string initialPage = "General",
        bool openThemeSelector = false) {
        this.config = config;
        this.previewTheme = previewTheme;
        originalTheme = XeCliTerminalForm.NormalizeTerminalThemeName(currentTheme);
        theme = XeCliTerminalForm.GetSettingsTheme(originalTheme);
        activePage = NormalizePageName(initialPage);

        Text = "XeCLI Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(960, 600);
        MinimumSize = new Size(780, 520);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        Icon = ownerIcon;
        TopMost = ownerTopMost;
        AccessibleName = "XeCLI settings";

        BuildLayout();
        LoadValues();
        ApplyTheme(theme);
        ShowPage(activePage);
        initializing = false;

        FormClosed += (_, _) => {
            if (!saved)
                previewTheme(originalTheme);
        };
        Shown += (_, _) => {
            ClampToWorkingArea();
            XeCliTerminalForm.ApplyNativeWindowTheme(this, theme.ShellBackground, theme.PrimaryText, theme.Border);
            if (openThemeSelector)
                BeginInvoke(new MethodInvoker(themeComboBox.OpenDropDown));
        };
    }

    protected override void Dispose(bool disposing) {
        if (disposing)
            validationToolTip.Dispose();
        base.Dispose(disposing);
    }

    private static string NormalizePageName(string? pageName) => pageName?.Trim().ToLowerInvariant() switch {
        "connection" => "Connection",
        "ftp" => "FTP",
        "paths" => "Paths",
        "tools" => "Tools",
        _ => "General"
    };

    private void BuildLayout() {
        TableLayoutPanel root = new() {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 2,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavigationWidth));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));

        Label brand = new() {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(20, 0, 0, 0),
            Text = "XeCLI",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point),
            Tag = "accent"
        };
        Panel header = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(24, 0, 20, 0) };
        Label title = new() {
            Dock = DockStyle.Fill,
            Text = "SETTINGS",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Point),
            Tag = "primary"
        };
        header.Controls.Add(title);

        navigationPanel.Dock = DockStyle.Fill;
        navigationPanel.Margin = Padding.Empty;
        navigationPanel.Padding = new Padding(12, 12, 12, 8);
        navigationFlow.Dock = DockStyle.Fill;
        navigationFlow.FlowDirection = FlowDirection.TopDown;
        navigationFlow.WrapContents = false;
        navigationFlow.AutoScroll = false;
        navigationFlow.Margin = Padding.Empty;
        navigationFlow.Padding = Padding.Empty;
        navigationFlow.Tag = "navigation-surface";
        navigationSectionLabel.Size = new Size(NavigationWidth - 24, 24);
        navigationSectionLabel.Margin = new Padding(0, 0, 0, 6);
        navigationSectionLabel.Padding = new Padding(10, 0, 0, 0);
        navigationSectionLabel.Text = "PREFERENCES";
        navigationSectionLabel.TextAlign = ContentAlignment.MiddleLeft;
        navigationSectionLabel.Font = new Font("Segoe UI", 7.75f, FontStyle.Bold, GraphicsUnit.Point);
        navigationSectionLabel.Tag = "muted";
        navigationSectionLabel.UseMnemonic = false;
        navigationFlow.Controls.Add(navigationSectionLabel);
        foreach (string name in new[] { "General", "Connection", "FTP", "Paths", "Tools" }) {
            XeCliTerminalForm.TerminalButton button = CreateButton(name.ToUpperInvariant());
            button.Name = "settings" + name + "Button";
            button.Size = new Size(NavigationWidth - 24, 38);
            button.Margin = new Padding(0, 0, 0, 5);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(16, 0, 8, 0);
            button.AccessibleName = name + " settings";
            button.Click += (_, _) => ShowPage(name);
            navigationButtons[name] = button;
            navigationFlow.Controls.Add(button);
        }
        navigationPanel.Controls.Add(navigationFlow);

        contentHost.Dock = DockStyle.Fill;
        contentHost.Margin = Padding.Empty;
        contentHost.Padding = new Padding(22, 8, 22, 8);
        pages["General"] = BuildGeneralPage();
        pages["Connection"] = BuildConnectionPage();
        pages["FTP"] = BuildFtpPage();
        pages["Paths"] = BuildPathsPage();
        pages["Tools"] = BuildToolsPage();
        foreach (Panel page in pages.Values)
            contentHost.Controls.Add(page);

        footerPanel.Dock = DockStyle.Fill;
        footerPanel.Margin = Padding.Empty;
        footerPanel.Padding = new Padding(16, 10, 16, 10);
        TableLayoutPanel footer = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        XeCliTerminalForm.TerminalButton resetButton = CreateButton("RESET PAGE");
        XeCliTerminalForm.TerminalButton cancelButton = CreateButton("CANCEL");
        XeCliTerminalForm.TerminalButton saveButton = CreateButton("SAVE");
        saveButton.Tag = "primary-action";
        resetButton.Dock = cancelButton.Dock = saveButton.Dock = DockStyle.Fill;
        resetButton.Margin = new Padding(0, 0, 8, 0);
        cancelButton.Margin = new Padding(0, 0, 8, 0);
        saveButton.Margin = Padding.Empty;
        resetButton.Click += (_, _) => ResetActivePage();
        cancelButton.Click += (_, _) => Close();
        saveButton.Click += (_, _) => SaveAndClose();
        cancelButton.DialogResult = DialogResult.Cancel;
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        validationLabel.Dock = DockStyle.Fill;
        validationLabel.TextAlign = ContentAlignment.MiddleRight;
        validationLabel.AutoEllipsis = true;
        validationLabel.Tag = "failure";
        footer.Controls.Add(resetButton, 0, 0);
        footer.Controls.Add(validationLabel, 1, 0);
        footer.Controls.Add(cancelButton, 2, 0);
        footer.Controls.Add(saveButton, 3, 0);
        footerPanel.Controls.Add(footer);

        root.Controls.Add(brand, 0, 0);
        root.Controls.Add(header, 1, 0);
        root.Controls.Add(navigationPanel, 0, 1);
        root.Controls.Add(contentHost, 1, 1);
        root.Controls.Add(footerPanel, 0, 2);
        root.SetColumnSpan(footerPanel, 2);
        Controls.Add(root);
    }

    private Panel BuildGeneralPage() {
        SettingsPageBuilder page = CreatePage("General");
        page.AddSection("APPEARANCE");
        page.AddRow("Theme", themeComboBox);
        page.AddRow("Language", languageComboBox);
        page.AddCheck(rememberWindowPlacementCheckBox);
        page.AddSection("STARTUP & PRIVACY");
        page.AddCheck(autoConnectCheckBox);
        page.AddCheck(telemetryCheckBox);
        page.AddCheck(discordPresenceCheckBox);
        page.AddRow("Discord client ID", discordClientIdTextBox);
        return page.Panel;
    }

    private Panel BuildConnectionPage() {
        SettingsPageBuilder page = CreatePage("Connection");
        page.AddSection("DEFAULT TARGET");
        page.AddRow("Console host or IP", defaultIpTextBox);
        page.AddRow("XBDM port", xbdmPortInput);
        page.AddRow("Timeout (ms)", connectionTimeoutInput);
        page.AddSection("RECONNECT POLICY");
        page.AddCheck(autoReconnectCheckBox);
        page.AddRow("Maximum attempts", reconnectAttemptsInput);
        page.AddRow("Delay (seconds)", reconnectDelayInput);
        return page.Panel;
    }

    private Panel BuildFtpPage() {
        SettingsPageBuilder page = CreatePage("FTP");
        page.AddSection("DEFAULT CREDENTIALS");
        page.AddRow("FTP port", ftpPortInput);
        page.AddRow("Username", ftpUserTextBox);
        ftpPasswordTextBox.UseSystemPasswordChar = true;
        page.AddRow("Password", ftpPasswordTextBox);
        page.AddCheck(showFtpPasswordCheckBox);
        page.AddSection("DESTINATION CONFLICTS");
        page.AddRow("Existing item", ftpConflictComboBox);
        showFtpPasswordCheckBox.CheckedChanged += (_, _) => ftpPasswordTextBox.UseSystemPasswordChar = !showFtpPasswordCheckBox.Checked;
        return page.Panel;
    }

    private Panel BuildPathsPage() {
        SettingsPageBuilder page = CreatePage("Paths", compact: true);
        page.AddSection("APPLICATION STORAGE");
        page.AddRow("Storage mode", storageModeTextBox);
        page.AddRow("Configuration file", configurationPathTextBox);
        page.AddRow("Application data", applicationDataPathTextBox);
        page.AddSection("WORKFLOW PATHS");
        page.AddPathRow("Local browser start", defaultLocalDirectoryTextBox);
        page.AddPathRow("Console captures", screenshotDirectoryTextBox);
        page.AddPathRow("Command logs", commandLogDirectoryTextBox);
        page.AddRow("After capture", screenshotActionComboBox);
        page.AddSection("TITLE METADATA");
        page.AddPathRow(
            "Title Database file",
            titleDatabasePathTextBox,
            allowFiles: true,
            filter: "Title Database files (*.csv;*.txt)|*.csv;*.txt|CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt");
        page.AddRow("Database status", titleDatabaseStatusTextBox);
        titleDatabasePathTextBox.TextChanged += (_, _) => RefreshTitleDatabaseStatus();
        return page.Panel;
    }

    private Panel BuildToolsPage() {
        SettingsPageBuilder page = CreatePage("Tools", compact: true);
        page.AddSection("GHIDRA");
        page.AddPathRow("Installation", ghidraPathTextBox);
        page.AddPathRow("Java", ghidraJavaPathTextBox);
        page.AddPathRow("Projects", ghidraProjectsPathTextBox);
        page.AddSection("IDA");
        page.AddPathRow("Installation", idaPathTextBox);
        page.AddPathRow("Python", idaPythonPathTextBox, allowFiles: true);
        page.AddPathRow("User directory", idaUserPathTextBox);
        page.AddRow("Backend", idaBackendComboBox);
        page.AddSection("XTAF-CLI");
        page.AddPathRow("Executable", xtafCliPathTextBox, allowFiles: true);
        return page.Panel;
    }

    private SettingsPageBuilder CreatePage(string name, bool compact = false) => new(name, CreateButton, compact);

    private void LoadValues() {
        defaultIpTextBox.Text = config.DefaultIp ?? string.Empty;
        xbdmPortInput.Value = ClampDecimal(config.DefaultPort ?? 730, xbdmPortInput);
        connectionTimeoutInput.Value = ClampDecimal(CliPreferences.GetConnectionTimeoutMs(config), connectionTimeoutInput);
        autoConnectCheckBox.Checked = config.TerminalAutoConnect == true;
        autoReconnectCheckBox.Checked = config.AutoReconnectEnabled != false;
        reconnectAttemptsInput.Value = ClampDecimal(CliPreferences.GetReconnectAttempts(config), reconnectAttemptsInput);
        reconnectDelayInput.Value = ClampDecimal(CliPreferences.GetReconnectDelaySeconds(config), reconnectDelayInput);

        ftpPortInput.Value = ClampDecimal(config.DefaultFtpPort ?? 21, ftpPortInput);
        ftpUserTextBox.Text = config.DefaultFtpUser ?? "xboxftp";
        ftpPasswordTextBox.Text = config.DefaultFtpPassword ?? "xboxftp";
        ftpConflictComboBox.Items.AddRange(["Ask every time", "Keep both", "Skip existing"]);
        ftpConflictComboBox.SelectedIndex = CliPreferences.NormalizeFtpConflictBehavior(config.FtpConflictBehavior) switch {
            "keep-both" => 1,
            "skip" => 2,
            _ => 0
        };

        defaultLocalDirectoryTextBox.Text = config.DefaultLocalDirectory ?? CliPreferences.GetDefaultLocalDirectory();
        screenshotDirectoryTextBox.Text = config.ScreenshotDirectory ?? CliPreferences.GetDefaultScreenshotDirectory();
        commandLogDirectoryTextBox.Text = config.CommandLogDirectory ?? CommandLogPaths.LogDirectory;
        titleDatabasePathTextBox.PlaceholderText = CliPaths.DefaultTitleDatabasePath;
        titleDatabasePathTextBox.Text = TitleIdDatabase.GetSettingsPath(config);
        storageModeTextBox.Text = CliPaths.StorageModeDisplayName;
        configurationPathTextBox.Text = CliPaths.ConfigPath;
        applicationDataPathTextBox.Text = CliPaths.DataRoot;
        screenshotActionComboBox.Items.AddRange(["Show notification", "Open XeCLI preview", "Show in folder"]);
        screenshotActionComboBox.SelectedIndex = CliPreferences.NormalizeScreenshotAction(config.ScreenshotAfterCapture) switch {
            "preview" => 1,
            "folder" => 2,
            _ => 0
        };

        foreach (string name in XeCliTerminalForm.TerminalThemeNames)
            themeComboBox.Items.Add(name);
        themeComboBox.ShowThemeSwatches = true;
        themeComboBox.ThemeResolver = XeCliTerminalForm.GetSettingsTheme;
        themeComboBox.SelectedItem = originalTheme;
        themeComboBox.SelectedIndexChanged += (_, _) => PreviewSelectedTheme();
        languageComboBox.Items.AddRange(["English", "Español"]);
        languageComboBox.SelectedIndex = string.Equals(config.UiLanguage, "es", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        rememberWindowPlacementCheckBox.Checked = config.RememberTerminalWindowPlacement != false;
        telemetryCheckBox.Checked = config.TerminalTelemetryEnabled != false;
        discordPresenceCheckBox.Checked = config.DiscordRichPresenceEnabled != false;
        discordClientIdTextBox.Text = config.DiscordClientId ?? string.Empty;

        ghidraPathTextBox.Text = config.GhidraPath ?? string.Empty;
        ghidraJavaPathTextBox.Text = config.GhidraJavaPath ?? string.Empty;
        ghidraProjectsPathTextBox.Text = config.GhidraProjectsPath ?? string.Empty;
        idaPathTextBox.Text = config.IdaPath ?? string.Empty;
        idaPythonPathTextBox.Text = config.IdaPythonPath ?? string.Empty;
        idaUserPathTextBox.Text = config.IdaUserPath ?? string.Empty;
        idaBackendComboBox.Items.AddRange(["auto", "batch", "idalib"]);
        string backend = config.IdaPreferredBackend?.Trim().ToLowerInvariant() ?? "auto";
        idaBackendComboBox.SelectedItem = idaBackendComboBox.Items.Contains(backend) ? backend : "auto";
        xtafCliPathTextBox.Text = config.XtafCliPath ?? string.Empty;
        RefreshTitleDatabaseStatus();
    }

    private void PreviewSelectedTheme() {
        if (initializing || themeComboBox.SelectedItem is not string selected)
            return;
        previewTheme(selected);
        ApplyTheme(XeCliTerminalForm.GetSettingsTheme(selected));
    }

    private void SaveAndClose() {
        validationLabel.Text = string.Empty;
        try {
            ValidateWritableDirectory(CliPaths.ConfigDirectory, "Application configuration");
            ValidateWritableDirectory(CliPaths.CachePath, "Application data");
            string? localDirectory = ValidateExistingDirectory(defaultLocalDirectoryTextBox.Text, "Local browser start");
            string? screenshotDirectory = ValidateWritableDirectory(screenshotDirectoryTextBox.Text, "Console captures");
            string? logDirectory = ValidateWritableDirectory(commandLogDirectoryTextBox.Text, "Command logs");
            string? titleDatabasePath = ValidateTitleDatabasePath(titleDatabasePathTextBox.Text);
            ValidateOptionalPath(ghidraPathTextBox.Text, "Ghidra installation");
            ValidateOptionalPath(ghidraJavaPathTextBox.Text, "Ghidra Java");
            ValidateOptionalPath(ghidraProjectsPathTextBox.Text, "Ghidra projects");
            ValidateOptionalPath(idaPathTextBox.Text, "IDA installation");
            ValidateOptionalPath(idaPythonPathTextBox.Text, "IDA Python");
            ValidateOptionalPath(idaUserPathTextBox.Text, "IDA user directory");
            ValidateOptionalPath(xtafCliPathTextBox.Text, "XTAF-CLI");

            string? host = NullIfWhiteSpace(defaultIpTextBox.Text);
            if (host?.Any(char.IsWhiteSpace) == true)
                throw new InvalidOperationException("Console host or IP cannot contain spaces.");
            string? discordClientId = NullIfWhiteSpace(discordClientIdTextBox.Text);
            if (discordClientId != null && !discordClientId.All(char.IsDigit))
                throw new InvalidOperationException("Discord client ID must contain digits only.");

            config.DefaultIp = host;
            config.DefaultPort = Decimal.ToInt32(xbdmPortInput.Value);
            config.ConnectionTimeoutMs = Decimal.ToInt32(connectionTimeoutInput.Value);
            config.TerminalAutoConnect = autoConnectCheckBox.Checked;
            config.AutoReconnectEnabled = autoReconnectCheckBox.Checked;
            config.ReconnectAttempts = Decimal.ToInt32(reconnectAttemptsInput.Value);
            config.ReconnectDelaySeconds = Decimal.ToInt32(reconnectDelayInput.Value);
            config.DefaultFtpPort = Decimal.ToInt32(ftpPortInput.Value);
            config.DefaultFtpUser = NullIfWhiteSpace(ftpUserTextBox.Text);
            config.DefaultFtpPassword = NullIfWhiteSpace(ftpPasswordTextBox.Text);
            config.FtpConflictBehavior = ftpConflictComboBox.SelectedIndex switch { 1 => "keep-both", 2 => "skip", _ => "ask" };
            config.DefaultLocalDirectory = localDirectory;
            config.ScreenshotDirectory = screenshotDirectory;
            config.CommandLogDirectory = logDirectory;
            config.TitleDatabasePath = titleDatabasePath == null ? null : CliPaths.GetStoredTitleDatabasePath(titleDatabasePath);
            config.ScreenshotAfterCapture = screenshotActionComboBox.SelectedIndex switch { 1 => "preview", 2 => "folder", _ => "notification" };
            config.TerminalTheme = XeCliTerminalForm.NormalizeTerminalThemeName(themeComboBox.SelectedItem as string);
            config.UiLanguage = languageComboBox.SelectedIndex == 1 ? "es" : "en";
            config.RememberTerminalWindowPlacement = rememberWindowPlacementCheckBox.Checked;
            config.TerminalTelemetryEnabled = telemetryCheckBox.Checked;
            config.DiscordRichPresenceEnabled = discordPresenceCheckBox.Checked;
            config.DiscordClientId = discordClientId;
            config.GhidraPath = NullIfWhiteSpace(ghidraPathTextBox.Text);
            config.GhidraJavaPath = NullIfWhiteSpace(ghidraJavaPathTextBox.Text);
            config.GhidraProjectsPath = NullIfWhiteSpace(ghidraProjectsPathTextBox.Text);
            config.IdaPath = NullIfWhiteSpace(idaPathTextBox.Text);
            config.IdaPythonPath = NullIfWhiteSpace(idaPythonPathTextBox.Text);
            config.IdaUserPath = NullIfWhiteSpace(idaUserPathTextBox.Text);
            config.IdaPreferredBackend = idaBackendComboBox.SelectedItem as string ?? "auto";
            config.XtafCliPath = NullIfWhiteSpace(xtafCliPathTextBox.Text);
            config.Save();
            TitleIdDatabase.Reload();
            saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException) {
            validationLabel.Text = ex.Message;
            validationToolTip.SetToolTip(validationLabel, ex.Message);
        }
    }

    private void ResetActivePage() {
        switch (activePage) {
            case "General":
                themeComboBox.SelectedItem = XeCliTerminalForm.DefaultTerminalThemeName;
                languageComboBox.SelectedIndex = 0;
                rememberWindowPlacementCheckBox.Checked = true;
                autoConnectCheckBox.Checked = false;
                telemetryCheckBox.Checked = true;
                discordPresenceCheckBox.Checked = true;
                discordClientIdTextBox.Clear();
                break;
            case "Connection":
                defaultIpTextBox.Text = "192.168.1.1";
                xbdmPortInput.Value = 730;
                connectionTimeoutInput.Value = CliPreferences.DefaultConnectionTimeoutMs;
                autoReconnectCheckBox.Checked = true;
                reconnectAttemptsInput.Value = CliPreferences.DefaultReconnectAttempts;
                reconnectDelayInput.Value = CliPreferences.DefaultReconnectDelaySeconds;
                break;
            case "FTP":
                ftpPortInput.Value = 21;
                ftpUserTextBox.Text = "xboxftp";
                ftpPasswordTextBox.Text = "xboxftp";
                showFtpPasswordCheckBox.Checked = false;
                ftpConflictComboBox.SelectedIndex = 0;
                break;
            case "Paths":
                defaultLocalDirectoryTextBox.Text = CliPreferences.GetDefaultLocalDirectory();
                screenshotDirectoryTextBox.Text = CliPreferences.GetDefaultScreenshotDirectory();
                commandLogDirectoryTextBox.Text = Path.Combine(CliPaths.CachePath, "logging");
                titleDatabasePathTextBox.Clear();
                screenshotActionComboBox.SelectedIndex = 0;
                break;
            case "Tools":
                foreach (TextBox box in new[] { ghidraPathTextBox, ghidraJavaPathTextBox, ghidraProjectsPathTextBox, idaPathTextBox, idaPythonPathTextBox, idaUserPathTextBox, xtafCliPathTextBox })
                    box.Clear();
                idaBackendComboBox.SelectedItem = "auto";
                break;
        }
    }

    private void ShowPage(string name) {
        activePage = name;
        foreach ((string pageName, Panel page) in pages) {
            page.Visible = string.Equals(pageName, name, StringComparison.OrdinalIgnoreCase);
            if (page.Visible)
                page.BringToFront();
        }
        RefreshNavigationTheme();
    }

    private void ApplyTheme(XeCliSettingsTheme value) {
        theme = value;
        BackColor = theme.ShellBackground;
        ForeColor = theme.PrimaryText;
        ApplyThemeRecursive(this);
        RefreshNavigationTheme();
        validationLabel.ForeColor = theme.Failure;
        XeCliTerminalForm.ApplyNativeWindowTheme(this, theme.ShellBackground, theme.PrimaryText, theme.Border);
        Invalidate(true);
        Update();
    }

    private void ApplyThemeRecursive(Control control) {
        control.BackColor = control switch {
            TextBox or XeCliSettingsNumeric or XeCliSettingsSelect => theme.FieldBackground,
            _ when ReferenceEquals(control, navigationPanel) => theme.CardBackground,
            _ when ReferenceEquals(control, navigationFlow) => theme.CardBackground,
            _ when ReferenceEquals(control, footerPanel) => theme.CardBackground,
            _ => theme.ShellBackground
        };
        control.ForeColor = (control.Tag as string) switch {
            "accent" => theme.Accent,
            "muted" => theme.SecondaryText,
            "failure" => theme.Failure,
            "warning" => theme.Warning,
            "success" => theme.Success,
            _ => theme.PrimaryText
        };
        if (control is XeCliSettingsSelect select)
            select.ApplyTheme(theme);
        else if (control is XeCliSettingsNumeric numeric)
            numeric.ApplyTheme(theme);
        else if (control is XeCliSettingsCheckBox checkBox)
            checkBox.ApplyTheme(theme);
        else if (control is XeCliTerminalForm.TerminalButton button)
            ApplyButtonTheme(button, string.Equals(button.Tag as string, "primary-action", StringComparison.Ordinal));
        foreach (Control child in control.Controls)
            ApplyThemeRecursive(child);
    }

    private void RefreshNavigationTheme() {
        int index = 0;
        foreach ((string name, XeCliTerminalForm.TerminalButton button) in navigationButtons) {
            bool selected = string.Equals(name, activePage, StringComparison.OrdinalIgnoreCase);
            ApplyButtonTheme(button, selected);
            button.EnabledTextColor = selected ? theme.PrimaryText : theme.SecondaryText;
            button.EnabledBackColor = selected
                ? Mix(theme.CardBackground, theme.Accent, 0.18f)
                : Mix(theme.CardBackground, theme.FieldBackground, index % 2 == 0 ? 0.14f : 0.26f);
            button.HoverBackColor = Mix(theme.CardBackground, theme.Accent, selected ? 0.24f : 0.11f);
            button.PressedBackColor = Mix(theme.CardBackground, theme.Accent, selected ? 0.30f : 0.17f);
            button.BorderColor = selected ? Mix(theme.Border, theme.Accent, 0.55f) : Mix(theme.CardBackground, theme.Border, 0.42f);
            button.SelectionRailColor = selected ? theme.Accent : Color.Empty;
            button.SelectionRailWidth = selected ? 4 : 0;
            button.CornerRadius = 5;
            button.DrawBorder = true;
            button.Invalidate();
            index++;
        }
    }

    private void ApplyButtonTheme(XeCliTerminalForm.TerminalButton button, bool primary) {
        button.EnabledBackColor = primary ? Mix(theme.FieldBackground, theme.Accent, 0.28f) : theme.FieldBackground;
        button.HoverBackColor = Mix(theme.FieldBackground, theme.Accent, primary ? 0.38f : 0.16f);
        button.PressedBackColor = Mix(theme.FieldBackground, theme.Accent, primary ? 0.48f : 0.24f);
        button.DisabledBackColor = Mix(theme.FieldBackground, theme.ShellBackground, 0.55f);
        button.BorderColor = primary ? theme.Accent : theme.Border;
        button.DisabledBorderColor = Mix(theme.FieldBackground, theme.Border, 0.40f);
        button.EnabledTextColor = primary ? theme.PrimaryText : theme.SecondaryText;
        button.DisabledTextColor = Mix(theme.FieldBackground, theme.SecondaryText, 0.45f);
        button.CornerRadius = 6;
        button.DrawBorder = true;
        button.Invalidate();
    }

    private void ClampToWorkingArea() {
        Rectangle area = Screen.FromControl(Owner ?? this).WorkingArea;
        int width = Math.Min(Width, Math.Max(720, area.Width - 32));
        int height = Math.Min(Height, Math.Max(480, area.Height - 32));
        Size = new Size(width, height);
        Location = new Point(area.Left + Math.Max(0, (area.Width - width) / 2), area.Top + Math.Max(0, (area.Height - height) / 2));
    }

    private static string? ValidateExistingDirectory(string? value, string label) {
        string? path = NullIfWhiteSpace(value);
        if (path == null)
            return null;
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path))
            throw new InvalidOperationException(label + " does not exist.");
        return path;
    }

    private static string? ValidateWritableDirectory(string? value, string label) {
        string? path = NullIfWhiteSpace(value);
        if (path == null)
            return null;
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(path);
        string probe = Path.Combine(path, ".xecli-write-" + Guid.NewGuid().ToString("N"));
        try {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new InvalidOperationException(label + " is not writable.", ex);
        }
        return path;
    }

    private static void ValidateOptionalPath(string? value, string label) {
        string? path = NullIfWhiteSpace(value);
        if (path == null)
            return;
        path = Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new InvalidOperationException(label + " was not found.");
    }

    private static string? ValidateTitleDatabasePath(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string fullPath = CliPaths.ResolveTitleDatabasePath(value);
        string extension = Path.GetExtension(fullPath);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Title Database file must use the .csv or .txt extension.");
        }
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Title Database file was not found: {fullPath}");

        using (FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
        }
        return fullPath;
    }

    private void RefreshTitleDatabaseStatus() {
        string? environmentPath = Environment.GetEnvironmentVariable(CliPaths.TitleDatabaseEnvironmentVariable);
        TitleIdDatabaseStatus status = TitleIdDatabase.InspectUserFile(titleDatabasePathTextBox.Text);
        string text;
        string tag;

        if (string.IsNullOrWhiteSpace(titleDatabasePathTextBox.Text)) {
            text = string.IsNullOrWhiteSpace(environmentPath)
                ? $"Not configured; default location is {CliPaths.DefaultTitleDatabasePath}"
                : $"Environment override active: {environmentPath.Trim()}";
            tag = string.IsNullOrWhiteSpace(environmentPath) ? "muted" : "warning";
        }
        else if (status.Warnings.Count > 0 || status.EntryCount == 0) {
            text = status.Warnings.Count > 0
                ? $"{status.EntryCount} valid entries; {status.Warnings.Count} warning(s)"
                : "No valid Title Database entries found";
            tag = "warning";
        }
        else {
            text = $"Ready: {status.EntryCount} entr{(status.EntryCount == 1 ? "y" : "ies")}";
            tag = "success";
        }

        if (!string.IsNullOrWhiteSpace(environmentPath) && !string.IsNullOrWhiteSpace(titleDatabasePathTextBox.Text))
            text += $"; {CliPaths.TitleDatabaseEnvironmentVariable} overrides this setting";
        if (string.Equals(status.SourcePath, CliPaths.LegacyTitleDatabasePath, StringComparison.OrdinalIgnoreCase))
            text += "; legacy titleids.local.csv compatibility path";

        titleDatabaseStatusTextBox.Text = text;
        titleDatabaseStatusTextBox.Tag = tag;
        titleDatabaseStatusTextBox.ForeColor = tag switch {
            "warning" => theme.Warning,
            "success" => theme.Success,
            _ => theme.SecondaryText
        };
        validationToolTip.SetToolTip(
            titleDatabaseStatusTextBox,
            status.Warnings.Count == 0 ? text : string.Join(Environment.NewLine, status.Warnings.Select(warning => warning.ToString())));
    }

    private static string? NullIfWhiteSpace(string? value) {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static decimal ClampDecimal(int value, XeCliSettingsNumeric input) => Math.Clamp(value, Decimal.ToInt32(input.Minimum), Decimal.ToInt32(input.Maximum));

    private static Color Mix(Color from, Color to, float amount) {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static TextBox CreateTextBox() => new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 0, 5) };

    private static TextBox CreateReadOnlyTextBox(string tag) => new() {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 5, 0, 5),
        ReadOnly = true,
        TabStop = false,
        Tag = tag
    };

    private static XeCliSettingsSelect CreateComboBox() => new();

    private static XeCliSettingsNumeric CreateNumericInput(int minimum, int maximum, int value, int increment = 1) => new() {
        Dock = DockStyle.Left,
        Width = 150,
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Increment = increment,
        ThousandsSeparator = true,
        Margin = new Padding(0, 5, 0, 5)
    };

    private static CheckBox CreateCheckBox(string text) => new XeCliSettingsCheckBox {
        Dock = DockStyle.Fill,
        Text = text,
        AutoSize = false,
        Margin = new Padding(0, 5, 0, 5),
        UseVisualStyleBackColor = false
    };

    private static XeCliTerminalForm.TerminalButton CreateButton(string text) => new() {
        Text = text,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point),
        CornerRadius = 6,
        BorderThickness = 1f,
        Margin = Padding.Empty
    };

    private sealed class SettingsPageBuilder {
        private readonly TableLayoutPanel layout;
        private readonly Func<string, XeCliTerminalForm.TerminalButton> buttonFactory;
        private readonly bool compact;
        private int row;

        internal Panel Panel { get; }

        internal SettingsPageBuilder(string name, Func<string, XeCliTerminalForm.TerminalButton> buttonFactory, bool compact) {
            this.buttonFactory = buttonFactory;
            this.compact = compact;
            Panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, AccessibleName = name + " settings page" };
            layout = new TableLayoutPanel {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            Panel.Controls.Add(layout);
        }

        internal void AddSection(string text) {
            Label label = new() { Dock = DockStyle.Fill, Text = text, TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point), Tag = "accent", Margin = compact ? new Padding(0, 3, 0, 1) : new Padding(0, 6, 0, 2), UseMnemonic = false };
            AddRowStyle(compact ? 23 : 28);
            layout.Controls.Add(label, 0, row);
            layout.SetColumnSpan(label, 3);
            row++;
        }

        internal void AddRow(string labelText, Control control, Control? action = null) {
            Label label = new() { Dock = DockStyle.Fill, Text = labelText, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, UseMnemonic = false };
            label.AccessibleName = labelText;
            control.AccessibleName = labelText;
            if (compact) {
                control.Margin = new Padding(control.Margin.Left, 2, control.Margin.Right, 2);
                if (action != null)
                    action.Margin = new Padding(action.Margin.Left, 2, action.Margin.Right, 2);
            }
            AddRowStyle(compact ? 30 : 34);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
            if (action != null)
                layout.Controls.Add(action, 2, row);
            row++;
        }

        internal void AddCheck(CheckBox checkBox) {
            AddRowStyle(32);
            layout.Controls.Add(checkBox, 1, row);
            layout.SetColumnSpan(checkBox, 2);
            row++;
        }

        internal void AddPathRow(string labelText, TextBox textBox, bool allowFiles = false, string? filter = null) {
            XeCliTerminalForm.TerminalButton browse = buttonFactory("...");
            browse.Dock = DockStyle.Fill;
            browse.Margin = new Padding(6, 5, 0, 5);
            browse.AccessibleName = "Browse for " + labelText;
            browse.Click += (_, _) => BrowsePath(textBox, allowFiles, filter);
            AddRow(labelText, textBox, browse);
        }

        private void AddRowStyle(float height) {
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        }

        private static void BrowsePath(TextBox target, bool allowFiles, string? filter) {
            if (allowFiles) {
                using OpenFileDialog dialog = new() {
                    CheckFileExists = true,
                    CheckPathExists = true,
                    FileName = File.Exists(target.Text) ? target.Text : string.Empty,
                    Filter = filter ?? "All files (*.*)|*.*"
                };
                if (dialog.ShowDialog(target.FindForm()) == DialogResult.OK)
                    target.Text = dialog.FileName;
                return;
            }
            using FolderBrowserDialog folder = new() { ShowNewFolderButton = true, InitialDirectory = Directory.Exists(target.Text) ? target.Text : string.Empty };
            if (folder.ShowDialog(target.FindForm()) == DialogResult.OK)
                target.Text = folder.SelectedPath;
        }
    }
}
