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
using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using Xbox360.Remote.Cli.LocalProfiles;
using XeCli.Localization;

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
		public int SessionEpoch { get; set; }

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

		public List<DriveInventoryEntry> Drives { get; } = new List<DriveInventoryEntry>();

		public List<string> Plugins { get; } = new List<string>();

		public List<string> Modules { get; } = new List<string>();

		public double FtpRxKbps { get; set; }

		public double FtpTxKbps { get; set; }
	}

	private sealed class DriveInventoryEntry
	{
		public required string Name { get; init; }

		public ulong? TotalBytes { get; init; }

		public ulong? FreeBytes { get; init; }
	}

	private sealed class FileEntryView
	{
		public required string Name { get; init; }

		public required string FullPath { get; init; }

		public bool IsDirectory { get; init; }

		public long? SizeBytes { get; init; }

		public DateTime? ModifiedUtc { get; init; }
	}

	private sealed class LocalBrowserSnapshot
	{
		public string? ResolvedPath { get; init; }

		public required List<FileEntryView> Entries { get; init; }

		public required List<string> Items { get; init; }

		public required string PathLabel { get; init; }
	}

	private sealed class PaneClipboardEntry
	{
		public required bool IsRemote { get; init; }

		public required string FullPath { get; init; }

		public required bool IsDirectory { get; init; }

		public required bool Move { get; set; }
	}

	private sealed class PaneDragPayload
	{
		public required bool FromRemote { get; init; }

		public required List<FileEntryView> Entries { get; init; }
	}

	private sealed class TransferQueueEntry
	{
		public required string CommandKey { get; init; }

		public required string CommandText { get; set; }

		public required string State { get; set; }

		public string? Detail { get; set; }

		public double? ProgressPercent { get; set; }

		public DateTime UpdatedAtLocal { get; set; }
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

	private readonly Font shellOutputFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

	private readonly Font headerFont = new Font("Consolas", 12.5f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Font microFont = new Font("Consolas", 6.75f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Label headerLabel = new Label();

	private readonly Label connectionStatusLabel = new Label();

	private readonly Label leftStatusLabel = new Label();

	private readonly Label leftConsoleHeaderLabel = new Label();

	private readonly Label leftConsoleLabel = new Label();

	private readonly StructuredInfoPanel leftConsoleInfoPanel = new StructuredInfoPanel();

	private readonly Label leftSignInLabel = new Label();

	private readonly Label shellTargetLabel = new Label();

	private readonly Label rightNetworkLabel = new Label();

	private readonly StructuredInfoPanel rightNetworkInfoPanel = new StructuredInfoPanel();

	private readonly Label rightStatusHeaderLabel = new Label();

	private readonly Label rightTempLabel = new Label();

	private readonly Label rightTrafficHeaderLabel = new Label();

	private readonly Label rightDetailLabel = new Label();

	private readonly Label rightDrivesLabel = new Label();

	private readonly Button connectButton = new Button();

	private readonly Button disconnectButton = new Button();

	private readonly Button screenshotButton = new Button();

	private readonly Button languageButton = new Button();

	private readonly TextBox targetIpTextBox = new TextBox();

	private readonly TextBox targetPortTextBox = new TextBox();

	private readonly Panel targetIpHostPanel = new Panel();

	private readonly Panel targetPortHostPanel = new Panel();

	private readonly Button targetApplyButton = new Button();

	private readonly Button pluginsTabButton = new Button();

	private readonly Button modulesTabButton = new Button();

	private readonly SlimListPanel inventoryList = new SlimListPanel();

	private readonly DriveInventoryPanel drivesList = new DriveInventoryPanel();

	private readonly TransferQueuePanel transferQueueList = new TransferQueuePanel();

	private readonly List<TransferQueueEntry> transferQueueEntries = new List<TransferQueueEntry>();

	private readonly PictureBox logoPictureBox = new PictureBox();

	private readonly FtpTrafficGraphControl ftpTrafficGraph = new FtpTrafficGraphControl();

	private readonly Label footerStatusLabel = new Label();

	private readonly Label footerVersionLabel = new Label();

	private readonly Label footerPresenceLabel = new Label();

	private readonly Label footerThermalLabel = new Label();

	private readonly Label mainShellBannerLabel = new Label();

	private readonly Label shellBadgeLabel = new Label();

	private readonly Label shellScaffoldLabel = new Label();

	private readonly RichTextBox terminalOutput = new RichTextBox();

	private readonly TerminalScrollIndicator terminalScrollIndicator = new TerminalScrollIndicator();

	private readonly TextBox commandInput = new TextBox();

	private readonly Panel commandInputHost = new Panel();

	private readonly SuggestionListPanel suggestionList = new SuggestionListPanel();

	private readonly Panel suggestionHost = new Panel();

	private RowStyle? suggestionRowStyle;

	private readonly Label localPathLabel = new Label();

	private readonly TextBox localPathTextBox = new TextBox();

	private readonly Panel localPathHostPanel = new Panel();

	private readonly Label remotePathLabel = new Label();

	private readonly Button localUpButton = new Button();

	private readonly Button localRefreshButton = new Button();

	private readonly Button remoteUpButton = new Button();

	private readonly Button remoteRefreshButton = new Button();

	private readonly Button ftpTabButton = new Button();

	private readonly Button queueTabButton = new Button();

	private readonly TextBox remotePathTextBox = new TextBox();

	private readonly Panel remotePathHostPanel = new Panel();

	private readonly FileGridPanel localFileList = new FileGridPanel();

	private readonly FileGridPanel remoteFileList = new FileGridPanel();

	private readonly Panel remoteContentHost = new Panel();

	private readonly ContextMenuStrip localFileMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip localEmptyMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip remoteFileMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip remoteEmptyMenu = new ContextMenuStrip();

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

	private bool shellDisconnected = true;

	private bool terminalFlushScheduled;

	private bool suppressSuggestionRefresh;

	private bool fileTransferInFlight;

	private Process? activeCommandProcess;

	private CancellationTokenSource? commandCts;

	private CancellationTokenSource? localBrowseCts;

	private CancellationTokenSource? remoteBrowseCts;

	private CancellationTokenSource? fileTransferCts;

	private CancellationTokenSource? connectProbeCts;

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

	private int localContextIndex = -1;

	private int remoteContextIndex = -1;

	private readonly DiscordRpcService? discordRpcService;

	private string currentTargetIp;

	private int currentTargetPort;

	private int sessionEpoch;

	private PaneClipboardEntry? paneClipboardEntry;

	private bool remotePaneShowsQueue;

	private bool firstConnectWorkspacePrepared;

	private string? activeTransferCommand;

	private string connectButtonTextSource = "CONNECT";

	private string commandInputPlaceholderSource = "Connect or enter a XeCLI command...";

	private string connectionStatusTextSource = "● DISCONNECTED";

	private string footerPresenceTextSource = "PRESENCE · OFFLINE";

	private string footerStatusTextSource = "DISCONNECTED";

	private string footerThermalTextSource = "CPU --°C · GPU --°C · RAM --°C · BOARD --°C";

	private string leftConsoleTextSource = "BOARD      unknown\nDASH       --\nGAME       --\nXEX        --\nTITLEID    --\nGAMERTAG   Not Signed In";

	private string leftSignInTextSource = "INVENTORY";

	private string mainShellBannerTextSource = "LINK OFFLINE";

	private string rightDetailTextSource = "No live console context.\nConnect to load title, XEX,\nuser, presence, and TitleID.";

	private string rightDrivesTitleTextSource = "DETECTED DRIVES";

	private string rightNetworkTextSource = string.Empty;

	private string rightTempTextSource = "IDLE";

	internal XeCliTerminalForm(TerminalOptions options)
	{
		this.options = options;
		currentTargetIp = options.Ip;
		currentTargetPort = options.Port;
		LocalizedText.Initialize(GetConfiguredUiLanguageCode());
		CultureInfo.CurrentUICulture = LocalizedText.Culture;
		CultureInfo.DefaultThreadCurrentUICulture = LocalizedText.Culture;
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
		LoadApplicationIcon();
		if (options.OpacityPercent < 100)
		{
			base.Opacity = Math.Max(0.55, Math.Min((double)options.OpacityPercent / 100.0, 1.0));
		}
		base.KeyPreview = true;
		InitializeLayout();
		LogRootLayoutSnapshot("ctor-after-initialize-layout");
		InitializeInteractiveActions();
		discordRpcService = (options.TelemetryEnabled ? DiscordRpcService.CreateIfConfigured() : null);
		discordRpcService?.Start();
		UpdateRuntimePresence(connected: false);
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
			BeginInvoke(new MethodInvoker(delegate
			{
				if (!commandInput.IsDisposed && commandInput.CanFocus)
				{
					commandInput.Focus();
				}
			}));
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
		AppendSystemLine(TranslateTerminalText("XeCLI terminal shell ready."), AccentGreen);
		AppendSystemLine(TranslateTerminalText("Session bus idle. Connect to activate live thermals, traffic, queue, and title state."), AccentDim);
		AppendSystemLine(TranslateTerminalText("Local commands remain available while the link is offline."), AccentDim);
		AppendSystemLine(TranslateTerminalText("Type any XeCLI command, for example: status, nand dump, xell info"), AccentDim);
		AppendSystemLine(TranslateTerminalText("Type 'exit' to close this shell UI."), AccentDim);
		UpdateConnectionStatusIndicator();
		UpdateShellScaffold();
		UpdateLiveHints();
		RefreshLocalizedUiText();
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
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 272f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
			base.Controls.Add(tableLayoutPanel);
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
			Panel panel6 = CreateCardPanel(CardBackground, new Padding(10, 4, 10, 4));
			ApplyProbeTheme(panel6, ProbeFooterColor);
			TracePanelDiagnostics(panel6, "root-footer");
			panel6.Dock = DockStyle.Fill;
			panel6.Margin = new Padding(0, 8, 0, 0);
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
			tableLayoutPanel3.Dock = DockStyle.Fill;
			tableLayoutPanel3.RowCount = 1;
			tableLayoutPanel3.ColumnCount = 3;
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
			footerVersionLabel.Dock = DockStyle.None;
			footerVersionLabel.AutoSize = true;
			footerVersionLabel.Anchor = AnchorStyles.Left;
			footerVersionLabel.Margin = new Padding(0);
			footerVersionLabel.TextAlign = ContentAlignment.MiddleLeft;
			footerVersionLabel.ForeColor = Color.FromArgb(214, 232, 244, 214);
			footerVersionLabel.Font = new Font("Consolas", 8.25f, FontStyle.Italic, GraphicsUnit.Point);
			footerVersionLabel.Text = GetFooterVersionText();
			footerVersionLabel.Cursor = Cursors.Hand;
			footerVersionLabel.Click += delegate
			{
				OpenXeCliRepository();
			};
			footerPresenceLabel.Dock = DockStyle.Fill;
			footerPresenceLabel.TextAlign = ContentAlignment.MiddleCenter;
			footerPresenceLabel.ForeColor = Color.WhiteSmoke;
			footerPresenceLabel.Font = shellFont;
			footerPresenceLabel.Text = "PRESENCE · OFFLINE";
			footerThermalLabel.Dock = DockStyle.Fill;
			footerThermalLabel.TextAlign = ContentAlignment.MiddleRight;
			footerThermalLabel.ForeColor = Color.WhiteSmoke;
			footerThermalLabel.Font = shellFont;
			footerThermalLabel.Text = "CPU --°C · GPU --°C · RAM --°C · BOARD --°C";
			tableLayoutPanel3.Controls.Add(footerVersionLabel, 0, 0);
			tableLayoutPanel3.Controls.Add(footerPresenceLabel, 1, 0);
			tableLayoutPanel3.Controls.Add(footerThermalLabel, 2, 0);
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
		tableLayoutPanel.RowCount = 5;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 128f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 6f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 114f));
		connectionStatusLabel.Dock = DockStyle.Fill;
		connectionStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		connectionStatusLabel.ForeColor = AccentGreen;
		connectionStatusLabel.Font = headerFont;
		SetConnectionStatusText("● DISCONNECTED");
		Panel panelConsole = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panelConsole.Dock = DockStyle.Fill;
		TableLayoutPanel tableLayoutPanelConsole = new TableLayoutPanel();
		tableLayoutPanelConsole.Dock = DockStyle.Fill;
		tableLayoutPanelConsole.RowCount = 2;
		tableLayoutPanelConsole.ColumnCount = 1;
		tableLayoutPanelConsole.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanelConsole.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		leftConsoleHeaderLabel.Dock = DockStyle.Fill;
		leftConsoleHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
		leftConsoleHeaderLabel.ForeColor = AccentGreen;
		leftConsoleHeaderLabel.Font = shellFontBold;
		leftConsoleHeaderLabel.Text = "CONSOLE";
		leftConsoleInfoPanel.Dock = DockStyle.Fill;
		leftConsoleInfoPanel.BackColor = TerminalBackground;
		leftConsoleInfoPanel.ForeColor = Color.WhiteSmoke;
		leftConsoleInfoPanel.Font = shellFont;
		SetLeftConsoleText("BOARD      unknown\nDASH       --\nGAME       --\nXEX        --\nTITLEID    --\nGAMERTAG   Not Signed In");
		tableLayoutPanelConsole.Controls.Add(leftConsoleHeaderLabel, 0, 0);
		tableLayoutPanelConsole.Controls.Add(leftConsoleInfoPanel, 0, 1);
		panelConsole.Controls.Add(tableLayoutPanelConsole);
		inventoryList.Dock = DockStyle.Fill;
		inventoryList.BackColor = TerminalBackground;
		inventoryList.ForeColor = Color.WhiteSmoke;
		inventoryList.Font = shellFont;
		inventoryList.SetItems(new string[1]
		{
			"Connect for Plugins"
		});
		leftSignInLabel.Dock = DockStyle.Fill;
		leftSignInLabel.TextAlign = ContentAlignment.MiddleLeft;
		leftSignInLabel.ForeColor = AccentDim;
		leftSignInLabel.Font = shellFont;
		leftSignInLabel.AutoEllipsis = true;
		leftSignInLabel.Text = string.Empty;
		leftSignInLabel.Visible = false;
		ConfigureToggleButton(pluginsTabButton, "Plugins");
		ConfigureToggleButton(modulesTabButton, "Modules");
		pluginsTabButton.Margin = new Padding(0, 0, 6, 0);
		modulesTabButton.Margin = Padding.Empty;
		TableLayoutPanel panelInventoryHeader = new TableLayoutPanel();
		panelInventoryHeader.Dock = DockStyle.Fill;
		panelInventoryHeader.ColumnCount = 2;
		panelInventoryHeader.RowCount = 1;
		panelInventoryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		panelInventoryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		panelInventoryHeader.Controls.Add(pluginsTabButton, 0, 0);
		panelInventoryHeader.Controls.Add(modulesTabButton, 1, 0);
		Panel inventoryPanel = CreateCardPanel(TerminalBackground, new Padding(6, 6, 6, 6));
		inventoryPanel.Dock = DockStyle.Fill;
		inventoryPanel.Margin = new Padding(0);
		TableLayoutPanel inventoryLayout = new TableLayoutPanel();
		inventoryLayout.Dock = DockStyle.Fill;
		inventoryLayout.RowCount = 2;
		inventoryLayout.ColumnCount = 1;
		inventoryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		inventoryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		inventoryLayout.Controls.Add(panelInventoryHeader, 0, 0);
		inventoryLayout.Controls.Add(inventoryList, 0, 1);
		inventoryPanel.Controls.Add(inventoryLayout);
		Panel panel2 = CreateCardPanel(TerminalBackground, new Padding(6, 6, 6, 6));
		panel2.Dock = DockStyle.Fill;
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.RowCount = 3;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		ConfigureActionButton(connectButton, "CONNECT");
		ConfigureActionButton(disconnectButton, "DISCONNECT");
		ConfigureActionButton(screenshotButton, "SCREENSHOT");
		ConfigureActionButton(languageButton, "LANG: EN");
		ConfigureInputTextBox(targetIpTextBox, string.Empty, horizontalAlignment: HorizontalAlignment.Center);
		ConfigureTargetIpHostPanel(targetIpHostPanel, targetIpTextBox, "HOST / IP");
		tableLayoutPanel2.Controls.Add(connectButton, 0, 0);
		tableLayoutPanel2.Controls.Add(disconnectButton, 1, 0);
		tableLayoutPanel2.Controls.Add(screenshotButton, 0, 1);
		tableLayoutPanel2.Controls.Add(languageButton, 1, 1);
		tableLayoutPanel2.Controls.Add(targetIpHostPanel, 0, 2);
		tableLayoutPanel2.SetColumnSpan(targetIpHostPanel, 2);
		panel2.Controls.Add(tableLayoutPanel2);
		tableLayoutPanel.Controls.Add(connectionStatusLabel, 0, 0);
		tableLayoutPanel.Controls.Add(panelConsole, 0, 1);
		tableLayoutPanel.Controls.Add(inventoryPanel, 0, 2);
		tableLayoutPanel.Controls.Add(new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent,
			Margin = Padding.Empty
		}, 0, 3);
		tableLayoutPanel.Controls.Add(panel2, 0, 4);
		RefreshTargetEditorText();
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
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f));
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
			TableLayoutPanel inventoryStack = new TableLayoutPanel();
			LogBootstrap($"center-after-side-shell bounds={inventoryStack.Bounds}");
			inventoryStack.Dock = DockStyle.Fill;
			inventoryStack.BackColor = CardBackground;
			inventoryStack.ColumnCount = 1;
			inventoryStack.RowCount = 1;
			inventoryStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			Label label = new Label();
			shellTargetLabel.Dock = DockStyle.Fill;
			shellTargetLabel.TextAlign = ContentAlignment.MiddleLeft;
			shellTargetLabel.ForeColor = Color.FromArgb(214, AccentDim);
			shellTargetLabel.Font = shellFont;
			shellTargetLabel.Text = "LIVE SHELL // TARGET " + FormatCurrentTarget();
			shellTargetLabel.AutoEllipsis = true;
			shellBadgeLabel.Dock = DockStyle.Fill;
			shellBadgeLabel.TextAlign = ContentAlignment.MiddleLeft;
			shellBadgeLabel.ForeColor = Color.FromArgb(230, AccentGreen);
			shellBadgeLabel.Font = new Font("Consolas", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
			shellBadgeLabel.Text = "XECLI // ACTIVE SHELL";
			shellBadgeLabel.AutoEllipsis = true;
			inventoryStack.Controls.Add(new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = CardBackground,
				Margin = Padding.Empty
			}, 0, 0);
			tableLayoutPanel2.Controls.Add(logoPictureBox, 0, 0);
			tableLayoutPanel2.Controls.Add(inventoryStack, 1, 0);
			panel2.Controls.Add(tableLayoutPanel2);
			LogBootstrap("center-after-hero-layout");
			Panel panel5 = CreateCardPanel(TerminalBackground, new Padding(0));
			LogBootstrap($"center-after-shell-card bounds={panel5.Bounds}");
			panel5.Dock = DockStyle.Fill;
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
			tableLayoutPanel3.Dock = DockStyle.Top;
			tableLayoutPanel3.Height = 32;
			tableLayoutPanel3.BackColor = TerminalBackground;
			tableLayoutPanel3.ColumnCount = 2;
			tableLayoutPanel3.RowCount = 1;
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164f));
			mainShellBannerLabel.Dock = DockStyle.Fill;
			mainShellBannerLabel.Margin = new Padding(0, 3, 4, 3);
			mainShellBannerLabel.TextAlign = ContentAlignment.MiddleCenter;
			mainShellBannerLabel.Font = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
			mainShellBannerLabel.ForeColor = Color.WhiteSmoke;
			mainShellBannerLabel.BackColor = Color.FromArgb(12, 22, 18);
			SetMainShellBannerText("LINK OFFLINE");
			mainShellBannerLabel.AutoEllipsis = true;
			tableLayoutPanel3.Controls.Add(mainShellBannerLabel, 1, 0);
			TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel();
			tableLayoutPanel5.Dock = DockStyle.Fill;
			tableLayoutPanel5.BackColor = TerminalBackground;
			tableLayoutPanel5.ColumnCount = 1;
			tableLayoutPanel5.RowCount = 2;
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel5.Controls.Add(tableLayoutPanel3, 0, 0);
			Panel panel6 = CreateScaffoldSurfacePanel(Color.FromArgb(9, 16, 14), new Padding(10, 10, 10, 10), 28, 28);
			LogBootstrap($"center-after-terminal-card bounds={panel6.Bounds}");
			panel6.Dock = DockStyle.Fill;
			panel6.Margin = new Padding(0);
			TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
			tableLayoutPanel4.Dock = DockStyle.Fill;
			tableLayoutPanel4.BackColor = TerminalBackground;
			tableLayoutPanel4.ColumnCount = 1;
			tableLayoutPanel4.RowCount = 4;
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 4f));
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
			suggestionRowStyle = tableLayoutPanel4.RowStyles[2];
			shellScaffoldLabel.Dock = DockStyle.Fill;
			shellScaffoldLabel.Margin = new Padding(0, 4, 0, 8);
			shellScaffoldLabel.Padding = new Padding(0, 2, 0, 0);
			shellScaffoldLabel.TextAlign = ContentAlignment.TopLeft;
			shellScaffoldLabel.Font = shellOutputFont;
			shellScaffoldLabel.ForeColor = Color.WhiteSmoke;
			shellScaffoldLabel.Text = BuildDisconnectedShellText();
			shellScaffoldLabel.Visible = false;
			Panel panel7 = new Panel();
			panel7.Dock = DockStyle.Fill;
			panel7.Margin = new Padding(0);
			panel7.Padding = new Padding(0, 4, 0, 0);
			panel7.BackColor = TerminalBackground;
			if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanelDiagnosticsEnvVar)))
			{
				terminalOutput.Dock = DockStyle.Fill;
				terminalOutput.BackColor = Color.FromArgb(9, 16, 14);
				terminalOutput.ForeColor = Color.WhiteSmoke;
				terminalOutput.BorderStyle = BorderStyle.None;
				terminalOutput.Font = shellOutputFont;
				terminalOutput.ReadOnly = true;
				terminalOutput.DetectUrls = false;
				terminalOutput.HideSelection = false;
				terminalOutput.ScrollBars = RichTextBoxScrollBars.None;
				terminalOutput.WordWrap = true;
				terminalOutput.TabStop = true;
				terminalOutput.Cursor = Cursors.IBeam;
				terminalOutput.Enter += delegate
				{
					HideTerminalOutputCaret();
				};
				terminalOutput.MouseDown += delegate
				{
					HideTerminalOutputCaret();
				};
				terminalOutput.MouseUp += delegate
				{
					HideTerminalOutputCaret();
				};
				terminalOutput.SelectionChanged += delegate
				{
					HideTerminalOutputCaret();
				};
				panel7.Controls.Add(terminalOutput);
				terminalScrollIndicator.Dock = DockStyle.Right;
				terminalScrollIndicator.Width = 8;
				terminalScrollIndicator.Margin = Padding.Empty;
				terminalScrollIndicator.BackColor = Color.FromArgb(9, 16, 14);
				terminalScrollIndicator.Attach(terminalOutput);
				panel7.Controls.Add(terminalScrollIndicator);
			}
			else
			{
				TextBox textBox = new TextBox();
				textBox.Dock = DockStyle.Fill;
				textBox.BackColor = Color.FromArgb(9, 16, 14);
				textBox.ForeColor = Color.WhiteSmoke;
				textBox.BorderStyle = BorderStyle.None;
				textBox.Font = shellOutputFont;
				textBox.ReadOnly = true;
				textBox.Multiline = true;
				textBox.ScrollBars = ScrollBars.Vertical;
				textBox.WordWrap = true;
				textBox.Text = "DIAGNOSTIC TERMINAL SURFACE\r\nRichTextBox temporarily bypassed.";
				panel7.Controls.Add(textBox);
			}
			tableLayoutPanel4.Controls.Add(panel7, 0, 1);
			suggestionHost.Dock = DockStyle.Fill;
			suggestionHost.Margin = Padding.Empty;
			suggestionHost.BackColor = Color.FromArgb(8, 14, 12);
			suggestionHost.Padding = new Padding(1);
			suggestionHost.Paint += delegate(object? sender, PaintEventArgs e)
			{
				if (sender is Control control)
				{
					using SolidBrush brush = new SolidBrush(Color.FromArgb(8, 14, 12));
					e.Graphics.FillRectangle(brush, control.ClientRectangle);
				}
			};
			suggestionList.Dock = DockStyle.Top;
			suggestionList.BackColor = Color.FromArgb(8, 14, 12);
			suggestionList.ForeColor = Color.WhiteSmoke;
			suggestionList.Font = shellFont;
			suggestionList.Visible = false;
			suggestionList.ItemActivated += delegate
			{
				ApplySelectedSuggestion();
			};
			suggestionList.Leave += delegate
			{
				HideSuggestionsIfInputInactive();
			};
			suggestionHost.Controls.Add(suggestionList);
			tableLayoutPanel4.Controls.Add(suggestionHost, 0, 2);
			commandInputHost.Dock = DockStyle.Fill;
			commandInputHost.Margin = new Padding(0, 4, 0, 0);
			commandInputHost.Padding = new Padding(6, 4, 6, 4);
			commandInputHost.BackColor = Color.FromArgb(8, 14, 11);
			commandInputHost.Paint += delegate(object? _, PaintEventArgs e)
			{
				Rectangle clientRectangle = commandInputHost.ClientRectangle;
				clientRectangle.Width = Math.Max(0, clientRectangle.Width - 1);
				clientRectangle.Height = Math.Max(0, clientRectangle.Height - 1);
				using SolidBrush brush = new SolidBrush(Color.FromArgb(8, 14, 11));
				using Pen pen = new Pen(Color.FromArgb(108, AccentGreen), 1f);
				e.Graphics.FillRectangle(brush, clientRectangle);
				e.Graphics.DrawRectangle(pen, clientRectangle);
			};
			commandInput.Dock = DockStyle.Fill;
			commandInput.Margin = Padding.Empty;
			commandInput.BackColor = Color.FromArgb(8, 14, 11);
			commandInput.ForeColor = AccentGreen;
			commandInput.BorderStyle = BorderStyle.None;
			commandInput.Font = shellFontBold;
			SetCommandInputPlaceholder("Connect or enter a XeCLI command...");
			commandInput.KeyDown += HandleCommandInputKeyDown;
			commandInput.Enter += delegate
			{
				if (!suppressSuggestionRefresh)
				{
					RefreshSuggestions();
				}
			};
			commandInput.Leave += delegate
			{
				HideSuggestionsIfInputInactive();
			};
			commandInput.TextChanged += delegate
			{
				if (!suppressSuggestionRefresh)
				{
					RefreshSuggestions();
				}
			};
			commandInputHost.Controls.Add(commandInput);
			tableLayoutPanel4.Controls.Add(commandInputHost, 0, 3);
			panel6.Controls.Add(tableLayoutPanel4);
			LogBootstrap("center-after-terminal-children");
			tableLayoutPanel5.Controls.Add(panel6, 0, 1);
			panel5.Controls.Add(tableLayoutPanel5);
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
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 118f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Panel panel2 = CreateCardPanel(TerminalBackground, new Padding(8, 6, 8, 6));
		panel2.Margin = new Padding(0, 0, 0, 6);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.RowCount = 2;
		tableLayoutPanel2.ColumnCount = 1;
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		rightStatusHeaderLabel.Dock = DockStyle.Fill;
		rightStatusHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
		rightStatusHeaderLabel.ForeColor = AccentGreen;
		rightStatusHeaderLabel.Font = shellFontBold;
		rightStatusHeaderLabel.Text = "STATUS";
		rightNetworkInfoPanel.Dock = DockStyle.Fill;
		rightNetworkInfoPanel.BackColor = TerminalBackground;
		rightNetworkInfoPanel.ForeColor = Color.WhiteSmoke;
		rightNetworkInfoPanel.Font = shellFont;
		SetRightNetworkText(BuildDisconnectedStatusText());
		tableLayoutPanel2.Controls.Add(rightStatusHeaderLabel, 0, 0);
		tableLayoutPanel2.Controls.Add(rightNetworkInfoPanel, 0, 1);
		panel2.Controls.Add(tableLayoutPanel2);
		Panel panel3 = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panel3.Margin = new Padding(0, 0, 0, 6);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.RowCount = 3;
		tableLayoutPanel3.ColumnCount = 1;
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		rightTrafficHeaderLabel.Dock = DockStyle.Fill;
		rightTrafficHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
		rightTrafficHeaderLabel.ForeColor = AccentGreen;
		rightTrafficHeaderLabel.Font = shellFontBold;
		rightTrafficHeaderLabel.Text = "TRAFFIC";
		ftpTrafficGraph.Dock = DockStyle.Fill;
		rightTempLabel.Dock = DockStyle.Fill;
		rightTempLabel.TextAlign = ContentAlignment.TopLeft;
		rightTempLabel.ForeColor = Color.WhiteSmoke;
		rightTempLabel.Font = shellFont;
		rightTempLabel.Padding = new Padding(2, 1, 2, 0);
		SetRightTempText("IDLE");
		tableLayoutPanel3.Controls.Add(rightTrafficHeaderLabel, 0, 0);
		tableLayoutPanel3.Controls.Add(ftpTrafficGraph, 0, 1);
		tableLayoutPanel3.Controls.Add(rightTempLabel, 0, 2);
		panel3.Controls.Add(tableLayoutPanel3);
		Panel panel4 = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
		tableLayoutPanel4.Dock = DockStyle.Fill;
		tableLayoutPanel4.RowCount = 2;
		tableLayoutPanel4.ColumnCount = 1;
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		rightDrivesLabel.Dock = DockStyle.Fill;
		rightDrivesLabel.TextAlign = ContentAlignment.MiddleLeft;
		rightDrivesLabel.ForeColor = AccentGreen;
		rightDrivesLabel.Font = new Font("Consolas", 9f, FontStyle.Bold, GraphicsUnit.Point);
		SetRightDrivesTitleText("DETECTED DRIVES");
		drivesList.Dock = DockStyle.Fill;
		drivesList.BackColor = TerminalBackground;
		drivesList.ForeColor = Color.WhiteSmoke;
		drivesList.Font = shellFont;
		drivesList.SetEntries(new DriveInventoryEntry[1]
		{
			new DriveInventoryEntry
			{
				Name = "No live drives detected"
			}
		});
		tableLayoutPanel4.Controls.Add(rightDrivesLabel, 0, 0);
		tableLayoutPanel4.Controls.Add(drivesList, 0, 1);
		panel4.Controls.Add(tableLayoutPanel4);
		tableLayoutPanel.Controls.Add(panel2, 0, 0);
		tableLayoutPanel.Controls.Add(panel3, 0, 1);
		tableLayoutPanel.Controls.Add(panel4, 0, 2);
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
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
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
		tableLayoutPanel.ColumnCount = 5;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
		localPathLabel.Dock = DockStyle.Fill;
		localPathLabel.ForeColor = AccentDim;
		localPathLabel.Font = shellFont;
		localPathLabel.TextAlign = ContentAlignment.MiddleLeft;
		localPathLabel.AutoEllipsis = true;
		localPathLabel.Text = string.Empty;
		ConfigureInputTextBox(localPathTextBox, string.Empty);
		ConfigureInputHostPanel(localPathHostPanel, localPathTextBox, "LOCAL PATH");
		ConfigureFilePaneButton(localUpButton, "UP");
		ConfigureFilePaneButton(localRefreshButton, "Refresh");
		localFileList.Dock = DockStyle.Fill;
		localFileList.BackColor = TerminalBackground;
		localFileList.ForeColor = Color.WhiteSmoke;
		localFileList.Font = shellFont;
		localFileList.HeaderText = "NAME";
		tableLayoutPanel.Controls.Add(localUpButton, 3, 1);
		tableLayoutPanel.Controls.Add(localRefreshButton, 4, 1);
		tableLayoutPanel.Controls.Add(localPathHostPanel, 0, 2);
		tableLayoutPanel.SetColumnSpan(localPathHostPanel, 5);
		tableLayoutPanel.Controls.Add(localFileList, 0, 3);
		tableLayoutPanel.SetColumnSpan(localFileList, 5);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildRemoteFilePane()
	{
		Panel panel = CreateCardPanel(TerminalBackground, new Padding(6, 4, 6, 6));
		panel.Margin = new Padding(5, 0, 0, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.ColumnCount = 5;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
		remotePathLabel.Dock = DockStyle.Fill;
		remotePathLabel.ForeColor = AccentDim;
		remotePathLabel.Font = shellFont;
		remotePathLabel.TextAlign = ContentAlignment.MiddleLeft;
		remotePathLabel.AutoEllipsis = true;
		remotePathLabel.Text = string.Empty;
		ConfigureFilePaneButton(remoteUpButton, "UP");
		ConfigureFilePaneButton(remoteRefreshButton, "Refresh");
		ConfigureToggleButton(ftpTabButton, "FTP");
		ConfigureToggleButton(queueTabButton, "QUEUE");
		ftpTabButton.Margin = new Padding(0, 0, 6, 0);
		queueTabButton.Margin = new Padding(0, 0, 10, 0);
		ConfigureInputTextBox(remotePathTextBox, string.Empty);
		ConfigureInputHostPanel(remotePathHostPanel, remotePathTextBox, "REMOTE PATH");
		remoteContentHost.Dock = DockStyle.Fill;
		remoteContentHost.BackColor = TerminalBackground;
		remoteContentHost.Margin = Padding.Empty;
		remoteFileList.Dock = DockStyle.Fill;
		remoteFileList.BackColor = TerminalBackground;
		remoteFileList.ForeColor = Color.WhiteSmoke;
		remoteFileList.Font = shellFont;
		remoteFileList.HeaderText = "NAME";
		transferQueueList.Dock = DockStyle.Fill;
		transferQueueList.BackColor = TerminalBackground;
		transferQueueList.ForeColor = Color.WhiteSmoke;
		transferQueueList.Font = shellFont;
		transferQueueList.SetEntries(Array.Empty<TransferQueueEntry>());
		remoteContentHost.Controls.Add(remoteFileList);
		remoteContentHost.Controls.Add(transferQueueList);
		tableLayoutPanel.Controls.Add(ftpTabButton, 0, 1);
		tableLayoutPanel.Controls.Add(queueTabButton, 1, 1);
		tableLayoutPanel.Controls.Add(remoteUpButton, 3, 1);
		tableLayoutPanel.Controls.Add(remoteRefreshButton, 4, 1);
		tableLayoutPanel.Controls.Add(remotePathHostPanel, 0, 2);
		tableLayoutPanel.SetColumnSpan(remotePathHostPanel, 5);
		tableLayoutPanel.Controls.Add(remoteContentHost, 0, 3);
		tableLayoutPanel.SetColumnSpan(remoteContentHost, 5);
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
		InitializeFilePaneMenus();
		localUpButton.Click += delegate
		{
			NavigateLocalUp();
		};
		localRefreshButton.Click += async delegate
		{
			await RefreshLocalBrowserAsync();
		};
		localPathTextBox.KeyDown += async delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				await ApplyLocalPathAsync();
			}
		};
		remoteUpButton.Click += async delegate
		{
			if (remotePaneShowsQueue)
			{
				return;
			}
			NavigateRemoteUp();
			await RefreshRemoteBrowserAsync(force: true);
		};
		remoteRefreshButton.Click += async delegate
		{
			if (remotePaneShowsQueue)
			{
				RefreshTransferQueueDisplay();
			}
			else
			{
				await RefreshRemoteBrowserAsync(force: true);
			}
		};
		ftpTabButton.Click += delegate
		{
			SetRemotePaneMode(showQueue: false);
		};
		queueTabButton.Click += delegate
		{
			SetRemotePaneMode(showQueue: true);
		};
		remotePathTextBox.KeyDown += async delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				await ApplyRemotePathAsync();
			}
		};
		localFileList.ItemActivated += HandleLocalItemActivated;
		remoteFileList.ItemActivated += async delegate(int index)
		{
			await HandleRemoteItemActivatedAsync(index);
		};
		localFileList.ItemContextRequested += delegate(int index, Point screenPoint)
		{
			localContextIndex = index;
			ShowContextMenu(localFileMenu, screenPoint);
		};
		localFileList.EmptyAreaContextRequested += delegate(Point screenPoint)
		{
			localContextIndex = -1;
			ShowContextMenu(localEmptyMenu, screenPoint);
		};
		remoteFileList.ItemContextRequested += delegate(int index, Point screenPoint)
		{
			remoteContextIndex = index;
			ShowContextMenu(remoteFileMenu, screenPoint);
		};
		remoteFileList.EmptyAreaContextRequested += delegate(Point screenPoint)
		{
			remoteContextIndex = -1;
			ShowContextMenu(remoteEmptyMenu, screenPoint);
		};
		localFileList.ItemDragRequested += BeginLocalPaneDrag;
		remoteFileList.ItemDragRequested += BeginRemotePaneDrag;
		localFileList.AllowDrop = true;
		remoteFileList.AllowDrop = true;
		localFileList.DragEnter += HandleLocalPaneDragEnter;
		localFileList.DragDrop += HandleLocalPaneDragDrop;
		remoteFileList.DragEnter += HandleRemotePaneDragEnter;
		remoteFileList.DragDrop += HandleRemotePaneDragDrop;
		SetRemotePaneMode(showQueue: false);
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

	private void InitializeFilePaneMenus()
	{
		if (remoteFileMenu.Items.Count != 0 || remoteEmptyMenu.Items.Count != 0 || localFileMenu.Items.Count != 0 || localEmptyMenu.Items.Count != 0)
		{
			return;
		}
		ConfigurePaneMenu(localFileMenu);
		ConfigurePaneMenu(localEmptyMenu);
		ConfigurePaneMenu(remoteFileMenu);
		ConfigurePaneMenu(remoteEmptyMenu);
	}

	private void ConfigurePaneMenu(ContextMenuStrip menu)
	{
		menu.ShowImageMargin = false;
		menu.BackColor = CardBackground;
		menu.ForeColor = Color.WhiteSmoke;
		menu.Font = shellFont;
		menu.RenderMode = ToolStripRenderMode.System;
	}

	private void ShowContextMenu(ContextMenuStrip menu, Point screenPoint)
	{
		PopulateContextMenu(menu);
		menu.Show(screenPoint);
	}

	private void PopulateContextMenu(ContextMenuStrip menu)
	{
		menu.SuspendLayout();
		try
		{
			menu.Items.Clear();
			if (ReferenceEquals(menu, remoteFileMenu))
			{
				BuildRemoteItemMenu(menu);
			}
			else if (ReferenceEquals(menu, remoteEmptyMenu))
			{
				BuildRemoteEmptyMenu(menu);
			}
			else if (ReferenceEquals(menu, localFileMenu))
			{
				BuildLocalItemMenu(menu);
			}
			else if (ReferenceEquals(menu, localEmptyMenu))
			{
				BuildLocalEmptyMenu(menu);
			}
		}
		finally
		{
			menu.ResumeLayout();
		}
	}

	private void BuildRemoteItemMenu(ContextMenuStrip menu)
	{
		FileEntryView? remoteEntryAtContext = GetRemoteEntryAtContext();
		bool flag = remoteEntryAtContext != null && remoteEntryAtContext.Name != "..";
		bool flag2 = flag && remoteEntryAtContext!.IsDirectory;
		bool enabled = flag && !fileTransferInFlight && !commandInFlight && !connectAttemptInFlight && !shellDisconnected;
		bool enabled2 = paneClipboardEntry != null && !fileTransferInFlight && !commandInFlight && !connectAttemptInFlight && !shellDisconnected;
		AddMenuItem(menu, "Download To PC", enabled, delegate
		{
			_ = DownloadRemoteEntriesToLocalAsync(new FileEntryView[1] { remoteEntryAtContext! });
		});
		AddMenuItem(menu, "Rename", enabled, delegate
		{
			_ = RenameRemoteEntryAsync(remoteEntryAtContext!);
		});
		AddMenuItem(menu, "Delete", enabled, delegate
		{
			_ = DeleteRemoteEntryAsync(remoteEntryAtContext!);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Cut", enabled, delegate
		{
			SetPaneClipboard(remoteEntryAtContext!, isRemote: true, move: true);
		});
		AddMenuItem(menu, "Copy", enabled, delegate
		{
			SetPaneClipboard(remoteEntryAtContext!, isRemote: true, move: false);
		});
		AddMenuItem(menu, flag2 ? "Paste Into Folder" : "Paste Here", enabled2, delegate
		{
			string destinationRemoteDirectory = flag2 ? remoteEntryAtContext!.FullPath : remoteCurrentPath;
			_ = PasteClipboardToRemoteAsync(destinationRemoteDirectory);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh", !connectAttemptInFlight, delegate
		{
			_ = RefreshRemoteBrowserAsync(force: true);
		});
	}

	private void BuildRemoteEmptyMenu(ContextMenuStrip menu)
	{
		bool enabled = !fileTransferInFlight && !commandInFlight && !connectAttemptInFlight && !shellDisconnected;
		bool enabled2 = paneClipboardEntry != null && enabled;
		AddMenuItem(menu, "Upload Files...", enabled, delegate
		{
			_ = UploadFilesToRemoteAsync();
		});
		AddMenuItem(menu, "Upload Folder...", enabled, delegate
		{
			_ = UploadFolderToRemoteAsync();
		});
		AddMenuItem(menu, "New Folder...", enabled, delegate
		{
			_ = CreateRemoteFolderAsync();
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Paste", enabled2, delegate
		{
			_ = PasteClipboardToRemoteAsync(remoteCurrentPath);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh", !connectAttemptInFlight, delegate
		{
			_ = RefreshRemoteBrowserAsync(force: true);
		});
	}

	private void BuildLocalItemMenu(ContextMenuStrip menu)
	{
		FileEntryView? localEntryAtContext = GetLocalEntryAtContext();
		bool flag = localEntryAtContext != null && localEntryAtContext.Name != "..";
		bool enabled = flag && !fileTransferInFlight && !commandInFlight && !connectAttemptInFlight;
		bool enabled2 = flag && !shellDisconnected && enabled;
		bool enabled3 = paneClipboardEntry != null && enabled;
		bool flag2 = localEntryAtContext?.IsDirectory == true;
		AddMenuItem(menu, flag2 ? "Open Folder" : "Open", enabled, delegate
		{
			HandleLocalItemActivated(localContextIndex);
		});
		AddMenuItem(menu, "Reveal In Explorer", enabled, delegate
		{
			OpenPath(localEntryAtContext!.IsDirectory ? localEntryAtContext.FullPath : Path.GetDirectoryName(localEntryAtContext.FullPath) ?? localEntryAtContext.FullPath, isFile: false);
		});
		AddMenuItem(menu, "Upload To Console", enabled2, delegate
		{
			_ = UploadLocalEntriesToRemoteAsync(new string[1] { localEntryAtContext!.FullPath }, remoteCurrentPath);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Rename", enabled, delegate
		{
			_ = RenameLocalEntryAsync(localEntryAtContext!);
		});
		AddMenuItem(menu, "Delete", enabled, delegate
		{
			_ = DeleteLocalEntryAsync(localEntryAtContext!);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Cut", enabled, delegate
		{
			SetPaneClipboard(localEntryAtContext!, isRemote: false, move: true);
		});
		AddMenuItem(menu, "Copy", enabled, delegate
		{
			SetPaneClipboard(localEntryAtContext!, isRemote: false, move: false);
		});
		AddMenuItem(menu, "Paste Here", enabled3 && flag2, delegate
		{
			_ = PasteClipboardToLocalAsync(localEntryAtContext!.FullPath);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh", !fileTransferInFlight, delegate
		{
			_ = RefreshLocalBrowserAsync();
		});
	}

	private void BuildLocalEmptyMenu(ContextMenuStrip menu)
	{
		bool enabled = paneClipboardEntry != null && !fileTransferInFlight && !commandInFlight && !connectAttemptInFlight;
		AddMenuItem(menu, "Paste Here", enabled, delegate
		{
			_ = PasteClipboardToLocalAsync();
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "New Folder...", !fileTransferInFlight && !commandInFlight, delegate
		{
			_ = CreateLocalFolderAsync();
		});
		AddMenuItem(menu, "Open In Explorer", !fileTransferInFlight, delegate
		{
			OpenPath(localCurrentPath ?? GetLocalTransferTargetDirectory(), isFile: false);
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh", !fileTransferInFlight, delegate
		{
			_ = RefreshLocalBrowserAsync();
		});
	}

	private void AddMenuSeparator(ContextMenuStrip menu)
	{
		if (menu.Items.Count > 0)
		{
			menu.Items.Add(new ToolStripSeparator());
		}
	}

	private void AddMenuItem(ContextMenuStrip menu, string text, bool enabled, Action action)
	{
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(text)
		{
			Enabled = enabled
		};
		toolStripMenuItem.Click += delegate
		{
			action();
		};
		menu.Items.Add(toolStripMenuItem);
	}

	private FileEntryView? GetRemoteEntryAtContext()
	{
		if (remoteContextIndex < 0 || remoteContextIndex >= remoteEntries.Count)
		{
			return null;
		}
		return remoteEntries[remoteContextIndex];
	}

	private FileEntryView? GetLocalEntryAtContext()
	{
		if (localContextIndex < 0 || localContextIndex >= localEntries.Count)
		{
			return null;
		}
		return localEntries[localContextIndex];
	}

	private void BeginLocalPaneDrag(int index)
	{
		if (index < 0 || index >= localEntries.Count)
		{
			return;
		}
		FileEntryView fileEntryView = localEntries[index];
		if (fileEntryView.Name == "..")
		{
			return;
		}
		DataObject dataObject = new DataObject();
		dataObject.SetData(typeof(PaneDragPayload), new PaneDragPayload
		{
			FromRemote = false,
			Entries = new List<FileEntryView> { fileEntryView }
		});
		localFileList.DoDragDrop(dataObject, DragDropEffects.Copy);
	}

	private void BeginRemotePaneDrag(int index)
	{
		if (index < 0 || index >= remoteEntries.Count)
		{
			return;
		}
		FileEntryView fileEntryView = remoteEntries[index];
		if (fileEntryView.Name == "..")
		{
			return;
		}
		DataObject dataObject = new DataObject();
		dataObject.SetData(typeof(PaneDragPayload), new PaneDragPayload
		{
			FromRemote = true,
			Entries = new List<FileEntryView> { fileEntryView }
		});
		remoteFileList.DoDragDrop(dataObject, DragDropEffects.Copy);
	}

	private void HandleRemotePaneDragEnter(object? sender, DragEventArgs e)
	{
		e.Effect = CanAcceptRemoteDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private async void HandleRemotePaneDragDrop(object? sender, DragEventArgs e)
	{
		if (!CanAcceptRemoteDrop(e.Data))
		{
			return;
		}
		if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
		{
			string[]? data = e.Data.GetData(DataFormats.FileDrop) as string[];
			if (data != null && data.Length != 0)
			{
				await UploadLocalEntriesToRemoteAsync(data, remoteCurrentPath);
			}
			return;
		}
		if (e.Data?.GetDataPresent(typeof(PaneDragPayload)) == true && e.Data.GetData(typeof(PaneDragPayload)) is PaneDragPayload paneDragPayload && !paneDragPayload.FromRemote)
		{
			await UploadLocalEntriesToRemoteAsync(paneDragPayload.Entries.Select((FileEntryView entry) => entry.FullPath), remoteCurrentPath);
		}
	}

	private void HandleLocalPaneDragEnter(object? sender, DragEventArgs e)
	{
		e.Effect = CanAcceptLocalDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private async void HandleLocalPaneDragDrop(object? sender, DragEventArgs e)
	{
		if (!CanAcceptLocalDrop(e.Data))
		{
			return;
		}
		if (e.Data?.GetDataPresent(typeof(PaneDragPayload)) == true && e.Data.GetData(typeof(PaneDragPayload)) is PaneDragPayload paneDragPayload && paneDragPayload.FromRemote)
		{
			await DownloadRemoteEntriesToLocalAsync(paneDragPayload.Entries);
		}
	}

	private bool CanAcceptRemoteDrop(IDataObject? data)
	{
		if (fileTransferInFlight || commandInFlight || connectAttemptInFlight || shellDisconnected)
		{
			return false;
		}
		if (data == null)
		{
			return false;
		}
		if (data.GetDataPresent(DataFormats.FileDrop))
		{
			return true;
		}
		if (data.GetDataPresent(typeof(PaneDragPayload)) && data.GetData(typeof(PaneDragPayload)) is PaneDragPayload paneDragPayload)
		{
			return !paneDragPayload.FromRemote && paneDragPayload.Entries.Count > 0;
		}
		return false;
	}

	private bool CanAcceptLocalDrop(IDataObject? data)
	{
		if (fileTransferInFlight || commandInFlight || connectAttemptInFlight)
		{
			return false;
		}
		if (data == null)
		{
			return false;
		}
		return data.GetDataPresent(typeof(PaneDragPayload)) && data.GetData(typeof(PaneDragPayload)) is PaneDragPayload paneDragPayload && paneDragPayload.FromRemote && paneDragPayload.Entries.Count > 0;
	}

	private void SetPaneClipboard(FileEntryView entry, bool isRemote, bool move)
	{
		paneClipboardEntry = new PaneClipboardEntry
		{
			IsRemote = isRemote,
			FullPath = entry.FullPath,
			IsDirectory = entry.IsDirectory,
			Move = move
		};
		AppendSystemLine((move ? "Cut" : "Copied") + " " + (entry.IsDirectory ? "folder" : "file") + ": " + entry.FullPath, AccentGreen);
	}

	private async Task UploadFilesToRemoteAsync()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Upload Files To Console",
			Multiselect = true,
			CheckFileExists = true,
			RestoreDirectory = true
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK && openFileDialog.FileNames.Length != 0)
		{
			await UploadLocalEntriesToRemoteAsync(openFileDialog.FileNames, remoteCurrentPath);
		}
	}

	private async Task UploadFolderToRemoteAsync()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Select a folder to upload to the console.",
			UseDescriptionForTitle = true,
			ShowNewFolderButton = false
		};
		if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
		{
			await UploadLocalEntriesToRemoteAsync(new string[1] { folderBrowserDialog.SelectedPath }, remoteCurrentPath);
		}
	}

	private async Task CreateRemoteFolderAsync()
	{
		string? text = ShowTextPrompt("New Remote Folder", "Folder name", string.Empty);
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string text2 = SanitizeRemoteLeaf(text);
		if (string.IsNullOrWhiteSpace(text2))
		{
			AppendSystemLine("Folder name is required.", WarningColor);
			return;
		}
		string text3 = CombineRemotePath(remoteCurrentPath, text2);
		await RunFileTransferAsync("CREATING FOLDER", "ftp mkdir --remote " + text3, requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
			await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
			await asyncFtpClient.Connect(cancellationToken);
			if (await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, text3))
			{
				throw new IOException("Remote folder already exists.");
			}
			await asyncFtpClient.CreateDirectory(text3);
			if (!await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, text3))
			{
				throw new IOException("Remote folder creation could not be verified.");
			}
		});
	}

	private async Task CreateLocalFolderAsync()
	{
		string text = localCurrentPath ?? GetLocalTransferTargetDirectory();
		if (string.IsNullOrWhiteSpace(text))
		{
			AppendSystemLine("Select a local folder before creating a directory.", WarningColor);
			return;
		}
		string? text2 = ShowTextPrompt("Create Local Folder", "Folder name", "New Folder");
		if (string.IsNullOrWhiteSpace(text2))
		{
			return;
		}
		string fileName = text2.Trim();
		await RunFileTransferAsync("CREATING LOCAL FOLDER", "local mkdir " + fileName, requiresRemote: false, refreshRemote: false, refreshLocal: true, async delegate(CancellationToken cancellationToken)
		{
			await Task.Run(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				string uniqueLocalPath = GetUniqueLocalPath(Path.Combine(text, fileName), isDirectory: true);
				Directory.CreateDirectory(uniqueLocalPath);
			}, cancellationToken);
		});
	}

	private async Task RenameRemoteEntryAsync(FileEntryView entry)
	{
		if (entry.Name == "..")
		{
			return;
		}
		string? text = ShowTextPrompt("Rename Remote Entry", "New name", entry.Name);
		string text2 = SanitizeRemoteLeaf(text);
		if (string.IsNullOrWhiteSpace(text2) || string.Equals(text2, entry.Name, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		await RunFileTransferAsync("RENAMING ENTRY", "ftp mv --from " + entry.FullPath + " --to " + text2, requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
			await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
			await asyncFtpClient.Connect(cancellationToken);
			string text3 = CombineRemotePath(GetRemoteParentPath(entry.FullPath), text2);
			if (entry.IsDirectory)
			{
				if (await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, text3))
				{
					throw new IOException("A remote folder with that name already exists.");
				}
				await asyncFtpClient.MoveDirectory(FtpHelpers.NormalizePath(entry.FullPath), text3);
				if (await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, entry.FullPath) || !await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, text3))
				{
					throw new IOException("Remote folder rename could not be verified.");
				}
			}
			else
			{
				if (await asyncFtpClient.FileExists(text3))
				{
					throw new IOException("A remote file with that name already exists.");
				}
				await FtpHelpers.MoveFileVerifiedAsync(asyncFtpClient, entry.FullPath, text3);
			}
		});
	}

	private async Task RenameLocalEntryAsync(FileEntryView entry)
	{
		if (entry.Name == "..")
		{
			return;
		}
		string? text = ShowTextPrompt("Rename Local Entry", "New name", entry.Name);
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string fileName = text.Trim();
		if (string.Equals(fileName, entry.Name, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		await RunFileTransferAsync("RENAMING LOCAL ENTRY", "local mv " + entry.FullPath, requiresRemote: false, refreshRemote: false, refreshLocal: true, async delegate(CancellationToken cancellationToken)
		{
			await Task.Run(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				string directoryName = Path.GetDirectoryName(entry.FullPath) ?? (localCurrentPath ?? GetLocalTransferTargetDirectory());
				string uniqueLocalPath = GetUniqueLocalPath(Path.Combine(directoryName, fileName), entry.IsDirectory);
				if (entry.IsDirectory)
				{
					Directory.Move(entry.FullPath, uniqueLocalPath);
				}
				else
				{
					File.Move(entry.FullPath, uniqueLocalPath);
				}
			}, cancellationToken);
		});
	}

	private async Task DeleteRemoteEntryAsync(FileEntryView entry)
	{
		if (entry.Name == "..")
		{
			return;
		}
		string text = entry.IsDirectory ? "Delete this remote folder?" : "Delete this remote file?";
		if (MessageBox.Show(this, text + "\n\n" + entry.FullPath, "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
		{
			return;
		}
		await RunFileTransferAsync("DELETING ENTRY", "ftp rm --remote " + entry.FullPath, requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			await DeleteRemotePathAsync(entry.FullPath, entry.IsDirectory, cancellationToken);
		});
	}

	private async Task DeleteLocalEntryAsync(FileEntryView entry)
	{
		if (entry.Name == "..")
		{
			return;
		}
		string text = entry.IsDirectory ? "Delete this local folder?" : "Delete this local file?";
		if (MessageBox.Show(this, text + "\n\n" + entry.FullPath, "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
		{
			return;
		}
		await RunFileTransferAsync("DELETING LOCAL ENTRY", "local rm " + entry.FullPath, requiresRemote: false, refreshRemote: false, refreshLocal: true, async delegate(CancellationToken cancellationToken)
		{
			await Task.Run(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				DeleteLocalPath(entry.FullPath, entry.IsDirectory);
			}, cancellationToken);
		});
	}

	private async Task DownloadRemoteEntriesToLocalAsync(IEnumerable<FileEntryView> entries)
	{
		FileEntryView[] array = entries.Where((FileEntryView entry) => entry.Name != "..").ToArray();
		if (array.Length == 0)
		{
			return;
		}
		string localTransferTargetDirectory = GetLocalTransferTargetDirectory();
		List<(string RemotePath, string LocalPath)> list = new List<(string RemotePath, string LocalPath)>();
		AppendSystemLine("Download from: " + string.Join(" | ", array.Select((FileEntryView entry) => entry.FullPath)), AccentDim);
		AppendSystemLine("Save to: " + localTransferTargetDirectory, AccentDim);
		bool flag = await RunFileTransferAsync("DOWNLOADING FILES", "ftp get --remote " + string.Join(";", array.Select((FileEntryView entry) => entry.FullPath)), requiresRemote: true, refreshRemote: false, refreshLocal: true, async delegate(CancellationToken cancellationToken)
		{
			list.AddRange(await DownloadRemoteEntriesToLocalDirectoryAsync(array, localTransferTargetDirectory, cancellationToken));
		});
		if (flag)
		{
			AppendTransferCompletionLines(list, localTransferTargetDirectory);
		}
	}

	private async Task UploadLocalEntriesToRemoteAsync(IEnumerable<string> localPaths, string destinationRemoteDirectory)
	{
		string[] array = localPaths.Where((string path) => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length == 0)
		{
			return;
		}
		await RunFileTransferAsync("UPLOADING FILES", "ftp put --local " + string.Join(";", array), requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			await UploadLocalPathsToRemoteDirectoryAsync(array, destinationRemoteDirectory, cancellationToken);
		});
	}

	private async Task PasteClipboardToRemoteAsync(string destinationRemoteDirectory)
	{
		PaneClipboardEntry paneClipboardEntry = this.paneClipboardEntry ?? throw new InvalidOperationException("Nothing is queued for paste.");
		string text = paneClipboardEntry.Move ? "MOVING ENTRY" : "COPYING ENTRY";
		string text2 = paneClipboardEntry.IsRemote ? ((paneClipboardEntry.Move ? "ftp mv --from " : "ftp cp --from ") + paneClipboardEntry.FullPath) : ("ftp put --local " + paneClipboardEntry.FullPath);
		await RunFileTransferAsync(text, text2, requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			if (paneClipboardEntry.IsRemote)
			{
				await PasteRemoteClipboardToRemoteAsync(paneClipboardEntry, destinationRemoteDirectory, cancellationToken);
			}
			if (paneClipboardEntry.Move)
			{
				this.paneClipboardEntry = null;
			}
		});
	}

	private async Task PasteClipboardToLocalAsync(string? destinationLocalDirectory = null)
	{
		PaneClipboardEntry paneClipboardEntry = this.paneClipboardEntry ?? throw new InvalidOperationException("Nothing is queued for paste.");
		string localTransferTargetDirectory = destinationLocalDirectory;
		if (string.IsNullOrWhiteSpace(localTransferTargetDirectory) || !Directory.Exists(localTransferTargetDirectory))
		{
			localTransferTargetDirectory = GetLocalTransferTargetDirectory();
		}
		if (!paneClipboardEntry.IsRemote)
		{
			await RunFileTransferAsync(paneClipboardEntry.Move ? "MOVING LOCAL ENTRY" : "COPYING LOCAL ENTRY", (paneClipboardEntry.Move ? "local mv " : "local cp ") + paneClipboardEntry.FullPath, requiresRemote: false, refreshRemote: false, refreshLocal: true, async delegate(CancellationToken cancellationToken)
			{
				await Task.Run(async delegate
				{
					cancellationToken.ThrowIfCancellationRequested();
					string uniqueLocalPath = GetUniqueLocalPath(Path.Combine(localTransferTargetDirectory, Path.GetFileName(paneClipboardEntry.FullPath)), paneClipboardEntry.IsDirectory);
					await CopyLocalEntryAsync(paneClipboardEntry.FullPath, uniqueLocalPath, paneClipboardEntry.IsDirectory, cancellationToken);
					if (paneClipboardEntry.Move)
					{
						DeleteLocalPath(paneClipboardEntry.FullPath, paneClipboardEntry.IsDirectory);
						this.paneClipboardEntry = null;
					}
				}, cancellationToken);
			});
			return;
		}
		List<(string RemotePath, string LocalPath)> list = new List<(string RemotePath, string LocalPath)>();
		string localTargetDirectory = localTransferTargetDirectory;
		AppendSystemLine("Download from: " + paneClipboardEntry.FullPath, AccentDim);
		AppendSystemLine("Save to: " + localTargetDirectory, AccentDim);
		bool flag = await RunFileTransferAsync(paneClipboardEntry.Move ? "MOVING TO PC" : "DOWNLOADING TO PC", "ftp get --remote " + paneClipboardEntry.FullPath, requiresRemote: true, refreshRemote: paneClipboardEntry.Move, refreshLocal: true, async delegate(CancellationToken cancellationToken)
		{
			FileEntryView fileEntryView = new FileEntryView
			{
				Name = GetRemoteLeafName(paneClipboardEntry.FullPath),
				FullPath = paneClipboardEntry.FullPath,
				IsDirectory = paneClipboardEntry.IsDirectory
			};
			list.AddRange(await DownloadRemoteEntriesToLocalDirectoryAsync(new FileEntryView[1] { fileEntryView }, localTargetDirectory, cancellationToken));
			if (paneClipboardEntry.Move)
			{
				await DeleteRemotePathAsync(paneClipboardEntry.FullPath, paneClipboardEntry.IsDirectory, cancellationToken);
				this.paneClipboardEntry = null;
			}
		});
		if (flag)
		{
			AppendTransferCompletionLines(list, localTargetDirectory);
		}
	}

	private async Task<bool> RunFileTransferAsync(string footerText, string activityCommand, bool requiresRemote, bool refreshRemote, bool refreshLocal, Func<CancellationToken, Task> action)
	{
		if (base.IsDisposed)
		{
			return false;
		}
		if (connectAttemptInFlight)
		{
			AppendSystemLine("Wait for the current console link attempt to finish.", Color.Gold);
			return false;
		}
		if (commandInFlight || fileTransferInFlight)
		{
			AppendSystemLine("A file action is already running. Wait for completion.", Color.Gold);
			return false;
		}
		if (requiresRemote && shellDisconnected)
		{
			AppendSystemLine("Connect to the console before using FTP actions.", WarningColor);
			return false;
		}
		bool result = false;
		fileTransferInFlight = true;
		activeTransferCommand = activityCommand;
		int num = Volatile.Read(ref sessionEpoch);
		string text = currentTargetIp;
		int num2 = currentTargetPort;
		UpdateLiveHints(latestSnapshot);
		RecordTransferActivity(activityCommand, "queued");
		RecordTransferActivity(activityCommand, "running");
		CancelAndDispose(ref fileTransferCts);
		fileTransferCts = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		try
		{
			await action(fileTransferCts.Token);
			RecordTransferActivity(activityCommand, "complete");
			AppendSystemLine(footerText + " complete.", AccentGreen);
			result = true;
		}
		catch (OperationCanceledException)
		{
			RecordTransferActivity(activityCommand, "cancelled");
			AppendSystemLine(footerText + " cancelled.", AccentPink);
		}
		catch (Exception ex)
		{
			RecordTransferActivity(activityCommand, "failed");
			AppendSystemLine(footerText + " failed: " + ex.Message, AccentPink);
		}
		finally
		{
			fileTransferInFlight = false;
			activeTransferCommand = null;
			CancelAndDispose(ref fileTransferCts);
			bool flag = !base.IsDisposed && num == Volatile.Read(ref sessionEpoch) && string.Equals(text, currentTargetIp, StringComparison.OrdinalIgnoreCase) && num2 == currentTargetPort;
			if (refreshLocal && flag)
			{
				await RefreshLocalBrowserAsync();
			}
			if (refreshRemote && flag)
			{
				await RefreshRemoteBrowserAsync(force: true);
			}
			UpdateLiveHints(latestSnapshot);
		}
		return result;
	}

	private (int Port, string User, string Pass, int TimeoutMs) GetTerminalFtpSettings()
	{
		CliConfig cliConfig = CliConfig.Load();
		return (cliConfig.DefaultFtpPort ?? 21, TrimOrNull(cliConfig.DefaultFtpUser) ?? "xbox", cliConfig.DefaultFtpPassword ?? "xbox", Math.Clamp(options.TimeoutMs, 1500, 7000));
	}

	private string? ShowTextPrompt(string title, string labelText, string initialValue)
	{
		using Form form = new Form();
		form.Text = title;
		form.StartPosition = FormStartPosition.CenterParent;
		form.FormBorderStyle = FormBorderStyle.FixedDialog;
		form.MinimizeBox = false;
		form.MaximizeBox = false;
		form.ShowInTaskbar = false;
		form.BackColor = TerminalBackground;
		form.ForeColor = Color.WhiteSmoke;
		form.ClientSize = new Size(420, 132);
		Label label = new Label
		{
			Text = labelText,
			ForeColor = Color.WhiteSmoke,
			Font = shellFont,
			AutoSize = false,
			TextAlign = ContentAlignment.MiddleLeft,
			Bounds = new Rectangle(12, 12, 396, 20)
		};
		TextBox textBox = new TextBox
		{
			Text = initialValue,
			Bounds = new Rectangle(12, 40, 396, 24),
			BackColor = CardBackground,
			ForeColor = Color.WhiteSmoke,
			BorderStyle = BorderStyle.FixedSingle,
			Font = shellFont
		};
		Button button = new Button
		{
			Text = "OK",
			DialogResult = DialogResult.OK,
			Bounds = new Rectangle(252, 88, 74, 28)
		};
		Button button2 = new Button
		{
			Text = "Cancel",
			DialogResult = DialogResult.Cancel,
			Bounds = new Rectangle(334, 88, 74, 28)
		};
		form.Controls.Add(label);
		form.Controls.Add(textBox);
		form.Controls.Add(button);
		form.Controls.Add(button2);
		form.AcceptButton = button;
		form.CancelButton = button2;
		if (form.ShowDialog(this) != DialogResult.OK)
		{
			return null;
		}
		return textBox.Text;
	}

	private async Task<List<(string RemotePath, string LocalPath)>> DownloadRemoteEntriesToLocalDirectoryAsync(IEnumerable<FileEntryView> entries, string destinationLocalDirectory, CancellationToken cancellationToken)
	{
		List<(string RemotePath, string LocalPath)> list = new List<(string RemotePath, string LocalPath)>();
		Directory.CreateDirectory(destinationLocalDirectory);
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		foreach (FileEntryView entry in entries.Where((FileEntryView entry) => entry.Name != ".."))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string uniqueLocalPath = GetUniqueLocalPath(Path.Combine(destinationLocalDirectory, entry.Name), entry.IsDirectory);
			if (entry.IsDirectory)
			{
				await DownloadRemoteDirectoryAsync(asyncFtpClient, entry.FullPath, uniqueLocalPath, cancellationToken);
			}
			else
			{
				await DownloadRemoteFileAsync(asyncFtpClient, entry.FullPath, uniqueLocalPath);
			}
			list.Add((entry.FullPath, uniqueLocalPath));
		}
		return list;
	}

	private async Task UploadLocalPathsToRemoteDirectoryAsync(IEnumerable<string> localPaths, string destinationRemoteDirectory, CancellationToken cancellationToken)
	{
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		foreach (string localPath in localPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(localPath))
			{
				string uniqueRemotePath = await GetUniqueRemotePathAsync(asyncFtpClient, CombineRemotePath(destinationRemoteDirectory, Path.GetFileName(localPath)), isDirectory: false);
				await UploadLocalFileAsync(asyncFtpClient, localPath, uniqueRemotePath, cancellationToken);
			}
			else if (Directory.Exists(localPath))
			{
				string fileName = new DirectoryInfo(localPath).Name;
				string uniqueRemotePath2 = await GetUniqueRemotePathAsync(asyncFtpClient, CombineRemotePath(destinationRemoteDirectory, fileName), isDirectory: true);
				await UploadLocalDirectoryAsync(asyncFtpClient, localPath, uniqueRemotePath2, cancellationToken);
			}
		}
	}

	private async Task PasteRemoteClipboardToRemoteAsync(PaneClipboardEntry clipboard, string destinationRemoteDirectory, CancellationToken cancellationToken)
	{
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		string remoteLeafName = GetRemoteLeafName(clipboard.FullPath);
		string uniqueRemotePath = await GetUniqueRemotePathAsync(asyncFtpClient, CombineRemotePath(destinationRemoteDirectory, remoteLeafName), clipboard.IsDirectory);
		if (clipboard.Move)
		{
			if (string.Equals(FtpHelpers.NormalizePath(clipboard.FullPath), uniqueRemotePath, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (clipboard.IsDirectory)
			{
				await asyncFtpClient.MoveDirectory(clipboard.FullPath, uniqueRemotePath);
				if (await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, clipboard.FullPath) || !await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, uniqueRemotePath))
				{
					throw new IOException("Remote folder move could not be verified.");
				}
			}
			else
			{
				await FtpHelpers.MoveFileVerifiedAsync(asyncFtpClient, clipboard.FullPath, uniqueRemotePath);
			}
			return;
		}
		await CopyRemoteEntryAsync(asyncFtpClient, clipboard.FullPath, uniqueRemotePath, clipboard.IsDirectory, cancellationToken);
	}

	private async Task DeleteRemotePathAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken)
	{
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		if (isDirectory)
		{
			await asyncFtpClient.DeleteDirectory(remotePath);
			if (await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, remotePath))
			{
				throw new IOException("Remote folder delete could not be verified.");
			}
		}
		else
		{
			await asyncFtpClient.DeleteFile(remotePath);
			if (await asyncFtpClient.FileExists(remotePath))
			{
				throw new IOException("Remote file delete could not be verified.");
			}
		}
	}

	private async Task CopyRemoteEntryAsync(AsyncFtpClient client, string sourceRemotePath, string destinationRemotePath, bool isDirectory, CancellationToken cancellationToken)
	{
		string text = Path.Combine(Path.GetTempPath(), "xecli-terminal-transfer-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, GetRemoteLeafName(destinationRemotePath));
			if (isDirectory)
			{
				await DownloadRemoteDirectoryAsync(client, sourceRemotePath, text2, cancellationToken);
				await UploadLocalDirectoryAsync(client, text2, destinationRemotePath, cancellationToken);
			}
			else
			{
				await DownloadRemoteFileAsync(client, sourceRemotePath, text2);
				await UploadLocalFileAsync(client, text2, destinationRemotePath, cancellationToken);
			}
		}
		finally
		{
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private async Task DownloadRemoteDirectoryAsync(AsyncFtpClient client, string remoteDirectory, string localDirectory, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(localDirectory);
		(FtpListItem[] Items, bool RootListing) valueTuple = await FtpHelpers.GetListingWithFallbackAsync(client, FtpHelpers.NormalizePath(remoteDirectory));
		foreach (FtpListItem item in valueTuple.Items)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(item.Name))
			{
				continue;
			}
			string text = FtpHelpers.NormalizePath(!string.IsNullOrWhiteSpace(item.FullName) ? item.FullName : CombineRemotePath(remoteDirectory, item.Name));
			string text2 = Path.Combine(localDirectory, item.Name);
			if (item.Type == FtpObjectType.File)
			{
				await DownloadRemoteFileAsync(client, text, text2);
			}
			else if (item.Type == FtpObjectType.Directory)
			{
				await DownloadRemoteDirectoryAsync(client, text, text2, cancellationToken);
			}
		}
	}

	private async Task DownloadRemoteFileAsync(AsyncFtpClient client, string remoteFilePath, string localFilePath)
	{
		string? directoryName = Path.GetDirectoryName(localFilePath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = GetRemoteLeafName(remoteFilePath);
		FtpStatus ftpStatus = await client.DownloadFile(localFilePath, FtpHelpers.NormalizePath(remoteFilePath), FtpLocalExists.Overwrite, FtpVerify.None, new Progress<FtpProgress>(delegate(FtpProgress progress)
		{
			HandleFtpProgress(progress, text);
		}));
		if (ftpStatus != FtpStatus.Success)
		{
			throw new IOException("FTP download did not complete for " + remoteFilePath + ".");
		}
		UpdateActiveTransferProgress(100.0, text);
	}

	private async Task UploadLocalDirectoryAsync(AsyncFtpClient client, string localDirectory, string remoteDirectory, CancellationToken cancellationToken)
	{
		await FtpHelpers.EnsureRemoteDirectoryAsync(client, remoteDirectory);
		foreach (string directory in Directory.GetDirectories(localDirectory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await UploadLocalDirectoryAsync(client, directory, CombineRemotePath(remoteDirectory, Path.GetFileName(directory)), cancellationToken);
		}
		foreach (string file in Directory.GetFiles(localDirectory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await UploadLocalFileAsync(client, file, CombineRemotePath(remoteDirectory, Path.GetFileName(file)), cancellationToken);
		}
	}

	private async Task UploadLocalFileAsync(AsyncFtpClient client, string localFilePath, string remoteFilePath, CancellationToken cancellationToken)
	{
		string remoteFilePath2 = FtpHelpers.NormalizePath(remoteFilePath);
		string remoteParentPath = GetRemoteParentPath(remoteFilePath2);
		await FtpHelpers.EnsureRemoteDirectoryAsync(client, remoteParentPath);
		long length = new FileInfo(localFilePath).Length;
		string text = Path.GetFileName(localFilePath);
		FtpStatus ftpStatus = await client.UploadFile(localFilePath, remoteFilePath2, FtpRemoteExists.Overwrite, false, FtpVerify.None, new Progress<FtpProgress>(delegate(FtpProgress progress)
		{
			HandleFtpProgress(progress, text);
		}), cancellationToken);
		if (ftpStatus != FtpStatus.Success)
		{
			throw new IOException("FTP upload did not complete for " + remoteFilePath2 + ".");
		}
		long? num = await FtpHelpers.TryGetFileSizeAsync(client, remoteFilePath2);
		if (!num.HasValue || num.Value != length)
		{
			throw new IOException("FTP upload size mismatch for " + remoteFilePath2 + ".");
		}
		UpdateActiveTransferProgress(100.0, text);
	}

	private static async Task CopyLocalEntryAsync(string sourcePath, string destinationPath, bool isDirectory, CancellationToken cancellationToken)
	{
		if (isDirectory)
		{
			await CopyLocalDirectoryAsync(sourcePath, destinationPath, cancellationToken);
			return;
		}
		string? directoryName = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		await using FileStream fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
		await using FileStream fileStream2 = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
		await fileStream.CopyToAsync(fileStream2, 131072, cancellationToken);
	}

	private static async Task CopyLocalDirectoryAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(destinationDirectory);
		foreach (string directory in Directory.GetDirectories(sourceDirectory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CopyLocalDirectoryAsync(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)), cancellationToken);
		}
		foreach (string file in Directory.GetFiles(sourceDirectory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CopyLocalEntryAsync(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), isDirectory: false, cancellationToken);
		}
	}

	private static void DeleteLocalPath(string path, bool isDirectory)
	{
		if (isDirectory)
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		else if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private async Task<string> GetUniqueRemotePathAsync(AsyncFtpClient client, string desiredRemotePath, bool isDirectory)
	{
		string text = FtpHelpers.NormalizePath(desiredRemotePath);
		if (!await RemotePathExistsAsync(client, text, isDirectory))
		{
			return text;
		}
		string remoteParentPath = GetRemoteParentPath(text);
		string remoteLeafName = GetRemoteLeafName(text);
		string extension = isDirectory ? string.Empty : Path.GetExtension(remoteLeafName);
		string text2 = isDirectory ? remoteLeafName : Path.GetFileNameWithoutExtension(remoteLeafName);
		for (int i = 1; i < 256; i++)
		{
			string text3 = text2 + ((i == 1) ? " - Copy" : $" - Copy {i}") + extension;
			string text4 = CombineRemotePath(remoteParentPath, text3);
			if (!await RemotePathExistsAsync(client, text4, isDirectory))
			{
				return text4;
			}
		}
		return CombineRemotePath(remoteParentPath, text2 + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + extension);
	}

	private async Task<bool> RemotePathExistsAsync(AsyncFtpClient client, string remotePath, bool isDirectory)
	{
		if (isDirectory)
		{
			return await FtpHelpers.DirectoryExistsByCwdAsync(client, remotePath);
		}
		return await client.FileExists(remotePath);
	}

	private string GetLocalTransferTargetDirectory()
	{
		string text = TrimOrNull(localCurrentPath);
		if (!string.IsNullOrWhiteSpace(text) && Directory.Exists(text))
		{
			return text;
		}
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
		if (Directory.Exists(path))
		{
			return path;
		}
		return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
	}

	private static string GetUniqueLocalPath(string desiredLocalPath, bool isDirectory)
	{
		if ((!isDirectory && !File.Exists(desiredLocalPath)) || (isDirectory && !Directory.Exists(desiredLocalPath)))
		{
			return desiredLocalPath;
		}
		string directoryName = Path.GetDirectoryName(desiredLocalPath) ?? string.Empty;
		string text = isDirectory ? new DirectoryInfo(desiredLocalPath).Name : Path.GetFileNameWithoutExtension(desiredLocalPath);
		string extension = isDirectory ? string.Empty : Path.GetExtension(desiredLocalPath);
		for (int i = 1; i < 256; i++)
		{
			string path = Path.Combine(directoryName, text + ((i == 1) ? " - Copy" : $" - Copy {i}") + extension);
			if ((!isDirectory && !File.Exists(path)) || (isDirectory && !Directory.Exists(path)))
			{
				return path;
			}
		}
		return Path.Combine(directoryName, text + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + extension);
	}

	private static string GetRemoteLeafName(string remotePath)
	{
		string text = FtpHelpers.NormalizePath(remotePath).TrimEnd('/');
		int num = text.LastIndexOf('/');
		return (num < 0) ? text : text.Substring(num + 1);
	}

	private static string SanitizeRemoteLeaf(string? value)
	{
		string text = TrimOrNull(value) ?? string.Empty;
		text = text.Replace('\\', ' ').Replace('/', ' ').Trim();
		return text;
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
		localPathLabel.Text = string.IsNullOrWhiteSpace(text) ? "PC /" : ("PC " + text);
		localPathTextBox.Text = string.IsNullOrWhiteSpace(text) ? "This PC" : text;
		localFileList.SetMessage("Loading local view...");
		try
		{
			LocalBrowserSnapshot localBrowserSnapshot = await Task.Run(() => LoadLocalBrowserSnapshot(text, cancellationTokenSource.Token), cancellationTokenSource.Token);
			if (base.IsDisposed || cancellationTokenSource.IsCancellationRequested || num != localBrowseVersion)
			{
				return;
			}
			localCurrentPath = localBrowserSnapshot.ResolvedPath;
			localPathLabel.Text = localBrowserSnapshot.PathLabel;
			localPathTextBox.Text = string.IsNullOrWhiteSpace(localBrowserSnapshot.ResolvedPath) ? "This PC" : localBrowserSnapshot.ResolvedPath;
			localEntries.Clear();
			localEntries.AddRange(localBrowserSnapshot.Entries);
			localFileList.SetEntries(localBrowserSnapshot.Entries);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!base.IsDisposed && num == localBrowseVersion)
			{
				localEntries.Clear();
				localFileList.SetMessage("Local browse failed: " + ex.Message);
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
				list.Add(new FileEntryView
				{
					Name = FormatLocalRootDisplayName(item),
					FullPath = item.RootDirectory.FullName,
					IsDirectory = true
				});
			}
		return new LocalBrowserSnapshot
		{
			ResolvedPath = null,
			Entries = list,
			Items = new List<string>(),
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
					IsDirectory = true,
					ModifiedUtc = Directory.GetLastWriteTimeUtc(item2)
				});
			}
			foreach (string item3 in Directory.GetFiles(path).OrderBy((string p) => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				list.Add(new FileEntryView
				{
					Name = Path.GetFileName(item3),
					FullPath = item3,
					IsDirectory = false,
					SizeBytes = new FileInfo(item3).Length,
					ModifiedUtc = File.GetLastWriteTimeUtc(item3)
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
		return new LocalBrowserSnapshot
		{
			ResolvedPath = path,
			Entries = list,
			Items = new List<string>(),
			PathLabel = "PC :: " + path
		};
	}

	private static string FormatLocalRootDisplayName(DriveInfo drive)
	{
		string text = drive.Name.TrimEnd('\\');
		string text2 = string.Empty;
		try
		{
			if (drive.IsReady)
			{
				text2 = TrimOrNull(drive.VolumeLabel) ?? string.Empty;
			}
		}
		catch
		{
		}
		return string.IsNullOrWhiteSpace(text2) ? text : (text + "  " + text2);
	}

	private async Task RefreshRemoteBrowserAsync(bool force = false)
	{
		if (shellDisconnected || base.IsDisposed || fileTransferInFlight || (!force && DateTime.UtcNow < nextRemoteRefreshAllowedUtc))
		{
			return;
		}
		int num = Interlocked.Increment(ref remoteBrowseVersion);
		CancelAndDispose(ref remoteBrowseCts);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		remoteBrowseCts = cancellationTokenSource;
		string text = NormalizeRemotePath(remoteCurrentPath);
		nextRemoteRefreshAllowedUtc = DateTime.UtcNow.AddSeconds(force ? 1.0 : 4.0);
		remotePathLabel.Text = FormatRemotePathLabel(text) + "  :: loading";
		remoteFileList.SetMessage("Loading remote view...");
		try
		{
			(string pathText, List<FileEntryView> entries, List<string> items) valueTuple = await Task.Run(() => LoadRemoteBrowserSnapshotAsync(text, cancellationTokenSource.Token), cancellationTokenSource.Token);
			if (base.IsDisposed || cancellationTokenSource.IsCancellationRequested || num != remoteBrowseVersion)
			{
				return;
			}
			remoteEntries.Clear();
			remoteEntries.AddRange(valueTuple.entries);
			remotePathLabel.Text = FormatRemotePathLabel(valueTuple.pathText);
			remotePathTextBox.Text = valueTuple.pathText;
			remoteFileList.SetEntries(valueTuple.entries);
			remoteCurrentPath = valueTuple.pathText;
			if (string.Equals(valueTuple.pathText, "/", StringComparison.Ordinal))
			{
				List<DriveInventoryEntry> list = valueTuple.entries.Where(static entry => entry.IsDirectory && entry.Name != "..")
					.Select(entry => NormalizeDriveEntry(new DriveInventoryEntry
					{
						Name = entry.Name
					}))
					.Where(static entry => entry != null)
					.Select(static entry => entry!)
					.GroupBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
					.Select(MergeDriveGroup)
					.OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
					.ToList();
				if (list.Count != 0)
				{
					List<DriveInventoryEntry> list2 = list;
					if (latestSnapshot != null)
					{
						Dictionary<string, DriveInventoryEntry> dictionary = latestSnapshot.Drives
							.GroupBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
							.Select(MergeDriveGroup)
							.ToDictionary(static entry => entry.Name, StringComparer.OrdinalIgnoreCase);
						list2 = list.Select(delegate(DriveInventoryEntry entry)
						{
							if (dictionary.TryGetValue(entry.Name, out DriveInventoryEntry value))
							{
								return new DriveInventoryEntry
								{
									Name = entry.Name,
									TotalBytes = value.TotalBytes,
									FreeBytes = value.FreeBytes
								};
							}
							return entry;
						}).ToList();
						latestSnapshot.Drives.Clear();
						latestSnapshot.Drives.AddRange(list2);
					}
					UpdateDriveInventory(list2);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!base.IsDisposed && num == remoteBrowseVersion)
			{
				nextRemoteRefreshAllowedUtc = DateTime.UtcNow.AddSeconds(10.0);
				remotePathLabel.Text = FormatRemotePathLabel(text);
				remotePathTextBox.Text = text;
				remoteFileList.SetMessage("Remote browse failed: " + ex.Message);
			}
		}
	}

	private async Task<(string PathText, List<FileEntryView> Entries, List<string> Items)> LoadRemoteBrowserSnapshotAsync(string remotePath, CancellationToken cancellationToken)
	{
		List<FileEntryView> list = new List<FileEntryView>();
		CliConfig cliConfig = CliConfig.Load();
		int num = cliConfig.DefaultFtpPort ?? 21;
		string user = TrimOrNull(cliConfig.DefaultFtpUser) ?? "xbox";
		string pass = cliConfig.DefaultFtpPassword ?? "xbox";
		int timeoutMs = Math.Clamp(options.TimeoutMs, 1200, 7000);
		string text = NormalizeRemotePath(remotePath);
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cancellationTokenSource.CancelAfter(timeoutMs);
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, num, user, pass, timeoutMs);
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
			string fullPath = NormalizeRemotePath(!string.IsNullOrWhiteSpace(item2.FullName) ? item2.FullName : CombineRemotePath(text, item2.Name));
			list.Add(new FileEntryView
			{
				Name = item2.Name,
				FullPath = fullPath,
				IsDirectory = isDirectory,
				SizeBytes = isDirectory ? null : item2.Size,
				ModifiedUtc = item2.Modified.ToUniversalTime()
			});
		}
		return (text, list, new List<string>());
	}

	private static string CombineRemotePath(string parentPath, string name)
	{
		string text = NormalizeRemotePath(parentPath).TrimEnd('/');
		if (string.IsNullOrEmpty(text))
		{
			text = "/";
		}
		if (text == "/")
		{
			return NormalizeRemotePath("/" + name.Trim('/'));
		}
		return NormalizeRemotePath(text + "/" + name.Trim('/'));
	}

	private static string GetRemoteParentPath(string path)
	{
		string text = NormalizeRemotePath(path).TrimEnd('/');
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

	private static string NormalizeRemotePath(string path)
	{
		string text = FtpHelpers.NormalizePath(path);
		string[] array = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
		List<string> list = new List<string>(array.Length);
		foreach (string text2 in array)
		{
			if (text2 == ".")
			{
				continue;
			}
			if (text2 == "..")
			{
				if (list.Count > 0)
				{
					list.RemoveAt(list.Count - 1);
				}
				continue;
			}
			list.Add(text2);
		}
		return "/" + string.Join("/", list);
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
		SetMainShellBannerText(text);
		mainShellBannerLabel.ForeColor = foreground;
		mainShellBannerLabel.BackColor = background;
	}

	private void InitializeInteractiveActions()
	{
		connectButton.Click += async delegate
		{
			if (connectAttemptInFlight || fileTransferInFlight)
			{
				return;
			}
			if (!TryApplyManualTargetFromInputs(announceChange: false, announceUnchanged: false))
			{
				return;
			}
			CancelAndDispose(ref connectProbeCts);
			CancelAndDispose(ref fileTransferCts);
			CancelAndDispose(ref remoteBrowseCts);
			int sessionVersion = Interlocked.Increment(ref sessionEpoch);
			connectAttemptInFlight = true;
			connectAttemptStartedUtc = DateTime.UtcNow;
			latestSnapshot = null;
			remoteEntries.Clear();
			remoteCurrentPath = "/";
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			SetFooterStatusText("CONNECTING");
			footerStatusLabel.ForeColor = AccentGreen;
			SetPersistentConnectState("LINK NEGOTIATING", AccentGreen, Color.FromArgb(18, 27, 18));
			SetRightNetworkText("LINK      NEGOTIATING\nTARGET    " + FormatStatusTarget() + "\nFTP       connecting\nPRESENCE  pending");
			SetRightTempText("CONNECTING");
			drivesList.SetEntries(new DriveInventoryEntry[1]
			{
				new DriveInventoryEntry
				{
					Name = "Connecting..."
				}
			});
			inventoryList.SetItems(new string[1] { TranslateTerminalText(inventoryShowsModules ? "Connecting to live modules..." : "Connecting to live plugins...") });
			SetRightDetailText(BuildConnectingSessionText());
			UpdateDriveInventory(Array.Empty<DriveInventoryEntry>());
			SetRemoteBrowserPlaceholder("/", "Waiting for FTP...");
			UpdateShellScaffold();
			SetCommandInputPlaceholder("Connecting to console...");
			SetConnectButtonConnectingState();
			await Task.Yield();
			bool flag = false;
			try
			{
				connectProbeCts = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
				flag = await Task.Run(() => TryProbeConnectionAsync(connectProbeCts.Token), formLifetimeCts.Token);
				if (!flag)
				{
					AppendSystemLine("Connect probe timed out.", WarningColor);
				}
			}
			catch (Exception ex)
			{
				flag = false;
				AppendSystemLine("Connect failed: " + ex.Message, AccentPink);
			}
			finally
			{
				CancelAndDispose(ref connectProbeCts);
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
					RestoreConnectButtonIdleState();
					SetCommandInputPlaceholder("Enter XeCLI command for the current console session...");
					return;
				}
				connectAttemptInFlight = false;
				RestoreConnectButtonIdleState();
				SetCommandInputPlaceholder("Connect or enter a XeCLI command for this session...");
				if (sessionVersion != Volatile.Read(ref sessionEpoch))
				{
					return;
				}
				if (flag)
				{
					shellDisconnected = false;
					if (!firstConnectWorkspacePrepared)
					{
						firstConnectWorkspacePrepared = true;
						ClearTerminalWorkspace();
						transferQueueEntries.Clear();
						RefreshTransferQueueDisplay();
					}
					SetPersistentConnectState("LINK ACTIVE", AccentGreen, Color.FromArgb(18, 34, 32));
					SetFooterStatusText("CONNECTED");
					footerStatusLabel.ForeColor = AccentGreen;
					AppendSystemLine("Console link active.", AccentGreen);
					UpdateRuntimePresence(connected: true, new TelemetrySnapshot
					{
						Connected = true
					});
					SetRightNetworkText("LINK      ONLINE\nTARGET    " + FormatStatusTarget() + "\nFTP       refreshing\nPRESENCE  syncing");
					SetRightDetailText("Connection established.\nLoading title, XEX, user,\nsign-in, and queue...");
					SetRemoteBrowserPlaceholder("/", "Refreshing remote root...");
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
					_ = HydrateConnectedSessionAsync(sessionVersion);
				}
				else
				{
					shellDisconnected = true;
					latestSnapshot = null;
					UpdateRuntimePresence(connected: false);
					SetPersistentConnectState("LINK FAILED", WarningColor, Color.FromArgb(27, 21, 16));
					SetFooterStatusText("DISCONNECTED");
					footerStatusLabel.ForeColor = WarningColor;
					SetRemoteBrowserPlaceholder("/", "Connect failed or timed out");
					SetRightNetworkText(BuildDisconnectedStatusText());
					UpdateShellScaffold();
				}
			}
		};
		disconnectButton.Click += delegate
		{
			CancelAndDispose(ref connectProbeCts);
			CancelAndDispose(ref fileTransferCts);
			CancelAndDispose(ref remoteBrowseCts);
			CancelActiveCommand();
			shellDisconnected = true;
			connectAttemptInFlight = false;
			Interlocked.Increment(ref sessionEpoch);
			remoteEntries.Clear();
			remoteCurrentPath = "/";
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			latestSnapshot = null;
			UpdateRuntimePresence(connected: false);
			SetFooterStatusText("DISCONNECTED");
			footerStatusLabel.ForeColor = WarningColor;
			SetPersistentConnectState("LINK OFFLINE", WarningColor, Color.FromArgb(24, 19, 15));
			UpdateConnectionStatusIndicator(new TelemetrySnapshot
			{
				Connected = false
			});
			UpdateShellScaffold();
			telemetryTimer.Stop();
			RestoreConnectButtonIdleState();
			SetCommandInputPlaceholder("Connect or enter a XeCLI command for this session...");
			SetRightNetworkText(BuildDisconnectedStatusText());
			SetRightTempText("IDLE");
			SetRightDetailText(BuildDisconnectedSessionText());
			SetRemoteBrowserPlaceholder("/", "Disconnected");
			if (transferQueueEntries.Count == 0)
			{
				RefreshTransferQueueDisplay();
			}
		};
		screenshotButton.Click += async delegate
		{
			await ExecuteScreenshotCaptureAsync();
		};
		languageButton.Click += delegate
		{
			ToggleUiLanguage();
		};
		targetIpTextBox.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				TryApplyManualTargetFromInputs();
			}
		};
		pluginsTabButton.Click += delegate
		{
			inventoryShowsModules = false;
			inventoryList.ResetViewport();
			RefreshInventoryHeader();
			RefreshInventoryListFromSnapshot();
		};
		modulesTabButton.Click += delegate
		{
			inventoryShowsModules = true;
			inventoryList.ResetViewport();
			RefreshInventoryHeader();
			RefreshInventoryListFromSnapshot();
		};
		RefreshInventoryHeader();
		RefreshLanguageButtonText();
	}

	private static string NormalizeUiLanguageCode(string? value)
	{
		return string.Equals(value?.Trim(), "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
	}

	private static string GetConfiguredUiLanguageCode()
	{
		try
		{
			return NormalizeUiLanguageCode(CliConfig.Load().UiLanguage);
		}
		catch
		{
			return "en";
		}
	}

	private void RefreshLanguageButtonText()
	{
		languageButton.Text = "LANG: " + (GetConfiguredUiLanguageCode() == "es" ? "ES" : "EN");
	}

	private static string GetFooterVersionText()
	{
		System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
		string? informationalVersion = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
			.OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
			.FirstOrDefault()?.InformationalVersion;
		string text = string.IsNullOrWhiteSpace(informationalVersion) ? assembly.GetName().Version?.ToString(3) : informationalVersion;
		if (!string.IsNullOrWhiteSpace(text))
		{
			int num = text.IndexOf('+');
			if (num >= 0)
			{
				text = text.Substring(0, num);
			}
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "1.1.0";
		}
		return "XeCLI v" + text.Trim();
	}

	private static void OpenXeCliRepository()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "https://github.com/SaveEditors/xecli",
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private static string TranslateTerminalText(string text)
	{
		if (!LocalizedText.IsSpanish || string.IsNullOrEmpty(text))
		{
			return text;
		}
		string text2 = text switch
		{
			"XeCLI Terminal" => "Terminal XeCLI",
			"CONSOLE" => "CONSOLA",
			"STATUS" => "ESTADO",
			"TRAFFIC" => "TRAFICO",
			"DETECTED DRIVES" => "UNIDADES DETECTADAS",
			"Plugins" => "Plugins",
			"Modules" => "Modulos",
			"CONNECT" => "CONECTAR",
			"CONNECTING" => "CONECTANDO",
			"DISCONNECT" => "DESCONECTAR",
			"SCREENSHOT" => "CAPTURA",
			"Refresh" => "Actualizar",
			"LOCAL PATH" => "RUTA LOCAL",
			"REMOTE PATH" => "RUTA REMOTA",
			"QUEUE" => "COLA",
			"NAME" => "NOMBRE",
			"MODIFIED" => "MODIFICADO",
			"SIZE" => "TAMANO",
			"IDLE" => "EN ESPERA",
			"XECLI // ACTIVE SHELL" => "XECLI // SHELL ACTIVA",
			"LIVE SHELL // TARGET " => "SHELL EN VIVO // DESTINO ",
			_ => text
		};
		if (!string.Equals(text2, text, StringComparison.Ordinal))
		{
			return text2;
		}
		text2 = TranslateTerminalStructuredLines(text2);
		text2 = text2.Replace("No live console context.", "No hay contexto activo de consola.", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to load title, XEX,", "Conecta para cargar titulo, XEX,", StringComparison.Ordinal);
		text2 = text2.Replace("user, presence, and TitleID.", "usuario, presencia y TitleID.", StringComparison.Ordinal);
		text2 = text2.Replace("DISCONNECTED SESSION", "SESION DESCONECTADA", StringComparison.Ordinal);
		text2 = text2.Replace("Use CONNECT to load thermals, FTP, title, and user state.", "Usa CONNECT para cargar termicas, FTP, titulo y estado del usuario.", StringComparison.Ordinal);
		text2 = text2.Replace("Local XeCLI commands remain available below.", "Los comandos locales de XeCLI siguen disponibles abajo.", StringComparison.Ordinal);
		text2 = text2.Replace("NEGOTIATING LINK", "NEGOCIANDO ENLACE", StringComparison.Ordinal);
		text2 = text2.Replace("Handshake in progress.", "Negociacion en curso.", StringComparison.Ordinal);
		text2 = text2.Replace("Live telemetry will hydrate automatically.", "La telemetria en vivo se cargara automaticamente.", StringComparison.Ordinal);
		text2 = text2.Replace("LIVE SESSION ONLINE", "SESION EN LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("Connection established.", "Conexion establecida.", StringComparison.Ordinal);
		text2 = text2.Replace("Loading title, XEX, user,", "Cargando titulo, XEX, usuario,", StringComparison.Ordinal);
		text2 = text2.Replace("sign-in, and queue...", "inicio de sesion y cola...", StringComparison.Ordinal);
		text2 = text2.Replace("Waiting for FTP...", "Esperando FTP...", StringComparison.Ordinal);
		text2 = text2.Replace("Refreshing remote root...", "Actualizando raiz remota...", StringComparison.Ordinal);
		text2 = text2.Replace("Connect for Modules", "Conecta para modulos", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to live modules...", "Conectando a modulos activos...", StringComparison.Ordinal);
		text2 = text2.Replace("Connect for Plugins", "Conecta para plugins", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to live plugins...", "Conectando a plugins activos...", StringComparison.Ordinal);
		text2 = text2.Replace("No live modules detected", "No se detectaron modulos activos", StringComparison.Ordinal);
		text2 = text2.Replace("No cached modules loaded", "No hay modulos en cache", StringComparison.Ordinal);
		text2 = text2.Replace("No live plugins detected", "No se detectaron plugins activos", StringComparison.Ordinal);
		text2 = text2.Replace("No cached plugins loaded", "No hay plugins en cache", StringComparison.Ordinal);
		text2 = text2.Replace("No live drives detected", "No se detectaron unidades activas", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting...", "Conectando...", StringComparison.Ordinal);
		text2 = text2.Replace("Live storage root", "Raiz de almacenamiento", StringComparison.Ordinal);
		text2 = text2.Replace("QUEUE IDLE", "COLA EN ESPERA", StringComparison.Ordinal);
		text2 = text2.Replace("No transfers staged", "No hay transferencias en cola", StringComparison.Ordinal);
		text2 = text2.Replace("Connect or enter a XeCLI command for this session...", "Conecta o escribe un comando XeCLI para esta sesion...", StringComparison.Ordinal);
		text2 = text2.Replace("Enter XeCLI command for the current console session...", "Escribe un comando XeCLI para la sesion actual de la consola...", StringComparison.Ordinal);
		text2 = text2.Replace("Connect or enter a XeCLI command...", "Conecta o escribe un comando XeCLI...", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to console...", "Conectando a la consola...", StringComparison.Ordinal);
		text2 = text2.Replace("TARGET READY", "OBJETIVO LISTO", StringComparison.Ordinal);
		text2 = text2.Replace("LINK NEGOTIATING", "NEGOCIANDO ENLACE", StringComparison.Ordinal);
		text2 = text2.Replace("LINK ACTIVE", "ENLACE ACTIVO", StringComparison.Ordinal);
		text2 = text2.Replace("LINK OFFLINE", "ENLACE FUERA DE LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("LINK FAILED", "FALLO DE ENLACE", StringComparison.Ordinal);
		text2 = text2.Replace("PRESENCE · OFFLINE", "PRESENCIA · FUERA DE LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("Signed In", "Sesion iniciada", StringComparison.Ordinal);
		text2 = text2.Replace("Not Signed In", "Sin iniciar sesion", StringComparison.Ordinal);
		text2 = text2.Replace("Spanish", "Espanol", StringComparison.Ordinal);
		text2 = text2.Replace("English", "Ingles", StringComparison.Ordinal);
		text2 = text2.Replace("CONNECTING", "CONECTANDO", StringComparison.Ordinal);
		text2 = text2.Replace("DISCONNECTED", "DESCONECTADO", StringComparison.Ordinal);
		text2 = text2.Replace("CONNECTED", "CONECTADO", StringComparison.Ordinal);
		text2 = text2.Replace("ONLINE", "EN LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("OFFLINE", "FUERA DE LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("syncing", "sincronizando", StringComparison.Ordinal);
		text2 = text2.Replace("refreshing", "actualizando", StringComparison.Ordinal);
		text2 = text2.Replace("connecting", "conectando", StringComparison.Ordinal);
		text2 = text2.Replace("pending", "pendiente", StringComparison.Ordinal);
		text2 = text2.Replace("offline", "fuera de linea", StringComparison.Ordinal);
		text2 = text2.Replace("idle", "inactivo", StringComparison.Ordinal);
		text2 = text2.Replace("unknown", "desconocido", StringComparison.Ordinal);
		return LocalizedText.Translate(text2);
	}

	private static string TranslateTerminalStructuredLines(string text)
	{
		string[] array = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			if (TryTranslateTerminalFieldPrefix(array[i], out string text2))
			{
				array[i] = text2;
			}
		}
		return string.Join(text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", array);
	}

	private static bool TryTranslateTerminalFieldPrefix(string line, out string translated)
	{
		translated = line;
		if (line.Length < 10)
		{
			return false;
		}
		string text = line.Substring(0, 10).Trim();
		string text2 = text switch
		{
			"BOARD" => "PLACA",
			"DASH" => "DASH",
			"GAME" => "JUEGO",
			"XEX" => "XEX",
			"TITLEID" => "TITLEID",
			"GAMERTAG" => "USUARIO",
			"LINK" => "ENLACE",
			"TARGET" => "DESTINO",
			"FTP" => "FTP",
			"DRIVES" => "UNIDADES",
			"PRESENCE" => "ESTADO",
			"ALERT" => "ALERTA",
			"TITLE" => "TITULO",
			"STATE" => "ESTADO",
			"USER" => "USUARIO",
			_ => string.Empty
		};
		if (string.IsNullOrEmpty(text2))
		{
			return false;
		}
		translated = text2.PadRight(10) + line.Substring(10);
		return true;
	}

	private void SetConnectionStatusText(string englishText)
	{
		connectionStatusTextSource = englishText;
		SetLabelText(connectionStatusLabel, TranslateTerminalText(englishText));
	}

	private void SetConnectButtonText(string englishText)
	{
		connectButtonTextSource = englishText;
		string text = TranslateTerminalText(englishText);
		if (!string.Equals(connectButton.Text, text, StringComparison.Ordinal))
		{
			connectButton.Text = text;
		}
	}

	private void SetCommandInputPlaceholder(string englishText)
	{
		commandInputPlaceholderSource = englishText;
		string text = TranslateTerminalText(englishText);
		if (!string.Equals(commandInput.PlaceholderText, text, StringComparison.Ordinal))
		{
			commandInput.PlaceholderText = text;
		}
	}

	private void SetFooterPresenceText(string englishText)
	{
		footerPresenceTextSource = englishText;
		SetLabelText(footerPresenceLabel, TranslateTerminalText(englishText));
	}

	private void SetFooterStatusText(string englishText)
	{
		footerStatusTextSource = englishText;
		SetLabelText(footerStatusLabel, TranslateTerminalText(englishText));
	}

	private void SetFooterThermalText(string englishText)
	{
		footerThermalTextSource = englishText;
		SetLabelText(footerThermalLabel, TranslateTerminalText(englishText));
	}

	private void SetLeftConsoleText(string englishText)
	{
		leftConsoleTextSource = englishText;
		leftConsoleInfoPanel.SetContent(TranslateTerminalText(englishText));
	}

	private void SetLeftSignInText(string englishText)
	{
		leftSignInTextSource = englishText;
		SetLabelText(leftSignInLabel, TranslateTerminalText(englishText));
	}

	private void SetMainShellBannerText(string englishText)
	{
		mainShellBannerTextSource = englishText;
		SetLabelText(mainShellBannerLabel, TranslateTerminalText(englishText));
	}

	private void SetRightDetailText(string englishText)
	{
		rightDetailTextSource = englishText;
		SetLabelText(rightDetailLabel, TranslateTerminalText(englishText));
	}

	private void SetRightDrivesTitleText(string englishText)
	{
		rightDrivesTitleTextSource = englishText;
		SetLabelText(rightDrivesLabel, TranslateTerminalText(englishText));
	}

	private void SetRightNetworkText(string englishText)
	{
		rightNetworkTextSource = englishText;
		rightNetworkInfoPanel.SetContent(TranslateTerminalText(englishText));
	}

	private void SetRightTempText(string englishText)
	{
		rightTempTextSource = englishText;
		SetLabelText(rightTempLabel, TranslateTerminalText(englishText));
	}

	private static void SetControlText(Control control, string value)
	{
		if (!string.Equals(control.Text, value, StringComparison.Ordinal))
		{
			control.Text = value;
		}
	}

	private static Label? FindFirstChildLabel(Control control)
	{
		foreach (Control child in control.Controls)
		{
			if (child is Label label)
			{
				return label;
			}
			Label? label2 = FindFirstChildLabel(child);
			if (label2 != null)
			{
				return label2;
			}
		}
		return null;
	}

	private void SetPanelHeaderLabelText(Panel panel, string englishText)
	{
		Label? label = FindFirstChildLabel(panel);
		if (label != null)
		{
			SetLabelText(label, TranslateTerminalText(englishText));
		}
	}

	private void RefreshShellTargetLabelText()
	{
		SetLabelText(shellTargetLabel, TranslateTerminalText("LIVE SHELL // TARGET ") + FormatCurrentTarget());
	}

	private void RefreshLocalizedUiText()
	{
		base.Text = TranslateTerminalText("XeCLI Terminal");
		SetLabelText(leftConsoleHeaderLabel, TranslateTerminalText("CONSOLE"));
		SetLabelText(rightStatusHeaderLabel, TranslateTerminalText("STATUS"));
		SetLabelText(rightTrafficHeaderLabel, TranslateTerminalText("TRAFFIC"));
		SetLabelText(shellBadgeLabel, TranslateTerminalText("XECLI // ACTIVE SHELL"));
		RefreshShellTargetLabelText();
		SetControlText(pluginsTabButton, TranslateTerminalText("Plugins"));
		SetControlText(modulesTabButton, TranslateTerminalText("Modules"));
		SetControlText(disconnectButton, TranslateTerminalText("DISCONNECT"));
		SetControlText(screenshotButton, TranslateTerminalText("SCREENSHOT"));
		SetControlText(localUpButton, TranslateTerminalText("UP"));
		SetControlText(localRefreshButton, TranslateTerminalText("Refresh"));
		SetControlText(remoteUpButton, TranslateTerminalText("UP"));
		SetControlText(remoteRefreshButton, TranslateTerminalText("Refresh"));
		SetControlText(ftpTabButton, TranslateTerminalText("FTP"));
		SetControlText(queueTabButton, TranslateTerminalText("QUEUE"));
		SetPanelHeaderLabelText(targetIpHostPanel, "HOST / IP");
		SetPanelHeaderLabelText(localPathHostPanel, "LOCAL PATH");
		SetPanelHeaderLabelText(remotePathHostPanel, "REMOTE PATH");
		localFileList.HeaderText = TranslateTerminalText("NAME");
		remoteFileList.HeaderText = TranslateTerminalText("NAME");
		RefreshLanguageButtonText();
		SetConnectButtonText(connectButtonTextSource);
		SetCommandInputPlaceholder(commandInputPlaceholderSource);
		SetConnectionStatusText(connectionStatusTextSource);
		SetFooterStatusText(footerStatusTextSource);
		SetFooterPresenceText(footerPresenceTextSource);
		SetFooterThermalText(footerThermalTextSource);
		SetMainShellBannerText(mainShellBannerTextSource);
		SetLeftConsoleText(leftConsoleTextSource);
		SetLeftSignInText(leftSignInTextSource);
		SetRightNetworkText(rightNetworkTextSource);
		SetRightTempText(rightTempTextSource);
		SetRightDetailText(rightDetailTextSource);
		SetRightDrivesTitleText(rightDrivesTitleTextSource);
		RefreshInventoryListFromSnapshot();
		UpdateDriveInventory((IEnumerable<DriveInventoryEntry>?)latestSnapshot?.Drives ?? Array.Empty<DriveInventoryEntry>());
		localFileList.Invalidate();
		remoteFileList.Invalidate();
		drivesList.Invalidate();
		transferQueueList.Invalidate();
		ftpTrafficGraph.Invalidate();
	}

	private void RefreshTargetEditorText()
	{
		targetIpTextBox.Text = currentTargetIp;
		RefreshShellTargetLabelText();
	}

	private void ToggleUiLanguage()
	{
		CliConfig cliConfig = CliConfig.Load();
		string text = (NormalizeUiLanguageCode(cliConfig.UiLanguage) == "es") ? "en" : "es";
		cliConfig.UiLanguage = text;
		cliConfig.Save();
		LocalizedText.Initialize(text);
		CultureInfo.CurrentUICulture = LocalizedText.Culture;
		CultureInfo.DefaultThreadCurrentUICulture = LocalizedText.Culture;
		RefreshLocalizedUiText();
		RefreshLanguageButtonText();
		AppendSystemLine(TranslateTerminalText("CLI language set to " + ((text == "es") ? "Spanish" : "English") + ". New commands use it immediately."), AccentGreen);
	}

	private void ConfigureActionButton(Button button, string text)
	{
		button.Dock = DockStyle.Fill;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderColor = BorderColor;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 46, 28);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(34, 60, 34);
		button.BackColor = TerminalBackground;
		button.ForeColor = Color.WhiteSmoke;
		button.Font = new Font("Consolas", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
		button.MinimumSize = new Size(0, 30);
		button.AutoEllipsis = false;
		button.TextAlign = ContentAlignment.MiddleCenter;
		button.Text = text;
		button.Cursor = Cursors.Hand;
	}

	private void ConfigureInputTextBox(TextBox textBox, string placeholderText, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
	{
		textBox.Dock = DockStyle.Fill;
		textBox.BackColor = Color.FromArgb(8, 14, 11);
		textBox.ForeColor = Color.WhiteSmoke;
		textBox.BorderStyle = BorderStyle.None;
		textBox.Font = shellFont;
		textBox.Margin = new Padding(0, 1, 0, 0);
		textBox.ShortcutsEnabled = true;
		textBox.Multiline = false;
		textBox.PlaceholderText = placeholderText;
		textBox.TextAlign = horizontalAlignment;
	}

	private void ConfigureInputHostPanel(Panel panel, TextBox textBox, string labelText)
	{
		panel.Dock = DockStyle.Fill;
		panel.Margin = new Padding(0);
		panel.Padding = new Padding(5, 3, 5, 3);
		panel.BackColor = Color.FromArgb(9, 14, 11);
		panel.Controls.Clear();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 2;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 11f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label label = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = new Padding(0),
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Color.FromArgb(188, AccentGreen),
			Font = microFont,
			Text = labelText
		};
		tableLayoutPanel.Controls.Add(label, 0, 0);
		tableLayoutPanel.Controls.Add(textBox, 0, 1);
		panel.Controls.Add(tableLayoutPanel);
		panel.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle clientRectangle = panel.ClientRectangle;
			clientRectangle.Width = Math.Max(0, clientRectangle.Width - 1);
			clientRectangle.Height = Math.Max(0, clientRectangle.Height - 1);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(8, 14, 11));
			using Pen pen = new Pen(Color.FromArgb(108, AccentGreen), 1f);
			e.Graphics.FillRectangle(brush, clientRectangle);
			e.Graphics.DrawRectangle(pen, clientRectangle);
		};
	}

	private void ConfigureTargetIpHostPanel(Panel panel, TextBox textBox, string labelText)
	{
		panel.Dock = DockStyle.Fill;
		panel.Margin = new Padding(0, 0, 0, 2);
		panel.Padding = new Padding(4, 2, 4, 3);
		panel.BackColor = Color.FromArgb(9, 14, 11);
		panel.Controls.Clear();
		panel.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle clientRectangle = panel.ClientRectangle;
			clientRectangle.Width = Math.Max(0, clientRectangle.Width - 1);
			clientRectangle.Height = Math.Max(0, clientRectangle.Height - 1);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(8, 14, 11));
			using Pen pen = new Pen(Color.FromArgb(108, AccentGreen), 1f);
			e.Graphics.FillRectangle(brush, clientRectangle);
			e.Graphics.DrawRectangle(pen, clientRectangle);
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 2;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 9f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label label = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Color.FromArgb(188, AccentGreen),
			Font = microFont,
			Text = labelText
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = Color.Transparent
		};
		textBox.Dock = DockStyle.None;
		textBox.AutoSize = false;
		textBox.Multiline = false;
		textBox.BorderStyle = BorderStyle.None;
		textBox.BackColor = Color.FromArgb(8, 14, 11);
		textBox.ForeColor = Color.WhiteSmoke;
		textBox.Font = shellFont;
		textBox.TextAlign = HorizontalAlignment.Center;
		textBox.Margin = Padding.Empty;
		textBox.Height = Math.Max(shellFont.Height + 4, 18);
		textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
		void layoutTargetTextBox()
		{
			int num = Math.Max(0, panel2.ClientSize.Width);
			int num2 = Math.Max(0, (panel2.ClientSize.Height - textBox.Height) / 2);
			textBox.SetBounds(0, num2, num, textBox.Height);
		}
		panel2.Resize += delegate
		{
			layoutTargetTextBox();
		};
		panel2.Controls.Add(textBox);
		layoutTargetTextBox();
		tableLayoutPanel.Controls.Add(label, 0, 0);
		tableLayoutPanel.Controls.Add(panel2, 0, 1);
		panel.Controls.Add(tableLayoutPanel);
	}

	private string FormatCurrentTarget()
	{
		return currentTargetIp;
	}

	private void RestoreConnectButtonIdleState()
	{
		connectButton.Text = "CONNECT";
		connectButton.ForeColor = Color.WhiteSmoke;
		connectButton.BackColor = TerminalBackground;
		connectButton.FlatAppearance.BorderColor = BorderColor;
		connectButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 46, 28);
		connectButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(34, 60, 34);
		connectButton.Enabled = true;
	}

	private string FormatStatusTarget(int maxLength = 18)
	{
		return FitStatusText(FormatCurrentTarget(), maxLength);
	}

	private void UpdateRuntimePresence(bool connected, TelemetrySnapshot? snapshot = null)
	{
		TelemetrySnapshot? telemetrySnapshot = snapshot ?? latestSnapshot;
		RuntimePresenceState.Update(new RuntimePresenceSnapshot
		{
			Connected = connected,
			DebugName = connected ? telemetrySnapshot?.DebugName : null,
			ExecutionState = connected ? telemetrySnapshot?.ExecutionState : null,
			Motherboard = connected ? telemetrySnapshot?.Motherboard : null,
			DashboardVersion = connected ? telemetrySnapshot?.DashboardVersion : null,
			Gamertag = connected ? telemetrySnapshot?.Gamertag : null,
			SignInStateText = connected ? telemetrySnapshot?.SignInStateText : null,
			TitleId = connected ? telemetrySnapshot?.TitleId : null,
			TitleName = connected ? telemetrySnapshot?.TitleName : null,
			RunningXex = connected ? telemetrySnapshot?.RunningXex : null,
			Ip = currentTargetIp,
			Port = currentTargetPort
		});
	}

	private bool IsSessionConnected(TelemetrySnapshot? snapshot = null)
	{
		return snapshot?.Connected ?? latestSnapshot?.Connected ?? !shellDisconnected;
	}

	private void SetConnectButtonConnectingState()
	{
		connectButton.Text = "CONNECTING";
		connectButton.ForeColor = Color.WhiteSmoke;
		connectButton.BackColor = Color.FromArgb(24, 54, 24);
		connectButton.FlatAppearance.BorderColor = Color.FromArgb(140, AccentGreen);
		connectButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(24, 54, 24);
		connectButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(24, 54, 24);
		connectButton.Enabled = true;
	}

	private void SetRemoteBrowserPlaceholder(string pathText, string message)
	{
		string text = NormalizeRemotePath(pathText);
		remotePathLabel.Text = FormatRemotePathLabel(text);
		remotePathTextBox.Text = text;
		remoteFileList.SetMessage(message);
	}

	private static string FormatRemotePathLabel(string path)
	{
		string text = NormalizeRemotePath(path);
		return (text == "/") ? "FTP /" : ("FTP " + text);
	}

	private static DriveInventoryEntry MergeDriveGroup(IGrouping<string, DriveInventoryEntry> group)
	{
		DriveInventoryEntry? driveInventoryEntry = group
			.Where(static entry => entry.TotalBytes.HasValue || entry.FreeBytes.HasValue)
			.OrderByDescending(static entry => entry.TotalBytes ?? 0)
			.ThenByDescending(static entry => entry.FreeBytes ?? 0)
			.FirstOrDefault();
		return new DriveInventoryEntry
		{
			Name = group.Key,
			TotalBytes = driveInventoryEntry?.TotalBytes,
			FreeBytes = driveInventoryEntry?.FreeBytes
		};
	}

	private static DriveInventoryEntry? NormalizeDriveEntry(DriveInventoryEntry entry)
	{
		if (!TryGetCanonicalDriveName(entry.Name, out string canonicalDriveName))
		{
			return null;
		}
		return new DriveInventoryEntry
		{
			Name = canonicalDriveName,
			TotalBytes = entry.TotalBytes,
			FreeBytes = entry.FreeBytes
		};
	}

	private static bool TryGetCanonicalDriveName(string? name, out string canonicalDriveName)
	{
		string text = TrimOrNull(name)?.Trim().TrimEnd(':', '\\', '/') ?? string.Empty;
		text = text.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(text))
		{
			canonicalDriveName = string.Empty;
			return false;
		}
		switch (text)
		{
		case "d":
		case "hdd":
		case "hdd1":
		case "game":
		case "devkit":
		case "internal":
			canonicalDriveName = "HDD1";
			return true;
		}
		if (text.StartsWith("usb", StringComparison.Ordinal))
		{
			for (int i = 0; i < text.Length; i++)
			{
				if (char.IsDigit(text[i]))
				{
					canonicalDriveName = "USB" + text[i];
					return true;
				}
			}
		}
		canonicalDriveName = string.Empty;
		return false;
	}

	private void UpdateDriveInventory(IEnumerable<DriveInventoryEntry> drives)
	{
		List<DriveInventoryEntry> list = drives.Where(static d => !string.IsNullOrWhiteSpace(d.Name))
			.Select(NormalizeDriveEntry)
			.Where(static entry => entry != null)
			.Select(static entry => entry!)
			.GroupBy(static d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
			.Select(MergeDriveGroup)
			.OrderBy(static d => d.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (latestSnapshot != null)
		{
			latestSnapshot.Drives.Clear();
			latestSnapshot.Drives.AddRange(list);
			if (latestSnapshot.Connected)
			{
				SetRightNetworkText(BuildConnectedStatusText(latestSnapshot));
			}
		}
		SetRightDrivesTitleText("DETECTED DRIVES");
		drivesList.SetEntries((list.Count != 0) ? list : new DriveInventoryEntry[1]
		{
			new DriveInventoryEntry
			{
				Name = TranslateTerminalText(connectAttemptInFlight ? "Connecting..." : "No live drives detected")
			}
		});
	}

	private void ResetLiveSessionUi(bool connected, string footerText, Color footerColor, string shellStatusText, Color shellStatusColor, Color shellStatusBackground, string remoteMessage)
	{
		shellDisconnected = !connected;
		SetFooterStatusText(footerText);
		footerStatusLabel.ForeColor = footerColor;
		SetPersistentConnectState(shellStatusText, shellStatusColor, shellStatusBackground);
		UpdateConnectionStatusIndicator(new TelemetrySnapshot
		{
			Connected = connected
		});
		UpdateShellScaffold(connected ? new TelemetrySnapshot
		{
			Connected = true
		} : null);
		if (!connected)
		{
			SetRightNetworkText(BuildDisconnectedStatusText());
			SetRightTempText("IDLE");
			SetRightDetailText(BuildDisconnectedSessionText());
			SetLeftConsoleText("BOARD      unknown\nDASH       --\nGAME       --\nXEX        --\nTITLEID    --\nGAMERTAG   Not Signed In");
			SetLeftSignInText("INVENTORY");
			SetFooterPresenceText("PRESENCE · OFFLINE");
			SetFooterThermalText(BuildFooterThermalText(null, null, null, null));
			UpdateDriveInventory(Array.Empty<DriveInventoryEntry>());
			latestSnapshot = null;
		}
		UpdateRuntimePresence(connected, connected ? latestSnapshot : null);
		SetRemoteBrowserPlaceholder("/", remoteMessage);
	}

	private void ApplyManualTargetFromInputs()
	{
		TryApplyManualTargetFromInputs();
	}

	private bool TryApplyManualTargetFromInputs(bool announceChange = true, bool announceUnchanged = true)
	{
		string text = TrimOrNull(targetIpTextBox.Text) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			AppendSystemLine("Target IP or host is required.", WarningColor);
			targetIpTextBox.Focus();
			return false;
		}
		int result = currentTargetPort > 0 ? currentTargetPort : 730;
		if (string.Equals(currentTargetIp, text, StringComparison.OrdinalIgnoreCase) && currentTargetPort == result)
		{
			if (announceUnchanged)
			{
				AppendSystemLine("Target already set to " + FormatCurrentTarget() + ".", AccentDim);
			}
			return true;
		}
		CancelAndDispose(ref remoteBrowseCts);
		CancelAndDispose(ref connectProbeCts);
		CancelAndDispose(ref fileTransferCts);
		telemetryTimer.Stop();
		connectAttemptInFlight = false;
		Interlocked.Increment(ref sessionEpoch);
		currentTargetIp = text;
		currentTargetPort = result;
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.DefaultIp = currentTargetIp;
		cliConfig.DefaultPort = currentTargetPort;
		cliConfig.Save();
		latestSnapshot = null;
		remoteEntries.Clear();
		remoteCurrentPath = "/";
		nextRemoteRefreshAllowedUtc = DateTime.MinValue;
		UpdateRuntimePresence(connected: false);
		RestoreConnectButtonIdleState();
		SetCommandInputPlaceholder("Connect or enter a XeCLI command for this session...");
		ResetLiveSessionUi(connected: false, "TARGET READY", AccentCyan, "LINK OFFLINE", WarningColor, Color.FromArgb(24, 19, 15), "Disconnected");
		if (announceChange)
		{
			AppendSystemLine("Target updated to " + FormatCurrentTarget() + ".", AccentGreen);
		}
		return true;
	}

	private async Task PrimeRemoteBrowserAfterConnectAsync()
	{
		try
		{
			if (fileTransferInFlight)
			{
				return;
			}
			await RefreshRemoteBrowserAsync(force: true);
			if (!base.IsDisposed && !shellDisconnected && !connectAttemptInFlight && !fileTransferInFlight && remoteEntries.Count == 0)
			{
				await Task.Delay(900, formLifetimeCts.Token);
				await RefreshRemoteBrowserAsync(force: true);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
		}
	}

	private void ConfigureToggleButton(Button button, string text)
	{
		button.Dock = DockStyle.Fill;
		button.MinimumSize = new Size(0, 24);
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderColor = BorderColor;
		button.FlatAppearance.MouseOverBackColor = Color.FromArgb(22, 36, 22);
		button.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 46, 28);
		button.BackColor = TerminalBackground;
		button.ForeColor = AccentDim;
		button.Font = shellFontBold;
		button.Text = text;
		button.TextAlign = ContentAlignment.MiddleCenter;
		button.Cursor = Cursors.Hand;
	}

	private void RefreshInventoryHeader()
	{
		inventoryList.ModuleView = inventoryShowsModules;
		pluginsTabButton.ForeColor = (!inventoryShowsModules ? AccentCyan : AccentDim);
		modulesTabButton.ForeColor = (inventoryShowsModules ? AccentCyan : AccentDim);
	}

	private void RefreshInventoryListFromSnapshot()
	{
		int num = 0;
		if (connectAttemptInFlight)
		{
			SetLeftSignInText("INVENTORY");
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText(inventoryShowsModules ? "Connecting to live modules..." : "Connecting to live plugins...")
			});
			return;
		}
		if (latestSnapshot == null)
		{
			SetLeftSignInText("INVENTORY");
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText(inventoryShowsModules ? "Connect for Modules" : "Connect for Plugins")
			});
			return;
		}
		IEnumerable<string> enumerable = inventoryShowsModules ? latestSnapshot.Modules : latestSnapshot.Plugins;
		num = enumerable.Count();
		SetLeftSignInText("INVENTORY");
		if (!enumerable.Any())
		{
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText(inventoryShowsModules ? (latestSnapshot.Connected ? "No live modules detected" : "No cached modules loaded") : (latestSnapshot.Connected ? "No live plugins detected" : "No cached plugins loaded"))
			});
			return;
		}
		inventoryList.SetItems(FormatInventoryEntries(enumerable).Take(256));
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
		string text = AbbreviateTransferCommand(command);
		TransferQueueEntry transferQueueEntry = transferQueueEntries.FirstOrDefault((TransferQueueEntry item) => string.Equals(item.CommandKey, text, StringComparison.Ordinal));
		if (transferQueueEntry == null)
		{
			transferQueueEntry = new TransferQueueEntry
			{
				CommandKey = text,
				CommandText = text,
				State = state,
				UpdatedAtLocal = DateTime.Now
			};
			transferQueueEntries.Insert(0, transferQueueEntry);
		}
		else
		{
			transferQueueEntries.Remove(transferQueueEntry);
			transferQueueEntries.Insert(0, transferQueueEntry);
		}
		transferQueueEntry.CommandText = text;
		transferQueueEntry.State = state;
		transferQueueEntry.UpdatedAtLocal = DateTime.Now;
		if (!string.Equals(state, "running", StringComparison.OrdinalIgnoreCase) && !string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase))
		{
			transferQueueEntry.ProgressPercent = string.Equals(state, "complete", StringComparison.OrdinalIgnoreCase) ? 100.0 : null;
		}
		if (transferQueueEntries.Count > 10)
		{
			transferQueueEntries.RemoveRange(10, transferQueueEntries.Count - 10);
		}
		RefreshTransferQueueDisplay();
	}

	private void UpdateActiveTransferProgress(double? percent, string? detail = null)
	{
		string text = activeTransferCommand;
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		if (InvokeRequired)
		{
			BeginInvoke(new Action<double?, string?>(UpdateActiveTransferProgress), percent, detail);
			return;
		}
		string text2 = AbbreviateTransferCommand(text);
		TransferQueueEntry transferQueueEntry = transferQueueEntries.FirstOrDefault((TransferQueueEntry item) => string.Equals(item.CommandKey, text2, StringComparison.Ordinal));
		if (transferQueueEntry == null)
		{
			transferQueueEntry = new TransferQueueEntry
			{
				CommandKey = text2,
				CommandText = text2,
				State = "running",
				UpdatedAtLocal = DateTime.Now
			};
			transferQueueEntries.Insert(0, transferQueueEntry);
		}
		transferQueueEntry.State = "running";
		transferQueueEntry.ProgressPercent = percent;
		transferQueueEntry.Detail = detail;
		transferQueueEntry.UpdatedAtLocal = DateTime.Now;
		RefreshTransferQueueDisplay();
	}

	private void HandleFtpProgress(FtpProgress progress, string detail)
	{
		double? nullable = null;
		if (progress.Progress >= 0.0 && progress.Progress <= 100.0)
		{
			nullable = progress.Progress;
		}
		UpdateActiveTransferProgress(nullable, detail);
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

	private void LoadApplicationIcon()
	{
		try
		{
			string? processPath = Environment.ProcessPath;
			if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
			{
				Icon? associatedIcon = Icon.ExtractAssociatedIcon(processPath);
				if (associatedIcon != null)
				{
					base.Icon = associatedIcon;
					return;
				}
			}
			string text = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
			if (File.Exists(text))
			{
				base.Icon = new Icon(text);
			}
		}
		catch
		{
		}
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
		if (e.KeyCode == Keys.Up && suggestionList.Visible && suggestionList.ItemCount > 0)
		{
			suggestionList.MoveSelection(-1);
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Down && suggestionList.Visible && suggestionList.ItemCount > 0)
		{
			suggestionList.MoveSelection(1);
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
		suppressSuggestionRefresh = true;
		commandInput.Clear();
		suppressSuggestionRefresh = false;
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
		List<string> list = XeCliSuggestions.Where((string s) => s.StartsWith(text, StringComparison.OrdinalIgnoreCase)).Take(18).ToList();
		if (list.Count == 0 && text.Length >= 3)
		{
			list = XeCliSuggestions.Where((string s) => s.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(18).ToList();
		}
		suggestionList.SetItems(list);
		bool visible = list.Count > 0;
		if (visible && suggestionList.SelectedIndex != 0)
		{
			suggestionList.SelectedIndex = 0;
		}
		ToggleSuggestions(visible);
	}

	private void ToggleSuggestions(bool visible)
	{
		float num = visible ? suggestionList.PreferredHostHeight : 0f;
		suggestionHost.SuspendLayout();
		suggestionList.Visible = visible;
		suggestionHost.Visible = visible;
		suggestionHost.BackColor = TerminalBackground;
		suggestionHost.Padding = (visible ? new Padding(1) : Padding.Empty);
		suggestionList.Height = Math.Max(0, (int)num - suggestionHost.Padding.Vertical);
		if (suggestionRowStyle != null)
		{
			if (Math.Abs(suggestionRowStyle.Height - num) > float.Epsilon)
			{
				suggestionRowStyle.Height = num;
			}
		}
		suggestionHost.ResumeLayout();
	}

	private void HideSuggestionsIfInputInactive()
	{
		if (base.IsDisposed)
		{
			return;
		}
		BeginInvoke((Action)delegate
		{
			if (!base.IsDisposed && !commandInput.Focused && !suggestionList.Focused && !commandInputHost.ContainsFocus)
			{
				ToggleSuggestions(visible: false);
			}
		});
	}

	private void ApplySelectedSuggestion()
	{
		if (!suggestionList.Visible || suggestionList.SelectedItem == null)
		{
			RefreshSuggestions();
		}
		if (suggestionList.SelectedItem is string text)
		{
			suppressSuggestionRefresh = true;
			commandInput.Text = text;
			commandInput.SelectionStart = commandInput.Text.Length;
			suppressSuggestionRefresh = false;
			commandInput.Focus();
			ToggleSuggestions(visible: false);
		}
	}

	private void SetRemotePaneMode(bool showQueue)
	{
		remotePaneShowsQueue = showQueue;
		ftpTabButton.ForeColor = (showQueue ? AccentDim : AccentCyan);
		queueTabButton.ForeColor = (showQueue ? AccentCyan : AccentDim);
		remoteFileList.Visible = !showQueue;
		transferQueueList.Visible = showQueue;
		remotePathHostPanel.Visible = !showQueue;
		remoteUpButton.Visible = !showQueue;
		remotePathLabel.Text = showQueue ? "QUEUE / TRANSFERS" : FormatRemotePathLabel(remoteCurrentPath);
		if (showQueue)
		{
			RefreshTransferQueueDisplay();
		}
	}

	private async Task ApplyLocalPathAsync()
	{
		string? text = TrimOrNull(localPathTextBox.Text);
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "This PC", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "PC :: This PC", StringComparison.OrdinalIgnoreCase))
		{
			localCurrentPath = null;
			await RefreshLocalBrowserAsync();
			return;
		}
		string text2 = Environment.ExpandEnvironmentVariables(text);
		if (File.Exists(text2))
		{
			text2 = Path.GetDirectoryName(text2) ?? text2;
		}
		if (!Directory.Exists(text2))
		{
			AppendSystemLine("Local path does not exist: " + text2, WarningColor);
			localPathTextBox.Focus();
			localPathTextBox.SelectAll();
			return;
		}
		localCurrentPath = text2;
		await RefreshLocalBrowserAsync();
	}

	private async Task ApplyRemotePathAsync()
	{
		if (remotePaneShowsQueue)
		{
			return;
		}
		string text = TrimOrNull(remotePathTextBox.Text) ?? "/";
		remoteCurrentPath = NormalizeRemotePath(text);
		await RefreshRemoteBrowserAsync(force: true);
	}

	private void RefreshTransferQueueDisplay()
	{
		transferQueueList.SetEntries(transferQueueEntries);
	}

	private void AppendTransferCompletionLines(List<(string RemotePath, string LocalPath)> completedTransfers, string defaultLocalDirectory)
	{
		if (completedTransfers.Count == 0)
		{
			AppendSystemLine("Saved to: " + defaultLocalDirectory, AccentGreen);
			return;
		}
		if (completedTransfers.Count == 1)
		{
			(string RemotePath, string LocalPath) valueTuple = completedTransfers[0];
			AppendSystemLine("Downloaded from: " + valueTuple.RemotePath, AccentDim);
			AppendSystemLine("Saved to: " + valueTuple.LocalPath, AccentGreen);
			return;
		}
		AppendSystemLine("Downloaded " + completedTransfers.Count + " items.", AccentGreen);
		AppendSystemLine("Saved to: " + defaultLocalDirectory, AccentGreen);
	}

	private void ClearTerminalWorkspace()
	{
		lock (pendingTerminalLock)
		{
			pendingTerminalLines.Clear();
			terminalFlushScheduled = false;
		}
		terminalFlushTimer.Stop();
		if (!terminalOutput.IsDisposed)
		{
			terminalOutput.Clear();
		}
	}

	private async Task<bool> ExecuteCommandAsync(string rawCommand)
	{
		if (commandInFlight)
		{
			AppendSystemLine("A command is already running. Wait for completion.", Color.Gold);
			return false;
		}
		string command = rawCommand.Trim();
		if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) || command.Equals("quit", StringComparison.OrdinalIgnoreCase))
		{
			Close();
			return true;
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
			return true;
		}
		commandInFlight = true;
		RecordTransferActivity(command, "queued");
		AppendCommandLine(command);
		bool flag = false;
		try
		{
			flag = await RunCliProcessAsync(command);
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
		return flag;
	}

	private async Task ExecuteScreenshotCaptureAsync()
	{
		string text = GetDefaultScreenshotPath();
		Directory.CreateDirectory(Path.GetDirectoryName(text) ?? Environment.CurrentDirectory);
		bool flag = await ExecuteCommandAsync("screenshot --out \"" + text + "\"");
		if (flag && File.Exists(text))
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = text,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				AppendSystemLine("Screenshot saved to: " + text, AccentGreen);
				AppendSystemLine("Open failed: " + ex.Message, Color.Gold);
			}
		}
	}

	private string GetDefaultScreenshotPath()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		}
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			folderPath = Environment.CurrentDirectory;
		}
		string text = Path.Combine(folderPath, "XeCLI", "Captures");
		string text2 = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		string text3 = BuildScreenshotTitleStem();
		return Path.Combine(text, text3 + "-" + text2 + ".png");
	}

	private string BuildScreenshotTitleStem()
	{
		string text = TrimOrNull(latestSnapshot?.TitleName) ?? TrimOrNull(latestSnapshot?.RunningXex) ?? "Screenshot";
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0)
			{
				continue;
			}
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
			}
			else if (char.IsWhiteSpace(c) || c == '-' || c == '_')
			{
				if (stringBuilder.Length > 0 && stringBuilder[stringBuilder.Length - 1] != '_')
				{
					stringBuilder.Append('_');
				}
			}
		}
		string text2 = stringBuilder.ToString().Trim('_');
		return string.IsNullOrWhiteSpace(text2) ? "Screenshot" : text2;
	}

	private async Task<bool> RunCliProcessAsync(string command)
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
		processStartInfo.Environment["XECLI_LANG"] = GetConfiguredUiLanguageCode();
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
				AppendPromptLine();
				return false;
			}
			RecordTransferActivity(command, "complete");
			AppendPromptLine();
			return true;
		}
		catch (OperationCanceledException)
		{
			RecordTransferActivity(command, "cancelled");
			AppendSystemLine("Command cancelled.", AccentPink);
			AppendPromptLine();
			return false;
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
		AppendTerminalLine("$Xe> " + command, AccentGreen);
	}

	private void AppendPromptLine()
	{
		AppendTerminalLine("$Xe>", Color.FromArgb(214, AccentGreen));
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
		if (!options.TelemetryEnabled || telemetryPollInFlight || shellDisconnected || base.IsDisposed || commandInFlight || connectAttemptInFlight || fileTransferInFlight)
		{
			return;
		}
		int num = Volatile.Read(ref sessionEpoch);
		telemetryPollInFlight = true;
		try
		{
			TelemetrySnapshot telemetrySnapshot = await Task.Run(() => TryReadTelemetryAsync(forceHeavyRefresh), formLifetimeCts.Token);
			if (base.IsDisposed || num != Volatile.Read(ref sessionEpoch))
			{
				return;
			}
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

	private async Task<bool> TryProbeConnectionAsync(CancellationToken externalCancellationToken)
	{
		int num = Math.Clamp(options.TimeoutMs, 3000, 10000);
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token, externalCancellationToken);
		cancellationTokenSource.CancelAfter(num);
		XbdmConnectionOptions xbdmConnectionOptions = new XbdmConnectionOptions
		{
			Host = currentTargetIp,
			Port = currentTargetPort,
			TimeoutMs = num
		};
		Task<XbdmClient> task = XbdmClient.ConnectAsync(xbdmConnectionOptions, cancellationTokenSource.Token);
		Task task2 = await Task.WhenAny(task, Task.Delay(num, formLifetimeCts.Token));
		if (task2 != task)
		{
			cancellationTokenSource.Cancel();
			return false;
		}
		using XbdmClient xbdmClient = await task;
		return xbdmClient != null;
	}

	private async Task HydrateConnectedSessionAsync(int expectedSessionEpoch)
	{
		if (base.IsDisposed || shellDisconnected || expectedSessionEpoch != Volatile.Read(ref sessionEpoch))
		{
			return;
		}
		if (!options.TelemetryEnabled)
		{
			_ = PrimeRemoteBrowserAfterConnectAsync();
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
		if (!base.IsDisposed && !shellDisconnected && expectedSessionEpoch == Volatile.Read(ref sessionEpoch) && latestSnapshot != null && latestSnapshot.Connected)
		{
			_ = PrimeRemoteBrowserAfterConnectAsync();
		}
	}

	private async Task<TelemetrySnapshot> TryReadTelemetryAsync(bool forceHeavyRefresh)
	{
		CliConfig cliConfig = CliConfig.Load();
		bool flag = forceHeavyRefresh;
		TelemetrySnapshot telemetrySnapshot = new TelemetrySnapshot
		{
			SessionEpoch = Volatile.Read(ref sessionEpoch),
			Connected = false,
			FtpPort = cliConfig.DefaultFtpPort ?? 21,
			FtpUser = TrimOrNull(cliConfig.DefaultFtpUser) ?? "xbox"
		};
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		int telemetryTimeoutMs = Math.Clamp(options.TimeoutMs * (flag ? 2 : 1), 1200, flag ? 9000 : 6000);
		cancellationTokenSource.CancelAfter(telemetryTimeoutMs);
		try
		{
			XbdmConnectionOptions xbdmConnectionOptions = new XbdmConnectionOptions
			{
				Host = currentTargetIp,
				Port = currentTargetPort,
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
			CancellationToken cancellationToken = cancellationTokenSource.Token;
			ProfileHelpers.ResolvedIdentityInfo resolvedIdentity = await ProfileHelpers.ResolveSignedInIdentityAsync(client, currentTargetIp, currentTargetPort, options.TimeoutMs, cliConfig, allowF3: true, allowProfilePackage: flag, cancellationToken);
			Jrpc2Client jrpc2Client = new Jrpc2Client(client);
			try
			{
				telemetrySnapshot.TitleId = await jrpc2Client.GetTitleIdAsync(cancellationToken);
			}
			catch
			{
			}
			telemetrySnapshot.Gamertag = TrimOrNull(resolvedIdentity.Gamertag);
			telemetrySnapshot.SignInStateText = resolvedIdentity.IsSignedIn ? resolvedIdentity.SignInStateText : "not detected";
			telemetrySnapshot.CpuTemp = await TryGetTempAsync(jrpc2Client, SensorType.CPU, cancellationToken);
			telemetrySnapshot.GpuTemp = await TryGetTempAsync(jrpc2Client, SensorType.GPU, cancellationToken);
			telemetrySnapshot.EdramTemp = await TryGetTempAsync(jrpc2Client, SensorType.EDRAM, cancellationToken);
			telemetrySnapshot.BoardTemp = await TryGetTempAsync(jrpc2Client, SensorType.MotherBoard, cancellationToken);
			telemetrySnapshot.DashboardVersion = await TryGetDashboardAsync(jrpc2Client, cancellationToken);
			telemetrySnapshot.Motherboard = await TryGetMotherboardAsync(jrpc2Client, cancellationToken);
			if (telemetrySnapshot.TitleId.HasValue && TitleIdDatabase.Instance.TryResolve(telemetrySnapshot.TitleId.Value, null, out TitleIdEntry titleEntry))
			{
				telemetrySnapshot.TitleName = titleEntry?.Name;
			}
			telemetrySnapshot.TitleName = ProfileHelpers.TryGetTitleFallbackName(telemetrySnapshot.TitleId, telemetrySnapshot.RunningXex, telemetrySnapshot.TitleName);
			try
			{
				if (flag)
				{
					foreach (XbdmDriveEntry item in await client.GetDrivesAsync(includeSize: true, cancellationTokenSource.Token))
					{
						string text2 = TrimOrNull(item.Name);
						if (!string.IsNullOrWhiteSpace(text2) && ShouldDisplayDriveName(text2))
						{
							DriveInventoryEntry? driveInventoryEntry = NormalizeDriveEntry(new DriveInventoryEntry
							{
								Name = text2,
								TotalBytes = item.TotalBytes,
								FreeBytes = item.FreeBytes
							});
							if (driveInventoryEntry != null)
							{
								telemetrySnapshot.Drives.Add(driveInventoryEntry);
							}
						}
					}
					if (telemetrySnapshot.Drives.Count == 0)
					{
						telemetrySnapshot.Drives.AddRange((await TryGetFtpDriveRootsAsync(cliConfig, cancellationTokenSource.Token)).Select(static name => new DriveInventoryEntry
						{
							Name = name
						}));
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
			if (telemetrySnapshot.Drives.Count > 0)
			{
				List<DriveInventoryEntry> list = telemetrySnapshot.Drives
					.GroupBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
					.Select(MergeDriveGroup)
					.OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
					.ToList();
				telemetrySnapshot.Drives.Clear();
				telemetrySnapshot.Drives.AddRange(list);
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
					PluginHelpers.PluginConfig pluginConfig = await PluginHelpers.LoadAsync(currentTargetIp, num, text4, text5, options.TimeoutMs, null);
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
		return TryGetCanonicalDriveName(name, out _);
	}

	private async Task<List<string>> TryGetFtpDriveRootsAsync(CliConfig cliConfig, CancellationToken cancellationToken)
	{
		List<string> list = new List<string>();
		try
		{
			int num = cliConfig.DefaultFtpPort ?? 21;
			string user = TrimOrNull(cliConfig.DefaultFtpUser) ?? "xbox";
			string pass = cliConfig.DefaultFtpPassword ?? "xbox";
			int timeoutMs = Math.Clamp(options.TimeoutMs, 1200, 7000);
			await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, num, user, pass, timeoutMs);
			await asyncFtpClient.Connect(cancellationToken);
			(FtpListItem[] Item1, bool Item2) tuple = await FtpHelpers.GetListingWithFallbackAsync(asyncFtpClient, "/");
			foreach (FtpListItem item in tuple.Item1)
			{
				if (item.Type == FtpObjectType.File || string.IsNullOrWhiteSpace(item.Name))
				{
					continue;
				}
				string text = item.Name.Trim().TrimEnd(':');
				if (TryGetCanonicalDriveName(text, out string canonicalDriveName) && !list.Any((string existing) => existing.Equals(canonicalDriveName, StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(canonicalDriveName);
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private (double RxKbps, double TxKbps) SampleFtpTraffic(int ftpPort)
	{
		bool flag = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any((TcpConnectionInformation c) => c.RemoteEndPoint.Address.ToString().Equals(currentTargetIp, StringComparison.OrdinalIgnoreCase) && c.RemoteEndPoint.Port == ftpPort && c.State == TcpState.Established);
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
		if (snapshot.Connected && latestSnapshot?.Connected == true)
		{
			if (string.IsNullOrWhiteSpace(TrimOrNull(snapshot.Gamertag)) && !string.IsNullOrWhiteSpace(TrimOrNull(latestSnapshot.Gamertag)))
			{
				snapshot.Gamertag = latestSnapshot.Gamertag;
			}
			if ((string.IsNullOrWhiteSpace(TrimOrNull(snapshot.SignInStateText)) || string.Equals(snapshot.SignInStateText, "not detected", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(TrimOrNull(latestSnapshot.SignInStateText)))
			{
				snapshot.SignInStateText = latestSnapshot.SignInStateText;
			}
			if (snapshot.Drives.Count == 0 && latestSnapshot.Drives.Count != 0)
			{
				snapshot.Drives.AddRange(latestSnapshot.Drives);
			}
			snapshot.CpuTemp ??= latestSnapshot.CpuTemp;
			snapshot.GpuTemp ??= latestSnapshot.GpuTemp;
			snapshot.EdramTemp ??= latestSnapshot.EdramTemp;
			snapshot.BoardTemp ??= latestSnapshot.BoardTemp;
			snapshot.DashboardVersion ??= latestSnapshot.DashboardVersion;
			if ((string.IsNullOrWhiteSpace(TrimOrNull(snapshot.Motherboard)) || string.Equals(snapshot.Motherboard, "unknown", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(TrimOrNull(latestSnapshot.Motherboard)))
			{
				snapshot.Motherboard = latestSnapshot.Motherboard;
			}
		}
		string text = snapshot.Connected ? "ONLINE" : "OFFLINE";
		string text3 = TrimOrNull(snapshot.Motherboard) ?? "unknown";
		string text4 = (snapshot.DashboardVersion.HasValue ? snapshot.DashboardVersion.Value.ToString() : "unknown");
		string text8 = TrimOrNull(snapshot.TitleName) ?? "unknown";
		string text9 = TrimOrNull(snapshot.RunningXex) ?? "unknown";
		string text5 = TrimOrNull(snapshot.Gamertag) ?? "Not Signed In";
		string text6 = NormalizePresenceStateText(snapshot.SignInStateText);
		SetLeftConsoleText("BOARD      " + FitStatusText(text3, 16) + "\nDASH       " + FitStatusText(text4, 16) + "\nGAME       " + FitStatusText(text8, 16) + "\nXEX        " + FitFileLeaf(text9, 16) + "\nTITLEID    " + FormatTitleId(snapshot.TitleId) + "\nGAMERTAG   " + FitStatusText(text5, 16));
		SetLeftSignInText("INVENTORY");
		string value = BuildConnectedStatusText(snapshot);
		if (!snapshot.Connected)
		{
			SetRightNetworkText(BuildDisconnectedStatusText());
			SetRightTempText("IDLE");
			SetRightDetailText(BuildDisconnectedSessionText());
		}
		else
		{
			SetRightNetworkText(value);
			SetRightTempText("RX " + snapshot.FtpRxKbps.ToString("0.0") + " kbps  |  TX " + snapshot.FtpTxKbps.ToString("0.0") + " kbps");
			SetRightDetailText("TITLE      " + FitStatusText(text8, 22) + "\nXEX        " + FitFileLeaf(text9, 22) + "\nUSER       " + FitStatusText(text5, 22) + "\nPRESENCE   " + FitStatusText(text6, 22) + "\nTITLEID    " + FormatTitleId(snapshot.TitleId));
		}
		ftpTrafficGraph.AddSample(snapshot.FtpRxKbps, snapshot.FtpTxKbps);
		shellDisconnected = !snapshot.Connected;
		latestSnapshot = snapshot;
		UpdateRuntimePresence(snapshot.Connected, snapshot);
		UpdateDriveInventory(snapshot.Drives);
		RefreshInventoryListFromSnapshot();
		if (!snapshot.Connected)
		{
			SetFooterStatusText("DISCONNECTED");
			footerStatusLabel.ForeColor = WarningColor;
			SetFooterPresenceText("PRESENCE · OFFLINE");
		}
		else
		{
			SetFooterStatusText("CONNECTED");
			footerStatusLabel.ForeColor = AccentGreen;
			SetFooterPresenceText(string.IsNullOrWhiteSpace(text5) || text5 == "--" ? "PRESENCE · " + text6 : (text5 + " · " + text6));
		}
		SetFooterThermalText(BuildFooterThermalText(snapshot.CpuTemp, snapshot.GpuTemp, snapshot.EdramTemp, snapshot.BoardTemp));
		if (!connectAttemptInFlight)
		{
			SetPersistentConnectState(snapshot.Connected ? "LINK ACTIVE" : "LINK OFFLINE", snapshot.Connected ? AccentGreen : WarningColor, snapshot.Connected ? Color.FromArgb(18, 34, 32) : Color.FromArgb(24, 19, 15));
		}
		UpdateConnectionStatusIndicator(snapshot);
		UpdateShellScaffold(snapshot);
		UpdateLiveHints(snapshot);
	}

	private void UpdateLiveHints(TelemetrySnapshot? snapshot = null)
	{
		bool flag = IsSessionConnected(snapshot);
		SetFooterStatusText(connectAttemptInFlight ? "CONNECTING" : (flag ? "CONNECTED" : "DISCONNECTED"));
		footerStatusLabel.ForeColor = (connectAttemptInFlight ? AccentGreen : (flag ? AccentGreen : WarningColor));
	}

	private void UpdateConnectionStatusIndicator(TelemetrySnapshot? snapshot = null)
	{
		bool flag = IsSessionConnected(snapshot);
		if (connectAttemptInFlight)
		{
			SetConnectionStatusText("◐ CONNECTING");
			connectionStatusLabel.ForeColor = WarningColor;
			return;
		}
		if (flag)
		{
			SetConnectionStatusText("● CONNECTED");
			connectionStatusLabel.ForeColor = AccentGreen;
			return;
		}
		SetConnectionStatusText("● DISCONNECTED");
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
		return label.PadRight(5) + " " + FormatTemp(value);
	}

	private static string BuildThermalBlock(uint? cpu, uint? gpu, uint? edram, uint? board)
	{
		return "THERMALS\n" + FormatThermalRow("CPU", cpu) + "\n" + FormatThermalRow("GPU", gpu) + "\n" + FormatThermalRow("EDRAM", edram) + "\n" + FormatThermalRow("BOARD", board);
	}

	private static string BuildFooterThermalText(uint? cpu, uint? gpu, uint? edram, uint? board)
	{
		return "CPU " + FormatTemp(cpu) + " · GPU " + FormatTemp(gpu) + " · RAM " + FormatTemp(edram) + " · BOARD " + FormatTemp(board);
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
		return "LINK      OFFLINE\nTARGET    " + FormatStatusTarget() + "\nFTP       idle\nPRESENCE  offline";
	}

	private string BuildConnectedStatusText(TelemetrySnapshot snapshot)
	{
		string text = snapshot.Connected ? "ONLINE" : "OFFLINE";
		string text3 = snapshot.FtpPort.HasValue ? snapshot.FtpPort.Value.ToString() : "--";
		string text4 = TrimOrNull(snapshot.FtpUser) ?? "xbox";
		string text5 = TrimOrNull(snapshot.ErrorText);
		string text6 = "LINK      " + text + "\nTARGET    " + FormatStatusTarget() + "\nFTP       " + text4 + "@" + text3 + "\nDRIVES    " + snapshot.Drives.Count + "\nPRESENCE  " + FitStatusText(NormalizePresenceStateText(snapshot.SignInStateText), 12);
		if (!string.IsNullOrWhiteSpace(text5))
		{
			text6 = "LINK      " + text + "\nTARGET    " + FormatStatusTarget() + "\nFTP       " + text4 + "@" + text3 + "\nALERT     " + FitStatusText(text5, 12) + "\nDRIVES    " + snapshot.Drives.Count;
		}
		return text6;
	}

	private static string BuildDisconnectedSessionText()
	{
		return "No live console context.\nConnect to load title, XEX,\nuser, presence, and TitleID.";
	}

	private static string BuildConnectingSessionText()
	{
		return "CONNECTING SESSION\nNegotiating console link.\nLoading title, XEX, user,\npresence, and TitleID...";
	}

	private static string BuildDisconnectedShellText()
	{
		return "DISCONNECTED SESSION\r\nUse CONNECT to load thermals, FTP, title, and user state.\r\nLocal XeCLI commands remain available below.";
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
			string text = FitStatusText(TrimOrNull(telemetrySnapshot.TitleName) ?? "unknown", 30);
			string text2 = FitStatusText(FormatExecutionState(telemetrySnapshot.ExecutionState), 16);
			string text3 = FitStatusText(TrimOrNull(telemetrySnapshot.Gamertag) ?? "--", 16);
			SetLabelText(shellScaffoldLabel, "LIVE SESSION ONLINE\r\nTitle: " + text + "\r\nState: " + text2 + "\r\nUser: " + text3);
			return;
		}
		SetLabelText(shellScaffoldLabel, BuildDisconnectedShellText());
	}

	private static string FormatExecutionState(string? value)
	{
		string text = TrimOrNull(value) ?? "unknown";
		return string.Equals(text, "start", StringComparison.OrdinalIgnoreCase) ? "running" : text;
	}

	private static IEnumerable<string> FormatInventoryEntries(IEnumerable<string> source)
	{
		int num = 1;
		foreach (string item in source)
		{
			string text = TrimOrNull(item) ?? string.Empty;
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			yield return num.ToString(CultureInfo.InvariantCulture) + ". " + StripInventoryPrefix(text);
			num++;
		}
	}

	private static string StripInventoryPrefix(string value)
	{
		int num = value.IndexOf(':');
		if (num > 0)
		{
			string text = value.Substring(0, num).Trim();
			if (text.StartsWith("plugin", StringComparison.OrdinalIgnoreCase) || text.StartsWith("module", StringComparison.OrdinalIgnoreCase))
			{
				string text2 = TrimOrNull(value.Substring(num + 1)) ?? string.Empty;
				if (!string.IsNullOrEmpty(text2))
				{
					return text2;
				}
			}
		}
		return value;
	}

	private static string NormalizePresenceStateText(string? value)
	{
		string text = TrimOrNull(value) ?? string.Empty;
		if (string.IsNullOrEmpty(text))
		{
			return "--";
		}
		if (string.Equals(text, "not detected", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "not signed in", StringComparison.OrdinalIgnoreCase))
		{
			return "Not Signed In";
		}
		if (text.IndexOf("signed", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("not", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return "Signed In";
		}
		return text;
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

	[DllImport("user32.dll")]
	private static extern bool HideCaret(IntPtr hWnd);

	private void HideTerminalOutputCaret()
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return;
		}
		HideCaret(terminalOutput.Handle);
	}

	private sealed class StructuredInfoPanel : Control
	{
		private readonly List<(string Key, string Value)> rows = new List<(string, string)>();

		public StructuredInfoPanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
		}

		public void SetContent(string text)
		{
			List<(string Key, string Value)> list = ParseRows(text).ToList();
			if (rows.SequenceEqual(list))
			{
				return;
			}
			rows.Clear();
			rows.AddRange(list);
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
			e.Graphics.Clear(BackColor);
			if (rows.Count == 0)
			{
				return;
			}
			int availableHeight = Math.Max(1, Height - 4);
			int rowHeight = Math.Max(14, Math.Min(20, availableHeight / Math.Max(1, rows.Count)));
			int topInset = 2 + Math.Max(0, (availableHeight - rowHeight * rows.Count) / 2);
			float keyFontSize = Math.Max(6f, Math.Min(Font.Size - 0.45f, rowHeight - 5.8f));
			float valueFontSize = Math.Max(6f, Math.Min(Font.Size - 0.15f, rowHeight - 5.4f));
			using Font keyFont = new Font(Font.FontFamily, keyFontSize, FontStyle.Bold, GraphicsUnit.Point);
			using Font valueFont = new Font(Font.FontFamily, valueFontSize, FontStyle.Regular, GraphicsUnit.Point);
			int keyWidth = CalculateKeyWidth(e.Graphics, keyFont);
			int valueGap = 12;
			for (int i = 0; i < rows.Count; i++)
			{
				int y = topInset + i * rowHeight;
				Rectangle rowRect = new Rectangle(1, y, Math.Max(1, Width - 2), Math.Max(1, rowHeight - 1));
				using (SolidBrush brush = new SolidBrush((i % 2 == 0) ? Color.FromArgb(18, 24, 18) : Color.FromArgb(28, 34, 24)))
				{
					e.Graphics.FillRectangle(brush, rowRect);
				}
				Rectangle keyRect = new Rectangle(rowRect.Left + 8, rowRect.Top, Math.Max(1, keyWidth - 10), rowRect.Height);
				Rectangle valueRect = new Rectangle(rowRect.Left + keyWidth + valueGap, rowRect.Top, Math.Max(1, rowRect.Width - keyWidth - valueGap - 8), rowRect.Height);
				DrawLeftFittedText(e.Graphics, rows[i].Key, keyFont, keyRect, AccentGreen);
				DrawLeftFittedText(e.Graphics, rows[i].Value, valueFont, valueRect, Color.WhiteSmoke);
			}
		}

		private int CalculateKeyWidth(Graphics graphics, Font font)
		{
			int num = 56;
			foreach ((string Key, string _) in rows)
			{
				Size size = TextRenderer.MeasureText(graphics, Key, font, new Size(int.MaxValue, Math.Max(16, font.Height + 1)), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
				num = Math.Max(num, size.Width + 10);
			}
			return Math.Min(Math.Max(56, num), Math.Max(64, Width / 3));
		}

		private static void DrawLeftFittedText(Graphics graphics, string text, Font baseFont, Rectangle bounds, Color color)
		{
			if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 4 || bounds.Height <= 2)
			{
				return;
			}
			Font? font = null;
			try
			{
				float num = baseFont.Size;
				while (num >= 5.75f)
				{
					font?.Dispose();
					font = new Font(baseFont.FontFamily, num, baseFont.Style, GraphicsUnit.Point);
					Size size = TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, bounds.Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
					if (size.Width <= bounds.Width - 2)
					{
						break;
					}
					num -= 0.25f;
				}
				TextRenderer.DrawText(graphics, text, font ?? baseFont, bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
			}
			finally
			{
				font?.Dispose();
			}
		}

		private static IEnumerable<(string Key, string Value)> ParseRows(string text)
		{
			foreach (string item in text.Replace("\r", string.Empty).Split('\n'))
			{
				string text2 = TrimOrNull(item) ?? string.Empty;
				if (string.IsNullOrWhiteSpace(text2))
				{
					continue;
				}
				int num = FindColumnSplit(text2);
				if (num > 0)
				{
					string text3 = text2.Substring(0, num).Trim();
					string text4 = TrimOrNull(text2.Substring(num)) ?? "--";
					if (!string.IsNullOrWhiteSpace(text3))
					{
						yield return (text3, text4);
						continue;
					}
				}
				yield return (text2, "--");
			}
		}

		private static int FindColumnSplit(string text)
		{
			for (int i = 0; i < text.Length - 1; i++)
			{
				if (text[i] == ' ' && text[i + 1] == ' ')
				{
					return i;
				}
			}
			return -1;
		}
	}

	private sealed class SlimListPanel : Control
	{
		private sealed class DisplayItem
		{
			public string RawText { get; init; } = string.Empty;

			public string NumberText { get; init; } = string.Empty;

			public string TitleText { get; init; } = string.Empty;

			public string SubtitleText { get; init; } = string.Empty;

			public bool Placeholder { get; init; }
		}

		private readonly List<DisplayItem> items = new List<DisplayItem>();

		private readonly System.Windows.Forms.Timer marqueeTimer = new System.Windows.Forms.Timer();

		private int firstVisibleIndex;

		private bool draggingScrollThumb;

		private int dragScrollOffsetY;

		private int selectedIndex = -1;

		private bool moduleView;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool ModuleView
		{
			get
			{
				return moduleView;
			}
			set
			{
				if (moduleView != value)
				{
					moduleView = value;
					Invalidate();
				}
			}
		}

		public event Action<int>? ItemActivated;

		public void ResetViewport()
		{
			firstVisibleIndex = 0;
			Invalidate();
		}

		public void SetItems(IEnumerable<string> source)
		{
			List<DisplayItem> list = source.Take(512).Select(ParseDisplayItem).ToList();
			if (items.Count == list.Count && items.Zip(list, (DisplayItem left, DisplayItem right) => left.RawText == right.RawText && left.NumberText == right.NumberText && left.TitleText == right.TitleText && left.SubtitleText == right.SubtitleText && left.Placeholder == right.Placeholder).All(static match => match))
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
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			DoubleBuffered = true;
			ResizeRedraw = true;
			SetStyle(ControlStyles.Selectable, value: true);
			marqueeTimer.Interval = 110;
			marqueeTimer.Tick += delegate
			{
				if (Visible && HasSelectedOverflow())
				{
					Invalidate();
				}
			};
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
			MouseMove += delegate(object? _, MouseEventArgs e)
			{
				if (draggingScrollThumb && e.Button == MouseButtons.Left)
				{
					UpdateScrollDrag(e.Y);
				}
			};
			MouseUp += delegate
			{
				EndScrollDrag();
			};
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
			e.Graphics.Clear(base.BackColor);
			using (Pen pen3 = new Pen(Color.FromArgb(92, AccentGreen), 1f))
			{
				e.Graphics.DrawRectangle(pen3, 0, 0, Math.Max(0, base.Width - 1), Math.Max(0, base.Height - 1));
			}
			int num = GetLineHeight();
			int visibleLineCount = GetVisibleLineCount();
			int num2 = Math.Min(items.Count, firstVisibleIndex + visibleLineCount);
			using SolidBrush brush = new SolidBrush(ForeColor);
			int num3 = 0;
			for (int i = firstVisibleIndex; i < num2; i++)
			{
				DisplayItem displayItem = items[i];
				bool flag = Focused && i == selectedIndex;
				float y = num3 * num + 2;
				Rectangle rectangle = new Rectangle(1, (int)y, Math.Max(1, base.Width - 9), num - 2);
				using (SolidBrush brush2 = new SolidBrush((i % 2 == 0) ? Color.FromArgb(24, 18, 30, 20) : Color.FromArgb(44, 22, 38, 24)))
				{
					e.Graphics.FillRectangle(brush2, rectangle);
				}
				if (flag)
				{
					using SolidBrush brush3 = new SolidBrush(Color.FromArgb(184, 48, 88, 44));
					using Pen pen = new Pen(Color.FromArgb(214, AccentGreen), 1f);
					e.Graphics.FillRectangle(brush3, rectangle);
					e.Graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
				}
				DrawListItem(e.Graphics, displayItem, rectangle, flag);
				num3++;
			}
			DrawScrollBar(e.Graphics, visibleLineCount);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			base.OnGotFocus(e);
			UpdateAnimationTimer();
			Invalidate();
		}

		protected override void OnLostFocus(EventArgs e)
		{
			base.OnLostFocus(e);
			UpdateAnimationTimer();
			Invalidate();
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (TryStartScrollDrag(e.Location))
			{
				return;
			}
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
					UpdateAnimationTimer();
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
					UpdateAnimationTimer();
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
			Rectangle rectangle = GetScrollTrackRect();
			using SolidBrush brush = new SolidBrush(Color.FromArgb(26, 58, 76, 84));
			graphics.FillRectangle(brush, rectangle);
			double num = (double)visibleLines / (double)items.Count;
			int height = Math.Max(14, (int)(rectangle.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, items.Count - visibleLines);
			int y = rectangle.Top + (int)((rectangle.Height - height) * num2);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(184, AccentGreen));
			graphics.FillRectangle(brush2, new Rectangle(rectangle.Left, y, rectangle.Width, height));
		}

		private void DrawListItem(Graphics graphics, DisplayItem item, Rectangle bounds, bool selected)
		{
			if (item.Placeholder)
			{
				TextRenderer.DrawText(graphics, item.TitleText, Font, new Rectangle(bounds.Left + 8, bounds.Top, Math.Max(1, bounds.Width - 16), bounds.Height), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				return;
			}
			using Font titleFont = new Font(Font.FontFamily, Font.Size + 0.15f, FontStyle.Bold, GraphicsUnit.Point);
			using Font subtitleFont = new Font(Font.FontFamily, Math.Max(6.5f, Font.Size - 0.55f), FontStyle.Regular, GraphicsUnit.Point);
			Rectangle rectangle = new Rectangle(bounds.Left + 8, bounds.Top + 4, 22, 14);
			Rectangle rectangle2 = new Rectangle(bounds.Left + 34, bounds.Top + 2, Math.Max(1, bounds.Width - 42), 14);
			Rectangle rectangle3 = new Rectangle(bounds.Left + 34, bounds.Top + 16, Math.Max(1, bounds.Width - 42), 12);
			Color color = selected ? Color.WhiteSmoke : AccentGreen;
			TextRenderer.DrawText(graphics, item.NumberText, titleFont, rectangle, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			TextRenderer.DrawText(graphics, item.TitleText, titleFont, rectangle2, selected ? Color.WhiteSmoke : ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			if (!string.IsNullOrWhiteSpace(item.SubtitleText))
			{
				DrawCenteredFittedText(graphics, item.SubtitleText, subtitleFont, rectangle3, Color.FromArgb(206, 182, 214, 184));
			}
		}

		private bool HasSelectedOverflow()
		{
			return false;
		}

		private void UpdateAnimationTimer()
		{
			marqueeTimer.Stop();
		}

		private int GetLineHeight()
		{
			return 34;
		}

		private int GetVisibleLineCount()
		{
			return Math.Max(1, (base.Height - 8) / GetLineHeight());
		}

		private int GetItemIndexAt(int y)
		{
			int lineHeight = GetLineHeight();
			if (lineHeight <= 0)
			{
				return -1;
			}
			int num = Math.Max(0, (y - 2) / lineHeight);
			return firstVisibleIndex + num;
		}

		private Rectangle GetScrollTrackRect()
		{
			return new Rectangle(base.Width - 7, 2, 4, Math.Max(10, base.Height - 4));
		}

		private Rectangle GetScrollThumbRect(int visibleLines)
		{
			if (items.Count <= visibleLines || visibleLines <= 0)
			{
				return Rectangle.Empty;
			}
			Rectangle scrollTrackRect = GetScrollTrackRect();
			double num = (double)visibleLines / (double)items.Count;
			int height = Math.Max(14, (int)(scrollTrackRect.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, items.Count - visibleLines);
			int y = scrollTrackRect.Top + (int)((scrollTrackRect.Height - height) * num2);
			return new Rectangle(scrollTrackRect.Left, y, scrollTrackRect.Width, height);
		}

		private bool TryStartScrollDrag(Point location)
		{
			int visibleLineCount = GetVisibleLineCount();
			Rectangle scrollTrackRect = GetScrollTrackRect();
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleLineCount);
			if (scrollThumbRect != Rectangle.Empty && scrollThumbRect.Contains(location))
			{
				draggingScrollThumb = true;
				dragScrollOffsetY = location.Y - scrollThumbRect.Top;
				Capture = true;
				return true;
			}
			if (scrollThumbRect != Rectangle.Empty && scrollTrackRect.Contains(location))
			{
				UpdateScrollFromThumbTop(location.Y - scrollThumbRect.Height / 2, visibleLineCount, scrollTrackRect, scrollThumbRect.Height);
				return true;
			}
			return false;
		}

		private void UpdateScrollDrag(int mouseY)
		{
			int visibleLineCount = GetVisibleLineCount();
			Rectangle scrollTrackRect = GetScrollTrackRect();
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleLineCount);
			if (scrollThumbRect == Rectangle.Empty)
			{
				return;
			}
			UpdateScrollFromThumbTop(mouseY - dragScrollOffsetY, visibleLineCount, scrollTrackRect, scrollThumbRect.Height);
		}

		private void UpdateScrollFromThumbTop(int thumbTop, int visibleLines, Rectangle trackRect, int thumbHeight)
		{
			if (items.Count <= visibleLines || visibleLines <= 0)
			{
				return;
			}
			int num = Math.Max(1, items.Count - visibleLines);
			int num2 = Math.Max(1, trackRect.Height - thumbHeight);
			int num3 = Math.Clamp(thumbTop, trackRect.Top, trackRect.Bottom - thumbHeight);
			double num4 = (double)(num3 - trackRect.Top) / (double)num2;
			firstVisibleIndex = Math.Clamp((int)Math.Round(num * num4), 0, num);
			Invalidate();
		}

		private void EndScrollDrag()
		{
			draggingScrollThumb = false;
			Capture = false;
		}

		private DisplayItem ParseDisplayItem(string value)
		{
			string text = TrimOrNull(value) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(text))
			{
				return new DisplayItem
				{
					RawText = string.Empty,
					TitleText = "--",
					Placeholder = true
				};
			}
			int num = text.IndexOf(". ", StringComparison.Ordinal);
			if (num > 0 && int.TryParse(text.Substring(0, num), NumberStyles.None, CultureInfo.InvariantCulture, out _))
			{
				string text2 = text.Substring(num + 2).Trim();
				return new DisplayItem
				{
					RawText = text,
					NumberText = text.Substring(0, num) + ".",
					TitleText = BuildInventoryTitle(text2),
					SubtitleText = BuildInventorySubtitle(text2),
					Placeholder = false
				};
			}
			return new DisplayItem
			{
				RawText = text,
				TitleText = text,
				Placeholder = true
			};
		}

		private string BuildInventoryTitle(string value)
		{
			string text = FitFileLeaf(value, 26);
			return string.IsNullOrWhiteSpace(text) ? "--" : text;
		}

		private string BuildInventorySubtitle(string value)
		{
			if (moduleView)
			{
				return string.Empty;
			}
			return TrimOrNull(value) ?? string.Empty;
		}

		private static string ExtractInventoryRoot(string value)
		{
			string text = TrimOrNull(value) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(text))
			{
				return string.Empty;
			}
			int num = text.IndexOf(':');
			if (num > 0)
			{
				return text.Substring(0, num).Trim().ToUpperInvariant();
			}
			if (text.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase))
			{
				return "DEVICE";
			}
			return string.Empty;
		}

		private static void DrawCenteredFittedText(Graphics graphics, string text, Font baseFont, Rectangle bounds, Color color)
		{
			if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 4 || bounds.Height <= 2)
			{
				return;
			}
			Font? font = null;
			try
			{
				float num = baseFont.Size;
				while (num >= 5.75f)
				{
					font?.Dispose();
					font = new Font(baseFont.FontFamily, num, baseFont.Style, GraphicsUnit.Point);
					Size size = TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, bounds.Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
					if (size.Width <= bounds.Width - 2)
					{
						break;
					}
					num -= 0.25f;
				}
				TextRenderer.DrawText(graphics, text, font ?? baseFont, bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			}
			finally
			{
				font?.Dispose();
			}
		}
	}

	private sealed class DriveInventoryPanel : Control
	{
		private readonly List<DriveInventoryEntry> entries = new List<DriveInventoryEntry>();

		private int firstVisibleIndex;

		private bool draggingScrollThumb;

		private int dragScrollOffsetY;

		public DriveInventoryPanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
			MouseWheel += delegate(object? _, MouseEventArgs e)
			{
				int num = GetVisibleRowCount();
				int num2 = Math.Max(1, num / 2);
				if (e.Delta < 0)
				{
					firstVisibleIndex = Math.Min(Math.Max(0, entries.Count - num), firstVisibleIndex + num2);
				}
				else if (e.Delta > 0)
				{
					firstVisibleIndex = Math.Max(0, firstVisibleIndex - num2);
				}
				Invalidate();
			};
			MouseMove += delegate(object? _, MouseEventArgs e)
			{
				if (draggingScrollThumb && e.Button == MouseButtons.Left)
				{
					UpdateScrollDrag(e.Y);
				}
			};
			MouseUp += delegate
			{
				EndScrollDrag();
			};
		}

		public void SetEntries(IEnumerable<DriveInventoryEntry> source)
		{
			List<DriveInventoryEntry> list = source.Take(64).Select(static entry => new DriveInventoryEntry
			{
				Name = entry.Name,
				TotalBytes = entry.TotalBytes,
				FreeBytes = entry.FreeBytes
			}).ToList();
			if (entries.Count == list.Count && entries.Zip(list, static (left, right) => string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.TotalBytes == right.TotalBytes && left.FreeBytes == right.FreeBytes).All(static match => match))
			{
				return;
			}
			entries.Clear();
			entries.AddRange(list);
			firstVisibleIndex = Math.Clamp(firstVisibleIndex, 0, Math.Max(0, entries.Count - 1));
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
			e.Graphics.Clear(BackColor);
			if (entries.Count == 0)
			{
				return;
			}
			if (entries.Count == 1 && IsPlaceholderEntry(entries[0]))
			{
				DrawPlaceholderState(e.Graphics, entries[0]);
				return;
			}
			int rowHeight = GetRowHeight();
			int visibleRowCount = GetVisibleRowCount();
			int num = Math.Min(entries.Count, firstVisibleIndex + visibleRowCount);
			using Font font = new Font(Font.FontFamily, Font.Size + 0.6f, FontStyle.Bold, GraphicsUnit.Point);
			for (int i = firstVisibleIndex; i < num; i++)
			{
				int num2 = (i - firstVisibleIndex) * rowHeight + 2;
				Rectangle rectangle = new Rectangle(1, num2, Math.Max(1, base.Width - 9), rowHeight - 2);
				using (SolidBrush brush3 = new SolidBrush((i % 2 == 0) ? Color.FromArgb(24, 18, 30, 20) : Color.FromArgb(44, 22, 38, 24)))
				{
					e.Graphics.FillRectangle(brush3, rectangle);
				}
				Rectangle rectangle2 = new Rectangle(rectangle.Left + 6, rectangle.Top + 2, Math.Max(1, rectangle.Width - 78), 16);
				Rectangle rectangle3 = new Rectangle(rectangle.Right - 68, rectangle.Top + 2, 62, 16);
				Rectangle rectangle4 = new Rectangle(rectangle.Left + 6, rectangle.Top + 20, Math.Max(40, rectangle.Width - 12), 8);
				Rectangle rectangle5 = new Rectangle(rectangle.Left + 6, rectangle.Top + 30, Math.Max(1, rectangle.Width - 12), 14);
				TextRenderer.DrawText(e.Graphics, entries[i].Name, font, rectangle2, AccentGreen, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				string text = FormatDrivePercent(entries[i]);
				if (!string.IsNullOrWhiteSpace(text))
				{
					TextRenderer.DrawText(e.Graphics, text, Font, rectangle3, Color.WhiteSmoke, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				}
				DrawDriveBar(e.Graphics, entries[i], rectangle4);
				TextRenderer.DrawText(e.Graphics, FormatDriveSubtitle(entries[i]), Font, rectangle5, Color.FromArgb(206, 182, 214, 184), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			}
			DrawScrollBar(e.Graphics, visibleRowCount);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			TryStartScrollDrag(e.Location);
		}

		private static void DrawDriveBar(Graphics graphics, DriveInventoryEntry entry, Rectangle bounds)
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(32, 54, 40));
			using Pen pen = new Pen(Color.FromArgb(90, AccentGreen), 1f);
			graphics.FillRectangle(brush, bounds);
			graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
			if (!entry.TotalBytes.HasValue || !entry.FreeBytes.HasValue || entry.TotalBytes.Value == 0)
			{
				return;
			}
			double num = Math.Clamp((double)(entry.TotalBytes.Value - entry.FreeBytes.Value) / (double)entry.TotalBytes.Value, 0.0, 1.0);
			int num2 = Math.Max(0, (int)Math.Round((bounds.Width - 2) * num));
			if (num2 <= 0)
			{
				return;
			}
			Color color = ResolveDriveUsageColor(num);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(200, color));
			graphics.FillRectangle(brush2, new Rectangle(bounds.X + 1, bounds.Y + 1, num2, Math.Max(1, bounds.Height - 2)));
		}

		private static Color ResolveDriveUsageColor(double usedRatio)
		{
			if (usedRatio >= 0.85)
			{
				return Color.FromArgb(230, 86, 74);
			}
			if (usedRatio >= 0.60)
			{
				return Color.FromArgb(212, 178, 62);
			}
			return AccentGreen;
		}

		private static string FormatDrivePercent(DriveInventoryEntry entry)
		{
			if (!entry.TotalBytes.HasValue || !entry.FreeBytes.HasValue || entry.TotalBytes.Value == 0)
			{
				return string.Empty;
			}
			double num = (double)(entry.TotalBytes.Value - entry.FreeBytes.Value) / (double)entry.TotalBytes.Value * 100.0;
			return LocalizedText.IsSpanish ? (num.ToString("0", CultureInfo.InvariantCulture) + "% usado") : (num.ToString("0", CultureInfo.InvariantCulture) + "% used");
		}

		private void DrawScrollBar(Graphics graphics, int visibleRows)
		{
			if (entries.Count <= visibleRows || visibleRows <= 0)
			{
				return;
			}
			Rectangle rectangle = GetScrollTrackRect();
			using SolidBrush brush = new SolidBrush(Color.FromArgb(74, 34, 62, 32));
			graphics.FillRectangle(brush, rectangle);
			double num = (double)visibleRows / (double)entries.Count;
			int height = Math.Max(18, (int)(rectangle.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, entries.Count - visibleRows);
			int y = rectangle.Top + (int)((rectangle.Height - height) * num2);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(220, AccentGreen));
			graphics.FillRectangle(brush2, new Rectangle(rectangle.Left, y, rectangle.Width, height));
		}

		private static string FormatDriveSubtitle(DriveInventoryEntry entry)
		{
			if (entry.TotalBytes.HasValue && entry.FreeBytes.HasValue && entry.TotalBytes.Value > 0)
			{
				return LocalizedText.IsSpanish ? (FormatStorageCompact(entry.FreeBytes.Value) + " libres de " + FormatStorageCompact(entry.TotalBytes.Value)) : (FormatStorageCompact(entry.FreeBytes.Value) + " free of " + FormatStorageCompact(entry.TotalBytes.Value));
			}
			return LocalizedText.IsSpanish ? "Tamano no disponible" : "Size unavailable";
		}

		private static bool IsPlaceholderEntry(DriveInventoryEntry entry)
		{
			string text = TrimOrNull(entry.Name) ?? string.Empty;
			return !entry.TotalBytes.HasValue
				&& !entry.FreeBytes.HasValue
				&& (string.Equals(text, "Connecting...", StringComparison.Ordinal)
					|| string.Equals(text, "No live drives detected", StringComparison.Ordinal)
					|| string.Equals(text, "Conectando...", StringComparison.Ordinal)
					|| string.Equals(text, "No se detectaron unidades activas", StringComparison.Ordinal));
		}

		private void DrawPlaceholderState(Graphics graphics, DriveInventoryEntry entry)
		{
			Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
			using Font font = new Font(Font.FontFamily, Font.Size + 0.75f, FontStyle.Bold, GraphicsUnit.Point);
			using Font font2 = new Font(Font.FontFamily, Math.Max(6.75f, Font.Size - 0.25f), FontStyle.Regular, GraphicsUnit.Point);
			string text = entry.Name;
			string text2 = string.Equals(text, "Connecting...", StringComparison.Ordinal) || string.Equals(text, "Conectando...", StringComparison.Ordinal)
				? string.Empty
				: (LocalizedText.IsSpanish ? "Conecta para detectar almacenamiento" : "Connect to detect storage");
			Rectangle rectangle2 = new Rectangle(rectangle.Left + 10, rectangle.Top + 16, Math.Max(1, rectangle.Width - 20), 22);
			Rectangle rectangle3 = new Rectangle(rectangle.Left + 10, rectangle.Top + 42, Math.Max(1, rectangle.Width - 20), 18);
			TextRenderer.DrawText(graphics, text, font, rectangle2, AccentGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				TextRenderer.DrawText(graphics, text2, font2, rectangle3, Color.FromArgb(206, 182, 214, 184), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
			}
		}

		private static string FormatStorage(ulong bytes)
		{
			string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
			double num = bytes;
			int num2 = 0;
			while (num >= 1024.0 && num2 < array.Length - 1)
			{
				num /= 1024.0;
				num2++;
			}
			return (num2 == 0) ? (num.ToString("0", CultureInfo.InvariantCulture) + " " + array[num2]) : (num.ToString("0.##", CultureInfo.InvariantCulture) + " " + array[num2]);
		}

		private static string FormatStorageCompact(ulong bytes)
		{
			string[] array = new string[5] { "B", "K", "M", "G", "T" };
			double num = bytes;
			int num2 = 0;
			while (num >= 1024.0 && num2 < array.Length - 1)
			{
				num /= 1024.0;
				num2++;
			}
			string text = (num2 <= 1) ? num.ToString("0", CultureInfo.InvariantCulture) : num.ToString("0.#", CultureInfo.InvariantCulture);
			return text + array[num2];
		}

		private int GetRowHeight()
		{
			return 48;
		}

		private Rectangle GetScrollTrackRect()
		{
			return new Rectangle(base.Width - 7, 2, 4, Math.Max(10, base.Height - 4));
		}

		private Rectangle GetScrollThumbRect(int visibleRows)
		{
			if (entries.Count <= visibleRows || visibleRows <= 0)
			{
				return Rectangle.Empty;
			}
			Rectangle scrollTrackRect = GetScrollTrackRect();
			double num = (double)visibleRows / (double)entries.Count;
			int height = Math.Max(18, (int)(scrollTrackRect.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, entries.Count - visibleRows);
			int y = scrollTrackRect.Top + (int)((scrollTrackRect.Height - height) * num2);
			return new Rectangle(scrollTrackRect.Left, y, scrollTrackRect.Width, height);
		}

		private bool TryStartScrollDrag(Point location)
		{
			int visibleRowCount = GetVisibleRowCount();
			Rectangle scrollTrackRect = GetScrollTrackRect();
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleRowCount);
			if (scrollThumbRect != Rectangle.Empty && scrollThumbRect.Contains(location))
			{
				draggingScrollThumb = true;
				dragScrollOffsetY = location.Y - scrollThumbRect.Top;
				Capture = true;
				return true;
			}
			if (scrollThumbRect != Rectangle.Empty && scrollTrackRect.Contains(location))
			{
				UpdateScrollFromThumbTop(location.Y - scrollThumbRect.Height / 2, visibleRowCount, scrollTrackRect, scrollThumbRect.Height);
				return true;
			}
			return false;
		}

		private void UpdateScrollDrag(int mouseY)
		{
			int visibleRowCount = GetVisibleRowCount();
			Rectangle scrollTrackRect = GetScrollTrackRect();
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleRowCount);
			if (scrollThumbRect == Rectangle.Empty)
			{
				return;
			}
			UpdateScrollFromThumbTop(mouseY - dragScrollOffsetY, visibleRowCount, scrollTrackRect, scrollThumbRect.Height);
		}

		private void UpdateScrollFromThumbTop(int thumbTop, int visibleRows, Rectangle trackRect, int thumbHeight)
		{
			if (entries.Count <= visibleRows || visibleRows <= 0)
			{
				return;
			}
			int num = Math.Max(1, entries.Count - visibleRows);
			int num2 = Math.Max(1, trackRect.Height - thumbHeight);
			int num3 = Math.Clamp(thumbTop, trackRect.Top, trackRect.Bottom - thumbHeight);
			double num4 = (double)(num3 - trackRect.Top) / (double)num2;
			firstVisibleIndex = Math.Clamp((int)Math.Round(num * num4), 0, num);
			Invalidate();
		}

		private void EndScrollDrag()
		{
			draggingScrollThumb = false;
			Capture = false;
		}

		private int GetVisibleRowCount()
		{
			return Math.Max(1, base.Height / GetRowHeight());
		}
	}

	private sealed class SuggestionListPanel : Control
	{
		private const int MaxVisibleSuggestionRows = 10;

		private readonly List<string> items = new List<string>();

		private int firstVisibleIndex;

		private int selectedIndex = -1;

		private bool draggingScrollThumb;

		private int dragScrollOffsetY;

		public event Action? ItemActivated;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ItemCount => items.Count;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int SelectedIndex
		{
			get
			{
				return selectedIndex;
			}
			set
			{
				if (items.Count == 0)
				{
					selectedIndex = -1;
					firstVisibleIndex = 0;
				}
				else
				{
					selectedIndex = Math.Clamp(value, 0, items.Count - 1);
					EnsureSelectionVisible();
				}
				Invalidate();
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string? SelectedItem
		{
			get
			{
				if (selectedIndex < 0 || selectedIndex >= items.Count)
				{
					return null;
				}
				return items[selectedIndex];
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int PreferredHostHeight
		{
			get
			{
				if (items.Count == 0)
				{
					return 0;
				}
				int num = Math.Clamp(items.Count, 1, MaxVisibleSuggestionRows);
				return num * GetLineHeight() + 8;
			}
		}

		public SuggestionListPanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			SetStyle(ControlStyles.Selectable, value: true);
			DoubleBuffered = true;
			BackColor = TerminalBackground;
			ForeColor = Color.WhiteSmoke;
			MouseWheel += delegate(object? _, MouseEventArgs e)
			{
				int num = GetVisibleLineCount();
				int num2 = Math.Max(1, num / 2);
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
			MouseMove += delegate(object? _, MouseEventArgs e)
			{
				if (draggingScrollThumb && e.Button == MouseButtons.Left)
				{
					UpdateScrollDrag(e.Y);
				}
			};
			MouseUp += delegate
			{
				EndScrollDrag();
			};
		}

		public void SetItems(IEnumerable<string> source)
		{
			List<string> list = source.Take(128).ToList();
			items.Clear();
			items.AddRange(list);
			if (items.Count == 0)
			{
				selectedIndex = -1;
				firstVisibleIndex = 0;
			}
			else
			{
				selectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
				EnsureSelectionVisible();
			}
			Invalidate();
		}

		public void MoveSelection(int delta)
		{
			if (items.Count == 0)
			{
				return;
			}
			if (selectedIndex < 0)
			{
				SelectedIndex = 0;
				return;
			}
			SelectedIndex = Math.Clamp(selectedIndex + delta, 0, items.Count - 1);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (TryStartScrollDrag(e.Location))
			{
				return;
			}
			if (!Focused && CanFocus)
			{
				Focus();
			}
			int itemIndexAt = GetItemIndexAt(e.Y);
			if (itemIndexAt >= 0 && itemIndexAt < items.Count)
			{
				SelectedIndex = itemIndexAt;
			}
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			int itemIndexAt = GetItemIndexAt(e.Y);
			if (itemIndexAt >= 0 && itemIndexAt < items.Count)
			{
				SelectedIndex = itemIndexAt;
				ItemActivated?.Invoke();
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
			using SolidBrush brush = new SolidBrush(Color.FromArgb(214, 8, 14, 12));
			e.Graphics.FillRectangle(brush, ClientRectangle);
			Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1));
			using Pen pen = new Pen(Color.FromArgb(168, BorderColor), 1f);
			e.Graphics.DrawRectangle(pen, rectangle);
			if (items.Count == 0)
			{
				return;
			}
			int lineHeight = GetLineHeight();
			int visibleLineCount = GetVisibleLineCount();
			int num = Math.Min(items.Count, firstVisibleIndex + visibleLineCount);
			for (int i = firstVisibleIndex; i < num; i++)
			{
				int num2 = (i - firstVisibleIndex) * lineHeight + 4;
				Rectangle rectangle2 = new Rectangle(5, num2, Math.Max(1, base.Width - 16), lineHeight - 1);
				bool flag = i == selectedIndex;
				if (i % 2 == 1)
				{
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(120, 12, 22, 17));
					e.Graphics.FillRectangle(brush2, rectangle2);
				}
				if (flag)
				{
					using SolidBrush brush3 = new SolidBrush(Color.FromArgb(156, 34, 62, 32));
					using Pen pen2 = new Pen(Color.FromArgb(220, AccentGreen), 1f);
					e.Graphics.FillRectangle(brush3, rectangle2);
					e.Graphics.DrawRectangle(pen2, rectangle2.X, rectangle2.Y, rectangle2.Width - 1, rectangle2.Height - 1);
				}
				Rectangle rectangle3 = new Rectangle(rectangle2.Left + 6, rectangle2.Top, 14, rectangle2.Height);
				Color color = flag ? Color.FromArgb(232, 236, 246, 232) : Color.FromArgb(208, 202, 224, 204);
				TextRenderer.DrawText(e.Graphics, "·", Font, rectangle3, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				Rectangle rectangle4 = new Rectangle(rectangle2.Left + 18, rectangle2.Top, Math.Max(1, rectangle2.Width - 18), rectangle2.Height);
				TextRenderer.DrawText(e.Graphics, items[i], Font, rectangle4, flag ? Color.WhiteSmoke : ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			}
			DrawScrollBar(e.Graphics, visibleLineCount);
		}

		private void EnsureSelectionVisible()
		{
			int visibleLineCount = GetVisibleLineCount();
			if (selectedIndex < firstVisibleIndex)
			{
				firstVisibleIndex = selectedIndex;
			}
			else if (selectedIndex >= firstVisibleIndex + visibleLineCount)
			{
				firstVisibleIndex = Math.Max(0, selectedIndex - visibleLineCount + 1);
			}
		}

		private void DrawScrollBar(Graphics graphics, int visibleLines)
		{
			if (items.Count <= visibleLines || visibleLines <= 0)
			{
				return;
			}
			Rectangle rectangle = GetScrollTrackRect();
			using SolidBrush brush = new SolidBrush(Color.FromArgb(74, 34, 62, 32));
			graphics.FillRectangle(brush, rectangle);
			double num = (double)visibleLines / (double)items.Count;
			int height = Math.Max(24, (int)(rectangle.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, items.Count - visibleLines);
			int y = rectangle.Top + (int)((rectangle.Height - height) * num2);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(220, AccentGreen));
			graphics.FillRectangle(brush2, new Rectangle(rectangle.Left, y, rectangle.Width, height));
		}

		private int GetLineHeight()
		{
			return Math.Max(24, TextRenderer.MeasureText("XeCLI", Font).Height + 6);
		}

		private int GetVisibleLineCount()
		{
			return Math.Max(1, (base.Height - 8) / GetLineHeight());
		}

		private int GetItemIndexAt(int y)
		{
			int lineHeight = GetLineHeight();
			if (lineHeight <= 0)
			{
				return -1;
			}
			int num = Math.Max(0, (y - 4) / lineHeight);
			return firstVisibleIndex + num;
		}

		private Rectangle GetScrollTrackRect()
		{
			return new Rectangle(base.Width - 8, 4, 4, Math.Max(10, base.Height - 8));
		}

		private Rectangle GetScrollThumbRect(int visibleLines)
		{
			if (items.Count <= visibleLines || visibleLines <= 0)
			{
				return Rectangle.Empty;
			}
			Rectangle scrollTrackRect = GetScrollTrackRect();
			double num = (double)visibleLines / (double)items.Count;
			int height = Math.Max(24, (int)(scrollTrackRect.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, items.Count - visibleLines);
			int y = scrollTrackRect.Top + (int)((scrollTrackRect.Height - height) * num2);
			return new Rectangle(scrollTrackRect.Left, y, scrollTrackRect.Width, height);
		}

		private bool TryStartScrollDrag(Point location)
		{
			int visibleLineCount = GetVisibleLineCount();
			Rectangle scrollTrackRect = GetScrollTrackRect();
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleLineCount);
			if (scrollThumbRect != Rectangle.Empty && scrollThumbRect.Contains(location))
			{
				draggingScrollThumb = true;
				dragScrollOffsetY = location.Y - scrollThumbRect.Top;
				Capture = true;
				return true;
			}
			if (scrollThumbRect != Rectangle.Empty && scrollTrackRect.Contains(location))
			{
				UpdateScrollFromThumbTop(location.Y - scrollThumbRect.Height / 2, visibleLineCount, scrollTrackRect, scrollThumbRect.Height);
				return true;
			}
			return false;
		}

		private void UpdateScrollDrag(int mouseY)
		{
			int visibleLineCount = GetVisibleLineCount();
			Rectangle scrollTrackRect = GetScrollTrackRect();
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleLineCount);
			if (scrollThumbRect == Rectangle.Empty)
			{
				return;
			}
			UpdateScrollFromThumbTop(mouseY - dragScrollOffsetY, visibleLineCount, scrollTrackRect, scrollThumbRect.Height);
		}

		private void UpdateScrollFromThumbTop(int thumbTop, int visibleLines, Rectangle trackRect, int thumbHeight)
		{
			if (items.Count <= visibleLines || visibleLines <= 0)
			{
				return;
			}
			int num = Math.Max(1, items.Count - visibleLines);
			int num2 = Math.Max(1, trackRect.Height - thumbHeight);
			int num3 = Math.Clamp(thumbTop, trackRect.Top, trackRect.Bottom - thumbHeight);
			double num4 = (double)(num3 - trackRect.Top) / (double)num2;
			firstVisibleIndex = Math.Clamp((int)Math.Round(num * num4), 0, num);
			Invalidate();
		}

		private void EndScrollDrag()
		{
			draggingScrollThumb = false;
			Capture = false;
		}
	}

	private sealed class TransferQueuePanel : Control
	{
		private readonly List<TransferQueueEntry> entries = new List<TransferQueueEntry>();

		public TransferQueuePanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			DoubleBuffered = true;
			ResizeRedraw = true;
		}

		public void SetEntries(IEnumerable<TransferQueueEntry> source)
		{
			entries.Clear();
			entries.AddRange(source.Take(10).Select(static item => new TransferQueueEntry
			{
				CommandKey = item.CommandKey,
				CommandText = item.CommandText,
				State = item.State,
				Detail = item.Detail,
				ProgressPercent = item.ProgressPercent,
				UpdatedAtLocal = item.UpdatedAtLocal
			}));
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
			e.Graphics.Clear(TerminalBackground);
			Rectangle clientRectangle = ClientRectangle;
			if (clientRectangle.Width <= 0 || clientRectangle.Height <= 0)
			{
				return;
			}
			Rectangle rectangle = Rectangle.Inflate(clientRectangle, -1, -1);
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(8, 14, 11)))
			using (Pen pen = new Pen(Color.FromArgb(104, AccentGreen), 1f))
			using (Pen pen2 = new Pen(Color.FromArgb(34, AccentGreen), 1f))
			{
				e.Graphics.FillRectangle(brush, rectangle);
				e.Graphics.DrawRectangle(pen, rectangle);
				e.Graphics.DrawRectangle(pen2, rectangle.X + 3, rectangle.Y + 3, Math.Max(0, rectangle.Width - 6), Math.Max(0, rectangle.Height - 6));
			}
			if (entries.Count == 0)
			{
				using Font font = new Font("Consolas", 11f, FontStyle.Bold, GraphicsUnit.Point);
				using Font font2 = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
				TextRenderer.DrawText(e.Graphics, LocalizedText.IsSpanish ? "COLA EN ESPERA" : "QUEUE IDLE", font, new Rectangle(rectangle.X + 12, rectangle.Y + 20, rectangle.Width - 24, 28), AccentGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, LocalizedText.IsSpanish ? "No hay transferencias en cola" : "No transfers staged", font2, new Rectangle(rectangle.X + 12, rectangle.Y + 52, rectangle.Width - 24, 22), AccentDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				return;
			}
			int y = rectangle.Y + 10;
			int num = rectangle.Width - 20;
			int num2 = Math.Max(54, Math.Min(70, (rectangle.Height - 20) / Math.Max(1, entries.Count)));
			for (int i = 0; i < entries.Count; i++)
			{
				TransferQueueEntry transferQueueEntry = entries[i];
				Rectangle rectangle2 = new Rectangle(rectangle.X + 10, y, num, Math.Max(48, num2 - 6));
				bool flag = string.Equals(transferQueueEntry.State, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(transferQueueEntry.State, "queued", StringComparison.OrdinalIgnoreCase);
				Color color = ResolveQueueStateColor(transferQueueEntry.State);
				using (SolidBrush brush2 = new SolidBrush(i % 2 == 0 ? Color.FromArgb(12, 18, 13) : Color.FromArgb(10, 16, 12)))
				using (Pen pen3 = new Pen(Color.FromArgb(flag ? 128 : 72, color), 1f))
				{
					e.Graphics.FillRectangle(brush2, rectangle2);
					e.Graphics.DrawRectangle(pen3, rectangle2);
				}
				TextRenderer.DrawText(e.Graphics, transferQueueEntry.UpdatedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Font, new Rectangle(rectangle2.X + 8, rectangle2.Y + 5, 66, 18), AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, transferQueueEntry.State.ToUpperInvariant(), Font, new Rectangle(rectangle2.X + 78, rectangle2.Y + 5, 86, 18), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				string text = transferQueueEntry.ProgressPercent.HasValue ? (transferQueueEntry.ProgressPercent.Value.ToString("0") + "%") : "--";
				TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(rectangle2.Right - 58, rectangle2.Y + 5, 50, 18), Color.WhiteSmoke, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, transferQueueEntry.CommandText, Font, new Rectangle(rectangle2.X + 8, rectangle2.Y + 24, rectangle2.Width - 16, 18), Color.WhiteSmoke, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				string text2 = TrimOrNull(transferQueueEntry.Detail) ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(text2))
				{
					using Font font3 = new Font("Consolas", 7.75f, FontStyle.Regular, GraphicsUnit.Point);
					TextRenderer.DrawText(e.Graphics, text2, font3, new Rectangle(rectangle2.X + 8, rectangle2.Y + 42, rectangle2.Width - 16, 14), AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				}
				Rectangle rectangle3 = new Rectangle(rectangle2.X + 8, rectangle2.Bottom - 10, rectangle2.Width - 16, 6);
				using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(20, 48, 32)))
				using (SolidBrush brush4 = new SolidBrush(Color.FromArgb(190, color)))
				{
					e.Graphics.FillRectangle(brush3, rectangle3);
					if (transferQueueEntry.ProgressPercent.HasValue)
					{
						int width = (int)Math.Round(rectangle3.Width * Math.Clamp(transferQueueEntry.ProgressPercent.Value / 100.0, 0.0, 1.0));
						if (width > 0)
						{
							e.Graphics.FillRectangle(brush4, new Rectangle(rectangle3.X, rectangle3.Y, width, rectangle3.Height));
						}
					}
				}
				y += num2;
				if (y >= rectangle.Bottom - 20)
				{
					break;
				}
			}
		}

		private static Color ResolveQueueStateColor(string state)
		{
			if (string.Equals(state, "complete", StringComparison.OrdinalIgnoreCase))
			{
				return AccentGreen;
			}
			if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "error", StringComparison.OrdinalIgnoreCase))
			{
				return WarningColor;
			}
			if (string.Equals(state, "cancelled", StringComparison.OrdinalIgnoreCase))
			{
				return AccentPink;
			}
			return AccentCyan;
		}
	}

	private sealed class TerminalScrollIndicator : Control
	{
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

		private const int EmGetFirstVisibleLine = 0x00CE;

		private RichTextBox? source;

		public TerminalScrollIndicator()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
			BackColor = Color.FromArgb(9, 16, 14);
		}

		public void Attach(RichTextBox richTextBox)
		{
			if (source == richTextBox)
			{
				return;
			}
			if (source != null)
			{
				source.VScroll -= HandleSourceScrollChanged;
				source.TextChanged -= HandleSourceScrollChanged;
				source.Resize -= HandleSourceScrollChanged;
				source.SelectionChanged -= HandleSourceScrollChanged;
			}
			source = richTextBox;
			source.VScroll += HandleSourceScrollChanged;
			source.TextChanged += HandleSourceScrollChanged;
			source.Resize += HandleSourceScrollChanged;
			source.SelectionChanged += HandleSourceScrollChanged;
			Invalidate();
		}

		private void HandleSourceScrollChanged(object? sender, EventArgs e)
		{
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.Clear(BackColor);
			Rectangle rectangle = new Rectangle(Math.Max(0, (base.Width - 4) / 2), 4, 4, Math.Max(0, base.Height - 8));
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(42, 54, 96, 46)))
			{
				e.Graphics.FillRectangle(brush, rectangle);
			}
			if (source == null || !source.IsHandleCreated || rectangle.Height <= 0)
			{
				return;
			}
			int lineCount = Math.Max(1, source.GetLineFromCharIndex(source.TextLength) + 1);
			int num = Math.Max(1, source.Font.Height + 1);
			int visibleLines = Math.Max(1, source.ClientSize.Height / num);
			int firstVisibleLine = Math.Max(0, SendMessage(source.Handle, EmGetFirstVisibleLine, 0, 0));
			if (lineCount <= visibleLines)
			{
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(188, AccentGreen)))
				{
					e.Graphics.FillRectangle(brush2, rectangle);
				}
				return;
			}
			double num2 = (double)visibleLines / (double)lineCount;
			int height = Math.Max(18, (int)Math.Round(rectangle.Height * num2));
			double num3 = (double)firstVisibleLine / (double)Math.Max(1, lineCount - visibleLines);
			int y = rectangle.Top + (int)Math.Round((rectangle.Height - height) * num3);
			Rectangle rectangle2 = new Rectangle(rectangle.X, y, rectangle.Width, height);
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(220, AccentGreen));
			using Pen pen = new Pen(Color.FromArgb(160, Color.WhiteSmoke), 1f);
			e.Graphics.FillRectangle(brush3, rectangle2);
			e.Graphics.DrawLine(pen, rectangle2.Left, rectangle2.Top, rectangle2.Right - 1, rectangle2.Top);
		}
	}

	private sealed class FileGridPanel : Control
	{
		private enum ResizeColumn
		{
			None,
			NameToModified,
			ModifiedToSize
		}

		private readonly List<FileEntryView> entries = new List<FileEntryView>();

		private int firstVisibleIndex;

		private bool draggingScrollThumb;

		private int dragScrollOffsetY;

		private int selectedIndex = -1;

		private int modifiedWidth = 180;

		private int sizeWidth = 84;

		private ResizeColumn activeResizeColumn;

		private int resizeAnchorX;

		private int resizeAnchorModifiedWidth;

		private int resizeAnchorSizeWidth;

		private string? message;

		private bool dragPending;

		private int dragIndex = -1;

		private Point dragStartPoint;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string HeaderText { get; set; } = "NAME";

		public event Action<int>? ItemActivated;

		public event Action<int, Point>? ItemContextRequested;

		public event Action<Point>? EmptyAreaContextRequested;

		public event Action<int>? ItemDragRequested;

		public FileGridPanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			DoubleBuffered = true;
			ResizeRedraw = true;
			SetStyle(ControlStyles.Selectable, value: true);
			MouseWheel += delegate(object? _, MouseEventArgs e)
			{
				int visibleRowCount = GetVisibleRowCount();
				int num = Math.Max(1, visibleRowCount / 3);
				if (e.Delta < 0)
				{
					firstVisibleIndex = Math.Min(Math.Max(0, entries.Count - visibleRowCount), firstVisibleIndex + num);
				}
				else if (e.Delta > 0)
				{
					firstVisibleIndex = Math.Max(0, firstVisibleIndex - num);
				}
				Invalidate();
			};
			MouseMove += delegate(object? _, MouseEventArgs e)
			{
				if (draggingScrollThumb && e.Button == MouseButtons.Left)
				{
					UpdateScrollDrag(e.Y);
					return;
				}
				HandleColumnResizeMove(e.Location, e.Button == MouseButtons.Left);
				if (!draggingColumn(e.Button) && dragPending && e.Button == MouseButtons.Left)
				{
					if (Math.Abs(e.X - dragStartPoint.X) >= SystemInformation.DragSize.Width / 2 || Math.Abs(e.Y - dragStartPoint.Y) >= SystemInformation.DragSize.Height / 2)
					{
						dragPending = false;
						if (dragIndex >= 0 && dragIndex < entries.Count)
						{
							ItemDragRequested?.Invoke(dragIndex);
						}
					}
				}
			};
			MouseUp += delegate
			{
				activeResizeColumn = ResizeColumn.None;
				EndScrollDrag();
				dragPending = false;
				dragIndex = -1;
				Cursor = Cursors.Default;
			};
			MouseLeave += delegate
			{
				dragPending = false;
				dragIndex = -1;
				if (activeResizeColumn == ResizeColumn.None)
				{
					Cursor = Cursors.Default;
				}
			};
		}

		public void SetEntries(IEnumerable<FileEntryView> source)
		{
			List<FileEntryView> list = source.Take(512).ToList();
			entries.Clear();
			entries.AddRange(list);
			message = null;
			firstVisibleIndex = Math.Clamp(firstVisibleIndex, 0, Math.Max(0, entries.Count - 1));
			if (entries.Count == 0)
			{
				selectedIndex = -1;
				message = "(empty)";
			}
			else if (selectedIndex >= entries.Count)
			{
				selectedIndex = entries.Count - 1;
			}
			Invalidate();
		}

		public void SetMessage(string text)
		{
			entries.Clear();
			firstVisibleIndex = 0;
			selectedIndex = -1;
			message = text;
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.None;
			e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
			e.Graphics.Clear(base.BackColor);
			Rectangle bounds = ClientRectangle;
			if (bounds.Width <= 0 || bounds.Height <= 0)
			{
				return;
			}
			int headerHeight = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
			Rectangle headerRect = new Rectangle(0, 0, bounds.Width, headerHeight);
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(12, AccentGreen)))
			using (Pen pen = new Pen(Color.FromArgb(80, AccentGreen), 1f))
			{
				e.Graphics.FillRectangle(brush, headerRect);
				e.Graphics.DrawLine(pen, headerRect.Left, headerRect.Bottom - 1, headerRect.Right, headerRect.Bottom - 1);
			}
			int scrollWidth = 8;
			(int nameWidth, int modifiedWidth2, int sizeWidth2) = GetColumnLayout(bounds.Width, scrollWidth);
			TextRenderer.DrawText(e.Graphics, HeaderText, Font, new Rectangle(6, 0, nameWidth - 8, headerHeight), AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			TextRenderer.DrawText(e.Graphics, LocalizedText.IsSpanish ? "MODIFICADO" : "MODIFIED", Font, new Rectangle(6 + nameWidth, 0, modifiedWidth2 - 6, headerHeight), AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			TextRenderer.DrawText(e.Graphics, LocalizedText.IsSpanish ? "TAMANO" : "SIZE", Font, new Rectangle(6 + nameWidth + modifiedWidth2, 0, sizeWidth2 - 6, headerHeight), AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			using (Pen dividerPen = new Pen(Color.FromArgb(34, AccentGreen), 1f))
			{
				e.Graphics.DrawLine(dividerPen, nameWidth, 2, nameWidth, bounds.Height - 2);
				e.Graphics.DrawLine(dividerPen, nameWidth + modifiedWidth2, 2, nameWidth + modifiedWidth2, bounds.Height - 2);
			}
			Rectangle bodyRect = new Rectangle(0, headerHeight, bounds.Width, Math.Max(0, bounds.Height - headerHeight));
			if (bodyRect.Height <= 0)
			{
				return;
			}
			if (!string.IsNullOrWhiteSpace(message))
			{
				TextRenderer.DrawText(e.Graphics, message, Font, new Rectangle(6, headerHeight + 6, bounds.Width - 16, bodyRect.Height - 12), ForeColor, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
				return;
			}
			int rowHeight = GetRowHeight();
			int visibleRowCount = GetVisibleRowCount();
			int lastIndex = Math.Min(entries.Count, firstVisibleIndex + visibleRowCount);
			int drawRow = 0;
			for (int i = firstVisibleIndex; i < lastIndex; i++)
			{
				bool flag = Focused && i == selectedIndex;
				int y = headerHeight + drawRow * rowHeight;
				Rectangle rowRect = new Rectangle(0, y, Math.Max(1, bounds.Width - scrollWidth), rowHeight);
				if (!flag && drawRow % 2 == 1)
				{
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(56, 20, 40, 20));
					e.Graphics.FillRectangle(brush2, rowRect);
				}
				if (flag)
				{
					using SolidBrush brush3 = new SolidBrush(Color.FromArgb(156, 52, 96, 42));
					using Pen pen2 = new Pen(Color.FromArgb(214, AccentGreen), 1f);
					e.Graphics.FillRectangle(brush3, rowRect);
					e.Graphics.DrawRectangle(pen2, rowRect.X, rowRect.Y, rowRect.Width - 1, rowRect.Height - 1);
				}
				FileEntryView entry = entries[i];
				string prefix = entry.IsDirectory ? "[DIR] " : "      ";
				string modified = entry.ModifiedUtc.HasValue ? entry.ModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "--";
				string size = FormatEntrySize(entry);
				TextRenderer.DrawText(e.Graphics, prefix + entry.Name, Font, new Rectangle(6, y, nameWidth - 8, rowHeight), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, modified, Font, new Rectangle(6 + nameWidth, y, modifiedWidth2 - 8, rowHeight), Color.WhiteSmoke, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, size, Font, new Rectangle(6 + nameWidth + modifiedWidth2, y, sizeWidth2 - 8, rowHeight), Color.WhiteSmoke, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				drawRow++;
			}
			DrawScrollBar(e.Graphics, visibleRowCount, headerHeight);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			base.OnGotFocus(e);
			Invalidate();
		}

		protected override void OnLostFocus(EventArgs e)
		{
			base.OnLostFocus(e);
			Invalidate();
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (TryStartScrollDrag(e.Location))
			{
				dragPending = false;
				dragIndex = -1;
				return;
			}
			if (TryStartColumnResize(e.Location))
			{
				dragPending = false;
				dragIndex = -1;
				return;
			}
			if (!Focused && CanFocus)
			{
				Focus();
			}
			int itemIndexAt = GetItemIndexAt(e.Y);
			if (itemIndexAt >= 0 && itemIndexAt < entries.Count)
			{
				selectedIndex = itemIndexAt;
				if (e.Button == MouseButtons.Left)
				{
					dragPending = true;
					dragIndex = itemIndexAt;
					dragStartPoint = e.Location;
				}
				else
				{
					dragPending = false;
					dragIndex = -1;
				}
				Invalidate();
				if (e.Button == MouseButtons.Right)
				{
					ItemContextRequested?.Invoke(itemIndexAt, PointToScreen(e.Location));
				}
			}
			else if (e.Button == MouseButtons.Right)
			{
				dragPending = false;
				dragIndex = -1;
				EmptyAreaContextRequested?.Invoke(PointToScreen(e.Location));
			}
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			int itemIndexAt = GetItemIndexAt(e.Y);
			if (itemIndexAt >= 0 && itemIndexAt < entries.Count)
			{
				selectedIndex = itemIndexAt;
				Invalidate();
				ItemActivated?.Invoke(itemIndexAt);
			}
		}

		private static string FormatEntrySize(FileEntryView entry)
		{
			if (entry.Name == ".." || entry.IsDirectory)
			{
				return string.Empty;
			}
			if (!entry.SizeBytes.HasValue)
			{
				return "--";
			}
			double num = entry.SizeBytes.Value;
			string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
			int num2 = 0;
			while (num >= 1024.0 && num2 < array.Length - 1)
			{
				num /= 1024.0;
				num2++;
			}
			return (num2 == 0 ? num.ToString("0", CultureInfo.InvariantCulture) : num.ToString("0.#", CultureInfo.InvariantCulture)) + " " + array[num2];
		}

		private int GetRowHeight()
		{
			return Math.Max(18, TextRenderer.MeasureText("W", Font).Height + 3);
		}

		private int GetVisibleRowCount()
		{
			int num = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
			return Math.Max(1, (base.Height - num) / GetRowHeight());
		}

		private int GetItemIndexAt(int y)
		{
			int num = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
			if (y < num)
			{
				return -1;
			}
			int rowHeight = GetRowHeight();
			return firstVisibleIndex + Math.Max(0, (y - num) / rowHeight);
		}

		private void DrawScrollBar(Graphics graphics, int visibleRows, int headerHeight)
		{
			if (entries.Count <= visibleRows || visibleRows <= 0)
			{
				return;
			}
			Rectangle rectangle = GetScrollTrackRect(headerHeight);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(26, 58, 76, 84));
			graphics.FillRectangle(brush, rectangle);
			double num = (double)visibleRows / (double)entries.Count;
			int height = Math.Max(14, (int)(rectangle.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, entries.Count - visibleRows);
			int y = rectangle.Top + (int)((rectangle.Height - height) * num2);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(130, AccentCyan));
			graphics.FillRectangle(brush2, new Rectangle(rectangle.Left, y, rectangle.Width, height));
		}

		private Rectangle GetScrollTrackRect(int headerHeight)
		{
			return new Rectangle(base.Width - 7, headerHeight + 2, 4, Math.Max(10, base.Height - headerHeight - 4));
		}

		private Rectangle GetScrollThumbRect(int visibleRows, int headerHeight)
		{
			if (entries.Count <= visibleRows || visibleRows <= 0)
			{
				return Rectangle.Empty;
			}
			Rectangle scrollTrackRect = GetScrollTrackRect(headerHeight);
			double num = (double)visibleRows / (double)entries.Count;
			int height = Math.Max(14, (int)(scrollTrackRect.Height * num));
			double num2 = (double)firstVisibleIndex / (double)Math.Max(1, entries.Count - visibleRows);
			int y = scrollTrackRect.Top + (int)((scrollTrackRect.Height - height) * num2);
			return new Rectangle(scrollTrackRect.Left, y, scrollTrackRect.Width, height);
		}

		private bool TryStartScrollDrag(Point location)
		{
			int num = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
			int visibleRowCount = GetVisibleRowCount();
			Rectangle scrollTrackRect = GetScrollTrackRect(num);
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleRowCount, num);
			if (scrollThumbRect != Rectangle.Empty && scrollThumbRect.Contains(location))
			{
				draggingScrollThumb = true;
				dragScrollOffsetY = location.Y - scrollThumbRect.Top;
				Capture = true;
				return true;
			}
			if (scrollThumbRect != Rectangle.Empty && scrollTrackRect.Contains(location))
			{
				UpdateScrollFromThumbTop(location.Y - scrollThumbRect.Height / 2, visibleRowCount, scrollTrackRect, scrollThumbRect.Height);
				return true;
			}
			return false;
		}

		private void UpdateScrollDrag(int mouseY)
		{
			int num = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
			int visibleRowCount = GetVisibleRowCount();
			Rectangle scrollTrackRect = GetScrollTrackRect(num);
			Rectangle scrollThumbRect = GetScrollThumbRect(visibleRowCount, num);
			if (scrollThumbRect == Rectangle.Empty)
			{
				return;
			}
			UpdateScrollFromThumbTop(mouseY - dragScrollOffsetY, visibleRowCount, scrollTrackRect, scrollThumbRect.Height);
		}

		private void UpdateScrollFromThumbTop(int thumbTop, int visibleRows, Rectangle trackRect, int thumbHeight)
		{
			if (entries.Count <= visibleRows || visibleRows <= 0)
			{
				return;
			}
			int num = Math.Max(1, entries.Count - visibleRows);
			int num2 = Math.Max(1, trackRect.Height - thumbHeight);
			int num3 = Math.Clamp(thumbTop, trackRect.Top, trackRect.Bottom - thumbHeight);
			double num4 = (double)(num3 - trackRect.Top) / (double)num2;
			firstVisibleIndex = Math.Clamp((int)Math.Round(num * num4), 0, num);
			Invalidate();
		}

		private void EndScrollDrag()
		{
			draggingScrollThumb = false;
			Capture = false;
		}

		private (int NameWidth, int ModifiedWidth, int SizeWidth) GetColumnLayout(int totalWidth, int scrollWidth)
		{
			int availableWidth = Math.Max(180, totalWidth - scrollWidth - 10);
			int num = Math.Clamp(modifiedWidth, 144, Math.Max(144, availableWidth - 120));
			int num2 = Math.Clamp(sizeWidth, 72, Math.Max(72, availableWidth - num - 96));
			int num3 = availableWidth - num - num2;
			if (num3 < 96)
			{
				int num4 = 96 - num3;
				if (num2 - num4 >= 72)
				{
					num2 -= num4;
				}
				else if (num - num4 >= 144)
				{
					num -= num4;
				}
				num3 = availableWidth - num - num2;
			}
			modifiedWidth = num;
			sizeWidth = num2;
			return (Math.Max(96, num3), num, num2);
		}

		private bool TryStartColumnResize(Point location)
		{
			int headerHeight = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
			if (location.Y > headerHeight + 3)
			{
				return false;
			}
			(int nameWidth, int modifiedWidth2, int sizeWidth2) = GetColumnLayout(base.Width, 8);
			int num = 5;
			if (Math.Abs(location.X - nameWidth) <= num)
			{
				activeResizeColumn = ResizeColumn.NameToModified;
			}
			else if (Math.Abs(location.X - (nameWidth + modifiedWidth2)) <= num)
			{
				activeResizeColumn = ResizeColumn.ModifiedToSize;
			}
			else
			{
				return false;
			}
			resizeAnchorX = location.X;
			resizeAnchorModifiedWidth = modifiedWidth2;
			resizeAnchorSizeWidth = sizeWidth2;
			Cursor = Cursors.VSplit;
			return true;
		}

		private void HandleColumnResizeMove(Point location, bool dragging)
		{
			if (!dragging || activeResizeColumn == ResizeColumn.None)
			{
				if (activeResizeColumn == ResizeColumn.None)
				{
					int headerHeight = Math.Max(20, TextRenderer.MeasureText("W", Font).Height + 7);
					if (location.Y <= headerHeight + 3)
					{
						(int nameWidth, int modifiedWidth2, _) = GetColumnLayout(base.Width, 8);
						int num = 5;
						Cursor = ((Math.Abs(location.X - nameWidth) <= num || Math.Abs(location.X - (nameWidth + modifiedWidth2)) <= num) ? Cursors.VSplit : Cursors.Default);
					}
					else
					{
						Cursor = Cursors.Default;
					}
				}
				return;
			}
			int availableWidth = Math.Max(180, base.Width - 8 - 10);
			int num2 = location.X - resizeAnchorX;
			switch (activeResizeColumn)
			{
			case ResizeColumn.NameToModified:
				modifiedWidth = Math.Clamp(resizeAnchorModifiedWidth - num2, 144, Math.Max(144, availableWidth - sizeWidth - 96));
				break;
			case ResizeColumn.ModifiedToSize:
			{
				int num3 = Math.Clamp(resizeAnchorSizeWidth - num2, 72, Math.Max(72, availableWidth - resizeAnchorModifiedWidth - 96));
				int num4 = availableWidth - resizeAnchorModifiedWidth - num3;
				if (num4 < 96)
				{
					num3 = Math.Max(72, availableWidth - resizeAnchorModifiedWidth - 96);
				}
				sizeWidth = num3;
				break;
			}
			}
			Invalidate();
		}

		private bool draggingColumn(MouseButtons buttons)
		{
			return activeResizeColumn != ResizeColumn.None && buttons == MouseButtons.Left;
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
