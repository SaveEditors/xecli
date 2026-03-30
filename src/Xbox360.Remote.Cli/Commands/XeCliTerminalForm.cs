using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FluentFTP;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class XeCliTerminalForm : Form
{
	internal sealed class TerminalOptions
	{
		public string ExePath { get; init; } = "rgh.exe";

		public string Ip { get; init; } = "127.0.0.1";

		public int Port { get; init; } = 730;

		public int TimeoutMs { get; init; } = 4000;

		public int OpacityPercent { get; init; } = 100;

		public string? BackgroundPath { get; init; }

		public bool TelemetryEnabled { get; init; } = true;
	}

	private sealed class TelemetrySnapshot
	{
		public bool Connected { get; set; }

		public string? DebugName { get; set; }

		public string? Motherboard { get; set; }

		public uint? DashboardVersion { get; set; }

		public string? ExecutionState { get; set; }

		public uint? TitleId { get; set; }

		public string? TitleName { get; set; }

		public string? RunningXex { get; set; }

		public string? Gamertag { get; set; }

		public string? SignInStateText { get; set; }

		public uint? CpuTemp { get; set; }

		public uint? GpuTemp { get; set; }

		public uint? EdramTemp { get; set; }

		public uint? BoardTemp { get; set; }

		public int? FtpPort { get; set; }

		public string? FtpUser { get; set; }

		public string? ErrorText { get; set; }

		public List<string> Drives { get; } = new List<string>();

		public List<string> Plugins { get; } = new List<string>();

		public List<string> Modules { get; } = new List<string>();

		public double FtpRxKbps { get; set; }

		public double FtpTxKbps { get; set; }
	}

	private sealed class FileEntryView
	{
		public required string Name { get; init; }

		public required string FullPath { get; init; }

		public bool IsDirectory { get; init; }
	}

	private sealed class LocalBrowserSnapshot
	{
		public string? ResolvedPath { get; init; }

		public required List<FileEntryView> Entries { get; init; }

		public required List<string> Items { get; init; }

		public required string PathLabel { get; init; }
	}

	private const int MaxTerminalCharacters = 64000;

	private const int TerminalFlushBatchSize = 96;

	private const int ConnectBannerMinimumHoldMs = 1200;

	private const int TelemetryPollIntervalMs = 30000;

	private const string PanelDiagnosticsEnvVar = "XECLI_PANEL_DIAG";

	private const string PanelDiagnosticsPathEnvVar = "XECLI_TERMINAL_DIAG_PATH";

	private static readonly Color ShellBackground = Color.FromArgb(7, 10, 9);

	private static readonly Color CardBackground = Color.FromArgb(10, 14, 13);

	private static readonly Color TerminalBackground = Color.FromArgb(8, 13, 12);

	private static readonly Color AccentCyan = Color.FromArgb(242, 246, 242);

	private static readonly Color AccentPink = Color.FromArgb(166, 201, 154);

	private static readonly Color AccentGreen = Color.FromArgb(148, 255, 102);

	private static readonly Color AccentDim = Color.FromArgb(170, 186, 166);

	private static readonly Color BorderColor = Color.FromArgb(58, 112, 54);

	private static readonly Color WarningColor = Color.FromArgb(198, 136, 88);

	private static readonly Color ProbeHeaderColor = Color.FromArgb(150, 40, 150);

	private static readonly Color ProbeLeftColor = Color.FromArgb(150, 140, 40);

	private static readonly Color ProbeCenterColor = Color.FromArgb(40, 140, 150);

	private static readonly Color ProbeRightColor = Color.FromArgb(40, 150, 60);

	private static readonly Color ProbeFileColor = Color.FromArgb(150, 90, 40);

	private static readonly Color ProbeFooterColor = Color.FromArgb(150, 40, 60);

	private static readonly string[] XeCliSuggestions = new string[57]
	{
		"connect --ip ",
		"status",
		"status --quick",
		"status --json",
		"scan",
		"target",
		"ping",
		"profiles",
		"title",
		"reboot",
		"shutdown",
		"launch --path ",
		"notify --message ",
		"screenshot",
		"ftp target --ip ",
		"ftp list --path ",
		"ftp get --remote ",
		"ftp put --local ",
		"save list",
		"save extract --title ",
		"save inject --title ",
		"content list",
		"content delete --id ",
		"nand dump",
		"xell info",
		"xell boot",
		"xell kv-export",
		"xbdm info",
		"xbdm raw ",
		"xbdm modules list",
		"xbdm modules info --name ",
		"xbdm modules dump --name ",
		"xbdm mem regions",
		"xbdm mem dump --start ",
		"xbdm mem hexdump --start ",
		"xbdm mem strings --start ",
		"xbdm mem find --type ",
		"xbdm xex dump",
		"xbdm debug watch",
		"xbdm threads list",
		"jrpc cpu-key",
		"jrpc temps",
		"jrpc title-id",
		"jrpc dashboard",
		"jrpc motherboard",
		"smc version",
		"fan set --speed ",
		"fan show",
		"led set --preset all-green",
		"led state",
		"ghidra config",
		"ghidra analyze --xex ",
		"ida check",
		"ida analyze --xex ",
		"help",
		"--version",
		"exit"
	};

	private readonly TerminalOptions options;

	private readonly Font shellFont = new Font("Consolas", 8.75f, FontStyle.Regular, GraphicsUnit.Point);

	private readonly Font shellFontBold = new Font("Consolas", 9.25f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Font headerFont = new Font("Consolas", 12.5f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Label headerLabel = new Label();

	private readonly Label connectionStatusLabel = new Label();

	private readonly Label leftStatusLabel = new Label();

	private readonly Label leftConsoleLabel = new Label();

	private readonly Label leftSignInLabel = new Label();

	private readonly Label rightNetworkLabel = new Label();

	private readonly Label rightTempLabel = new Label();

	private readonly Label rightDetailLabel = new Label();

	private readonly Button connectButton = new Button();

	private readonly Button disconnectButton = new Button();

	private readonly Button screenshotButton = new Button();

	private readonly Button pluginsTabButton = new Button();

	private readonly Button modulesTabButton = new Button();

	private readonly SlimListPanel inventoryList = new SlimListPanel();

	private readonly SlimListPanel drivesList = new SlimListPanel();

	private readonly SlimListPanel transferQueueList = new SlimListPanel();

	private readonly List<string> transferQueueEntries = new List<string>();

	private readonly PictureBox logoPictureBox = new PictureBox();

	private readonly FtpTrafficGraphControl ftpTrafficGraph = new FtpTrafficGraphControl();

	private readonly Label footerStatusLabel = new Label();

	private readonly Label footerHintLabel = new Label();

	private readonly Label mainShellBannerLabel = new Label();

	private readonly Label shellBadgeLabel = new Label();

	private readonly Label shellScaffoldLabel = new Label();

	private readonly RichTextBox terminalOutput = new RichTextBox();

	private readonly TextBox commandInput = new TextBox();

	private readonly ListBox suggestionList = new ListBox();

	private readonly Panel suggestionHost = new Panel();

	private readonly Label localPathLabel = new Label();

	private readonly Label remotePathLabel = new Label();

	private readonly Button localUpButton = new Button();

	private readonly Button localRefreshButton = new Button();

	private readonly Button remoteUpButton = new Button();

	private readonly Button remoteRefreshButton = new Button();

	private readonly SlimListPanel localFileList = new SlimListPanel();

	private readonly SlimListPanel remoteFileList = new SlimListPanel();

	private readonly System.Windows.Forms.Timer heartbeatTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer telemetryTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer terminalFlushTimer = new System.Windows.Forms.Timer();

	private readonly Queue<(string Text, Color Color)> pendingTerminalLines = new Queue<(string, Color)>();

	private readonly object pendingTerminalLock = new object();

	private readonly CancellationTokenSource formLifetimeCts = new CancellationTokenSource();

	private bool telemetryPollInFlight;

	private bool commandInFlight;

	private bool connectAttemptInFlight;

	private DateTime connectAttemptStartedUtc;

	private bool inventoryShowsModules;

	private bool shellDisconnected;

	private bool terminalFlushScheduled;

	private Process? activeCommandProcess;

	private CancellationTokenSource? commandCts;

	private CancellationTokenSource? localBrowseCts;

	private CancellationTokenSource? remoteBrowseCts;

	private long lastNetSentBytes;

	private long lastNetReceivedBytes;

	private DateTime lastNetSampleUtc;

	private TelemetrySnapshot? latestSnapshot;

	private readonly List<FileEntryView> localEntries = new List<FileEntryView>();

	private readonly List<FileEntryView> remoteEntries = new List<FileEntryView>();

	private string? localCurrentPath;

	private string remoteCurrentPath = "/";

	private DateTime nextRemoteRefreshAllowedUtc = DateTime.MinValue;

	private int localBrowseVersion;

	private int remoteBrowseVersion;

	private readonly DiscordRpcService? discordRpcService;

	internal XeCliTerminalForm(TerminalOptions options)
	{
		this.options = options;
		LogBootstrap("ctor-start");
		base.Text = "XeCLI Terminal";
		base.StartPosition = FormStartPosition.CenterScreen;
		Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 920);
		int num = Math.Min(1600, Math.Max(1120, workingArea.Width - 32));
		int num2 = Math.Min(920, Math.Max(700, workingArea.Height - 32));
		base.MinimumSize = new Size(Math.Min(1240, num), Math.Min(760, num2));
		base.Size = new Size(num, num2);
		base.BackColor = ShellBackground;
		base.ForeColor = Color.White;
		base.Font = shellFont;
		if (options.OpacityPercent < 100)
		{
			base.Opacity = Math.Max(0.55, Math.Min((double)options.OpacityPercent / 100.0, 1.0));
		}
		base.KeyPreview = true;
		InitializeLayout();
		LogRootLayoutSnapshot("ctor-after-initialize-layout");
		InitializeInteractiveActions();
		discordRpcService = DiscordRpcService.CreateIfConfigured();
		discordRpcService?.Start();
		localCurrentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		base.Load += delegate
		{
			LogRootLayoutSnapshot("load");
		};
		base.Shown += delegate
		{
			LogRootLayoutSnapshot("shown");
			LoadOptionalBackground();
			LoadXeCliLogo();
			if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
			{
				BeginInvoke(new MethodInvoker(delegate
				{
					CaptureVisualSnapshot("shown");
				}));
				_ = Task.Run(async delegate
				{
					try
					{
						await Task.Delay(1800, formLifetimeCts.Token).ConfigureAwait(false);
						if (!IsDisposed && IsHandleCreated)
						{
							BeginInvoke(new MethodInvoker(delegate
							{
								CaptureVisualSnapshot("after-1800ms");
							}));
						}
						await Task.Delay(1800, formLifetimeCts.Token).ConfigureAwait(false);
						if (!IsDisposed && IsHandleCreated)
						{
							BeginInvoke(new MethodInvoker(delegate
							{
								CaptureVisualSnapshot("after-3600ms");
							}));
						}
					}
					catch
					{
					}
				});
			}
		};
		InitializeFileManagerInteractions();
		terminalFlushTimer.Interval = 80;
		terminalFlushTimer.Tick += delegate
		{
			FlushPendingTerminalOutput();
		};
		heartbeatTimer.Interval = 1000;
		heartbeatTimer.Tick += delegate
		{
			UpdateConnectionStatusIndicator();
		};
		heartbeatTimer.Start();
		if (options.TelemetryEnabled)
		{
			telemetryTimer.Interval = TelemetryPollIntervalMs;
			telemetryTimer.Tick += async delegate
			{
			await PollTelemetrySafeAsync();
			};
		}
		_ = RefreshLocalBrowserAsync();
		AppendSystemLine("XeCLI terminal shell ready.", AccentGreen);
		AppendSystemLine("Session bus idle. Connect to activate live thermals, traffic, queue, and title state.", AccentDim);
		AppendSystemLine("Local commands remain available while the link is offline.", AccentDim);
		AppendSystemLine("Type any XeCLI command, for example: status, nand dump, xell info", AccentDim);
		AppendSystemLine("Type 'exit' to close this shell UI.", AccentDim);
		UpdateConnectionStatusIndicator();
		UpdateShellScaffold();
		UpdateLiveHints();
		base.FormClosing += delegate
		{
			CancelAllBackgroundWork();
		};
		base.FormClosed += delegate
		{
			terminalFlushTimer.Stop();
			heartbeatTimer.Stop();
			telemetryTimer.Stop();
			discordRpcService?.Dispose();
			lock (pendingTerminalLock)
			{
				pendingTerminalLines.Clear();
			}
			logoPictureBox.Image?.Dispose();
			base.BackgroundImage?.Dispose();
			formLifetimeCts.Dispose();
			LogBootstrap("closed");
		};
		LogBootstrap("ctor-end");
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle &= -33554433;
			return createParams;
		}
	}

	private void InitializeLayout()
	{
		try
		{
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.BackColor = ShellBackground;
			tableLayoutPanel.RowCount = 4;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.Padding = new Padding(8);
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 176f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
			base.Controls.Add(tableLayoutPanel);
			Panel panel = CreateCardPanel(CardBackground, new Padding(10, 4, 10, 4));
			ApplyProbeTheme(panel, ProbeHeaderColor);
			TracePanelDiagnostics(panel, "root-header");
			panel.Dock = DockStyle.Fill;
			panel.Margin = new Padding(0, 0, 0, 8);
			headerLabel.Dock = DockStyle.Fill;
			headerLabel.AutoEllipsis = true;
			headerLabel.TextAlign = ContentAlignment.MiddleLeft;
			headerLabel.Font = shellFontBold;
			headerLabel.ForeColor = Color.WhiteSmoke;
			headerLabel.Text = "PANEL   |   SYSTEM   |   TERMINAL";
			panel.Controls.Add(headerLabel);
			tableLayoutPanel.Controls.Add(panel, 0, 0);
			TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
			tableLayoutPanel2.Dock = DockStyle.Fill;
			tableLayoutPanel2.BackColor = ShellBackground;
			tableLayoutPanel2.RowCount = 1;
			tableLayoutPanel2.ColumnCount = 3;
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17f));
			tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 1);
			LogBootstrap("layout-before-left");
			Panel panel2 = BuildLeftPanel();
			LogBootstrap($"layout-after-left bounds={panel2.Bounds}");
			LogBootstrap("layout-before-center");
			Panel panel3 = BuildCenterPanel();
			LogBootstrap($"layout-after-center bounds={panel3.Bounds}");
			LogBootstrap("layout-before-right");
			Panel panel4 = BuildRightPanel();
			LogBootstrap($"layout-after-right bounds={panel4.Bounds}");
			tableLayoutPanel2.Controls.Add(panel2, 0, 0);
			tableLayoutPanel2.Controls.Add(panel3, 1, 0);
			tableLayoutPanel2.Controls.Add(panel4, 2, 0);
			LogBootstrap("layout-before-file");
			Panel panel5 = BuildFileStripPanel();
			TracePanelDiagnostics(panel5, "root-file");
			LogBootstrap($"layout-after-file bounds={panel5.Bounds}");
			tableLayoutPanel.Controls.Add(panel5, 0, 2);
			Panel panel6 = CreateCardPanel(CardBackground, new Padding(10, 2, 10, 2));
			ApplyProbeTheme(panel6, ProbeFooterColor);
			TracePanelDiagnostics(panel6, "root-footer");
			panel6.Dock = DockStyle.Fill;
			panel6.Margin = new Padding(0, 8, 0, 0);
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
			tableLayoutPanel3.Dock = DockStyle.Fill;
			tableLayoutPanel3.RowCount = 1;
			tableLayoutPanel3.ColumnCount = 2;
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
			footerStatusLabel.Dock = DockStyle.Fill;
			footerStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
			footerStatusLabel.ForeColor = AccentGreen;
			footerStatusLabel.Font = shellFont;
			footerStatusLabel.Text = "SESSION READY";
			footerHintLabel.Dock = DockStyle.Fill;
			footerHintLabel.TextAlign = ContentAlignment.MiddleRight;
			footerHintLabel.ForeColor = Color.WhiteSmoke;
			footerHintLabel.Font = shellFont;
			footerHintLabel.Text = "ENTER = RUN · TAB = AUTOFILL";
			tableLayoutPanel3.Controls.Add(footerStatusLabel, 0, 0);
			tableLayoutPanel3.Controls.Add(footerHintLabel, 1, 0);
			panel6.Controls.Add(tableLayoutPanel3);
			tableLayoutPanel.Controls.Add(panel6, 0, 3);
			LogBootstrap("layout-after-footer");
			LogRootLayoutSnapshot("layout-ready");
		}
		catch (Exception ex)
		{
			LogBootstrap($"layout-exception {ex.GetType().FullName}: {ex.Message}");
			throw;
		}
	}

	private Panel BuildLeftPanel()
	{
		Panel panel = CreateCardPanel(CardBackground, new Padding(10, 10, 10, 10));
		ApplyProbeTheme(panel, ProbeLeftColor);
		TracePanelDiagnostics(panel, "root-left");
		panel.Margin = new Padding(0, 0, 8, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.RowCount = 7;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 108f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16f));
		connectionStatusLabel.Dock = DockStyle.Fill;
		connectionStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		connectionStatusLabel.ForeColor = AccentGreen;
		connectionStatusLabel.Font = headerFont;
		connectionStatusLabel.Text = "● DISCONNECTED";
		leftConsoleLabel.Dock = DockStyle.Fill;
		leftConsoleLabel.TextAlign = ContentAlignment.TopLeft;
		leftConsoleLabel.ForeColor = Color.WhiteSmoke;
		leftConsoleLabel.Font = shellFont;
		leftConsoleLabel.Text = "UNIT PROFILE\nDEBUG   standby\nBOARD   unknown\nDASH    --\nSTATE   idle";
		leftStatusLabel.Dock = DockStyle.Fill;
		leftStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		leftStatusLabel.Font = shellFontBold;
		leftStatusLabel.ForeColor = Color.WhiteSmoke;
		leftStatusLabel.Text = "THERMALS\nCPU    --°C\nGPU    --°C\nEDRAM  --°C\nBOARD  --°C";
		drivesList.Dock = DockStyle.Fill;
		drivesList.BackColor = TerminalBackground;
		drivesList.ForeColor = Color.WhiteSmoke;
		drivesList.Font = shellFont;
		leftSignInLabel.Dock = DockStyle.Fill;
		leftSignInLabel.TextAlign = ContentAlignment.MiddleLeft;
		leftSignInLabel.ForeColor = AccentDim;
		leftSignInLabel.Font = shellFont;
		leftSignInLabel.Text = "DETECTED DRIVES";
		Panel panel2 = new Panel();
		panel2.Dock = DockStyle.Fill;
		panel2.BackColor = CardBackground;
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.RowCount = 2;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
		ConfigureActionButton(connectButton, "CONNECT");
		ConfigureActionButton(disconnectButton, "DISCONNECT");
		ConfigureActionButton(screenshotButton, "SCREENSHOT");
		tableLayoutPanel2.Controls.Add(connectButton, 0, 0);
		tableLayoutPanel2.Controls.Add(disconnectButton, 1, 0);
		tableLayoutPanel2.Controls.Add(screenshotButton, 0, 1);
		tableLayoutPanel2.SetColumnSpan(screenshotButton, 2);
		panel2.Controls.Add(tableLayoutPanel2);
		Label label = new Label
		{
			Dock = DockStyle.Fill,
			ForeColor = AccentDim,
			Font = shellFont,
			TextAlign = ContentAlignment.MiddleLeft,
			Text = "SESSION ACTIONS"
		};
		tableLayoutPanel.Controls.Add(connectionStatusLabel, 0, 0);
		tableLayoutPanel.Controls.Add(leftConsoleLabel, 0, 1);
		tableLayoutPanel.Controls.Add(leftStatusLabel, 0, 2);
		tableLayoutPanel.Controls.Add(drivesList, 0, 3);
		tableLayoutPanel.Controls.Add(leftSignInLabel, 0, 4);
		tableLayoutPanel.Controls.Add(panel2, 0, 5);
		tableLayoutPanel.Controls.Add(label, 0, 6);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildCenterPanel()
	{
		LogBootstrap("center-start");
		try
		{
			Panel panel = CreateCardPanel(TerminalBackground, new Padding(3));
			ApplyProbeTheme(panel, ProbeCenterColor);
			TracePanelDiagnostics(panel, "root-center");
			LogBootstrap($"center-after-root-panel bounds={panel.Bounds}");
			panel.Margin = new Padding(0, 0, 8, 0);
			LogBootstrap($"center-after-margin bounds={panel.Bounds}");
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.RowCount = 2;
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			LogBootstrap("center-after-outer-table");
			Panel panel2 = CreateCardPanel(CardBackground, new Padding(6, 5, 6, 5));
			LogBootstrap($"center-after-hero-card bounds={panel2.Bounds}");
			panel2.Dock = DockStyle.Fill;
			panel2.Margin = new Padding(0, 0, 0, 6);
			TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
			tableLayoutPanel2.Dock = DockStyle.Fill;
			tableLayoutPanel2.ColumnCount = 2;
			tableLayoutPanel2.RowCount = 1;
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			logoPictureBox.Dock = DockStyle.Fill;
			logoPictureBox.BackColor = Color.Transparent;
			logoPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
			logoPictureBox.Margin = new Padding(0, 1, 8, 1);
			logoPictureBox.Padding = new Padding(0);
			Panel panel3 = new Panel();
			LogBootstrap($"center-after-side-shell bounds={panel3.Bounds}");
			panel3.Dock = DockStyle.Fill;
			panel3.BackColor = CardBackground;
			Label label = new Label();
			label.Dock = DockStyle.Top;
			label.Height = 16;
			label.TextAlign = ContentAlignment.MiddleLeft;
			label.ForeColor = Color.FromArgb(214, AccentDim);
			label.Font = shellFont;
			label.Text = "PLUGINS / MODULES";
			shellBadgeLabel.Dock = DockStyle.Top;
			shellBadgeLabel.Height = 16;
			shellBadgeLabel.TextAlign = ContentAlignment.MiddleLeft;
			shellBadgeLabel.ForeColor = Color.FromArgb(230, AccentGreen);
			shellBadgeLabel.Font = new Font("Consolas", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
			shellBadgeLabel.Text = "XECLI // LIVE INVENTORY";
			Panel panel4 = new Panel();
			LogBootstrap($"center-after-toggle-strip bounds={panel4.Bounds}");
			panel4.Dock = DockStyle.Top;
			panel4.Height = 24;
			panel4.BackColor = CardBackground;
			ConfigureToggleButton(pluginsTabButton, "Plugins");
			ConfigureToggleButton(modulesTabButton, "Modules");
			pluginsTabButton.Left = 0;
			modulesTabButton.Left = pluginsTabButton.Width + 6;
			panel4.Controls.Add(pluginsTabButton);
			panel4.Controls.Add(modulesTabButton);
			inventoryList.Dock = DockStyle.Fill;
			inventoryList.BackColor = TerminalBackground;
			inventoryList.ForeColor = Color.White;
			inventoryList.Font = shellFont;
			panel3.Controls.Add(inventoryList);
			panel3.Controls.Add(panel4);
			panel3.Controls.Add(label);
			panel3.Controls.Add(shellBadgeLabel);
			tableLayoutPanel2.Controls.Add(logoPictureBox, 0, 0);
			tableLayoutPanel2.Controls.Add(panel3, 1, 0);
			panel2.Controls.Add(tableLayoutPanel2);
			LogBootstrap("center-after-hero-layout");
			Panel panel5 = CreateCardPanel(TerminalBackground, new Padding(0));
			LogBootstrap($"center-after-shell-card bounds={panel5.Bounds}");
			panel5.Dock = DockStyle.Fill;
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
			tableLayoutPanel3.Dock = DockStyle.Top;
			tableLayoutPanel3.Height = 24;
			tableLayoutPanel3.BackColor = TerminalBackground;
			tableLayoutPanel3.ColumnCount = 2;
			tableLayoutPanel3.RowCount = 1;
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144f));
			Label label2 = new Label();
			label2.Dock = DockStyle.Fill;
			label2.TextAlign = ContentAlignment.MiddleLeft;
			label2.Padding = new Padding(8, 0, 0, 0);
			label2.Font = new Font("Consolas", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
			label2.ForeColor = Color.FromArgb(156, AccentDim);
			label2.Text = "MAIN SHELL";
			mainShellBannerLabel.Dock = DockStyle.Fill;
			mainShellBannerLabel.Margin = new Padding(0, 2, 4, 2);
			mainShellBannerLabel.TextAlign = ContentAlignment.MiddleCenter;
			mainShellBannerLabel.Font = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
			mainShellBannerLabel.ForeColor = Color.WhiteSmoke;
			mainShellBannerLabel.BackColor = Color.FromArgb(12, 22, 18);
			mainShellBannerLabel.Text = "LINK OFFLINE";
			tableLayoutPanel3.Controls.Add(label2, 0, 0);
			tableLayoutPanel3.Controls.Add(mainShellBannerLabel, 1, 0);
			panel5.Controls.Add(tableLayoutPanel3);
			Panel panel6 = CreateScaffoldSurfacePanel(Color.FromArgb(9, 16, 14), new Padding(10, 8, 10, 8), 28, 28);
			LogBootstrap($"center-after-terminal-card bounds={panel6.Bounds}");
			panel6.Dock = DockStyle.Fill;
			panel6.Margin = new Padding(0);
			shellScaffoldLabel.Dock = DockStyle.Top;
			shellScaffoldLabel.Height = 54;
			shellScaffoldLabel.TextAlign = ContentAlignment.MiddleLeft;
			shellScaffoldLabel.Font = shellFont;
			shellScaffoldLabel.ForeColor = Color.WhiteSmoke;
			shellScaffoldLabel.Text = BuildDisconnectedShellText();
			panel6.Controls.Add(shellScaffoldLabel);
			if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
			{
				terminalOutput.Dock = DockStyle.Fill;
				terminalOutput.BackColor = Color.FromArgb(9, 16, 14);
				terminalOutput.ForeColor = Color.WhiteSmoke;
				terminalOutput.BorderStyle = BorderStyle.FixedSingle;
				terminalOutput.Font = shellFont;
				terminalOutput.ReadOnly = true;
				terminalOutput.DetectUrls = false;
				terminalOutput.HideSelection = false;
				terminalOutput.ScrollBars = RichTextBoxScrollBars.Vertical;
				terminalOutput.WordWrap = false;
				panel6.Controls.Add(terminalOutput);
			}
			else
			{
				TextBox textBox = new TextBox();
				textBox.Dock = DockStyle.Fill;
				textBox.BackColor = Color.FromArgb(9, 16, 14);
				textBox.ForeColor = Color.WhiteSmoke;
				textBox.BorderStyle = BorderStyle.FixedSingle;
				textBox.Font = shellFont;
				textBox.ReadOnly = true;
				textBox.Multiline = true;
				textBox.ScrollBars = ScrollBars.Vertical;
				textBox.WordWrap = false;
				textBox.Text = "DIAGNOSTIC TERMINAL SURFACE\r\nRichTextBox temporarily bypassed.";
				panel6.Controls.Add(textBox);
			}
			suggestionHost.Dock = DockStyle.Bottom;
			suggestionHost.Height = 84;
			suggestionHost.BackColor = CardBackground;
			suggestionHost.Padding = new Padding(1);
			suggestionList.Dock = DockStyle.Fill;
			suggestionList.BackColor = CardBackground;
			suggestionList.ForeColor = AccentCyan;
			suggestionList.BorderStyle = BorderStyle.None;
			suggestionList.Font = shellFont;
			suggestionList.Visible = false;
			suggestionList.IntegralHeight = false;
			suggestionList.DoubleClick += delegate
			{
				ApplySelectedSuggestion();
			};
			suggestionHost.Controls.Add(suggestionList);
			panel6.Controls.Add(suggestionHost);
			commandInput.Dock = DockStyle.Bottom;
			commandInput.Height = 28;
			commandInput.BackColor = CardBackground;
			commandInput.ForeColor = AccentGreen;
			commandInput.BorderStyle = BorderStyle.FixedSingle;
			commandInput.Font = shellFontBold;
			commandInput.PlaceholderText = "Connect or enter a XeCLI command for this session...";
			commandInput.KeyDown += HandleCommandInputKeyDown;
			commandInput.TextChanged += delegate
			{
				RefreshSuggestions();
			};
			panel6.Controls.Add(commandInput);
			LogBootstrap("center-after-terminal-children");
			panel5.Controls.Add(panel6);
			tableLayoutPanel.Controls.Add(panel2, 0, 0);
			tableLayoutPanel.Controls.Add(panel5, 0, 1);
			panel.Controls.Add(tableLayoutPanel);
			LogRootLayoutSnapshot("center-end");
			return panel;
		}
		catch (Exception ex)
		{
			LogBootstrap($"center-exception {ex.GetType().FullName}: {ex.Message}");
			throw;
		}
	}

	private Panel BuildRightPanel()
	{
		Panel panel = CreateCardPanel(CardBackground, new Padding(8, 8, 8, 8));
		ApplyProbeTheme(panel, ProbeRightColor);
		TracePanelDiagnostics(panel, "root-right");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Panel panel2 = CreateCardPanel(TerminalBackground, new Padding(8, 6, 8, 6));
		panel2.Margin = new Padding(0, 0, 0, 6);
		rightNetworkLabel.Dock = DockStyle.Fill;
		rightNetworkLabel.TextAlign = ContentAlignment.TopLeft;
		rightNetworkLabel.ForeColor = Color.WhiteSmoke;
		rightNetworkLabel.Font = shellFont;
		rightNetworkLabel.Padding = new Padding(2, 1, 2, 1);
		rightNetworkLabel.Text = BuildDisconnectedStatusText();
		panel2.Controls.Add(rightNetworkLabel);
		Panel panel3 = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panel3.Margin = new Padding(0, 0, 0, 6);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.RowCount = 3;
		tableLayoutPanel2.ColumnCount = 1;
			tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		Label label = new Label();
		label.Dock = DockStyle.Fill;
		label.TextAlign = ContentAlignment.MiddleLeft;
		label.ForeColor = AccentGreen;
		label.Font = shellFontBold;
		label.Text = "TRAFFIC";
		ftpTrafficGraph.Dock = DockStyle.Fill;
		rightTempLabel.Dock = DockStyle.Fill;
		rightTempLabel.TextAlign = ContentAlignment.TopLeft;
		rightTempLabel.ForeColor = Color.WhiteSmoke;
		rightTempLabel.Font = shellFont;
		rightTempLabel.Padding = new Padding(2, 1, 2, 0);
		rightTempLabel.Text = "IDLE · waiting for FTP traffic";
		tableLayoutPanel2.Controls.Add(label, 0, 0);
		tableLayoutPanel2.Controls.Add(ftpTrafficGraph, 0, 1);
		tableLayoutPanel2.Controls.Add(rightTempLabel, 0, 2);
		panel3.Controls.Add(tableLayoutPanel2);
		Panel panel4 = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panel4.Margin = new Padding(0, 0, 0, 6);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.RowCount = 2;
		tableLayoutPanel3.ColumnCount = 1;
			tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label label2 = new Label();
		label2.Dock = DockStyle.Fill;
		label2.TextAlign = ContentAlignment.MiddleLeft;
		label2.ForeColor = AccentGreen;
		label2.Font = shellFontBold;
		label2.Text = "QUEUE";
		transferQueueList.Dock = DockStyle.Fill;
		transferQueueList.BackColor = TerminalBackground;
		transferQueueList.ForeColor = Color.WhiteSmoke;
		transferQueueList.Font = shellFont;
		transferQueueList.SetItems(new string[2] { "No transfer activity yet", "Queued dumps and FTP jobs appear here" });
		tableLayoutPanel3.Controls.Add(label2, 0, 0);
		tableLayoutPanel3.Controls.Add(transferQueueList, 0, 1);
		panel4.Controls.Add(tableLayoutPanel3);
		Panel panel5 = CreateCardPanel(TerminalBackground, new Padding(6, 6, 6, 6));
		rightDetailLabel.Dock = DockStyle.Fill;
		rightDetailLabel.TextAlign = ContentAlignment.TopLeft;
		rightDetailLabel.ForeColor = Color.WhiteSmoke;
		rightDetailLabel.Font = shellFont;
		rightDetailLabel.Padding = new Padding(2, 2, 2, 2);
		rightDetailLabel.Text = BuildDisconnectedSessionText();
		panel5.Controls.Add(rightDetailLabel);
		tableLayoutPanel.Controls.Add(panel2, 0, 0);
		tableLayoutPanel.Controls.Add(panel3, 0, 1);
		tableLayoutPanel.Controls.Add(panel4, 0, 2);
		tableLayoutPanel.Controls.Add(panel5, 0, 3);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildFileStripPanel()
	{
		Panel panel = CreateCardPanel(CardBackground, new Padding(8, 4, 8, 4));
		ApplyProbeTheme(panel, ProbeFileColor);
		TracePanelDiagnostics(panel, "root-file");
		panel.Margin = new Padding(0, 6, 0, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 2;
		tableLayoutPanel.RowCount = 2;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		Label label = new Label
		{
			Dock = DockStyle.Fill,
			ForeColor = Color.FromArgb(190, AccentDim),
			Font = shellFontBold,
			TextAlign = ContentAlignment.MiddleLeft,
			Text = "FILE MANAGER / LOCAL + FTP"
		};
		tableLayoutPanel.Controls.Add(label, 0, 0);
		tableLayoutPanel.SetColumnSpan(label, 2);
		Panel panel2 = BuildLocalFilePane();
		Panel panel3 = BuildRemoteFilePane();
		tableLayoutPanel.Controls.Add(panel2, 0, 1);
		tableLayoutPanel.Controls.Add(panel3, 1, 1);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildLocalFilePane()
	{
		Panel panel = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panel.Margin = new Padding(0, 0, 5, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 3;
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
		localPathLabel.Dock = DockStyle.Fill;
		localPathLabel.ForeColor = AccentDim;
		localPathLabel.Font = shellFont;
		localPathLabel.TextAlign = ContentAlignment.MiddleLeft;
		localPathLabel.AutoEllipsis = true;
		localPathLabel.Text = "PC / user";
		ConfigureFilePaneButton(localUpButton, "UP");
		ConfigureFilePaneButton(localRefreshButton, "REF");
		localFileList.Dock = DockStyle.Fill;
		localFileList.BackColor = TerminalBackground;
		localFileList.ForeColor = Color.WhiteSmoke;
		localFileList.Font = shellFont;
		tableLayoutPanel.Controls.Add(localPathLabel, 0, 0);
		tableLayoutPanel.SetColumnSpan(localPathLabel, 3);
		tableLayoutPanel.Controls.Add(localUpButton, 1, 1);
		tableLayoutPanel.Controls.Add(localRefreshButton, 2, 1);
		tableLayoutPanel.Controls.Add(localFileList, 0, 2);
		tableLayoutPanel.SetColumnSpan(localFileList, 3);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildRemoteFilePane()
	{
		Panel panel = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panel.Margin = new Padding(5, 0, 0, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 3;
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
		remotePathLabel.Dock = DockStyle.Fill;
		remotePathLabel.ForeColor = AccentDim;
		remotePathLabel.Font = shellFont;
		remotePathLabel.TextAlign = ContentAlignment.MiddleLeft;
		remotePathLabel.AutoEllipsis = true;
		remotePathLabel.Text = "FTP /";
		ConfigureFilePaneButton(remoteUpButton, "UP");
		ConfigureFilePaneButton(remoteRefreshButton, "REF");
		remoteFileList.Dock = DockStyle.Fill;
		remoteFileList.BackColor = TerminalBackground;
		remoteFileList.ForeColor = Color.WhiteSmoke;
		remoteFileList.Font = shellFont;
		tableLayoutPanel.Controls.Add(remotePathLabel, 0, 0);
		tableLayoutPanel.SetColumnSpan(remotePathLabel, 3);
		tableLayoutPanel.Controls.Add(remoteUpButton, 1, 1);
		tableLayoutPanel.Controls.Add(remoteRefreshButton, 2, 1);
		tableLayoutPanel.Controls.Add(remoteFileList, 0, 2);
		tableLayoutPanel.SetColumnSpan(remoteFileList, 3);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private void ConfigureFilePaneButton(Button button, string text)
	{
		button.Dock = DockStyle.Fill;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderColor = BorderColor;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(26, 44, 30);
		button.BackColor = CardBackground;
		button.ForeColor = AccentCyan;
		button.Font = shellFont;
		button.Text = text;
		button.Cursor = Cursors.Hand;
	}

	private void InitializeFileManagerInteractions()
	{
		localUpButton.Click += delegate
		{
			NavigateLocalUp();
		};
		localRefreshButton.Click += async delegate
		{
			await RefreshLocalBrowserAsync();
		};
		remoteUpButton.Click += async delegate
		{
			NavigateRemoteUp();
			await RefreshRemoteBrowserAsync(force: true);
		};
		remoteRefreshButton.Click += async delegate
		{
			await RefreshRemoteBrowserAsync(force: true);
		};
		localFileList.ItemActivated += HandleLocalItemActivated;
		remoteFileList.ItemActivated += async delegate(int index)
		{
			await HandleRemoteItemActivatedAsync(index);
		};
	}

	private void HandleLocalItemActivated(int index)
	{
		if (index < 0 || index >= localEntries.Count)
		{
			return;
		}
		FileEntryView fileEntryView = localEntries[index];
		if (fileEntryView.IsDirectory)
		{
			localCurrentPath = fileEntryView.FullPath;
			_ = RefreshLocalBrowserAsync();
		}
		else
		{
			OpenPath(fileEntryView.FullPath, isFile: true);
		}
	}

	private async Task HandleRemoteItemActivatedAsync(int index)
	{
		if (index < 0 || index >= remoteEntries.Count)
		{
			return;
		}
		FileEntryView fileEntryView = remoteEntries[index];
		if (fileEntryView.IsDirectory)
		{
			remoteCurrentPath = FtpHelpers.NormalizePath(fileEntryView.FullPath);
			await RefreshRemoteBrowserAsync(force: true);
		}
		else
		{
			AppendSystemLine("Remote file: " + fileEntryView.FullPath, AccentDim);
		}
	}

	private void NavigateLocalUp()
	{
		if (string.IsNullOrWhiteSpace(localCurrentPath))
		{
			return;
		}
		try
		{
			DirectoryInfo parent = Directory.GetParent(localCurrentPath);
			localCurrentPath = parent?.FullName;
			_ = RefreshLocalBrowserAsync();
		}
		catch
		{
			localCurrentPath = null;
			_ = RefreshLocalBrowserAsync();
		}
	}

	private void NavigateRemoteUp()
	{
		remoteCurrentPath = GetRemoteParentPath(remoteCurrentPath);
	}

	private async Task RefreshLocalBrowserAsync()
	{
		if (base.IsDisposed)
		{
			return;
		}
		int num = Interlocked.Increment(ref localBrowseVersion);
		CancelAndDispose(ref localBrowseCts);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		localBrowseCts = cancellationTokenSource;
		string text = localCurrentPath;
		localPathLabel.Text = string.IsNullOrWhiteSpace(text) ? "PC :: This PC" : ("PC :: " + text);
		localFileList.SetItems(new string[1] { "Loading local view..." });
		try
		{
			LocalBrowserSnapshot localBrowserSnapshot = await Task.Run(() => LoadLocalBrowserSnapshot(text, cancellationTokenSource.Token), cancellationTokenSource.Token);
			if (base.IsDisposed || cancellationTokenSource.IsCancellationRequested || num != localBrowseVersion)
			{
				return;
			}
			localCurrentPath = localBrowserSnapshot.ResolvedPath;
			localPathLabel.Text = localBrowserSnapshot.PathLabel;
			localEntries.Clear();
			localEntries.AddRange(localBrowserSnapshot.Entries);
			localFileList.SetItems(localBrowserSnapshot.Items);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!base.IsDisposed && num == localBrowseVersion)
			{
				localEntries.Clear();
				localFileList.SetItems(new string[1] { "Local browse failed: " + ex.Message });
			}
		}
	}

	private static LocalBrowserSnapshot LoadLocalBrowserSnapshot(string? path, CancellationToken cancellationToken)
	{
		List<FileEntryView> list = new List<FileEntryView>();
		if (string.IsNullOrWhiteSpace(path))
		{
			foreach (DriveInfo item in DriveInfo.GetDrives().OrderBy((DriveInfo d) => d.Name, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				string text = item.IsReady ? (item.DriveFormat + ", " + item.DriveType) : item.DriveType.ToString();
				list.Add(new FileEntryView
				{
					Name = item.Name + "  " + text,
					FullPath = item.RootDirectory.FullName,
					IsDirectory = true
				});
			}
			List<string> items = list.Select((FileEntryView entry) => "[DIR] " + entry.Name).ToList();
			if (items.Count == 0)
			{
				items.Add("(empty)");
			}
			return new LocalBrowserSnapshot
			{
				ResolvedPath = null,
				Entries = list,
				Items = items,
				PathLabel = "PC :: This PC"
			};
		}
		if (!Directory.Exists(path))
		{
			return LoadLocalBrowserSnapshot(null, cancellationToken);
		}
		try
		{
			foreach (string item2 in Directory.GetDirectories(path).OrderBy((string p) => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				list.Add(new FileEntryView
				{
					Name = Path.GetFileName(item2),
					FullPath = item2,
					IsDirectory = true
				});
			}
			foreach (string item3 in Directory.GetFiles(path).OrderBy((string p) => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				list.Add(new FileEntryView
				{
					Name = Path.GetFileName(item3),
					FullPath = item3,
					IsDirectory = false
				});
			}
		}
		catch (Exception ex)
		{
			list.Clear();
			list.Add(new FileEntryView
			{
				Name = "Access denied: " + ex.Message,
				FullPath = string.Empty,
				IsDirectory = false
			});
		}
		List<string> list2 = list.Select((FileEntryView entry) => entry.IsDirectory ? ("[DIR] " + entry.Name) : ("      " + entry.Name)).ToList();
		if (list2.Count == 0)
		{
			list2.Add("(empty)");
		}
		return new LocalBrowserSnapshot
		{
			ResolvedPath = path,
			Entries = list,
			Items = list2,
			PathLabel = "PC :: " + path
		};
	}

	private async Task RefreshRemoteBrowserAsync(bool force = false)
	{
		if (shellDisconnected || base.IsDisposed || (!force && DateTime.UtcNow < nextRemoteRefreshAllowedUtc))
		{
			return;
		}
		int num = Interlocked.Increment(ref remoteBrowseVersion);
		CancelAndDispose(ref remoteBrowseCts);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		remoteBrowseCts = cancellationTokenSource;
		string text = FtpHelpers.NormalizePath(remoteCurrentPath);
		nextRemoteRefreshAllowedUtc = DateTime.UtcNow.AddSeconds(force ? 1.0 : 4.0);
		remotePathLabel.Text = "FTP / " + text + "  :: loading";
		remoteFileList.SetItems(new string[1] { "Loading remote view..." });
		try
		{
			(string pathText, List<FileEntryView> entries, List<string> items) valueTuple = await Task.Run(() => LoadRemoteBrowserSnapshotAsync(text, cancellationTokenSource.Token), cancellationTokenSource.Token);
			if (base.IsDisposed || cancellationTokenSource.IsCancellationRequested || num != remoteBrowseVersion)
			{
				return;
			}
			remoteEntries.Clear();
			remoteEntries.AddRange(valueTuple.entries);
			remotePathLabel.Text = "FTP / " + valueTuple.pathText;
			remoteFileList.SetItems(valueTuple.items);
			remoteCurrentPath = valueTuple.pathText;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!base.IsDisposed && num == remoteBrowseVersion)
			{
				nextRemoteRefreshAllowedUtc = DateTime.UtcNow.AddSeconds(10.0);
				remotePathLabel.Text = "FTP / " + text;
				remoteFileList.SetItems(new string[1] { "Remote browse failed: " + ex.Message });
			}
		}
	}

	private async Task<(string PathText, List<FileEntryView> Entries, List<string> Items)> LoadRemoteBrowserSnapshotAsync(string remotePath, CancellationToken cancellationToken)
	{
		List<FileEntryView> list = new List<FileEntryView>();
		CliConfig cliConfig = CliConfig.Load();
		int num = cliConfig.DefaultFtpPort ?? 21;
		string user = TrimOrNull(cliConfig.DefaultFtpUser) ?? "xboxftp";
		string pass = cliConfig.DefaultFtpPassword ?? "xboxftp";
		int timeoutMs = Math.Clamp(options.TimeoutMs, 1200, 7000);
		string text = FtpHelpers.NormalizePath(remotePath);
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(timeoutMs);
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(options.Ip, num, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationTokenSource.Token);
		(FtpListItem[], bool) tuple = await FtpHelpers.GetListingWithFallbackAsync(asyncFtpClient, text);
		FtpListItem[] item = tuple.Item1;
		if (!string.Equals(text, "/", StringComparison.Ordinal))
		{
			list.Add(new FileEntryView
			{
				Name = "..",
				FullPath = GetRemoteParentPath(text),
				IsDirectory = true
			});
		}
		foreach (FtpListItem item2 in item.OrderBy((FtpListItem i) => i.Type == FtpObjectType.File).ThenBy((FtpListItem i) => i.Name, StringComparer.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(item2.Name))
			{
				continue;
			}
			bool isDirectory = item2.Type != FtpObjectType.File;
			string fullPath = FtpHelpers.NormalizePath(!string.IsNullOrWhiteSpace(item2.FullName) ? item2.FullName : CombineRemotePath(text, item2.Name));
			list.Add(new FileEntryView
			{
				Name = item2.Name,
				FullPath = fullPath,
				IsDirectory = isDirectory
			});
		}
		List<string> list2 = list.Select(delegate(FileEntryView e)
		{
			if (!e.IsDirectory)
			{
				return "      " + e.Name;
			}
			return "[DIR] " + e.Name;
		}).ToList();
		if (list2.Count == 0)
		{
			list2.Add("(empty)");
		}
		return (text, list, list2);
	}

	private static string CombineRemotePath(string parentPath, string name)
	{
		string text = FtpHelpers.NormalizePath(parentPath).TrimEnd('/');
		if (string.IsNullOrEmpty(text))
		{
			text = "/";
		}
		if (text == "/")
		{
			return "/" + name.Trim('/');
		}
		return text + "/" + name.Trim('/');
	}

	private static string GetRemoteParentPath(string path)
	{
		string text = FtpHelpers.NormalizePath(path).TrimEnd('/');
		if (string.IsNullOrWhiteSpace(text) || text == "/")
		{
			return "/";
		}
		int num = text.LastIndexOf('/');
		if (num <= 0)
		{
			return "/";
		}
		return text.Substring(0, num);
	}

	private static void CancelAndDispose(ref CancellationTokenSource? cts)
	{
		CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref cts, null);
		if (cancellationTokenSource == null)
		{
			return;
		}
		try
		{
			cancellationTokenSource.Cancel();
		}
		catch
		{
		}
		cancellationTokenSource.Dispose();
	}

	private void CancelActiveCommand()
	{
		CancelAndDispose(ref commandCts);
		Process process = activeCommandProcess;
		if (process == null)
		{
			return;
		}
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}
		finally
		{
			activeCommandProcess = null;
		}
	}

	private void CancelAllBackgroundWork()
	{
		try
		{
			formLifetimeCts.Cancel();
		}
		catch
		{
		}
		CancelAndDispose(ref localBrowseCts);
		CancelAndDispose(ref remoteBrowseCts);
		CancelActiveCommand();
	}

	private static Panel CreateCardPanel(Color background, Padding padding)
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		panel.BackColor = background;
		panel.Padding = padding;
		panel.Paint += delegate(object? sender, PaintEventArgs args)
		{
			try
			{
				if (sender is Control control)
				{
					LogPanelEvent("paint", control, "card");
					int width = Math.Max(0, control.Width);
					int height = Math.Max(0, control.Height);
					if (width < 2 || height < 2)
					{
						LogPanelEvent("paint-skip", control, "card");
						return;
					}
					args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					using Pen pen = new Pen(Color.FromArgb(118, BorderColor), 1f);
					Rectangle rectangle = new Rectangle(0, 0, width - 1, height - 1);
					args.Graphics.DrawRectangle(pen, rectangle);
					using Pen pen2 = new Pen(Color.FromArgb(72, AccentGreen), 1f);
					args.Graphics.DrawLine(pen2, rectangle.Left + 1, rectangle.Top + 1, rectangle.Left + Math.Min(rectangle.Width - 2, 26), rectangle.Top + 1);
					using Pen pen3 = new Pen(Color.FromArgb(32, BorderColor), 1f);
					args.Graphics.DrawLine(pen3, rectangle.Right - 20, rectangle.Top + 1, rectangle.Right - 1, rectangle.Top + 1);
					using Pen pen4 = new Pen(Color.FromArgb(28, BorderColor), 1f);
					args.Graphics.DrawLine(pen4, rectangle.Left + 1, rectangle.Bottom - 1, rectangle.Left + 18, rectangle.Bottom - 1);
				}
			}
			catch (Exception ex)
			{
				LogPanelException("card", ex);
				throw;
			}
		};
		return panel;
	}

	private static void TracePanelDiagnostics(Control control, string panelName)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		LogPanelEvent("create", control, panelName);
		control.SizeChanged += delegate
		{
			LogPanelEvent("size-changed", control, panelName);
		};
		control.Layout += delegate
		{
			LogPanelEvent("layout", control, panelName);
		};
		control.Paint += delegate
		{
			LogPanelEvent("paint", control, panelName);
		};
	}

	private static void LogBootstrap(string phase)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		File.AppendAllText(ResolveDiagLogPath(), $"{DateTime.UtcNow:O} form {phase}\r\n");
	}

	private static void LogPanelEvent(string phase, Control control, string panelName)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		File.AppendAllText(ResolveDiagLogPath(), $"{DateTime.UtcNow:O} {panelName} {phase} size={control.Width}x{control.Height} bounds={control.Bounds} handle={control.IsHandleCreated} visible={control.Visible}\r\n");
	}

	private static void LogPanelException(string panelName, Exception ex)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		File.AppendAllText(ResolveDiagLogPath(), $"{DateTime.UtcNow:O} {panelName} exception {ex.GetType().FullName}: {ex.Message}\r\n{ex.StackTrace}\r\n");
	}

	private static void ApplyProbeTheme(Control control, Color color)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		control.BackColor = color;
	}

	private void LogRootLayoutSnapshot(string phase)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		Control? outer = base.Controls.Count > 0 ? base.Controls[0] : null;
		Control? header = outer?.Controls.Count > 0 ? outer.Controls[0] : null;
		Control? middle = outer?.Controls.Count > 1 ? outer.Controls[1] : null;
		Control? file = outer?.Controls.Count > 2 ? outer.Controls[2] : null;
		Control? footer = outer?.Controls.Count > 3 ? outer.Controls[3] : null;
		Control? left = middle?.Controls.Count > 0 ? middle.Controls[0] : null;
		Control? center = middle?.Controls.Count > 1 ? middle.Controls[1] : null;
		Control? right = middle?.Controls.Count > 2 ? middle.Controls[2] : null;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append($"{DateTime.UtcNow:O} form {phase} ");
		stringBuilder.Append($"size={base.Size} client={base.ClientSize} root={base.Controls.Count} ");
		stringBuilder.Append($"outer={(outer == null ? "<null>" : outer.Bounds.ToString())} ");
		stringBuilder.Append($"header={(header == null ? "<null>" : header.Bounds.ToString())} ");
		stringBuilder.Append($"left={(left == null ? "<null>" : left.Bounds.ToString())} ");
		stringBuilder.Append($"center={(center == null ? "<null>" : center.Bounds.ToString())} ");
		stringBuilder.Append($"right={(right == null ? "<null>" : right.Bounds.ToString())} ");
		stringBuilder.Append($"file={(file == null ? "<null>" : file.Bounds.ToString())} ");
		stringBuilder.Append($"footer={(footer == null ? "<null>" : footer.Bounds.ToString())}\r\n");
		File.AppendAllText(ResolveDiagLogPath(), stringBuilder.ToString());
		if (outer != null)
		{
			LogControlOrder("outer", outer);
		}
		if (middle != null)
		{
			LogControlOrder("middle", middle);
		}
	}

	private void LogControlOrder(string name, Control container)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append($"{DateTime.UtcNow:O} order {name} count={container.Controls.Count} ");
		for (int i = 0; i < container.Controls.Count; i++)
		{
			Control control = container.Controls[i];
			int childIndex = container.Controls.GetChildIndex(control);
			stringBuilder.Append($"[{i}:z{childIndex}:{control.GetType().Name}:{control.Bounds}]");
			if (control.Width == container.ClientSize.Width && control.Height == container.ClientSize.Height)
			{
				stringBuilder.Append("<full-client>");
			}
			stringBuilder.Append(' ');
		}
		stringBuilder.Append("\r\n");
		File.AppendAllText(ResolveDiagLogPath(), stringBuilder.ToString());
	}

	private void CaptureVisualSnapshot(string phase)
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
		{
			return;
		}
		string directoryName = Path.GetDirectoryName(ResolveDiagLogPath()) ?? AppContext.BaseDirectory;
		Control? outer = base.Controls.Count > 0 ? base.Controls[0] : null;
		TryCaptureControlSnapshot(this, Path.Combine(directoryName, $"drawtobitmap-form-{phase}.png"));
		if (outer != null)
		{
			TryCaptureControlSnapshot(outer, Path.Combine(directoryName, $"drawtobitmap-outer-{phase}.png"));
		}
	}

	private static void TryCaptureControlSnapshot(Control control, string path)
	{
		if (control.Width <= 0 || control.Height <= 0)
		{
			File.AppendAllText(ResolveDiagLogPath(), $"{DateTime.UtcNow:O} snapshot-skip {control.GetType().Name} size={control.Width}x{control.Height} path={path}\r\n");
			return;
		}
		using Bitmap bitmap = new Bitmap(control.Width, control.Height);
		control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));
		bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
		File.AppendAllText(ResolveDiagLogPath(), $"{DateTime.UtcNow:O} snapshot-save {control.GetType().Name} size={control.Width}x{control.Height} path={path}\r\n");
	}

	private static string ResolveDiagLogPath()
	{
		string? path = Environment.GetEnvironmentVariable(PanelDiagnosticsPathEnvVar);
		if (string.IsNullOrWhiteSpace(path))
		{
			path = Path.Combine(AppContext.BaseDirectory, "temp", "xecli-terminal-bootstrap-diag.log");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		return path;
	}

	private static Panel CreateScaffoldSurfacePanel(Color background, Padding padding, int horizontalStep, int verticalStep)
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		panel.BackColor = background;
		panel.Padding = padding;
		panel.Paint += delegate(object? sender, PaintEventArgs args)
		{
			if (sender is not Control control)
			{
				return;
			}
			args.Graphics.SmoothingMode = SmoothingMode.None;
			Rectangle rectangle = new Rectangle(0, 0, Math.Max(0, control.Width - 1), Math.Max(0, control.Height - 1));
			using Pen pen = new Pen(Color.FromArgb(138, BorderColor), 1f);
			args.Graphics.DrawRectangle(pen, rectangle);
			using Pen pen2 = new Pen(Color.FromArgb(18, AccentGreen), 1f);
			for (int i = 10; i < rectangle.Width; i += Math.Max(12, horizontalStep))
			{
				args.Graphics.DrawLine(pen2, i, 8, i, rectangle.Height - 8);
			}
			for (int j = 10; j < rectangle.Height; j += Math.Max(12, verticalStep))
			{
				args.Graphics.DrawLine(pen2, 8, j, rectangle.Width - 8, j);
			}
			using Pen pen3 = new Pen(Color.FromArgb(64, AccentGreen), 1f);
			args.Graphics.DrawLine(pen3, 1, 1, Math.Min(rectangle.Width - 1, 24), 1);
			args.Graphics.DrawLine(pen3, 1, 1, 1, Math.Min(rectangle.Height - 1, 18));
		};
		return panel;
	}

	private void SetPersistentConnectState(string text, Color foreground, Color background)
	{
		mainShellBannerLabel.Text = text;
		mainShellBannerLabel.ForeColor = foreground;
		mainShellBannerLabel.BackColor = background;
	}

	private void InitializeInteractiveActions()
	{
		connectButton.Click += async delegate
		{
			CancelAndDispose(ref remoteBrowseCts);
			shellDisconnected = false;
			connectAttemptInFlight = true;
			connectAttemptStartedUtc = DateTime.UtcNow;
			remoteEntries.Clear();
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			footerStatusLabel.Text = "CONNECTING...";
			footerStatusLabel.ForeColor = AccentGreen;
			SetPersistentConnectState("LINK NEGOTIATING", AccentGreen, Color.FromArgb(18, 27, 18));
			UpdateShellScaffold();
			terminalOutput.SelectionStart = terminalOutput.TextLength;
			terminalOutput.SelectionLength = 0;
			terminalOutput.SelectedText = Environment.NewLine + "[connecting] Establishing console link..." + Environment.NewLine;
			commandInput.PlaceholderText = "Connecting to console...";
			connectButton.Text = "CONNECTING";
			connectButton.ForeColor = AccentGreen;
			connectButton.Enabled = false;
			await Task.Yield();
			bool flag = false;
			try
			{
				flag = await TryProbeConnectionAsync();
			}
			catch (Exception ex)
			{
				flag = false;
				AppendSystemLine("Connect failed: " + ex.Message, AccentPink);
			}
			int num = (int)(DateTime.UtcNow - connectAttemptStartedUtc).TotalMilliseconds;
			if (num < ConnectBannerMinimumHoldMs)
			{
				try
				{
					await Task.Delay(ConnectBannerMinimumHoldMs - num, formLifetimeCts.Token);
				}
				catch
				{
				}
			}
			if (!base.IsDisposed)
			{
				if (!connectAttemptInFlight)
				{
					connectButton.Text = "CONNECT";
					connectButton.ForeColor = AccentGreen;
					connectButton.Enabled = true;
					commandInput.PlaceholderText = "Enter XeCLI command for the current console session...";
					return;
				}
				if (flag)
				{
					SetPersistentConnectState("LINK ACTIVE", AccentGreen, Color.FromArgb(18, 34, 32));
					footerStatusLabel.Text = "CONNECTED";
					footerStatusLabel.ForeColor = AccentGreen;
					UpdateConnectionStatusIndicator(new TelemetrySnapshot
					{
						Connected = true
					});
					if (options.TelemetryEnabled && !telemetryTimer.Enabled)
					{
						telemetryTimer.Start();
					}
					UpdateShellScaffold(new TelemetrySnapshot
					{
						Connected = true
					});
					_ = HydrateConnectedSessionAsync();
				}
				else
				{
					SetPersistentConnectState("LINK FAILED", WarningColor, Color.FromArgb(27, 21, 16));
					footerStatusLabel.Text = "CONNECT FAILED";
					footerStatusLabel.ForeColor = WarningColor;
					UpdateShellScaffold();
				}
				connectButton.Text = "CONNECT";
				connectButton.ForeColor = AccentGreen;
				connectButton.Enabled = true;
				commandInput.PlaceholderText = "Connect or enter a XeCLI command for this session...";
				connectAttemptInFlight = false;
			}
		};
		disconnectButton.Click += delegate
		{
			CancelAndDispose(ref remoteBrowseCts);
			CancelActiveCommand();
			shellDisconnected = true;
			connectAttemptInFlight = false;
			remoteEntries.Clear();
			remoteCurrentPath = "/";
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			footerStatusLabel.Text = "DISCONNECTED";
			footerStatusLabel.ForeColor = WarningColor;
			SetPersistentConnectState("LINK OFFLINE", WarningColor, Color.FromArgb(24, 19, 15));
			UpdateConnectionStatusIndicator(new TelemetrySnapshot
			{
				Connected = false
			});
			UpdateShellScaffold();
			telemetryTimer.Stop();
			connectButton.Text = "CONNECT";
			connectButton.ForeColor = AccentCyan;
			connectButton.Enabled = true;
			commandInput.PlaceholderText = "Connect or enter a XeCLI command for this session...";
			SetLabelText(rightNetworkLabel, BuildDisconnectedStatusText());
			SetLabelText(rightTempLabel, "IDLE · waiting for FTP traffic");
			SetLabelText(rightDetailLabel, BuildDisconnectedSessionText());
			remotePathLabel.Text = "FTP /";
			remoteFileList.SetItems(new string[1] { "Disconnected" });
			if (transferQueueEntries.Count == 0)
			{
				transferQueueList.SetItems(new string[2] { "No transfer activity yet", "Queued dumps and FTP jobs appear here" });
			}
		};
		screenshotButton.Click += async delegate
		{
			await ExecuteCommandAsync("screenshot");
		};
		pluginsTabButton.Click += delegate
		{
			inventoryShowsModules = false;
			RefreshInventoryHeader();
			RefreshInventoryListFromSnapshot();
		};
		modulesTabButton.Click += delegate
		{
			inventoryShowsModules = true;
			RefreshInventoryHeader();
			RefreshInventoryListFromSnapshot();
		};
		RefreshInventoryHeader();
	}

	private void ConfigureActionButton(Button button, string text)
	{
		button.Dock = DockStyle.Fill;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderColor = BorderColor;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 46, 28);
		button.BackColor = TerminalBackground;
		button.ForeColor = Color.WhiteSmoke;
		button.Font = new Font("Consolas", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
		button.MinimumSize = new Size(0, 30);
		button.AutoEllipsis = false;
		button.TextAlign = ContentAlignment.MiddleCenter;
		button.Text = text;
		button.Cursor = Cursors.Hand;
	}

	private void ConfigureToggleButton(Button button, string text)
	{
		button.Width = 82;
		button.Height = 24;
		button.Top = 0;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderColor = BorderColor;
		button.BackColor = TerminalBackground;
		button.ForeColor = AccentDim;
		button.Font = shellFontBold;
		button.Text = text;
		button.Cursor = Cursors.Hand;
	}

	private void RefreshInventoryHeader()
	{
		pluginsTabButton.ForeColor = (!inventoryShowsModules ? AccentCyan : AccentDim);
		modulesTabButton.ForeColor = (inventoryShowsModules ? AccentCyan : AccentDim);
	}

	private void RefreshInventoryListFromSnapshot()
	{
		if (latestSnapshot == null)
		{
			inventoryList.SetItems(new string[2]
			{
				inventoryShowsModules ? "Connect to enumerate live modules" : "Connect to enumerate loaded plugins",
				"Local shell is ready below"
			});
			return;
		}
		IEnumerable<string> enumerable = inventoryShowsModules ? latestSnapshot.Modules : latestSnapshot.Plugins;
		if (!enumerable.Any())
		{
			inventoryList.SetItems(new string[2]
			{
				inventoryShowsModules ? (latestSnapshot.Connected ? "No live modules detected" : "No cached modules available") : (latestSnapshot.Connected ? "No live plugins detected" : "No cached plugins available"),
				latestSnapshot.Connected ? "Inventory will refresh automatically" : "Reconnect to refresh live inventory"
			});
			return;
		}
		inventoryList.SetItems(enumerable.Take(256));
	}

	private void RecordTransferActivity(string command, string state)
	{
		if (!IsTransferCommand(command))
		{
			return;
		}
		if (base.IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(new Action<string, string>(RecordTransferActivity), command, state);
			return;
		}
		string item2 = $"{DateTime.Now:HH:mm:ss}  {state.ToUpperInvariant(),-9}  {AbbreviateTransferCommand(command)}";
		transferQueueEntries.RemoveAll((string item) => item.EndsWith(AbbreviateTransferCommand(command), StringComparison.Ordinal));
		transferQueueEntries.Insert(0, item2);
		if (transferQueueEntries.Count > 6)
		{
			transferQueueEntries.RemoveRange(6, transferQueueEntries.Count - 6);
		}
		transferQueueList.SetItems(transferQueueEntries);
	}

	private static bool IsTransferCommand(string command)
	{
		string text = command.Trim().ToLowerInvariant();
		return text.StartsWith("ftp get ", StringComparison.Ordinal) || text.StartsWith("ftp put ", StringComparison.Ordinal) || text.StartsWith("ftp list ", StringComparison.Ordinal) || text.StartsWith("nand dump", StringComparison.Ordinal) || text.StartsWith("xell kv-export", StringComparison.Ordinal) || text.StartsWith("save extract", StringComparison.Ordinal) || text.StartsWith("save inject", StringComparison.Ordinal) || text.StartsWith("xbdm xex dump", StringComparison.Ordinal) || text.Contains(" dump ", StringComparison.Ordinal) || text.EndsWith(" dump", StringComparison.Ordinal);
	}

	private static string AbbreviateTransferCommand(string command)
	{
		string text = command.Trim();
		if (text.Length <= 34)
		{
			return text;
		}
		return text.Substring(0, 31) + "...";
	}

	private void LoadXeCliLogo()
	{
		string[] array = new string[4]
		{
			Path.Combine(AppContext.BaseDirectory, "Assets", "header.png"),
			Path.Combine(AppContext.BaseDirectory, "Assets", "readme", "xecli-logo.jpg"),
			Path.Combine(AppContext.BaseDirectory, "Assets", "readme", "logo.png"),
			Path.Combine(AppContext.BaseDirectory, "Assets", "readme", "logo.jpg")
		};
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!File.Exists(text))
			{
				continue;
			}
			try
			{
				logoPictureBox.Image?.Dispose();
				logoPictureBox.Image = CreateMonochromeHeaderImage(text);
				return;
			}
			catch
			{
			}
		}
		logoPictureBox.Image?.Dispose();
		logoPictureBox.Image = CreateGeneratedHeaderFallbackImage();
	}

	private void LoadOptionalBackground()
	{
		base.BackgroundImage = null;
		base.BackgroundImageLayout = ImageLayout.None;
	}

	private static Image LoadImageCopy(string path)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using Image image = Image.FromStream(fileStream);
		return new Bitmap(image);
	}

	private static Color InterpolateColor(Color first, Color second, float amount)
	{
		float num = Math.Clamp(amount, 0f, 1f);
		return Color.FromArgb((int)Math.Round((float)first.R + ((float)second.R - (float)first.R) * num), (int)Math.Round((float)first.G + ((float)second.G - (float)first.G) * num), (int)Math.Round((float)first.B + ((float)second.B - (float)first.B) * num));
	}

	private static Image CreateMonochromeHeaderImage(string path)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using Image image = Image.FromStream(fileStream);
		Bitmap bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.Clear(Color.Transparent);
		graphics.DrawImage(image, 0, 0, image.Width, image.Height);
		for (int y = 0; y < bitmap.Height; y++)
		{
			for (int x = 0; x < bitmap.Width; x++)
			{
				Color pixel = bitmap.GetPixel(x, y);
				if (pixel.A < 8)
				{
					bitmap.SetPixel(x, y, Color.Transparent);
					continue;
				}
				int num = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
				int num2 = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
				float num3 = (float)((0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B) / 255.0);
				if (num <= 8 || (num <= 16 && num3 <= 0.03f && num - num2 <= 4))
				{
					bitmap.SetPixel(x, y, Color.Transparent);
					continue;
				}
				float amount = Math.Clamp((Math.Max(num3, num / 255f * 0.82f) - 0.03f) / 0.94f, 0f, 1f);
				bool flag = pixel.G >= pixel.R + 10 && pixel.G >= pixel.B + 8;
				Color color = flag
					? InterpolateColor(Color.FromArgb(205, 255, 186), AccentGreen, 0.42f + amount * 0.34f)
					: InterpolateColor(Color.FromArgb(210, 222, 210), Color.WhiteSmoke, 0.36f + amount * 0.64f);
				int alpha = (int)Math.Round(pixel.A * (0.78f + amount * 0.22f));
				bitmap.SetPixel(x, y, Color.FromArgb(alpha, color));
			}
		}
		Rectangle rectangle = FindOpaqueBounds(bitmap, 4);
		if (rectangle.Width <= 0 || rectangle.Height <= 0)
		{
			return bitmap;
		}
		rectangle = ExpandWithinBounds(rectangle, 4, bitmap.Width, bitmap.Height);
		Bitmap bitmap2 = new Bitmap(rectangle.Width + 12, rectangle.Height + 8, PixelFormat.Format32bppArgb);
		using (Graphics graphics2 = Graphics.FromImage(bitmap2))
		{
			graphics2.Clear(Color.Transparent);
			graphics2.DrawImage(bitmap, new Rectangle(6, 4, rectangle.Width, rectangle.Height), rectangle, GraphicsUnit.Pixel);
		}
		bitmap.Dispose();
		return bitmap2;
	}

	private static Image CreateGeneratedHeaderFallbackImage()
	{
		Bitmap bitmap = new Bitmap(512, 128, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.Clear(Color.Transparent);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
		using Font font = new Font("Consolas", 34f, FontStyle.Bold, GraphicsUnit.Point);
		using Brush brush = new SolidBrush(Color.WhiteSmoke);
		using Brush brush2 = new SolidBrush(Color.FromArgb(220, AccentGreen));
		using Pen pen = new Pen(Color.FromArgb(220, AccentGreen), 4f);
		graphics.DrawString("XeCLI", font, brush, 16f, 20f);
		graphics.DrawLine(pen, 18f, 98f, 210f, 98f);
		graphics.FillRectangle(brush2, 18f, 108f, 48f, 4f);
		return bitmap;
	}

	private static Rectangle FindOpaqueBounds(Bitmap bitmap, byte alphaThreshold)
	{
		int num = bitmap.Width;
		int num2 = bitmap.Height;
		int num3 = -1;
		int num4 = -1;
		for (int i = 0; i < bitmap.Height; i++)
		{
			for (int j = 0; j < bitmap.Width; j++)
			{
				if (bitmap.GetPixel(j, i).A <= alphaThreshold)
				{
					continue;
				}
				num = Math.Min(num, j);
				num2 = Math.Min(num2, i);
				num3 = Math.Max(num3, j);
				num4 = Math.Max(num4, i);
			}
		}
		if (num3 < 0 || num4 < 0)
		{
			return Rectangle.Empty;
		}
		return Rectangle.FromLTRB(num, num2, num3 + 1, num4 + 1);
	}

	private static Rectangle ExpandWithinBounds(Rectangle rectangle, int padding, int width, int height)
	{
		int num = Math.Max(0, rectangle.Left - padding);
		int num2 = Math.Max(0, rectangle.Top - padding);
		int num3 = Math.Min(width, rectangle.Right + padding);
		int num4 = Math.Min(height, rectangle.Bottom + padding);
		return Rectangle.FromLTRB(num, num2, num3, num4);
	}

	private void HandleCommandInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Tab)
		{
			ApplySelectedSuggestion();
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Up && suggestionList.Visible && suggestionList.Items.Count > 0)
		{
			int num = Math.Max(suggestionList.SelectedIndex - 1, 0);
			suggestionList.SelectedIndex = num;
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Down && suggestionList.Visible && suggestionList.Items.Count > 0)
		{
			int num2 = Math.Min(suggestionList.SelectedIndex + 1, suggestionList.Items.Count - 1);
			suggestionList.SelectedIndex = num2;
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode != Keys.Enter)
		{
			return;
		}
		e.Handled = true;
		e.SuppressKeyPress = true;
		string text = commandInput.Text.Trim();
		if (text.Length == 0)
		{
			return;
		}
		commandInput.Clear();
		ToggleSuggestions(visible: false);
		_ = ExecuteCommandAsync(text);
	}

	private void RefreshSuggestions()
	{
		string text = commandInput.Text.TrimStart();
		if (text.Length == 0)
		{
			ToggleSuggestions(visible: false);
			return;
		}
		List<string> list = XeCliSuggestions.Where((string s) => s.StartsWith(text, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
		if (list.Count == 0 && text.Length >= 3)
		{
			list = XeCliSuggestions.Where((string s) => s.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
		}
		suggestionList.Items.Clear();
		foreach (string item in list)
		{
			suggestionList.Items.Add(item);
		}
		bool visible = list.Count > 0;
		if (visible)
		{
			suggestionList.SelectedIndex = 0;
		}
		ToggleSuggestions(visible);
	}

	private void ToggleSuggestions(bool visible)
	{
		suggestionList.Visible = visible;
		suggestionHost.Height = (visible ? 112 : 4);
	}

	private void ApplySelectedSuggestion()
	{
		if (!suggestionList.Visible || suggestionList.SelectedItem == null)
		{
			RefreshSuggestions();
		}
		if (suggestionList.SelectedItem is string text)
		{
			commandInput.Text = text;
			commandInput.SelectionStart = commandInput.Text.Length;
			commandInput.Focus();
			ToggleSuggestions(visible: false);
		}
	}

	private async Task ExecuteCommandAsync(string rawCommand)
	{
		if (commandInFlight)
		{
			AppendSystemLine("A command is already running. Wait for completion.", Color.Gold);
			return;
		}
		string command = rawCommand.Trim();
		if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) || command.Equals("quit", StringComparison.OrdinalIgnoreCase))
		{
			Close();
			return;
		}
		if (command.Equals("clear", StringComparison.OrdinalIgnoreCase))
		{
			lock (pendingTerminalLock)
			{
				pendingTerminalLines.Clear();
				terminalFlushScheduled = false;
			}
			terminalFlushTimer.Stop();
			terminalOutput.Clear();
			return;
		}
		commandInFlight = true;
		footerStatusLabel.Text = "EXECUTING  " + command;
		footerStatusLabel.ForeColor = AccentCyan;
		RecordTransferActivity(command, "queued");
		AppendCommandLine(command);
		try
		{
			await RunCliProcessAsync(command);
		}
		catch (Exception ex)
		{
			RecordTransferActivity(command, "error");
			AppendSystemLine("Execution error: " + ex.Message, Color.IndianRed);
		}
		finally
		{
			commandInFlight = false;
			UpdateLiveHints();
		}
	}

	private async Task RunCliProcessAsync(string command)
	{
		string arguments = command;
		if (arguments.StartsWith("rgh ", StringComparison.OrdinalIgnoreCase))
		{
			arguments = arguments.Substring(4).TrimStart();
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = options.ExePath,
			Arguments = arguments,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetDirectoryName(options.ExePath) ?? Environment.CurrentDirectory,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		using Process process = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = false
		};
		CancelAndDispose(ref commandCts);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		commandCts = cancellationTokenSource;
		activeCommandProcess = process;
		using CancellationTokenRegistration cancellationTokenRegistration = cancellationTokenSource.Token.Register(delegate
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
		});
		try
		{
			process.Start();
			RecordTransferActivity(command, "running");
			Task task = StreamProcessLinesAsync(process.StandardOutput, isError: false, cancellationTokenSource.Token);
			Task task2 = StreamProcessLinesAsync(process.StandardError, isError: true, cancellationTokenSource.Token);
			await Task.WhenAll(task, task2, process.WaitForExitAsync(cancellationTokenSource.Token)).ConfigureAwait(false);
			if (process.ExitCode != 0)
			{
				RecordTransferActivity(command, "failed");
				AppendSystemLine("Exit code: " + process.ExitCode, Color.OrangeRed);
			}
			else
			{
				RecordTransferActivity(command, "complete");
				AppendSystemLine("Command completed.", AccentDim);
			}
		}
		catch (OperationCanceledException)
		{
			RecordTransferActivity(command, "cancelled");
			AppendSystemLine("Command cancelled.", AccentPink);
		}
		finally
		{
			activeCommandProcess = null;
			CancelAndDispose(ref commandCts);
		}
	}

	private async Task StreamProcessLinesAsync(StreamReader reader, bool isError, CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string text = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
			if (text == null)
			{
				break;
			}
			AppendTerminalLine(text, isError ? Color.IndianRed : Color.WhiteSmoke);
		}
	}

	private void AppendCommandLine(string command)
	{
		AppendTerminalLine(string.Empty, Color.White);
		AppendTerminalLine("> " + command, AccentGreen);
	}

	private void AppendSystemLine(string text, Color color)
	{
		AppendTerminalLine(text, color);
	}

	private void AppendTerminalLine(string text, Color color)
	{
		if (base.IsDisposed)
		{
			return;
		}
		bool flag = false;
		lock (pendingTerminalLock)
		{
			pendingTerminalLines.Enqueue((text, color));
			if (!terminalFlushScheduled)
			{
				terminalFlushScheduled = true;
				flag = true;
			}
		}
		if (!flag)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(new Action(StartTerminalFlush));
		}
		else
		{
			StartTerminalFlush();
		}
	}

	private void StartTerminalFlush()
	{
		if (!terminalFlushTimer.Enabled && !base.IsDisposed)
		{
			terminalFlushTimer.Start();
		}
	}

	private void FlushPendingTerminalOutput()
	{
		if (base.IsDisposed)
		{
			terminalFlushTimer.Stop();
			return;
		}
		List<(string Text, Color Color)> list = new List<(string, Color)>();
		lock (pendingTerminalLock)
		{
			while (pendingTerminalLines.Count > 0 && list.Count < TerminalFlushBatchSize)
			{
				list.Add(pendingTerminalLines.Dequeue());
			}
			if (pendingTerminalLines.Count == 0)
			{
				terminalFlushScheduled = false;
				terminalFlushTimer.Stop();
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		terminalOutput.SuspendLayout();
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			Color color = list[0].Color;
			foreach ((string Text, Color Color) item in list)
			{
				if (item.Color != color && stringBuilder.Length > 0)
				{
					terminalOutput.SelectionStart = terminalOutput.TextLength;
					terminalOutput.SelectionLength = 0;
					terminalOutput.SelectionColor = color;
					terminalOutput.AppendText(stringBuilder.ToString());
					stringBuilder.Clear();
					color = item.Color;
				}
				stringBuilder.Append(item.Text);
				stringBuilder.AppendLine();
			}
			if (stringBuilder.Length > 0)
			{
				terminalOutput.SelectionStart = terminalOutput.TextLength;
				terminalOutput.SelectionLength = 0;
				terminalOutput.SelectionColor = color;
				terminalOutput.AppendText(stringBuilder.ToString());
			}
		}
		finally
		{
			terminalOutput.ResumeLayout(performLayout: false);
		}
		terminalOutput.SelectionColor = terminalOutput.ForeColor;
		TrimTerminalOutput();
		terminalOutput.ScrollToCaret();
	}

	private void TrimTerminalOutput()
	{
		int num = terminalOutput.TextLength - MaxTerminalCharacters;
		if (num <= 0)
		{
			return;
		}
		int num2 = terminalOutput.Find("\n", num, RichTextBoxFinds.None);
		int num3 = (num2 >= 0) ? (num2 + 1) : num;
		terminalOutput.Select(0, Math.Min(num3, terminalOutput.TextLength));
		terminalOutput.SelectedText = string.Empty;
	}

	private async Task PollTelemetrySafeAsync(bool forceHeavyRefresh = false)
	{
		if (!options.TelemetryEnabled || telemetryPollInFlight || shellDisconnected || base.IsDisposed || commandInFlight || connectAttemptInFlight)
		{
			return;
		}
		telemetryPollInFlight = true;
		try
		{
			TelemetrySnapshot telemetrySnapshot = await Task.Run(() => TryReadTelemetryAsync(forceHeavyRefresh), formLifetimeCts.Token);
			ApplyTelemetry(telemetrySnapshot);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			telemetryPollInFlight = false;
		}
	}

	private async Task<bool> TryProbeConnectionAsync()
	{
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		cancellationTokenSource.CancelAfter(Math.Max(800, Math.Min(options.TimeoutMs, 2000)));
		XbdmConnectionOptions xbdmConnectionOptions = new XbdmConnectionOptions
		{
			Host = options.Ip,
			Port = options.Port,
			TimeoutMs = Math.Max(800, Math.Min(options.TimeoutMs, 2000))
		};
		using XbdmClient xbdmClient = await XbdmClient.ConnectAsync(xbdmConnectionOptions, cancellationTokenSource.Token);
		return xbdmClient != null;
	}

	private async Task HydrateConnectedSessionAsync()
	{
		if (base.IsDisposed || shellDisconnected)
		{
			return;
		}
		if (!options.TelemetryEnabled)
		{
			_ = RefreshRemoteBrowserAsync(force: true);
			return;
		}
		try
		{
			await PollTelemetrySafeAsync(forceHeavyRefresh: true);
		}
		catch (Exception ex)
		{
			AppendSystemLine("Telemetry refresh delayed: " + ex.Message, AccentDim);
		}
		if (!base.IsDisposed && !shellDisconnected && latestSnapshot != null && latestSnapshot.Connected)
		{
			_ = RefreshRemoteBrowserAsync(force: true);
		}
	}

	private async Task<TelemetrySnapshot> TryReadTelemetryAsync(bool forceHeavyRefresh)
	{
		CliConfig cliConfig = CliConfig.Load();
		bool flag = forceHeavyRefresh;
		TelemetrySnapshot telemetrySnapshot = new TelemetrySnapshot
		{
			Connected = false,
			FtpPort = cliConfig.DefaultFtpPort ?? 21,
			FtpUser = TrimOrNull(cliConfig.DefaultFtpUser) ?? "xbox"
		};
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		cancellationTokenSource.CancelAfter(Math.Clamp(options.TimeoutMs, 1200, 6000));
		try
		{
			XbdmConnectionOptions xbdmConnectionOptions = new XbdmConnectionOptions
			{
				Host = options.Ip,
				Port = options.Port,
				TimeoutMs = options.TimeoutMs
			};
			using XbdmClient client = await XbdmClient.ConnectAsync(xbdmConnectionOptions, cancellationTokenSource.Token);
			telemetrySnapshot.Connected = true;
			XbdmConsoleInfo consoleInfo = await client.GetConsoleInfoAsync(cancellationTokenSource.Token);
			telemetrySnapshot.DebugName = TrimOrNull(consoleInfo.DebugName);
			telemetrySnapshot.ExecutionState = TrimOrNull(consoleInfo.ExecutionState);
			try
			{
				telemetrySnapshot.RunningXex = await client.GetRunningXexPathAsync(null, cancellationTokenSource.Token);
			}
			catch
			{
			}
			IReadOnlyList<XbdmUserInfo> userListAsync = await client.GetUserListAsync(cancellationTokenSource.Token);
			XbdmUserInfo xbdmUserInfo = userListAsync.FirstOrDefault((XbdmUserInfo u) => u.SignInState.HasValue && u.SignInState.Value != 0 && !string.IsNullOrWhiteSpace(u.Gamertag)) ?? userListAsync.FirstOrDefault((XbdmUserInfo u) => !string.IsNullOrWhiteSpace(u.Gamertag));
			string text = TrimOrNull(xbdmUserInfo?.Gamertag);
			uint? nullable = xbdmUserInfo?.SignInState;
			if (string.IsNullOrWhiteSpace(text) && string.Equals(Environment.GetEnvironmentVariable("XECLI_ALLOW_XAM_PROBE"), "1", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					ProfileHelpers.XamUserInfo xamUserInfo = await HardwareHelpers.TryGetSignedInUserAsync(client, cancellationTokenSource.Token);
					if (xamUserInfo != null)
					{
						text = TrimOrNull(xamUserInfo.Gamertag) ?? text;
						nullable = xamUserInfo.SignInState;
					}
				}
				catch
				{
				}
			}
			Jrpc2Client jrpc2Client = new Jrpc2Client(client);
			try
			{
				telemetrySnapshot.TitleId = await jrpc2Client.GetTitleIdAsync(cancellationTokenSource.Token);
			}
			catch
			{
			}
			telemetrySnapshot.Gamertag = text;
			telemetrySnapshot.SignInStateText = nullable.HasValue ? HardwareHelpers.DescribeSignInState(nullable.Value) : (!string.IsNullOrWhiteSpace(text) ? "Signed in" : "not detected");
			telemetrySnapshot.CpuTemp = await TryGetTempAsync(jrpc2Client, SensorType.CPU, cancellationTokenSource.Token);
			telemetrySnapshot.GpuTemp = await TryGetTempAsync(jrpc2Client, SensorType.GPU, cancellationTokenSource.Token);
			telemetrySnapshot.EdramTemp = await TryGetTempAsync(jrpc2Client, SensorType.EDRAM, cancellationTokenSource.Token);
			telemetrySnapshot.BoardTemp = await TryGetTempAsync(jrpc2Client, SensorType.MotherBoard, cancellationTokenSource.Token);
			telemetrySnapshot.DashboardVersion = await TryGetDashboardAsync(jrpc2Client, cancellationTokenSource.Token);
			telemetrySnapshot.Motherboard = await TryGetMotherboardAsync(jrpc2Client, cancellationTokenSource.Token);
			if (telemetrySnapshot.TitleId.HasValue && TitleIdDatabase.Instance.TryResolve(telemetrySnapshot.TitleId.Value, null, out TitleIdEntry titleEntry))
			{
				telemetrySnapshot.TitleName = titleEntry?.Name;
			}
			telemetrySnapshot.TitleName = ProfileHelpers.TryGetTitleFallbackName(telemetrySnapshot.TitleId, telemetrySnapshot.RunningXex, telemetrySnapshot.TitleName);
			try
			{
				if (flag)
				{
					foreach (XbdmDriveEntry item in await client.GetDrivesAsync(includeSize: false, cancellationTokenSource.Token))
					{
						string text2 = TrimOrNull(item.Name);
						if (!string.IsNullOrWhiteSpace(text2) && ShouldDisplayDriveName(text2))
						{
							telemetrySnapshot.Drives.Add(text2.ToUpperInvariant());
						}
					}
				}
				else if (latestSnapshot != null)
				{
					telemetrySnapshot.Drives.AddRange(latestSnapshot.Drives);
				}
			}
			catch
			{
			}
			try
			{
				if (flag)
				{
					foreach (XbdmModuleInfo item2 in await client.GetModulesAsync(includeSections: false, cancellationTokenSource.Token))
					{
						string text3 = TrimOrNull(item2.Name);
						if (!string.IsNullOrWhiteSpace(text3))
						{
							telemetrySnapshot.Modules.Add(text3);
						}
					}
				}
				else if (latestSnapshot != null)
				{
					telemetrySnapshot.Modules.AddRange(latestSnapshot.Modules);
				}
			}
			catch
			{
			}
			try
			{
				if (flag)
				{
					int num = telemetrySnapshot.FtpPort ?? 21;
					string text4 = telemetrySnapshot.FtpUser ?? "xbox";
					string text5 = cliConfig.DefaultFtpPassword ?? "xbox";
					PluginHelpers.PluginConfig pluginConfig = await PluginHelpers.LoadAsync(options.Ip, num, text4, text5, options.TimeoutMs, null);
					foreach (var item3 in pluginConfig.Slots.OrderBy((KeyValuePair<int, string> kv) => kv.Key))
					{
						if (!string.IsNullOrWhiteSpace(item3.Value))
						{
							telemetrySnapshot.Plugins.Add("plugin" + item3.Key + " : " + item3.Value);
						}
					}
				}
				else if (latestSnapshot != null)
				{
					telemetrySnapshot.Plugins.AddRange(latestSnapshot.Plugins);
				}
			}
			catch
			{
			}
			(double, double) tuple = SampleFtpTraffic(telemetrySnapshot.FtpPort ?? 21);
			telemetrySnapshot.FtpRxKbps = tuple.Item1;
			telemetrySnapshot.FtpTxKbps = tuple.Item2;
		}
		catch (Exception ex)
		{
			telemetrySnapshot.ErrorText = ex.Message;
		}
		return telemetrySnapshot;
	}

	private static async Task<uint?> TryGetTempAsync(Jrpc2Client jrpc, SensorType type, CancellationToken cancellationToken)
	{
		try
		{
			return await jrpc.GetTemperatureAsync(type, cancellationToken);
		}
		catch
		{
			return null;
		}
	}

	private static async Task<uint?> TryGetDashboardAsync(Jrpc2Client jrpc, CancellationToken cancellationToken)
	{
		try
		{
			return await jrpc.GetDashboardVersionAsync(cancellationToken);
		}
		catch
		{
			return null;
		}
	}

	private static async Task<string?> TryGetMotherboardAsync(Jrpc2Client jrpc, CancellationToken cancellationToken)
	{
		try
		{
			return TrimOrNull(await jrpc.GetMotherboardTypeAsync(cancellationToken));
		}
		catch
		{
			return null;
		}
	}

	private bool ShouldDisplayDriveName(string name)
	{
		string text = name.Trim().TrimEnd(':').ToLowerInvariant();
		if (text.StartsWith("usb", StringComparison.Ordinal))
		{
			return true;
		}
		switch (text)
		{
		case "hdd":
		case "hdd1":
		case "hddx":
		case "mu":
		case "memunit":
		case "intmu":
		case "mmcmu":
			return true;
		default:
			return false;
		}
	}

	private (double RxKbps, double TxKbps) SampleFtpTraffic(int ftpPort)
	{
		bool flag = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any((TcpConnectionInformation c) => c.RemoteEndPoint.Address.ToString().Equals(options.Ip, StringComparison.OrdinalIgnoreCase) && c.RemoteEndPoint.Port == ftpPort && c.State == TcpState.Established);
		if (!flag)
		{
			lastNetSampleUtc = DateTime.UtcNow;
			lastNetReceivedBytes = 0L;
			lastNetSentBytes = 0L;
			return (0.0, 0.0);
		}
		long num = 0L;
		long num2 = 0L;
		foreach (NetworkInterface allNetworkInterface in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (allNetworkInterface.OperationalStatus != OperationalStatus.Up)
			{
				continue;
			}
			try
			{
				IPInterfaceStatistics iPStatistics = allNetworkInterface.GetIPStatistics();
				num += iPStatistics.BytesReceived;
				num2 += iPStatistics.BytesSent;
			}
			catch
			{
			}
		}
		DateTime utcNow = DateTime.UtcNow;
		if (lastNetSampleUtc == default(DateTime) || lastNetReceivedBytes == 0L || lastNetSentBytes == 0L)
		{
			lastNetSampleUtc = utcNow;
			lastNetReceivedBytes = num;
			lastNetSentBytes = num2;
			return (0.0, 0.0);
		}
		double num3 = Math.Max((utcNow - lastNetSampleUtc).TotalSeconds, 0.25);
		double rxKbps = Math.Max(0.0, ((double)(num - lastNetReceivedBytes) * 8.0 / 1000.0) / num3);
		double txKbps = Math.Max(0.0, ((double)(num2 - lastNetSentBytes) * 8.0 / 1000.0) / num3);
		lastNetSampleUtc = utcNow;
		lastNetReceivedBytes = num;
		lastNetSentBytes = num2;
		return (rxKbps, txKbps);
	}

	private void ApplyTelemetry(TelemetrySnapshot snapshot)
	{
		if (base.IsDisposed)
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(new Action<TelemetrySnapshot>(ApplyTelemetry), snapshot);
			return;
		}
		string text = snapshot.Connected ? "ONLINE" : "OFFLINE";
		leftStatusLabel.Text = "THERMALS\n" + FormatThermalRow("CPU", snapshot.CpuTemp) + "\n" + FormatThermalRow("GPU", snapshot.GpuTemp) + "\n" + FormatThermalRow("EDR", snapshot.EdramTemp) + "\n" + FormatThermalRow("BRD", snapshot.BoardTemp);
		leftStatusLabel.ForeColor = Color.WhiteSmoke;
		string text2 = TrimOrNull(snapshot.DebugName) ?? "unknown";
		string text3 = TrimOrNull(snapshot.Motherboard) ?? "unknown";
		string text4 = (snapshot.DashboardVersion.HasValue ? snapshot.DashboardVersion.Value.ToString() : "unknown");
		string text7 = TrimOrNull(snapshot.ExecutionState) ?? "unknown";
		string text8 = TrimOrNull(snapshot.TitleName) ?? "unknown";
		string text9 = TrimOrNull(snapshot.RunningXex) ?? "unknown";
		leftConsoleLabel.Text = "UNIT PROFILE\nDEBUG   " + FitStatusText(text2, 14) + "\nBOARD   " + FitStatusText(text3, 14) + "\nDASH    " + FitStatusText(text4, 14) + "\nSTATE   " + FitStatusText(text7, 14) + "\nLINK    " + text + "\nTITLE   " + FitStatusText(text8, 14);
		leftSignInLabel.Text = "DETECTED DRIVES  |  " + snapshot.Drives.Count;
		string text5 = TrimOrNull(snapshot.Gamertag) ?? "not detected";
		string text6 = TrimOrNull(snapshot.SignInStateText) ?? "unknown";
		string text10 = snapshot.FtpPort.HasValue ? snapshot.FtpPort.Value.ToString() : "--";
		string text11 = TrimOrNull(snapshot.FtpUser) ?? "xbox";
		string text12 = TrimOrNull(snapshot.ErrorText);
		string value = "STATUS\nLINK     " + text + "\nTARGET   " + options.Ip + ":" + options.Port + "\nFTP      " + text11 + "@" + text10 + "\nEXEC     " + FitStatusText(text7, 12) + "\nINV      " + snapshot.Drives.Count + "D / " + snapshot.Plugins.Count + "P / " + snapshot.Modules.Count + "M";
		if (!string.IsNullOrWhiteSpace(text12))
		{
			value = "STATUS\nLINK     " + text + "\nTARGET   " + options.Ip + ":" + options.Port + "\nFTP      " + text11 + "@" + text10 + "\nALERT    " + FitStatusText(text12, 12) + "\nINV      " + snapshot.Drives.Count + "D / " + snapshot.Plugins.Count + "P / " + snapshot.Modules.Count + "M";
		}
		if (!snapshot.Connected)
		{
			SetLabelText(rightNetworkLabel, BuildDisconnectedStatusText());
			SetLabelText(rightTempLabel, "IDLE · waiting for FTP traffic");
			SetLabelText(rightDetailLabel, BuildDisconnectedSessionText());
		}
		else
		{
			SetLabelText(rightNetworkLabel, value);
			SetLabelText(rightTempLabel, "RX " + snapshot.FtpRxKbps.ToString("0.0") + " kbps  |  TX " + snapshot.FtpTxKbps.ToString("0.0") + " kbps");
			SetLabelText(rightDetailLabel, "SESSION\nTITLE    " + FitStatusText(text8, 18) + "\nXEX      " + FitFileLeaf(text9, 18) + "\nUSER     " + FitStatusText(text5, 18) + "\nSIGN     " + FitStatusText(text6, 18) + "\nTID      " + FormatTitleId(snapshot.TitleId));
		}
		ftpTrafficGraph.AddSample(snapshot.FtpRxKbps, snapshot.FtpTxKbps);
		latestSnapshot = snapshot;
		RuntimePresenceState.Update(new RuntimePresenceSnapshot
		{
			Connected = snapshot.Connected,
			DebugName = snapshot.DebugName,
			ExecutionState = snapshot.ExecutionState,
			Gamertag = snapshot.Gamertag,
			SignInStateText = snapshot.SignInStateText,
			TitleId = snapshot.TitleId,
			TitleName = snapshot.TitleName,
			RunningXex = snapshot.RunningXex,
			Ip = options.Ip,
			Port = options.Port
		});
		if (snapshot.Drives.Count == 0)
		{
			drivesList.SetItems(new string[1] { "No supported drives detected" });
		}
		else
		{
			drivesList.SetItems(snapshot.Drives.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string d) => d));
		}
		RefreshInventoryListFromSnapshot();
		if (!snapshot.Connected)
		{
			footerStatusLabel.Text = "DISCONNECTED";
			footerStatusLabel.ForeColor = WarningColor;
		}
		else
		{
			footerStatusLabel.Text = "READY";
			footerStatusLabel.ForeColor = AccentGreen;
		}
		if (!connectAttemptInFlight)
		{
			SetPersistentConnectState(snapshot.Connected ? "LINK ACTIVE" : "LINK OFFLINE", snapshot.Connected ? AccentGreen : WarningColor, snapshot.Connected ? Color.FromArgb(18, 34, 32) : Color.FromArgb(24, 19, 15));
		}
		if (!string.IsNullOrWhiteSpace(snapshot.ErrorText))
		{
			footerStatusLabel.Text = footerStatusLabel.Text + " · " + snapshot.ErrorText;
		}
		UpdateConnectionStatusIndicator(snapshot);
		UpdateShellScaffold(snapshot);
		UpdateLiveHints(snapshot);
	}

	private void UpdateLiveHints(TelemetrySnapshot? snapshot = null)
	{
		if (commandInFlight)
		{
			footerHintLabel.Text = "RUNNING COMMAND...";
			footerHintLabel.ForeColor = Color.WhiteSmoke;
			return;
		}
		if (snapshot != null && snapshot.Connected)
		{
			string text = TrimOrNull(snapshot.Gamertag);
			if (!string.IsNullOrWhiteSpace(text))
			{
				footerHintLabel.Text = "SIGNED IN: " + text;
				footerHintLabel.ForeColor = Color.WhiteSmoke;
				return;
			}
		}
		footerHintLabel.Text = ((snapshot?.Connected ?? latestSnapshot?.Connected ?? false) ? "ENTER = RUN · TAB = AUTOFILL" : "CONNECT = LIVE SESSION · ENTER = RUN · TAB = AUTOFILL");
		footerHintLabel.ForeColor = Color.WhiteSmoke;
	}

	private void UpdateConnectionStatusIndicator(TelemetrySnapshot? snapshot = null)
	{
		bool flag = snapshot?.Connected ?? latestSnapshot?.Connected ?? false;
		if (connectAttemptInFlight)
		{
			connectionStatusLabel.Text = "◐ CONNECTING";
			connectionStatusLabel.ForeColor = WarningColor;
			return;
		}
		if (flag)
		{
			connectionStatusLabel.Text = "● CONNECTED";
			connectionStatusLabel.ForeColor = AccentGreen;
			return;
		}
		connectionStatusLabel.Text = "● DISCONNECTED";
		connectionStatusLabel.ForeColor = WarningColor;
	}

	private void OpenPath(string path, bool isFile)
	{
		try
		{
			if (isFile && File.Exists(path))
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
				{
					UseShellExecute = true
				});
				return;
			}
			if (Directory.Exists(path))
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
				{
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			AppendSystemLine("Failed to open path: " + ex.Message, Color.IndianRed);
		}
	}

	private static string FormatTemp(uint? value)
	{
		if (!value.HasValue)
		{
			return "--°C";
		}
		return value.Value + "°C";
	}

	private static string FormatThermalRow(string label, uint? value)
	{
		return label.PadRight(3) + "  " + FormatTemp(value).PadRight(5) + "  " + BuildMeter(value);
	}

	private static string BuildMeter(uint? value)
	{
		if (!value.HasValue)
		{
			return "--------";
		}
		int num = Math.Clamp((int)Math.Round((double)value.Value / 100.0 * 8.0), 0, 8);
		return new string('█', num).PadRight(8, '·');
	}

	private static string FitStatusText(string? value, int maxLength)
	{
		string text = TrimOrNull(value) ?? "unknown";
		if (text.Length <= maxLength)
		{
			return text;
		}
		return text.Substring(0, Math.Max(0, maxLength - 3)) + "...";
	}

	private static string FitFileLeaf(string? value, int maxLength)
	{
		string text = TrimOrNull(value);
		if (string.IsNullOrWhiteSpace(text))
		{
			return "--";
		}
		string fileName = Path.GetFileName(text);
		return FitStatusText(string.IsNullOrWhiteSpace(fileName) ? text : fileName, maxLength);
	}

	private static string FormatTitleId(uint? value)
	{
		if (!value.HasValue)
		{
			return "--";
		}
		return "0x" + value.Value.ToString("X8");
	}

	private string BuildDisconnectedStatusText()
	{
		return "STATUS\nLINK     OFFLINE\nTARGET   " + options.Ip + ":" + options.Port + "\nFTP      idle until link\nACTION   press CONNECT";
	}

	private static string BuildDisconnectedSessionText()
	{
		return "SESSION\nNo live console context yet.\nConnect to load title, XEX,\nuser, sign-in, and queue state.";
	}

	private static string BuildDisconnectedShellText()
	{
		return "DISCONNECTED SESSION\r\nUse CONNECT to activate live thermals, traffic, queue, and title state.\r\nLocal XeCLI commands remain available below.";
	}

	private void UpdateShellScaffold(TelemetrySnapshot? snapshot = null)
	{
		if (connectAttemptInFlight)
		{
			SetLabelText(shellScaffoldLabel, "NEGOTIATING LINK\r\nHandshake in progress.\r\nLive telemetry will hydrate automatically.");
			return;
		}
		TelemetrySnapshot? telemetrySnapshot = snapshot ?? latestSnapshot;
		if (telemetrySnapshot != null && telemetrySnapshot.Connected)
		{
			string text = FitStatusText(TrimOrNull(telemetrySnapshot.TitleName) ?? "unknown", 28);
			string text2 = FitStatusText(TrimOrNull(telemetrySnapshot.ExecutionState) ?? "unknown", 12);
			string text3 = FitStatusText(TrimOrNull(telemetrySnapshot.Gamertag) ?? "not detected", 12);
			SetLabelText(shellScaffoldLabel, "LIVE SESSION ONLINE\r\nTitle: " + text + "\r\nState: " + text2 + "  User: " + text3);
			return;
		}
		SetLabelText(shellScaffoldLabel, BuildDisconnectedShellText());
	}

	private static void SetLabelText(Label label, string value)
	{
		if (!string.Equals(label.Text, value, StringComparison.Ordinal))
		{
			label.Text = value;
		}
	}

	private static string? TrimOrNull(string? value)
	{
		string text = value?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}

	private sealed class SlimListPanel : Control
	{
		private readonly List<string> items = new List<string>();

		private int firstVisibleIndex;

		private int selectedIndex = -1;

		public event Action<int>? ItemActivated;

		public void SetItems(IEnumerable<string> source)
		{
			List<string> list = source.Take(512).ToList();
			if (items.SequenceEqual(list, StringComparer.Ordinal))
			{
				return;
			}
			items.Clear();
			items.AddRange(list);
			firstVisibleIndex = Math.Clamp(firstVisibleIndex, 0, Math.Max(0, items.Count - 1));
			if (items.Count == 0)
			{
				selectedIndex = -1;
			}
			else if (selectedIndex >= items.Count)
			{
				selectedIndex = items.Count - 1;
			}
			Invalidate();
		}

		public SlimListPanel()
		{
			DoubleBuffered = true;
			ResizeRedraw = true;
			SetStyle(ControlStyles.Selectable, value: true);
			MouseWheel += delegate(object? _, MouseEventArgs e)
			{
				int num = GetVisibleLineCount();
				int num2 = Math.Max(1, num / 3);
				if (e.Delta < 0)
				{
					firstVisibleIndex = Math.Min(Math.Max(0, items.Count - num), firstVisibleIndex + num2);
				}
				else if (e.Delta > 0)
				{
					firstVisibleIndex = Math.Max(0, firstVisibleIndex - num2);
				}
				Invalidate();
			};
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
			e.Graphics.Clear(base.BackColor);
			int num = GetLineHeight();
			int visibleLineCount = GetVisibleLineCount();
			int num2 = Math.Min(items.Count, firstVisibleIndex + visibleLineCount);
			using SolidBrush brush = new SolidBrush(ForeColor);
			int num3 = 0;
			for (int i = firstVisibleIndex; i < num2; i++)
			{
				float y = num3 * num + 2;
				if (i == selectedIndex)
				{
					Rectangle rectangle = new Rectangle(1, (int)y, Math.Max(1, base.Width - 9), num);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(184, 48, 88, 44));
			using Pen pen = new Pen(Color.FromArgb(214, AccentGreen), 1f);
			e.Graphics.FillRectangle(brush2, rectangle);
			e.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
			using Pen pen2 = new Pen(Color.FromArgb(164, Color.WhiteSmoke), 1f);
					e.Graphics.DrawLine(pen2, rectangle.X + 1, rectangle.Y + 1, rectangle.Right - 2, rectangle.Y + 1);
				}
				e.Graphics.DrawString(items[i], Font, brush, new PointF(4f, y));
				num3++;
			}
			DrawScrollBar(e.Graphics, visibleLineCount);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (!Focused && CanFocus)
			{
				Focus();
			}
			int itemIndexAt = GetItemIndexAt(e.Y);
			if (itemIndexAt >= 0 && itemIndexAt < items.Count)
			{
				Action action = delegate
				{
					if (base.IsDisposed)
					{
						return;
					}
					selectedIndex = itemIndexAt;
					Invalidate();
				};
				if (IsHandleCreated)
				{
					BeginInvoke(action);
				}
				else
				{
					action();
				}
			}
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			int itemIndexAt = GetItemIndexAt(e.Y);
			if (itemIndexAt >= 0 && itemIndexAt < items.Count)
			{
				Action action = delegate
				{
					if (base.IsDisposed)
					{
						return;
					}
					selectedIndex = itemIndexAt;
					Invalidate();
					ItemActivated?.Invoke(itemIndexAt);
				};
				if (IsHandleCreated)
				{
					BeginInvoke(action);
				}
				else
				{
					action();
				}
			}
		}

		private void DrawScrollBar(Graphics graphics, int visibleLines)
		{
			if (items.Count <= visibleLines || visibleLines <= 0)
			{
				return;
			}
			Rectangle rectangle = new Rectangle(base.Width - 7, 2, 4, base.Height - 4);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(26, 58, 76, 84));
			graphics.FillRectangle(brush, rectangle);
			double num = (double)visibleLines / (double)items.Count;
			int height = Math.Max(14, (int)(rectangle.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, items.Count - visibleLines);
			int y = rectangle.Top + (int)((rectangle.Height - height) * num2);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(130, AccentCyan));
			graphics.FillRectangle(brush2, new Rectangle(rectangle.Left, y, rectangle.Width, height));
		}

		private int GetLineHeight()
		{
			return Math.Max(14, TextRenderer.MeasureText("W", Font).Height + 1);
		}

		private int GetVisibleLineCount()
		{
			return Math.Max(1, base.Height / GetLineHeight());
		}

		private int GetItemIndexAt(int y)
		{
			int lineHeight = GetLineHeight();
			if (lineHeight <= 0)
			{
				return -1;
			}
			int num = Math.Max(0, y / lineHeight);
			return firstVisibleIndex + num;
		}
	}

	private sealed class FtpTrafficGraphControl : Control
	{
		private readonly Queue<(double Rx, double Tx)> samples = new Queue<(double, double)>();

		public FtpTrafficGraphControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			DoubleBuffered = true;
			ResizeRedraw = true;
		}

		public void AddSample(double rxKbps, double txKbps)
		{
			int num = Math.Max(36, base.Width / 6);
			samples.Enqueue((Math.Clamp(rxKbps, 0.0, 120000.0), Math.Clamp(txKbps, 0.0, 120000.0)));
			while (samples.Count > num)
			{
				samples.Dequeue();
			}
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			e.Graphics.Clear(TerminalBackground);
			Rectangle rectangle = new Rectangle(4, 4, Math.Max(1, base.Width - 8), Math.Max(1, base.Height - 8));
			using (Pen pen = new Pen(Color.FromArgb(30, BorderColor), 1f))
			{
				for (int i = rectangle.Left; i <= rectangle.Right; i += 24)
				{
					e.Graphics.DrawLine(pen, i, rectangle.Top, i, rectangle.Bottom);
				}
				for (int j = rectangle.Top; j <= rectangle.Bottom; j += 18)
				{
					e.Graphics.DrawLine(pen, rectangle.Left, j, rectangle.Right, j);
				}
			}
			if (samples.Count == 0)
			{
				using Font idleFont = new Font("Consolas", 8f, FontStyle.Bold);
				using SolidBrush idleBrush = new SolidBrush(Color.FromArgb(210, AccentDim));
				using Pen ghostPen = new Pen(Color.FromArgb(90, AccentGreen), 1.3f);
				e.Graphics.DrawString("IDLE", idleFont, idleBrush, rectangle.Left + 2, rectangle.Top + 2);
				e.Graphics.DrawLine(ghostPen, rectangle.Left + 2, rectangle.Bottom - rectangle.Height * 0.34f, rectangle.Right - 2, rectangle.Bottom - rectangle.Height * 0.34f);
				e.Graphics.DrawLine(ghostPen, rectangle.Left + 2, rectangle.Bottom - rectangle.Height * 0.58f, rectangle.Right - 2, rectangle.Bottom - rectangle.Height * 0.58f);
				return;
			}
			List<(double Rx, double Tx)> list = samples.ToList();
			if (list.Count == 1)
			{
				list.Insert(0, list[0]);
			}
			double num = Math.Max(list.Max((ValueTuple<double, double> s) => Math.Max(s.Item1, s.Item2)), 64.0);
			PointF[] array = new PointF[list.Count];
			PointF[] array2 = new PointF[list.Count];
			int num2 = 0;
			foreach (var sample in list)
			{
				float x = rectangle.Left + (float)num2 / (float)Math.Max(1, list.Count - 1) * rectangle.Width;
				float y = rectangle.Bottom - (float)(sample.Item1 / num) * rectangle.Height;
				float y2 = rectangle.Bottom - (float)(sample.Item2 / num) * rectangle.Height;
				array[num2] = new PointF(x, y);
				array2[num2] = new PointF(x, y2);
				num2++;
			}
			PointF[] array3 = array.Concat(new PointF[2]
			{
				new PointF(rectangle.Right, rectangle.Bottom),
				new PointF(rectangle.Left, rectangle.Bottom)
			}).ToArray();
			PointF[] array4 = array2.Concat(new PointF[2]
			{
				new PointF(rectangle.Right, rectangle.Bottom),
				new PointF(rectangle.Left, rectangle.Bottom)
			}).ToArray();
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(48, AccentGreen));
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(20, AccentGreen));
			e.Graphics.FillPolygon(brush3, array4);
			e.Graphics.FillPolygon(brush2, array3);
			using Pen pen2 = new Pen(Color.FromArgb(228, AccentGreen), 1.8f);
			using Pen pen3 = new Pen(Color.FromArgb(142, AccentDim), 1.35f);
			e.Graphics.DrawLines(pen2, array);
			e.Graphics.DrawLines(pen3, array2);
			using Font axisFont = new Font("Consolas", 7.5f, FontStyle.Bold);
			using SolidBrush axisBrush = new SolidBrush(AccentDim);
			e.Graphics.DrawString(num.ToString("0"), axisFont, axisBrush, rectangle.Right - 34, rectangle.Top + 2);
			e.Graphics.DrawString("0", axisFont, axisBrush, rectangle.Right - 14, rectangle.Bottom - 14);
		}
	}

	private sealed class WireframeXboxControl : Control
	{
		private readonly record struct TriangleFace(int A, int B, int C);

		private readonly record struct ProjectedVertex(Vector3 World, PointF Screen, bool Visible);

		private readonly record struct ProxySegment(Vector3 A, Vector3 B, float Emphasis);

		private sealed class MeshModel
		{
			public required Vector3[] Vertices { get; init; }

			public required TriangleFace[] Faces { get; init; }

			public required FeatureEdge[] FeatureEdges { get; init; }

			public required Vector2[] TexCoords { get; init; }

			public required Color[] FaceColors { get; init; }

			public Bitmap? TextureImage { get; init; }
		}

		private sealed class FeatureEdgeInfo
		{
			public int A { get; set; }

			public int B { get; set; }

			public Vector3 FirstNormal { get; set; }

			public Vector3 SecondNormal { get; set; }

			public int FaceCount { get; set; }
		}

		private readonly record struct FeatureEdge(int A, int B, Vector3 FirstNormal, Vector3 SecondNormal, int FaceCount, float CreaseDot);

		private readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();

		private const float RotationRadiansPerSecond = 1.62f;

		private static readonly ProxySegment[] StylizedProxySegments = CreateStylizedProxySegments();

		private MeshModel? meshModel;

		private Image? fallbackImage;

		private bool meshAssetsLoading = true;

		private bool meshAssetsRequested;

		private bool meshAssetsFailed;

		private ProjectedVertex[] projectedVertices = Array.Empty<ProjectedVertex>();

		private float angle;

		private long lastFrameTimestamp = Stopwatch.GetTimestamp();

		private bool paintInFlight;

		public WireframeXboxControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			DoubleBuffered = true;
			ResizeRedraw = true;
			animationTimer.Interval = 24;
			animationTimer.Tick += delegate
			{
				if (!paintInFlight && Visible && IsHandleCreated && Width > 0 && Height > 0)
				{
					long timestamp = Stopwatch.GetTimestamp();
					double num = (double)(timestamp - lastFrameTimestamp) / (double)Stopwatch.Frequency;
					lastFrameTimestamp = timestamp;
					if (num > 0.0)
					{
						angle += (float)Math.Min(num, 0.05) * RotationRadiansPerSecond;
					}
					Invalidate();
				}
			};
			animationTimer.Start();
			HandleCreated += delegate
			{
				StartMeshAssetLoad();
			};
			Disposed += delegate
			{
				animationTimer.Stop();
				animationTimer.Dispose();
				DisposeMeshTexture();
				fallbackImage?.Dispose();
			};
		}

		private void StartMeshAssetLoad()
		{
			if (meshAssetsRequested || IsDisposed)
			{
				return;
			}
			meshAssetsRequested = true;
			_ = LoadMeshAssetsAsync();
		}

		private async Task LoadMeshAssetsAsync()
		{
			(MeshModel? model, Image? fallback) tuple = await Task.Run(() =>
			{
				MeshModel? meshModel = TryLoadMeshModel();
				return (meshModel, meshModel == null ? LoadFallbackImage() : null);
			});
			if (IsDisposed)
			{
				tuple.fallback?.Dispose();
				return;
			}
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() => ApplyMeshAssets(tuple.model, tuple.fallback)));
			}
			else
			{
				ApplyMeshAssets(tuple.model, tuple.fallback);
			}
		}

		private void ApplyMeshAssets(MeshModel? model, Image? fallback)
		{
			if (IsDisposed)
			{
				fallback?.Dispose();
				model?.TextureImage?.Dispose();
				return;
			}
			DisposeMeshTexture();
			meshModel = model;
			fallbackImage?.Dispose();
			fallbackImage = fallback;
			meshAssetsLoading = false;
			meshAssetsFailed = model == null;
			Invalidate();
		}

		private void DisposeMeshTexture()
		{
			meshModel?.TextureImage?.Dispose();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			paintInFlight = true;
			try
			{
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				e.Graphics.PixelOffsetMode = PixelOffsetMode.Default;
				e.Graphics.Clear(TerminalBackground);
				if (meshAssetsLoading)
				{
					if (fallbackImage != null)
					{
						DrawFallbackImage(e.Graphics);
					}
					return;
				}
				if (meshModel != null)
				{
					if (meshModel.Vertices.Length > 0 && meshModel.Faces.Length > 0)
					{
						DrawMeshViewport(e.Graphics);
					}
					return;
				}
				if (fallbackImage != null)
				{
					DrawFallbackImage(e.Graphics);
				}
			}
			finally
			{
				paintInFlight = false;
			}
		}

		private void DrawMeshViewport(Graphics graphics)
		{
			if (meshModel == null || meshModel.Vertices.Length == 0 || meshModel.Faces.Length == 0)
			{
				return;
			}
			Rectangle rectangle = new Rectangle(4, 6, Math.Max(1, Width - 8), Math.Max(1, Height - 12));
			float num = Math.Min(rectangle.Width, rectangle.Height) * 1.24f;
			float num2 = rectangle.Left + rectangle.Width * 0.53f;
			float num3 = rectangle.Top + rectangle.Height * 0.57f;
			float num4 = 6.45f;
			float num5 = angle * 0.74f + 1.18f;
			Matrix4x4 matrix4x = Matrix4x4.CreateRotationX(-0.22f) * Matrix4x4.CreateRotationY(num5);
			if (projectedVertices.Length != meshModel.Vertices.Length)
			{
				projectedVertices = new ProjectedVertex[meshModel.Vertices.Length];
			}
			for (int i = 0; i < meshModel.Vertices.Length; i++)
			{
				Vector3 vector = Vector3.Transform(meshModel.Vertices[i], matrix4x);
				bool flag = TryProject(vector, num, num2, num3, num4, out PointF value);
				projectedVertices[i] = new ProjectedVertex(vector, value, flag);
			}
			List<(float Depth, PointF[] Points, Color Color)> list = new List<(float, PointF[], Color)>(meshModel.Faces.Length);
			Vector3 vector2 = Vector3.Normalize(new Vector3(-0.22f, 0.35f, 0.91f));
			Vector3 vector3 = new Vector3(0f, 0f, num4);
			for (int j = 0; j < meshModel.Faces.Length; j++)
			{
				TriangleFace triangleFace = meshModel.Faces[j];
				ProjectedVertex projectedVertex = projectedVertices[triangleFace.A];
				ProjectedVertex projectedVertex2 = projectedVertices[triangleFace.B];
				ProjectedVertex projectedVertex3 = projectedVertices[triangleFace.C];
				if (!projectedVertex.Visible || !projectedVertex2.Visible || !projectedVertex3.Visible)
				{
					continue;
				}
				Vector3 value2 = projectedVertex2.World - projectedVertex.World;
				Vector3 value3 = projectedVertex3.World - projectedVertex.World;
				Vector3 vector4 = Vector3.Cross(value2, value3);
				if (vector4.LengthSquared() < 0.00001f)
				{
					continue;
				}
				Vector3 vector5 = Vector3.Normalize(vector4);
				Vector3 value4 = vector3 - ((projectedVertex.World + projectedVertex2.World + projectedVertex3.World) / 3f);
				Vector3 vector6 = (value4.LengthSquared() > 0.00001f) ? Vector3.Normalize(value4) : Vector3.UnitZ;
				float num6 = Vector3.Dot(vector5, vector6);
				if (num6 >= 0f)
				{
					continue;
				}
				float num7 = MathF.Max(0.58f, MathF.Abs(Vector3.Dot(vector5, vector2)));
				Color color = ((meshModel.FaceColors.Length > j) ? meshModel.FaceColors[j] : AccentGreen);
				color = ShadeFaceColor(color, num7);
				float num8 = (projectedVertex.World.Z + projectedVertex2.World.Z + projectedVertex3.World.Z) / 3f;
				list.Add((num8, new PointF[3] { projectedVertex.Screen, projectedVertex2.Screen, projectedVertex3.Screen }, color));
			}
			list.Sort(((float Depth, PointF[] Points, Color Color) a, (float Depth, PointF[] Points, Color Color) b) => a.Depth.CompareTo(b.Depth));
			using SolidBrush solidBrush = new SolidBrush(Color.White);
			foreach ((float Depth, PointF[] Points, Color Color) item in list)
			{
				solidBrush.Color = item.Color;
				graphics.FillPolygon(solidBrush, item.Points);
			}
		}

		private static Color ShadeFaceColor(Color color, float intensity)
		{
			intensity = Math.Clamp(intensity, 0.76f, 1f);
			int red = Math.Clamp((int)((float)(int)color.R * intensity), 0, 255);
			int green = Math.Clamp((int)((float)(int)color.G * intensity), 0, 255);
			int blue = Math.Clamp((int)((float)(int)color.B * intensity), 0, 255);
			red = Math.Clamp(red + (255 - red) / 6, 0, 255);
			green = Math.Clamp(green + (255 - green) / 6, 0, 255);
			blue = Math.Clamp(blue + (255 - blue) / 6, 0, 255);
			return Color.FromArgb(246, red, green, blue);
		}

		private static ProxySegment[] CreateStylizedProxySegments()
		{
			List<ProxySegment> list = new List<ProxySegment>();
			void Add(Vector3 a, Vector3 b, float emphasis = 1f)
			{
				list.Add(new ProxySegment(a, b, emphasis));
			}
			Vector3 vector = new Vector3(-0.34f, -1.12f, -0.24f);
			Vector3 vector2 = new Vector3(0.34f, -1.12f, -0.24f);
			Vector3 vector3 = new Vector3(-0.26f, 1.12f, -0.24f);
			Vector3 vector4 = new Vector3(0.26f, 1.12f, -0.24f);
			Vector3 vector5 = new Vector3(-0.54f, -1.02f, 0.46f);
			Vector3 vector6 = new Vector3(0.54f, -1.02f, 0.46f);
			Vector3 vector7 = new Vector3(-0.42f, 1.18f, 0.46f);
			Vector3 vector8 = new Vector3(0.42f, 1.18f, 0.46f);
			Add(vector, vector2);
			Add(vector3, vector4);
			Add(vector, vector3);
			Add(vector2, vector4);
			Add(vector5, vector6);
			Add(vector7, vector8);
			Add(vector5, vector7);
			Add(vector6, vector8);
			Add(vector, vector5);
			Add(vector2, vector6);
			Add(vector3, vector7);
			Add(vector4, vector8);
			Vector3 vector9 = new Vector3(-0.19f, 0.18f, -0.235f);
			Vector3 vector10 = new Vector3(0.19f, 0.18f, -0.235f);
			Vector3 vector11 = new Vector3(-0.17f, 0.82f, -0.235f);
			Vector3 vector12 = new Vector3(0.17f, 0.82f, -0.235f);
			Add(vector9, vector10, 0.88f);
			Add(vector11, vector12, 0.88f);
			Add(vector9, vector11, 0.88f);
			Add(vector10, vector12, 0.88f);
			Add(new Vector3(-0.12f, 0.49f, -0.235f), new Vector3(0.12f, 0.49f, -0.235f), 0.8f);
			Vector3 vector13 = new Vector3(0.12f, -0.6f, -0.235f);
			float num = 0.14f;
			Vector3? value = null;
			for (int i = 0; i <= 12; i++)
			{
				float num2 = (float)i / 12f * (float)Math.PI * 2f;
				Vector3 value2 = new Vector3(vector13.X + MathF.Cos(num2) * num, vector13.Y + MathF.Sin(num2) * num, vector13.Z);
				if (value.HasValue)
				{
					Add(value.Value, value2, 0.72f);
				}
				value = value2;
			}
			Add(new Vector3(-0.08f, -0.82f, -0.235f), new Vector3(0.04f, -0.82f, -0.235f), 0.72f);
			Vector3 vector14 = new Vector3(-0.2f, -0.88f, -0.235f);
			Vector3 vector15 = new Vector3(-0.03f, -0.88f, -0.235f);
			Vector3 vector16 = new Vector3(-0.19f, -0.68f, -0.235f);
			Vector3 vector17 = new Vector3(-0.04f, -0.68f, -0.235f);
			Add(vector14, vector15, 0.76f);
			Add(vector16, vector17, 0.76f);
			Add(vector14, vector16, 0.76f);
			Add(vector15, vector17, 0.76f);
			Add(new Vector3(0.34f, -1.12f, -0.24f), new Vector3(0.54f, -1.02f, 0.46f), 0.8f);
			Add(new Vector3(0.26f, 1.12f, -0.24f), new Vector3(0.42f, 1.18f, 0.46f), 0.8f);
			for (int j = 0; j < 4; j++)
			{
				float num3 = -0.15f + (float)j * 0.09f;
				Add(new Vector3(-0.38f, 0.32f + num3, 0.43f), new Vector3(0.18f, 0.38f + num3, 0.43f), 0.48f);
			}
			for (int k = 0; k < 3; k++)
			{
				float num4 = -0.22f + (float)k * 0.14f;
				Add(new Vector3(0.28f, -0.2f + num4, 0.12f), new Vector3(0.47f, -0.15f + num4, 0.42f), 0.42f);
			}
			return list.ToArray();
		}

		private static bool TryProject(Vector3 point, float focalLength, float centerX, float centerY, float cameraDistance, out PointF projected)
		{
			float num = cameraDistance - point.Z;
			if (num <= 0.2f)
			{
				projected = default(PointF);
				return false;
			}
			float num2 = focalLength / num;
			projected = new PointF(centerX + point.X * num2, centerY - point.Y * num2);
			return true;
		}

		private void DrawFallbackImage(Graphics graphics)
		{
			if (fallbackImage == null)
			{
				return;
			}
			Rectangle rectangle = GetImageFitRect(fallbackImage.Size, new Rectangle(30, 44, Math.Max(1, Width - 60), Math.Max(1, Height - 96)));
			using ImageAttributes imageAttributes = new ImageAttributes();
			ColorMatrix newColorMatrix = new ColorMatrix(new float[5][]
			{
				new float[5] { 0.58f, 0f, 0f, 0f, 0f },
				new float[5] { 0f, 0.64f, 0f, 0f, 0f },
				new float[5] { 0f, 0f, 0.68f, 0f, 0f },
				new float[5] { 0f, 0f, 0f, 0.42f, 0f },
				new float[5] { 0f, 0f, 0f, 0f, 1f }
			});
			imageAttributes.SetColorMatrix(newColorMatrix);
			graphics.DrawImage(fallbackImage, rectangle, 0, 0, fallbackImage.Width, fallbackImage.Height, GraphicsUnit.Pixel, imageAttributes);
		}

		private static Rectangle GetImageFitRect(Size imageSize, Rectangle bounds)
		{
			float num = Math.Min((float)bounds.Width / (float)imageSize.Width, (float)bounds.Height / (float)imageSize.Height);
			int width = Math.Max(1, (int)Math.Round((float)imageSize.Width * num));
			int height = Math.Max(1, (int)Math.Round((float)imageSize.Height * num));
			return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
		}

		private static Image? LoadFallbackImage()
		{
			return null;
		}

		private static MeshModel? TryLoadMeshModel()
		{
			string text = ResolveAssetPath("xbox360-slim-textured.gltf");
			if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
			{
				text = ResolveAssetPath("xbox360-slim.gltf");
			}
			if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
			{
				return null;
			}
			try
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(text));
				JsonElement rootElement = jsonDocument.RootElement;
				JsonElement jsonElement = rootElement.GetProperty("accessors");
				JsonElement jsonElement2 = rootElement.GetProperty("bufferViews");
				JsonElement? jsonElement3 = rootElement.TryGetProperty("images", out JsonElement imagesElement) ? imagesElement : null;
				JsonElement? jsonElement4 = rootElement.TryGetProperty("textures", out JsonElement texturesElement) ? texturesElement : null;
				JsonElement? jsonElement5 = rootElement.TryGetProperty("materials", out JsonElement materialsElement) ? materialsElement : null;
				List<byte[]> list = rootElement.GetProperty("buffers").EnumerateArray().Select(delegate(JsonElement buffer)
				{
					string stringValue = buffer.GetProperty("uri").GetString() ?? string.Empty;
					int num4 = stringValue.IndexOf(',', StringComparison.Ordinal);
					return Convert.FromBase64String(stringValue.Substring(num4 + 1));
				}).ToList();
				JsonElement[] array = rootElement.TryGetProperty("nodes", out JsonElement value4) ? value4.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
				Dictionary<int, Matrix4x4[]> dictionary = BuildMeshTransforms(array);
				List<Vector3> list2 = new List<Vector3>();
				List<TriangleFace> list3 = new List<TriangleFace>();
				List<Vector2> list4 = new List<Vector2>();
				Bitmap? bitmap = null;
				int num = -1;
				foreach (JsonElement item in rootElement.GetProperty("meshes").EnumerateArray())
				{
					num++;
					Matrix4x4[] array2 = dictionary.TryGetValue(num, out Matrix4x4[]? value5) ? value5 : new Matrix4x4[1]
					{
						Matrix4x4.Identity
					};
					foreach (Matrix4x4 matrix4x in array2)
					{
						foreach (JsonElement item2 in item.GetProperty("primitives").EnumerateArray())
						{
							int int32 = item2.GetProperty("attributes").GetProperty("POSITION").GetInt32();
							Vector3[] array3 = ReadPositions(jsonElement[int32], jsonElement2, list);
							if (array3.Length == 0)
							{
								continue;
							}
							if (!matrix4x.IsIdentity)
							{
								for (int i = 0; i < array3.Length; i++)
								{
									array3[i] = Vector3.Transform(array3[i], matrix4x);
								}
							}
							Vector2[] array4 = Array.Empty<Vector2>();
							if (item2.GetProperty("attributes").TryGetProperty("TEXCOORD_0", out JsonElement value3))
							{
								array4 = ReadTexCoords(jsonElement[value3.GetInt32()], jsonElement2, list);
							}
							if (array4.Length != array3.Length)
							{
								array4 = Enumerable.Repeat(Vector2.Zero, array3.Length).ToArray();
							}
							int[] array5 = item2.TryGetProperty("indices", out JsonElement value) ? ReadIndices(jsonElement[value.GetInt32()], jsonElement2, list) : Enumerable.Range(0, array3.Length).ToArray();
							int num2 = item2.TryGetProperty("mode", out JsonElement value2) ? value2.GetInt32() : 4;
							int count = list2.Count;
							list2.AddRange(array3);
							list4.AddRange(array4);
							if (bitmap == null)
							{
								bitmap = TryLoadTextureBitmap(text, item2, jsonElement3, jsonElement4, jsonElement5);
							}
							list3.AddRange(BuildFaces(array5, num2, count));
						}
					}
				}
				if (list2.Count == 0 || list3.Count == 0)
				{
					return null;
				}
				Vector3[] vertices = NormalizeVertices(list2.ToArray());
				TriangleFace[] faces = list3.ToArray();
				if (faces.Length > 1200)
				{
					int num2 = Math.Max(1, faces.Length / 1200);
					faces = faces.Where((TriangleFace _, int index) => index % num2 == 0).ToArray();
				}
				FeatureEdge[] featureEdges = Array.Empty<FeatureEdge>();
				Color[] faceColors = Array.Empty<Color>();
				if (bitmap != null && list4.Count >= vertices.Length)
				{
					faceColors = faces.Select(delegate(TriangleFace face)
					{
						Vector2 uv = (list4[face.A] + list4[face.B] + list4[face.C]) / 3f;
						return SampleTextureColor(bitmap, uv);
					}).ToArray();
				}
				return new MeshModel
				{
					Vertices = vertices,
					Faces = faces,
					FeatureEdges = featureEdges,
					TexCoords = list4.ToArray(),
					FaceColors = faceColors,
					TextureImage = bitmap
				};
			}
			catch
			{
				return null;
			}
		}

		private static string? ResolveAssetPath(string fileName)
		{
			string[] array = new string[3]
			{
				Path.Combine(AppContext.BaseDirectory, "Assets", "models", "xbox360-slim", fileName),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets", "models", "xbox360-slim", fileName)),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "models", "xbox360-slim", fileName))
            };
            return array.FirstOrDefault(File.Exists);
        }

		private static Color Blend(Color first, Color second, float amount)
		{
			float num = Math.Clamp(amount, 0f, 1f);
			return Color.FromArgb((int)Math.Round((float)first.R + ((float)second.R - (float)first.R) * num), (int)Math.Round((float)first.G + ((float)second.G - (float)first.G) * num), (int)Math.Round((float)first.B + ((float)second.B - (float)first.B) * num));
		}

		private static Vector3[] NormalizeVertices(Vector3[] vertices)
		{
			if (vertices.Length == 0)
			{
				return vertices;
			}
			Vector3 vector = new Vector3(vertices.Min((Vector3 v) => v.X), vertices.Min((Vector3 v) => v.Y), vertices.Min((Vector3 v) => v.Z));
			Vector3 vector2 = new Vector3(vertices.Max((Vector3 v) => v.X), vertices.Max((Vector3 v) => v.Y), vertices.Max((Vector3 v) => v.Z));
			Vector3 vector3 = (vector + vector2) * 0.5f;
			float num = Math.Max(Math.Max(vector2.X - vector.X, vector2.Y - vector.Y), vector2.Z - vector.Z);
			if (num <= 0f)
			{
				num = 1f;
			}
			return vertices.Select((Vector3 v) => new Vector3((v.X - vector3.X) / num * 4.9f, -((v.Y - vector3.Y) / num) * 4.9f, (v.Z - vector3.Z) / num * 4.9f)).ToArray();
		}

		private static Dictionary<int, Matrix4x4[]> BuildMeshTransforms(JsonElement[] nodes)
		{
			if (nodes.Length == 0)
			{
				return new Dictionary<int, Matrix4x4[]>();
			}
			int[] array = Enumerable.Repeat(-1, nodes.Length).ToArray();
			for (int i = 0; i < nodes.Length; i++)
			{
				if (!nodes[i].TryGetProperty("children", out JsonElement value))
				{
					continue;
				}
				foreach (JsonElement item in value.EnumerateArray())
				{
					int int32 = item.GetInt32();
					if (int32 >= 0 && int32 < array.Length)
					{
						array[int32] = i;
					}
				}
			}
			Matrix4x4[] array2 = new Matrix4x4[nodes.Length];
			bool[] array3 = new bool[nodes.Length];
			Matrix4x4 GetWorldTransform(int index)
			{
				if (array3[index])
				{
					return array2[index];
				}
				Matrix4x4 matrix4x = ReadNodeTransform(nodes[index]);
				if (array[index] >= 0)
				{
					matrix4x *= GetWorldTransform(array[index]);
				}
				array2[index] = matrix4x;
				array3[index] = true;
				return matrix4x;
			}
			Dictionary<int, List<Matrix4x4>> dictionary = new Dictionary<int, List<Matrix4x4>>();
			for (int j = 0; j < nodes.Length; j++)
			{
				if (!nodes[j].TryGetProperty("mesh", out JsonElement value2))
				{
					continue;
				}
				int int33 = value2.GetInt32();
				if (!dictionary.TryGetValue(int33, out List<Matrix4x4>? value3))
				{
					value3 = new List<Matrix4x4>();
					dictionary[int33] = value3;
				}
				value3.Add(GetWorldTransform(j));
			}
			return dictionary.ToDictionary((KeyValuePair<int, List<Matrix4x4>> pair) => pair.Key, (KeyValuePair<int, List<Matrix4x4>> pair) => pair.Value.ToArray());
		}

		private static Matrix4x4 ReadNodeTransform(JsonElement node)
		{
			Vector3 scale = Vector3.One;
			Vector3 translation = Vector3.Zero;
			Quaternion quaternion = Quaternion.Identity;
			if (node.TryGetProperty("scale", out JsonElement value) && value.GetArrayLength() == 3)
			{
				scale = new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
			}
			if (node.TryGetProperty("translation", out JsonElement value2) && value2.GetArrayLength() == 3)
			{
				translation = new Vector3(value2[0].GetSingle(), value2[1].GetSingle(), value2[2].GetSingle());
			}
			if (node.TryGetProperty("rotation", out JsonElement value3) && value3.GetArrayLength() == 4)
			{
				quaternion = new Quaternion(value3[0].GetSingle(), value3[1].GetSingle(), value3[2].GetSingle(), value3[3].GetSingle());
			}
			return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(quaternion) * Matrix4x4.CreateTranslation(translation);
		}

		private static Vector3[] ReadPositions(JsonElement accessor, JsonElement bufferViews, List<byte[]> buffers)
		{
			int int32 = accessor.GetProperty("bufferView").GetInt32();
			JsonElement jsonElement = bufferViews[int32];
			byte[] array = buffers[jsonElement.GetProperty("buffer").GetInt32()];
			int num = (jsonElement.TryGetProperty("byteOffset", out JsonElement value) ? value.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out JsonElement value2) ? value2.GetInt32() : 0);
			int int33 = accessor.GetProperty("count").GetInt32();
			Vector3[] array2 = new Vector3[int33];
			for (int i = 0; i < int33; i++)
			{
				int num2 = num + i * 12;
				array2[i] = new Vector3(BitConverter.ToSingle(array, num2), BitConverter.ToSingle(array, num2 + 4), BitConverter.ToSingle(array, num2 + 8));
			}
			return array2;
		}

		private static int[] ReadIndices(JsonElement accessor, JsonElement bufferViews, List<byte[]> buffers)
		{
			int int32 = accessor.GetProperty("bufferView").GetInt32();
			JsonElement jsonElement = bufferViews[int32];
			byte[] array = buffers[jsonElement.GetProperty("buffer").GetInt32()];
			int num = (jsonElement.TryGetProperty("byteOffset", out JsonElement value) ? value.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out JsonElement value2) ? value2.GetInt32() : 0);
			int int33 = accessor.GetProperty("count").GetInt32();
			int int34 = accessor.GetProperty("componentType").GetInt32();
			int[] array2 = new int[int33];
			for (int i = 0; i < int33; i++)
			{
				int num2;
				switch (int34)
				{
				case 5125:
					num2 = BitConverter.ToInt32(array, num + i * 4);
					break;
				case 5123:
					num2 = BitConverter.ToUInt16(array, num + i * 2);
					break;
				case 5121:
					num2 = array[num + i];
					break;
				default:
					throw new NotSupportedException("Unsupported glTF index component type: " + int34);
				}
				array2[i] = num2;
			}
			return array2;
		}

		private static Vector2[] ReadTexCoords(JsonElement accessor, JsonElement bufferViews, List<byte[]> buffers)
		{
			int int32 = accessor.GetProperty("bufferView").GetInt32();
			JsonElement jsonElement = bufferViews[int32];
			byte[] array = buffers[jsonElement.GetProperty("buffer").GetInt32()];
			int num = (jsonElement.TryGetProperty("byteOffset", out JsonElement value) ? value.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out JsonElement value2) ? value2.GetInt32() : 0);
			int int33 = accessor.GetProperty("count").GetInt32();
			Vector2[] array2 = new Vector2[int33];
			for (int i = 0; i < int33; i++)
			{
				int num2 = num + i * 8;
				array2[i] = new Vector2(BitConverter.ToSingle(array, num2), BitConverter.ToSingle(array, num2 + 4));
			}
			return array2;
		}

		private static IEnumerable<TriangleFace> BuildFaces(int[] indices, int mode, int vertexOffset)
		{
			if (mode == 5)
			{
				for (int i = 0; i < indices.Length - 2; i++)
				{
					int num = indices[i];
					int num2 = indices[i + 1];
					int num3 = indices[i + 2];
					if ((i & 1) == 1)
					{
						(num, num2) = (num2, num);
					}
					if (num != num2 && num2 != num3 && num != num3)
					{
						yield return new TriangleFace(num + vertexOffset, num2 + vertexOffset, num3 + vertexOffset);
					}
				}
				yield break;
			}
			for (int j = 0; j < indices.Length - 2; j += 3)
			{
				int num4 = indices[j];
				int num5 = indices[j + 1];
				int num6 = indices[j + 2];
				if (num4 != num5 && num5 != num6 && num4 != num6)
				{
					yield return new TriangleFace(num4 + vertexOffset, num5 + vertexOffset, num6 + vertexOffset);
				}
			}
		}

		private static FeatureEdge[] BuildFeatureEdges(IEnumerable<TriangleFace> faces, Vector3[] vertices)
		{
			Dictionary<long, FeatureEdgeInfo> dictionary = new Dictionary<long, FeatureEdgeInfo>();
			foreach (TriangleFace face in faces)
			{
				Vector3 vector = ComputeFaceNormal(vertices[face.A], vertices[face.B], vertices[face.C]);
				AddEdge(dictionary, face.A, face.B, vector);
				AddEdge(dictionary, face.B, face.C, vector);
				AddEdge(dictionary, face.C, face.A, vector);
			}
			return dictionary.Values.Where(delegate(FeatureEdgeInfo edge)
			{
				if (edge.FaceCount <= 1)
				{
					return true;
				}
				float num = Vector3.Dot(Vector3.Normalize(edge.FirstNormal), Vector3.Normalize(edge.SecondNormal));
				return num < 0.82f;
			}).Select(delegate(FeatureEdgeInfo edge)
			{
				float creaseDot = Vector3.Dot(Vector3.Normalize(edge.FirstNormal), Vector3.Normalize(edge.SecondNormal));
				return new FeatureEdge(edge.A, edge.B, edge.FirstNormal, edge.SecondNormal, edge.FaceCount, creaseDot);
			}).ToArray();
		}

		private static void AddEdge(Dictionary<long, FeatureEdgeInfo> edges, int a, int b, Vector3 normal)
		{
			if (a == b)
			{
				return;
			}
			int num = Math.Min(a, b);
			int num2 = Math.Max(a, b);
			long key = ((long)num << 32) | (uint)num2;
			if (!edges.TryGetValue(key, out FeatureEdgeInfo value))
			{
				edges[key] = new FeatureEdgeInfo
				{
					A = num,
					B = num2,
					FirstNormal = normal,
					SecondNormal = normal,
					FaceCount = 1
				};
				return;
			}
			value.FaceCount++;
			if (value.FaceCount == 2)
			{
				value.SecondNormal = normal;
			}
			edges[key] = value;
		}

		private static Vector3 ComputeFaceNormal(Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 vector = Vector3.Cross(b - a, c - a);
			if (vector.LengthSquared() < 1E-06f)
			{
				return Vector3.UnitZ;
			}
			return Vector3.Normalize(vector);
		}

		private static Bitmap? TryLoadTextureBitmap(string gltfPath, JsonElement primitive, JsonElement? imagesElement, JsonElement? texturesElement, JsonElement? materialsElement)
		{
			if (!primitive.TryGetProperty("material", out JsonElement value) || !imagesElement.HasValue || !texturesElement.HasValue || !materialsElement.HasValue)
			{
				return null;
			}
			int int32 = value.GetInt32();
			JsonElement jsonElement = materialsElement.Value[int32];
			if (!jsonElement.TryGetProperty("pbrMetallicRoughness", out JsonElement value2) || !value2.TryGetProperty("baseColorTexture", out JsonElement value3))
			{
				return null;
			}
			int int33 = value3.GetProperty("index").GetInt32();
			JsonElement jsonElement2 = texturesElement.Value[int33];
			int int34 = jsonElement2.GetProperty("source").GetInt32();
			JsonElement jsonElement3 = imagesElement.Value[int34];
			string text = jsonElement3.GetProperty("uri").GetString() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
			{
				int num = text.IndexOf(',', StringComparison.Ordinal);
				if (num < 0)
				{
					return null;
				}
				try
				{
					using MemoryStream memoryStream = new MemoryStream(Convert.FromBase64String(text[(num + 1)..]));
					using Image image = Image.FromStream(memoryStream);
					return new Bitmap(image);
				}
				catch
				{
					return null;
				}
			}
			string? text2 = ResolveTextureAssetPath(gltfPath, text);
			if (string.IsNullOrWhiteSpace(text2) || !File.Exists(text2))
			{
				return null;
			}
			try
			{
				return LoadImageCopy(text2) as Bitmap;
			}
			catch
			{
				return null;
			}
		}

		private static Color SampleTextureColor(Bitmap texture, Vector2 uv)
		{
			Vector2 vector = new Vector2(uv.X - (float)Math.Floor(uv.X), uv.Y - (float)Math.Floor(uv.Y));
			Color color = SampleTexturePoint(texture, vector);
			Color color2 = SampleTexturePoint(texture, new Vector2(vector.X * 0.75f + 0.125f, vector.Y * 0.75f + 0.125f));
			Color color3 = SampleTexturePoint(texture, new Vector2(vector.X * 0.5f + 0.25f, vector.Y * 0.5f + 0.25f));
			return Color.FromArgb(255, (color.R + color2.R + color3.R) / 3, (color.G + color2.G + color3.G) / 3, (color.B + color2.B + color3.B) / 3);
		}

		private static Color SampleTexturePoint(Bitmap texture, Vector2 uv)
		{
			int num = Math.Clamp((int)Math.Round(uv.X * (float)Math.Max(0, texture.Width - 1)), 0, Math.Max(0, texture.Width - 1));
			int num2 = Math.Clamp((int)Math.Round((1f - uv.Y) * (float)Math.Max(0, texture.Height - 1)), 0, Math.Max(0, texture.Height - 1));
			return texture.GetPixel(num, num2);
		}

		private static string? ResolveTextureAssetPath(string gltfPath, string uri)
		{
			string text = Path.GetDirectoryName(gltfPath) ?? AppContext.BaseDirectory;
			string text2 = Path.GetFileName(uri);
			string[] array = new string[4]
			{
				Path.Combine(text, uri),
				Path.Combine(text, text2),
				Path.Combine(text, StripGeneratedAssetPrefix(text2)),
				Path.Combine(text, StripGeneratedAssetPrefix(uri))
			};
			foreach (string text3 in array.Where((string candidate) => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if (File.Exists(text3))
				{
					return text3;
				}
			}
			string text4 = StripGeneratedAssetPrefix(text2);
			return Directory.EnumerateFiles(text, "*", SearchOption.TopDirectoryOnly).FirstOrDefault((string candidate) => Path.GetFileName(candidate).EndsWith(text4, StringComparison.OrdinalIgnoreCase));
		}

		private static string StripGeneratedAssetPrefix(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
			int num = value.IndexOf('_');
			if (num > 0 && num < value.Length - 1)
			{
				return value[(num + 1)..];
			}
			return value;
		}
	}
}
