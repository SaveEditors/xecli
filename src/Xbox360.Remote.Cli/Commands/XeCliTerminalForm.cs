using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FluentFTP;
using FluentFTP.Exceptions;
using Xbox360.Remote;
using Xbox360.Remote.Cli;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using System.Security.Cryptography;
using Xbox360.Remote.Cli.LocalProfiles;
using XeCli.Localization;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class XeCliTerminalForm : Form, IMessageFilter
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

		public bool DiscordRichPresenceEnabled { get; init; } = true;

		public bool AutoConnect { get; init; }

		public string ThemeName { get; init; } = DefaultTerminalThemeName;
	}

	private sealed class TelemetrySnapshot
	{
		public int SessionEpoch { get; set; }

		public bool Connected { get; set; }

		public int ModulesSessionEpoch { get; set; }

		public int PluginsSessionEpoch { get; set; }

		public string? DebugName { get; set; }

		public string? Motherboard { get; set; }

		public uint? DashboardVersion { get; set; }

		public string? ExecutionState { get; set; }

		public uint? TitleId { get; set; }

		public string? TitleName { get; set; }

		public string? RunningXex { get; set; }

		public string? Gamertag { get; set; }

		public string? Xuid { get; set; }

		public string? SignInStateText { get; set; }

		public double? CpuTemp { get; set; }

		public double? GpuTemp { get; set; }

		public double? EdramTemp { get; set; }

		public double? BoardTemp { get; set; }

		public string? ThermalSource { get; set; }

		public bool ThermalIntegrityFailure { get; set; }

		public int? FtpPort { get; set; }

		public string? FtpUser { get; set; }

		public bool? FtpServiceReachable { get; set; }

		public string? ErrorText { get; set; }

		public List<DriveInventoryEntry> Drives { get; } = new List<DriveInventoryEntry>();

		public List<string> Plugins { get; } = new List<string>();

		public List<string> Modules { get; } = new List<string>();

		public double FtpRxKbps { get; set; }

		public double FtpTxKbps { get; set; }
	}

	private enum FtpTrafficDirection
	{
		Receive,
		Send
	}

	private readonly record struct FtpTrafficRateSample(
		double RxKbps,
		double TxKbps,
		long RxBytes,
		long TxBytes);

	private sealed class FtpTrafficMeter
	{
		private readonly object syncRoot = new object();

		private long receivedBytes;

		private long sentBytes;

		private long sampledReceivedBytes;

		private long sampledSentBytes;

		private DateTime sampledUtc;

		internal void Record(FtpTrafficDirection direction, long byteCount)
		{
			if (byteCount <= 0)
			{
				return;
			}

			lock (syncRoot)
			{
				if (direction == FtpTrafficDirection.Receive)
				{
					receivedBytes = SaturatingAdd(receivedBytes, byteCount);
				}
				else
				{
					sentBytes = SaturatingAdd(sentBytes, byteCount);
				}
			}
		}

		private static long SaturatingAdd(long current, long increment)
		{
			return current > long.MaxValue - increment ? long.MaxValue : current + increment;
		}

		internal FtpTrafficRateSample Sample(DateTime utcNow)
		{
			lock (syncRoot)
			{
				double elapsedSeconds = sampledUtc == default
					? 0.0
					: Math.Max((utcNow - sampledUtc).TotalSeconds, 0.001);
				long receivedDelta = Math.Max(0L, receivedBytes - sampledReceivedBytes);
				long sentDelta = Math.Max(0L, sentBytes - sampledSentBytes);
				sampledReceivedBytes = receivedBytes;
				sampledSentBytes = sentBytes;
				sampledUtc = utcNow;

				if (elapsedSeconds <= 0.0)
				{
					return new FtpTrafficRateSample(0.0, 0.0, receivedDelta, sentDelta);
				}

				return new FtpTrafficRateSample(
					receivedDelta * 8.0 / 1000.0 / elapsedSeconds,
					sentDelta * 8.0 / 1000.0 / elapsedSeconds,
					receivedDelta,
					sentDelta);
			}
		}

		internal void Reset(DateTime utcNow)
		{
			lock (syncRoot)
			{
				receivedBytes = 0L;
				sentBytes = 0L;
				sampledReceivedBytes = 0L;
				sampledSentBytes = 0L;
				sampledUtc = utcNow;
			}
		}
	}

	private sealed class FtpProgressByteTracker
	{
		private long lastTransferredBytes;

		internal long Observe(long transferredBytes)
		{
			long normalized = Math.Max(0L, transferredBytes);
			long previous = Interlocked.Exchange(ref lastTransferredBytes, normalized);
			return normalized >= previous ? normalized - previous : normalized;
		}
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

	internal enum TransferConflictDecision
	{
		Replace,
		KeepBoth,
		Skip,
		Cancel
	}

	internal enum TransferEntryKind
	{
		None,
		File,
		Directory
	}

	internal readonly record struct TransferItemMetadata(
		long? SizeBytes,
		DateTime? ModifiedUtc);

	internal readonly record struct TransferHashComparison(
		string SourceSha256,
		string DestinationSha256)
	{
		internal bool Matches => string.Equals(SourceSha256, DestinationSha256, StringComparison.OrdinalIgnoreCase);
	}

	internal sealed record TransferConflictPrompt(
		string Direction,
		string SourcePath,
		string DestinationPath,
		string KeepBothPath,
		TransferEntryKind SourceKind,
		TransferEntryKind DestinationKind,
		TransferItemMetadata SourceMetadata = default,
		TransferItemMetadata DestinationMetadata = default,
		Func<CancellationToken, Task<TransferHashComparison>>? CompareHashesAsync = null);

	private sealed record TransferConflictDetailSpec(
		string Caption,
		string Path,
		TransferEntryKind Kind,
		TransferItemMetadata Metadata,
		Color AccentColor,
		bool Emphasized,
		bool IncludeMetadata);

	private sealed class TransferConflictDialogResources
	{
		internal required Font CaptionFont { get; init; }

		internal required Font NameFont { get; init; }

		internal required Font PathFont { get; init; }

		internal required Font MetadataCaptionFont { get; init; }

		internal required Font MetadataValueFont { get; init; }

		internal required ToolTip ToolTip { get; init; }
	}

	internal readonly record struct TransferConflictResult(
		TransferConflictDecision Decision,
		bool ApplyToRemaining);

	internal readonly record struct TransferDestinationResolution(
		string? Path,
		bool OverwriteApproved)
	{
		internal bool Skipped => string.IsNullOrWhiteSpace(Path);
	}

	internal sealed class TransferConflictPolicy
	{
		internal TransferConflictDecision? RemainingDecision { get; set; }

		internal int ConflictCount { get; set; }

		internal int SkippedCount { get; set; }
	}

	private const int MaxTerminalCharacters = 64000;

	private const int TerminalFlushBatchSize = 1024;

	private const int MouseWheelDelta = 120;

	private const int WmMouseWheel = 0x020A;

	private const int WmSetRedraw = 0x000B;

	private const int EmGetFirstVisibleLine = 0x00CE;

	private const int EmLineScroll = 0x00B6;

	private const int EmScrollCaret = 0x00B7;

	private const int ConnectBannerMinimumHoldMs = 1200;

	private const int InventoryLoadingGraceMs = 2000;

	private const int TelemetryPollIntervalMs = 30000;

	private const int FirstIncompleteTelemetryRetryDelaySeconds = 30;

	private const int SubsequentIncompleteTelemetryRetryDelaySeconds = 120;

	private const int FtpTrafficSampleIntervalMs = 500;


	private const int LeftRailWidth = 284;

	private const int RightRailWidth = 348;

	private const int MinLeftRailWidth = 272;

	private const int MaxLeftRailWidth = 304;

	private const int MinRightRailWidth = 316;

	private const int MaxRightRailWidth = 392;

	private const int MinCenterColumnWidth = 640;

	private const int DefaultFileBrowserHeight = 390;

	private const int MinFileBrowserHeight = 390;

	private const int MaxFileBrowserHeight = 760;

	private const int FooterStatusBarHeight = 43;

	private const int ScreenshotNotificationWidth = 448;

	private const int ScreenshotNotificationHeight = 112;

	private const int ScreenshotNotificationMargin = 16;

	private const int ScreenshotNotificationDurationMs = 8000;

	private const int ThemePersistenceDelayMs = 250;

	private const int CenterHeroHeight = 44;

	private const int FileBrowserResizeGripHeight = 12;

	private const int FileBrowserPaneSplitterWidth = 10;

	private const int MinFileBrowserPaneWidth = 600;

	private const int CommandInputPromptLeftInset = 12;

	private const int CommandInputPromptWidth = 12;

	private const int CommandInputTextGap = 10;

	private const int CommandInputOuterRadius = 8;

	private const int CommandInputFramePaintClearance = 4;

	private const int CommandInputRightFrameClearance = CommandInputOuterRadius + CommandInputFramePaintClearance;

	private const int CommandInputMinimumHeight = 44;

	private const int CommandInputRowHeight = 52;

	private const int CommandSubmitColumnWidth = 44;

	private const int CommandSurfaceRadius = 12;

	internal const string DefaultTerminalThemeName = "cyan";

	internal static IReadOnlyList<string> TerminalThemeNames => TerminalThemes.Select(theme => theme.Name).ToArray();

	private sealed record TerminalThemePalette(
		string Name,
		Color ShellBackground,
		Color CardBackground,
		Color TerminalBackground,
		Color AccentCyan,
		Color AccentPink,
		Color AccentGreen,
		Color AccentDim,
		Color BorderColor,
		Color WarningColor,
		Color LoadingStateColor,
		Color EmptyStateColor,
		Color FooterChipBackground
	);

	private static readonly TerminalThemePalette[] TerminalThemes =
	[
		new TerminalThemePalette(
			"matrix",
			Color.FromArgb(7, 10, 9),
			Color.FromArgb(10, 14, 13),
			Color.FromArgb(8, 13, 12),
			Color.FromArgb(242, 246, 242),
			Color.FromArgb(166, 201, 154),
			Color.FromArgb(148, 255, 102),
			Color.FromArgb(170, 186, 166),
			Color.FromArgb(58, 112, 54),
			Color.FromArgb(198, 136, 88),
			Color.FromArgb(118, 224, 208),
			Color.FromArgb(156, 184, 148),
			Color.FromArgb(12, 20, 15)),
		new TerminalThemePalette(
			"cyan",
			Color.FromArgb(5, 12, 16),
			Color.FromArgb(7, 18, 22),
			Color.FromArgb(5, 16, 20),
			Color.FromArgb(235, 250, 255),
			Color.FromArgb(130, 204, 220),
			Color.FromArgb(90, 220, 245),
			Color.FromArgb(160, 196, 205),
			Color.FromArgb(48, 122, 138),
			Color.FromArgb(222, 168, 92),
			Color.FromArgb(116, 232, 226),
			Color.FromArgb(150, 188, 198),
			Color.FromArgb(9, 24, 30)),
		new TerminalThemePalette(
			"amber",
			Color.FromArgb(15, 11, 5),
			Color.FromArgb(22, 15, 7),
			Color.FromArgb(17, 12, 6),
			Color.FromArgb(255, 246, 224),
			Color.FromArgb(226, 178, 112),
			Color.FromArgb(255, 194, 92),
			Color.FromArgb(208, 184, 142),
			Color.FromArgb(132, 92, 42),
			Color.FromArgb(232, 126, 92),
			Color.FromArgb(244, 204, 128),
			Color.FromArgb(190, 164, 118),
			Color.FromArgb(28, 18, 8)),
		new TerminalThemePalette(
			"mono",
			Color.FromArgb(9, 10, 12),
			Color.FromArgb(14, 16, 18),
			Color.FromArgb(10, 12, 14),
			Color.FromArgb(238, 240, 242),
			Color.FromArgb(188, 196, 204),
			Color.FromArgb(210, 220, 228),
			Color.FromArgb(158, 166, 174),
			Color.FromArgb(92, 102, 112),
			Color.FromArgb(224, 158, 104),
			Color.FromArgb(176, 210, 224),
			Color.FromArgb(160, 168, 176),
			Color.FromArgb(18, 21, 24)),
		new TerminalThemePalette(
			"carbon",
			Color.FromArgb(8, 9, 10),
			Color.FromArgb(14, 15, 16),
			Color.FromArgb(10, 11, 12),
			Color.FromArgb(236, 240, 238),
			Color.FromArgb(154, 190, 176),
			Color.FromArgb(92, 214, 174),
			Color.FromArgb(164, 174, 170),
			Color.FromArgb(72, 94, 86),
			Color.FromArgb(226, 152, 94),
			Color.FromArgb(116, 208, 196),
			Color.FromArgb(158, 172, 166),
			Color.FromArgb(18, 20, 20)),
		new TerminalThemePalette(
			"ruby",
			Color.FromArgb(14, 6, 8),
			Color.FromArgb(22, 10, 13),
			Color.FromArgb(17, 7, 10),
			Color.FromArgb(255, 241, 240),
			Color.FromArgb(231, 145, 153),
			Color.FromArgb(238, 76, 88),
			Color.FromArgb(214, 177, 181),
			Color.FromArgb(139, 54, 61),
			Color.FromArgb(244, 182, 90),
			Color.FromArgb(239, 143, 151),
			Color.FromArgb(194, 157, 160),
			Color.FromArgb(31, 12, 15)),
		new TerminalThemePalette(
			"xenon",
			Color.FromArgb(6, 10, 13),
			Color.FromArgb(10, 16, 20),
			Color.FromArgb(7, 13, 17),
			Color.FromArgb(236, 250, 252),
			Color.FromArgb(132, 190, 212),
			Color.FromArgb(88, 224, 214),
			Color.FromArgb(160, 190, 202),
			Color.FromArgb(50, 98, 118),
			Color.FromArgb(232, 160, 86),
			Color.FromArgb(116, 224, 228),
			Color.FromArgb(152, 184, 194),
			Color.FromArgb(10, 22, 28)),
		new TerminalThemePalette(
			"violet",
			Color.FromArgb(10, 8, 14),
			Color.FromArgb(16, 13, 22),
			Color.FromArgb(12, 10, 18),
			Color.FromArgb(246, 240, 255),
			Color.FromArgb(188, 160, 228),
			Color.FromArgb(166, 132, 255),
			Color.FromArgb(188, 178, 210),
			Color.FromArgb(82, 64, 122),
			Color.FromArgb(238, 158, 98),
			Color.FromArgb(196, 176, 248),
			Color.FromArgb(176, 166, 196),
			Color.FromArgb(22, 17, 30)),
		new TerminalThemePalette(
			"aurora",
			Color.FromArgb(4, 12, 15),
			Color.FromArgb(8, 20, 23),
			Color.FromArgb(5, 15, 18),
			Color.FromArgb(234, 251, 255),
			Color.FromArgb(234, 155, 196),
			Color.FromArgb(98, 242, 168),
			Color.FromArgb(169, 200, 194),
			Color.FromArgb(47, 128, 109),
			Color.FromArgb(242, 176, 75),
			Color.FromArgb(120, 223, 232),
			Color.FromArgb(161, 183, 168),
			Color.FromArgb(13, 36, 35)),
		new TerminalThemePalette(
			"steel",
			Color.FromArgb(10, 12, 16),
			Color.FromArgb(17, 21, 26),
			Color.FromArgb(13, 16, 21),
			Color.FromArgb(242, 244, 248),
			Color.FromArgb(196, 208, 232),
			Color.FromArgb(132, 197, 255),
			Color.FromArgb(183, 192, 206),
			Color.FromArgb(72, 84, 100),
			Color.FromArgb(242, 168, 94),
			Color.FromArgb(143, 216, 255),
			Color.FromArgb(170, 178, 188),
			Color.FromArgb(21, 26, 32)),
		new TerminalThemePalette(
			"sunset",
			Color.FromArgb(15, 8, 12),
			Color.FromArgb(24, 13, 17),
			Color.FromArgb(18, 10, 14),
			Color.FromArgb(255, 241, 226),
			Color.FromArgb(255, 137, 122),
			Color.FromArgb(255, 194, 94),
			Color.FromArgb(220, 178, 156),
			Color.FromArgb(128, 68, 54),
			Color.FromArgb(118, 222, 202),
			Color.FromArgb(246, 174, 114),
			Color.FromArgb(202, 164, 146),
			Color.FromArgb(31, 16, 20)),
		new TerminalThemePalette(
			"enterprise",
			Color.FromArgb(7, 10, 16),
			Color.FromArgb(13, 20, 30),
			Color.FromArgb(9, 16, 25),
			Color.FromArgb(239, 246, 255),
			Color.FromArgb(195, 158, 176),
			Color.FromArgb(76, 137, 230),
			Color.FromArgb(170, 187, 208),
			Color.FromArgb(48, 72, 108),
			Color.FromArgb(231, 166, 83),
			Color.FromArgb(102, 167, 238),
			Color.FromArgb(151, 169, 188),
			Color.FromArgb(15, 22, 33)),
		new TerminalThemePalette(
			"operations",
			Color.FromArgb(8, 10, 11),
			Color.FromArgb(15, 17, 19),
			Color.FromArgb(10, 13, 15),
			Color.FromArgb(242, 244, 240),
			Color.FromArgb(210, 126, 146),
			Color.FromArgb(98, 198, 166),
			Color.FromArgb(174, 182, 176),
			Color.FromArgb(82, 104, 102),
			Color.FromArgb(236, 174, 84),
			Color.FromArgb(124, 210, 222),
			Color.FromArgb(156, 166, 160),
			Color.FromArgb(18, 22, 24)),
		new TerminalThemePalette(
			"debugger",
			Color.FromArgb(6, 7, 9),
			Color.FromArgb(13, 14, 17),
			Color.FromArgb(8, 10, 13),
			Color.FromArgb(242, 246, 250),
			Color.FromArgb(224, 146, 96),
			Color.FromArgb(104, 205, 232),
			Color.FromArgb(170, 184, 196),
			Color.FromArgb(64, 86, 108),
			Color.FromArgb(232, 134, 110),
			Color.FromArgb(134, 220, 240),
			Color.FromArgb(154, 168, 178),
			Color.FromArgb(17, 20, 25)),
		new TerminalThemePalette(
			"command",
			Color.FromArgb(7, 8, 8),
			Color.FromArgb(14, 15, 14),
			Color.FromArgb(9, 10, 10),
			Color.FromArgb(242, 244, 236),
			Color.FromArgb(172, 186, 164),
			Color.FromArgb(214, 202, 118),
			Color.FromArgb(180, 184, 168),
			Color.FromArgb(92, 94, 78),
			Color.FromArgb(232, 146, 88),
			Color.FromArgb(198, 218, 158),
			Color.FromArgb(160, 164, 148),
			Color.FromArgb(18, 20, 18)),
		new TerminalThemePalette(
			"signal",
			Color.FromArgb(6, 9, 12),
			Color.FromArgb(12, 16, 20),
			Color.FromArgb(8, 12, 16),
			Color.FromArgb(238, 246, 248),
			Color.FromArgb(232, 150, 184),
			Color.FromArgb(122, 216, 176),
			Color.FromArgb(168, 188, 190),
			Color.FromArgb(58, 96, 104),
			Color.FromArgb(236, 176, 88),
			Color.FromArgb(120, 218, 230),
			Color.FromArgb(152, 172, 176),
			Color.FromArgb(14, 23, 28)),
		new TerminalThemePalette(
			"graphite",
			Color.FromArgb(8, 9, 11),
			Color.FromArgb(15, 17, 19),
			Color.FromArgb(10, 12, 14),
			Color.FromArgb(240, 242, 238),
			Color.FromArgb(188, 166, 126),
			Color.FromArgb(104, 206, 188),
			Color.FromArgb(172, 178, 174),
			Color.FromArgb(78, 86, 88),
			Color.FromArgb(226, 152, 92),
			Color.FromArgb(134, 212, 224),
			Color.FromArgb(158, 166, 164),
			Color.FromArgb(18, 20, 22)),
		new TerminalThemePalette(
			"alpine",
			Color.FromArgb(6, 11, 12),
			Color.FromArgb(11, 18, 19),
			Color.FromArgb(8, 14, 15),
			Color.FromArgb(238, 248, 246),
			Color.FromArgb(154, 194, 184),
			Color.FromArgb(112, 224, 158),
			Color.FromArgb(170, 194, 188),
			Color.FromArgb(58, 112, 98),
			Color.FromArgb(232, 170, 92),
			Color.FromArgb(126, 220, 210),
			Color.FromArgb(152, 180, 172),
			Color.FromArgb(14, 26, 24)),
		new TerminalThemePalette(
			"console",
			Color.FromArgb(9, 8, 7),
			Color.FromArgb(17, 15, 13),
			Color.FromArgb(12, 11, 9),
			Color.FromArgb(245, 242, 232),
			Color.FromArgb(140, 184, 176),
			Color.FromArgb(226, 204, 116),
			Color.FromArgb(188, 182, 164),
			Color.FromArgb(94, 90, 72),
			Color.FromArgb(226, 118, 94),
			Color.FromArgb(196, 216, 154),
			Color.FromArgb(164, 160, 146),
			Color.FromArgb(21, 19, 16))
	];

	private static readonly Color SemanticFailureBaseColor = Color.FromArgb(244, 112, 128);
	private static readonly Color SemanticSuccessBaseColor = Color.FromArgb(92, 218, 137);
	private static readonly Color ThermalCautionColor = Color.FromArgb(244, 211, 94);
	private static readonly Color ThermalHotColor = Color.FromArgb(244, 153, 72);

	private readonly record struct ThermalThresholdProfile(
		int CpuTarget,
		int GpuTarget,
		int EdramTarget,
		int CpuCritical,
		int GpuCritical,
		int EdramCritical);

	private static readonly ThermalThresholdProfile UnknownThermalThresholds = new(80, 78, 78, 95, 95, 95);

	private static readonly IReadOnlyDictionary<string, ThermalThresholdProfile> ThermalThresholdsByMotherboard =
		new Dictionary<string, ThermalThresholdProfile>(StringComparer.OrdinalIgnoreCase)
		{
			["Xenon"] = new(80, 83, 85, 100, 110, 117),
			["XenonRefurb"] = new(80, 75, 78, 100, 100, 102),
			["Zephyr"] = new(80, 75, 78, 100, 100, 102),
			["Falcon"] = new(80, 75, 78, 100, 100, 102),
			["Jasper"] = new(80, 71, 73, 95, 90, 92),
			["Tonasket"] = new(80, 75, 77, 95, 90, 92),
			["Trinity"] = new(82, 78, 76, 89, 82, 82),
			["Corona"] = new(82, 78, 76, 89, 82, 82),
			["Winchester"] = new(82, 78, 76, 91, 82, 91)
		};

	private static readonly TerminalThemePalette DefaultTerminalTheme = TerminalThemes.First(theme =>
		string.Equals(theme.Name, DefaultTerminalThemeName, StringComparison.OrdinalIgnoreCase));

	private static Color ShellBackground = DefaultTerminalTheme.ShellBackground;

	private static Color CardBackground = DefaultTerminalTheme.CardBackground;

	private static Color TerminalBackground = DefaultTerminalTheme.TerminalBackground;

	private static Color AccentCyan = DefaultTerminalTheme.AccentCyan;

	private static Color AccentPink = DefaultTerminalTheme.AccentPink;

	private static Color FailureStateColor = ComputeFailureStateColor(DefaultTerminalTheme);

	private static Color SuccessStateColor = ComputeSuccessStateColor(DefaultTerminalTheme);

	private static Color AccentGreen = DefaultTerminalTheme.AccentGreen;

	private static Color AccentDim = DefaultTerminalTheme.AccentDim;

	private static Color BorderColor = DefaultTerminalTheme.BorderColor;

	private static Color WarningColor = DefaultTerminalTheme.WarningColor;

	private static Color LoadingStateColor = DefaultTerminalTheme.LoadingStateColor;

	private static Color EmptyStateColor = DefaultTerminalTheme.EmptyStateColor;

	private static Color FooterChipBackground = DefaultTerminalTheme.FooterChipBackground;

	private static Color PrimaryTextColor => AccentCyan;


	private readonly TerminalOptions options;

	private readonly Font shellFont = new Font("Consolas", 8.75f, FontStyle.Regular, GraphicsUnit.Point);

	private readonly Font shellFontBold = new Font("Consolas", 9.25f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Font shellOutputFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

	private readonly Font headerFont = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Font microFont = new Font("Consolas", 6.75f, FontStyle.Bold, GraphicsUnit.Point);

	private readonly Label headerLabel = new Label();

	private readonly Label connectionStatusLabel = new Label();

	private ConnectionUiState connectionStatusIndicatorState = ConnectionUiState.Disconnected;

	private Color connectionStatusIndicatorColor = AccentDim;

	private readonly Label statusToastLabel = new Label();

	private readonly Panel screenshotNotificationPanel = new Panel();

	private readonly PictureBox screenshotNotificationThumbnail = new PictureBox();

	private readonly Label screenshotNotificationTitleLabel = new Label();

	private readonly Label screenshotNotificationDetailLabel = new Label();

	private readonly Button screenshotNotificationPreviewButton = new TerminalButton();

	private readonly Button screenshotNotificationOpenButton = new TerminalButton();

	private readonly Button screenshotNotificationFolderButton = new TerminalButton();

	private readonly Button screenshotNotificationCloseButton = new TerminalButton();

	private readonly ToolTip screenshotNotificationToolTip = new ToolTip
	{
		ShowAlways = true
	};

	private readonly Label leftStatusLabel = new Label();

	private readonly Label leftConsoleHeaderLabel = new Label();

	private readonly Label leftConsoleLabel = new Label();

	private readonly StructuredInfoPanel leftConsoleInfoPanel = new StructuredInfoPanel();

	private readonly Label leftSignInLabel = new Label();

	private readonly Label shellTargetLabel = new Label();

	private readonly Label rightNetworkLabel = new Label();

	private readonly StructuredInfoPanel rightNetworkInfoPanel = new StructuredInfoPanel();

	private readonly Label rightStatusHeaderLabel = new Label();

	private readonly TrafficRateLabel rightTempLabel = new TrafficRateLabel();

	private readonly Label rightTrafficHeaderLabel = new Label();

	private readonly Label rightDetailLabel = new Label();

	private readonly Label rightDrivesLabel = new Label();

	private readonly Button connectButton = new TerminalButton();

	private readonly Button disconnectButton = new TerminalButton();

	private readonly Button screenshotButton = new TerminalButton();

	private readonly Button languageButton = new TerminalButton();

	private readonly Button avatarButton = new TerminalButton();

	private readonly Button themeButton = new TerminalButton();

	private readonly Button settingsButton = new TerminalButton();

	private readonly Button commandSubmitButton = new TerminalButton();

	private readonly TextBox targetIpTextBox = new TextBox();

	private readonly TextBox targetPortTextBox = new TextBox();

	private readonly Panel targetIpHostPanel = new Panel();

	private readonly Panel targetPortHostPanel = new Panel();

	private readonly Button targetApplyButton = new Button();

	private readonly Button pluginsTabButton = new TerminalButton();

	private readonly Button modulesTabButton = new TerminalButton();

	private readonly SlimListPanel inventoryList = new SlimListPanel();

	private readonly DriveInventoryPanel drivesList = new DriveInventoryPanel();

	private readonly TransferQueuePanel transferQueueList = new TransferQueuePanel();

	private readonly List<TransferQueueEntry> transferQueueEntries = new List<TransferQueueEntry>();

	private readonly FtpTrafficGraphControl ftpTrafficGraph = new FtpTrafficGraphControl();

	private readonly FooterSegmentLabel footerStatusLabel = new FooterSegmentLabel { ShowLeadingSeparator = true };

	private readonly Label footerVersionLabel = new Label();

	private readonly FooterSegmentLabel footerPresenceLabel = new FooterSegmentLabel { ShowLeadingSeparator = true };

	private readonly FooterSegmentLabel footerTargetLabel = new FooterSegmentLabel { ShowLeadingSeparator = true };

	private readonly FooterThermalLabel footerThermalLabel = new FooterThermalLabel { ShowLeadingSeparator = true };

	private readonly Label shellBadgeLabel = new Label();

	private readonly Label shellScaffoldLabel = new Label();

	private readonly TerminalOutputBox terminalOutput = new TerminalOutputBox();

	private readonly TerminalScrollIndicator terminalScrollIndicator = new TerminalScrollIndicator();

	private readonly TerminalCommandTextBox commandInput = new TerminalCommandTextBox();

	private readonly ToolTip commandInputToolTip = new ToolTip
	{
		ShowAlways = true
	};

	private readonly ToolTip footerThermalToolTip = new ToolTip
	{
		ShowAlways = true
	};

	private readonly ToolTip filePathToolTip = new ToolTip
	{
		ShowAlways = true,
		AutoPopDelay = 12000,
		InitialDelay = 450,
		ReshowDelay = 100
	};

	private readonly Panel commandInputHost = new Panel();

	private readonly TableLayoutPanel commandInputLayout = new TableLayoutPanel();

	private readonly SuggestionListPanel suggestionList = new SuggestionListPanel();

	private readonly Panel suggestionHost = new Panel();

	private RowStyle? suggestionRowStyle;

	private readonly List<string> commandHistory = new List<string>();

	private int commandHistoryCursor = -1;

	private string commandHistoryDraft = string.Empty;

	private bool commandPaletteOpen;

	private bool commandHistorySearchOpen;

	private readonly Label localPathLabel = new Label();

	private readonly TextBox localPathTextBox = new TextBox();

	private readonly Panel localPathHostPanel = new Panel();

	private readonly Label remotePathLabel = new Label();

	private readonly Button localUpButton = new TerminalButton();

	private readonly Button localRefreshButton = new TerminalButton();

	private readonly Button remoteUpButton = new TerminalButton();

	private readonly Button remoteRefreshButton = new TerminalButton();

	private readonly Button ftpTabButton = new TerminalButton();

	private readonly Button queueTabButton = new TerminalButton();

	private readonly Button localTabButton = new TerminalButton();

	private readonly TextBox remotePathTextBox = new TextBox();

	private readonly Panel remotePathHostPanel = new Panel();

	private RowStyle? remotePathRowStyle;

	private readonly FileGridPanel localFileList = new FileGridPanel();

	private readonly FileGridPanel remoteFileList = new FileGridPanel();

	private readonly Panel remoteContentHost = new Panel();

	private TableLayoutPanel? shellLayoutTable;

	private TableLayoutPanel? primaryShellLayoutTable;

	private ColumnStyle? leftRailColumnStyle;

	private ColumnStyle? rightRailColumnStyle;

	private RowStyle? fileBrowserRowStyle;

	private TableLayoutPanel? fileBrowserSplitLayoutTable;

	private ColumnStyle? fileBrowserLocalColumnStyle;

	private ColumnStyle? fileBrowserRemoteColumnStyle;

	private int fileBrowserHeight = DefaultFileBrowserHeight;

	private bool fileBrowserResizeActive;

	private int fileBrowserResizeStartY;

	private int fileBrowserResizeStartHeight;

	private bool fileBrowserPaneResizeActive;

	private readonly ContextMenuStrip localFileMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip localEmptyMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip remoteFileMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip remoteEmptyMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip terminalOutputMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip inventoryMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip driveMenu = new ContextMenuStrip();

	private readonly ContextMenuStrip diagnosticsMenu = new ContextMenuStrip();

	private readonly System.Windows.Forms.Timer heartbeatTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer telemetryTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer ftpTrafficTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer statusToastTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer screenshotNotificationTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer terminalFlushTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer windowPlacementSaveTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer themePersistenceTimer = new System.Windows.Forms.Timer();

	private readonly Queue<(string Text, Color Color)> pendingTerminalLines = new Queue<(string, Color)>();

	private readonly object pendingTerminalLock = new object();

	private readonly CancellationTokenSource formLifetimeCts = new CancellationTokenSource();

	private bool telemetryPollInFlight;

	private DateTime nextHeavyTelemetryRetryUtc = DateTime.MinValue;

	private int heavyTelemetryRetrySessionEpoch = -1;

	private int heavyTelemetryRetryAttempt;

	private int telemetryOperationEpoch;

	private bool commandInFlight;

	private bool connectAttemptInFlight;

	private DateTime connectAttemptStartedUtc;

	private bool inventoryShowsModules;

	private DateTime modulesInventoryLoadingSinceUtc = DateTime.MinValue;

	private DateTime pluginsInventoryLoadingSinceUtc = DateTime.MinValue;

	private bool shellDisconnected = true;

	private bool connectAttemptFailed;

	private CliErrorEnvelope? lastConnectionFailureEnvelope;

	private string? lastConnectionFailureText;

	private bool showStatusTargetEndpoint;

	private bool terminalFlushScheduled;

	private bool suppressWindowPlacementPersistence;

	private bool suppressSuggestionRefresh;

	private bool fileTransferInFlight;

	private Process? activeCommandProcess;

	private CancellationTokenSource? commandCts;

	private CancellationTokenSource? localBrowseCts;

	private CancellationTokenSource? remoteBrowseCts;

	private CancellationTokenSource? fileTransferCts;

	private bool closeRequestedDuringTransfer;

	private bool allowCloseAfterTransfer;

	private CancellationTokenSource? connectProbeCts;

	private CancellationTokenSource? autoReconnectCts;

	private bool autoReconnectArmed;

	private bool autoReconnectInFlight;

	private bool autoReconnectAttemptExecuting;

	private readonly FtpTrafficMeter ftpTrafficMeter = new FtpTrafficMeter();

	private TelemetrySnapshot? latestSnapshot;

	private readonly List<FileEntryView> localEntries = new List<FileEntryView>();

	private readonly List<FileEntryView> remoteEntries = new List<FileEntryView>();

	private bool remoteBrowserHasLoaded;

	private string? localCurrentPath;

	private string remoteCurrentPath = "/";

	private DateTime nextRemoteRefreshAllowedUtc = DateTime.MinValue;

	private int localBrowseVersion;

	private int remoteBrowseVersion;

	private int localContextIndex = -1;

	private int remoteContextIndex = -1;

	private int inventoryContextIndex = -1;

	private int driveContextIndex = -1;

	private DiscordRpcService? discordRpcService;

	private bool telemetryEnabled;

	private bool discordRichPresenceEnabled;

	private int connectionTimeoutMs;

	private string currentTargetIp;

	private int currentTargetPort;

	private int sessionEpoch;

	private PaneClipboardEntry? paneClipboardEntry;

	private bool remotePaneShowsQueue;

	private bool firstConnectWorkspacePrepared;

	private string? activeTransferCommand;

	private string connectButtonTextSource = "CONNECT";

	private string commandInputPlaceholderSource = "Enter a local command";

	private string connectionStatusTextSource = "Not connected";

	private string footerPresenceTextSource = "IDLE";

	private string footerStatusTextSource = "READY";

	private string? footerOperationStatusOverride;

	private string footerTargetTextSource = "QUEUE: 0";

	private string footerThermalTextSource = "THERMALS --";

	private double? footerCpuTemp;

	private double? footerGpuTemp;

	private double? footerEdramTemp;

	private double? footerBoardTemp;

	private string? footerThermalMotherboard;

	private string? footerThermalSource;

	private string leftConsoleTextSource = BuildDisconnectedConsoleText();

	private string leftSignInTextSource = "INVENTORY";

	private string rightDetailTextSource = BuildDisconnectedSessionText();

	private string rightDrivesTitleTextSource = "DETECTED DRIVES";

	private string rightNetworkTextSource = string.Empty;

	private string rightTempTextSource = "IDLE";

	private string currentThemeName = DefaultTerminalThemeName;

	private string? latestScreenshotPath;

	private Image? screenshotNotificationImage;

	private ScreenshotPreviewForm? screenshotPreviewForm;

	private Color screenshotNotificationAccentColor = AccentGreen;

	private bool screenshotNotificationIsFailure;

	private string screenshotNotificationDetailSource = string.Empty;

	internal XeCliTerminalForm(TerminalOptions options)
	{
		this.options = options;
		currentThemeName = ApplyTerminalTheme(options.ThemeName);
		telemetryEnabled = options.TelemetryEnabled;
		discordRichPresenceEnabled = options.DiscordRichPresenceEnabled;
		connectionTimeoutMs = Math.Clamp(options.TimeoutMs, 1000, 60000);
		currentTargetIp = options.Ip;
		currentTargetPort = options.Port;
		suppressWindowPlacementPersistence = true;
		LocalizedText.Initialize(GetConfiguredUiLanguageCode());
		CultureInfo.CurrentUICulture = LocalizedText.Culture;
		CultureInfo.DefaultThreadCurrentUICulture = LocalizedText.Culture;
		AutoScaleMode = AutoScaleMode.Dpi;
		AutoScaleDimensions = new SizeF(96f, 96f);
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
		DoubleBuffered = true;
		base.Text = "XeCLI Terminal";
		ApplyStartupPlacement();
		base.BackColor = ShellBackground;
		base.ForeColor = PrimaryTextColor;
		base.Font = shellFont;
		LoadApplicationIcon();
		if (options.OpacityPercent < 100)
		{
			base.Opacity = Math.Max(0.55, Math.Min((double)options.OpacityPercent / 100.0, 1.0));
		}
		base.KeyPreview = true;
		InitializeLayout();
		InitializeWindowPlacementPersistence();
		Application.AddMessageFilter(this);
		InitializeInteractiveActions();
		discordRpcService = discordRichPresenceEnabled ? DiscordRpcService.CreateIfConfigured() : null;
		discordRpcService?.Start();
		UpdateRuntimePresence(connected: false);
		localCurrentPath = ResolveConfiguredLocalDirectory();
		base.Shown += delegate
		{
			ApplyNativeWindowTheme();
			LoadOptionalBackground();
			BeginInvoke(new MethodInvoker(delegate
			{
				if (!commandInput.IsDisposed && commandInput.CanFocus)
				{
					commandInput.Focus();
				}
			}));
			if (options.AutoConnect)
			{
				BeginInvoke(new MethodInvoker(delegate
				{
					if (!base.IsDisposed && !connectAttemptInFlight && shellDisconnected)
						connectButton.PerformClick();
				}));
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
			if (ShouldRefreshInventoryLoadingState())
			{
				RefreshInventoryListFromSnapshot();
			}
		};
		heartbeatTimer.Start();
		ftpTrafficMeter.Reset(DateTime.UtcNow);
		ftpTrafficTimer.Interval = FtpTrafficSampleIntervalMs;
		ftpTrafficTimer.Tick += delegate
		{
			RefreshFtpTrafficPresentation();
		};
		ftpTrafficTimer.Start();
		telemetryTimer.Interval = TelemetryPollIntervalMs;
		telemetryTimer.Tick += async delegate
		{
			await PollTelemetrySafeAsync(forceHeavyRefresh: ShouldRetryHeavyTelemetryRefresh());
		};
		statusToastTimer.Interval = 2200;
		statusToastTimer.Tick += delegate
		{
			ClearStatusToast();
		};
		screenshotNotificationTimer.Interval = ScreenshotNotificationDurationMs;
		screenshotNotificationTimer.Tick += delegate
		{
			HideScreenshotNotification();
		};
		themePersistenceTimer.Interval = ThemePersistenceDelayMs;
		themePersistenceTimer.Tick += delegate
		{
			FlushConfiguredTerminalThemeSave();
		};
		_ = RefreshLocalBrowserAsync();
		SetRemoteBrowserPlaceholder("/", "Connect to browse remote files.");
		AppendSystemLine(TranslateTerminalText("XeCLI terminal shell ready."), AccentGreen);
		AppendSystemLine(TranslateTerminalText("Offline session. Local command execution is available."), AccentDim);
		UpdateConnectionStatusIndicator();
		UpdateShellScaffold();
		UpdateLiveHints();
		RefreshLocalizedUiText();
		base.FormClosing += delegate(object? _, FormClosingEventArgs e)
		{
			FlushWindowPlacementSave();
			FlushConfiguredTerminalThemeSave();
			if (fileTransferInFlight && !allowCloseAfterTransfer)
			{
				e.Cancel = true;
				if (!closeRequestedDuringTransfer)
				{
					closeRequestedDuringTransfer = true;
					fileTransferCts?.Cancel();
					SetFooterOperationStatus("CANCELLING TRANSFER");
					AppendSystemLine("Cancelling the active transfer and cleaning up its temporary files before closing.", WarningColor);
				}
				return;
			}
			CancelAllBackgroundWork();
			suppressWindowPlacementPersistence = true;
		};
		base.FormClosed += delegate
		{
			Application.RemoveMessageFilter(this);
			terminalFlushTimer.Stop();
			heartbeatTimer.Stop();
			telemetryTimer.Stop();
			ftpTrafficTimer.Stop();
			screenshotNotificationTimer.Stop();
			themePersistenceTimer.Stop();
			discordRpcService?.Dispose();
			commandInputToolTip.Dispose();
			footerThermalToolTip.Dispose();
			filePathToolTip.Dispose();
			screenshotNotificationToolTip.Dispose();
			foreach (ContextMenuStrip menu in new[]
			{
				localFileMenu,
				localEmptyMenu,
				remoteFileMenu,
				remoteEmptyMenu,
				terminalOutputMenu,
				inventoryMenu,
				driveMenu,
				diagnosticsMenu
			})
			{
				menu.Dispose();
			}
			lock (pendingTerminalLock)
			{
				pendingTerminalLines.Clear();
			}
			screenshotNotificationImage?.Dispose();
			screenshotPreviewForm?.Close();
			screenshotPreviewForm?.Dispose();
			base.BackgroundImage?.Dispose();
			formLifetimeCts.Dispose();
		};
		suppressWindowPlacementPersistence = false;
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ApplyResponsiveRailWidths();
		SetFileBrowserHeight(fileBrowserHeight);
		PositionScreenshotNotification();
	}

	private void ApplyStartupPlacement()
	{
		base.StartPosition = FormStartPosition.Manual;
		if (TryRestoreSavedTerminalPlacement(out Rectangle restoredBounds, out FormWindowState restoredState, out _, out string? restoredScreenDeviceName))
		{
			Screen workingScreen = ResolvePlacementScreen(restoredScreenDeviceName, restoredBounds);
			base.MinimumSize = GetTerminalMinimumSize(workingScreen.WorkingArea);
			Rectangle clampedBounds = ClampBoundsToWorkingArea(restoredBounds, workingScreen.WorkingArea, base.MinimumSize);
			base.Bounds = clampedBounds;
			base.WindowState = restoredState;
			return;
		}
		Screen startupScreen = ResolveStartupScreen();
		Rectangle workingArea = startupScreen.WorkingArea;
		base.MinimumSize = GetTerminalMinimumSize(workingArea);
		Size size = GetDefaultTerminalSize(workingArea);
		Point location = GetCenteredLocation(workingArea, size);
		base.Size = size;
		base.Location = location;
		base.WindowState = FormWindowState.Normal;
	}

	private void InitializeWindowPlacementPersistence()
	{
		windowPlacementSaveTimer.Interval = 300;
		windowPlacementSaveTimer.Tick += delegate
		{
			windowPlacementSaveTimer.Stop();
			PersistTerminalWindowPlacement();
		};
		base.Move += delegate
		{
			if (!suppressWindowPlacementPersistence)
			{
				ScheduleTerminalWindowPlacementSave();
			}
		};
		base.SizeChanged += delegate
		{
			if (!suppressWindowPlacementPersistence)
			{
				ScheduleTerminalWindowPlacementSave();
			}
			RefreshFooterThermalPresentation();
		};
	}

	private void ScheduleTerminalWindowPlacementSave()
	{
		if (base.IsDisposed)
		{
			return;
		}
		windowPlacementSaveTimer.Stop();
		windowPlacementSaveTimer.Start();
	}

	private void FlushWindowPlacementSave()
	{
		windowPlacementSaveTimer.Stop();
		PersistTerminalWindowPlacement();
	}

	private bool TryRestoreSavedTerminalPlacement(out Rectangle bounds, out FormWindowState windowState, out string source, out string? screenDeviceName)
	{
		bounds = default;
		windowState = FormWindowState.Normal;
		source = "missing";
		screenDeviceName = null;
		if (!CliConfig.TryLoad(out CliConfig cliConfig) || cliConfig.RememberTerminalWindowPlacement == false || cliConfig.TerminalWindowPlacement == null)
		{
			return false;
		}
		CliConfig.TerminalWindowPlacementInfo placement = cliConfig.TerminalWindowPlacement;
		if (placement.Width <= 0 || placement.Height <= 0)
		{
			source = "invalid-size";
			return false;
		}
		bounds = new Rectangle(placement.Left, placement.Top, placement.Width, placement.Height);
		if (!Enum.TryParse(placement.WindowState, ignoreCase: true, out windowState))
		{
			windowState = FormWindowState.Normal;
		}
		if (windowState == FormWindowState.Minimized)
		{
			windowState = FormWindowState.Normal;
		}
		screenDeviceName = TrimOrNull(placement.ScreenDeviceName);
		source = "saved";
		return true;
	}

	private void PersistTerminalWindowPlacement()
	{
		if (suppressWindowPlacementPersistence || base.IsDisposed || base.Disposing)
		{
			return;
		}
		Rectangle bounds = base.WindowState == FormWindowState.Normal ? base.Bounds : base.RestoreBounds;
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			return;
		}
		FormWindowState windowState = base.WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
		Screen screen = Screen.FromRectangle(bounds);
		CliConfig.TryLoad(out CliConfig cliConfig);
		if (cliConfig.RememberTerminalWindowPlacement == false)
		{
			return;
		}
		cliConfig.TerminalWindowPlacement = new CliConfig.TerminalWindowPlacementInfo
		{
			Left = bounds.Left,
			Top = bounds.Top,
			Width = bounds.Width,
			Height = bounds.Height,
			WindowState = windowState.ToString(),
			ScreenDeviceName = screen.DeviceName
		};
		try
		{
			cliConfig.Save();
		}
		catch
		{
		}
	}

	private static Size GetTerminalMinimumSize(Rectangle workingArea)
	{
		int width = Math.Min(1280, Math.Max(1, workingArea.Width - 32));
		int height = Math.Min(940, Math.Max(1, workingArea.Height - 32));
		return new Size(width, height);
	}

	private void ApplyResponsiveRailWidths()
	{
		if (primaryShellLayoutTable == null || leftRailColumnStyle == null || rightRailColumnStyle == null)
		{
			return;
		}

		int availableWidth = primaryShellLayoutTable.ClientSize.Width;
		if (availableWidth <= 0)
		{
			int shellPadding = shellLayoutTable?.Padding.Horizontal ?? 0;
			availableWidth = Math.Max(1, ClientSize.Width - shellPadding);
		}

		int leftWidth = ClampInt((int)Math.Round(availableWidth * 0.218), MinLeftRailWidth, MaxLeftRailWidth);
		int rightWidth = ClampInt((int)Math.Round(availableWidth * 0.236), MinRightRailWidth, MaxRightRailWidth);
		int remainingCenterWidth = availableWidth - leftWidth - rightWidth;
		if (remainingCenterWidth < MinCenterColumnWidth)
		{
			int deficit = MinCenterColumnWidth - remainingCenterWidth;
			int leftSlack = Math.Max(0, leftWidth - MinLeftRailWidth);
			int rightSlack = Math.Max(0, rightWidth - MinRightRailWidth);
			int slack = leftSlack + rightSlack;
			if (slack > 0)
			{
				int leftReduction = Math.Min(leftSlack, (int)Math.Ceiling(deficit * (leftSlack / (double)slack)));
				int rightReduction = Math.Min(rightSlack, deficit - leftReduction);
				leftWidth -= leftReduction;
				rightWidth -= rightReduction;
			}
		}

		leftRailColumnStyle.Width = leftWidth;
		rightRailColumnStyle.Width = rightWidth;
	}

	private static int ClampInt(int value, int minimum, int maximum)
	{
		return Math.Min(maximum, Math.Max(minimum, value));
	}

	private static int GetCommandInputPromptReservedWidth()
	{
		return CommandInputPromptLeftInset + CommandInputPromptWidth + CommandInputTextGap;
	}

	private static int GetCommandInputMinimumHeight()
	{
		return CommandInputMinimumHeight;
	}

	private static Color GetTerminalOutputSurfaceColor()
	{
		return InterpolateColor(TerminalBackground, AccentGreen, 0.035f);
	}

	private static Color GetSuggestionSurfaceColor()
	{
		return InterpolateColor(TerminalBackground, ShellBackground, 0.20f);
	}

	private static Color GetStatusToastSurfaceColor()
	{
		return InterpolateColor(CardBackground, ShellBackground, 0.20f);
	}

	private static Color GetCommandInputSurfaceColor()
	{
		return ComputeCommandInputSurfaceColor(CardBackground, AccentCyan, AccentGreen);
	}

	private static Color GetCommandInputPlaceholderColor()
	{
		return InterpolateColor(GetCommandInputSurfaceColor(), PrimaryTextColor, 0.40f);
	}

	private static Color GetAlternatingRowBackgroundColor(int rowIndex)
	{
		return InterpolateColor(TerminalBackground, AccentGreen, (rowIndex & 1) == 0 ? 0.035f : 0.075f);
	}

	private static Color GetSelectedRowBackgroundColor()
	{
		return InterpolateColor(TerminalBackground, AccentGreen, 0.24f);
	}

	private static Color GetSelectedRowTextColor()
	{
		return PrimaryTextColor;
	}

	private static Color GetScrollTrackBackgroundColor()
	{
		return InterpolateColor(TerminalBackground, BorderColor, 0.34f);
	}

	private static Color GetProgressTrackBackgroundColor()
	{
		return InterpolateColor(TerminalBackground, AccentDim, 0.16f);
	}

	private static Color ComputeCommandInputSurfaceColor(Color cardBackground, Color _, Color primaryAccent)
	{
		return InterpolateColor(cardBackground, primaryAccent, 0.085f);
	}

	private static Size GetDefaultTerminalSize(Rectangle workingArea)
	{
		int availableWidth = Math.Max(1, workingArea.Width - 32);
		int availableHeight = Math.Max(1, workingArea.Height - 32);
		int width = Math.Min(1640, Math.Max(1280, availableWidth));
		int height = Math.Min(980, Math.Max(900, availableHeight));
		width = Math.Min(width, availableWidth);
		height = Math.Min(height, availableHeight);
		return new Size(width, height);
	}

	private static Rectangle ClampBoundsToWorkingArea(Rectangle bounds, Rectangle workingArea, Size minimumSize)
	{
		int width = Math.Max(1, Math.Min(Math.Max(bounds.Width, minimumSize.Width), workingArea.Width));
		int height = Math.Max(1, Math.Min(Math.Max(bounds.Height, minimumSize.Height), workingArea.Height));
		int left = Math.Min(Math.Max(bounds.Left, workingArea.Left), workingArea.Right - width);
		int top = Math.Min(Math.Max(bounds.Top, workingArea.Top), workingArea.Bottom - height);
		return new Rectangle(left, top, width, height);
	}

	private static Screen ResolveStartupScreen()
	{
		Screen[] allScreens = Screen.AllScreens;
		return Screen.PrimaryScreen ?? allScreens[0];
	}

	private static Screen ResolvePlacementScreen(string? deviceName, Rectangle bounds)
	{
		if (!string.IsNullOrWhiteSpace(deviceName))
		{
			Screen? matchingScreen = Screen.AllScreens.FirstOrDefault(screen => string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
			if (matchingScreen != null)
			{
				return matchingScreen;
			}
		}

		return Screen.FromRectangle(bounds);
	}

	private static int GetStartupScreenIndex(Screen startupScreen)
	{
		Screen[] allScreens = Screen.AllScreens;
		for (int i = 0; i < allScreens.Length; i++)
		{
			if (string.Equals(allScreens[i].DeviceName, startupScreen.DeviceName, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		return -1;
	}

	private static Point GetCenteredLocation(Rectangle workingArea, Size size)
	{
		int x = workingArea.Left + Math.Max(0, (workingArea.Width - size.Width) / 2);
		int y = workingArea.Top + Math.Max(0, (workingArea.Height - size.Height) / 2);
		x = Math.Min(Math.Max(x, workingArea.Left), workingArea.Right - size.Width);
		y = Math.Min(Math.Max(y, workingArea.Top), workingArea.Bottom - size.Height);
		return new Point(Math.Max(workingArea.Left, x), Math.Max(workingArea.Top, y));
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

	public bool PreFilterMessage(ref Message m)
	{
		if (m.Msg != WmMouseWheel || !IsMouseOverTerminalOutput())
		{
			return false;
		}
		int delta = unchecked((short)(((long)m.WParam >> 16) & 0xFFFF));
		if (delta == 0)
		{
			return false;
		}
		ScrollTerminalOutputByWheelDelta(delta);
		return true;
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
			tableLayoutPanel.Padding = new Padding(6);
			shellLayoutTable = tableLayoutPanel;
			fileBrowserRowStyle = new RowStyle(SizeType.Absolute, fileBrowserHeight);
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel.RowStyles.Add(fileBrowserRowStyle);
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterStatusBarHeight));
			base.Controls.Add(tableLayoutPanel);
			TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
			tableLayoutPanel2.Dock = DockStyle.Fill;
			tableLayoutPanel2.BackColor = ShellBackground;
			tableLayoutPanel2.RowCount = 1;
			tableLayoutPanel2.ColumnCount = 3;
			primaryShellLayoutTable = tableLayoutPanel2;
			leftRailColumnStyle = new ColumnStyle(SizeType.Absolute, LeftRailWidth);
			rightRailColumnStyle = new ColumnStyle(SizeType.Absolute, RightRailWidth);
			tableLayoutPanel2.ColumnStyles.Add(leftRailColumnStyle);
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel2.ColumnStyles.Add(rightRailColumnStyle);
			tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 1);
			Panel panel2 = BuildLeftPanel();
			panel2.MinimumSize = new Size(MinLeftRailWidth, 0);
			Panel panel3 = BuildCenterPanel();
			panel3.MinimumSize = new Size(MinCenterColumnWidth, 0);
			Panel panel4 = BuildRightPanel();
			panel4.MinimumSize = new Size(MinRightRailWidth, 0);
			tableLayoutPanel2.Controls.Add(panel2, 0, 0);
			tableLayoutPanel2.Controls.Add(panel3, 1, 0);
			tableLayoutPanel2.Controls.Add(panel4, 2, 0);
			ApplyResponsiveRailWidths();
			Panel panel5 = BuildFileStripPanel();
			tableLayoutPanel.Controls.Add(panel5, 0, 2);
			Panel panel6 = CreateCardPanel(FooterChipBackground, new Padding(8, 2, 8, 2));
			panel6.Dock = DockStyle.Fill;
			panel6.Margin = new Padding(0, 6, 0, 0);
			panel6.Paint += delegate(object? sender, PaintEventArgs e)
			{
				if (sender is Control control && control.Width > 2)
				{
					using Pen rail = new Pen(Color.FromArgb(112, AccentGreen), 1f);
					e.Graphics.DrawLine(rail, 8, 0, control.Width - 9, 0);
				}
			};
			TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
			tableLayoutPanel3.Dock = DockStyle.Fill;
			tableLayoutPanel3.Margin = Padding.Empty;
			tableLayoutPanel3.Padding = Padding.Empty;
			tableLayoutPanel3.BackColor = FooterChipBackground;
			tableLayoutPanel3.RowCount = 1;
			tableLayoutPanel3.ColumnCount = 5;
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430f));
			ConfigureFooterChipLabel(footerVersionLabel);
			footerVersionLabel.Text = GetFooterVersionText();
			footerVersionLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
			footerVersionLabel.ForeColor = PrimaryTextColor;
			footerVersionLabel.Cursor = Cursors.Hand;
			footerVersionLabel.Click += delegate
			{
				OpenXeCliRepository();
			};
			ConfigureFooterChipLabel(footerStatusLabel);
			ConfigureFooterChipLabel(footerTargetLabel);
			ConfigureFooterChipLabel(footerPresenceLabel);
			ConfigureFooterChipLabel(footerThermalLabel);
			footerTargetLabel.Visible = true;
			footerPresenceLabel.Visible = true;
			footerStatusLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
			footerStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
			footerPresenceLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
			footerPresenceLabel.TextAlign = ContentAlignment.MiddleLeft;
			footerTargetLabel.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
			footerTargetLabel.TextAlign = ContentAlignment.MiddleCenter;
			footerThermalLabel.Font = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
			footerThermalLabel.ForeColor = AccentCyan;
			footerThermalLabel.AutoEllipsis = false;
			footerThermalLabel.TextAlign = ContentAlignment.MiddleRight;
			SetFooterStatusText("READY");
			SetFooterTargetText("QUEUE: 0");
			SetFooterPresenceText("IDLE");
			SetFooterThermalText("THERMALS --");
			tableLayoutPanel3.Controls.Add(footerVersionLabel, 0, 0);
			tableLayoutPanel3.Controls.Add(footerStatusLabel, 1, 0);
			tableLayoutPanel3.Controls.Add(footerPresenceLabel, 2, 0);
			tableLayoutPanel3.Controls.Add(footerTargetLabel, 3, 0);
			tableLayoutPanel3.Controls.Add(footerThermalLabel, 4, 0);
			panel6.Controls.Add(tableLayoutPanel3);
			tableLayoutPanel.Controls.Add(panel6, 0, 3);
			RefreshFooterThermalPresentation();
			InitializeScreenshotNotification();
		}
		catch
		{
			throw;
		}
	}

	private Panel BuildLeftPanel()
	{
		Panel panel = CreateCardPanel(CardBackground, new Padding(10, 8, 10, 4));
		panel.Margin = new Padding(0, 0, 8, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 114f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		connectionStatusLabel.Dock = DockStyle.Fill;
		connectionStatusLabel.Margin = new Padding(0, 0, 0, 2);
		connectionStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
		connectionStatusLabel.ForeColor = AccentGreen;
		connectionStatusLabel.Font = new Font("Consolas", 9.25f, FontStyle.Bold, GraphicsUnit.Point);
		connectionStatusLabel.Padding = new Padding(30, 0, 8, 0);
		connectionStatusLabel.BackColor = CardBackground;
		connectionStatusLabel.AutoEllipsis = true;
		connectionStatusLabel.UseMnemonic = false;
		connectionStatusLabel.AccessibleName = "Console connection status";
		connectionStatusLabel.AccessibleRole = AccessibleRole.StatusBar;
		connectionStatusLabel.Paint += PaintConnectionStatusIndicator;
		SetConnectionStatusText("Not connected");
		ApplyConnectionStatusIndicator(ConnectionUiState.Disconnected);
		Panel panelConsole = CreateCardPanel(TerminalBackground, new Padding(5, 1, 5, 1));
		panelConsole.Dock = DockStyle.Fill;
		panelConsole.Margin = Padding.Empty;
		TableLayoutPanel tableLayoutPanelConsole = new TableLayoutPanel();
		tableLayoutPanelConsole.Dock = DockStyle.Fill;
		tableLayoutPanelConsole.Margin = Padding.Empty;
		tableLayoutPanelConsole.Padding = Padding.Empty;
		tableLayoutPanelConsole.RowCount = 2;
		tableLayoutPanelConsole.ColumnCount = 1;
		tableLayoutPanelConsole.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
		tableLayoutPanelConsole.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		leftConsoleHeaderLabel.Dock = DockStyle.Fill;
		leftConsoleHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
		leftConsoleHeaderLabel.ForeColor = AccentGreen;
		leftConsoleHeaderLabel.Font = shellFontBold;
		leftConsoleHeaderLabel.Text = "CONSOLE ID";
		leftConsoleInfoPanel.Dock = DockStyle.Fill;
		leftConsoleInfoPanel.BackColor = TerminalBackground;
		leftConsoleInfoPanel.ForeColor = PrimaryTextColor;
		leftConsoleInfoPanel.Font = shellFont;
		SetLeftConsoleText(BuildDisconnectedConsoleText());
		tableLayoutPanelConsole.Controls.Add(leftConsoleHeaderLabel, 0, 0);
		tableLayoutPanelConsole.Controls.Add(leftConsoleInfoPanel, 0, 1);
		panelConsole.Controls.Add(tableLayoutPanelConsole);
		inventoryList.Dock = DockStyle.Fill;
		inventoryList.BackColor = TerminalBackground;
		inventoryList.ForeColor = PrimaryTextColor;
		inventoryList.Font = shellFont;
		inventoryList.SetItems(new string[1]
		{
			"Connect to load plugins."
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
		pluginsTabButton.Margin = new Padding(0, 0, 4, 0);
		modulesTabButton.Margin = Padding.Empty;
		TableLayoutPanel panelInventoryHeader = new TableLayoutPanel();
		panelInventoryHeader.Dock = DockStyle.Fill;
		panelInventoryHeader.RowCount = 1;
		panelInventoryHeader.ColumnCount = 2;
		panelInventoryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		panelInventoryHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		panelInventoryHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		panelInventoryHeader.Margin = Padding.Empty;
		panelInventoryHeader.Padding = Padding.Empty;
		Panel inventoryPanel = CreateCardPanel(TerminalBackground, new Padding(4, 3, 4, 3));
		inventoryPanel.Dock = DockStyle.Fill;
		inventoryPanel.Margin = new Padding(0);
		TableLayoutPanel inventoryLayout = new TableLayoutPanel();
		inventoryLayout.Dock = DockStyle.Fill;
		inventoryLayout.RowCount = 2;
		inventoryLayout.ColumnCount = 1;
		inventoryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		inventoryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		inventoryLayout.Controls.Add(panelInventoryHeader, 0, 0);
		inventoryLayout.Controls.Add(inventoryList, 0, 1);
		inventoryPanel.Controls.Add(inventoryLayout);
		Panel panel2 = CreateCardPanel(TerminalBackground, new Padding(2, 2, 2, 4));
		panel2.Dock = DockStyle.Fill;
		panel2.Margin = Padding.Empty;
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
		tableLayoutPanel2.Dock = DockStyle.Fill;
		tableLayoutPanel2.Margin = Padding.Empty;
		tableLayoutPanel2.Padding = Padding.Empty;
		tableLayoutPanel2.ColumnCount = 2;
		tableLayoutPanel2.RowCount = 4;
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		TableLayoutPanel preferencesRow = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = TerminalBackground
		};
		preferencesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
		preferencesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		preferencesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
		preferencesRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		ConfigureActionButton(connectButton, "CONNECT");
		ConfigureActionButton(disconnectButton, "DISCONNECT");
		ConfigureActionButton(screenshotButton, "SCREENSHOT");
		ConfigureActionButton(languageButton, "LANG: EN");
		ConfigureActionButton(avatarButton, "AVATAR ITEMS");
		ConfigureActionButton(themeButton, "THEME: MATRIX");
		ConfigureActionButton(settingsButton, "⚙");
		settingsButton.AccessibleName = "Settings";
		settingsButton.AccessibleDescription = "Open XeCLI settings";
		settingsButton.Font = new Font("Segoe UI Symbol", 10f, FontStyle.Regular, GraphicsUnit.Point);
		filePathToolTip.SetToolTip(settingsButton, "Settings (Ctrl+,)");
		ConfigureInputTextBox(targetIpTextBox, string.Empty, horizontalAlignment: HorizontalAlignment.Center);
		ConfigureTargetIpHostPanel(targetIpHostPanel, targetIpTextBox, "Host / IP[:port]");
		tableLayoutPanel2.Controls.Add(targetIpHostPanel, 0, 0);
		tableLayoutPanel2.SetColumnSpan(targetIpHostPanel, 2);
		tableLayoutPanel2.Controls.Add(connectButton, 0, 1);
		tableLayoutPanel2.Controls.Add(disconnectButton, 1, 1);
		tableLayoutPanel2.Controls.Add(screenshotButton, 0, 2);
		tableLayoutPanel2.Controls.Add(avatarButton, 1, 2);
		preferencesRow.Controls.Add(languageButton, 0, 0);
		preferencesRow.Controls.Add(themeButton, 1, 0);
		preferencesRow.Controls.Add(settingsButton, 2, 0);
		tableLayoutPanel2.Controls.Add(preferencesRow, 0, 3);
		tableLayoutPanel2.SetColumnSpan(preferencesRow, 2);
		panel2.Controls.Add(tableLayoutPanel2);
		tableLayoutPanel.Controls.Add(connectionStatusLabel, 0, 0);
		tableLayoutPanel.Controls.Add(panel2, 0, 1);
		tableLayoutPanel.Controls.Add(panelConsole, 0, 2);
		tableLayoutPanel.Controls.Add(inventoryPanel, 0, 3);
		panelInventoryHeader.Controls.Add(pluginsTabButton, 0, 0);
		panelInventoryHeader.Controls.Add(modulesTabButton, 1, 0);
		RefreshTargetEditorText();
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildCenterPanel()
	{
		try
		{
			Panel panel = CreateCardPanel(TerminalBackground, new Padding(2));
			panel.Margin = new Padding(0, 0, 8, 0);
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
			tableLayoutPanel.Dock = DockStyle.Fill;
			tableLayoutPanel.ColumnCount = 1;
			tableLayoutPanel.RowCount = 2;
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, CenterHeroHeight));
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			Panel panel2 = CreateCardPanel(CardBackground, new Padding(6, 5, 6, 5));
			panel2.Dock = DockStyle.Fill;
			panel2.Margin = new Padding(0, 0, 0, 6);
			TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel();
			tableLayoutPanel2.Dock = DockStyle.Fill;
			tableLayoutPanel2.ColumnCount = 2;
			tableLayoutPanel2.RowCount = 1;
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
			tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			shellTargetLabel.Dock = DockStyle.Fill;
			shellTargetLabel.Margin = Padding.Empty;
			shellTargetLabel.TextAlign = ContentAlignment.MiddleRight;
			shellTargetLabel.ForeColor = Color.FromArgb(214, AccentDim);
			shellTargetLabel.Font = shellFont;
			shellTargetLabel.Text = "TARGET  " + FormatCurrentTarget();
			shellTargetLabel.AutoEllipsis = true;
			shellBadgeLabel.Dock = DockStyle.Fill;
			shellBadgeLabel.Margin = Padding.Empty;
			shellBadgeLabel.TextAlign = ContentAlignment.MiddleLeft;
			shellBadgeLabel.ForeColor = AccentGreen;
			shellBadgeLabel.Font = headerFont;
			shellBadgeLabel.Text = "XeCLI";
			shellBadgeLabel.AutoEllipsis = true;
			tableLayoutPanel2.Controls.Add(shellBadgeLabel, 0, 0);
			tableLayoutPanel2.Controls.Add(shellTargetLabel, 1, 0);
			panel2.Controls.Add(tableLayoutPanel2);
			Panel panel5 = CreateSoftShellPanel(TerminalBackground, new Padding(0));
			panel5.Dock = DockStyle.Fill;
			TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel();
			tableLayoutPanel5.Dock = DockStyle.Fill;
			tableLayoutPanel5.BackColor = TerminalBackground;
			tableLayoutPanel5.ColumnCount = 1;
			tableLayoutPanel5.RowCount = 2;
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			statusToastLabel.Dock = DockStyle.Fill;
			statusToastLabel.Margin = new Padding(0);
			statusToastLabel.Padding = new Padding(8, 0, 8, 0);
			statusToastLabel.TextAlign = ContentAlignment.MiddleLeft;
			statusToastLabel.Font = shellFont;
			statusToastLabel.ForeColor = WarningColor;
			statusToastLabel.BackColor = GetStatusToastSurfaceColor();
			statusToastLabel.AutoEllipsis = true;
			statusToastLabel.UseMnemonic = false;
			statusToastLabel.Visible = false;
			tableLayoutPanel5.Controls.Add(statusToastLabel, 0, 0);
			Panel panel6 = CreateScaffoldSurfacePanel(GetTerminalOutputSurfaceColor(), new Padding(10, 10, 10, 10), 28, 28);
			panel6.Dock = DockStyle.Fill;
			panel6.Margin = new Padding(0);
			TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
			tableLayoutPanel4.Dock = DockStyle.Fill;
			tableLayoutPanel4.BackColor = TerminalBackground;
			tableLayoutPanel4.ColumnCount = 1;
			tableLayoutPanel4.RowCount = 4;
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 4f));
			tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, CommandInputRowHeight));
			suggestionRowStyle = tableLayoutPanel4.RowStyles[2];
			shellScaffoldLabel.Dock = DockStyle.Fill;
			shellScaffoldLabel.Margin = new Padding(0, 0, 0, 3);
			shellScaffoldLabel.Padding = new Padding(8, 0, 8, 0);
			shellScaffoldLabel.TextAlign = ContentAlignment.MiddleLeft;
			shellScaffoldLabel.Font = shellOutputFont;
			shellScaffoldLabel.ForeColor = Color.FromArgb(218, AccentDim);
			shellScaffoldLabel.Text = "Command stream";
			shellScaffoldLabel.Visible = true;
			Panel panel7 = new Panel();
			panel7.Dock = DockStyle.Fill;
			panel7.Margin = new Padding(0);
			panel7.Padding = new Padding(0, 4, 0, 0);
			panel7.BackColor = TerminalBackground;
			terminalOutput.Dock = DockStyle.Fill;
			terminalOutput.BackColor = GetTerminalOutputSurfaceColor();
			terminalOutput.ForeColor = PrimaryTextColor;
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
			terminalScrollIndicator.BackColor = GetTerminalOutputSurfaceColor();
			terminalScrollIndicator.Attach(terminalOutput);
			panel7.Controls.Add(terminalScrollIndicator);
			tableLayoutPanel4.Controls.Add(shellScaffoldLabel, 0, 0);
			tableLayoutPanel4.Controls.Add(panel7, 0, 1);
			suggestionHost.Dock = DockStyle.Fill;
			suggestionHost.Margin = Padding.Empty;
			suggestionHost.BackColor = GetSuggestionSurfaceColor();
			suggestionHost.Padding = new Padding(3, 2, 3, 2);
			suggestionHost.Paint += delegate(object? sender, PaintEventArgs e)
			{
				if (sender is Control control && control.Width > 2 && control.Height > 2)
				{
					e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					Rectangle bounds = new Rectangle(0, 0, control.Width - 1, control.Height - 1);
					using GraphicsPath path = CreateRoundedRectanglePath(bounds, CommandSurfaceRadius);
					using SolidBrush brush = new SolidBrush(Color.FromArgb(224, GetSuggestionSurfaceColor()));
					using Pen border = new Pen(Color.FromArgb(92, AccentGreen), 1f);
					e.Graphics.FillPath(brush, path);
					e.Graphics.DrawPath(border, path);
				}
			};
			suggestionList.Dock = DockStyle.Top;
			suggestionList.BackColor = GetSuggestionSurfaceColor();
			suggestionList.ForeColor = PrimaryTextColor;
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
			commandInputHost.Margin = new Padding(0, 6, 0, 0);
			commandInputHost.Padding = new Padding(GetCommandInputPromptReservedWidth(), 3, CommandInputRightFrameClearance, 3);
			commandInputHost.MinimumSize = new Size(0, CommandInputMinimumHeight);
			commandInputHost.BackColor = GetTerminalOutputSurfaceColor();
			commandInputHost.Paint += delegate(object? _, PaintEventArgs e)
			{
				Rectangle clientRectangle = commandInputHost.ClientRectangle;
				if (clientRectangle.Width <= 2 || clientRectangle.Height <= 2)
				{
					return;
				}
				e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
				e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
				Rectangle inputBounds = Rectangle.Inflate(clientRectangle, -1, -1);
				using GraphicsPath inputPath = CreateRoundedRectanglePath(inputBounds, CommandInputOuterRadius);
				using SolidBrush brush = new SolidBrush(GetCommandInputSurfaceColor());
				bool composerFocused = commandInput.Focused || commandSubmitButton.Focused || suggestionList.Visible;
				Color borderColor = composerFocused ? AccentGreen : Color.FromArgb(92, AccentGreen);
				using Pen border = new Pen(borderColor, composerFocused ? 1.5f : 1.25f) { Alignment = PenAlignment.Inset };
				e.Graphics.FillPath(brush, inputPath);
				e.Graphics.DrawPath(border, inputPath);
				if (composerFocused)
				{
					Rectangle focusBounds = Rectangle.Inflate(inputBounds, -2, -2);
					if (focusBounds.Width > 2 && focusBounds.Height > 2)
					{
						using GraphicsPath focusPath = CreateRoundedRectanglePath(focusBounds, Math.Max(2, CommandInputOuterRadius - 2));
						using Pen focusRing = new Pen(Color.FromArgb(58, AccentGreen), 1f) { Alignment = PenAlignment.Inset };
						e.Graphics.DrawPath(focusRing, focusPath);
					}
				}
				Rectangle promptBounds = new Rectangle(
					inputBounds.Left + CommandInputPromptLeftInset,
					inputBounds.Top,
					CommandInputPromptWidth,
					inputBounds.Height);
				TextRenderer.DrawText(
					e.Graphics,
					">",
					shellFontBold,
					promptBounds,
					InterpolateColor(GetCommandInputSurfaceColor(), AccentGreen, composerFocused ? 0.72f : 0.52f),
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
			};
			commandInputLayout.Dock = DockStyle.Fill;
			commandInputLayout.Margin = Padding.Empty;
			commandInputLayout.Padding = Padding.Empty;
			commandInputLayout.BackColor = GetCommandInputSurfaceColor();
			commandInputLayout.ColumnCount = 2;
			commandInputLayout.RowCount = 1;
			commandInputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			commandInputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CommandSubmitColumnWidth));
			commandInputLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			commandInput.Dock = DockStyle.Fill;
			commandInput.Margin = new Padding(0, 8, 0, 7);
			commandInput.MinimumSize = new Size(0, 20);
			commandInput.BackColor = GetCommandInputSurfaceColor();
			commandInput.ForeColor = PrimaryTextColor;
			commandInput.BorderStyle = BorderStyle.None;
			commandInput.Font = shellFont;
			commandInput.Multiline = false;
			commandInput.ScrollBars = ScrollBars.None;
			commandInput.AccessibleName = "Command entry";
			SetCommandInputPlaceholder("Enter a local command");
			commandInput.KeyDown += HandleCommandInputKeyDown;
			commandInput.Enter += delegate
			{
				commandInputHost.Invalidate();
				if (!suppressSuggestionRefresh)
				{
					RefreshSuggestions();
				}
			};
			commandInput.Leave += delegate
			{
				commandInputHost.Invalidate();
				HideSuggestionsIfInputInactive();
			};
			commandInput.TextChanged += delegate
			{
				HandleCommandInputTextChanged();
			};
			ConfigureCommandSubmitButton();
			commandInputLayout.Controls.Add(commandInput, 0, 0);
			commandInputLayout.Controls.Add(commandSubmitButton, 1, 0);
			commandInputHost.Controls.Add(commandInputLayout);
			UpdateCommandSubmitButtonState();
			tableLayoutPanel4.Controls.Add(commandInputHost, 0, 3);
			panel6.Controls.Add(tableLayoutPanel4);
			tableLayoutPanel5.Controls.Add(panel6, 0, 1);
			panel5.Controls.Add(tableLayoutPanel5);
			tableLayoutPanel.Controls.Add(panel2, 0, 0);
			tableLayoutPanel.Controls.Add(panel5, 0, 1);
			panel.Controls.Add(tableLayoutPanel);
			return panel;
		}
		catch
		{
			throw;
		}
	}

	private Panel BuildRightPanel()
	{
		Panel panel = CreateCardPanel(CardBackground, new Padding(8, 7, 8, 9));
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.RowCount = 3;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 106f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Panel panel3 = CreateCardPanel(TerminalBackground, new Padding(5, 4, 5, 2));
		panel3.Margin = new Padding(0, 0, 0, 2);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel();
		tableLayoutPanel3.Dock = DockStyle.Fill;
		tableLayoutPanel3.Margin = Padding.Empty;
		tableLayoutPanel3.Padding = Padding.Empty;
		tableLayoutPanel3.RowCount = 3;
		tableLayoutPanel3.ColumnCount = 1;
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		rightTrafficHeaderLabel.Dock = DockStyle.Fill;
		rightTrafficHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
		rightTrafficHeaderLabel.ForeColor = AccentGreen;
		rightTrafficHeaderLabel.Font = shellFontBold;
		rightTrafficHeaderLabel.Text = "FTP TRAFFIC";
		rightTrafficHeaderLabel.AccessibleName = "FTP traffic";
		rightTrafficHeaderLabel.AccessibleDescription = "Live payload rates for transfers performed by the XeCLI file browser.";
		filePathToolTip.SetToolTip(rightTrafficHeaderLabel, "Live FTP payload rates for the XeCLI file browser.");
		ftpTrafficGraph.Dock = DockStyle.Fill;
		ftpTrafficGraph.AccessibleName = "FTP traffic history";
		ftpTrafficGraph.AccessibleDescription = "Recent receive and send rates for FTP payloads transferred by the XeCLI file browser.";
		rightTempLabel.Dock = DockStyle.Fill;
		rightTempLabel.TextAlign = ContentAlignment.TopLeft;
		rightTempLabel.ForeColor = PrimaryTextColor;
		rightTempLabel.Font = shellFont;
		rightTempLabel.Padding = new Padding(1, 1, 1, 0);
		rightTempLabel.AccessibleName = "Current FTP transfer rates";
		SetFtpTrafficUnavailable("OFFLINE", EmptyStateColor);
		tableLayoutPanel3.Controls.Add(rightTrafficHeaderLabel, 0, 0);
		tableLayoutPanel3.Controls.Add(ftpTrafficGraph, 0, 1);
		tableLayoutPanel3.Controls.Add(rightTempLabel, 0, 2);
		panel3.Controls.Add(tableLayoutPanel3);
		Panel panelStatus = CreateCardPanel(TerminalBackground, new Padding(6, 3, 6, 3));
		panelStatus.Margin = new Padding(0, 0, 0, 2);
		TableLayoutPanel statusLayout = new TableLayoutPanel();
		statusLayout.Dock = DockStyle.Fill;
		statusLayout.Margin = Padding.Empty;
		statusLayout.Padding = Padding.Empty;
		statusLayout.RowCount = 2;
		statusLayout.ColumnCount = 1;
		statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));
		statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		rightStatusHeaderLabel.Dock = DockStyle.Fill;
		rightStatusHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
		rightStatusHeaderLabel.ForeColor = AccentGreen;
		rightStatusHeaderLabel.Font = shellFontBold;
		SetLabelText(rightStatusHeaderLabel, TranslateTerminalText("RGH STATUS"));
		rightNetworkInfoPanel.Dock = DockStyle.Fill;
		rightNetworkInfoPanel.BackColor = TerminalBackground;
		rightNetworkInfoPanel.ForeColor = PrimaryTextColor;
		rightNetworkInfoPanel.Font = shellFont;
		statusLayout.Controls.Add(rightStatusHeaderLabel, 0, 0);
		statusLayout.Controls.Add(rightNetworkInfoPanel, 0, 1);
		panelStatus.Controls.Add(statusLayout);
		Panel panel4 = CreateCardPanel(TerminalBackground, new Padding(5, 4, 5, 6));
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel();
		tableLayoutPanel4.Dock = DockStyle.Fill;
		tableLayoutPanel4.Margin = Padding.Empty;
		tableLayoutPanel4.Padding = Padding.Empty;
		tableLayoutPanel4.RowCount = 2;
		tableLayoutPanel4.ColumnCount = 1;
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		rightDrivesLabel.Dock = DockStyle.Fill;
		rightDrivesLabel.TextAlign = ContentAlignment.MiddleLeft;
		rightDrivesLabel.ForeColor = AccentGreen;
		rightDrivesLabel.Font = new Font("Consolas", 9f, FontStyle.Bold, GraphicsUnit.Point);
		SetRightDrivesTitleText("DETECTED DRIVES");
		drivesList.Dock = DockStyle.Fill;
		drivesList.BackColor = TerminalBackground;
		drivesList.ForeColor = PrimaryTextColor;
		drivesList.Font = shellFont;
		drivesList.SetEntries(new DriveInventoryEntry[1]
		{
			new DriveInventoryEntry
			{
				Name = "No drives detected"
			}
		});
		tableLayoutPanel4.Controls.Add(rightDrivesLabel, 0, 0);
		tableLayoutPanel4.Controls.Add(drivesList, 0, 1);
		panel4.Controls.Add(tableLayoutPanel4);
		tableLayoutPanel.Controls.Add(panel3, 0, 0);
		tableLayoutPanel.Controls.Add(panelStatus, 0, 1);
		tableLayoutPanel.Controls.Add(panel4, 0, 2);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private Panel BuildFileStripPanel()
	{
		Panel panel = CreateCardPanel(CardBackground, new Padding(9, 3, 9, 7));
		panel.Margin = new Padding(0, 6, 0, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 3;
		tableLayoutPanel.RowCount = 2;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, FileBrowserResizeGripHeight));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		fileBrowserLocalColumnStyle = new ColumnStyle(SizeType.Percent, 50f);
		fileBrowserRemoteColumnStyle = new ColumnStyle(SizeType.Percent, 50f);
		tableLayoutPanel.ColumnStyles.Add(fileBrowserLocalColumnStyle);
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FileBrowserPaneSplitterWidth));
		tableLayoutPanel.ColumnStyles.Add(fileBrowserRemoteColumnStyle);
		fileBrowserSplitLayoutTable = tableLayoutPanel;
		Control resizeGrip = BuildFileBrowserResizeGrip();
		Panel panel2 = BuildLocalFilePane();
		Panel panel3 = BuildRemoteFilePane();
		Control paneSplitter = BuildFileBrowserPaneSplitter();
		tableLayoutPanel.Controls.Add(resizeGrip, 0, 0);
		tableLayoutPanel.SetColumnSpan(resizeGrip, 3);
		tableLayoutPanel.Controls.Add(panel2, 0, 1);
		tableLayoutPanel.Controls.Add(paneSplitter, 1, 1);
		tableLayoutPanel.Controls.Add(panel3, 2, 1);
		panel.Controls.Add(tableLayoutPanel);
		return panel;
	}

	private void InitializeScreenshotNotification()
	{
		screenshotNotificationPanel.Size = new Size(ScreenshotNotificationWidth, ScreenshotNotificationHeight);
		screenshotNotificationPanel.BackColor = CardBackground;
		screenshotNotificationPanel.Padding = new Padding(10);
		screenshotNotificationPanel.Visible = false;
		screenshotNotificationPanel.TabStop = false;
		screenshotNotificationPanel.Paint += delegate(object? sender, PaintEventArgs e)
		{
			if (sender is not Control control || control.Width <= 1 || control.Height <= 1)
			{
				return;
			}
			using Pen border = new Pen(Color.FromArgb(190, screenshotNotificationAccentColor), 1f);
			using SolidBrush rail = new SolidBrush(screenshotNotificationAccentColor);
			e.Graphics.DrawRectangle(border, 0, 0, control.Width - 1, control.Height - 1);
			e.Graphics.FillRectangle(rail, 0, 0, 3, control.Height);
		};

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = CardBackground,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 3,
			RowCount = 3
		};
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

		screenshotNotificationThumbnail.Dock = DockStyle.Fill;
		screenshotNotificationThumbnail.Margin = new Padding(0, 0, 10, 0);
		screenshotNotificationThumbnail.Padding = new Padding(1);
		screenshotNotificationThumbnail.BackColor = TerminalBackground;
		screenshotNotificationThumbnail.SizeMode = PictureBoxSizeMode.Zoom;
		screenshotNotificationThumbnail.BorderStyle = BorderStyle.FixedSingle;
		screenshotNotificationThumbnail.Cursor = Cursors.Hand;
		screenshotNotificationThumbnail.AccessibleName = "Preview screenshot";
		screenshotNotificationThumbnail.Click += delegate
		{
			OpenScreenshotPreview();
		};
		screenshotNotificationThumbnail.Paint += delegate(object? _, PaintEventArgs e)
		{
			if (screenshotNotificationIsFailure)
			{
				DrawScreenshotFailureGlyph(e.Graphics, screenshotNotificationThumbnail.ClientRectangle);
			}
		};
		layout.Controls.Add(screenshotNotificationThumbnail, 0, 0);
		layout.SetRowSpan(screenshotNotificationThumbnail, 3);

		screenshotNotificationTitleLabel.Dock = DockStyle.Fill;
		screenshotNotificationTitleLabel.Margin = Padding.Empty;
		screenshotNotificationTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
		screenshotNotificationTitleLabel.Font = shellFontBold;
		screenshotNotificationTitleLabel.ForeColor = AccentGreen;
		screenshotNotificationTitleLabel.AutoEllipsis = true;
		screenshotNotificationTitleLabel.UseMnemonic = false;
		layout.Controls.Add(screenshotNotificationTitleLabel, 1, 0);

		screenshotNotificationDetailLabel.Dock = DockStyle.Fill;
		screenshotNotificationDetailLabel.Margin = Padding.Empty;
		screenshotNotificationDetailLabel.Padding = new Padding(0, 2, 4, 2);
		screenshotNotificationDetailLabel.TextAlign = ContentAlignment.TopLeft;
		screenshotNotificationDetailLabel.Font = shellOutputFont;
		screenshotNotificationDetailLabel.ForeColor = PrimaryTextColor;
		screenshotNotificationDetailLabel.AutoEllipsis = true;
		screenshotNotificationDetailLabel.UseMnemonic = false;
		layout.Controls.Add(screenshotNotificationDetailLabel, 1, 1);
		layout.SetColumnSpan(screenshotNotificationDetailLabel, 2);

		TableLayoutPanel actions = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 3,
			RowCount = 1,
			BackColor = CardBackground
		};
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
		actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		ConfigureScreenshotNotificationAction(screenshotNotificationPreviewButton, "PREVIEW", "Preview screenshot in XeCLI");
		ConfigureScreenshotNotificationAction(screenshotNotificationOpenButton, "OPEN", "Open screenshot with the default app");
		ConfigureScreenshotNotificationAction(screenshotNotificationFolderButton, "FOLDER", "Show screenshot in File Explorer");
		screenshotNotificationPreviewButton.Click += delegate
		{
			OpenScreenshotPreview();
		};
		screenshotNotificationOpenButton.Click += delegate
		{
			OpenLatestScreenshotExternally();
		};
		screenshotNotificationFolderButton.Click += delegate
		{
			RevealLatestScreenshotInExplorer();
		};
		actions.Controls.Add(screenshotNotificationPreviewButton, 0, 0);
		actions.Controls.Add(screenshotNotificationOpenButton, 1, 0);
		actions.Controls.Add(screenshotNotificationFolderButton, 2, 0);
		layout.Controls.Add(actions, 1, 2);
		layout.SetColumnSpan(actions, 2);

		ConfigureActionButton(screenshotNotificationCloseButton, "x");
		screenshotNotificationCloseButton.Dock = DockStyle.Fill;
		screenshotNotificationCloseButton.Margin = Padding.Empty;
		screenshotNotificationCloseButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
		screenshotNotificationCloseButton.AccessibleName = "Dismiss screenshot notification";
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationCloseButton, "Dismiss");
		screenshotNotificationCloseButton.Click += delegate
		{
			HideScreenshotNotification();
		};
		layout.Controls.Add(screenshotNotificationCloseButton, 2, 0);

		screenshotNotificationPanel.Controls.Add(layout);
		base.Controls.Add(screenshotNotificationPanel);
		primaryShellLayoutTable?.SizeChanged += delegate
		{
			PositionScreenshotNotification();
		};
		PositionScreenshotNotification();
	}

	private void ConfigureScreenshotNotificationAction(Button button, string text, string toolTip)
	{
		ConfigureActionButton(button, text);
		button.Margin = new Padding(0, 0, 6, 0);
		button.AccessibleName = toolTip;
		screenshotNotificationToolTip.SetToolTip(button, toolTip);
	}

	private void PositionScreenshotNotification()
	{
		if (screenshotNotificationPanel.IsDisposed || primaryShellLayoutTable == null || base.IsDisposed)
		{
			return;
		}
		Control? center = primaryShellLayoutTable.GetControlFromPosition(1, 0);
		if (center == null || center.Width <= 0 || center.Height <= 0)
		{
			return;
		}
		Point centerOrigin = PointToClient(center.PointToScreen(Point.Empty));
		int availableWidth = Math.Max(360, center.Width - ScreenshotNotificationMargin * 2);
		screenshotNotificationPanel.Width = Math.Min(ScreenshotNotificationWidth, availableWidth);
		screenshotNotificationPanel.Height = ScreenshotNotificationHeight;
		int x = centerOrigin.X + center.Width - screenshotNotificationPanel.Width - ScreenshotNotificationMargin;
		int y = centerOrigin.Y + center.Height - screenshotNotificationPanel.Height - CommandInputRowHeight - ScreenshotNotificationMargin;
		screenshotNotificationPanel.Location = new Point(
			Math.Max(ScreenshotNotificationMargin, x),
			Math.Max(ScreenshotNotificationMargin, y));
		if (screenshotNotificationPanel.Visible)
		{
			screenshotNotificationPanel.BringToFront();
		}
	}

	private Control BuildFileBrowserResizeGrip()
	{
		Panel grip = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Cursor = Cursors.SizeNS,
			BackColor = CardBackground,
			MinimumSize = new Size(0, FileBrowserResizeGripHeight)
		};
		grip.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
			{
				return;
			}
			fileBrowserResizeActive = true;
			fileBrowserResizeStartY = PointToClient(Control.MousePosition).Y;
			fileBrowserResizeStartHeight = fileBrowserHeight;
			grip.Capture = true;
		};
		grip.MouseMove += delegate
		{
			if (!fileBrowserResizeActive)
			{
				return;
			}
			int currentY = PointToClient(Control.MousePosition).Y;
			SetFileBrowserHeight(fileBrowserResizeStartHeight - (currentY - fileBrowserResizeStartY));
		};
		grip.MouseUp += delegate
		{
			EndFileBrowserResize(grip);
		};
		grip.MouseLeave += delegate
		{
			if ((Control.MouseButtons & MouseButtons.Left) == 0)
			{
				EndFileBrowserResize(grip);
			}
		};
		grip.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle bounds = grip.ClientRectangle;
			int y = Math.Max(1, bounds.Height / 2);
			int gripCenter = Math.Max(0, bounds.Width / 2);
			int lineHalfWidth = Math.Min(120, Math.Max(34, bounds.Width / 5));
			int lineLeft = Math.Max(18, gripCenter - lineHalfWidth);
			int lineRight = Math.Min(Math.Max(18, bounds.Width - 18), gripCenter + lineHalfWidth);
			using Pen pen = new Pen(Color.FromArgb(136, AccentGreen), 1f);
			e.Graphics.DrawLine(pen, lineLeft, y, Math.Max(lineLeft, lineRight), y);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(176, AccentDim));
			for (int x = Math.Max(24, gripCenter - 18); x <= gripCenter + 18; x += 9)
			{
				e.Graphics.FillRectangle(brush, x, Math.Max(1, y - 1), 3, 3);
			}
		};
		return grip;
	}

	private Control BuildFileBrowserPaneSplitter()
	{
		Panel splitter = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Cursor = Cursors.SizeWE,
			BackColor = CardBackground,
			MinimumSize = new Size(FileBrowserPaneSplitterWidth, 0),
			AccessibleName = "Resize local and remote file panes"
		};
		splitter.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
			{
				return;
			}
			fileBrowserPaneResizeActive = true;
			splitter.Capture = true;
		};
		splitter.MouseMove += delegate
		{
			if (!fileBrowserPaneResizeActive || fileBrowserSplitLayoutTable == null)
			{
				return;
			}
			int localX = fileBrowserSplitLayoutTable.PointToClient(Control.MousePosition).X;
			SetFileBrowserPaneSplit(localX);
		};
		splitter.MouseUp += delegate
		{
			EndFileBrowserPaneResize(splitter);
		};
		splitter.MouseLeave += delegate
		{
			if ((Control.MouseButtons & MouseButtons.Left) == 0)
			{
				EndFileBrowserPaneResize(splitter);
			}
		};
		splitter.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle bounds = splitter.ClientRectangle;
			int centerX = Math.Max(0, bounds.Width / 2);
			using Pen separator = new Pen(Color.FromArgb(90, BorderColor), 1f);
			e.Graphics.DrawLine(separator, centerX, 6, centerX, Math.Max(6, bounds.Height - 6));
			using SolidBrush dot = new SolidBrush(Color.FromArgb(176, AccentDim));
			for (int y = Math.Max(18, bounds.Height / 2 - 18); y <= bounds.Height / 2 + 18; y += 9)
			{
				e.Graphics.FillRectangle(dot, Math.Max(0, centerX - 1), y, 3, 3);
			}
		};
		return splitter;
	}

	private void EndFileBrowserResize(Control grip)
	{
		fileBrowserResizeActive = false;
		grip.Capture = false;
	}

	private void EndFileBrowserPaneResize(Control splitter)
	{
		fileBrowserPaneResizeActive = false;
		splitter.Capture = false;
	}

	private void SetFileBrowserHeight(int requestedHeight)
	{
		int maxHeight = Math.Max(MinFileBrowserHeight, Math.Min(MaxFileBrowserHeight, ClientSize.Height - 340));
		int newHeight = Math.Clamp(requestedHeight, MinFileBrowserHeight, maxHeight);
		if (newHeight == fileBrowserHeight)
		{
			return;
		}
		fileBrowserHeight = newHeight;
		if (fileBrowserRowStyle != null)
		{
			fileBrowserRowStyle.Height = fileBrowserHeight;
		}
		shellLayoutTable?.PerformLayout();
	}

	private void SetFileBrowserPaneSplit(int requestedLocalPaneWidth)
	{
		if (fileBrowserSplitLayoutTable == null || fileBrowserLocalColumnStyle == null || fileBrowserRemoteColumnStyle == null)
		{
			return;
		}
		int availableWidth = Math.Max(1, fileBrowserSplitLayoutTable.ClientSize.Width - FileBrowserPaneSplitterWidth);
		int minimumPaneWidth = GetEffectiveFileBrowserPaneMinimumWidth(availableWidth);
		int localPaneWidth = Math.Clamp(requestedLocalPaneWidth, minimumPaneWidth, availableWidth - minimumPaneWidth);
		float localPercent = Math.Clamp(localPaneWidth * 100f / availableWidth, 1f, 99f);
		fileBrowserLocalColumnStyle.Width = localPercent;
		fileBrowserRemoteColumnStyle.Width = 100f - localPercent;
		fileBrowserSplitLayoutTable.PerformLayout();
	}

	private static int GetEffectiveFileBrowserPaneMinimumWidth(int availableWidth)
	{
		if (availableWidth <= 2)
		{
			return 1;
		}
		int balancedMinimum = Math.Max(1, (availableWidth - 16) / 2);
		return Math.Min(MinFileBrowserPaneWidth, balancedMinimum);
	}

	private static Color GetStateMessageColor(string? message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return EmptyStateColor;
		}
		string text = message.Trim();
		if (text.Contains("failed", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("failure", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("denied", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("error", StringComparison.OrdinalIgnoreCase))
		{
			return FailureStateColor;
		}
		if (text.Contains("loading", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("connecting", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("probing", StringComparison.OrdinalIgnoreCase))
		{
			return LoadingStateColor;
		}
		if (text.Contains("empty", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("no live", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("not connected", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("connect to", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("detected", StringComparison.OrdinalIgnoreCase))
		{
			return EmptyStateColor;
		}
		return AccentDim;
	}

	private Panel BuildLocalFilePane()
	{
		Panel panel = CreateCardPanel(TerminalBackground, new Padding(5, 3, 5, 5));
		panel.Margin = new Padding(0, 0, 3, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 5;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
		localPathLabel.Dock = DockStyle.Fill;
		localPathLabel.ForeColor = AccentDim;
		localPathLabel.Font = shellFont;
		localPathLabel.TextAlign = ContentAlignment.MiddleLeft;
		localPathLabel.AutoEllipsis = true;
		localPathLabel.Text = string.Empty;
		ConfigureInputTextBox(localPathTextBox, string.Empty);
		ConfigureInputHostPanel(localPathHostPanel, localPathTextBox, "LOCAL PATH");
		ConfigureFilePathTextBox(localPathTextBox, "Local file browser path");
		ConfigureToggleButton(localTabButton, "LOCAL");
		localTabButton.Cursor = Cursors.Default;
		localTabButton.TabStop = false;
		localTabButton.AccessibleRole = AccessibleRole.PageTab;
		localTabButton.AccessibleName = "Local files";
		ApplyToggleButtonState(localTabButton, active: true);
		ConfigureFilePaneButton(localUpButton, "↑", "Parent directory");
		ConfigureFilePaneButton(localRefreshButton, "↻", "Refresh local files");
		localFileList.Dock = DockStyle.Fill;
		localFileList.BackColor = TerminalBackground;
		localFileList.ForeColor = PrimaryTextColor;
		localFileList.Font = shellFont;
		localFileList.HeaderText = "NAME";
		localFileList.AccessibleName = "Local file list";
		localFileList.AccessibleDescription = "Files and folders in the selected local path.";
		localFileList.AccessibleRole = AccessibleRole.List;
		tableLayoutPanel.Controls.Add(localTabButton, 0, 1);
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
		Panel panel = CreateCardPanel(TerminalBackground, new Padding(5, 3, 5, 5));
		panel.Margin = new Padding(3, 0, 0, 0);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 5;
		tableLayoutPanel.RowCount = 4;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		remotePathRowStyle = new RowStyle(SizeType.Absolute, 48f);
		tableLayoutPanel.RowStyles.Add(remotePathRowStyle);
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
		remotePathLabel.Dock = DockStyle.Fill;
		remotePathLabel.ForeColor = AccentDim;
		remotePathLabel.Font = shellFont;
		remotePathLabel.TextAlign = ContentAlignment.MiddleLeft;
		remotePathLabel.AutoEllipsis = true;
		remotePathLabel.Text = string.Empty;
		ConfigureFilePaneButton(remoteUpButton, "↑", "Parent directory");
		ConfigureFilePaneButton(remoteRefreshButton, "↻", "Refresh remote files");
		ConfigureToggleButton(ftpTabButton, "FTP");
		ConfigureToggleButton(queueTabButton, "QUEUE");
		ftpTabButton.Margin = new Padding(0, 0, 6, 0);
		queueTabButton.Margin = new Padding(0, 0, 10, 0);
		ConfigureInputTextBox(remotePathTextBox, string.Empty);
		ConfigureInputHostPanel(remotePathHostPanel, remotePathTextBox, "REMOTE PATH");
		ConfigureFilePathTextBox(remotePathTextBox, "Remote FTP browser path");
		remoteContentHost.Dock = DockStyle.Fill;
		remoteContentHost.BackColor = TerminalBackground;
		remoteContentHost.Margin = Padding.Empty;
		remoteFileList.Dock = DockStyle.Fill;
		remoteFileList.BackColor = TerminalBackground;
		remoteFileList.ForeColor = PrimaryTextColor;
		remoteFileList.Font = shellFont;
		remoteFileList.HeaderText = "NAME";
		remoteFileList.AccessibleName = "Remote file list";
		remoteFileList.AccessibleDescription = "Files and folders in the selected console FTP path.";
		remoteFileList.AccessibleRole = AccessibleRole.List;
		transferQueueList.Dock = DockStyle.Fill;
		transferQueueList.BackColor = TerminalBackground;
		transferQueueList.ForeColor = PrimaryTextColor;
		transferQueueList.Font = shellFont;
		transferQueueList.AccessibleName = "Transfer queue";
		transferQueueList.AccessibleDescription = "Current and recent GUI file transfer operations.";
		transferQueueList.AccessibleRole = AccessibleRole.List;
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

	private void ConfigureFilePaneButton(Button button, string icon, string accessibleName)
	{
		button.Dock = DockStyle.Fill;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 1;
		button.BackColor = TerminalBackground;
		button.ForeColor = PrimaryTextColor;
		button.Font = new Font("Segoe UI Symbol", 11f, FontStyle.Regular, GraphicsUnit.Point);
		button.Text = icon;
		button.AccessibleName = accessibleName;
		button.AccessibleDescription = accessibleName;
		button.AccessibleRole = AccessibleRole.PushButton;
		filePathToolTip.SetToolTip(button, accessibleName);
		button.MinimumSize = new Size(0, 30);
		button.Margin = Padding.Empty;
		button.Padding = new Padding(2, 0, 2, 0);
		button.Cursor = Cursors.Hand;
		if (button is TerminalButton terminalButton)
		{
			terminalButton.EnabledBackColor = TerminalBackground;
			terminalButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.14f);
			terminalButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.22f);
			terminalButton.BorderColor = Color.FromArgb(122, BorderColor);
			terminalButton.DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);
			terminalButton.EnabledTextColor = PrimaryTextColor;
			terminalButton.DisabledTextColor = AccentDim;
		}
	}

	private void InitializeFileManagerInteractions()
	{
		InitializeFilePaneMenus();
		InitializeSurfaceMenus();
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
		avatarButton.Click += async delegate
		{
			await LaunchAvatarBrowserAsync();
		};
	}

	private async Task LaunchAvatarBrowserAsync()
	{
		try
		{
			if (!AvatarCommandHelpers.TryBuildAvatarBrowseGuiCommand(RuntimePresenceState.Current, out string command, out string? actionableMessage))
			{
				AppendSystemLine(actionableMessage ?? "Avatar browser is unavailable.", Color.Gold);
				return;
			}
			await AvatarCommandHelpers.RunWithWatchdogAsync(
				() => ExecuteCommandAsync(command),
				AvatarCommandHelpers.DefaultOperationTimeout + TimeSpan.FromSeconds(15),
				() =>
				{
					AppendSystemLine("Avatar browser timed out after 45 seconds.", Color.Gold);
					CancelActiveCommand();
				});
		}
		catch (Exception ex)
		{
			AppendSystemLine("Avatar browser launch failed: " + ex.Message, FailureStateColor);
		}
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

	private void InitializeSurfaceMenus()
	{
		if (terminalOutput.ContextMenuStrip != null)
		{
			return;
		}
		ConfigurePaneMenu(terminalOutputMenu);
		ConfigurePaneMenu(inventoryMenu);
		ConfigurePaneMenu(driveMenu);
		ConfigurePaneMenu(diagnosticsMenu);
		terminalOutputMenu.Opening += delegate
		{
			PopulateContextMenu(terminalOutputMenu);
		};
		diagnosticsMenu.Opening += delegate
		{
			PopulateContextMenu(diagnosticsMenu);
		};
		terminalOutput.ContextMenuStrip = terminalOutputMenu;
		rightNetworkInfoPanel.ContextMenuStrip = diagnosticsMenu;
		rightNetworkLabel.ContextMenuStrip = diagnosticsMenu;
		rightDetailLabel.ContextMenuStrip = diagnosticsMenu;
		rightTempLabel.ContextMenuStrip = diagnosticsMenu;
		inventoryList.ItemContextRequested += delegate(int index, Point screenPoint)
		{
			inventoryContextIndex = index;
			ShowContextMenu(inventoryMenu, screenPoint);
		};
		drivesList.ItemContextRequested += delegate(int index, Point screenPoint)
		{
			driveContextIndex = index;
			ShowContextMenu(driveMenu, screenPoint);
		};
	}

	private void ConfigurePaneMenu(ContextMenuStrip menu)
	{
		menu.ShowImageMargin = false;
		menu.BackColor = CardBackground;
		menu.ForeColor = PrimaryTextColor;
		menu.Font = shellFont;
		menu.Padding = new Padding(2);
		menu.Renderer = new ToolStripProfessionalRenderer(new TerminalMenuColorTable())
		{
			RoundedEdges = false
		};
	}

	private void RefreshContextMenuThemes()
	{
		foreach (ContextMenuStrip menu in new[]
		{
			localFileMenu,
			localEmptyMenu,
			remoteFileMenu,
			remoteEmptyMenu,
			terminalOutputMenu,
			inventoryMenu,
			driveMenu,
			diagnosticsMenu
		})
		{
			ConfigurePaneMenu(menu);
		}
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
			else if (ReferenceEquals(menu, terminalOutputMenu))
			{
				BuildTerminalOutputMenu(menu);
			}
			else if (ReferenceEquals(menu, inventoryMenu))
			{
				BuildInventoryMenu(menu);
			}
			else if (ReferenceEquals(menu, driveMenu))
			{
				BuildDriveMenu(menu);
			}
			else if (ReferenceEquals(menu, diagnosticsMenu))
			{
				BuildDiagnosticsMenu(menu);
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

	private void BuildTerminalOutputMenu(ContextMenuStrip menu)
	{
		bool hasSelection = !string.IsNullOrEmpty(terminalOutput.SelectedText);
		bool hasTranscript = !string.IsNullOrEmpty(terminalOutput.Text);
		AddMenuItem(menu, "Copy Selection", hasSelection, delegate
		{
			CopyTextToClipboard(terminalOutput.SelectedText, "Selection copied.");
		});
		AddMenuItem(menu, "Copy All", hasTranscript, delegate
		{
			CopyTextToClipboard(terminalOutput.Text, "Transcript copied.");
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Export Transcript...", hasTranscript, ExportTerminalTranscript);
		AddMenuItem(menu, "Clear Transcript", hasTranscript, ClearTerminalWorkspace);
	}

	private void BuildInventoryMenu(ContextMenuStrip menu)
	{
		string? itemText = inventoryList.GetItemTitle(inventoryContextIndex);
		bool hasItem = !string.IsNullOrWhiteSpace(itemText);
		string itemType = inventoryShowsModules ? "Module" : "Plugin";
		AddMenuItem(menu, "Copy " + itemType + " Name", hasItem, delegate
		{
			CopyTextToClipboard(itemText, itemType + " name copied.");
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh " + (inventoryShowsModules ? "Modules" : "Plugins"), !connectAttemptInFlight && !commandInFlight && !fileTransferInFlight, delegate
		{
			_ = PollTelemetrySafeAsync(forceHeavyRefresh: true);
		});
	}

	private void BuildDriveMenu(ContextMenuStrip menu)
	{
		DriveInventoryEntry? entry = drivesList.GetEntry(driveContextIndex);
		bool hasDrive = entry != null && !string.IsNullOrWhiteSpace(entry.Name) && !DriveInventoryPanel.IsPlaceholderEntry(entry);
		string drivePath = hasDrive ? BuildRemoteDrivePath(entry!.Name) : "/";
		bool canBrowse = hasDrive && !shellDisconnected && !connectAttemptInFlight && !fileTransferInFlight && latestSnapshot?.FtpServiceReachable != false;
		AddMenuItem(menu, "Browse In FTP", canBrowse, delegate
		{
			SetRemotePaneMode(showQueue: false);
			remoteCurrentPath = drivePath;
			_ = RefreshRemoteBrowserAsync(force: true);
		});
		AddMenuItem(menu, "Copy Drive Path", hasDrive, delegate
		{
			CopyTextToClipboard(drivePath, "Drive path copied.");
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh Drives", !connectAttemptInFlight && !commandInFlight && !fileTransferInFlight, delegate
		{
			_ = PollTelemetrySafeAsync(forceHeavyRefresh: true);
		});
	}

	private void BuildDiagnosticsMenu(ContextMenuStrip menu)
	{
		AddMenuItem(menu, "Copy Status", true, delegate
		{
			CopyTextToClipboard(connectionStatusTextSource, "Status copied.");
		});
		AddMenuItem(menu, "Copy Diagnostics Summary", true, delegate
		{
			CopyTextToClipboard(BuildDiagnosticsSummary(), "Diagnostics copied.");
		});
		AddMenuSeparator(menu);
		AddMenuItem(menu, "Refresh Diagnostics", !shellDisconnected && !connectAttemptInFlight && !commandInFlight && !fileTransferInFlight, delegate
		{
			_ = PollTelemetrySafeAsync(forceHeavyRefresh: true);
		});
		AddMenuItem(menu, "Export Transcript...", !string.IsNullOrEmpty(terminalOutput.Text), ExportTerminalTranscript);
	}

	private static string BuildRemoteDrivePath(string driveName)
	{
		string text = driveName.Trim().Trim('/', '\\').TrimEnd(':');
		return string.IsNullOrWhiteSpace(text) ? "/" : FtpHelpers.NormalizePath("/" + text);
	}

	private string BuildDiagnosticsSummary()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("XeCLI diagnostics");
		stringBuilder.Append("Captured: ").AppendLine(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
		stringBuilder.Append("State: ").AppendLine(connectionStatusTextSource);
		stringBuilder.Append("Target: ").AppendLine(FormatCurrentTarget());
		stringBuilder.Append("Thermals: ").AppendLine(footerThermalTextSource);
		if (!string.IsNullOrWhiteSpace(rightNetworkTextSource))
		{
			stringBuilder.AppendLine().AppendLine(rightNetworkTextSource);
		}
		if (!string.IsNullOrWhiteSpace(rightDetailTextSource))
		{
			stringBuilder.AppendLine().AppendLine(rightDetailTextSource);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private void CopyTextToClipboard(string? text, string successMessage)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			Clipboard.SetText(text);
			ShowStatusToast(TranslateTerminalText(successMessage));
		}
		catch
		{
			ShowStatusToast(TranslateTerminalText("Clipboard unavailable."));
		}
	}

	private void ExportTerminalTranscript()
	{
		if (string.IsNullOrEmpty(terminalOutput.Text))
		{
			return;
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Export XeCLI Transcript",
			Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
			DefaultExt = "txt",
			AddExtension = true,
			FileName = "XeCLI-transcript-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt",
			RestoreDirectory = true
		};
		if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		try
		{
			File.WriteAllText(saveFileDialog.FileName, terminalOutput.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			ShowStatusToast(TranslateTerminalText("Transcript exported."));
		}
		catch (Exception ex)
		{
			ShowStatusToast(TranslateTerminalText("Transcript export failed: ") + ex.Message);
		}
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
		TransferConflictPolicy conflictPolicy = CreateTransferConflictPolicy();
		AppendSystemLine("Download from: " + string.Join(" | ", array.Select((FileEntryView entry) => entry.FullPath)), AccentDim);
		AppendSystemLine("Save to: " + localTransferTargetDirectory, AccentDim);
		bool flag = await RunFileTransferAsync("DOWNLOADING FILES", "ftp get --remote " + string.Join(";", array.Select((FileEntryView entry) => entry.FullPath)), requiresRemote: true, refreshRemote: false, refreshLocal: true, async delegate(CancellationToken cancellationToken)
		{
			list.AddRange(await DownloadRemoteEntriesToLocalDirectoryAsync(array, localTransferTargetDirectory, conflictPolicy, cancellationToken));
		});
		AppendTransferConflictSummary(conflictPolicy);
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
		TransferConflictPolicy conflictPolicy = CreateTransferConflictPolicy();
		await RunFileTransferAsync("UPLOADING FILES", "ftp put --local " + string.Join(";", array), requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			await UploadLocalPathsToRemoteDirectoryAsync(array, destinationRemoteDirectory, conflictPolicy, cancellationToken);
		});
		AppendTransferConflictSummary(conflictPolicy);
	}

	private async Task PasteClipboardToRemoteAsync(string destinationRemoteDirectory)
	{
		PaneClipboardEntry paneClipboardEntry = this.paneClipboardEntry ?? throw new InvalidOperationException("Nothing is queued for paste.");
		string text = paneClipboardEntry.Move ? "MOVING ENTRY" : "COPYING ENTRY";
		string text2 = paneClipboardEntry.IsRemote ? ((paneClipboardEntry.Move ? "ftp mv --from " : "ftp cp --from ") + paneClipboardEntry.FullPath) : ("ftp put --local " + paneClipboardEntry.FullPath);
		TransferConflictPolicy conflictPolicy = CreateTransferConflictPolicy();
		await RunFileTransferAsync(text, text2, requiresRemote: true, refreshRemote: true, refreshLocal: false, async delegate(CancellationToken cancellationToken)
		{
			if (paneClipboardEntry.IsRemote)
			{
				bool completed = await PasteRemoteClipboardToRemoteAsync(paneClipboardEntry, destinationRemoteDirectory, conflictPolicy, cancellationToken);
				if (completed && paneClipboardEntry.Move)
				{
					this.paneClipboardEntry = null;
				}
			}
			else
			{
				await UploadLocalPathsToRemoteDirectoryAsync(new string[1] { paneClipboardEntry.FullPath }, destinationRemoteDirectory, conflictPolicy, cancellationToken);
				if (paneClipboardEntry.Move && conflictPolicy.SkippedCount == 0)
				{
					DeleteLocalPath(paneClipboardEntry.FullPath, paneClipboardEntry.IsDirectory);
					this.paneClipboardEntry = null;
				}
			}
		});
		AppendTransferConflictSummary(conflictPolicy);
	}

	private async Task PasteClipboardToLocalAsync(string? destinationLocalDirectory = null)
	{
		PaneClipboardEntry paneClipboardEntry = this.paneClipboardEntry ?? throw new InvalidOperationException("Nothing is queued for paste.");
		string localTransferTargetDirectory = destinationLocalDirectory ?? string.Empty;
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
		TransferConflictPolicy conflictPolicy = CreateTransferConflictPolicy();
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
			list.AddRange(await DownloadRemoteEntriesToLocalDirectoryAsync(new FileEntryView[1] { fileEntryView }, localTargetDirectory, conflictPolicy, cancellationToken));
			if (paneClipboardEntry.Move && list.Count > 0)
			{
				await DeleteRemotePathAsync(paneClipboardEntry.FullPath, paneClipboardEntry.IsDirectory, cancellationToken);
				this.paneClipboardEntry = null;
			}
		});
		AppendTransferConflictSummary(conflictPolicy);
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
			AppendSystemLine(TranslateTerminalText("Wait for the current console connection attempt to finish."), Color.Gold);
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
		string? completionStatus = null;
		footerOperationStatusOverride = null;
		Interlocked.Increment(ref telemetryOperationEpoch);
		fileTransferInFlight = true;
		activeTransferCommand = activityCommand;
		UpdateCommandSubmitButtonState();
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
			completionStatus = "TRANSFER COMPLETE";
			result = true;
		}
		catch (OperationCanceledException)
		{
			RecordTransferActivity(activityCommand, "cancelled", "Cancelled by user");
			AppendSystemLine(footerText + " cancelled.", WarningColor);
			completionStatus = "TRANSFER CANCELLED";
		}
		catch (Exception ex)
		{
			string detail = FtpHelpers.DescribeTransferException(ex);
			RecordTransferActivity(activityCommand, "failed", detail);
			AppendSystemLine(footerText + " failed: " + detail, FailureStateColor);
			completionStatus = "TRANSFER FAILED";
		}
		finally
		{
			fileTransferInFlight = false;
			activeTransferCommand = null;
			footerOperationStatusOverride = completionStatus;
			CancelAndDispose(ref fileTransferCts);
			UpdateCommandSubmitButtonState();
			UpdateLiveHints(latestSnapshot);
			bool flag = !base.IsDisposed && num == Volatile.Read(ref sessionEpoch) && string.Equals(text, currentTargetIp, StringComparison.OrdinalIgnoreCase) && num2 == currentTargetPort;
			bool closeAfterTransfer = closeRequestedDuringTransfer;
			if (refreshLocal && flag && !closeAfterTransfer)
			{
				await RefreshLocalBrowserAsync();
			}
			if (refreshRemote && flag && !closeAfterTransfer)
			{
				await RefreshRemoteBrowserAsync(force: true);
			}
			UpdateLiveHints(latestSnapshot);
			if (closeAfterTransfer && !base.IsDisposed)
			{
				allowCloseAfterTransfer = true;
				BeginInvoke(new MethodInvoker(Close));
			}
		}
		return result;
	}

	private (int Port, string User, string Pass, int TimeoutMs) GetTerminalFtpSettings(CliConfig? cliConfig = null)
	{
		cliConfig ??= CliConfig.Load();
		TargetProfileRecord? profile = TargetProfileStore.ResolveCurrentProfile(TargetProfileStore.Load(), cliConfig);
		FtpConnectionDefaults configured = FtpEndpointHelpers.ResolveConfiguredConnection(cliConfig, profile);
		return (configured.Port, configured.User, configured.Pass, Math.Clamp(connectionTimeoutMs, 1500, 7000));
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
		form.ForeColor = PrimaryTextColor;
		form.ClientSize = new Size(420, 132);
		Label label = new Label
		{
			Text = labelText,
			ForeColor = PrimaryTextColor,
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
			ForeColor = PrimaryTextColor,
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

	internal TransferDestinationResolution ResolveLocalTransferDestination(
		string direction,
		string sourcePath,
		string desiredLocalPath,
		TransferEntryKind sourceKind,
		TransferConflictPolicy conflictPolicy,
		TransferItemMetadata sourceMetadata = default,
		Func<CancellationToken, Task<TransferHashComparison>>? compareHashesAsync = null)
	{
		TransferEntryKind destinationKind = GetLocalEntryKind(desiredLocalPath);
		if (destinationKind == TransferEntryKind.None)
		{
			return new TransferDestinationResolution(desiredLocalPath, OverwriteApproved: false);
		}

		string keepBothPath = GetUniqueLocalPath(desiredLocalPath, sourceKind == TransferEntryKind.Directory);
		TransferItemMetadata effectiveSourceMetadata = HasTransferMetadata(sourceMetadata)
			? sourceMetadata
			: GetLocalTransferMetadata(sourcePath, sourceKind);
		TransferItemMetadata destinationMetadata = GetLocalTransferMetadata(desiredLocalPath, destinationKind);
		TransferConflictDecision decision = ResolveTransferConflictDecision(
			new TransferConflictPrompt(
				direction,
				sourcePath,
				desiredLocalPath,
				keepBothPath,
				sourceKind,
				destinationKind,
				effectiveSourceMetadata,
				destinationMetadata,
				sourceKind == TransferEntryKind.File && destinationKind == TransferEntryKind.File ? compareHashesAsync : null),
			conflictPolicy);
		switch (decision)
		{
			case TransferConflictDecision.Replace:
				if (sourceKind == TransferEntryKind.File && destinationKind == TransferEntryKind.File)
				{
					return new TransferDestinationResolution(desiredLocalPath, OverwriteApproved: true);
				}
				DeleteLocalPath(desiredLocalPath, destinationKind == TransferEntryKind.Directory);
				return new TransferDestinationResolution(desiredLocalPath, OverwriteApproved: false);
			case TransferConflictDecision.KeepBoth:
				return new TransferDestinationResolution(keepBothPath, OverwriteApproved: false);
			case TransferConflictDecision.Skip:
				conflictPolicy.SkippedCount++;
				return new TransferDestinationResolution(null, OverwriteApproved: false);
			default:
				throw new OperationCanceledException("File transfer cancelled at a destination conflict.");
		}
	}

	private async Task<TransferDestinationResolution> ResolveRemoteTransferDestinationAsync(
		AsyncFtpClient client,
		string direction,
		string sourcePath,
		string desiredRemotePath,
		TransferEntryKind sourceKind,
		TransferConflictPolicy conflictPolicy,
		CancellationToken cancellationToken)
	{
		string normalizedPath = FtpHelpers.NormalizePath(desiredRemotePath);
		TransferEntryKind destinationKind = await GetRemoteEntryKindAsync(client, normalizedPath);
		if (destinationKind == TransferEntryKind.None)
		{
			return new TransferDestinationResolution(normalizedPath, OverwriteApproved: false);
		}

		string keepBothPath = await GetUniqueRemotePathAsync(client, normalizedPath, sourceKind == TransferEntryKind.Directory);
		TransferItemMetadata sourceMetadata = GetLocalTransferMetadata(sourcePath, sourceKind);
		bool sourceIsLocal = HasTransferMetadata(sourceMetadata) || File.Exists(sourcePath) || Directory.Exists(sourcePath);
		if (!sourceIsLocal)
		{
			sourceMetadata = await GetRemoteTransferMetadataAsync(client, sourcePath, sourceKind, cancellationToken);
		}
		TransferItemMetadata destinationMetadata = await GetRemoteTransferMetadataAsync(client, normalizedPath, destinationKind, cancellationToken);
		Func<CancellationToken, Task<TransferHashComparison>>? compareHashesAsync = null;
		if (sourceKind == TransferEntryKind.File && destinationKind == TransferEntryKind.File)
		{
			compareHashesAsync = sourceIsLocal
				? token => CompareTransferHashesAsync(
					innerToken => ComputeLocalTransferSha256Async(sourcePath, innerToken),
					innerToken => ComputeRemoteTransferSha256Async(client, normalizedPath, innerToken),
					token)
				: token => CompareTransferHashesAsync(
					innerToken => ComputeRemoteTransferSha256Async(client, sourcePath, innerToken),
					innerToken => ComputeRemoteTransferSha256Async(client, normalizedPath, innerToken),
					token);
		}
		TransferConflictDecision decision = ResolveTransferConflictDecision(
			new TransferConflictPrompt(
				direction,
				sourcePath,
				normalizedPath,
				keepBothPath,
				sourceKind,
				destinationKind,
				sourceMetadata,
				destinationMetadata,
				compareHashesAsync),
			conflictPolicy);
		switch (decision)
		{
			case TransferConflictDecision.Replace:
				if (sourceKind == TransferEntryKind.File && destinationKind == TransferEntryKind.File)
				{
					return new TransferDestinationResolution(normalizedPath, OverwriteApproved: true);
				}
				await DeleteRemoteExistingPathAsync(client, normalizedPath, destinationKind, cancellationToken);
				return new TransferDestinationResolution(normalizedPath, OverwriteApproved: false);
			case TransferConflictDecision.KeepBoth:
				return new TransferDestinationResolution(keepBothPath, OverwriteApproved: false);
			case TransferConflictDecision.Skip:
				conflictPolicy.SkippedCount++;
				return new TransferDestinationResolution(null, OverwriteApproved: false);
			default:
				throw new OperationCanceledException("File transfer cancelled at a destination conflict.");
		}
	}

	private TransferConflictDecision ResolveTransferConflictDecision(TransferConflictPrompt prompt, TransferConflictPolicy conflictPolicy)
	{
		conflictPolicy.ConflictCount++;
		if (conflictPolicy.RemainingDecision.HasValue)
		{
			return conflictPolicy.RemainingDecision.Value;
		}

		TransferConflictResult result = ShowTransferConflictDialog(prompt);
		if (result.ApplyToRemaining && result.Decision != TransferConflictDecision.Cancel)
		{
			conflictPolicy.RemainingDecision = result.Decision;
		}
		return result.Decision;
	}

	private static TransferConflictPolicy CreateTransferConflictPolicy()
	{
		CliConfig.TryLoad(out CliConfig config);
		return new TransferConflictPolicy
		{
			RemainingDecision = CliPreferences.NormalizeFtpConflictBehavior(config.FtpConflictBehavior) switch
			{
				"keep-both" => TransferConflictDecision.KeepBoth,
				"skip" => TransferConflictDecision.Skip,
				_ => null
			}
		};
	}

	private TransferConflictResult ShowTransferConflictDialog(TransferConflictPrompt prompt)
	{
		using Font dialogBodyFont = new Font("Segoe UI", 9.25f, FontStyle.Regular, GraphicsUnit.Point);
		using Font dialogBodyBoldFont = new Font("Segoe UI", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
		using Font dialogHeadingFont = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Point);
		using Font dialogEyebrowFont = new Font("Segoe UI", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
		using Font dialogPathFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
		using Font dialogButtonFont = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
		using Font dialogMetadataCaptionFont = new Font("Segoe UI", 7.75f, FontStyle.Bold, GraphicsUnit.Point);
		using Font dialogMetadataValueFont = new Font("Segoe UI", 9.25f, FontStyle.Bold, GraphicsUnit.Point);
		using Bitmap warningIcon = SystemIcons.Warning.ToBitmap();
		using ToolTip conflictToolTip = new ToolTip
		{
			AutoPopDelay = 12000,
			InitialDelay = 450,
			ReshowDelay = 100,
			ShowAlways = true
		};
		TransferConflictDialogResources dialogResources = new TransferConflictDialogResources
		{
			CaptionFont = dialogEyebrowFont,
			NameFont = dialogBodyBoldFont,
			PathFont = dialogPathFont,
			MetadataCaptionFont = dialogMetadataCaptionFont,
			MetadataValueFont = dialogMetadataValueFont,
			ToolTip = conflictToolTip
		};
		using Form form = new Form
		{
			Text = "XeCLI - Destination Conflict",
			StartPosition = FormStartPosition.CenterParent,
			FormBorderStyle = FormBorderStyle.Sizable,
			MinimizeBox = false,
			MaximizeBox = false,
			ShowInTaskbar = false,
			AutoScaleMode = AutoScaleMode.Dpi,
			AutoScaleDimensions = new SizeF(96f, 96f),
			BackColor = ShellBackground,
			ForeColor = PrimaryTextColor,
			Font = dialogBodyFont,
			ClientSize = new Size(920, 600),
			AccessibleName = "File transfer destination conflict",
			AccessibleDescription = "Compare the incoming and existing items before choosing how to continue."
		};
		if (Icon != null)
		{
			form.Icon = Icon;
		}
		form.TopMost = TopMost;

		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Padding = new Padding(18, 14, 18, 14),
			BackColor = ShellBackground
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f));

		Panel detailViewport = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = ShellBackground,
			AutoScroll = true,
			TabStop = true,
			AccessibleName = "Conflict comparison details"
		};
		TableLayoutPanel detailLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			Height = 344,
			BackColor = ShellBackground
		};
		detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 172f));
		detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
		detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));

		TableLayoutPanel header = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 3,
			Margin = new Padding(0, 0, 0, 6),
			BackColor = ShellBackground
		};
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		header.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
		header.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
		header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		PictureBox icon = new PictureBox
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 3, 12, 21),
			Image = warningIcon,
			SizeMode = PictureBoxSizeMode.Zoom,
			AccessibleName = "File transfer conflict warning"
		};
		Label eyebrow = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = prompt.Direction.ToUpperInvariant(),
			ForeColor = AccentDim,
			Font = dialogEyebrowFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		Label heading = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = prompt.DestinationKind == TransferEntryKind.Directory
				? "A folder with this name already exists"
				: "A file with this name already exists",
			ForeColor = PrimaryTextColor,
			Font = dialogHeadingFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		Label summary = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = "Choose how XeCLI should handle the incoming item. Nothing will be replaced without your approval.",
			ForeColor = AccentDim,
			Font = dialogBodyFont,
			TextAlign = ContentAlignment.TopLeft,
			AutoEllipsis = true
		};
		header.Controls.Add(icon, 0, 0);
		header.SetRowSpan(icon, 3);
		header.Controls.Add(eyebrow, 1, 0);
		header.Controls.Add(heading, 1, 1);
		header.Controls.Add(summary, 1, 2);

		TableLayoutPanel comparison = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Margin = Padding.Empty,
			BackColor = ShellBackground
		};
		comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		Panel source = CreateTransferConflictDetailPanel(
			new TransferConflictDetailSpec(
				prompt.SourceKind == TransferEntryKind.Directory ? "INCOMING FOLDER" : "INCOMING FILE",
				prompt.SourcePath,
				prompt.SourceKind,
				prompt.SourceMetadata,
				AccentGreen,
				Emphasized: false,
				IncludeMetadata: true),
			dialogResources);
		source.Margin = new Padding(0, 0, 6, 8);
		Panel destination = CreateTransferConflictDetailPanel(
			new TransferConflictDetailSpec(
				"EXISTING DESTINATION",
				prompt.DestinationPath,
				prompt.DestinationKind,
				prompt.DestinationMetadata,
				WarningColor,
				Emphasized: false,
				IncludeMetadata: true),
			dialogResources);
		destination.Margin = new Padding(6, 0, 0, 8);
		comparison.Controls.Add(source, 0, 0);
		comparison.Controls.Add(destination, 1, 0);

		Panel comparisonTools = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 0, 0, 8),
			Padding = new Padding(14, 3, 8, 3),
			BackColor = CardBackground
		};
		comparisonTools.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle bounds = comparisonTools.ClientRectangle;
			if (bounds.Width <= 1 || bounds.Height <= 1)
			{
				return;
			}
			using Pen border = new Pen(Color.FromArgb(118, BorderColor), 1f);
			using Pen rail = new Pen(Color.FromArgb(172, AccentDim), 2f);
			e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
			e.Graphics.DrawLine(rail, 1, 1, 1, bounds.Height - 2);
		};
		TableLayoutPanel comparisonToolsLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 2,
			Margin = Padding.Empty,
			BackColor = comparisonTools.BackColor
		};
		comparisonToolsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146f));
		comparisonToolsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		comparisonToolsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154f));
		comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
		comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label quickComparisonTitle = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = "QUICK COMPARISON",
			ForeColor = AccentDim,
			Font = dialogEyebrowFont,
			TextAlign = ContentAlignment.MiddleLeft
		};
		Label quickComparisonValue = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = BuildTransferConflictComparisonText(prompt),
			ForeColor = GetTransferConflictComparisonColor(prompt),
			Font = dialogBodyFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = false,
			UseCompatibleTextRendering = true,
			UseMnemonic = false
		};
		Label contentCheckTitle = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = "CONTENT CHECK",
			ForeColor = AccentDim,
			Font = dialogEyebrowFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AccessibleName = "Content comparison heading"
		};
		Label contentCheckStatus = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = prompt.CompareHashesAsync == null
				? "SHA-256 comparison is unavailable for this item."
				: "Optional SHA-256 comparison. Reads both files once.",
			ForeColor = AccentDim,
			Font = dialogBodyFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = false,
			UseCompatibleTextRendering = true,
			UseMnemonic = false,
			AccessibleName = "Content comparison status"
		};
		TerminalButton compareContentsButton = new TerminalButton
		{
			Margin = new Padding(8, 0, 0, 0),
			Enabled = prompt.CompareHashesAsync != null
		};
		ConfigureActionButton(compareContentsButton, "Compare Contents");
		compareContentsButton.Dock = DockStyle.None;
		compareContentsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		compareContentsButton.Size = new Size(120, 32);
		compareContentsButton.Margin = new Padding(8, 2, 0, 0);
		compareContentsButton.Font = dialogButtonFont;
		compareContentsButton.EnabledBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.10f);
		compareContentsButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.18f);
		compareContentsButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.24f);
		compareContentsButton.EnabledTextColor = AccentGreen;
		compareContentsButton.BorderColor = InterpolateColor(BorderColor, AccentGreen, 0.46f);
		compareContentsButton.AccessibleName = "Compare file contents with SHA-256";
		compareContentsButton.AccessibleDescription = "Reads the incoming and existing files once, then compares their SHA-256 hashes.";
		conflictToolTip.SetToolTip(compareContentsButton, compareContentsButton.AccessibleDescription);
		comparisonToolsLayout.Controls.Add(quickComparisonTitle, 0, 0);
		comparisonToolsLayout.Controls.Add(quickComparisonValue, 1, 0);
		comparisonToolsLayout.SetColumnSpan(quickComparisonValue, 2);
		comparisonToolsLayout.Controls.Add(contentCheckTitle, 0, 1);
		comparisonToolsLayout.Controls.Add(contentCheckStatus, 1, 1);
		comparisonToolsLayout.Controls.Add(compareContentsButton, 2, 1);
		comparisonTools.Controls.Add(comparisonToolsLayout);

		Panel keepBoth = CreateTransferConflictDetailPanel(
			new TransferConflictDetailSpec(
				"KEEP BOTH CREATES",
				prompt.KeepBothPath,
				prompt.SourceKind,
				prompt.SourceMetadata,
				AccentGreen,
				Emphasized: true,
				IncludeMetadata: false),
			dialogResources);
		keepBoth.Margin = new Padding(0, 0, 0, 8);

		bool destructiveFolderReplace = prompt.DestinationKind == TransferEntryKind.Directory;
		string warningText = destructiveFolderReplace
			? "Replacing removes the existing destination folder and all of its contents before transfer."
			: "Replacing writes the incoming file over the existing destination file.";
		const int decisionButtonLogicalHeight = 34;
		Panel footer = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 8, 0, 0),
			BackColor = ShellBackground,
			AccessibleName = "Conflict dialog action footer"
		};
		footer.Paint += delegate(object? _, PaintEventArgs e)
		{
			using Pen divider = new Pen(Color.FromArgb(150, BorderColor), 1f);
			e.Graphics.DrawLine(divider, 0, 0, Math.Max(0, footer.ClientSize.Width - 1), 0);
		};
		TableLayoutPanel footerLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = new Padding(0, 8, 0, 4),
			BackColor = ShellBackground
		};
		footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, decisionButtonLogicalHeight));

		CheckBox applyToRemaining = new CheckBox
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(2, 0, 0, 2),
			Text = "Use this choice for remaining conflicts in this transfer",
			ForeColor = PrimaryTextColor,
			BackColor = ShellBackground,
			Font = dialogBodyFont,
			AutoSize = false,
			Checked = false,
			TextAlign = ContentAlignment.MiddleLeft
		};

		TableLayoutPanel actions = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 5,
			RowCount = 1,
			Margin = Padding.Empty,
			Height = decisionButtonLogicalHeight,
			BackColor = ShellBackground
		};
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
		actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
		actions.RowStyles.Add(new RowStyle(SizeType.Absolute, decisionButtonLogicalHeight));

		TerminalButton replaceButton = new TerminalButton { DialogResult = DialogResult.Yes };
		TerminalButton cancelButton = new TerminalButton { DialogResult = DialogResult.Cancel };
		TerminalButton skipButton = new TerminalButton { DialogResult = DialogResult.Ignore };
		TerminalButton keepBothButton = new TerminalButton { DialogResult = DialogResult.No };
		ConfigureActionButton(replaceButton, "Replace");
		ConfigureActionButton(skipButton, "Skip");
		ConfigureActionButton(cancelButton, "Cancel");
		ConfigureActionButton(keepBothButton, "Keep Both");
		replaceButton.Font = dialogButtonFont;
		skipButton.Font = dialogButtonFont;
		cancelButton.Font = dialogButtonFont;
		keepBothButton.Font = dialogButtonFont;
		replaceButton.EnabledBackColor = InterpolateColor(TerminalBackground, FailureStateColor, 0.08f);
		replaceButton.HoverBackColor = InterpolateColor(TerminalBackground, FailureStateColor, 0.18f);
		replaceButton.PressedBackColor = InterpolateColor(TerminalBackground, FailureStateColor, 0.26f);
		replaceButton.EnabledTextColor = FailureStateColor;
		replaceButton.BorderColor = InterpolateColor(BorderColor, FailureStateColor, 0.66f);
		replaceButton.FocusBorderColor = FailureStateColor;
		keepBothButton.EnabledBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.30f);
		keepBothButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.40f);
		keepBothButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.23f);
		keepBothButton.EnabledTextColor = InterpolateColor(PrimaryTextColor, Color.White, 0.42f);
		keepBothButton.BorderColor = AccentGreen;
		keepBothButton.FocusBorderColor = InterpolateColor(AccentGreen, Color.White, 0.30f);
		cancelButton.EnabledTextColor = AccentDim;
		replaceButton.AccessibleDescription = warningText;
		keepBothButton.AccessibleDescription = "Save the incoming item with a unique copy name.";
		skipButton.AccessibleDescription = "Leave the existing destination unchanged and skip this item.";
		cancelButton.AccessibleDescription = "Stop the entire transfer without replacing this item.";
		replaceButton.Margin = new Padding(0, 1, 16, 1);
		cancelButton.Margin = new Padding(8, 1, 4, 1);
		skipButton.Margin = new Padding(4, 1, 4, 1);
		keepBothButton.Margin = new Padding(4, 1, 4, 1);
		actions.Controls.Add(replaceButton, 1, 0);
		actions.Controls.Add(cancelButton, 2, 0);
		actions.Controls.Add(skipButton, 3, 0);
		actions.Controls.Add(keepBothButton, 4, 0);
		footerLayout.Controls.Add(applyToRemaining, 0, 0);
		footerLayout.Controls.Add(actions, 0, 1);
		footer.Controls.Add(footerLayout);

		CancellationTokenSource? contentComparisonCancellation = null;
		bool contentComparisonRunning = false;
		bool contentComparisonCompleted = false;
		void SetDecisionButtonsEnabled(bool enabled)
		{
			replaceButton.Enabled = enabled;
			skipButton.Enabled = enabled;
			keepBothButton.Enabled = enabled;
		}
		compareContentsButton.Click += async delegate
		{
			if (contentComparisonRunning)
			{
				contentComparisonCancellation?.Cancel();
				return;
			}
			if (prompt.CompareHashesAsync == null)
			{
				return;
			}

			contentComparisonRunning = true;
			contentComparisonCompleted = false;
			contentComparisonCancellation = new CancellationTokenSource();
			SetDecisionButtonsEnabled(enabled: false);
			compareContentsButton.Text = "Cancel Check";
			contentCheckTitle.Text = "CONTENT CHECK";
			contentCheckTitle.ForeColor = AccentDim;
			contentCheckStatus.Font = dialogBodyFont;
			contentCheckStatus.Text = "Reading both files once and calculating SHA-256...";
			contentCheckStatus.ForeColor = LoadingStateColor;
			try
			{
				TransferHashComparison hashComparison = await prompt.CompareHashesAsync(contentComparisonCancellation.Token);
				if (form.IsDisposed)
				{
					return;
				}
				contentComparisonCompleted = true;
				string sourceHash = FormatTransferHashForDisplay(hashComparison.SourceSha256);
				string destinationHash = FormatTransferHashForDisplay(hashComparison.DestinationSha256);
				contentCheckStatus.Font = dialogPathFont;
				if (hashComparison.Matches)
				{
					contentCheckTitle.Text = "SHA-256 MATCH";
					contentCheckTitle.ForeColor = AccentGreen;
					contentCheckStatus.Text = "INCOMING  " + sourceHash + Environment.NewLine + "EXISTING  " + destinationHash;
					contentCheckStatus.ForeColor = AccentGreen;
				}
				else
				{
					contentCheckTitle.Text = "SHA-256 DIFFERENT";
					contentCheckTitle.ForeColor = WarningColor;
					contentCheckStatus.Text = "INCOMING  " + sourceHash + Environment.NewLine + "EXISTING  " + destinationHash;
					contentCheckStatus.ForeColor = WarningColor;
				}
				conflictToolTip.SetToolTip(
					contentCheckStatus,
					"Incoming SHA-256: " + hashComparison.SourceSha256 + Environment.NewLine
					+ "Existing SHA-256: " + hashComparison.DestinationSha256);
			}
			catch (OperationCanceledException)
			{
				if (!form.IsDisposed)
				{
					contentCheckTitle.Text = "CONTENT CHECK";
					contentCheckTitle.ForeColor = AccentDim;
					contentCheckStatus.Font = dialogBodyFont;
					contentCheckStatus.Text = "Content comparison cancelled. No files were changed.";
					contentCheckStatus.ForeColor = AccentDim;
				}
			}
			catch (Exception ex)
			{
				if (!form.IsDisposed)
				{
					contentCheckTitle.Text = "CONTENT CHECK FAILED";
					contentCheckTitle.ForeColor = FailureStateColor;
					contentCheckStatus.Font = dialogBodyFont;
					contentCheckStatus.Text = "Content comparison failed. No files were changed.";
					contentCheckStatus.ForeColor = FailureStateColor;
					conflictToolTip.SetToolTip(contentCheckStatus, ex.GetBaseException().Message);
				}
			}
			finally
			{
				contentComparisonRunning = false;
				contentComparisonCancellation?.Dispose();
				contentComparisonCancellation = null;
				if (!form.IsDisposed)
				{
					SetDecisionButtonsEnabled(enabled: true);
					compareContentsButton.Text = contentComparisonCompleted ? "Check Again" : "Compare Contents";
					keepBothButton.Focus();
				}
			}
		};
		form.FormClosing += delegate
		{
			contentComparisonCancellation?.Cancel();
		};

		detailLayout.Controls.Add(comparison, 0, 0);
		detailLayout.Controls.Add(comparisonTools, 0, 1);
		detailLayout.Controls.Add(keepBoth, 0, 2);
		detailViewport.Controls.Add(detailLayout);
		root.Controls.Add(header, 0, 0);
		root.Controls.Add(detailViewport, 0, 1);
		root.Controls.Add(footer, 0, 2);
		form.Controls.Add(root);
		form.AcceptButton = keepBothButton;
		form.CancelButton = cancelButton;

		int ScaleDialogValue(int logicalValue)
		{
			return Math.Max(1, (int)Math.Round(logicalValue * form.DeviceDpi / 96f));
		}

		bool? detailsAreStacked = null;
		int detailsLayoutDpi = 0;
		void UpdateResponsiveConflictLayout()
		{
			if (form.IsDisposed || detailViewport.IsDisposed)
			{
				return;
			}
			bool stackDetails = detailViewport.ClientSize.Width < ScaleDialogValue(760);
			if (detailsAreStacked == stackDetails && detailsLayoutDpi == form.DeviceDpi)
			{
				return;
			}
			detailsAreStacked = stackDetails;
			detailsLayoutDpi = form.DeviceDpi;
			detailLayout.SuspendLayout();
			comparison.SuspendLayout();
			comparisonToolsLayout.SuspendLayout();
			try
			{
				comparison.RowStyles.Clear();
				comparison.RowCount = stackDetails ? 2 : 1;
				if (stackDetails)
				{
					comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
					comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
					comparison.SetCellPosition(destination, new TableLayoutPanelCellPosition(0, 1));
					comparison.SetColumnSpan(destination, 2);
					comparison.SetCellPosition(source, new TableLayoutPanelCellPosition(0, 0));
					comparison.SetColumnSpan(source, 2);
					source.Margin = new Padding(0, 0, 0, 6);
					destination.Margin = new Padding(0, 6, 0, 8);
				}
				else
				{
					comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
					comparison.SetColumnSpan(source, 1);
					comparison.SetCellPosition(source, new TableLayoutPanelCellPosition(0, 0));
					comparison.SetColumnSpan(destination, 1);
					comparison.SetCellPosition(destination, new TableLayoutPanelCellPosition(1, 0));
					source.Margin = new Padding(0, 0, 6, 8);
					destination.Margin = new Padding(6, 0, 0, 8);
				}

				comparisonToolsLayout.RowStyles.Clear();
				comparisonToolsLayout.RowCount = stackDetails ? 3 : 2;
				comparisonToolsLayout.SetCellPosition(quickComparisonTitle, new TableLayoutPanelCellPosition(0, 0));
				comparisonToolsLayout.SetColumnSpan(quickComparisonTitle, 1);
				comparisonToolsLayout.SetCellPosition(quickComparisonValue, new TableLayoutPanelCellPosition(1, 0));
				comparisonToolsLayout.SetColumnSpan(quickComparisonValue, 2);
				if (stackDetails)
				{
					comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleDialogValue(24)));
					comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleDialogValue(38)));
					comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
					comparisonToolsLayout.SetCellPosition(contentCheckStatus, new TableLayoutPanelCellPosition(0, 2));
					comparisonToolsLayout.SetColumnSpan(contentCheckStatus, 3);
					comparisonToolsLayout.SetCellPosition(contentCheckTitle, new TableLayoutPanelCellPosition(0, 1));
					comparisonToolsLayout.SetColumnSpan(contentCheckTitle, 2);
					comparisonToolsLayout.SetCellPosition(compareContentsButton, new TableLayoutPanelCellPosition(2, 1));
					contentCheckStatus.TextAlign = ContentAlignment.TopLeft;
				}
				else
				{
					comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleDialogValue(24)));
					comparisonToolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
					comparisonToolsLayout.SetColumnSpan(contentCheckStatus, 1);
					comparisonToolsLayout.SetCellPosition(contentCheckStatus, new TableLayoutPanelCellPosition(1, 1));
					comparisonToolsLayout.SetCellPosition(contentCheckTitle, new TableLayoutPanelCellPosition(0, 1));
					comparisonToolsLayout.SetColumnSpan(contentCheckTitle, 1);
					comparisonToolsLayout.SetCellPosition(compareContentsButton, new TableLayoutPanelCellPosition(2, 1));
					contentCheckStatus.TextAlign = ContentAlignment.MiddleLeft;
				}

				int comparisonHeight = ScaleDialogValue(stackDetails ? 336 : 172);
				int toolsHeight = ScaleDialogValue(stackDetails ? 142 : 88);
				int keepBothHeight = ScaleDialogValue(84);
				detailLayout.RowStyles[0].Height = comparisonHeight;
				detailLayout.RowStyles[1].Height = toolsHeight;
				detailLayout.RowStyles[2].Height = keepBothHeight;
				detailLayout.Height = comparisonHeight + toolsHeight + keepBothHeight;
			}
			finally
			{
				comparisonToolsLayout.ResumeLayout(performLayout: true);
				comparison.ResumeLayout(performLayout: true);
				detailLayout.ResumeLayout(performLayout: true);
			}
		}

		void ClampConflictDialogToWorkingArea()
		{
			Rectangle workingArea = Screen.FromControl(this).WorkingArea;
			int inset = ScaleDialogValue(8);
			Rectangle usableArea = Rectangle.Inflate(workingArea, -inset, -inset);
			if (usableArea.Width <= 0 || usableArea.Height <= 0)
			{
				usableArea = workingArea;
			}
			Size minimumSize = new Size(
				Math.Min(ScaleDialogValue(680), usableArea.Width),
				Math.Min(ScaleDialogValue(460), usableArea.Height));
			form.MinimumSize = minimumSize;
			form.MaximumSize = usableArea.Size;
			form.Bounds = ClampBoundsToWorkingArea(form.Bounds, usableArea, minimumSize);
		}

		detailViewport.ClientSizeChanged += delegate
		{
			UpdateResponsiveConflictLayout();
		};
		form.Load += delegate
		{
			ClampConflictDialogToWorkingArea();
			UpdateResponsiveConflictLayout();
		};
		form.DpiChanged += delegate
		{
			ClampConflictDialogToWorkingArea();
			UpdateResponsiveConflictLayout();
		};
		form.Shown += delegate
		{
			ClampConflictDialogToWorkingArea();
			UpdateResponsiveConflictLayout();
			form.BringToFront();
			form.Activate();
			ApplyNativeWindowTheme(form);
			keepBothButton.Focus();
		};

		DialogResult dialogResult = form.ShowDialog(this);
		TransferConflictDecision decision = dialogResult switch
		{
			DialogResult.Yes => TransferConflictDecision.Replace,
			DialogResult.No => TransferConflictDecision.KeepBoth,
			DialogResult.Ignore => TransferConflictDecision.Skip,
			_ => TransferConflictDecision.Cancel
		};
		return new TransferConflictResult(decision, applyToRemaining.Checked);
	}

	private Panel CreateTransferConflictDetailPanel(
		TransferConflictDetailSpec detail,
		TransferConflictDialogResources resources)
	{
		Color background = detail.Emphasized
			? InterpolateColor(CardBackground, detail.AccentColor, 0.07f)
			: CardBackground;
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = new Padding(14, detail.IncludeMetadata ? 10 : 8, 12, detail.IncludeMetadata ? 10 : 8),
			BackColor = background
		};
		panel.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle bounds = panel.ClientRectangle;
			if (bounds.Width <= 1 || bounds.Height <= 1)
			{
				return;
			}
			Color borderColor = InterpolateColor(CardBackground, detail.AccentColor, detail.Emphasized ? 0.52f : 0.30f);
			using Pen border = new Pen(borderColor, 1f);
			using Pen rail = new Pen(detail.AccentColor, detail.Emphasized ? 3f : 2f);
			e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
			e.Graphics.DrawLine(rail, 1, 1, 1, bounds.Height - 2);
		};

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = detail.IncludeMetadata ? 4 : 3,
			Margin = Padding.Empty,
			BackColor = background
		};
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25f));
		layout.RowStyles.Add(detail.IncludeMetadata
			? new RowStyle(SizeType.Absolute, 36f)
			: new RowStyle(SizeType.Percent, 100f));
		if (detail.IncludeMetadata)
		{
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		}
		Label captionLabel = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = detail.Caption,
			ForeColor = detail.AccentColor,
			Font = resources.CaptionFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			UseMnemonic = false
		};
		string displayName = GetTransferConflictDisplayName(detail.Path);
		Label nameLabel = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = displayName,
			ForeColor = PrimaryTextColor,
			Font = resources.NameFont,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true,
			UseMnemonic = false
		};
		Label pathLabel = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Text = detail.Path,
			ForeColor = InterpolateColor(AccentDim, PrimaryTextColor, 0.18f),
			Font = resources.PathFont,
			TextAlign = ContentAlignment.TopLeft,
			AutoEllipsis = false,
			AutoSize = false,
			UseCompatibleTextRendering = true,
			UseMnemonic = false
		};
		resources.ToolTip.SetToolTip(nameLabel, detail.Path);
		resources.ToolTip.SetToolTip(pathLabel, detail.Path);
		layout.Controls.Add(captionLabel, 0, 0);
		layout.Controls.Add(nameLabel, 0, 1);
		layout.Controls.Add(pathLabel, 0, 2);
		if (detail.IncludeMetadata)
		{
			TableLayoutPanel metadataLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 2,
				Margin = Padding.Empty,
				BackColor = background
			};
			metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
			metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
			metadataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
			metadataLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			Label sizeCaption = new Label
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Text = "SIZE",
				ForeColor = AccentDim,
				Font = resources.MetadataCaptionFont,
				TextAlign = ContentAlignment.BottomLeft
			};
			Label modifiedCaption = new Label
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Text = "LAST MODIFIED",
				ForeColor = AccentDim,
				Font = resources.MetadataCaptionFont,
				TextAlign = ContentAlignment.BottomLeft
			};
			Label sizeValue = new Label
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Text = FormatTransferConflictSize(detail.Metadata, detail.Kind),
				ForeColor = PrimaryTextColor,
				Font = resources.MetadataValueFont,
				TextAlign = ContentAlignment.TopLeft,
				AutoEllipsis = true,
				UseMnemonic = false
			};
			Label modifiedValue = new Label
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Text = FormatTransferConflictModified(detail.Metadata),
				ForeColor = PrimaryTextColor,
				Font = resources.MetadataValueFont,
				TextAlign = ContentAlignment.TopLeft,
				AutoEllipsis = true,
				UseMnemonic = false
			};
			resources.ToolTip.SetToolTip(sizeValue, FormatTransferConflictSizeDetail(detail.Metadata, detail.Kind));
			resources.ToolTip.SetToolTip(modifiedValue, FormatTransferConflictModifiedDetail(detail.Metadata));
			metadataLayout.Controls.Add(sizeCaption, 0, 0);
			metadataLayout.Controls.Add(modifiedCaption, 1, 0);
			metadataLayout.Controls.Add(sizeValue, 0, 1);
			metadataLayout.Controls.Add(modifiedValue, 1, 1);
			layout.Controls.Add(metadataLayout, 0, 3);
		}
		panel.Controls.Add(layout);
		return panel;
	}

	private static string FormatTransferConflictSize(TransferItemMetadata metadata, TransferEntryKind kind)
	{
		if (kind == TransferEntryKind.Directory)
		{
			return "Not calculated";
		}
		return metadata.SizeBytes is >= 0
			? FtpHelpers.FormatBytes(metadata.SizeBytes.Value)
			: "Not reported";
	}

	private static string FormatTransferConflictSizeDetail(TransferItemMetadata metadata, TransferEntryKind kind)
	{
		if (kind == TransferEntryKind.Directory)
		{
			return "Folder size is not calculated automatically to avoid a recursive FTP scan.";
		}
		return metadata.SizeBytes is >= 0
			? metadata.SizeBytes.Value.ToString("N0", CultureInfo.CurrentCulture) + " bytes"
			: "The file size was not reported by the source.";
	}

	private static string FormatTransferConflictModified(TransferItemMetadata metadata)
	{
		DateTime? modifiedUtc = NormalizeTransferModifiedUtc(metadata.ModifiedUtc);
		return modifiedUtc.HasValue
			? modifiedUtc.Value.ToLocalTime().ToString("MMM d, yyyy  h:mm tt", CultureInfo.CurrentCulture)
			: "Not reported";
	}

	private static string FormatTransferConflictModifiedDetail(TransferItemMetadata metadata)
	{
		DateTime? modifiedUtc = NormalizeTransferModifiedUtc(metadata.ModifiedUtc);
		return modifiedUtc.HasValue
			? "UTC: " + modifiedUtc.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
			: "The last modified time was not reported by the source.";
	}

	private static string BuildTransferConflictComparisonText(TransferConflictPrompt prompt)
	{
		List<string> details = new List<string>();
		if (prompt.SourceKind == TransferEntryKind.File
			&& prompt.DestinationKind == TransferEntryKind.File
			&& prompt.SourceMetadata.SizeBytes is >= 0
			&& prompt.DestinationMetadata.SizeBytes is >= 0)
		{
			long difference = prompt.SourceMetadata.SizeBytes.Value - prompt.DestinationMetadata.SizeBytes.Value;
			if (difference == 0)
			{
				details.Add("Reported sizes match.");
			}
			else
			{
				details.Add("Incoming is " + FtpHelpers.FormatBytes(Math.Abs(difference)) + (difference > 0 ? " larger." : " smaller."));
			}
		}

		DateTime? sourceModifiedUtc = NormalizeTransferModifiedUtc(prompt.SourceMetadata.ModifiedUtc);
		DateTime? destinationModifiedUtc = NormalizeTransferModifiedUtc(prompt.DestinationMetadata.ModifiedUtc);
		if (sourceModifiedUtc.HasValue && destinationModifiedUtc.HasValue)
		{
			TimeSpan difference = sourceModifiedUtc.Value - destinationModifiedUtc.Value;
			if (difference.Duration() < TimeSpan.FromMinutes(1))
			{
				details.Add("Modified times match.");
			}
			else
			{
				details.Add("Incoming is " + FormatTransferConflictAgeDifference(difference.Duration()) + (difference > TimeSpan.Zero ? " newer." : " older."));
			}
		}

		return details.Count > 0
			? string.Join(" ", details)
			: "Some details were not reported. Review the paths before replacing.";
	}

	private static string FormatTransferConflictAgeDifference(TimeSpan difference)
	{
		if (difference.TotalDays >= 1)
		{
			int days = Math.Max(1, (int)Math.Round(difference.TotalDays));
			return days.ToString(CultureInfo.InvariantCulture) + (days == 1 ? " day" : " days");
		}
		if (difference.TotalHours >= 1)
		{
			int hours = Math.Max(1, (int)Math.Round(difference.TotalHours));
			return hours.ToString(CultureInfo.InvariantCulture) + (hours == 1 ? " hour" : " hours");
		}
		int minutes = Math.Max(1, (int)Math.Round(difference.TotalMinutes));
		return minutes.ToString(CultureInfo.InvariantCulture) + (minutes == 1 ? " minute" : " minutes");
	}

	private static Color GetTransferConflictComparisonColor(TransferConflictPrompt prompt)
	{
		DateTime? sourceModifiedUtc = NormalizeTransferModifiedUtc(prompt.SourceMetadata.ModifiedUtc);
		DateTime? destinationModifiedUtc = NormalizeTransferModifiedUtc(prompt.DestinationMetadata.ModifiedUtc);
		if (sourceModifiedUtc.HasValue
			&& destinationModifiedUtc.HasValue
			&& destinationModifiedUtc.Value - sourceModifiedUtc.Value >= TimeSpan.FromMinutes(1))
		{
			return WarningColor;
		}
		return HasTransferMetadata(prompt.SourceMetadata) && HasTransferMetadata(prompt.DestinationMetadata)
			? PrimaryTextColor
			: AccentDim;
	}

	private static string FormatTransferHashForDisplay(string hash)
	{
		return hash.Trim().ToUpperInvariant();
	}

	private static string GetTransferConflictDisplayName(string path)
	{
		string trimmedPath = path.TrimEnd('/', '\\');
		if (trimmedPath.Length == 0)
		{
			return path;
		}
		string fileName = Path.GetFileName(trimmedPath);
		return string.IsNullOrWhiteSpace(fileName) ? trimmedPath : fileName;
	}

	private static bool HasTransferMetadata(TransferItemMetadata metadata)
	{
		return metadata.SizeBytes.HasValue || metadata.ModifiedUtc.HasValue;
	}

	private static TransferItemMetadata GetLocalTransferMetadata(string path, TransferEntryKind kind)
	{
		try
		{
			if (kind == TransferEntryKind.File && File.Exists(path))
			{
				FileInfo file = new FileInfo(path);
				return new TransferItemMetadata(file.Length, NormalizeTransferModifiedUtc(file.LastWriteTimeUtc));
			}
			if (kind == TransferEntryKind.Directory && Directory.Exists(path))
			{
				DirectoryInfo directory = new DirectoryInfo(path);
				return new TransferItemMetadata(null, NormalizeTransferModifiedUtc(directory.LastWriteTimeUtc));
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
		}
		return default;
	}

	private static async Task<TransferItemMetadata> GetRemoteTransferMetadataAsync(
		AsyncFtpClient client,
		string path,
		TransferEntryKind kind,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		FtpListItem? item = null;
		try
		{
			item = await client.GetObjectInfo(FtpHelpers.NormalizePath(path));
		}
		catch (Exception ex) when (ex is FtpException or IOException or TimeoutException)
		{
		}
		cancellationToken.ThrowIfCancellationRequested();
		item ??= await FtpHelpers.TryGetEntryFromParentListingAsync(client, path);
		if (item == null)
		{
			return default;
		}

		long? size = kind == TransferEntryKind.File && item.Size >= 0 ? item.Size : null;
		return new TransferItemMetadata(size, NormalizeTransferModifiedUtc(item.Modified));
	}

	private static DateTime? NormalizeTransferModifiedUtc(DateTime? value)
	{
		if (!value.HasValue || value.Value == DateTime.MinValue || value.Value == DateTime.MaxValue)
		{
			return null;
		}
		return value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
	}

	private static async Task<string> ComputeLocalTransferSha256Async(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			128 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		using SHA256 sha256 = SHA256.Create();
		return Convert.ToHexString(await sha256.ComputeHashAsync(stream, cancellationToken));
	}

	private async Task<string> ComputeRemoteTransferSha256Async(
		AsyncFtpClient client,
		string path,
		CancellationToken cancellationToken)
	{
		await using Stream stream = await client.OpenRead(FtpHelpers.NormalizePath(path), token: cancellationToken);
		using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = new byte[128 * 1024];
		while (true)
		{
			int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
			if (bytesRead == 0)
			{
				break;
			}
			sha256.AppendData(buffer, 0, bytesRead);
			RecordFtpTrafficBytes(FtpTrafficDirection.Receive, bytesRead);
		}
		return Convert.ToHexString(sha256.GetHashAndReset());
	}

	private static async Task<TransferHashComparison> CompareTransferHashesAsync(
		Func<CancellationToken, Task<string>> sourceHashFactory,
		Func<CancellationToken, Task<string>> destinationHashFactory,
		CancellationToken cancellationToken)
	{
		string sourceHash = await sourceHashFactory(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		string destinationHash = await destinationHashFactory(cancellationToken);
		return new TransferHashComparison(sourceHash, destinationHash);
	}

	private static TransferEntryKind GetLocalEntryKind(string path)
	{
		if (File.Exists(path))
		{
			return TransferEntryKind.File;
		}
		return Directory.Exists(path) ? TransferEntryKind.Directory : TransferEntryKind.None;
	}

	private async Task<TransferEntryKind> GetRemoteEntryKindAsync(AsyncFtpClient client, string path)
	{
		string normalizedPath = FtpHelpers.NormalizePath(path);
		FtpListItem? listedEntry = await FtpHelpers.TryGetEntryFromParentListingAsync(client, normalizedPath);
		if (listedEntry?.Type == FtpObjectType.File)
		{
			return TransferEntryKind.File;
		}
		if (listedEntry?.Type == FtpObjectType.Directory)
		{
			return TransferEntryKind.Directory;
		}
		if (await FtpHelpers.RemoteFileExistsAsync(client, normalizedPath))
		{
			return TransferEntryKind.File;
		}
		return await FtpHelpers.DirectoryExistsByCwdAsync(client, normalizedPath) ? TransferEntryKind.Directory : TransferEntryKind.None;
	}

	private async Task DeleteRemoteExistingPathAsync(AsyncFtpClient client, string path, TransferEntryKind kind, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		bool expectedDirectory = kind == TransferEntryKind.Directory;
		bool deletedDirectory = await FtpHelpers.DeleteRemotePathAsync(client, path, cancellationToken);
		if (deletedDirectory != expectedDirectory)
		{
			throw new IOException("The remote entry type changed before the approved replacement could be completed.");
		}
	}

	private void AppendTransferConflictSummary(TransferConflictPolicy conflictPolicy)
	{
		if (conflictPolicy.ConflictCount <= 0)
		{
			return;
		}
		string detail = conflictPolicy.ConflictCount.ToString(CultureInfo.InvariantCulture) + " destination conflict(s) reviewed";
		if (conflictPolicy.SkippedCount > 0)
		{
			detail += "; " + conflictPolicy.SkippedCount.ToString(CultureInfo.InvariantCulture) + " skipped without changing the destination";
		}
		AppendSystemLine(detail + ".", conflictPolicy.SkippedCount > 0 ? WarningColor : AccentDim);
	}

	private async Task<List<(string RemotePath, string LocalPath)>> DownloadRemoteEntriesToLocalDirectoryAsync(IEnumerable<FileEntryView> entries, string destinationLocalDirectory, TransferConflictPolicy conflictPolicy, CancellationToken cancellationToken)
	{
		List<(string RemotePath, string LocalPath)> list = new List<(string RemotePath, string LocalPath)>();
		Directory.CreateDirectory(destinationLocalDirectory);
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		foreach (FileEntryView entry in entries.Where((FileEntryView entry) => entry.Name != ".."))
		{
			cancellationToken.ThrowIfCancellationRequested();
			TransferEntryKind sourceKind = entry.IsDirectory ? TransferEntryKind.Directory : TransferEntryKind.File;
			string desiredLocalPath = Path.Combine(destinationLocalDirectory, entry.Name);
			TransferDestinationResolution destination = ResolveLocalTransferDestination(
				"Download from console",
				entry.FullPath,
				desiredLocalPath,
				sourceKind,
				conflictPolicy,
				new TransferItemMetadata(entry.SizeBytes, NormalizeTransferModifiedUtc(entry.ModifiedUtc)),
				entry.IsDirectory
					? null
					: token => CompareTransferHashesAsync(
						innerToken => ComputeRemoteTransferSha256Async(asyncFtpClient, entry.FullPath, innerToken),
						innerToken => ComputeLocalTransferSha256Async(desiredLocalPath, innerToken),
						token));
			if (destination.Skipped)
			{
				continue;
			}
			string localPath = destination.Path!;
			if (entry.IsDirectory)
			{
				await DownloadRemoteDirectoryAsync(asyncFtpClient, entry.FullPath, localPath, cancellationToken);
			}
			else
			{
				await DownloadRemoteFileAsync(asyncFtpClient, entry.FullPath, localPath, destination.OverwriteApproved);
			}
			list.Add((entry.FullPath, localPath));
		}
		return list;
	}

	private async Task UploadLocalPathsToRemoteDirectoryAsync(IEnumerable<string> localPaths, string destinationRemoteDirectory, TransferConflictPolicy conflictPolicy, CancellationToken cancellationToken)
	{
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		foreach (string localPath in localPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(localPath))
			{
				TransferDestinationResolution destination = await ResolveRemoteTransferDestinationAsync(
					asyncFtpClient,
					"Upload to console",
					localPath,
					CombineRemotePath(destinationRemoteDirectory, Path.GetFileName(localPath)),
					TransferEntryKind.File,
					conflictPolicy,
					cancellationToken);
				if (!destination.Skipped)
				{
					await UploadLocalFileAsync(asyncFtpClient, localPath, destination.Path!, destination.OverwriteApproved, cancellationToken);
				}
			}
			else if (Directory.Exists(localPath))
			{
				string fileName = new DirectoryInfo(localPath).Name;
				TransferDestinationResolution destination = await ResolveRemoteTransferDestinationAsync(
					asyncFtpClient,
					"Upload to console",
					localPath,
					CombineRemotePath(destinationRemoteDirectory, fileName),
					TransferEntryKind.Directory,
					conflictPolicy,
					cancellationToken);
				if (!destination.Skipped)
				{
					await UploadLocalDirectoryAsync(asyncFtpClient, localPath, destination.Path!, cancellationToken);
				}
			}
		}
	}

	private async Task<bool> PasteRemoteClipboardToRemoteAsync(PaneClipboardEntry clipboard, string destinationRemoteDirectory, TransferConflictPolicy conflictPolicy, CancellationToken cancellationToken)
	{
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		string remoteLeafName = GetRemoteLeafName(clipboard.FullPath);
		TransferEntryKind sourceKind = clipboard.IsDirectory ? TransferEntryKind.Directory : TransferEntryKind.File;
		TransferDestinationResolution destination = await ResolveRemoteTransferDestinationAsync(
			asyncFtpClient,
			clipboard.Move ? "Move on console" : "Copy on console",
			clipboard.FullPath,
			CombineRemotePath(destinationRemoteDirectory, remoteLeafName),
			sourceKind,
			conflictPolicy,
			cancellationToken);
		if (destination.Skipped)
		{
			return false;
		}
		string destinationRemotePath = destination.Path!;
		if (clipboard.Move)
		{
			if (string.Equals(FtpHelpers.NormalizePath(clipboard.FullPath), destinationRemotePath, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (clipboard.IsDirectory)
			{
				await asyncFtpClient.MoveDirectory(clipboard.FullPath, destinationRemotePath);
				if (await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, clipboard.FullPath) || !await FtpHelpers.DirectoryExistsByCwdAsync(asyncFtpClient, destinationRemotePath))
				{
					throw new IOException("Remote folder move could not be verified.");
				}
			}
			else
			{
				await FtpHelpers.MoveFileVerifiedAsync(asyncFtpClient, clipboard.FullPath, destinationRemotePath);
			}
			return true;
		}
		await CopyRemoteEntryAsync(asyncFtpClient, clipboard.FullPath, destinationRemotePath, clipboard.IsDirectory, destination.OverwriteApproved, cancellationToken);
		return true;
	}

	private async Task DeleteRemotePathAsync(string remotePath, bool isDirectory, CancellationToken cancellationToken)
	{
		(int port, string user, string pass, int timeoutMs) = GetTerminalFtpSettings();
		await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, port, user, pass, timeoutMs);
		await asyncFtpClient.Connect(cancellationToken);
		bool deletedDirectory = await FtpHelpers.DeleteRemotePathAsync(asyncFtpClient, remotePath, cancellationToken);
		if (deletedDirectory != isDirectory)
		{
			throw new IOException("The remote entry type changed before deletion could be completed.");
		}
	}

	private async Task CopyRemoteEntryAsync(AsyncFtpClient client, string sourceRemotePath, string destinationRemotePath, bool isDirectory, bool overwriteApproved, CancellationToken cancellationToken)
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
				await DownloadRemoteFileAsync(client, sourceRemotePath, text2, overwriteApproved: false);
				await UploadLocalFileAsync(client, text2, destinationRemotePath, overwriteApproved, cancellationToken);
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
			if (string.IsNullOrWhiteSpace(item.Name) || item.Name is "." or "..")
			{
				continue;
			}
			string text = CombineRemotePath(remoteDirectory, item.Name);
			string text2 = Path.Combine(localDirectory, item.Name);
			if (item.Type == FtpObjectType.File)
			{
				await DownloadRemoteFileAsync(client, text, text2, overwriteApproved: false);
			}
			else if (item.Type == FtpObjectType.Directory)
			{
				await DownloadRemoteDirectoryAsync(client, text, text2, cancellationToken);
			}
		}
	}

	private async Task DownloadRemoteFileAsync(AsyncFtpClient client, string remoteFilePath, string localFilePath, bool overwriteApproved)
	{
		string? directoryName = Path.GetDirectoryName(localFilePath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = GetRemoteLeafName(remoteFilePath);
		string stagedLocalPath = localFilePath + ".xecli-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".part";
		FtpProgressByteTracker trafficTracker = new FtpProgressByteTracker();
		try
		{
			string normalizedRemotePath = FtpHelpers.NormalizePath(remoteFilePath);
			long? expectedSize = await FtpHelpers.TryGetFileSizeAsync(client, normalizedRemotePath);
			FtpStatus ftpStatus = await client.DownloadFile(stagedLocalPath, normalizedRemotePath, FtpLocalExists.Overwrite, FtpVerify.None, new Progress<FtpProgress>(delegate(FtpProgress progress)
			{
				HandleFtpProgress(progress, text, FtpTrafficDirection.Receive, trafficTracker);
			}));
			if (ftpStatus != FtpStatus.Success)
			{
				throw new IOException("FTP download did not complete for " + remoteFilePath + ".");
			}

			long stagedSize = new FileInfo(stagedLocalPath).Length;
			if (expectedSize.HasValue && expectedSize.Value != stagedSize)
			{
				throw new IOException("FTP download size mismatch for " + remoteFilePath + ".");
			}
			CommitStagedLocalFile(stagedLocalPath, localFilePath, overwriteApproved);
		}
		finally
		{
			TryDeleteLocalTransferFile(stagedLocalPath);
		}
		UpdateActiveTransferProgress(100.0, text);
	}

	private static void CommitStagedLocalFile(string stagingPath, string destinationPath, bool overwriteApproved)
	{
		bool destinationExists = File.Exists(destinationPath);
		if (destinationExists && !overwriteApproved)
		{
			throw new IOException("The destination appeared after conflict checking; no file was overwritten. Retry the transfer and choose how to handle the conflict.");
		}

		string? backupPath = null;
		if (destinationExists)
		{
			backupPath = destinationPath + ".xecli-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".backup";
			File.Move(destinationPath, backupPath);
		}

		try
		{
			File.Move(stagingPath, destinationPath);
		}
		catch (Exception commitError)
		{
			Exception? rollbackError = null;
			if (backupPath != null && File.Exists(backupPath))
			{
				try
				{
					TryDeleteLocalTransferFile(destinationPath);
					File.Move(backupPath, destinationPath);
				}
				catch (Exception ex)
				{
					rollbackError = ex;
				}
			}

			if (rollbackError != null)
			{
				throw new IOException(
					"FTP download commit failed and the original local file could not be restored automatically.",
					new AggregateException(commitError, rollbackError));
			}
			throw new IOException("FTP download commit failed. The existing local destination was restored.", commitError);
		}

		if (backupPath != null)
		{
			TryDeleteLocalTransferFile(backupPath);
		}
	}

	private static void TryDeleteLocalTransferFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
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
			await UploadLocalFileAsync(client, file, CombineRemotePath(remoteDirectory, Path.GetFileName(file)), overwriteApproved: false, cancellationToken);
		}
	}

	private async Task UploadLocalFileAsync(AsyncFtpClient client, string localFilePath, string remoteFilePath, bool overwriteApproved, CancellationToken cancellationToken)
	{
		string remoteFilePath2 = FtpHelpers.NormalizePath(remoteFilePath);
		string remoteParentPath = GetRemoteParentPath(remoteFilePath2);
		await FtpHelpers.EnsureRemoteDirectoryAsync(client, remoteParentPath);
		string text = Path.GetFileName(localFilePath);
		FtpProgressByteTracker trafficTracker = new FtpProgressByteTracker();
		await FtpHelpers.UploadFileAtomicAsync(
			client,
			localFilePath,
			remoteFilePath2,
			overwriteApproved,
			new Progress<FtpProgress>(delegate(FtpProgress progress)
			{
				HandleFtpProgress(progress, text, FtpTrafficDirection.Send, trafficTracker);
			}),
			cancellationToken);
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
		if (await GetRemoteEntryKindAsync(client, text) == TransferEntryKind.None)
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
			if (await GetRemoteEntryKindAsync(client, text4) == TransferEntryKind.None)
			{
				return text4;
			}
		}
		return CombineRemotePath(remoteParentPath, text2 + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + extension);
	}

	private string GetLocalTransferTargetDirectory()
	{
		string? text = TrimOrNull(localCurrentPath);
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
		if (GetLocalEntryKind(desiredLocalPath) == TransferEntryKind.None)
		{
			return desiredLocalPath;
		}
		string directoryName = Path.GetDirectoryName(desiredLocalPath) ?? string.Empty;
		string text = isDirectory ? new DirectoryInfo(desiredLocalPath).Name : Path.GetFileNameWithoutExtension(desiredLocalPath);
		string extension = isDirectory ? string.Empty : Path.GetExtension(desiredLocalPath);
		for (int i = 1; i < 256; i++)
		{
			string path = Path.Combine(directoryName, text + ((i == 1) ? " - Copy" : $" - Copy {i}") + extension);
			if (GetLocalEntryKind(path) == TransferEntryKind.None)
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
			DirectoryInfo? parent = Directory.GetParent(localCurrentPath);
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
		string? text = localCurrentPath;
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
		catch (Exception)
		{
			if (!base.IsDisposed && num == localBrowseVersion)
			{
				localEntries.Clear();
				localFileList.SetMessage("Local browse failed: Operation failed");
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
		string parentPath = Directory.GetParent(path)?.FullName ?? string.Empty;
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
		catch (Exception)
		{
			list.Clear();
			list.Add(new FileEntryView
			{
				Name = "Access denied: Operation failed",
				FullPath = string.Empty,
				IsDirectory = false
			});
		}
		list.Insert(0, new FileEntryView
		{
			Name = "..",
			FullPath = parentPath,
			IsDirectory = true
		});
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
		string text2 = GetRemoteBrowserLoadingMessage();
		remotePathLabel.Text = FormatRemotePathLabel(text) + "  :: " + TranslateTerminalText(text2);
		remoteFileList.SetMessage(TranslateTerminalText(text2));
		try
		{
			(string pathText, List<FileEntryView> entries, List<string> items) valueTuple = await Task.Run(() => LoadRemoteBrowserSnapshotAsync(text, cancellationTokenSource.Token), cancellationTokenSource.Token);
			if (base.IsDisposed || cancellationTokenSource.IsCancellationRequested || num != remoteBrowseVersion)
			{
				return;
			}
			remoteEntries.Clear();
			remoteEntries.AddRange(valueTuple.entries);
			remoteBrowserHasLoaded = true;
			if (latestSnapshot?.Connected == true)
			{
				latestSnapshot.FtpServiceReachable = true;
			}
			remotePathLabel.Text = FormatRemotePathLabel(valueTuple.pathText);
			remotePathTextBox.Text = valueTuple.pathText;
			remoteFileList.SetEntries(valueTuple.entries);
			if (valueTuple.entries.Count == 0)
			{
				remoteFileList.SetMessage(TranslateTerminalText("EMPTY"));
			}
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
						if (dictionary.TryGetValue(entry.Name, out DriveInventoryEntry? value))
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
				remoteBrowserHasLoaded = false;
				if (latestSnapshot?.Connected == true)
				{
					latestSnapshot.FtpServiceReachable = false;
				}
				nextRemoteRefreshAllowedUtc = DateTime.UtcNow.AddSeconds(10.0);
				SetRemoteBrowserUnavailablePlaceholder(text, BuildRemoteBrowseFailureSummary(ex));
			}
		}
	}

	private async Task<(string PathText, List<FileEntryView> Entries, List<string> Items)> LoadRemoteBrowserSnapshotAsync(string remotePath, CancellationToken cancellationToken)
	{
		List<FileEntryView> list = new List<FileEntryView>();
		CliConfig cliConfig = CliConfig.Load();
		(int configuredPort, string user, string pass, int timeoutMs) = GetTerminalFtpSettings(cliConfig);
		FtpEndpointResolution ftpEndpoint = await FtpEndpointHelpers.ResolveBrowserEndpointAsync(currentTargetIp, configuredPort, Math.Clamp(connectionTimeoutMs, 1200, 3000), cancellationToken);
		int num = ftpEndpoint.Port;
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
			if (string.IsNullOrWhiteSpace(item2.Name) || item2.Name is "." or "..")
			{
				continue;
			}
			bool isDirectory = item2.Type != FtpObjectType.File;
			string fullPath = CombineRemotePath(text, item2.Name);
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
		CancellationTokenSource? cancellationTokenSource = Interlocked.Exchange(ref cts, null);
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
		Process? process = activeCommandProcess;
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

	private bool CancelActiveFileTransfer()
	{
		CancellationTokenSource? cancellationTokenSource = fileTransferCts;
		if (cancellationTokenSource == null)
		{
			return false;
		}
		try
		{
			cancellationTokenSource.Cancel();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void UpdateCommandSubmitButtonState()
	{
		if (commandSubmitButton == null || commandInput == null)
		{
			return;
		}
		bool operationActive = commandInFlight || fileTransferInFlight || commandCts != null || activeCommandProcess != null || fileTransferCts != null || connectAttemptInFlight;
		bool hasCommandText = !string.IsNullOrWhiteSpace(commandInput.Text)
			|| ((commandPaletteOpen || commandHistorySearchOpen) && suggestionList != null && suggestionList.SelectedItem is string selectedItem && !string.IsNullOrWhiteSpace(selectedItem));
		commandSubmitButton.Enabled = !operationActive && hasCommandText;
		string submitHint = commandSubmitButton.Enabled
			? "Run command (Enter)."
			: "Type a command, then press Enter.";
		commandSubmitButton.AccessibleDescription = submitHint;
		if (commandInputToolTip != null && !string.Equals(commandInputToolTip.GetToolTip(commandSubmitButton), submitHint, StringComparison.Ordinal))
		{
			commandInputToolTip.SetToolTip(commandSubmitButton, submitHint);
		}
		RefreshFooterOperationalSummary();
	}

	private bool CancelActiveOperation()
	{
		bool cancelled = false;
		if (fileTransferInFlight)
		{
			cancelled = CancelActiveFileTransfer();
		}
		if (commandInFlight || commandCts != null || activeCommandProcess != null)
		{
			CancelActiveCommand();
			cancelled = true;
		}
		if (cancelled)
		{
			AppendSystemLine("Cancellation requested.", WarningColor);
			return true;
		}
		AppendSystemLine("No command or file transfer is running.", AccentDim);
		return false;
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
		CancelAutoReconnect(disarm: true);
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
					int width = Math.Max(0, control.Width);
					int height = Math.Max(0, control.Height);
					if (width < 2 || height < 2)
					{
						return;
					}
					args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					using SolidBrush parentBrush = new SolidBrush(control.Parent?.BackColor ?? ShellBackground);
					args.Graphics.FillRectangle(parentBrush, 0, 0, width, height);
					using SolidBrush fill = new SolidBrush(control.BackColor);
					using Pen pen = new Pen(Color.FromArgb(118, BorderColor), 1f);
					Rectangle rectangle = new Rectangle(0, 0, width - 1, height - 1);
					using GraphicsPath cardPath = CreateRoundedRectanglePath(rectangle, 5);
					args.Graphics.FillPath(fill, cardPath);
					args.Graphics.DrawPath(pen, cardPath);
					using Pen pen2 = new Pen(Color.FromArgb(72, AccentGreen), 1f);
					args.Graphics.DrawLine(pen2, rectangle.Left + 1, rectangle.Top + 1, rectangle.Left + Math.Min(rectangle.Width - 2, 26), rectangle.Top + 1);
					using Pen pen3 = new Pen(Color.FromArgb(32, BorderColor), 1f);
					args.Graphics.DrawLine(pen3, rectangle.Right - 20, rectangle.Top + 1, rectangle.Right - 1, rectangle.Top + 1);
					using Pen pen4 = new Pen(Color.FromArgb(28, BorderColor), 1f);
					args.Graphics.DrawLine(pen4, rectangle.Left + 1, rectangle.Bottom - 1, rectangle.Left + 18, rectangle.Bottom - 1);
				}
			}
			catch
			{
			}
		};
		return panel;
	}

	private static Panel CreateSoftShellPanel(Color background, Padding padding)
	{
		Panel panel = new Panel();
		panel.Dock = DockStyle.Fill;
		panel.BackColor = background;
		panel.Padding = padding;
		panel.Paint += delegate(object? sender, PaintEventArgs args)
		{
			try
			{
				if (sender is not Control control)
				{
					return;
				}
				int width = Math.Max(0, control.Width);
				int height = Math.Max(0, control.Height);
				if (width < 2 || height < 2)
				{
					return;
				}
				args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				Rectangle bounds = new Rectangle(0, 0, width - 1, height - 1);
				using SolidBrush fill = new SolidBrush(control.BackColor);
				using Pen topLine = new Pen(Color.FromArgb(58, AccentGreen), 1f);
				using Pen bottomLine = new Pen(Color.FromArgb(34, BorderColor), 1f);
				using Pen leftRail = new Pen(Color.FromArgb(130, AccentGreen), 2f);
				using GraphicsPath shellPath = CreateRoundedRectanglePath(bounds, 8);
				args.Graphics.FillPath(fill, shellPath);
				args.Graphics.DrawLine(topLine, bounds.Left + 8, bounds.Top, Math.Max(bounds.Left + 8, bounds.Right - 8), bounds.Top);
				args.Graphics.DrawLine(bottomLine, bounds.Left + 8, bounds.Bottom, Math.Max(bounds.Left + 8, bounds.Right - 8), bounds.Bottom);
				args.Graphics.DrawLine(leftRail, bounds.Left + 1, bounds.Top + 8, bounds.Left + 1, Math.Max(bounds.Top + 8, bounds.Bottom - 8));
			}
			catch
			{
			}
		};
		return panel;
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
			Rectangle rectangle = control.ClientRectangle;
			if (rectangle.Width <= 0 || rectangle.Height <= 0)
			{
				return;
			}
			args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using SolidBrush fill = new SolidBrush(control.BackColor);
			args.Graphics.FillRectangle(fill, rectangle);
			Rectangle bounds = new Rectangle(rectangle.Left, rectangle.Top, Math.Max(1, rectangle.Width - 1), Math.Max(1, rectangle.Height - 1));
			using GraphicsPath shellPath = CreateRoundedRectanglePath(bounds, CommandSurfaceRadius);
			using Pen shellBorder = new Pen(Color.FromArgb(44, BorderColor), 1f);
			using Pen accent = new Pen(Color.FromArgb(102, AccentGreen), 2f);
			args.Graphics.DrawPath(shellBorder, shellPath);
			args.Graphics.DrawLine(accent, bounds.Left + 12, bounds.Top + 1, Math.Min(bounds.Right - 12, bounds.Left + 82), bounds.Top + 1);
		};
		return panel;
	}

	private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
	{
		GraphicsPath path = new GraphicsPath();
		int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
		if (diameter <= 2)
		{
			path.AddRectangle(bounds);
			path.CloseFigure();
			return path;
		}
		Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
		path.AddArc(arc, 180, 90);
		arc.X = bounds.Right - diameter;
		path.AddArc(arc, 270, 90);
		arc.Y = bounds.Bottom - diameter;
		path.AddArc(arc, 0, 90);
		arc.X = bounds.Left;
		path.AddArc(arc, 90, 90);
		path.CloseFigure();
		return path;
	}

	private static GraphicsPath CreateHorizontalSegmentPath(Rectangle bounds, int radius, bool roundLeftCorners, bool roundRightCorners)
	{
		if (roundLeftCorners && roundRightCorners)
		{
			return CreateRoundedRectanglePath(bounds, radius);
		}

		GraphicsPath path = new GraphicsPath();
		int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
		if (diameter <= 2 || (!roundLeftCorners && !roundRightCorners))
		{
			path.AddRectangle(bounds);
			path.CloseFigure();
			return path;
		}

		int arcRadius = diameter / 2;
		path.StartFigure();
		if (roundLeftCorners)
		{
			path.AddArc(new Rectangle(bounds.Left, bounds.Top, diameter, diameter), 180, 90);
		}
		else
		{
			path.AddLine(bounds.Left, bounds.Top, bounds.Left, bounds.Top);
		}

		int topStart = roundLeftCorners ? bounds.Left + arcRadius : bounds.Left;
		if (roundRightCorners)
		{
			path.AddLine(topStart, bounds.Top, bounds.Right - arcRadius, bounds.Top);
			path.AddArc(new Rectangle(bounds.Right - diameter, bounds.Top, diameter, diameter), 270, 90);
			path.AddLine(bounds.Right, bounds.Top + arcRadius, bounds.Right, bounds.Bottom - arcRadius);
			path.AddArc(new Rectangle(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter), 0, 90);
		}
		else
		{
			path.AddLine(topStart, bounds.Top, bounds.Right, bounds.Top);
			path.AddLine(bounds.Right, bounds.Top, bounds.Right, bounds.Bottom);
		}

		int bottomStart = roundRightCorners ? bounds.Right - arcRadius : bounds.Right;
		if (roundLeftCorners)
		{
			path.AddLine(bottomStart, bounds.Bottom, bounds.Left + arcRadius, bounds.Bottom);
			path.AddArc(new Rectangle(bounds.Left, bounds.Bottom - diameter, diameter, diameter), 90, 90);
		}
		else
		{
			path.AddLine(bottomStart, bounds.Bottom, bounds.Left, bounds.Bottom);
		}
		path.CloseFigure();
		return path;
	}

	private void ClearConnectionFailureState()
	{
		lastConnectionFailureEnvelope = null;
		lastConnectionFailureText = null;
		ClearStatusToast();
	}

	private void SetConnectionFailureState(CliErrorEnvelope failure)
	{
		lastConnectionFailureEnvelope = failure;
		lastConnectionFailureText = BuildConnectionFailureText(failure);
		ShowStatusToast(TranslateTerminalText("Connection failed."));
	}

	private void ShowStatusToast(string text)
	{
		string text2 = TrimOrNull(text) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text2) || base.IsDisposed || statusToastLabel == null || statusToastTimer == null)
		{
			return;
		}
		if (ShouldMarshalToUiThread())
		{
			BeginInvoke(new Action<string>(ShowStatusToast), text2);
			return;
		}
		if (statusToastLabel.Visible && statusToastTimer.Enabled && string.Equals(statusToastLabel.Text, text2, StringComparison.Ordinal))
		{
			return;
		}
		statusToastLabel.Text = text2;
		statusToastLabel.ForeColor = GetSemanticStatusColor(text2);
		statusToastLabel.Visible = true;
		statusToastTimer.Stop();
		statusToastTimer.Start();
	}

	private void ApplyConnectionStatusIndicator(ConnectionUiState state, Color? colorOverride = null)
	{
		Color color = colorOverride ?? GetConnectionStateColor(state);
		connectionStatusIndicatorState = state;
		connectionStatusIndicatorColor = color;
		connectionStatusLabel.ForeColor = color;
		connectionStatusLabel.BackColor = CardBackground;
		connectionStatusLabel.AccessibleDescription = connectionStatusTextSource;
		connectionStatusLabel.Invalidate();
	}

	private void PaintConnectionStatusIndicator(object? sender, PaintEventArgs e)
	{
		if (connectionStatusLabel.Width <= 1 || connectionStatusLabel.Height <= 1)
			return;

		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		int centerY = connectionStatusLabel.ClientRectangle.Top + (connectionStatusLabel.ClientRectangle.Height / 2);
		Rectangle outerGlow = new Rectangle(7, centerY - 9, 18, 18);
		Rectangle innerGlow = new Rectangle(9, centerY - 7, 14, 14);
		Rectangle outerDot = new Rectangle(11, centerY - 5, 10, 10);
		Rectangle innerDot = new Rectangle(13, centerY - 3, 6, 6);
		using SolidBrush glowOuter = new SolidBrush(Color.FromArgb(24, connectionStatusIndicatorColor));
		using SolidBrush glowInner = new SolidBrush(Color.FromArgb(58, connectionStatusIndicatorColor));
		using Pen dotRing = new Pen(Color.FromArgb(220, connectionStatusIndicatorColor), 1.5f);
		using SolidBrush dot = new SolidBrush(connectionStatusIndicatorColor);
		e.Graphics.FillEllipse(glowOuter, outerGlow);
		e.Graphics.FillEllipse(glowInner, innerGlow);
		e.Graphics.DrawEllipse(dotRing, outerDot);
		if (connectionStatusIndicatorState == ConnectionUiState.Connecting)
		{
			e.Graphics.FillEllipse(dot, innerDot);
		}
		else
		{
			Rectangle filledDot = new Rectangle(12, centerY - 4, 8, 8);
			e.Graphics.FillEllipse(dot, filledDot);
		}
	}

	private void ClearStatusToast()
	{
		if (base.IsDisposed || statusToastLabel == null || statusToastTimer == null)
		{
			return;
		}
		if (ShouldMarshalToUiThread())
		{
			BeginInvoke(new Action(ClearStatusToast));
			return;
		}
		statusToastTimer.Stop();
		statusToastLabel.Text = string.Empty;
		statusToastLabel.Visible = false;
	}

	private bool ShouldMarshalToUiThread()
	{
		if (base.IsDisposed)
		{
			return false;
		}
		try
		{
			return base.IsHandleCreated && InvokeRequired;
		}
		catch (NullReferenceException)
		{
			return false;
		}
	}

	private enum ConnectionUiState
	{
		Disconnected,
		Connecting,
		Connected,
		Failed
	}

	private ConnectionUiState GetConnectionUiState(TelemetrySnapshot? snapshot = null)
	{
		if (connectAttemptInFlight)
		{
			return ConnectionUiState.Connecting;
		}
		if (IsSessionConnected(snapshot))
		{
			return ConnectionUiState.Connected;
		}
		if (connectAttemptFailed)
		{
			return ConnectionUiState.Failed;
		}
		return ConnectionUiState.Disconnected;
	}

	private void UpdateConnectionStatusSurfaces(TelemetrySnapshot? snapshot = null)
	{
		TelemetrySnapshot? telemetrySnapshot = snapshot ?? latestSnapshot;
		ConnectionUiState connectionUiState = GetConnectionUiState(telemetrySnapshot);
		bool connectedTelemetryComplete = connectionUiState == ConnectionUiState.Connected && HasCompleteCoreTelemetry(telemetrySnapshot);
		bool connectedTelemetryAvailable = connectionUiState == ConnectionUiState.Connected && HasConnectedTelemetry(telemetrySnapshot);
		showStatusTargetEndpoint = connectionUiState != ConnectionUiState.Disconnected;
		TelemetrySnapshot? statusSnapshot = telemetrySnapshot;
		if (connectionUiState == ConnectionUiState.Connected && !remoteBrowserHasLoaded && telemetrySnapshot?.FtpServiceReachable != false)
		{
			statusSnapshot = new TelemetrySnapshot
			{
				Connected = true
			};
		}
		CliErrorEnvelope? failure = connectionUiState == ConnectionUiState.Failed ? lastConnectionFailureEnvelope : null;
		string statusText = connectionUiState switch
		{
			ConnectionUiState.Connecting => "Connecting",
			ConnectionUiState.Connected when connectedTelemetryComplete => "LIVE",
			ConnectionUiState.Connected when connectedTelemetryAvailable => "Live limited",
			ConnectionUiState.Connected => "Live loading",
			ConnectionUiState.Failed => "Connection failed",
			_ => "Not connected"
		};
		Color? statusColor = connectionUiState == ConnectionUiState.Connected
			? (connectedTelemetryComplete ? SuccessStateColor : (connectedTelemetryAvailable ? WarningColor : LoadingStateColor))
			: null;
		string text4 = connectionUiState switch
		{
			ConnectionUiState.Connecting => BuildConnectingStatusText(),
			ConnectionUiState.Connected => BuildConnectedStatusText(statusSnapshot ?? new TelemetrySnapshot
			{
				Connected = true
			}),
			ConnectionUiState.Failed => BuildFailedStatusText(failure),
			_ => BuildDisconnectedStatusText()
		};
		SetConnectionStatusText(statusText);
		ApplyConnectionStatusIndicator(connectionUiState, statusColor);
		RefreshFooterOperationalSummary(statusSnapshot);
		SetRightNetworkText(text4);
		UpdateRemotePaneAvailability(telemetrySnapshot);
		UpdateConnectionActionAvailability(connectionUiState);
	}

	private static bool ShouldScheduleAutoReconnect(
		bool wasConnected,
		bool isConnected,
		bool reconnectArmed,
		bool reconnectEnabled,
		bool reconnectInFlight)
	{
		return wasConnected && !isConnected && reconnectArmed && reconnectEnabled && !reconnectInFlight;
	}

	private static bool ShouldClearAutoReconnectActivity(bool cancellationRequested, bool shellDisconnected)
	{
		return cancellationRequested || !shellDisconnected;
	}

	private void CancelAutoReconnect(bool disarm)
	{
		if (disarm)
		{
			autoReconnectArmed = false;
		}
		try
		{
			autoReconnectCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task RunAutoReconnectAsync()
	{
		if (base.IsDisposed || autoReconnectInFlight || !autoReconnectArmed || !shellDisconnected)
		{
			return;
		}

		CliConfig.TryLoad(out CliConfig config);
		if (config.AutoReconnectEnabled == false)
		{
			return;
		}

		int attempts = CliPreferences.GetReconnectAttempts(config);
		int delaySeconds = CliPreferences.GetReconnectDelaySeconds(config);
		CancellationTokenSource reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		autoReconnectCts = reconnectCts;
		autoReconnectInFlight = true;
		try
		{
			for (int attempt = 1; attempt <= attempts; attempt++)
			{
				if (!autoReconnectArmed || !shellDisconnected)
				{
					return;
				}

				string attemptLabel = "RECONNECT " + attempt.ToString(CultureInfo.InvariantCulture) + "/" + attempts.ToString(CultureInfo.InvariantCulture);
				SetFooterOperationStatus(attemptLabel);
				ShowStatusToast("Connection lost. " + attemptLabel + " in " + delaySeconds.ToString(CultureInfo.InvariantCulture) + "s.");
				await Task.Delay(TimeSpan.FromSeconds(delaySeconds), reconnectCts.Token);

				if (!autoReconnectArmed || !shellDisconnected || base.IsDisposed)
				{
					return;
				}

				autoReconnectAttemptExecuting = true;
				try
				{
					connectButton.PerformClick();
				}
				finally
				{
					autoReconnectAttemptExecuting = false;
				}

				while (connectAttemptInFlight)
				{
					await Task.Delay(50, reconnectCts.Token);
				}

				if (!shellDisconnected)
				{
					SetFooterOperationStatus(null!);
					ShowStatusToast(TranslateTerminalText("Connection restored."));
					return;
				}
			}

			SetFooterOperationStatus("RECONNECT FAILED");
			AppendSystemLine("Automatic reconnect stopped after " + attempts.ToString(CultureInfo.InvariantCulture) + " attempts.", WarningColor);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (ReferenceEquals(autoReconnectCts, reconnectCts))
			{
				autoReconnectCts = null;
			}
			autoReconnectInFlight = false;
			if (ShouldClearAutoReconnectActivity(reconnectCts.IsCancellationRequested, shellDisconnected))
			{
				footerOperationStatusOverride = null;
			}
			reconnectCts.Dispose();
			RefreshFooterOperationalSummary();
		}
	}

	private void InitializeInteractiveActions()
	{
		connectButton.Click += async delegate
		{
			if (connectAttemptInFlight || fileTransferInFlight)
			{
				return;
			}
			bool automaticReconnectAttempt = autoReconnectAttemptExecuting;
			if (!automaticReconnectAttempt)
			{
				CancelAutoReconnect(disarm: true);
			}
			bool wasSessionConnected = IsSessionConnected();
			if (!TryApplyManualTargetFromInputs(announceChange: false, announceUnchanged: false))
			{
				return;
			}
			CancelAndDispose(ref connectProbeCts);
			CancelAndDispose(ref fileTransferCts);
			CancelAndDispose(ref remoteBrowseCts);
			ClearConnectionFailureState();
			int sessionVersion = Interlocked.Increment(ref sessionEpoch);
			ftpTrafficMeter.Reset(DateTime.UtcNow);
			ftpTrafficGraph.ClearSamples();
			connectAttemptInFlight = true;
			connectAttemptFailed = false;
			connectAttemptStartedUtc = DateTime.UtcNow;
			ResetInventoryLoadingState();
			latestSnapshot = null;
			remoteEntries.Clear();
			remoteCurrentPath = "/";
			remoteBrowserHasLoaded = false;
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			UpdateConnectionStatusSurfaces();
			SetFtpTrafficUnavailable("CONNECTING", LoadingStateColor);
			SetFooterThermalsLoading();
			drivesList.SetEntries(new DriveInventoryEntry[1]
			{
				new DriveInventoryEntry
				{
					Name = "CONNECTING"
				}
			});
			inventoryList.SetItems(new string[1] { TranslateTerminalText("CONNECTING") });
			SetRightDetailText(BuildConnectingSessionText());
			UpdateDriveInventory(Array.Empty<DriveInventoryEntry>());
			SetRemoteBrowserPlaceholder("/", "CONNECTING");
			UpdateShellScaffold();
			SetCommandInputPlaceholder("Connecting");
			SetConnectButtonConnectingState();
			await Task.Yield();
			(bool success, CliErrorEnvelope? failure) = (false, null);
			try
			{
				connectProbeCts = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
				(success, failure) = await Task.Run(() => TryProbeConnectionAsync(connectProbeCts.Token), formLifetimeCts.Token);
			}
			catch (OperationCanceledException)
			{
				success = false;
			}
			catch (Exception)
			{
				success = false;
				failure ??= ConnectionFailureModel.BuildXbdmError("XBDM connection failed.", FormatCurrentTarget());
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
					SetCommandInputPlaceholder("Enter a command");
					return;
				}
				connectAttemptInFlight = false;
				RestoreConnectButtonIdleState();
				SetCommandInputPlaceholder("Connecting");
				if (sessionVersion != Volatile.Read(ref sessionEpoch))
				{
					return;
				}
				if (success)
				{
					ClearConnectionFailureState();
					shellDisconnected = false;
					autoReconnectArmed = true;
					ftpTrafficMeter.Reset(DateTime.UtcNow);
					ftpTrafficGraph.ClearSamples();
					if (!firstConnectWorkspacePrepared)
					{
						firstConnectWorkspacePrepared = true;
						ClearTerminalWorkspace();
						transferQueueEntries.Clear();
						RefreshTransferQueueDisplay();
					}
					UpdateConnectionStatusSurfaces(new TelemetrySnapshot
					{
						Connected = true
					});
					UpdateRuntimePresence(connected: true, new TelemetrySnapshot
					{
						Connected = true
					});
					SetRightDetailText(TranslateTerminalText("Connected\nLoading live data"));
					SetLeftConsoleText("BOARD      WAITING FOR DATA\nDASH       WAITING FOR DATA\nGAME       WAITING FOR DATA\nXEX        WAITING FOR DATA\nTITLEID    WAITING FOR DATA\nGAMERTAG   WAITING FOR DATA");
					SetConnectedLoadingAuxiliaryState();
					RefreshInventoryListFromSnapshot();
					SetCommandInputPlaceholder("Enter a command");
					if (telemetryEnabled && !telemetryTimer.Enabled)
					{
						telemetryTimer.Start();
					}
					UpdateShellScaffold(new TelemetrySnapshot
					{
						Connected = true
					});
					if (automaticReconnectAttempt)
					{
						AppendSystemLine(TranslateTerminalText("Connection restored."), AccentGreen);
					}
					else if (ShouldAnnounceConnectedTransition(wasSessionConnected))
					{
						AppendSystemLine(TranslateTerminalText("Connected."), AccentGreen);
					}
					_ = HydrateConnectedSessionAsync(sessionVersion);
				}
				else
				{
					CliErrorEnvelope connectionFailure = failure ?? ConnectionFailureModel.BuildXbdmError("XBDM connection failed.", FormatCurrentTarget());
					SetConnectionFailureState(connectionFailure);
					shellDisconnected = true;
					ResetFtpTrafficTracking("FAILED");
					connectAttemptFailed = true;
					ResetInventoryLoadingState();
					latestSnapshot = null;
					remoteBrowserHasLoaded = false;
					UpdateRuntimePresence(connected: false);
					UpdateConnectionStatusSurfaces();
					SetFtpTrafficUnavailable("FAILED", FailureStateColor);
					SetRemoteBrowserPlaceholder("/", "UNAVAILABLE");
					SetCommandInputPlaceholder("Enter a local command");
					SetRightDetailText(lastConnectionFailureText ?? BuildFailedSessionText());
					AppendSystemLine(lastConnectionFailureText ?? BuildFailedSessionText(), FailureStateColor);
					UpdateDriveInventory(Array.Empty<DriveInventoryEntry>());
					RefreshInventoryListFromSnapshot();
					UpdateShellScaffold();
					UpdateLiveHints();
				}
			}
		};
		disconnectButton.Click += delegate
		{
			CancelAutoReconnect(disarm: true);
			CancelAndDispose(ref connectProbeCts);
			CancelAndDispose(ref fileTransferCts);
			CancelAndDispose(ref remoteBrowseCts);
			CancelActiveCommand();
			ClearConnectionFailureState();
			shellDisconnected = true;
			ResetFtpTrafficTracking();
			connectAttemptInFlight = false;
			connectAttemptFailed = false;
			Interlocked.Increment(ref sessionEpoch);
			ResetInventoryLoadingState();
			remoteEntries.Clear();
			remoteCurrentPath = "/";
			remoteBrowserHasLoaded = false;
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			latestSnapshot = null;
			UpdateRuntimePresence(connected: false);
			UpdateConnectionStatusSurfaces();
			UpdateShellScaffold();
			telemetryTimer.Stop();
			RestoreConnectButtonIdleState();
			SetCommandInputPlaceholder("Enter a local command");
			SetRightDetailText(BuildDisconnectedSessionText());
			SetRemoteBrowserPlaceholder("/", "Connect to browse remote files.");
			AppendSystemLine(TranslateTerminalText("Not connected."), WarningColor);
			RefreshInventoryListFromSnapshot();
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
		themeButton.Click += delegate
		{
			CycleTerminalTheme();
		};
		settingsButton.Click += delegate
		{
			ShowSettingsDialog();
		};
		base.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.Control && e.KeyCode == Keys.Oemcomma)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				ShowSettingsDialog();
			}
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
		RefreshThemeButtonText();
	}

	private void ShowSettingsDialog(string initialPage = "General", bool openThemeSelector = false)
	{
		if (base.IsDisposed || base.Disposing)
		{
			return;
		}
		FlushConfiguredTerminalThemeSave();
		CliConfig.TryLoad(out CliConfig config);
		using XeCliSettingsForm form = new XeCliSettingsForm(
			config,
			currentThemeName,
			Icon,
			TopMost,
			PreviewTerminalTheme,
			initialPage,
			openThemeSelector);
		if (form.ShowDialog(this) == DialogResult.OK)
		{
			CliConfig.TryLoad(out CliConfig savedConfig);
			ApplySavedTerminalPreferences(savedConfig);
			ShowStatusToast(TranslateTerminalText("Settings saved."));
		}
	}

	private void ApplySavedTerminalPreferences(CliConfig config)
	{
		connectionTimeoutMs = CliPreferences.GetConnectionTimeoutMs(config);
		telemetryEnabled = config.TerminalTelemetryEnabled != false;
		discordRichPresenceEnabled = config.DiscordRichPresenceEnabled != false;
		if (config.AutoReconnectEnabled == false)
		{
			CancelAutoReconnect(disarm: true);
		}

		if (telemetryEnabled)
		{
			if (!telemetryTimer.Enabled)
				telemetryTimer.Start();
			if (!shellDisconnected)
				_ = PollTelemetrySafeAsync(forceHeavyRefresh: true);
		}
		else
		{
			telemetryTimer.Stop();
		}

		discordRpcService?.Dispose();
		discordRpcService = discordRichPresenceEnabled ? DiscordRpcService.CreateIfConfigured() : null;
		discordRpcService?.Start();

		if (shellDisconnected && !connectAttemptInFlight)
		{
			currentTargetIp = string.IsNullOrWhiteSpace(config.DefaultIp) ? "192.168.1.1" : config.DefaultIp.Trim();
			currentTargetPort = config.DefaultPort ?? 730;
			RefreshTargetEditorText();
			RefreshShellTargetLabelText();
		}

		string configuredLocalDirectory = ResolveConfiguredLocalDirectory(config);
		if (!string.Equals(localCurrentPath, configuredLocalDirectory, StringComparison.OrdinalIgnoreCase))
		{
			localCurrentPath = configuredLocalDirectory;
			_ = RefreshLocalBrowserAsync();
		}

		string languageCode = NormalizeUiLanguageCode(config.UiLanguage);
		LocalizedText.Initialize(languageCode);
		CultureInfo.CurrentUICulture = LocalizedText.Culture;
		CultureInfo.DefaultThreadCurrentUICulture = LocalizedText.Culture;
		RefreshLocalizedUiText();
		RefreshLanguageButtonText();
		RefreshThemeButtonText();
		UpdateConnectionStatusSurfaces(latestSnapshot);
	}

	private static string ResolveConfiguredLocalDirectory()
	{
		CliConfig.TryLoad(out CliConfig config);
		return ResolveConfiguredLocalDirectory(config);
	}

	private static string ResolveConfiguredLocalDirectory(CliConfig config)
	{
		string? configured = TrimOrNull(config.DefaultLocalDirectory);
		if (configured != null)
		{
			try
			{
				string path = Path.GetFullPath(configured);
				if (Directory.Exists(path))
					return path;
			}
			catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
			{
			}
		}
		return CliPreferences.GetDefaultLocalDirectory();
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

	internal static string NormalizeTerminalThemeName(string? value)
	{
		string? text = TrimOrNull(value);
		if (text == null)
			return DefaultTerminalThemeName;

		foreach (TerminalThemePalette theme in TerminalThemes)
		{
			if (string.Equals(theme.Name, text, StringComparison.OrdinalIgnoreCase))
				return theme.Name;
		}

		return DefaultTerminalThemeName;
	}

	private static TerminalThemePalette ResolveTerminalTheme(string? value)
	{
		string normalized = NormalizeTerminalThemeName(value);
		return TerminalThemes.First(theme => string.Equals(theme.Name, normalized, StringComparison.OrdinalIgnoreCase));
	}

	internal static XeCliSettingsTheme GetSettingsTheme(string? value)
	{
		TerminalThemePalette theme = ResolveTerminalTheme(value);
		return new XeCliSettingsTheme(
			theme.Name,
			theme.ShellBackground,
			theme.CardBackground,
			theme.TerminalBackground,
			theme.AccentCyan,
			theme.AccentDim,
			theme.AccentGreen,
			theme.BorderColor,
			theme.WarningColor,
			ComputeFailureStateColor(theme),
			ComputeSuccessStateColor(theme));
	}

	private static Color ComputeFailureStateColor(TerminalThemePalette theme)
	{
		return InterpolateColor(SemanticFailureBaseColor, theme.AccentPink, 0.10f);
	}

	private static Color ComputeSuccessStateColor(TerminalThemePalette theme)
	{
		return InterpolateColor(SemanticSuccessBaseColor, theme.AccentGreen, 0.08f);
	}

	private static string ApplyTerminalTheme(string? value)
	{
		TerminalThemePalette theme = ResolveTerminalTheme(value);
		ShellBackground = theme.ShellBackground;
		CardBackground = theme.CardBackground;
		TerminalBackground = theme.TerminalBackground;
		AccentCyan = theme.AccentCyan;
		AccentPink = theme.AccentPink;
		FailureStateColor = ComputeFailureStateColor(theme);
		SuccessStateColor = ComputeSuccessStateColor(theme);
		AccentGreen = theme.AccentGreen;
		AccentDim = theme.AccentDim;
		BorderColor = theme.BorderColor;
		WarningColor = theme.WarningColor;
		LoadingStateColor = theme.LoadingStateColor;
		EmptyStateColor = theme.EmptyStateColor;
		FooterChipBackground = theme.FooterChipBackground;
		return theme.Name;
	}

	private void RefreshThemeButtonText()
	{
		themeButton.Text = "THEME: " + currentThemeName.ToUpperInvariant();
	}

	private void CycleTerminalTheme()
	{
		TerminalThemePalette previousTheme = ResolveTerminalTheme(currentThemeName);
		int index = Array.FindIndex(TerminalThemes, theme => string.Equals(theme.Name, currentThemeName, StringComparison.OrdinalIgnoreCase));
		TerminalThemePalette nextTheme = TerminalThemes[(Math.Max(0, index) + 1) % TerminalThemes.Length];
		currentThemeName = ApplyTerminalTheme(nextTheme.Name);
		List<Control> redrawLockedControls = SuspendControlTreeRedraw(this);
		SuspendLayout();
		try
		{
			RefreshThemeButtonText();
			RefreshThemeControlTree(this, previousTheme);
			RefreshThemeBoundSurfaces();
			RefreshContextMenuThemes();
			RefreshShellTargetLabelText();
			ApplyCommandSubmitButtonTheme();
			UpdateCommandSubmitButtonState();
			ApplyToggleButtonState(localTabButton, active: true);
			RefreshInventoryHeader();
			SetRemotePaneMode(remotePaneShowsQueue);
			UpdateConnectionStatusSurfaces(latestSnapshot);
			RefreshFooterThermalPresentation();
			RefreshScreenshotNotificationPresentation();
			RefreshScreenshotPreviewTheme();
			ApplyNativeWindowTheme();
		}
		finally
		{
			ResumeLayout(performLayout: false);
			ResumeControlTreeRedraw(redrawLockedControls);
		}
		PerformLayout();
		Invalidate(invalidateChildren: true);
		Update();
		ScheduleConfiguredTerminalThemeSave();
	}

	private void PreviewTerminalTheme(string requestedTheme)
	{
		string normalized = NormalizeTerminalThemeName(requestedTheme);
		if (string.Equals(normalized, currentThemeName, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		TerminalThemePalette previousTheme = ResolveTerminalTheme(currentThemeName);
		currentThemeName = ApplyTerminalTheme(normalized);
		List<Control> redrawLockedControls = SuspendControlTreeRedraw(this);
		SuspendLayout();
		try
		{
			RefreshThemeButtonText();
			RefreshThemeControlTree(this, previousTheme);
			RefreshThemeBoundSurfaces();
			RefreshContextMenuThemes();
			RefreshShellTargetLabelText();
			ApplyCommandSubmitButtonTheme();
			UpdateCommandSubmitButtonState();
			ApplyToggleButtonState(localTabButton, active: true);
			RefreshInventoryHeader();
			SetRemotePaneMode(remotePaneShowsQueue);
			UpdateConnectionStatusSurfaces(latestSnapshot);
			RefreshFooterThermalPresentation();
			RefreshScreenshotNotificationPresentation();
			RefreshScreenshotPreviewTheme();
			ApplyNativeWindowTheme();
		}
		finally
		{
			ResumeLayout(performLayout: false);
			ResumeControlTreeRedraw(redrawLockedControls);
		}
		PerformLayout();
		Invalidate(invalidateChildren: true);
		Update();
	}

	private void RefreshScreenshotPreviewTheme()
	{
		if (screenshotPreviewForm != null && !screenshotPreviewForm.IsDisposed)
		{
			screenshotPreviewForm.ApplyTheme();
		}
	}

	private static List<Control> SuspendControlTreeRedraw(Control root)
	{
		List<Control> controls = new List<Control>();
		CollectCreatedControls(root, controls);
		if (OperatingSystem.IsWindows())
		{
			foreach (Control control in controls)
			{
				SendMessage(control.Handle, WmSetRedraw, 0, 0);
			}
		}
		return controls;
	}

	private static void CollectCreatedControls(Control control, List<Control> controls)
	{
		if (!control.IsDisposed && control.IsHandleCreated)
		{
			controls.Add(control);
		}
		foreach (Control child in control.Controls)
		{
			CollectCreatedControls(child, controls);
		}
	}

	private static void ResumeControlTreeRedraw(IReadOnlyList<Control> controls)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		for (int index = controls.Count - 1; index >= 0; index--)
		{
			Control control = controls[index];
			if (!control.IsDisposed && control.IsHandleCreated)
			{
				SendMessage(control.Handle, WmSetRedraw, 1, 0);
			}
		}
	}

	private void RefreshThemeBoundSurfaces()
	{
		shellScaffoldLabel.ForeColor = Color.FromArgb(218, AccentDim);
		statusToastLabel.BackColor = GetStatusToastSurfaceColor();
		if (!string.IsNullOrWhiteSpace(statusToastLabel.Text))
		{
			statusToastLabel.ForeColor = GetSemanticStatusColor(statusToastLabel.Text);
		}
		SetPanelHeaderTheme(localPathHostPanel);
		SetPanelHeaderTheme(remotePathHostPanel);
		footerThermalLabel.Invalidate();
		foreach (Control control in new Control[] { shellScaffoldLabel, localPathHostPanel, remotePathHostPanel })
		{
			control.Invalidate(invalidateChildren: true);
		}
	}

	private void SetPanelHeaderTheme(Panel panel)
	{
		Label? label = FindFirstChildLabel(panel);
		if (label == null)
		{
			return;
		}
		label.ForeColor = AccentGreen;
		label.Font = shellFontBold;
	}

	private static void SaveConfiguredTerminalTheme(string themeName)
	{
		try
		{
			CliConfig config = CliConfig.Load();
			config.TerminalTheme = NormalizeTerminalThemeName(themeName);
			config.Save();
		}
		catch
		{
			// Theme switching is cosmetic; keep the live terminal usable if config persistence fails.
		}
	}

	private void ScheduleConfiguredTerminalThemeSave()
	{
		themePersistenceTimer.Stop();
		themePersistenceTimer.Start();
	}

	private void FlushConfiguredTerminalThemeSave()
	{
		if (!themePersistenceTimer.Enabled)
		{
			return;
		}
		themePersistenceTimer.Stop();
		SaveConfiguredTerminalTheme(currentThemeName);
	}

	private static void RefreshThemeControlTree(Control control, TerminalThemePalette previousTheme)
	{
		control.BackColor = ReplaceThemeColor(control.BackColor, previousTheme, isBackColor: true);
		control.ForeColor = ReplaceThemeColor(control.ForeColor, previousTheme, isBackColor: false);
			if (control is TerminalButton terminalButton)
			{
				terminalButton.EnabledBackColor = TerminalBackground;
				terminalButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.12f);
				terminalButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.18f);
				terminalButton.BorderColor = BorderColor;
				terminalButton.DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);
				terminalButton.DisabledBorderColor = InterpolateColor(TerminalBackground, BorderColor, 0.42f);
				terminalButton.EnabledTextColor = AccentCyan;
				terminalButton.DisabledTextColor = InterpolateColor(TerminalBackground, AccentDim, 0.52f);
			}
		foreach (Control child in control.Controls)
		{
			RefreshThemeControlTree(child, previousTheme);
		}
	}

	private static Color ReplaceThemeColor(Color color, TerminalThemePalette previousTheme, bool isBackColor)
	{
		int argb = color.ToArgb();
		if (argb == previousTheme.ShellBackground.ToArgb())
			return ShellBackground;
		if (argb == previousTheme.CardBackground.ToArgb())
			return CardBackground;
		if (argb == previousTheme.TerminalBackground.ToArgb())
			return TerminalBackground;
		if (argb == previousTheme.FooterChipBackground.ToArgb())
			return FooterChipBackground;
		if (argb == InterpolateColor(previousTheme.TerminalBackground, previousTheme.AccentGreen, 0.035f).ToArgb())
			return GetTerminalOutputSurfaceColor();
		if (argb == InterpolateColor(previousTheme.TerminalBackground, previousTheme.ShellBackground, 0.20f).ToArgb())
			return GetSuggestionSurfaceColor();
		if (argb == InterpolateColor(previousTheme.CardBackground, previousTheme.ShellBackground, 0.20f).ToArgb())
			return GetStatusToastSurfaceColor();
		if (argb == ComputeCommandInputSurfaceColor(previousTheme.CardBackground, previousTheme.AccentCyan, previousTheme.AccentGreen).ToArgb())
			return GetCommandInputSurfaceColor();
		if (argb == previousTheme.AccentCyan.ToArgb())
			return AccentCyan;
		if (argb == previousTheme.AccentPink.ToArgb())
			return AccentPink;
		if (argb == ComputeFailureStateColor(previousTheme).ToArgb())
			return FailureStateColor;
		if (argb == ComputeSuccessStateColor(previousTheme).ToArgb())
			return SuccessStateColor;
		if (argb == previousTheme.AccentGreen.ToArgb())
			return AccentGreen;
		if (argb == previousTheme.AccentDim.ToArgb())
			return AccentDim;
		if (argb == previousTheme.BorderColor.ToArgb())
			return BorderColor;
		if (argb == previousTheme.WarningColor.ToArgb())
			return WarningColor;
		if (argb == previousTheme.LoadingStateColor.ToArgb())
			return LoadingStateColor;
		if (argb == previousTheme.EmptyStateColor.ToArgb())
			return EmptyStateColor;
		return isBackColor && color.A == 0 ? Color.Transparent : color;
	}


	private static string GetFooterVersionText()
	{
		System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
		string? informationalVersion = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
			.OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
			.FirstOrDefault()?.InformationalVersion;
		string? text = string.IsNullOrWhiteSpace(informationalVersion) ? assembly.GetName().Version?.ToString(3) : informationalVersion;
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
			text = "2.0.0";
		}
		string normalized = text.Trim();
		return "XeCLI v" + normalized;
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
			"RGH STATUS" => "ESTADO RGH",
			"TRAFFIC" => "TRAFICO",
			"FTP TRAFFIC" => "TRAFICO FTP",
			"DETECTED DRIVES" => "UNIDADES DETECTADAS",
			"Plugins" => "Plugins",
			"Modules" => "Modulos",
			"CONNECT" => "CONECTAR",
			"CONNECTING" => "CONECTANDO",
			"LOADING" => "CARGANDO",
			"DISCONNECT" => "DESCONECTAR",
			"SCREENSHOT" => "CAPTURA",
			"SETTINGS" => "AJUSTES",
			"Settings saved." => "Ajustes guardados.",
			"Screenshot saved" => "Captura guardada",
			"Screenshot failed" => "Fallo la captura",
			"PREVIEW" => "VISTA PREVIA",
			"OPEN" => "ABRIR",
			"FOLDER" => "CARPETA",
			"Preview screenshot in XeCLI" => "Vista previa de la captura en XeCLI",
			"Open screenshot with the default app" => "Abrir la captura con la aplicacion predeterminada",
			"Show screenshot in File Explorer" => "Mostrar la captura en el Explorador de archivos",
			"The capture command failed. Review the terminal output." => "El comando de captura fallo. Revisa la salida del terminal.",
			"Capture completed without a usable image." => "La captura termino sin una imagen utilizable.",
			"Capture folder is unavailable." => "La carpeta de capturas no esta disponible.",
			"The saved screenshot is no longer available." => "La captura guardada ya no esta disponible.",
			"Screenshot open failed." => "No se pudo abrir la captura.",
			"Screenshot folder could not be opened." => "No se pudo abrir la carpeta de capturas.",
			"Refresh" => "Actualizar",
			"LOCAL PATH" => "RUTA LOCAL",
			"REMOTE PATH" => "RUTA REMOTA",
			"QUEUE" => "COLA",
			"NAME" => "NOMBRE",
			"MODIFIED" => "MODIFICADO",
			"SIZE" => "TAMANO",
			"IDLE" => "EN ESPERA",
			"READY" => "LISTO",
			"NOT READY" => "NO LISTO",
			"COMMAND RUNNING" => "COMANDO EN CURSO",
			"COMMAND COMPLETE" => "COMANDO COMPLETADO",
			"COMMAND FAILED" => "COMANDO FALLIDO",
			"COMMAND CANCELLED" => "COMANDO CANCELADO",
			"COMMAND INTERRUPTED" => "COMANDO INTERRUMPIDO",
			"TRANSFER ACTIVE" => "TRANSFERENCIA ACTIVA",
			"SCREENSHOT SAVED" => "CAPTURA GUARDADA",
			"SCREENSHOT FAILED" => "CAPTURA FALLIDA",
			"OFFLINE" => "SIN CONEXION",
			"OFFLINE SESSION" => "SESION SIN CONEXION",
			"LIVE / CONNECTED" => "EN VIVO / CONECTADO",
			"LIVE / LOADING" => "EN VIVO / CARGANDO",
			"LIVE / LIMITED" => "EN VIVO / LIMITADO",
			"LIVE SESSION" => "SESION EN VIVO",
			"THERMALS LOADING" => "TERMICAS CARGANDO",
			"THERMALS UNAVAILABLE" => "TERMICAS NO DISPONIBLES",
			"CONNECTION FAILED" => "CONEXION FALLIDA",
			"TARGET  " => "DESTINO  ",
			"Host or IP (optional port)" => "Host o IP (puerto opcional)",
			"Enter a local command" => "Escribe un comando local",
			"Connecting" => "Conectando",
			"Enter a command" => "Escribe un comando",
			"Connected" => "Conectado",
			"Not connected" => "No conectado",
			"Loading live data" => "Cargando datos en vivo",
			"Connecting\nLoading live data" => "Conectando\nCargando datos en vivo",
			"Connected\nLoading live data" => "Conectado\nCargando datos en vivo",
			"Could not open the selected path." => "No se pudo abrir la ruta seleccionada.",
			"Connecting to console\nLoading title, XEX, user,\npresence, and TitleID..." => "Conectando a la consola\nCargando titulo, XEX, usuario,\npresencia e ID de titulo...",
			"Connecting\r\nLoading live data" => "Conectando\r\nCargando datos en vivo",
			_ => text
		};
		if (!string.Equals(text2, text, StringComparison.Ordinal))
		{
			return text2;
		}
		text2 = TranslateTerminalStructuredLines(text2);
		text2 = text2.Replace("No connected console.", "No hay consola conectada.", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to view console status", "Conecta para ver el estado de la consola", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to view status,", "Conecta para ver el estado,", StringComparison.Ordinal);
		text2 = text2.Replace("and drives. Title, XEX, user,", "y unidades. Titulo, XEX, usuario,", StringComparison.Ordinal);
		text2 = text2.Replace("presence, and TitleID appear after.", "presencia e ID de titulo aparecen despues.", StringComparison.Ordinal);
		text2 = text2.Replace("Not connected", "No conectado", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting", "Conectando", StringComparison.Ordinal);
		text2 = text2.Replace("Connected", "Conectado", StringComparison.Ordinal);
		text2 = text2.Replace("Loading live data", "Cargando datos en vivo", StringComparison.Ordinal);
		text2 = text2.Replace("Console disconnected.", "Consola desconectada.", StringComparison.Ordinal);
		text2 = text2.Replace("Console disconnected", "Consola desconectada", StringComparison.Ordinal);
		text2 = text2.Replace("Console connected", "Consola conectada", StringComparison.Ordinal);
		text2 = text2.Replace("Console connection failed", "Conexion de consola fallida", StringComparison.Ordinal);
		text2 = text2.Replace("Connection failed", "Conexion fallida", StringComparison.Ordinal);
		text2 = text2.Replace("Check the console is powered on, not frozen, and reachable, then try again or re-run rgh connect if the target changed.", "Verifica que la consola este encendida, no se haya congelado y sea accesible; luego vuelve a intentarlo o ejecuta rgh connect si el objetivo cambio.", StringComparison.Ordinal);
		text2 = text2.Replace("XBDM connection failed", "Conexion XBDM fallida", StringComparison.Ordinal);
		text2 = text2.Replace("XBDM connection timed out", "La conexion XBDM excedio el tiempo de espera", StringComparison.Ordinal);
		text2 = text2.Replace("XBDM connection did not complete", "La conexion XBDM no se completo", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to view console status,", "Conecta para ver el estado de la consola,", StringComparison.Ordinal);
		text2 = text2.Replace("drives, title, and user.", "unidades, titulo y usuario.", StringComparison.Ordinal);
		text2 = text2.Replace("drives, thermals, FTP 21, title, and user state.", "unidades, termicas, FTP 21, titulo y estado del usuario.", StringComparison.Ordinal);
		text2 = text2.Replace("Local XeCLI commands remain available below.", "Los comandos locales de XeCLI siguen disponibles abajo.", StringComparison.Ordinal);
		text2 = text2.Replace("Handshake in progress.", "Negociacion en curso.", StringComparison.Ordinal);
		text2 = text2.Replace("Live telemetry will hydrate automatically.", "La telemetria en vivo se cargara automaticamente.", StringComparison.Ordinal);
		text2 = text2.Replace("Connection established.", "Conexion establecida.", StringComparison.Ordinal);
		text2 = text2.Replace("Loading title, XEX, user,", "Cargando titulo, XEX, usuario,", StringComparison.Ordinal);
		text2 = text2.Replace("sign-in, and queue...", "inicio de sesion y cola...", StringComparison.Ordinal);
		text2 = text2.Replace("CONNECTING", "CONECTANDO", StringComparison.Ordinal);
		text2 = text2.Replace("WAITING FOR DATA", "ESPERANDO DATOS", StringComparison.Ordinal);
		text2 = text2.Replace("EMPTY", "VACIO", StringComparison.Ordinal);
		text2 = text2.Replace("UNAVAILABLE", "NO DISPONIBLE", StringComparison.Ordinal);
		text2 = text2.Replace("Loading console inventory...", "Cargando inventario de la consola...", StringComparison.Ordinal);
		text2 = text2.Replace("Connection failed. Awaiting console.", "La conexion fallo. Esperando consola.", StringComparison.Ordinal);
		text2 = text2.Replace("Awaiting console", "Esperando consola", StringComparison.Ordinal);
		text2 = text2.Replace("Connection failed.\nWaiting for the console to reconnect\nbefore loading status and drives.", "La conexion fallo.\nEsperando que la consola se reconecte\nantes de cargar estado y unidades.", StringComparison.Ordinal);
		text2 = text2.Replace("Not connected. Connect to load modules.", "No conectado. Conecta para cargar modulos.", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to modules...", "Conectando a modulos...", StringComparison.Ordinal);
		text2 = text2.Replace("Not connected. Connect to load plugins.", "No conectado. Conecta para cargar plugins.", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to plugins...", "Conectando a plugins...", StringComparison.Ordinal);
		text2 = text2.Replace("No live modules detected.", "No se detectaron modulos activos.", StringComparison.Ordinal);
		text2 = text2.Replace("No live plugins detected.", "No se detectaron plugins activos.", StringComparison.Ordinal);
		text2 = text2.Replace("No drives detected", "No se detectaron unidades", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting...", "Conectando...", StringComparison.Ordinal);
		text2 = text2.Replace("Live storage root", "Raiz de almacenamiento", StringComparison.Ordinal);
		text2 = text2.Replace("FTP service unavailable.", "Servicio FTP no disponible.", StringComparison.Ordinal);
		text2 = text2.Replace("FTP browse timed out.", "La exploracion FTP excedio el tiempo de espera.", StringComparison.Ordinal);
		text2 = text2.Replace("FTP is only available when Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin is running; launch one, then retry.", "FTP solo esta disponible cuando Aurora, FreestyleDash, XexMenu o un plugin FTP de DashLaunch esta en ejecucion; inicia uno y vuelve a intentarlo.", StringComparison.Ordinal);
		text2 = text2.Replace("FTP only works when an FTP service is running on Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin; it will not answer from NXE or a game.", "FTP solo funciona cuando un servicio FTP esta en ejecucion en Aurora, FreestyleDash, XexMenu o un plugin FTP de DashLaunch; no responde desde NXE ni desde un juego.", StringComparison.Ordinal);
		text2 = text2.Replace("Launch one, then retry.", "Inicia uno y vuelve a intentarlo.", StringComparison.Ordinal);
		text2 = text2.Replace("QUEUE IDLE", "COLA EN ESPERA", StringComparison.Ordinal);
		text2 = text2.Replace("QUEUE:", "COLA:", StringComparison.Ordinal);
		text2 = text2.Replace("No transfers staged", "No hay transferencias en cola", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to view the console.", "Conecta para ver la consola.", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to load console status, or enter a XeCLI command...", "Conecta para cargar el estado de la consola, o escribe un comando XeCLI...", StringComparison.Ordinal);
		text2 = text2.Replace("Enter XeCLI command for the current console session...", "Escribe un comando XeCLI para la sesion actual de la consola...", StringComparison.Ordinal);
		text2 = text2.Replace("Connect or enter a XeCLI command...", "Conecta o escribe un comando XeCLI...", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to console...", "Conectando a la consola...", StringComparison.Ordinal);
		text2 = text2.Replace("Wait for the current console connection attempt to finish.", "Espera a que termine el intento de conexion de la consola.", StringComparison.Ordinal);
		text2 = text2.Replace("Connect to the console before using FTP actions.", "Conecta a la consola antes de usar acciones FTP.", StringComparison.Ordinal);
		text2 = text2.Replace("Host or IP (optional port)", "Host o IP (puerto opcional)", StringComparison.Ordinal);
		text2 = text2.Replace("Enter a local command", "Escribe un comando local", StringComparison.Ordinal);
		text2 = text2.Replace("Enter a command", "Escribe un comando", StringComparison.Ordinal);
		text2 = text2.Replace("Console ready", "Consola lista", StringComparison.Ordinal);
		text2 = text2.Replace("Could not open the selected path.", "No se pudo abrir la ruta seleccionada.", StringComparison.Ordinal);
		text2 = text2.Replace("TARGET READY", "OBJETIVO LISTO", StringComparison.Ordinal);
		text2 = text2.Replace("Connecting to console", "Conectando a la consola", StringComparison.Ordinal);
		text2 = text2.Replace("Console connected", "Consola conectada", StringComparison.Ordinal);
		text2 = text2.Replace("Console disconnected", "Consola desconectada", StringComparison.Ordinal);
		text2 = text2.Replace("Console connection failed", "Conexion de consola fallida", StringComparison.Ordinal);
		text2 = text2.Replace("PRESENCE · NOT CONNECTED", "PRESENCIA · NO CONECTADA", StringComparison.Ordinal);
		text2 = text2.Replace("PRESENCE · FAILED", "PRESENCIA · FALLIDA", StringComparison.Ordinal);
		text2 = text2.Replace("Signed In", "Sesion iniciada", StringComparison.Ordinal);
		text2 = text2.Replace("Not Signed In", "Sin sesion", StringComparison.Ordinal);
		text2 = text2.Replace("Not signed in", "Sin sesion", StringComparison.Ordinal);
		text2 = text2.Replace("Spanish", "Espanol", StringComparison.Ordinal);
		text2 = text2.Replace("English", "Ingles", StringComparison.Ordinal);
		text2 = text2.Replace("DISCONNECTED", "DESCONECTADO", StringComparison.Ordinal);
		text2 = text2.Replace("CONNECTED", "CONECTADO", StringComparison.Ordinal);
		text2 = text2.Replace("FAILED", "FALLIDO", StringComparison.Ordinal);
		text2 = text2.Replace("ONLINE", "EN LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("OFFLINE", "FUERA DE LINEA", StringComparison.Ordinal);
		text2 = text2.Replace("syncing", "sincronizando", StringComparison.Ordinal);
		text2 = text2.Replace("refreshing", "actualizando", StringComparison.Ordinal);
		text2 = text2.Replace("connecting", "conectando", StringComparison.Ordinal);
		text2 = text2.Replace("pending", "pendiente", StringComparison.Ordinal);
		text2 = text2.Replace("offline", "fuera de linea", StringComparison.Ordinal);
		text2 = text2.Replace("not connected", "no conectado", StringComparison.Ordinal);
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
		string indicatorText = englishText switch
		{
			"LIVE" => "LIVE / CONNECTED",
			"Live loading" => "LIVE / LOADING",
			"Live limited" => "LIVE / LIMITED",
			"Connecting" => "CONNECTING",
			"Connection failed" => "CONNECTION FAILED",
			_ => "OFFLINE"
		};
		SetLabelText(connectionStatusLabel, TranslateTerminalText(indicatorText));
		connectionStatusLabel.AccessibleDescription = englishText;
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
		if (commandPaletteOpen || commandHistorySearchOpen)
		{
			UpdateCommandInputPresentation(scrollToCaret: false);
			return;
		}
		string text = TranslateTerminalText(englishText);
		if (!string.Equals(commandInput.PlaceholderText, text, StringComparison.Ordinal))
		{
			commandInput.PlaceholderText = text;
		}
		commandInput.RefreshPlaceholderRendering();
		UpdateCommandInputPresentation(scrollToCaret: false);
	}

	private void UpdateCommandInputPresentation(bool scrollToCaret)
	{
		string text = commandInput.Text;
		string text2 = string.IsNullOrWhiteSpace(text) ? commandInput.PlaceholderText : text;
		if (!string.Equals(commandInput.AccessibleDescription, text2, StringComparison.Ordinal))
		{
			commandInput.AccessibleDescription = text2;
		}
		if (commandInputToolTip != null && !string.Equals(commandInputToolTip.GetToolTip(commandInput), text2, StringComparison.Ordinal))
		{
			commandInputToolTip.SetToolTip(commandInput, text2);
		}
		if (scrollToCaret && commandInput.IsHandleCreated)
		{
			commandInput.ScrollToCaret();
			SendMessage(commandInput.Handle, EmScrollCaret, 0, 0);
		}
	}

	private void SetFooterPresenceText(string englishText)
	{
		footerPresenceTextSource = englishText;
		SetLabelText(footerPresenceLabel, TranslateTerminalText(englishText));
		footerPresenceLabel.ForeColor = string.Equals(englishText, "IDLE", StringComparison.OrdinalIgnoreCase)
			? AccentDim
			: GetSemanticStatusColor(englishText);
		footerThermalToolTip?.SetToolTip(footerPresenceLabel, TranslateTerminalText(englishText));
	}

	private void SetFooterStatusText(string englishText)
	{
		footerStatusTextSource = englishText;
		string displayText = FormatFooterStatusDisplayText(englishText);
		SetLabelText(footerStatusLabel, TranslateTerminalText(displayText));
		footerStatusLabel.ForeColor = GetFooterStatusColor(englishText);
		footerThermalToolTip?.SetToolTip(footerStatusLabel, TranslateTerminalText(englishText));
	}

	private void SetFooterOperationStatus(string englishText)
	{
		footerOperationStatusOverride = TrimOrNull(englishText);
		RefreshFooterOperationalSummary();
	}

	private static string FormatFooterStatusDisplayText(string text)
	{
		if (string.Equals(text, "NOT READY", StringComparison.OrdinalIgnoreCase))
			return "NOT READY";
		if (string.Equals(text, "READY", StringComparison.OrdinalIgnoreCase))
			return "READY";
		if (text.Contains("LIVE", StringComparison.OrdinalIgnoreCase))
			return "LIVE";
		if (text.Contains("fail", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("error", StringComparison.OrdinalIgnoreCase))
			return "FAILED";
		if (text.Contains("interrupt", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("lost", StringComparison.OrdinalIgnoreCase))
			return "INTERRUPTED";
		if (text.Contains("cancel", StringComparison.OrdinalIgnoreCase))
			return "CANCELLED";
		if (text.Contains("complete", StringComparison.OrdinalIgnoreCase))
			return "COMPLETE";
		if (text.Contains("connecting", StringComparison.OrdinalIgnoreCase))
			return "CONNECTING";
		if (text.Contains("not connected", StringComparison.OrdinalIgnoreCase))
			return "OFFLINE";
		return text;
	}

	private void SetFooterTargetText(string englishText)
	{
		footerTargetTextSource = englishText;
		SetLabelText(footerTargetLabel, TranslateTerminalText(englishText));
		footerTargetLabel.ForeColor = englishText.EndsWith(" 0", StringComparison.Ordinal)
			? AccentDim
			: LoadingStateColor;
		footerThermalToolTip?.SetToolTip(footerTargetLabel, TranslateTerminalText(englishText));
	}

	private void SetFooterThermals(double? cpu, double? gpu, double? edram, double? board, string? motherboard = null)
	{
		footerCpuTemp = cpu;
		footerGpuTemp = gpu;
		footerEdramTemp = edram;
		footerBoardTemp = board;
		footerThermalMotherboard = TrimOrNull(motherboard);
		bool hasReadings = cpu.HasValue || gpu.HasValue || edram.HasValue || board.HasValue;
		if (!hasReadings)
		{
			footerThermalSource = null;
		}
		SetFooterThermalText(!hasReadings && IsSessionConnected() ? "THERMALS UNAVAILABLE" : BuildFooterThermalText(cpu, gpu, edram, board));
	}

	private void SetFooterThermalsLoading()
	{
		footerCpuTemp = null;
		footerGpuTemp = null;
		footerEdramTemp = null;
		footerBoardTemp = null;
		footerThermalMotherboard = null;
		footerThermalSource = null;
		SetFooterThermalText("THERMALS LOADING");
	}

	private void SetFooterThermalText(string englishText)
	{
		footerThermalTextSource = englishText;
		RefreshFooterThermalPresentation();
	}

	private void RefreshFooterThermalPresentation()
	{
		if (footerThermalLabel.IsDisposed)
		{
			return;
		}

		string fullText = TranslateTerminalText(footerThermalTextSource);
		string displayText = SelectFooterThermalDisplayText(fullText);
		SetLabelText(footerThermalLabel, displayText);
		footerThermalLabel.ForeColor = GetFooterThermalStateColor(footerThermalTextSource);
		footerThermalLabel.SetReadings(footerCpuTemp, footerGpuTemp, footerEdramTemp, footerBoardTemp, footerThermalMotherboard);
		string toolTipText = BuildFooterThermalToolTip(fullText);
		if (!string.Equals(footerThermalToolTip.GetToolTip(footerThermalLabel), toolTipText, StringComparison.Ordinal))
		{
			footerThermalToolTip.SetToolTip(footerThermalLabel, toolTipText);
		}
	}

	private string BuildFooterThermalToolTip(string fullText)
	{
		if (!footerCpuTemp.HasValue && !footerGpuTemp.HasValue && !footerEdramTemp.HasValue && !footerBoardTemp.HasValue)
		{
			return fullText;
		}

		ThermalThresholdProfile thresholds = ResolveThermalThresholds(footerThermalMotherboard);
		string boardName = footerThermalMotherboard ?? "unknown motherboard";
		return fullText
			+ Environment.NewLine
			+ "Source: " + (footerThermalSource ?? "unknown")
			+ Environment.NewLine
			+ $"{boardName} SMC target / critical: CPU {thresholds.CpuTarget}/{thresholds.CpuCritical}°C, GPU {thresholds.GpuTarget}/{thresholds.GpuCritical}°C, EDRAM {thresholds.EdramTarget}/{thresholds.EdramCritical}°C."
			+ Environment.NewLine
			+ "Board sensor advisory: yellow 55°C, orange 65°C, red 75°C.";
	}

	private string SelectFooterThermalDisplayText(string fullText)
	{
		int availableWidth = Math.Max(footerThermalLabel.ClientSize.Width, footerThermalLabel.Width) - footerThermalLabel.Padding.Horizontal;
		if (availableWidth <= 0)
		{
			return fullText;
		}

		if (MeasureFooterTextWidth(fullText) <= availableWidth)
		{
			return fullText;
		}

		string compactText = BuildCompactFooterThermalText(fullText);
		if (MeasureFooterTextWidth(compactText) <= availableWidth)
		{
			return compactText;
		}

		return BuildUltraCompactFooterThermalText(fullText);
	}

	private int MeasureFooterTextWidth(string text)
	{
		Size size = TextRenderer.MeasureText(text, footerThermalLabel.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		return size.Width;
	}

	private static string BuildCompactFooterThermalText(string text)
	{
		return text
			.Replace("EDRAM ", "ED ", StringComparison.Ordinal)
			.Replace("BOARD ", "BD ", StringComparison.Ordinal)
			.Replace("°C", "°", StringComparison.Ordinal)
			.Replace(" · ", "  ", StringComparison.Ordinal);
	}

	private static string BuildUltraCompactFooterThermalText(string text)
	{
		return text
			.Replace("EDRAM ", "E", StringComparison.Ordinal)
			.Replace("BOARD ", "B", StringComparison.Ordinal)
			.Replace("CPU ", "C", StringComparison.Ordinal)
			.Replace("GPU ", "G", StringComparison.Ordinal)
			.Replace("°C", "°", StringComparison.Ordinal)
			.Replace(" · ", "  ", StringComparison.Ordinal);
	}

	private static Color GetFooterStatusColor(string text)
	{
		if (string.Equals(text, "NOT READY", StringComparison.OrdinalIgnoreCase))
		{
			return FailureStateColor;
		}
		if (string.Equals(text, "READY", StringComparison.OrdinalIgnoreCase))
		{
			return SuccessStateColor;
		}
		return GetSemanticStatusColor(text);
	}

	private static Color GetFooterThermalStateColor(string text)
	{
		if (text.Contains("loading", StringComparison.OrdinalIgnoreCase))
		{
			return LoadingStateColor;
		}
		if (text.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
		{
			return WarningColor;
		}
		if (text.Contains("--", StringComparison.Ordinal))
		{
			return EmptyStateColor;
		}
		return PrimaryTextColor;
	}

	private static Color GetConnectionStateColor(ConnectionUiState state)
	{
		return state switch
		{
			ConnectionUiState.Connected => SuccessStateColor,
			ConnectionUiState.Connecting => LoadingStateColor,
			ConnectionUiState.Failed => FailureStateColor,
			_ => FailureStateColor
		};
	}

	private static Color GetSemanticStatusColor(string text)
	{
		string value = text ?? string.Empty;
		if (value.Contains("fail", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("error", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("denied", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("timed out", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("interrupted", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("connection lost", StringComparison.OrdinalIgnoreCase))
			return FailureStateColor;
		if (value.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("canceled", StringComparison.OrdinalIgnoreCase))
			return WarningColor;
		if (value.Contains("connecting", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("loading", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("awaiting", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("waiting", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("reading", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("running", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("active", StringComparison.OrdinalIgnoreCase))
			return LoadingStateColor;
		if (value.Contains("not connected", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("offline", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("no live", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("no drives", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("connect to", StringComparison.OrdinalIgnoreCase))
			return EmptyStateColor;
		if (value.Contains("LIVE", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("connected", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("restored", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("complete", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("saved", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("success", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("validated", StringComparison.OrdinalIgnoreCase))
			return SuccessStateColor;
		if (string.Equals(value.Trim(), "READY", StringComparison.OrdinalIgnoreCase))
			return SuccessStateColor;
		return AccentDim;
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
		rightTempLabel.SetStateText(TranslateTerminalText(englishText));
		Color stateColor = GetTrafficStatusColor(englishText);
		rightTempLabel.ForeColor = stateColor;
		ftpTrafficGraph?.SetStatus(FormatTrafficGraphStatus(englishText), stateColor);
	}

	private static string FormatTrafficGraphStatus(string text)
	{
		if (text.Contains("failed", StringComparison.OrdinalIgnoreCase))
			return "FAILED";
		if (text.Contains("XBDM", StringComparison.OrdinalIgnoreCase))
			return "NO FTP";
		if (text.Contains("loading", StringComparison.OrdinalIgnoreCase))
			return "LOADING";
		if (text.Contains("connecting", StringComparison.OrdinalIgnoreCase))
			return "CONNECTING";
		return "IDLE";
	}

	private static Color GetTrafficStatusColor(string text)
	{
		if (text.Contains("XBDM", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("loading", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("connecting", StringComparison.OrdinalIgnoreCase))
			return LoadingStateColor;
		if (text.Contains("RX", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("TX", StringComparison.OrdinalIgnoreCase))
			return PrimaryTextColor;
		return GetSemanticStatusColor(text);
	}

	private void RecordFtpTrafficBytes(FtpTrafficDirection direction, long byteCount)
	{
		ftpTrafficMeter.Record(direction, byteCount);
	}

	private void RefreshFtpTrafficPresentation()
	{
		if (IsDisposed || Disposing)
		{
			return;
		}

		FtpTrafficRateSample sample = ftpTrafficMeter.Sample(DateTime.UtcNow);
		if (shellDisconnected)
		{
			string disconnectedStatus = connectAttemptInFlight
				? "CONNECTING"
				: connectAttemptFailed ? "FAILED" : "OFFLINE";
			Color disconnectedColor = connectAttemptInFlight
				? LoadingStateColor
				: connectAttemptFailed ? FailureStateColor : EmptyStateColor;
			SetFtpTrafficUnavailable(disconnectedStatus, disconnectedColor);
			return;
		}
		if (latestSnapshot?.FtpServiceReachable == false)
		{
			SetFtpTrafficUnavailable("NO FTP", LoadingStateColor);
			return;
		}

		bool active = fileTransferInFlight || sample.RxBytes > 0L || sample.TxBytes > 0L;
		SetFtpTrafficRates(sample.RxKbps, sample.TxKbps, active);
	}

	private void SetFtpTrafficRates(double rxKbps, double txKbps, bool active)
	{
		double normalizedRx = Math.Max(0.0, rxKbps);
		double normalizedTx = Math.Max(0.0, txKbps);
		string rxText = FormatFtpTransferRate(normalizedRx);
		string txText = FormatFtpTransferRate(normalizedTx);
		rightTempTextSource = "RX " + rxText + Environment.NewLine + "TX " + txText;
		rightTempLabel.SetRates(rxText, txText);
		string status = active ? "LIVE" : "IDLE";
		Color statusColor = active ? AccentGreen : AccentDim;
		ftpTrafficGraph.SetStatus(status, statusColor);
		ftpTrafficGraph.AddSample(normalizedRx, normalizedTx);
		ftpTrafficGraph.AccessibleDescription = "FTP payload traffic. Receive " + rxText + "; send " + txText + "; status " + status + ".";
	}

	private void SetFtpTrafficUnavailable(string status, Color statusColor)
	{
		rightTempTextSource = "RX --" + Environment.NewLine + "TX --";
		rightTempLabel.SetUnavailable();
		ftpTrafficGraph.SetStatus(status, statusColor);
		ftpTrafficGraph.AccessibleDescription = "FTP payload traffic is " + status.ToLowerInvariant() + ".";
	}

	private void ResetFtpTrafficTracking(string status = "OFFLINE")
	{
		ftpTrafficMeter.Reset(DateTime.UtcNow);
		ftpTrafficGraph.ClearSamples();
		SetFtpTrafficUnavailable(status, string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase) ? FailureStateColor : EmptyStateColor);
	}

	private static string FormatFtpTransferRate(double kbps)
	{
		if (kbps >= 1000.0)
		{
			return (kbps / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " Mbps";
		}
		return kbps.ToString(kbps >= 100.0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " kbps";
	}

	private static void SetControlText(Control control, string value)
	{
		if (!string.Equals(control.Text, value, StringComparison.Ordinal))
		{
			control.Text = value;
		}
	}

	private static void ConfigureFooterChipLabel(Label label)
	{
		label.Dock = DockStyle.Fill;
		label.Margin = Padding.Empty;
		label.Padding = new Padding(12, 0, 12, 0);
		label.AutoEllipsis = true;
		label.ForeColor = PrimaryTextColor;
		label.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
		label.BackColor = FooterChipBackground;
		label.MinimumSize = new Size(0, 32);
		label.TextAlign = ContentAlignment.MiddleLeft;
	}

	private class FooterSegmentLabel : Label
	{
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool ShowLeadingSeparator { get; init; }

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			PaintLeadingSeparator(e.Graphics);
		}

		protected void PaintLeadingSeparator(Graphics graphics)
		{
			if (!ShowLeadingSeparator || ClientSize.Height < 10)
			{
				return;
			}

			Color dividerColor = InterpolateColor(BorderColor, AccentCyan, 0.48f);
			using Pen divider = new Pen(Color.FromArgb(190, dividerColor), 1f);
			graphics.DrawLine(divider, 1, 6, 1, ClientSize.Height - 7);
		}
	}

	private sealed class FooterThermalLabel : FooterSegmentLabel
	{
		private double? cpu;
		private double? gpu;
		private double? edram;
		private double? board;
		private string? motherboard;

		public void SetReadings(double? cpuValue, double? gpuValue, double? edramValue, double? boardValue, string? motherboardValue)
		{
			cpu = cpuValue;
			gpu = gpuValue;
			edram = edramValue;
			board = boardValue;
			motherboard = motherboardValue;
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (!cpu.HasValue && !gpu.HasValue && !edram.HasValue && !board.HasValue)
			{
				base.OnPaint(e);
				return;
			}

			base.OnPaintBackground(e);
			using Font labelFont = new Font(Font.FontFamily, Math.Max(7.75f, Font.Size - 0.25f), FontStyle.Bold, GraphicsUnit.Point);
			using Font valueFont = new Font(Font.FontFamily, Font.Size, FontStyle.Regular, GraphicsUnit.Point);
			(string Label, string Sensor, double? Value)[] readings =
			[
				("CPU", "CPU", cpu),
				("GPU", "GPU", gpu),
				("EDRAM", "EDRAM", edram),
				("BOARD", "BOARD", board)
			];
			TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
			int gapWidth = TextRenderer.MeasureText("   ", valueFont, Size.Empty, flags).Width;
			int totalWidth = 0;
			foreach ((string label, string _, double? value) in readings)
			{
				totalWidth += TextRenderer.MeasureText(label + " ", labelFont, Size.Empty, flags).Width;
				totalWidth += TextRenderer.MeasureText(FormatTemp(value), valueFont, Size.Empty, flags).Width;
			}
			totalWidth += gapWidth * (readings.Length - 1);
			int x = Math.Max(Padding.Left + 5, ClientSize.Width - Padding.Right - totalWidth);
			int contentHeight = Math.Max(labelFont.Height, valueFont.Height) + 2;
			int y = Math.Max(0, (ClientSize.Height - contentHeight) / 2);

			for (int i = 0; i < readings.Length; i++)
			{
				(string label, string sensor, double? value) = readings[i];
				string labelText = label + " ";
				int labelWidth = TextRenderer.MeasureText(labelText, labelFont, Size.Empty, flags).Width;
				TextRenderer.DrawText(e.Graphics, labelText, labelFont, new Rectangle(x, y, labelWidth, contentHeight), PrimaryTextColor, flags);
				x += labelWidth;
				string valueText = FormatTemp(value);
				int valueWidth = TextRenderer.MeasureText(valueText, valueFont, Size.Empty, flags).Width;
				TextRenderer.DrawText(e.Graphics, valueText, valueFont, new Rectangle(x, y, valueWidth, contentHeight), GetThermalSeverityColor(sensor, value, motherboard), flags);
				x += valueWidth + gapWidth;
			}
			PaintLeadingSeparator(e.Graphics);
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
		SetLabelText(shellBadgeLabel, "XeCLI");
		shellBadgeLabel.ForeColor = AccentGreen;
		shellBadgeLabel.Font = headerFont;
		SetLabelText(shellTargetLabel, TranslateTerminalText("TARGET  ") + FormatCurrentTarget());
		shellTargetLabel.ForeColor = Color.FromArgb(214, AccentDim);
	}

	private void RefreshLocalizedUiText()
	{
		base.Text = TranslateTerminalText("XeCLI Terminal");
		SetLabelText(leftConsoleHeaderLabel, TranslateTerminalText("CONSOLE"));
		SetLabelText(rightStatusHeaderLabel, TranslateTerminalText("RGH STATUS"));
		SetLabelText(rightTrafficHeaderLabel, TranslateTerminalText("FTP TRAFFIC"));
		RefreshShellTargetLabelText();
		SetControlText(pluginsTabButton, TranslateTerminalText("Plugins"));
		SetControlText(modulesTabButton, TranslateTerminalText("Modules"));
		SetControlText(disconnectButton, TranslateTerminalText("DISCONNECT"));
		SetControlText(screenshotButton, TranslateTerminalText("SCREENSHOT"));
		SetControlText(settingsButton, "⚙");
		settingsButton.AccessibleName = TranslateTerminalText("SETTINGS");
		filePathToolTip.SetToolTip(settingsButton, TranslateTerminalText("SETTINGS") + " (Ctrl+,)");
		RefreshScreenshotNotificationPresentation();
		SetControlText(commandSubmitButton, "→");
		SetControlText(localUpButton, "↑");
		SetControlText(localRefreshButton, "↻");
		SetControlText(remoteUpButton, "↑");
		SetControlText(remoteRefreshButton, "↻");
		SetControlText(localTabButton, TranslateTerminalText("LOCAL"));
		SetControlText(ftpTabButton, TranslateTerminalText("FTP"));
		SetControlText(queueTabButton, TranslateTerminalText("QUEUE"));
		SetPanelHeaderLabelText(targetIpHostPanel, "Host or IP (optional port)");
		targetIpTextBox.PlaceholderText = TranslateTerminalText("Host or IP (optional port)");
		SetPanelHeaderLabelText(localPathHostPanel, "LOCAL PATH");
		SetPanelHeaderLabelText(remotePathHostPanel, "REMOTE PATH");
		localFileList.HeaderText = TranslateTerminalText("NAME");
		remoteFileList.HeaderText = TranslateTerminalText("NAME");
		RefreshLanguageButtonText();
		RefreshThemeButtonText();
		SetConnectButtonText(connectButtonTextSource);
		SetCommandInputPlaceholder(commandInputPlaceholderSource);
		SetLeftConsoleText(leftConsoleTextSource);
		SetLeftSignInText(leftSignInTextSource);
		SetFooterPresenceText(footerPresenceTextSource);
		SetFooterStatusText(footerStatusTextSource);
		SetFooterTargetText(footerTargetTextSource);
		if (rightTempTextSource.StartsWith("RX ", StringComparison.Ordinal) && rightTempTextSource.Contains("TX ", StringComparison.Ordinal))
		{
			rightTempLabel.Invalidate();
			ftpTrafficGraph.Invalidate();
		}
		else
		{
			SetRightTempText(rightTempTextSource);
		}
		SetRightDetailText(rightDetailTextSource);
		SetRightDrivesTitleText(rightDrivesTitleTextSource);
		UpdateConnectionStatusSurfaces();
		SetFooterThermalText(footerThermalTextSource);
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
		targetIpTextBox.Text = FormatTargetEndpoint(currentTargetIp, currentTargetPort);
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
		button.FlatAppearance.BorderSize = 0;
		button.BackColor = TerminalBackground;
		button.ForeColor = PrimaryTextColor;
		button.Font = new Font("Consolas", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
		button.MinimumSize = new Size(0, 22);
		button.Margin = Padding.Empty;
		button.Padding = new Padding(2, 0, 2, 0);
		button.AutoEllipsis = false;
		button.TextAlign = ContentAlignment.MiddleCenter;
		button.Text = text;
		button.Cursor = Cursors.Hand;
		if (button is TerminalButton terminalButton)
		{
			terminalButton.EnabledBackColor = TerminalBackground;
			terminalButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.12f);
			terminalButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.18f);
			terminalButton.BorderColor = BorderColor;
			terminalButton.DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);
			terminalButton.DisabledBorderColor = InterpolateColor(TerminalBackground, BorderColor, 0.42f);
			terminalButton.EnabledTextColor = PrimaryTextColor;
			terminalButton.DisabledTextColor = InterpolateColor(TerminalBackground, AccentDim, 0.52f);
		}
	}

	private void ConfigureCommandSubmitButton()
	{
		ConfigureActionButton(commandSubmitButton, "→");
		commandSubmitButton.Margin = new Padding(3);
		commandSubmitButton.Padding = Padding.Empty;
		commandSubmitButton.Font = new Font("Segoe UI Symbol", 14f, FontStyle.Bold, GraphicsUnit.Point);
		commandSubmitButton.AccessibleName = "Submit command";
		commandSubmitButton.AccessibleDescription = "Type a command, then press Enter.";
		commandInputToolTip?.SetToolTip(commandSubmitButton, "Type a command, then press Enter.");
		commandSubmitButton.Enabled = false;
		commandSubmitButton.Enter += delegate
		{
			commandInputHost.Invalidate();
		};
		commandSubmitButton.Leave += delegate
		{
			commandInputHost.Invalidate();
		};
		commandSubmitButton.Click += delegate
		{
			SubmitCommandInput();
			UpdateCommandSubmitButtonState();
		};
		if (commandSubmitButton is TerminalButton terminalButton)
		{
			terminalButton.CornerRadius = CommandInputOuterRadius;
			terminalButton.RoundLeftCorners = true;
			terminalButton.RoundRightCorners = true;
			terminalButton.DrawBorder = true;
			terminalButton.BorderThickness = 1.5f;
			terminalButton.DrawFocusCue = false;
		}
		ApplyCommandSubmitButtonTheme();
	}

	private void ApplyCommandSubmitButtonTheme()
	{
		Color commandSurface = GetCommandInputSurfaceColor();
		Color enabledBackColor = InterpolateColor(commandSurface, AccentGreen, 0.18f);
		commandInputHost.BackColor = GetTerminalOutputSurfaceColor();
		commandInputLayout.BackColor = commandSurface;
		commandInput.BackColor = commandSurface;
		commandInput.ForeColor = PrimaryTextColor;
		commandInput.PlaceholderColor = GetCommandInputPlaceholderColor();
		commandInput.RefreshPlaceholderRendering();
		commandInputHost.Invalidate();
		if (commandSubmitButton is TerminalButton terminalButton)
		{
			terminalButton.EnabledBackColor = enabledBackColor;
			terminalButton.HoverBackColor = InterpolateColor(commandSurface, AccentGreen, 0.26f);
			terminalButton.PressedBackColor = InterpolateColor(commandSurface, AccentGreen, 0.12f);
			terminalButton.BorderColor = InterpolateColor(commandSurface, AccentGreen, 0.68f);
			terminalButton.FocusBorderColor = AccentGreen;
			terminalButton.LeftDividerColor = Color.Empty;
			terminalButton.DisabledBackColor = InterpolateColor(commandSurface, AccentGreen, 0.045f);
			terminalButton.DisabledBorderColor = InterpolateColor(commandSurface, AccentGreen, 0.30f);
			terminalButton.EnabledTextColor = InterpolateColor(AccentGreen, PrimaryTextColor, 0.18f);
			terminalButton.DisabledTextColor = InterpolateColor(commandSurface, AccentGreen, 0.35f);
		}
	}

	private void ConfigureInputTextBox(TextBox textBox, string placeholderText, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
	{
		textBox.Dock = DockStyle.Fill;
		textBox.BackColor = TerminalBackground;
		textBox.ForeColor = PrimaryTextColor;
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
		panel.Padding = new Padding(6, 3, 6, 4);
		panel.BackColor = TerminalBackground;
		panel.Controls.Clear();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 2;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 17f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Label label = new Label
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = new Padding(0),
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = AccentGreen,
			Font = shellFontBold,
			AutoEllipsis = true,
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
			using SolidBrush brush = new SolidBrush(TerminalBackground);
			using Pen pen = new Pen(Color.FromArgb(84, AccentGreen), 1f);
			e.Graphics.FillRectangle(brush, clientRectangle);
			e.Graphics.DrawRectangle(pen, clientRectangle);
		};
	}

	private void ConfigureFilePathTextBox(TextBox textBox, string accessibleName)
	{
		textBox.AccessibleName = accessibleName;
		textBox.TextChanged += delegate
		{
			RefreshFilePathDetails(textBox);
		};
		textBox.SizeChanged += delegate
		{
			RefreshFilePathDetails(textBox);
		};
		RefreshFilePathDetails(textBox);
	}

	private void RefreshFilePathDetails(TextBox textBox)
	{
		string path = string.IsNullOrWhiteSpace(textBox.Text) ? "Path is empty" : textBox.Text;
		textBox.AccessibleDescription = "Full path: " + path;
		filePathToolTip.SetToolTip(textBox, string.Empty);
	}

	private void ConfigureTargetIpHostPanel(Panel panel, TextBox textBox, string labelText)
	{
		panel.Dock = DockStyle.Fill;
		panel.Margin = new Padding(0, 0, 0, 2);
		panel.Padding = new Padding(6, 4, 6, 3);
		panel.BackColor = TerminalBackground;
		panel.Controls.Clear();
		panel.Paint += delegate(object? _, PaintEventArgs e)
		{
			Rectangle clientRectangle = panel.ClientRectangle;
			clientRectangle.Width = Math.Max(0, clientRectangle.Width - 1);
			clientRectangle.Height = Math.Max(0, clientRectangle.Height - 1);
			using SolidBrush brush = new SolidBrush(TerminalBackground);
			using Pen pen = new Pen(Color.FromArgb(84, AccentGreen), 1f);
			e.Graphics.FillRectangle(brush, clientRectangle);
			e.Graphics.DrawRectangle(pen, clientRectangle);
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Fill;
		tableLayoutPanel.Margin = Padding.Empty;
		tableLayoutPanel.Padding = Padding.Empty;
		tableLayoutPanel.ColumnCount = 1;
		tableLayoutPanel.RowCount = 1;
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
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
		textBox.BackColor = TerminalBackground;
		textBox.ForeColor = PrimaryTextColor;
		textBox.Font = shellFont;
		textBox.PlaceholderText = labelText;
		textBox.TextAlign = HorizontalAlignment.Center;
		textBox.Margin = Padding.Empty;
		textBox.Height = Math.Max(shellFont.Height + 6, 22);
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
		tableLayoutPanel.Controls.Add(panel2, 0, 0);
		panel.Controls.Add(tableLayoutPanel);
	}

	private string FormatCurrentTarget()
	{
		return FormatTargetEndpoint(currentTargetIp, currentTargetPort);
	}

	private static string FormatTargetEndpoint(string ip, int port)
	{
		if (port == 730)
			return ip;
		return ip + ":" + port.ToString(CultureInfo.InvariantCulture);
	}

	private void RestoreConnectButtonIdleState()
	{
		if (connectButton == null)
		{
			return;
		}
		SetConnectButtonText("CONNECT");
		if (connectButton is TerminalButton terminalButton)
		{
			terminalButton.EnabledBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.18f);
			terminalButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.28f);
			terminalButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.38f);
			terminalButton.BorderColor = Color.FromArgb(182, AccentGreen);
			terminalButton.EnabledTextColor = AccentGreen;
			terminalButton.DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);
			terminalButton.DisabledBorderColor = InterpolateColor(TerminalBackground, BorderColor, 0.42f);
			terminalButton.DisabledTextColor = InterpolateColor(TerminalBackground, AccentDim, 0.52f);
		}
		connectButton.Enabled = true;
		connectButton.Invalidate();
	}

	private string FormatStatusTarget(int maxLength = 18)
	{
		return FitStatusText(showStatusTargetEndpoint ? FormatCurrentTarget() : "Not connected", maxLength);
	}

	private string FormatStatusTargetEndpoint(int maxLength = 18)
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
			Xuid = connected ? telemetrySnapshot?.Xuid : null,
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
		if (connectButton == null)
		{
			return;
		}
		SetConnectButtonText("CONNECTING");
		if (connectButton is TerminalButton terminalButton)
		{
			Color connectingBackground = InterpolateColor(TerminalBackground, LoadingStateColor, 0.20f);
			terminalButton.DisabledBackColor = connectingBackground;
			terminalButton.DisabledBorderColor = Color.FromArgb(172, LoadingStateColor);
			terminalButton.DisabledTextColor = LoadingStateColor;
		}
		connectButton.Enabled = false;
		connectButton.Invalidate();
	}

	private void UpdateConnectionActionAvailability(ConnectionUiState connectionUiState)
	{
		if (connectButton != null)
		{
			if (connectionUiState == ConnectionUiState.Connecting)
			{
				SetConnectButtonConnectingState();
			}
			else
			{
				RestoreConnectButtonIdleState();
			}
			connectButton.Enabled = connectionUiState == ConnectionUiState.Disconnected || connectionUiState == ConnectionUiState.Failed;
		}
		if (disconnectButton != null)
		{
			disconnectButton.Enabled = connectionUiState == ConnectionUiState.Connected;
		}
		if (screenshotButton != null)
		{
			screenshotButton.Enabled = connectionUiState == ConnectionUiState.Connected && !fileTransferInFlight;
		}
		if (avatarButton != null)
		{
			avatarButton.Enabled = connectionUiState == ConnectionUiState.Connected && !fileTransferInFlight;
		}
	}

	private void SetRemoteBrowserPlaceholder(string pathText, string message)
	{
		string text = NormalizeRemotePath(pathText);
		remotePathLabel.Text = FormatRemotePathLabel(text);
		remotePathTextBox.Text = text;
		remoteFileList.SetMessage(TranslateTerminalText(message));
	}

	private void SetRemoteBrowserUnavailablePlaceholder(string pathText, string summary)
	{
		string text = NormalizeRemotePath(pathText);
		remotePathLabel.Text = FormatRemotePathLabel(text);
		remotePathTextBox.Text = text;
		remoteFileList.SetMessage(TranslateTerminalText(BuildRemoteFtpUnavailableMessage(summary)));
		ClearStatusToast();
	}

	private string GetRemoteBrowserLoadingMessage()
	{
		return connectAttemptInFlight ? "CONNECTING" : "LOADING";
	}

	private void SetConnectedLoadingAuxiliaryState()
	{
		SetFtpTrafficUnavailable("LOADING", LoadingStateColor);
		SetFooterThermalsLoading();
		UpdateDriveInventory(Array.Empty<DriveInventoryEntry>());
		SetRemoteBrowserPlaceholder("/", GetRemoteBrowserLoadingMessage());
	}

	private static string BuildRemoteFtpUnavailableMessage(string summary)
	{
		return summary + "\nAurora/FreestyleDash/XexMenu/DashLaunch FTP.\nLaunch one, then retry.";
	}

	private static bool LooksLikeFtpTimeout(Exception ex)
	{
		return EnumerateExceptions(ex).Any(inner =>
			inner is TimeoutException ||
			inner is OperationCanceledException ||
			(inner.Message?.Contains("timed out", StringComparison.OrdinalIgnoreCase) ?? false) ||
			(inner.Message?.Contains("did not complete", StringComparison.OrdinalIgnoreCase) ?? false));
	}

	private static IEnumerable<Exception> EnumerateExceptions(Exception ex)
	{
		yield return ex;
		if (ex is AggregateException aggregate)
		{
			foreach (Exception inner in aggregate.Flatten().InnerExceptions)
			{
				foreach (Exception nested in EnumerateExceptions(inner))
					yield return nested;
			}
		}
		else if (ex.InnerException != null)
		{
			foreach (Exception nested in EnumerateExceptions(ex.InnerException))
				yield return nested;
		}
	}

	private static string BuildRemoteBrowseFailureSummary(Exception ex)
	{
		if (LooksLikeFtpTimeout(ex))
		{
			return "FTP browse timed out.";
		}
		string detail = string.Join(" | ", EnumerateExceptions(ex).Select(inner => inner.Message));
		if (detail.Contains("530", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("authentication", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("login", StringComparison.OrdinalIgnoreCase))
		{
			return "FTP sign-in failed.";
		}
		if (detail.Contains("550", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("no such", StringComparison.OrdinalIgnoreCase))
		{
			return "Remote directory was not found.";
		}
		if (detail.Contains("425", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("426", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("data connection", StringComparison.OrdinalIgnoreCase))
		{
			return "FTP data connection failed.";
		}
		if (detail.Contains("refused", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("socket", StringComparison.OrdinalIgnoreCase)
			|| detail.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
		{
			return "FTP service unavailable.";
		}
		return "FTP browse failed.";
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
		case "flash":
			canonicalDriveName = "FLASH";
			return true;
		case "dvd":
			canonicalDriveName = "DVD";
			return true;
		}
		if (TryGetIndexedCanonicalDriveName(text, "hdd", "HDD", out canonicalDriveName)
			|| TryGetIndexedCanonicalDriveName(text, "usb", "USB", out canonicalDriveName)
			|| TryGetIndexedCanonicalDriveName(text, "memunit", "MEMUNIT", out canonicalDriveName)
			|| TryGetIndexedCanonicalDriveName(text, "mu", "MEMUNIT", out canonicalDriveName)
			|| TryGetIndexedCanonicalDriveName(text, "sysdev", "SYSDEV", out canonicalDriveName))
		{
			return true;
		}
		canonicalDriveName = string.Empty;
		return false;
	}

	private static bool TryGetIndexedCanonicalDriveName(string value, string sourcePrefix, string canonicalPrefix, out string canonicalDriveName)
	{
		canonicalDriveName = string.Empty;
		if (!value.StartsWith(sourcePrefix, StringComparison.Ordinal) || value.Length == sourcePrefix.Length)
		{
			return false;
		}
		int digitStart = sourcePrefix.Length;
		int digitEnd = digitStart;
		while (digitEnd < value.Length && char.IsDigit(value[digitEnd]))
		{
			digitEnd++;
		}
		if (digitEnd == digitStart
			|| !int.TryParse(value.AsSpan(digitStart, digitEnd - digitStart), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
			|| index > 99)
		{
			return false;
		}
		canonicalDriveName = canonicalPrefix + index.ToString(CultureInfo.InvariantCulture);
		return true;
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
				UpdateConnectionStatusSurfaces(latestSnapshot);
			}
		}
		SetRightDrivesTitleText("DETECTED DRIVES");
		drivesList.SetEntries((list.Count != 0) ? list : new DriveInventoryEntry[1]
		{
			new DriveInventoryEntry
			{
				Name = TranslateTerminalText(connectAttemptInFlight ? "CONNECTING" : (!IsSessionConnected() ? "Connect to view drives." : (HasConnectedTelemetry(latestSnapshot) ? "No drives detected" : "LOADING")))
			}
		});
	}

	private void ResetLiveSessionUi(bool connected, string footerText, Color footerColor, string shellStatusText, Color shellStatusColor, Color shellStatusBackground, string remoteMessage)
	{
		shellDisconnected = !connected;
		if (!connected)
		{
			ResetFtpTrafficTracking();
			SetRightDetailText(BuildDisconnectedSessionText());
			SetLeftConsoleText(BuildDisconnectedConsoleText());
			SetLeftSignInText("INVENTORY");
			SetFooterThermals(null, null, null, null);
			UpdateDriveInventory(Array.Empty<DriveInventoryEntry>());
			latestSnapshot = null;
			remoteBrowserHasLoaded = false;
			ClearStatusToast();
		}
		else
		{
			UpdateDriveInventory((IEnumerable<DriveInventoryEntry>?)latestSnapshot?.Drives ?? Array.Empty<DriveInventoryEntry>());
		}
		UpdateConnectionStatusSurfaces(connected ? latestSnapshot : null);
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
		if (!TargetProfileStore.TryParseTargetEndpoint(text, out string normalizedIp, out int normalizedPort))
		{
			AppendSystemLine("Target host or IP is invalid.", WarningColor);
			targetIpTextBox.Focus();
			return false;
		}
		if (string.Equals(currentTargetIp, normalizedIp, StringComparison.OrdinalIgnoreCase) && currentTargetPort == normalizedPort)
		{
			if (announceUnchanged)
			{
				AppendSystemLine("Target already set to " + FormatCurrentTarget() + ".", AccentDim);
			}
			return true;
		}
		CancelAutoReconnect(disarm: true);
		CancelAndDispose(ref remoteBrowseCts);
		CancelAndDispose(ref connectProbeCts);
		CancelAndDispose(ref fileTransferCts);
		telemetryTimer.Stop();
		connectAttemptInFlight = false;
		connectAttemptFailed = false;
		ClearConnectionFailureState();
		Interlocked.Increment(ref sessionEpoch);
		currentTargetIp = normalizedIp;
		currentTargetPort = normalizedPort;
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.DefaultIp = currentTargetIp;
		cliConfig.DefaultPort = currentTargetPort;
		cliConfig.Save();
		latestSnapshot = null;
		remoteEntries.Clear();
		remoteCurrentPath = "/";
		remoteBrowserHasLoaded = false;
		nextRemoteRefreshAllowedUtc = DateTime.MinValue;
		UpdateRuntimePresence(connected: false);
		RestoreConnectButtonIdleState();
		SetCommandInputPlaceholder("Enter a local command");
		ResetLiveSessionUi(connected: false, "TARGET READY", PrimaryTextColor, "Not connected", EmptyStateColor, InterpolateColor(TerminalBackground, EmptyStateColor, 0.07f), "Connect to browse remote files.");
		RefreshInventoryListFromSnapshot();
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
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 0;
		button.BackColor = TerminalBackground;
		button.ForeColor = AccentDim;
		button.Font = shellFontBold;
		button.MinimumSize = new Size(0, 24);
		button.Margin = Padding.Empty;
		button.Padding = new Padding(2, 0, 2, 0);
		button.Text = text;
		button.TextAlign = ContentAlignment.MiddleCenter;
		button.Cursor = Cursors.Hand;
		if (button is TerminalButton terminalButton)
		{
			terminalButton.EnabledBackColor = TerminalBackground;
			terminalButton.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.12f);
			terminalButton.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.18f);
			terminalButton.BorderColor = BorderColor;
			terminalButton.DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);
			terminalButton.DisabledBorderColor = InterpolateColor(TerminalBackground, BorderColor, 0.42f);
			terminalButton.EnabledTextColor = AccentDim;
			terminalButton.DisabledTextColor = InterpolateColor(TerminalBackground, AccentDim, 0.52f);
		}
	}

	private void RefreshInventoryHeader()
	{
		inventoryList.ModuleView = inventoryShowsModules;
		ApplyToggleButtonState(pluginsTabButton, !inventoryShowsModules);
		ApplyToggleButtonState(modulesTabButton, inventoryShowsModules);
	}

	private static void ApplyToggleButtonState(Button button, bool active)
	{
		Color activeBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.20f);
		Color inactiveBackColor = TerminalBackground;
		Color activeBorderColor = Color.FromArgb(210, AccentGreen);
		Color inactiveBorderColor = BorderColor;
		Color activeTextColor = AccentCyan;
		Color inactiveTextColor = AccentDim;
		button.BackColor = active ? activeBackColor : inactiveBackColor;
		button.ForeColor = active ? activeTextColor : inactiveTextColor;
		if (button is TerminalButton terminalButton)
		{
			terminalButton.EnabledBackColor = active ? activeBackColor : inactiveBackColor;
			terminalButton.HoverBackColor = active ? InterpolateColor(TerminalBackground, AccentGreen, 0.30f) : InterpolateColor(TerminalBackground, AccentGreen, 0.12f);
			terminalButton.PressedBackColor = active ? InterpolateColor(TerminalBackground, AccentGreen, 0.22f) : InterpolateColor(TerminalBackground, AccentGreen, 0.18f);
			terminalButton.BorderColor = active ? activeBorderColor : inactiveBorderColor;
			terminalButton.EnabledTextColor = active ? activeTextColor : inactiveTextColor;
		}
		button.Invalidate();
	}

	private void ResetInventoryLoadingState()
	{
		modulesInventoryLoadingSinceUtc = DateTime.MinValue;
		pluginsInventoryLoadingSinceUtc = DateTime.MinValue;
	}

	private DateTime GetInventoryLoadingSinceUtc(bool modulesView)
	{
		return modulesView ? modulesInventoryLoadingSinceUtc : pluginsInventoryLoadingSinceUtc;
	}

	private void SetInventoryLoadingSinceUtc(bool modulesView, DateTime value)
	{
		if (modulesView)
		{
			modulesInventoryLoadingSinceUtc = value;
			return;
		}
		pluginsInventoryLoadingSinceUtc = value;
	}

	private string GetInventoryLoadingOrEmptyText(bool modulesView, bool allowEmptyFallback)
	{
		DateTime utcNow = DateTime.UtcNow;
		DateTime inventoryLoadingSinceUtc = GetInventoryLoadingSinceUtc(modulesView);
		if (inventoryLoadingSinceUtc == DateTime.MaxValue)
		{
			return modulesView ? "No live modules detected." : "No live plugins detected.";
		}
		if (inventoryLoadingSinceUtc == DateTime.MinValue)
		{
			SetInventoryLoadingSinceUtc(modulesView, utcNow);
			return "LOADING";
		}
		if (!allowEmptyFallback || (utcNow - inventoryLoadingSinceUtc).TotalMilliseconds < InventoryLoadingGraceMs)
		{
			return "LOADING";
		}
		SetInventoryLoadingSinceUtc(modulesView, DateTime.MaxValue);
		return modulesView ? "No live modules detected." : "No live plugins detected.";
	}

	private bool ShouldRefreshInventoryLoadingState()
	{
		if (base.IsDisposed || connectAttemptInFlight || shellDisconnected || latestSnapshot == null || !latestSnapshot.Connected)
		{
			return false;
		}
		if (IsInventorySnapshotLive(latestSnapshot, Volatile.Read(ref sessionEpoch), inventoryShowsModules))
		{
			return false;
		}
		DateTime inventoryLoadingSinceUtc = GetInventoryLoadingSinceUtc(inventoryShowsModules);
		return inventoryLoadingSinceUtc != DateTime.MinValue && inventoryLoadingSinceUtc != DateTime.MaxValue && HasConnectedTelemetry(latestSnapshot) && (DateTime.UtcNow - inventoryLoadingSinceUtc).TotalMilliseconds >= InventoryLoadingGraceMs;
	}

	private static bool IsInventorySnapshotLive(TelemetrySnapshot? snapshot, int currentSessionEpoch, bool modulesView)
	{
		if (snapshot == null || !snapshot.Connected || snapshot.SessionEpoch != currentSessionEpoch)
		{
			return false;
		}
		int inventorySessionEpoch = modulesView ? snapshot.ModulesSessionEpoch : snapshot.PluginsSessionEpoch;
		return inventorySessionEpoch == currentSessionEpoch;
	}

	private void RefreshInventoryListFromSnapshot()
	{
		if (connectAttemptInFlight)
		{
			SetLeftSignInText("INVENTORY");
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText("CONNECTING")
			});
			return;
		}
		if (latestSnapshot == null)
		{
			SetLeftSignInText("INVENTORY");
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText(IsSessionConnected() ? "LOADING" : (inventoryShowsModules ? "Connect to load modules." : "Connect to load plugins."))
			});
			if (IsSessionConnected())
			{
				SetInventoryLoadingSinceUtc(inventoryShowsModules, DateTime.UtcNow);
			}
			else
			{
				ResetInventoryLoadingState();
			}
			return;
		}
		bool isLiveSnapshot = IsInventorySnapshotLive(latestSnapshot, Volatile.Read(ref sessionEpoch), inventoryShowsModules);
		IEnumerable<string> enumerable = inventoryShowsModules ? latestSnapshot.Modules : latestSnapshot.Plugins;
		SetLeftSignInText("INVENTORY");
		if (!isLiveSnapshot)
		{
			if (!latestSnapshot.Connected)
			{
				inventoryList.SetItems(new string[1]
				{
					TranslateTerminalText(inventoryShowsModules ? "Connect to load modules." : "Connect to load plugins.")
				});
				return;
			}
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText(GetInventoryLoadingOrEmptyText(inventoryShowsModules, HasConnectedTelemetry(latestSnapshot)))
			});
			return;
		}
		ResetInventoryLoadingState();
		if (!enumerable.Any())
		{
			inventoryList.SetItems(new string[1]
			{
				TranslateTerminalText(inventoryShowsModules ? "No live modules detected." : "No live plugins detected.")
			});
			return;
		}
		inventoryList.SetItems(FormatInventoryEntries(enumerable).Take(256));
	}

	private void RecordTransferActivity(string command, string state, string? detail = null)
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
			BeginInvoke(new Action<string, string, string?>(RecordTransferActivity), command, state, detail);
			return;
		}
		string text = AbbreviateTransferCommand(command);
		TransferQueueEntry? transferQueueEntry = transferQueueEntries.FirstOrDefault((TransferQueueEntry item) => string.Equals(item.CommandKey, text, StringComparison.Ordinal));
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
		if (detail != null)
		{
			transferQueueEntry.Detail = detail;
		}
		if (transferQueueEntries.Count > 10)
		{
			transferQueueEntries.RemoveRange(10, transferQueueEntries.Count - 10);
		}
		RefreshTransferQueueDisplay();
	}

	private void UpdateActiveTransferProgress(double? percent, string? detail = null)
	{
		string? text = activeTransferCommand;
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
		TransferQueueEntry? transferQueueEntry = transferQueueEntries.FirstOrDefault((TransferQueueEntry item) => string.Equals(item.CommandKey, text2, StringComparison.Ordinal));
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

	private void HandleFtpProgress(FtpProgress progress, string detail, FtpTrafficDirection direction, FtpProgressByteTracker trafficTracker)
	{
		RecordFtpTrafficBytes(direction, trafficTracker.Observe(progress.TransferredBytes));
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
		return text.StartsWith("ftp get ", StringComparison.Ordinal) || text.StartsWith("ftp put ", StringComparison.Ordinal) || text.StartsWith("ftp list ", StringComparison.Ordinal) || text.StartsWith("nand dump", StringComparison.Ordinal) || text.StartsWith("xell kv export", StringComparison.Ordinal) || text.StartsWith("save extract", StringComparison.Ordinal) || text.StartsWith("save inject", StringComparison.Ordinal) || text.StartsWith("xbdm xex dump", StringComparison.Ordinal) || text.Contains(" dump ", StringComparison.Ordinal) || text.EndsWith(" dump", StringComparison.Ordinal);
	}

	private static bool IsCancelCommand(string command)
	{
		return command.Equals("cancel", StringComparison.OrdinalIgnoreCase) || command.Equals("/cancel", StringComparison.OrdinalIgnoreCase);
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
					? InterpolateColor(PrimaryTextColor, AccentGreen, 0.42f + amount * 0.34f)
					: InterpolateColor(AccentDim, PrimaryTextColor, 0.36f + amount * 0.64f);
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
		if (e.KeyCode == Keys.Escape && (commandPaletteOpen || commandHistorySearchOpen))
		{
			CloseCommandPickers(restoreDraft: true);
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Escape && suggestionList.Visible)
		{
			ToggleSuggestions(visible: false);
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.Control && e.KeyCode == Keys.K)
		{
			OpenCommandPalette();
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.Control && e.KeyCode == Keys.R)
		{
			OpenCommandHistorySearch();
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
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
		if (!commandPaletteOpen && !commandHistorySearchOpen && e.KeyCode == Keys.Up)
		{
			NavigateCommandHistory(-1);
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (!commandPaletteOpen && !commandHistorySearchOpen && e.KeyCode == Keys.Down)
		{
			NavigateCommandHistory(1);
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
		SubmitCommandInput();
	}

	private bool SubmitCommandInput()
	{
		if (commandInFlight || fileTransferInFlight || connectAttemptInFlight)
		{
			return false;
		}
		string text = (commandPaletteOpen || commandHistorySearchOpen) && suggestionList.SelectedItem is string selectedPaletteItem
			? selectedPaletteItem.Trim()
			: commandInput.Text.Trim();
		if (text.Length == 0)
		{
			UpdateCommandSubmitButtonState();
			return false;
		}
		suppressSuggestionRefresh = true;
		commandInput.Clear();
		suppressSuggestionRefresh = false;
		CloseCommandPickers(restoreDraft: false);
		_ = ExecuteCommandAsync(text);
		UpdateCommandSubmitButtonState();
		return true;
	}

	private void HandleCommandInputTextChanged()
	{
		if (suppressSuggestionRefresh)
		{
			return;
		}
		commandHistoryCursor = -1;
		if (!commandPaletteOpen && !commandHistorySearchOpen)
		{
			commandHistoryDraft = commandInput.Text;
		}
		UpdateCommandInputPresentation(scrollToCaret: true);
		UpdateCommandSubmitButtonState();
		RefreshSuggestions();
	}

	private void RefreshSuggestions()
	{
		string text = commandInput.Text.TrimStart();
		if (commandHistorySearchOpen)
		{
			List<string> historyMatches = XeCliTerminalPaletteCatalog.GetRegisteredSuggestions(commandHistory
				.AsEnumerable()
				.Reverse()
				.Where(command => text.Length == 0 || command.Contains(text, StringComparison.OrdinalIgnoreCase)))
				.Take(18)
				.ToList();
			suggestionList.SetItems(historyMatches);
			bool historyVisible = historyMatches.Count > 0;
			if (historyVisible && suggestionList.SelectedIndex != 0)
			{
				suggestionList.SelectedIndex = 0;
			}
			ToggleSuggestions(historyVisible);
			return;
		}
		if (commandPaletteOpen)
		{
			List<string> paletteMatches = XeCliTerminalPaletteCatalog.GetRegisteredSuggestions(XeCliTerminalPaletteCatalog.QuickActions)
				.Where(command => text.Length == 0 || command.Contains(text, StringComparison.OrdinalIgnoreCase))
				.Take(18)
				.ToList();
			suggestionList.SetItems(paletteMatches);
			bool paletteVisible = paletteMatches.Count > 0;
			if (paletteVisible && suggestionList.SelectedIndex != 0)
			{
				suggestionList.SelectedIndex = 0;
			}
			ToggleSuggestions(paletteVisible);
			return;
		}
		if (text.Length == 0)
		{
			ToggleSuggestions(visible: false);
			return;
		}
		IEnumerable<string> registeredSuggestions = XeCliTerminalPaletteCatalog.GetRegisteredSuggestions(XeCliTerminalPaletteCatalog.QuickActions
			.Concat(commandHistory.AsEnumerable().Reverse())
		);
		List<string> list = registeredSuggestions
			.Where((string s) => s.StartsWith(text, StringComparison.OrdinalIgnoreCase))
			.Take(18)
			.ToList();
		if (list.Count == 0 && text.Length >= 3)
		{
			list = registeredSuggestions
				.Where((string s) => s.Contains(text, StringComparison.OrdinalIgnoreCase))
				.Take(18)
				.ToList();
		}
		list = list
			.Where((string s) => !string.Equals(s.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
			.ToList();
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
		Padding hostPadding = visible ? new Padding(3, 14, 3, 3) : Padding.Empty;
		float num = visible ? suggestionList.PreferredHostHeight + hostPadding.Vertical : 0f;
		suggestionHost.SuspendLayout();
		suggestionList.Visible = visible;
		suggestionHost.Visible = visible;
		suggestionHost.BackColor = TerminalBackground;
		suggestionHost.Padding = hostPadding;
		suggestionList.Height = Math.Max(0, suggestionList.PreferredHostHeight);
		if (suggestionRowStyle != null)
		{
			if (Math.Abs(suggestionRowStyle.Height - num) > float.Epsilon)
			{
				suggestionRowStyle.Height = num;
			}
		}
		suggestionHost.ResumeLayout(performLayout: true);
		suggestionHost.Parent?.PerformLayout();
		suggestionHost.Invalidate(invalidateChildren: true);
	}

	private void HideSuggestionsIfInputInactive()
	{
		if (base.IsDisposed)
		{
			return;
		}
		BeginInvoke((Action)delegate
		{
			HideSuggestionsIfInputInactiveCore();
		});
	}

	private void HideSuggestionsIfInputInactiveCore()
	{
		if (base.IsDisposed || commandInput.Focused || suggestionList.Focused || commandInputHost.ContainsFocus)
		{
			return;
		}
		if (commandPaletteOpen || commandHistorySearchOpen)
		{
			CloseCommandPickers(restoreDraft: true);
			return;
		}
		ToggleSuggestions(visible: false);
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
			UpdateCommandInputPresentation(scrollToCaret: true);
			commandInput.Focus();
			CloseCommandPickers(restoreDraft: false);
		}
	}


	private void OpenCommandPalette()
	{
		CaptureCommandInputDraft();
		commandPaletteOpen = true;
		commandHistorySearchOpen = false;
		commandHistoryCursor = -1;
		commandInput.PlaceholderText = TranslateTerminalText("Quick actions: type to filter commands...");
		commandInput.RefreshPlaceholderRendering();
		suppressSuggestionRefresh = true;
		commandInput.Clear();
		suppressSuggestionRefresh = false;
		commandInput.Focus();
		UpdateCommandInputPresentation(scrollToCaret: false);
		RefreshSuggestions();
	}

	private void OpenCommandHistorySearch()
	{
		CaptureCommandInputDraft();
		commandHistorySearchOpen = true;
		commandPaletteOpen = false;
		commandHistoryCursor = -1;
		commandInput.PlaceholderText = TranslateTerminalText("History search: type to filter prior commands...");
		commandInput.RefreshPlaceholderRendering();
		suppressSuggestionRefresh = true;
		commandInput.Clear();
		suppressSuggestionRefresh = false;
		commandInput.Focus();
		UpdateCommandInputPresentation(scrollToCaret: false);
		RefreshSuggestions();
	}

	private void CloseCommandPickers(bool restoreDraft)
	{
		commandPaletteOpen = false;
		commandHistorySearchOpen = false;
		commandInput.PlaceholderText = TranslateTerminalText(commandInputPlaceholderSource);
		commandInput.RefreshPlaceholderRendering();
		commandHistoryCursor = -1;
		if (restoreDraft)
		{
			RestoreCommandInputDraft();
		}
		UpdateCommandInputPresentation(scrollToCaret: false);
		ToggleSuggestions(visible: false);
	}

	private void CaptureCommandInputDraft()
	{
		commandHistoryDraft = commandInput.Text;
	}

	private void RestoreCommandInputDraft()
	{
		suppressSuggestionRefresh = true;
		commandInput.Text = commandHistoryDraft;
		commandInput.SelectionStart = commandInput.Text.Length;
		suppressSuggestionRefresh = false;
		UpdateCommandInputPresentation(scrollToCaret: true);
	}

	private static string BuildCommandEntryShortcutText()
	{
		return "Ctrl+K opens quick actions. Ctrl+R searches command history. Esc closes the picker and restores the draft. Up/Down browse history. Enter executes.";
	}

	private static bool ShouldAnnounceConnectedTransition(bool wasSessionConnected)
	{
		return !wasSessionConnected;
	}

	private void AddCommandHistory(string command)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return;
		}
		if (commandHistory.Count > 0 && string.Equals(commandHistory[commandHistory.Count - 1], command, StringComparison.OrdinalIgnoreCase))
		{
			commandHistoryCursor = -1;
			return;
		}
		commandHistory.Add(command);
		if (commandHistory.Count > 80)
		{
			commandHistory.RemoveAt(0);
		}
		commandHistoryCursor = -1;
		commandHistoryDraft = string.Empty;
	}

	private void NavigateCommandHistory(int direction)
	{
		if (commandHistory.Count == 0)
		{
			return;
		}
		if (commandHistoryCursor < 0)
		{
			commandHistoryDraft = commandInput.Text;
			commandHistoryCursor = commandHistory.Count;
		}
		commandHistoryCursor = Math.Clamp(commandHistoryCursor + direction, 0, commandHistory.Count);
		suppressSuggestionRefresh = true;
		commandInput.Text = commandHistoryCursor == commandHistory.Count ? commandHistoryDraft : commandHistory[commandHistoryCursor];
		commandInput.SelectionStart = commandInput.Text.Length;
		suppressSuggestionRefresh = false;
	}

	private void SetRemotePaneMode(bool showQueue)
	{
		remotePaneShowsQueue = showQueue;
		if (remotePathRowStyle != null)
		{
			remotePathRowStyle.Height = showQueue ? 0f : 48f;
		}
		ApplyToggleButtonState(ftpTabButton, !showQueue);
		ApplyToggleButtonState(queueTabButton, showQueue);
		remoteFileList.Visible = !showQueue;
		transferQueueList.Visible = showQueue;
		remotePathHostPanel.Visible = !showQueue;
		remoteUpButton.Visible = !showQueue;
		remotePathLabel.Text = showQueue ? "QUEUE / TRANSFERS" : FormatRemotePathLabel(remoteCurrentPath);
		if (showQueue)
		{
			RefreshTransferQueueDisplay();
		}
		remoteContentHost.Parent?.PerformLayout();
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
		RefreshFooterOperationalSummary();
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
		object? lockObject = pendingTerminalLock;
		if (lockObject != null)
		{
			lock (lockObject)
			{
				pendingTerminalLines?.Clear();
				terminalFlushScheduled = false;
			}
		}
		else
		{
			pendingTerminalLines?.Clear();
			terminalFlushScheduled = false;
		}
		terminalFlushTimer?.Stop();
		if (terminalOutput != null && !terminalOutput.IsDisposed)
		{
			terminalOutput.Clear();
		}
	}

	private void ClearVisibleTerminalSurface()
	{
		ClearTerminalWorkspace();
		CloseCommandPickers(restoreDraft: false);
		suppressSuggestionRefresh = true;
		try
		{
			commandInput.Clear();
			commandHistoryCursor = -1;
			commandHistoryDraft = string.Empty;
			suggestionList.SetItems(Array.Empty<string>());
		}
		finally
		{
			suppressSuggestionRefresh = false;
		}
		commandInput.Focus();
		UpdateCommandInputPresentation(scrollToCaret: false);
	}

	private async Task<bool> ExecuteCommandAsync(string rawCommand)
	{
		string command = rawCommand.Trim();
		if (IsCancelCommand(command))
		{
			return CancelActiveOperation();
		}
		if (commandInFlight)
		{
			AppendSystemLine("A command is already running. Wait for completion.", Color.Gold);
			return false;
		}
		if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) || command.Equals("quit", StringComparison.OrdinalIgnoreCase))
		{
			Close();
			return true;
		}
		string historyCommand = command.StartsWith("/", StringComparison.Ordinal) ? command[1..] : command;
		AddCommandHistory(historyCommand);
		if (command.Equals("clear", StringComparison.OrdinalIgnoreCase) || command.Equals("/clear", StringComparison.OrdinalIgnoreCase))
		{
			ClearVisibleTerminalSurface();
			return true;
		}
		footerOperationStatusOverride = null;
		Interlocked.Increment(ref telemetryOperationEpoch);
		commandInFlight = true;
		UpdateCommandSubmitButtonState();
		RecordTransferActivity(command, "queued");
		AppendCommandLine(command);
		bool flag = false;
		try
		{
			flag = await RunCliProcessAsync(command);
		}
		catch (Exception)
		{
			RecordTransferActivity(command, "error");
			AppendSystemLine("Operation failed", FailureStateColor);
		}
		finally
		{
			commandInFlight = false;
			footerOperationStatusOverride = flag ? "COMMAND COMPLETE" : "COMMAND FAILED";
			UpdateCommandSubmitButtonState();
			UpdateLiveHints();
		}
		return flag;
	}

	private async Task ExecuteScreenshotCaptureAsync()
	{
		string path = GetDefaultScreenshotPath();
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
		}
		catch (Exception ex)
		{
			ShowScreenshotFailure("Capture folder is unavailable.");
			AppendSystemLine("Screenshot failed: " + ex.Message, FailureStateColor);
			return;
		}

		bool succeeded = await ExecuteCommandAsync("screenshot --out \"" + path + "\"");
		if (!succeeded)
		{
			ShowScreenshotFailure("The capture command failed. Review the terminal output.");
			return;
		}
		if (!TryLoadScreenshotPreview(path, out Image? preview, out string validationError) || preview == null)
		{
			ShowScreenshotFailure("Capture completed without a usable image.");
			AppendSystemLine("Screenshot validation failed: " + validationError, FailureStateColor);
			return;
		}

		ShowScreenshotSuccess(path, preview);
	}

	private void ShowScreenshotSuccess(string path, Image preview)
	{
		latestScreenshotPath = path;
		screenshotNotificationIsFailure = false;
		screenshotNotificationDetailSource = path;
		ReplaceScreenshotNotificationImage(preview);
		screenshotNotificationThumbnail.AccessibleName = "Preview screenshot";
		screenshotNotificationPreviewButton.Visible = true;
		screenshotNotificationOpenButton.Visible = true;
		screenshotNotificationFolderButton.Visible = true;
		screenshotNotificationThumbnail.Cursor = Cursors.Hand;
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationDetailLabel, path);
		RefreshScreenshotNotificationPresentation();
		ShowScreenshotNotification();
		SetFooterOperationStatus("SCREENSHOT SAVED");
		ApplyScreenshotCompletionPreference();
	}

	private void ApplyScreenshotCompletionPreference()
	{
		CliConfig.TryLoad(out CliConfig config);
		switch (CliPreferences.NormalizeScreenshotAction(config.ScreenshotAfterCapture))
		{
			case "preview":
				OpenScreenshotPreview();
				break;
			case "folder":
				RevealLatestScreenshotInExplorer();
				break;
		}
	}

	private void ShowScreenshotFailure(string detail)
	{
		latestScreenshotPath = null;
		screenshotNotificationIsFailure = true;
		screenshotNotificationDetailSource = detail;
		ClearScreenshotNotificationImage();
		screenshotNotificationThumbnail.AccessibleName = "Screenshot failed";
		screenshotNotificationPreviewButton.Visible = false;
		screenshotNotificationOpenButton.Visible = false;
		screenshotNotificationFolderButton.Visible = false;
		screenshotNotificationThumbnail.Cursor = Cursors.Default;
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationThumbnail, string.Empty);
		RefreshScreenshotNotificationPresentation();
		ShowScreenshotNotification();
		SetFooterOperationStatus("SCREENSHOT FAILED");
	}

	private void RefreshScreenshotNotificationPresentation()
	{
		if (screenshotNotificationPanel.IsDisposed)
		{
			return;
		}
		screenshotNotificationAccentColor = screenshotNotificationIsFailure ? FailureStateColor : AccentGreen;
		screenshotNotificationTitleLabel.Text = TranslateTerminalText(screenshotNotificationIsFailure ? "Screenshot failed" : "Screenshot saved");
		screenshotNotificationTitleLabel.ForeColor = screenshotNotificationAccentColor;
		screenshotNotificationDetailLabel.Text = screenshotNotificationIsFailure
			? TranslateTerminalText(screenshotNotificationDetailSource)
			: screenshotNotificationDetailSource;
		screenshotNotificationDetailLabel.ForeColor = screenshotNotificationIsFailure ? FailureStateColor : PrimaryTextColor;
		SetControlText(screenshotNotificationPreviewButton, TranslateTerminalText("PREVIEW"));
		SetControlText(screenshotNotificationOpenButton, TranslateTerminalText("OPEN"));
		SetControlText(screenshotNotificationFolderButton, TranslateTerminalText("FOLDER"));
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationPreviewButton, TranslateTerminalText("Preview screenshot in XeCLI"));
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationOpenButton, TranslateTerminalText("Open screenshot with the default app"));
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationFolderButton, TranslateTerminalText("Show screenshot in File Explorer"));
		if (!screenshotNotificationIsFailure)
		{
			screenshotNotificationToolTip.SetToolTip(screenshotNotificationThumbnail, TranslateTerminalText("Preview screenshot in XeCLI"));
		}
		screenshotNotificationToolTip.SetToolTip(screenshotNotificationDetailLabel,
			screenshotNotificationIsFailure ? TranslateTerminalText(screenshotNotificationDetailSource) : latestScreenshotPath ?? string.Empty);
		screenshotNotificationPanel.Invalidate();
		screenshotNotificationThumbnail.Invalidate();
	}

	private void ShowScreenshotNotification()
	{
		if (base.IsDisposed || screenshotNotificationPanel.IsDisposed)
		{
			return;
		}
		PositionScreenshotNotification();
		screenshotNotificationPanel.Visible = true;
		screenshotNotificationPanel.BringToFront();
		screenshotNotificationPanel.Invalidate();
		screenshotNotificationTimer.Stop();
		screenshotNotificationTimer.Start();
	}

	private void HideScreenshotNotification()
	{
		if (base.IsDisposed || screenshotNotificationPanel.IsDisposed)
		{
			return;
		}
		screenshotNotificationTimer.Stop();
		screenshotNotificationPanel.Visible = false;
	}

	private void ReplaceScreenshotNotificationImage(Image image)
	{
		ClearScreenshotNotificationImage();
		screenshotNotificationImage = image;
		screenshotNotificationThumbnail.Image = screenshotNotificationImage;
	}

	private void ClearScreenshotNotificationImage()
	{
		screenshotNotificationThumbnail.Image = null;
		screenshotNotificationImage?.Dispose();
		screenshotNotificationImage = null;
	}

	private void DrawScreenshotFailureGlyph(Graphics graphics, Rectangle bounds)
	{
		int diameter = Math.Max(24, Math.Min(bounds.Width, bounds.Height) - 28);
		Rectangle iconBounds = new Rectangle(
			bounds.Left + (bounds.Width - diameter) / 2,
			bounds.Top + (bounds.Height - diameter) / 2,
			diameter,
			diameter);
		GraphicsState state = graphics.Save();
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using SolidBrush fill = new SolidBrush(InterpolateColor(TerminalBackground, FailureStateColor, 0.13f));
		using Pen ring = new Pen(Color.FromArgb(230, FailureStateColor), 2f);
		using Pen cross = new Pen(FailureStateColor, Math.Max(3f, diameter / 15f))
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		};
		graphics.FillEllipse(fill, iconBounds);
		graphics.DrawEllipse(ring, iconBounds);
		int inset = Math.Max(8, diameter * 3 / 10);
		graphics.DrawLine(cross, iconBounds.Left + inset, iconBounds.Top + inset, iconBounds.Right - inset, iconBounds.Bottom - inset);
		graphics.DrawLine(cross, iconBounds.Right - inset, iconBounds.Top + inset, iconBounds.Left + inset, iconBounds.Bottom - inset);
		graphics.Restore(state);
	}

	private void OpenScreenshotPreview()
	{
		if (!TryGetValidatedLatestScreenshot(out string path, out Image? preview) || preview == null)
		{
			return;
		}
		try
		{
			screenshotNotificationTimer.Stop();
			if (screenshotPreviewForm != null && !screenshotPreviewForm.IsDisposed)
			{
				screenshotPreviewForm.Close();
				screenshotPreviewForm.Dispose();
			}
			screenshotPreviewForm = new ScreenshotPreviewForm(path, preview, OpenLatestScreenshotExternally, RevealLatestScreenshotInExplorer);
			screenshotPreviewForm.FormClosed += delegate
			{
				screenshotPreviewForm = null;
			};
			screenshotPreviewForm.Show(this);
			screenshotPreviewForm.Activate();
		}
		finally
		{
			preview.Dispose();
		}
	}

	private void OpenLatestScreenshotExternally()
	{
		if (!TryGetValidatedLatestScreenshot(out string path, out Image? preview))
		{
			return;
		}
		preview?.Dispose();
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowStatusToast(TranslateTerminalText("Screenshot open failed."));
			AppendSystemLine("Screenshot open failed: " + ex.Message, FailureStateColor);
		}
	}

	private void RevealLatestScreenshotInExplorer()
	{
		if (!TryGetValidatedLatestScreenshot(out string path, out Image? preview))
		{
			return;
		}
		preview?.Dispose();
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "/select,\"" + path + "\"",
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowStatusToast(TranslateTerminalText("Screenshot folder could not be opened."));
			AppendSystemLine("Screenshot folder open failed: " + ex.Message, FailureStateColor);
		}
	}

	private bool TryGetValidatedLatestScreenshot(out string path, out Image? preview)
	{
		path = latestScreenshotPath ?? string.Empty;
		string validationError = "No saved screenshot is available.";
		preview = null;
		if (string.IsNullOrWhiteSpace(path) || !TryLoadScreenshotPreview(path, out preview, out validationError))
		{
			preview = null;
			ShowScreenshotFailure("The saved screenshot is no longer available.");
			if (!string.IsNullOrWhiteSpace(validationError))
			{
				AppendSystemLine("Screenshot validation failed: " + validationError, FailureStateColor);
			}
			return false;
		}
		return true;
	}

	private static bool TryLoadScreenshotPreview(string path, out Image? preview, out string error)
	{
		preview = null;
		error = string.Empty;
		try
		{
			FileInfo file = new FileInfo(path);
			if (!file.Exists)
			{
				error = "The output file was not created.";
				return false;
			}
			if (file.Length <= 0)
			{
				error = "The output file is empty.";
				return false;
			}
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
			using Image source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
			if (source.Width <= 0 || source.Height <= 0)
			{
				error = "The output image has invalid dimensions.";
				return false;
			}
			preview = new Bitmap(source);
			return true;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException)
		{
			error = "The output file is not a readable image: " + ex.Message;
			preview?.Dispose();
			preview = null;
			return false;
		}
	}

	private string GetDefaultScreenshotPath()
	{
		CliConfig.TryLoad(out CliConfig config);
		string text = string.IsNullOrWhiteSpace(config.ScreenshotDirectory)
			? CliPreferences.GetDefaultScreenshotDirectory()
			: Path.GetFullPath(config.ScreenshotDirectory);
		string text2 = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
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
			Task task = StreamProcessLinesAsync(process.StandardOutput, isError: false);
			Task task2 = StreamProcessLinesAsync(process.StandardError, isError: true);
			Task task3 = process.WaitForExitAsync();
			await Task.WhenAll(task, task2, task3).ConfigureAwait(false);
			if (cancellationTokenSource.IsCancellationRequested)
			{
				RecordTransferActivity(command, "cancelled");
				AppendSystemLine("Command cancelled.", WarningColor);
				AppendPromptLine();
				return false;
			}
			if (process.ExitCode != 0)
			{
				RecordTransferActivity(command, "failed", "Exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture));
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
			AppendSystemLine("Command cancelled.", WarningColor);
			AppendPromptLine();
			return false;
		}
		finally
		{
			activeCommandProcess = null;
			CancelAndDispose(ref commandCts);
		}
	}

	private async Task StreamProcessLinesAsync(StreamReader reader, bool isError)
	{
		while (true)
		{
			string? text = await reader.ReadLineAsync().ConfigureAwait(false);
			if (text == null)
			{
				break;
			}
			AppendTerminalLine(text, isError ? FailureStateColor : PrimaryTextColor);
		}
	}

	private void AppendCommandLine(string command)
	{
		AppendTerminalLine(string.Empty, PrimaryTextColor);
		AppendTerminalLine("> " + command, AccentGreen);
	}

	private void AppendPromptLine()
	{
		AppendTerminalLine(">", Color.FromArgb(214, AccentGreen));
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
		bool shouldAutoScroll = IsTerminalOutputAtBottom();
		int firstVisibleLine = GetTerminalFirstVisibleLine();
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
		int trimmedLines = TrimTerminalOutput();
		if (shouldAutoScroll)
		{
			terminalOutput.ScrollToCaret();
		}
		else
		{
			ScrollTerminalOutputToFirstVisibleLine(Math.Max(0, firstVisibleLine - trimmedLines));
		}
		terminalScrollIndicator.Invalidate();
	}

	private int TrimTerminalOutput()
	{
		int num = terminalOutput.TextLength - MaxTerminalCharacters;
		if (num <= 0)
		{
			return 0;
		}
		int num2 = terminalOutput.Find("\n", num, RichTextBoxFinds.None);
		int num3 = (num2 >= 0) ? (num2 + 1) : num;
		int removeLength = Math.Min(num3, terminalOutput.TextLength);
		int removedLines = terminalOutput.GetLineFromCharIndex(removeLength);
		bool wasReadOnly = terminalOutput.ReadOnly;
		int selectionStart = terminalOutput.SelectionStart;
		int selectionLength = terminalOutput.SelectionLength;
		terminalOutput.ReadOnly = false;
		try
		{
			terminalOutput.Select(0, removeLength);
			terminalOutput.SelectedText = string.Empty;
		}
		finally
		{
			terminalOutput.ReadOnly = wasReadOnly;
		}
		int selectionEnd = Math.Min(selectionStart + selectionLength, terminalOutput.TextLength + removeLength);
		int restoredSelectionStart = Math.Max(0, selectionStart - removeLength);
		int restoredSelectionLength = Math.Max(0, selectionEnd - Math.Max(selectionStart, removeLength));
		if (restoredSelectionStart <= terminalOutput.TextLength)
		{
			terminalOutput.Select(restoredSelectionStart, Math.Min(restoredSelectionLength, terminalOutput.TextLength - restoredSelectionStart));
		}
		return removedLines;
	}

	private bool IsMouseOverTerminalOutput()
	{
		if (base.IsDisposed || !terminalOutput.IsHandleCreated || terminalOutput.IsDisposed || !terminalOutput.Visible)
		{
			return false;
		}
		Point cursor = Cursor.Position;
		if (terminalOutput.RectangleToScreen(terminalOutput.ClientRectangle).Contains(cursor))
		{
			return true;
		}
		return terminalScrollIndicator.Visible && terminalScrollIndicator.RectangleToScreen(terminalScrollIndicator.ClientRectangle).Contains(cursor);
	}

	private void ScrollTerminalOutputByWheelDelta(int delta)
	{
		if (delta == 0)
		{
			return;
		}
		int notches = Math.Abs(delta) >= MouseWheelDelta ? delta / MouseWheelDelta : Math.Sign(delta);
		int linesPerNotch = SystemInformation.MouseWheelScrollLines;
		if (linesPerNotch <= 0 || linesPerNotch == int.MaxValue)
		{
			linesPerNotch = Math.Max(1, GetTerminalVisibleLineCount() - 1);
		}
		ScrollTerminalOutputByLines(-notches * Math.Max(1, linesPerNotch));
	}

	private void ScrollTerminalOutputByLines(int lineDelta)
	{
		if (lineDelta == 0 || !terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return;
		}
		SendMessage(terminalOutput.Handle, EmLineScroll, 0, lineDelta);
		terminalScrollIndicator.Invalidate();
		HideTerminalOutputCaret();
	}

	private void ScrollTerminalOutputToFirstVisibleLine(int line)
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return;
		}
		int maxFirstLine = Math.Max(0, GetTerminalLineCount() - GetTerminalVisibleLineCount());
		int targetLine = Math.Clamp(line, 0, maxFirstLine);
		int delta = targetLine - GetTerminalFirstVisibleLine();
		if (delta != 0)
		{
			SendMessage(terminalOutput.Handle, EmLineScroll, 0, delta);
		}
		terminalScrollIndicator.Invalidate();
		HideTerminalOutputCaret();
	}

	private bool IsTerminalOutputAtBottom()
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return true;
		}
		int lineCount = GetTerminalLineCount();
		int visibleLines = GetTerminalVisibleLineCount();
		if (lineCount <= visibleLines)
		{
			return true;
		}
		return GetTerminalFirstVisibleLine() >= Math.Max(0, lineCount - visibleLines - 1);
	}

	private int GetTerminalLineCount()
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed || terminalOutput.TextLength == 0)
		{
			return 1;
		}
		return Math.Max(1, terminalOutput.GetLineFromCharIndex(terminalOutput.TextLength) + 1);
	}

	private int GetTerminalVisibleLineCount()
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return 1;
		}
		int lineHeight = Math.Max(1, terminalOutput.Font.Height + 1);
		return Math.Max(1, terminalOutput.ClientSize.Height / lineHeight);
	}

	private int GetTerminalFirstVisibleLine()
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return 0;
		}
		return Math.Max(0, SendMessage(terminalOutput.Handle, EmGetFirstVisibleLine, 0, 0));
	}

	private async Task PollTelemetrySafeAsync(bool forceHeavyRefresh = false)
	{
		if (!telemetryEnabled || telemetryPollInFlight || shellDisconnected || base.IsDisposed || commandInFlight || connectAttemptInFlight || fileTransferInFlight)
		{
			return;
		}
		int num = Volatile.Read(ref sessionEpoch);
		int operationEpoch = Volatile.Read(ref telemetryOperationEpoch);
		telemetryPollInFlight = true;
		try
		{
			TelemetrySnapshot telemetrySnapshot = await Task.Run(() => TryReadTelemetryAsync(forceHeavyRefresh), formLifetimeCts.Token);
			if (base.IsDisposed
				|| num != Volatile.Read(ref sessionEpoch)
				|| operationEpoch != Volatile.Read(ref telemetryOperationEpoch)
				|| commandInFlight
				|| fileTransferInFlight)
			{
				return;
			}
			ApplyTelemetry(telemetrySnapshot);
			UpdateHeavyTelemetryRetrySchedule(telemetrySnapshot, forceHeavyRefresh);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			telemetryPollInFlight = false;
		}
	}

	private async Task<(bool Success, CliErrorEnvelope? Failure)> TryProbeConnectionAsync(CancellationToken externalCancellationToken)
	{
		int num = Math.Clamp(connectionTimeoutMs, 3000, 10000);
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
			return (false, ConnectionFailureModel.BuildXbdmError("XBDM connection timed out.", FormatCurrentTarget()));
		}
		try
		{
			using XbdmClient xbdmClient = await task;
			return (xbdmClient != null, null);
		}
		catch (Exception ex)
		{
			if (ConnectionFailureModel.TryBuildXbdmError(ex, "XBDM connection failed.", FormatCurrentTarget(), out CliErrorEnvelope error))
			{
				return (false, error);
			}
			return (false, ConnectionFailureModel.BuildXbdmError("XBDM connection failed.", FormatCurrentTarget()));
		}
	}

	private async Task HydrateConnectedSessionAsync(int expectedSessionEpoch)
	{
		if (base.IsDisposed || shellDisconnected || expectedSessionEpoch != Volatile.Read(ref sessionEpoch))
		{
			return;
		}
		if (!telemetryEnabled)
		{
			_ = PrimeRemoteBrowserAfterConnectAsync();
			return;
		}
		try
		{
			await PollTelemetrySafeAsync(forceHeavyRefresh: true);
		}
		catch (Exception)
		{
			AppendSystemLine("Telemetry unavailable", AccentDim);
			ShowStatusToast(TranslateTerminalText("Telemetry unavailable."));
		}
		if (!base.IsDisposed && !shellDisconnected && expectedSessionEpoch == Volatile.Read(ref sessionEpoch) && latestSnapshot != null && latestSnapshot.Connected)
		{
			_ = PrimeRemoteBrowserAfterConnectAsync();
		}
	}

	private async Task<TelemetrySnapshot> TryReadTelemetryAsync(bool forceHeavyRefresh)
	{
		CliConfig cliConfig = CliConfig.Load();
		(int ftpPort, string ftpUser, string ftpPass, _) = GetTerminalFtpSettings(cliConfig);
		bool flag = forceHeavyRefresh;
		TelemetrySnapshot telemetrySnapshot = new TelemetrySnapshot
		{
			SessionEpoch = Volatile.Read(ref sessionEpoch),
			Connected = false,
			FtpPort = ftpPort,
			FtpUser = ftpUser
		};
		using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(formLifetimeCts.Token);
		int telemetryTimeoutMs = ComputeTelemetryBudgetMs(connectionTimeoutMs, flag);
		cancellationTokenSource.CancelAfter(telemetryTimeoutMs);
		try
		{
			XbdmConnectionOptions xbdmConnectionOptions = new XbdmConnectionOptions
			{
				Host = currentTargetIp,
				Port = currentTargetPort,
				TimeoutMs = connectionTimeoutMs
			};
			using XbdmClient client = await XbdmClient.ConnectAsync(xbdmConnectionOptions, cancellationTokenSource.Token);
			telemetrySnapshot.Connected = true;
			FtpEndpointResolution ftpEndpoint = await FtpEndpointHelpers.ResolveStatusEndpointAsync(currentTargetIp, ftpPort, allowProbe: true, Math.Clamp(connectionTimeoutMs, 1200, 3000), cancellationTokenSource.Token);
			telemetrySnapshot.FtpPort = ftpEndpoint.Port;
			telemetrySnapshot.FtpServiceReachable = ftpEndpoint.Reachable;
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
			Jrpc2Client jrpc2Client = new Jrpc2Client(client);
			try
			{
				telemetrySnapshot.TitleId = await jrpc2Client.GetTitleIdAsync(cancellationToken);
			}
			catch
			{
			}
			if (!await TryGetSmcTemperaturesAsync(client, telemetrySnapshot, cancellationToken))
			{
				telemetrySnapshot.CpuTemp = await TryGetTempAsync(jrpc2Client, SensorType.CPU, cancellationToken);
				telemetrySnapshot.GpuTemp = await TryGetTempAsync(jrpc2Client, SensorType.GPU, cancellationToken);
				telemetrySnapshot.EdramTemp = await TryGetTempAsync(jrpc2Client, SensorType.EDRAM, cancellationToken);
				telemetrySnapshot.BoardTemp = await TryGetTempAsync(jrpc2Client, SensorType.MotherBoard, cancellationToken);
				if (telemetrySnapshot.CpuTemp.HasValue || telemetrySnapshot.GpuTemp.HasValue || telemetrySnapshot.EdramTemp.HasValue || telemetrySnapshot.BoardTemp.HasValue)
				{
					telemetrySnapshot.ThermalSource = "JRPC2 fallback";
				}
			}
			telemetrySnapshot.DashboardVersion = await TryGetDashboardAsync(jrpc2Client, cancellationToken);
			telemetrySnapshot.Motherboard = await TryGetMotherboardAsync(jrpc2Client, cancellationToken);
			TitleIdDatabase titleDatabase = TitleIdDatabase.Instance;
			if (telemetrySnapshot.TitleId.HasValue && titleDatabase.TryResolve(telemetrySnapshot.TitleId.Value, null, out TitleIdEntry? titleEntry))
			{
				telemetrySnapshot.TitleName = titleEntry?.Name;
			}
			else if (telemetrySnapshot.TitleId.HasValue && titleDatabase.TryRememberDiscoveredTitle(telemetrySnapshot.TitleId.Value, telemetrySnapshot.RunningXex, out string? discoveredName))
			{
				telemetrySnapshot.TitleName = discoveredName;
			}
			telemetrySnapshot.TitleName = ProfileHelpers.TryGetTitleFallbackName(telemetrySnapshot.TitleId, telemetrySnapshot.RunningXex, telemetrySnapshot.TitleName);
			try
			{
				if (flag)
				{
					foreach (XbdmDriveEntry item in await client.GetDrivesAsync(includeSize: true, cancellationTokenSource.Token))
					{
						string? text2 = TrimOrNull(item.Name);
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
						telemetrySnapshot.Drives.AddRange((await TryGetFtpDriveRootsAsync(telemetrySnapshot.FtpPort ?? 21, ftpUser, ftpPass, cancellationTokenSource.Token)).Select(static name => new DriveInventoryEntry
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
			if (!cancellationToken.IsCancellationRequested)
			{
				ProfileHelpers.ResolvedIdentityInfo resolvedIdentity = await ProfileHelpers.ResolveSignedInIdentityAsync(client, currentTargetIp, currentTargetPort, connectionTimeoutMs, cliConfig, allowF3: true, allowProfilePackage: flag, cancellationToken);
				telemetrySnapshot.Gamertag = TrimOrNull(resolvedIdentity.Gamertag);
				telemetrySnapshot.Xuid = TrimOrNull(resolvedIdentity.Xuid);
				telemetrySnapshot.SignInStateText = resolvedIdentity.IsSignedIn ? resolvedIdentity.SignInStateText : "not detected";
			}
			try
			{
				if (flag)
				{
					foreach (XbdmModuleInfo item2 in await client.GetModulesAsync(includeSections: false, cancellationTokenSource.Token))
					{
						string? text3 = TrimOrNull(item2.Name);
						if (!string.IsNullOrWhiteSpace(text3))
						{
							telemetrySnapshot.Modules.Add(text3);
						}
					}
					telemetrySnapshot.ModulesSessionEpoch = telemetrySnapshot.SessionEpoch;
				}
				else if (latestSnapshot != null)
				{
					telemetrySnapshot.Modules.AddRange(latestSnapshot.Modules);
					telemetrySnapshot.ModulesSessionEpoch = latestSnapshot.ModulesSessionEpoch;
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
					string text4 = telemetrySnapshot.FtpUser ?? FtpEndpointHelpers.DefaultUser;
					string text5 = ftpPass;
					PluginHelpers.PluginConfig pluginConfig = await PluginHelpers.LoadAsync(currentTargetIp, num, text4, text5, connectionTimeoutMs, iniPath: null, cancellationToken: cancellationToken);
					foreach (var item3 in pluginConfig.Slots.OrderBy((KeyValuePair<int, string> kv) => kv.Key))
					{
						if (!string.IsNullOrWhiteSpace(item3.Value))
						{
							telemetrySnapshot.Plugins.Add("plugin" + item3.Key + " : " + item3.Value);
						}
					}
					telemetrySnapshot.PluginsSessionEpoch = telemetrySnapshot.SessionEpoch;
				}
				else if (latestSnapshot != null)
				{
					telemetrySnapshot.Plugins.AddRange(latestSnapshot.Plugins);
					telemetrySnapshot.PluginsSessionEpoch = latestSnapshot.PluginsSessionEpoch;
				}
			}
			catch
			{
			}
		}
		catch (SmcScratchRestorationException ex)
		{
			telemetrySnapshot.ErrorText = ex.Message;
			telemetrySnapshot.ThermalIntegrityFailure = true;
		}
		catch (Exception)
		{
			telemetrySnapshot.ErrorText = "Telemetry unavailable";
		}
		return telemetrySnapshot;
	}

	private static int ComputeTelemetryBudgetMs(int timeoutMs, bool forceHeavyRefresh)
	{
		int boundedTimeoutMs = Math.Clamp(timeoutMs, 1000, 10000);
		return forceHeavyRefresh
			? Math.Clamp(boundedTimeoutMs * 3, 12000, 24000)
			: Math.Clamp(boundedTimeoutMs, 6000, 10000);
	}

	private static async Task<bool> TryGetSmcTemperaturesAsync(XbdmClient client, TelemetrySnapshot snapshot, CancellationToken cancellationToken)
	{
		try
		{
			SmcTemperatureSnapshot temperatures = await HardwareHelpers.GetSmcTemperaturesAsync(client, cancellationToken);
			snapshot.CpuTemp = temperatures.CpuCelsius;
			snapshot.GpuTemp = temperatures.GpuCelsius;
			snapshot.EdramTemp = temperatures.EdramCelsius;
			snapshot.BoardTemp = temperatures.MotherboardCelsius;
			snapshot.ThermalSource = "SMC fixed-point";
			return true;
		}
		catch (SmcScratchRestorationException)
		{
			throw;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<double?> TryGetTempAsync(Jrpc2Client jrpc, SensorType type, CancellationToken cancellationToken)
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

	private async Task<List<string>> TryGetFtpDriveRootsAsync(int ftpPort, string user, string pass, CancellationToken cancellationToken)
	{
		List<string> list = new List<string>();
		try
		{
			int timeoutMs = Math.Clamp(connectionTimeoutMs, 1200, 7000);
			await using AsyncFtpClient asyncFtpClient = FtpHelpers.CreateClient(currentTargetIp, ftpPort, user, pass, timeoutMs);
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
		bool wasConnected = IsSessionConnected();
		CliConfig.TryLoad(out CliConfig reconnectConfig);
		bool shouldScheduleReconnect = ShouldScheduleAutoReconnect(
			wasConnected,
			snapshot.Connected,
			autoReconnectArmed,
			reconnectConfig.AutoReconnectEnabled != false,
			autoReconnectInFlight);
		if (snapshot.ThermalIntegrityFailure)
		{
			telemetryTimer.Stop();
			AppendSystemLine("Critical telemetry safety stop: " + (snapshot.ErrorText ?? "SMC scratch restoration was not verified."), FailureStateColor);
			ShowStatusToast(TranslateTerminalText("Telemetry stopped for console memory safety."));
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
			snapshot.ThermalSource ??= latestSnapshot.ThermalSource;
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
		string text5 = TrimOrNull(snapshot.Gamertag) ?? "Not signed in";
		string text6 = NormalizePresenceStateText(snapshot.SignInStateText);
		SetLeftConsoleText("BOARD      " + FitStatusText(text3, 16) + "\nDASH       " + FitStatusText(text4, 16) + "\nGAME       " + FitStatusText(text8, 16) + "\nXEX        " + FitFileLeaf(text9, 16) + "\nTITLEID    " + FormatTitleId(snapshot.TitleId) + "\nGAMERTAG   " + FitStatusText(text5, 16));
		SetLeftSignInText("INVENTORY");
		if (!snapshot.Connected)
		{
			CancelAndDispose(ref remoteBrowseCts);
			remoteEntries.Clear();
			remoteCurrentPath = "/";
			remoteBrowserHasLoaded = false;
			nextRemoteRefreshAllowedUtc = DateTime.MinValue;
			ResetFtpTrafficTracking();
			SetRightDetailText(BuildDisconnectedSessionText());
			SetRemoteBrowserPlaceholder("/", shouldScheduleReconnect ? "Waiting to reconnect." : "Connect to browse remote files.");
		}
		else
		{
			if (snapshot.FtpServiceReachable == false)
			{
				SetFtpTrafficUnavailable("NO FTP", LoadingStateColor);
			}
			else if (snapshot.FtpRxKbps > 0.0 || snapshot.FtpTxKbps > 0.0)
			{
				SetFtpTrafficRates(snapshot.FtpRxKbps, snapshot.FtpTxKbps, snapshot.FtpRxKbps > 0.0 || snapshot.FtpTxKbps > 0.0);
			}
			SetRightDetailText("TITLE      " + FitStatusText(text8, 22) + "\nXEX        " + FitFileLeaf(text9, 22) + "\nUSER       " + FitStatusText(text5, 22) + "\nPRESENCE   " + FitStatusText(text6, 22) + "\nTITLEID    " + FormatTitleId(snapshot.TitleId));
		}
		shellDisconnected = !snapshot.Connected;
		latestSnapshot = snapshot;
		UpdateRuntimePresence(snapshot.Connected, snapshot);
		UpdateDriveInventory(snapshot.Drives);
		RefreshInventoryListFromSnapshot();
		if (snapshot.Connected)
		{
			connectAttemptFailed = false;
			ClearConnectionFailureState();
			ClearStatusToast();
		}
		UpdateConnectionStatusSurfaces(snapshot);
		footerThermalSource = TrimOrNull(snapshot.ThermalSource);
		SetFooterThermals(snapshot.CpuTemp, snapshot.GpuTemp, snapshot.EdramTemp, snapshot.BoardTemp, snapshot.Motherboard);
		UpdateShellScaffold(snapshot);
		UpdateLiveHints(snapshot);
		if (shouldScheduleReconnect)
		{
			_ = RunAutoReconnectAsync();
		}
	}

	private void UpdateLiveHints(TelemetrySnapshot? snapshot = null)
	{
		UpdateConnectionStatusSurfaces(snapshot);
	}

	private void UpdateConnectionStatusIndicator(TelemetrySnapshot? snapshot = null)
	{
		UpdateConnectionStatusSurfaces(snapshot);
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
		catch (Exception)
		{
			AppendSystemLine(TranslateTerminalText("Could not open the selected path."), FailureStateColor);
		}
	}

	private static string FormatTemp(double? value)
	{
		if (!value.HasValue)
		{
			return "--°C";
		}
		return value.Value.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
	}

	private static string FormatThermalRow(string label, double? value)
	{
		return label.PadRight(5) + " " + FormatTemp(value);
	}

	private static string BuildThermalBlock(double? cpu, double? gpu, double? edram, double? board)
	{
		return "THERMALS\n" + FormatThermalRow("CPU", cpu) + "\n" + FormatThermalRow("GPU", gpu) + "\n" + FormatThermalRow("EDRAM", edram) + "\n" + FormatThermalRow("BOARD", board);
	}

	private static string BuildFooterThermalText(double? cpu, double? gpu, double? edram, double? board)
	{
		if (!cpu.HasValue && !gpu.HasValue && !edram.HasValue && !board.HasValue)
		{
			return "THERMALS --";
		}
		return "CPU " + FormatTemp(cpu) + " · GPU " + FormatTemp(gpu) + " · EDRAM " + FormatTemp(edram) + " · BOARD " + FormatTemp(board);
	}

	private static ThermalThresholdProfile ResolveThermalThresholds(string? motherboard)
	{
		string value = TrimOrNull(motherboard) ?? string.Empty;
		if (ThermalThresholdsByMotherboard.TryGetValue(value, out ThermalThresholdProfile exact))
		{
			return exact;
		}
		foreach ((string key, ThermalThresholdProfile profile) in ThermalThresholdsByMotherboard)
		{
			if (value.Contains(key, StringComparison.OrdinalIgnoreCase))
			{
				return profile;
			}
		}
		return UnknownThermalThresholds;
	}

	private static Color GetThermalSeverityColor(string sensor, double? value, string? motherboard)
	{
		if (!value.HasValue)
		{
			return EmptyStateColor;
		}

		ThermalThresholdProfile thresholds = ResolveThermalThresholds(motherboard);
		(int target, int critical) = sensor.ToUpperInvariant() switch
		{
			"CPU" => (thresholds.CpuTarget, thresholds.CpuCritical),
			"GPU" => (thresholds.GpuTarget, thresholds.GpuCritical),
			"EDRAM" => (thresholds.EdramTarget, thresholds.EdramCritical),
			_ => (55, 75)
		};

		double temperature = value.Value;
		double cautionStart = Math.Max(0, target - 8);
		if (temperature < cautionStart)
		{
			return SuccessStateColor;
		}
		if (temperature < target)
		{
			return InterpolateColor(SuccessStateColor, ThermalCautionColor, (float)((temperature - cautionStart) / Math.Max(1, target - cautionStart)));
		}
		if (temperature >= critical)
		{
			return FailureStateColor;
		}

		double hotStart = target + Math.Max(1.0, (critical - target) * 0.55);
		if (temperature < hotStart)
		{
			return InterpolateColor(ThermalCautionColor, ThermalHotColor, (float)((temperature - target) / Math.Max(1.0, hotStart - target)));
		}
		return InterpolateColor(ThermalHotColor, FailureStateColor, (float)((temperature - hotStart) / Math.Max(1.0, critical - hotStart)));
	}

	private void RefreshFooterOperationalSummary(TelemetrySnapshot? snapshot = null)
	{
		bool transferActive = fileTransferInFlight || fileTransferCts != null;
		bool commandActive = commandInFlight || commandCts != null || activeCommandProcess != null;
		string activity = transferActive
			? "TRANSFER ACTIVE"
			: (commandActive
				? "COMMAND RUNNING"
				: (connectAttemptInFlight
					? "CONNECTING"
					: footerOperationStatusOverride ?? "IDLE"));
		bool ready = IsSessionConnected(snapshot) && !transferActive && !commandActive && !connectAttemptInFlight;
		int activeTransfers = transferQueueEntries?.Count(static entry => string.Equals(entry.State, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(entry.State, "queued", StringComparison.OrdinalIgnoreCase)) ?? 0;
		SetFooterStatusText(ready ? "READY" : "NOT READY");
		SetFooterTargetText("QUEUE: " + activeTransfers.ToString(CultureInfo.InvariantCulture));
		SetFooterPresenceText(activity);
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
		string? text = TrimOrNull(value);
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
		return "FTP       --\nPRESENCE  --\nDRIVES    --";
	}

	private string BuildConnectingStatusText()
	{
		return "FTP       PROBING\nPRESENCE  PROBING\nDRIVES    PROBING";
	}

	private string BuildFailedStatusText(CliErrorEnvelope? failure = null)
	{
		return "FTP       UNAVAILABLE\nALERT     CONSOLE UNREACHABLE\nPRESENCE  --";
	}

	private string BuildConnectedStatusText(TelemetrySnapshot snapshot)
	{
		if (!HasConnectedTelemetry(snapshot))
		{
			return "FTP       PROBING\nPRESENCE  PROBING\nDRIVES    PROBING";
		}
		string text3 = snapshot.FtpServiceReachable == false
			? "UNAVAILABLE"
			: (snapshot.FtpPort.HasValue ? snapshot.FtpPort.Value.ToString() : "21");
		string text4 = snapshot.FtpServiceReachable == false ? string.Empty : (TrimOrNull(snapshot.FtpUser) ?? "xbox");
		string? text5 = TrimOrNull(snapshot.ErrorText);
		int driveCount = snapshot.Drives?.Count ?? 0;
		if (snapshot.FtpServiceReachable == false)
		{
			string text7 = "FTP       XBDM ONLY\nPRESENCE  " + NormalizePresenceStateText(snapshot.SignInStateText) + "\nDRIVES    " + driveCount;
			if (!string.IsNullOrWhiteSpace(text5))
			{
				text7 += "\nALERT     " + FitStatusText(text5, 42);
			}
			return text7;
		}
		string text6 = "FTP       " + text3 + (string.IsNullOrWhiteSpace(text4) ? string.Empty : " (" + text4 + ")") + "\nPRESENCE  " + NormalizePresenceStateText(snapshot.SignInStateText) + "\nDRIVES    " + driveCount;
		if (!string.IsNullOrWhiteSpace(text5))
		{
			text6 = "FTP       " + text3 + (string.IsNullOrWhiteSpace(text4) ? string.Empty : " (" + text4 + ")") + "\nALERT     " + TrimOrNull(text5) + "\nDRIVES    " + driveCount;
		}
		return text6;
	}

	private static bool HasConnectedTelemetry(TelemetrySnapshot? snapshot)
	{
		return snapshot?.Drives?.Count > 0 || snapshot?.TitleId.HasValue == true || snapshot?.DashboardVersion.HasValue == true || snapshot?.CpuTemp.HasValue == true || snapshot?.GpuTemp.HasValue == true || snapshot?.EdramTemp.HasValue == true || snapshot?.BoardTemp.HasValue == true || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.DebugName)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.ExecutionState)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.Motherboard)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.TitleName)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.RunningXex)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.Gamertag)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.SignInStateText)) || snapshot?.FtpPort.HasValue == true || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.FtpUser)) || !string.IsNullOrWhiteSpace(TrimOrNull(snapshot?.ErrorText));
	}

	private static bool HasCompleteCoreTelemetry(TelemetrySnapshot? snapshot)
	{
		return snapshot?.Connected == true
			&& snapshot.TitleId.HasValue
			&& snapshot.DashboardVersion.HasValue
			&& !string.IsNullOrWhiteSpace(TrimOrNull(snapshot.Motherboard))
			&& snapshot.CpuTemp.HasValue
			&& snapshot.GpuTemp.HasValue
			&& snapshot.EdramTemp.HasValue
			&& snapshot.BoardTemp.HasValue;
	}

	private void UpdateRemotePaneAvailability(TelemetrySnapshot? snapshot = null)
	{
		TelemetrySnapshot? telemetrySnapshot = snapshot ?? latestSnapshot;
		ConnectionUiState connectionUiState = GetConnectionUiState(telemetrySnapshot);
		bool ftpAvailable = connectionUiState == ConnectionUiState.Connected && telemetrySnapshot?.FtpServiceReachable != false;
		bool browserEnabled = ftpAvailable && !connectAttemptInFlight && !fileTransferInFlight && !shellDisconnected;
		ftpTabButton.Enabled = browserEnabled;
		remoteUpButton.Enabled = browserEnabled && !remotePaneShowsQueue;
		remoteRefreshButton.Enabled = browserEnabled && !remotePaneShowsQueue;
		remotePathTextBox.ReadOnly = !browserEnabled || remotePaneShowsQueue;
		remotePathTextBox.Enabled = true;
		remotePathTextBox.ForeColor = browserEnabled && !remotePaneShowsQueue ? PrimaryTextColor : AccentDim;
		remotePathTextBox.BackColor = browserEnabled && !remotePaneShowsQueue
			? TerminalBackground
			: InterpolateColor(TerminalBackground, ShellBackground, 0.28f);
		remotePathTextBox.Cursor = browserEnabled && !remotePaneShowsQueue ? Cursors.IBeam : Cursors.Default;
		remotePathTextBox.TabStop = browserEnabled && !remotePaneShowsQueue;
		remoteFileList.Enabled = browserEnabled && !remotePaneShowsQueue;
		if (connectionUiState == ConnectionUiState.Connected && !ftpAvailable && !remotePaneShowsQueue)
		{
			remoteEntries.Clear();
			remoteBrowserHasLoaded = false;
			remoteCurrentPath = "/";
			SetRemoteBrowserUnavailablePlaceholder("/", "FTP service unavailable.");
		}
	}

	private static string BuildDisconnectedSessionText()
	{
		return "Not connected\nConnect to view console status,\ndrives, title, and user.\nLocal XeCLI commands stay below.";
	}

	private static string BuildDisconnectedConsoleText()
	{
		return "BOARD      --\nDASH       --\nGAME       --\nXEX        --\nTITLEID    --\nGAMERTAG   --";
	}

	private static string BuildConnectingConsoleText()
	{
		return "BOARD      --\nDASH       --\nGAME       --\nXEX        --\nTITLEID    --\nGAMERTAG   --";
	}

	private string BuildFailedSessionText()
	{
		return BuildConnectionFailureText(lastConnectionFailureEnvelope);
	}

	private string BuildConnectionFailureText(CliErrorEnvelope? failure)
	{
		string text = "Console connection failed.";
		string text2 = "Check the console is powered on, not frozen, and reachable, then try again or re-run rgh connect if the target changed.";
		return text + "\n" + text2;
	}

	private static string BuildConnectingSessionText()
	{
		return TranslateTerminalText("Connecting\nLoading live data");
	}

	private string BuildFailedShellText()
	{
		return BuildFailedSessionText();
	}

	private static string BuildDisconnectedShellText()
	{
		return "Not connected\r\nConnect to view console status,\ndrives, thermals, FTP 21, title, and user state.\r\nLocal XeCLI commands remain available below.";
	}

	private void UpdateShellScaffold(TelemetrySnapshot? snapshot = null)
	{
		string statusText = snapshot?.Connected == true
			? "Command output - live target"
			: "Command output - local shell";
		SetLabelText(shellScaffoldLabel, TranslateTerminalText(statusText));
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
			return "Not signed in";
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
		string? text = value?.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}

	[DllImport("user32.dll")]
	private static extern bool HideCaret(IntPtr hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int valueSize);

	private void ApplyNativeWindowTheme()
	{
		ApplyNativeWindowTheme(this);
	}

	private static void ApplyNativeWindowTheme(Form window)
	{
		ApplyNativeWindowTheme(window, ShellBackground, PrimaryTextColor, BorderColor);
	}

	internal static void ApplyNativeWindowTheme(Form window, Color background, Color text, Color border)
	{
		if (!OperatingSystem.IsWindows() || !window.IsHandleCreated)
		{
			return;
		}
		try
		{
			int enabled = 1;
			if (DwmSetWindowAttribute(window.Handle, 20, ref enabled, sizeof(int)) != 0)
			{
				DwmSetWindowAttribute(window.Handle, 19, ref enabled, sizeof(int));
			}
			int captionColor = ToNativeColorRef(background);
			int textColor = ToNativeColorRef(text);
			int borderColor = ToNativeColorRef(border);
			DwmSetWindowAttribute(window.Handle, 35, ref captionColor, sizeof(int));
			DwmSetWindowAttribute(window.Handle, 36, ref textColor, sizeof(int));
			DwmSetWindowAttribute(window.Handle, 34, ref borderColor, sizeof(int));
		}
		catch (DllNotFoundException)
		{
		}
		catch (EntryPointNotFoundException)
		{
		}
	}

	private bool ShouldRetryHeavyTelemetryRefresh()
	{
		TelemetrySnapshot? snapshot = latestSnapshot;
		if (snapshot == null || !snapshot.Connected || snapshot.SessionEpoch != Volatile.Read(ref sessionEpoch))
		{
			return true;
		}

		if (!IsHeavyTelemetryIncomplete(snapshot))
		{
			heavyTelemetryRetryAttempt = 0;
			nextHeavyTelemetryRetryUtc = DateTime.MinValue;
			return false;
		}
		return DateTime.UtcNow >= nextHeavyTelemetryRetryUtc;
	}

	private void UpdateHeavyTelemetryRetrySchedule(TelemetrySnapshot snapshot, bool forceHeavyRefresh)
	{
		if (snapshot.SessionEpoch != heavyTelemetryRetrySessionEpoch)
		{
			heavyTelemetryRetrySessionEpoch = snapshot.SessionEpoch;
			heavyTelemetryRetryAttempt = 0;
			nextHeavyTelemetryRetryUtc = DateTime.MinValue;
		}
		if (!IsHeavyTelemetryIncomplete(snapshot))
		{
			heavyTelemetryRetryAttempt = 0;
			nextHeavyTelemetryRetryUtc = DateTime.MinValue;
			return;
		}
		if (!forceHeavyRefresh && nextHeavyTelemetryRetryUtc != DateTime.MinValue)
		{
			return;
		}

		heavyTelemetryRetryAttempt++;
		int delaySeconds = heavyTelemetryRetryAttempt == 1
			? FirstIncompleteTelemetryRetryDelaySeconds
			: SubsequentIncompleteTelemetryRetryDelaySeconds;
		nextHeavyTelemetryRetryUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
	}

	private static bool IsHeavyTelemetryIncomplete(TelemetrySnapshot snapshot)
	{
		bool driveInventoryIncomplete = snapshot.Drives.Count == 0
			|| snapshot.Drives.Any(static drive => !drive.TotalBytes.HasValue || !drive.FreeBytes.HasValue);
		bool moduleInventoryIncomplete = snapshot.ModulesSessionEpoch != snapshot.SessionEpoch;
		bool pluginInventoryIncomplete = snapshot.FtpServiceReachable == true
			&& snapshot.PluginsSessionEpoch != snapshot.SessionEpoch;
		return !HasCompleteCoreTelemetry(snapshot)
			|| driveInventoryIncomplete
			|| moduleInventoryIncomplete
			|| pluginInventoryIncomplete;
	}

	private static int ToNativeColorRef(Color color)
	{
		return color.R | color.G << 8 | color.B << 16;
	}

	private void HideTerminalOutputCaret()
	{
		if (!terminalOutput.IsHandleCreated || terminalOutput.IsDisposed)
		{
			return;
		}
		HideCaret(terminalOutput.Handle);
	}

	private sealed class ScreenshotPreviewForm : Form
	{
		private readonly Bitmap previewImage;

		private readonly TableLayoutPanel root;

		private readonly TableLayoutPanel header;

		private readonly Label titleLabel;

		private readonly Label fileNameLabel;

		private readonly PictureBox picture;

		private readonly TableLayoutPanel footer;

		private readonly SingleLineEllipsisLabel pathLabel;

		private readonly TerminalButton openButton;

		private readonly TerminalButton folderButton;

		private readonly TerminalButton closeButton;

		private readonly ToolTip toolTip = new ToolTip
		{
			ShowAlways = true
		};

		internal ScreenshotPreviewForm(string path, Image image, Action openExternal, Action revealInExplorer)
		{
			previewImage = new Bitmap(image);
			Text = "XeCLI Screenshot Preview";
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(980, 700);
			MinimumSize = new Size(640, 480);
			BackColor = ShellBackground;
			ForeColor = PrimaryTextColor;
			Font = new Font("Consolas", 8.75f, FontStyle.Regular, GraphicsUnit.Point);
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimizeBox = false;
			ShowInTaskbar = false;
			KeyPreview = true;
			AutoScaleMode = AutoScaleMode.Dpi;
			AutoScaleDimensions = new SizeF(96f, 96f);

			root = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Padding = new Padding(12),
				ColumnCount = 1,
				RowCount = 3,
				BackColor = ShellBackground
			};
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

			header = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				ColumnCount = 2,
				RowCount = 1,
				BackColor = ShellBackground
			};
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			titleLabel = new Label
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Text = "SCREENSHOT PREVIEW",
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = AccentGreen,
				Font = new Font("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point)
			};
			fileNameLabel = new Label
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				Text = Path.GetFileName(path),
				TextAlign = ContentAlignment.MiddleRight,
				ForeColor = AccentDim,
				AutoEllipsis = true,
				UseMnemonic = false
			};
			toolTip.SetToolTip(fileNameLabel, path);
			header.Controls.Add(titleLabel, 0, 0);
			header.Controls.Add(fileNameLabel, 1, 0);

			picture = new PictureBox
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty,
				BackColor = TerminalBackground,
				BorderStyle = BorderStyle.FixedSingle,
				Image = previewImage,
				SizeMode = PictureBoxSizeMode.Zoom,
				AccessibleName = "Captured console screenshot"
			};

			footer = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 10, 0, 0),
				ColumnCount = 4,
				RowCount = 1,
				BackColor = ShellBackground
			};
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146f));
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));
			pathLabel = new SingleLineEllipsisLabel
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 0, 12, 0),
				Text = path,
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = AccentDim,
				BackColor = ShellBackground,
				AutoEllipsis = true,
				UseMnemonic = false,
				AccessibleName = "Screenshot file path",
				AccessibleDescription = path
			};
			toolTip.SetToolTip(pathLabel, path);
			openButton = CreatePreviewActionButton("OPEN");
			folderButton = CreatePreviewActionButton("SHOW IN FOLDER");
			closeButton = CreatePreviewActionButton("CLOSE");
			openButton.Click += delegate
			{
				openExternal();
			};
			folderButton.Click += delegate
			{
				revealInExplorer();
			};
			closeButton.Click += delegate
			{
				Close();
			};
			footer.Controls.Add(pathLabel, 0, 0);
			footer.Controls.Add(openButton, 1, 0);
			footer.Controls.Add(folderButton, 2, 0);
			footer.Controls.Add(closeButton, 3, 0);

			root.Controls.Add(header, 0, 0);
			root.Controls.Add(picture, 0, 1);
			root.Controls.Add(footer, 0, 2);
			Controls.Add(root);
			AcceptButton = openButton;
			CancelButton = closeButton;
			KeyDown += delegate(object? sender, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Escape)
				{
					Close();
					e.Handled = true;
				}
			};
			Shown += delegate
			{
				ApplyTheme();
				openButton.Focus();
			};
		}

		private static TerminalButton CreatePreviewActionButton(string text)
		{
			TerminalButton button = new TerminalButton
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(6, 0, 0, 0),
				Text = text,
				Font = new Font("Consolas", 8.25f, FontStyle.Bold, GraphicsUnit.Point),
				Cursor = Cursors.Hand,
				EnabledBackColor = TerminalBackground,
				HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.16f),
				PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.26f),
				BorderColor = BorderColor,
				EnabledTextColor = PrimaryTextColor
			};
			return button;
		}

		internal void ApplyTheme()
		{
			if (IsDisposed || Disposing)
			{
				return;
			}
			List<Control> redrawLockedControls = SuspendControlTreeRedraw(this);
			SuspendLayout();
			try
			{
				BackColor = ShellBackground;
				ForeColor = PrimaryTextColor;
				root.BackColor = ShellBackground;
				header.BackColor = ShellBackground;
				titleLabel.ForeColor = AccentGreen;
				fileNameLabel.ForeColor = AccentDim;
				picture.BackColor = TerminalBackground;
				footer.BackColor = ShellBackground;
				pathLabel.BackColor = ShellBackground;
				pathLabel.ForeColor = AccentDim;
				ApplyPreviewActionButtonTheme(openButton);
				ApplyPreviewActionButtonTheme(folderButton);
				ApplyPreviewActionButtonTheme(closeButton);
				ApplyNativePreviewTheme();
			}
			finally
			{
				ResumeLayout(performLayout: false);
				ResumeControlTreeRedraw(redrawLockedControls);
			}
			PerformLayout();
			Invalidate(invalidateChildren: true);
			Update();
		}

		private static void ApplyPreviewActionButtonTheme(TerminalButton button)
		{
			button.EnabledBackColor = TerminalBackground;
			button.DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);
			button.HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.16f);
			button.PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.26f);
			button.BorderColor = BorderColor;
			button.DisabledBorderColor = InterpolateColor(TerminalBackground, BorderColor, 0.42f);
			button.FocusBorderColor = AccentGreen;
			button.EnabledTextColor = PrimaryTextColor;
			button.DisabledTextColor = InterpolateColor(TerminalBackground, AccentDim, 0.48f);
			button.Invalidate();
		}

		private sealed class SingleLineEllipsisLabel : Label
		{
			protected override void OnPaint(PaintEventArgs e)
			{
				e.Graphics.Clear(BackColor);
				TextRenderer.DrawText(
					e.Graphics,
					Text,
					Font,
					ClientRectangle,
					ForeColor,
					TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
					TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
			}
		}

		private void ApplyNativePreviewTheme()
		{
			if (!OperatingSystem.IsWindows() || !IsHandleCreated)
			{
				return;
			}
			try
			{
				int enabled = 1;
				if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
				{
					DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
				}
				int captionColor = ToNativeColorRef(ShellBackground);
				int textColor = ToNativeColorRef(PrimaryTextColor);
				int borderColor = ToNativeColorRef(BorderColor);
				DwmSetWindowAttribute(Handle, 35, ref captionColor, sizeof(int));
				DwmSetWindowAttribute(Handle, 36, ref textColor, sizeof(int));
				DwmSetWindowAttribute(Handle, 34, ref borderColor, sizeof(int));
			}
			catch (DllNotFoundException)
			{
			}
			catch (EntryPointNotFoundException)
			{
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				toolTip.Dispose();
				previewImage.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	private sealed class TerminalMenuColorTable : ProfessionalColorTable
	{
		public override Color ToolStripDropDownBackground => CardBackground;

		public override Color MenuBorder => Color.FromArgb(176, BorderColor);

		public override Color MenuItemBorder => Color.FromArgb(188, AccentGreen);

		public override Color MenuItemSelected => InterpolateColor(CardBackground, AccentGreen, 0.18f);

		public override Color MenuItemSelectedGradientBegin => MenuItemSelected;

		public override Color MenuItemSelectedGradientEnd => MenuItemSelected;

		public override Color MenuItemPressedGradientBegin => InterpolateColor(CardBackground, AccentGreen, 0.24f);

		public override Color MenuItemPressedGradientMiddle => MenuItemPressedGradientBegin;

		public override Color MenuItemPressedGradientEnd => MenuItemPressedGradientBegin;

		public override Color SeparatorDark => Color.FromArgb(122, BorderColor);

		public override Color SeparatorLight => Color.FromArgb(28, AccentGreen);

		public override Color ImageMarginGradientBegin => CardBackground;

		public override Color ImageMarginGradientMiddle => CardBackground;

		public override Color ImageMarginGradientEnd => CardBackground;
	}

	internal sealed class TerminalButton : Button
	{
		private bool isHot;

		private bool isPressed;

		internal Color EnabledBackColor = TerminalBackground;

		internal Color DisabledBackColor = InterpolateColor(TerminalBackground, ShellBackground, 0.50f);

		internal Color HoverBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.12f);

		internal Color PressedBackColor = InterpolateColor(TerminalBackground, AccentGreen, 0.18f);

		internal Color BorderColor = XeCliTerminalForm.BorderColor;

		internal Color DisabledBorderColor = InterpolateColor(TerminalBackground, XeCliTerminalForm.BorderColor, 0.42f);

		internal Color EnabledTextColor = PrimaryTextColor;

		internal Color DisabledTextColor = InterpolateColor(TerminalBackground, AccentDim, 0.52f);

		internal Color FocusBorderColor = Color.Empty;

		internal Color LeftDividerColor = Color.Empty;

		internal Color SelectionRailColor = Color.Empty;

		internal int SelectionRailWidth;

		internal int CornerRadius = 4;

		internal float BorderThickness = 1f;

		internal bool RoundLeftCorners = true;

		internal bool RoundRightCorners = true;

		internal bool DrawBorder = true;

		internal bool DrawFocusCue = true;

		public TerminalButton()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			UseVisualStyleBackColor = false;
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			isHot = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			isHot = false;
			isPressed = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				isPressed = true;
				Invalidate();
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			isPressed = false;
			Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}

		protected override void OnPaint(PaintEventArgs pevent)
		{
			Graphics graphics = pevent.Graphics;
			Rectangle rectangle = ClientRectangle;
			if (rectangle.Width <= 0 || rectangle.Height <= 0)
			{
				return;
			}
			rectangle.Width--;
			rectangle.Height--;
			Color backColor = !Enabled
				? DisabledBackColor
				: (isPressed ? PressedBackColor : (isHot ? HoverBackColor : EnabledBackColor));
			Color borderColor = !Enabled ? DisabledBorderColor : BorderColor;
			Color textColor = !Enabled ? DisabledTextColor : EnabledTextColor;
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.Clear(Parent?.BackColor ?? BackColor);
			using GraphicsPath buttonPath = CreateHorizontalSegmentPath(rectangle, CornerRadius, RoundLeftCorners, RoundRightCorners);
			using (SolidBrush brush = new SolidBrush(backColor))
			{
				graphics.FillPath(brush, buttonPath);
			}
			if (DrawBorder)
			{
				using Pen pen = new Pen(borderColor, Math.Max(1f, BorderThickness)) { Alignment = PenAlignment.Inset };
				graphics.DrawPath(pen, buttonPath);
			}
			if (!SelectionRailColor.IsEmpty && SelectionRailWidth > 0 && rectangle.Height > 10)
			{
				GraphicsState state = graphics.Save();
				graphics.SetClip(buttonPath);
				using SolidBrush rail = new SolidBrush(SelectionRailColor);
				graphics.FillRectangle(rail, rectangle.Left, rectangle.Top + 5, SelectionRailWidth, rectangle.Height - 9);
				graphics.Restore(state);
			}
			if (!LeftDividerColor.IsEmpty && rectangle.Height > 10)
			{
				graphics.SmoothingMode = SmoothingMode.None;
				using Pen divider = new Pen(LeftDividerColor, 1f);
				graphics.DrawLine(divider, rectangle.Left, rectangle.Top + 7, rectangle.Left, rectangle.Bottom - 7);
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
			}
			TextFormatFlags textFlags = GetTextAlignmentFlags(TextAlign) | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
			Rectangle textBounds = Rectangle.FromLTRB(
				ClientRectangle.Left + Padding.Left,
				ClientRectangle.Top + Padding.Top,
				ClientRectangle.Right - Padding.Right,
				ClientRectangle.Bottom - Padding.Bottom);
			TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor, textFlags);
			if (DrawFocusCue && Focused && ShowFocusCues)
			{
				Rectangle focusBounds = Rectangle.Inflate(rectangle, -3, -3);
				if (focusBounds.Width > 0 && focusBounds.Height > 0)
				{
					Color focusColor = FocusBorderColor.IsEmpty ? borderColor : FocusBorderColor;
					using GraphicsPath focusPath = CreateHorizontalSegmentPath(focusBounds, 2, RoundLeftCorners, RoundRightCorners);
					using Pen focusPen = new Pen(focusColor) { DashStyle = DashStyle.Dot };
					graphics.DrawPath(focusPen, focusPath);
				}
			}
		}

		private Color GetEffectiveTextColor()
		{
			return Enabled ? EnabledTextColor : DisabledTextColor;
		}

		private static TextFormatFlags GetTextAlignmentFlags(ContentAlignment alignment)
		{
			TextFormatFlags horizontal = alignment is ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft
				? TextFormatFlags.Left
				: alignment is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
					? TextFormatFlags.Right
					: TextFormatFlags.HorizontalCenter;
			TextFormatFlags vertical = alignment is ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight
				? TextFormatFlags.Top
				: alignment is ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
					? TextFormatFlags.Bottom
					: TextFormatFlags.VerticalCenter;
			return horizontal | vertical;
		}
	}

	private sealed class TerminalCommandTextBox : TextBox
	{
		private const int WmPaint = 0x000F;

		private const int WmPrintClient = 0x0318;

		private const int EmSetCueBanner = 0x1501;

		internal Color PlaceholderColor = AccentDim;

		internal void RefreshPlaceholderRendering()
		{
			ClearNativeCueBanner();
			Invalidate();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			ClearNativeCueBanner();
		}

		protected override void OnTextChanged(EventArgs e)
		{
			base.OnTextChanged(e);
			Invalidate();
		}

		protected override void OnEnter(EventArgs e)
		{
			base.OnEnter(e);
			Invalidate();
		}

		protected override void OnLeave(EventArgs e)
		{
			base.OnLeave(e);
			Invalidate();
		}

		protected override void WndProc(ref Message m)
		{
			base.WndProc(ref m);
			if (m.Msg == WmPaint)
			{
				using Graphics graphics = Graphics.FromHwnd(Handle);
				DrawPlaceholder(graphics);
			}
			else if (m.Msg == WmPrintClient && m.WParam != IntPtr.Zero)
			{
				using Graphics graphics = Graphics.FromHdc(m.WParam);
				DrawPlaceholder(graphics);
			}
		}

		private void DrawPlaceholder(Graphics graphics)
		{
			if (!ShouldDrawPlaceholder())
			{
				return;
			}

			Rectangle bounds = ClientRectangle;
			using (SolidBrush backgroundBrush = new SolidBrush(BackColor))
			{
				graphics.FillRectangle(backgroundBrush, bounds);
			}
			TextRenderer.DrawText(
				graphics,
				PlaceholderText,
				Font,
				bounds,
				PlaceholderColor,
				TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
		}

		private bool ShouldDrawPlaceholder()
		{
			return TextLength == 0
				&& !string.IsNullOrWhiteSpace(PlaceholderText)
				&& ClientSize.Width > 0
				&& ClientSize.Height > 0;
		}

		private void ClearNativeCueBanner()
		{
			if (OperatingSystem.IsWindows() && IsHandleCreated)
			{
				SendMessage(Handle, EmSetCueBanner, IntPtr.Zero, string.Empty);
			}
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);
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
				using (SolidBrush brush = new SolidBrush(GetAlternatingRowBackgroundColor(i)))
				{
					e.Graphics.FillRectangle(brush, rowRect);
				}
				Rectangle keyRect = new Rectangle(rowRect.Left + 8, rowRect.Top, Math.Max(1, keyWidth - 10), rowRect.Height);
				Rectangle valueRect = new Rectangle(rowRect.Left + keyWidth + valueGap, rowRect.Top, Math.Max(1, rowRect.Width - keyWidth - valueGap - 8), rowRect.Height);
				DrawLeftFittedText(e.Graphics, rows[i].Key, keyFont, keyRect, AccentGreen);
				DrawLeftFittedText(e.Graphics, rows[i].Value, valueFont, valueRect, PrimaryTextColor);
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

		public event Action<int, Point>? ItemContextRequested;

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

		public string? GetItemTitle(int index)
		{
			if (index < 0 || index >= items.Count || items[index].Placeholder)
			{
				return null;
			}
			return TrimOrNull(items[index].TitleText);
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
				using (SolidBrush brush2 = new SolidBrush(GetAlternatingRowBackgroundColor(i)))
				{
					e.Graphics.FillRectangle(brush2, rectangle);
				}
				if (flag)
				{
					using SolidBrush brush3 = new SolidBrush(GetSelectedRowBackgroundColor());
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
			if (e.Button == MouseButtons.Left && TryStartScrollDrag(e.Location))
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
				if (e.Button == MouseButtons.Right)
				{
					selectedIndex = itemIndexAt;
					UpdateAnimationTimer();
					Invalidate();
					ItemContextRequested?.Invoke(itemIndexAt, PointToScreen(e.Location));
					return;
				}
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
			using SolidBrush brush = new SolidBrush(GetScrollTrackBackgroundColor());
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
				Rectangle placeholderBounds = new Rectangle(bounds.Left + 8, bounds.Top, Math.Max(1, bounds.Width - 16), bounds.Height);
				DrawLeftAlignedFittedText(graphics, item.TitleText, Font, placeholderBounds, GetStateMessageColor(item.TitleText));
				return;
			}
			using Font titleFont = new Font(Font.FontFamily, Font.Size + 0.15f, FontStyle.Bold, GraphicsUnit.Point);
			using Font subtitleFont = new Font(Font.FontFamily, Math.Max(6.5f, Font.Size - 0.55f), FontStyle.Regular, GraphicsUnit.Point);
			Rectangle numberBounds = new Rectangle(bounds.Left + 8, bounds.Top + 2, 22, Math.Max(12, bounds.Height - 4));
			Rectangle titleBounds = moduleView
				? new Rectangle(bounds.Left + 34, bounds.Top + 1, Math.Max(1, bounds.Width - 42), Math.Max(14, bounds.Height - 2))
				: new Rectangle(bounds.Left + 34, bounds.Top + 1, Math.Max(1, bounds.Width - 42), 13);
			Rectangle subtitleBounds = new Rectangle(bounds.Left + 34, bounds.Top + 14, Math.Max(1, bounds.Width - 42), 10);
			Color color = selected ? GetSelectedRowTextColor() : AccentGreen;
			TextRenderer.DrawText(graphics, item.NumberText, titleFont, numberBounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			TextRenderer.DrawText(graphics, item.TitleText, titleFont, titleBounds, selected ? GetSelectedRowTextColor() : ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			if (!moduleView && !string.IsNullOrWhiteSpace(item.SubtitleText))
			{
				DrawLeftAlignedFittedText(graphics, item.SubtitleText, subtitleFont, subtitleBounds, AccentDim);
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
			return moduleView ? 21 : 23;
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
			string text = TrimOrNull(value) ?? string.Empty;
			string title = BuildInventoryTitle(text);
			return string.Equals(text, title, StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
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

		private static void DrawLeftAlignedFittedText(Graphics graphics, string text, Font baseFont, Rectangle bounds, Color color)
		{
			if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 4 || bounds.Height <= 2)
			{
				return;
			}
			Font? font = null;
			try
			{
				float size = GetFittedFontSize(graphics, text, baseFont, bounds);
				font = new Font(baseFont.FontFamily, size, baseFont.Style, GraphicsUnit.Point);
				TextRenderer.DrawText(graphics, text, font, bounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
			}
			finally
			{
				font?.Dispose();
			}
		}

		private static float GetFittedFontSize(Graphics graphics, string text, Font baseFont, Rectangle bounds, float minimumSize = 5.75f)
		{
			if (bounds.Width <= 4 || bounds.Height <= 2)
			{
				return baseFont.Size;
			}
			float size = baseFont.Size;
			while (size >= minimumSize)
			{
				using Font candidate = new Font(baseFont.FontFamily, size, baseFont.Style, GraphicsUnit.Point);
				Size measured = TextRenderer.MeasureText(graphics, text, candidate, new Size(int.MaxValue, bounds.Height), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
				if (measured.Width <= bounds.Width - 2)
				{
					return size;
				}
				size -= 0.25f;
			}
			return minimumSize;
		}
	}

	private sealed class DriveInventoryPanel : Control
	{
		private readonly List<DriveInventoryEntry> entries = new List<DriveInventoryEntry>();

		private int firstVisibleIndex;

		private bool draggingScrollThumb;

		private int dragScrollOffsetY;

		private int selectedIndex = -1;

		public event Action<int, Point>? ItemContextRequested;

		public DriveInventoryPanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			SetStyle(ControlStyles.Selectable, value: true);
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
			selectedIndex = entries.Count == 0 || selectedIndex < 0 ? -1 : Math.Clamp(selectedIndex, 0, entries.Count - 1);
			Invalidate();
		}

		public DriveInventoryEntry? GetEntry(int index)
		{
			return index >= 0 && index < entries.Count ? entries[index] : null;
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
				bool selected = Focused && i == selectedIndex;
				using (SolidBrush brush3 = new SolidBrush(selected ? GetSelectedRowBackgroundColor() : GetAlternatingRowBackgroundColor(i)))
				{
					e.Graphics.FillRectangle(brush3, rectangle);
				}
				if (selected)
				{
					using Pen selectionBorder = new Pen(Color.FromArgb(214, AccentGreen), 1f);
					e.Graphics.DrawRectangle(selectionBorder, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
				}
				int percentWidth = Math.Min(68, Math.Max(54, rectangle.Width / 3));
				Rectangle rectangle2 = new Rectangle(rectangle.Left + 6, rectangle.Top + 1, Math.Max(1, rectangle.Width - percentWidth - 14), 15);
				Rectangle rectangle3 = new Rectangle(rectangle.Right - percentWidth - 6, rectangle.Top + 1, percentWidth, 15);
				Rectangle rectangle4 = new Rectangle(rectangle.Left + 6, rectangle.Top + 17, Math.Max(40, rectangle.Width - 12), 7);
				Rectangle rectangle5 = new Rectangle(rectangle.Left + 6, rectangle.Top + 26, Math.Max(1, rectangle.Width - 12), 12);
				TextRenderer.DrawText(e.Graphics, entries[i].Name, font, rectangle2, AccentGreen, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				string text = FormatDrivePercent(entries[i]);
				if (!string.IsNullOrWhiteSpace(text))
				{
					TextRenderer.DrawText(e.Graphics, text, Font, rectangle3, PrimaryTextColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				}
				DrawDriveBar(e.Graphics, entries[i], rectangle4);
				TextRenderer.DrawText(e.Graphics, FormatDriveSubtitle(entries[i]), Font, rectangle5, AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
			}
			DrawScrollBar(e.Graphics, visibleRowCount);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button == MouseButtons.Left && TryStartScrollDrag(e.Location))
			{
				return;
			}
			if (!Focused && CanFocus)
			{
				Focus();
			}
			int index = GetEntryIndexAt(e.Y);
			if (index < 0 || index >= entries.Count || IsPlaceholderEntry(entries[index]))
			{
				return;
			}
			selectedIndex = index;
			Invalidate();
			if (e.Button == MouseButtons.Right)
			{
				ItemContextRequested?.Invoke(index, PointToScreen(e.Location));
			}
		}

		private static void DrawDriveBar(Graphics graphics, DriveInventoryEntry entry, Rectangle bounds)
		{
			if (!entry.TotalBytes.HasValue || !entry.FreeBytes.HasValue || entry.TotalBytes.Value == 0)
			{
				return;
			}
			using SolidBrush brush = new SolidBrush(GetProgressTrackBackgroundColor());
			using Pen pen = new Pen(Color.FromArgb(90, AccentGreen), 1f);
			graphics.FillRectangle(brush, bounds);
			graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
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
				return FailureStateColor;
			}
			if (usedRatio >= 0.60)
			{
				return WarningColor;
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
			using SolidBrush brush = new SolidBrush(GetScrollTrackBackgroundColor());
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

		public static bool IsPlaceholderEntry(DriveInventoryEntry entry)
		{
			string text = TrimOrNull(entry.Name) ?? string.Empty;
			return !entry.TotalBytes.HasValue
				&& !entry.FreeBytes.HasValue
				&& (string.Equals(text, "CONNECTING", StringComparison.Ordinal)
					|| string.Equals(text, "LOADING", StringComparison.Ordinal)
					|| string.Equals(text, "Connect to view drives.", StringComparison.Ordinal)
					|| string.Equals(text, "No drives detected", StringComparison.Ordinal)
					|| string.Equals(text, "CONECTANDO", StringComparison.Ordinal)
					|| string.Equals(text, "CARGANDO", StringComparison.Ordinal)
					|| string.Equals(text, "No se detectaron unidades", StringComparison.Ordinal)
					|| string.Equals(text, "No se detectaron unidades activas", StringComparison.Ordinal));
		}

		private void DrawPlaceholderState(Graphics graphics, DriveInventoryEntry entry)
		{
			Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
			Rectangle content = Rectangle.Inflate(rectangle, -10, -10);
			content.Width = Math.Max(1, content.Width);
			content.Height = Math.Max(1, content.Height);
			using Font titleFont = new Font(Font.FontFamily, Font.Size + 0.65f, FontStyle.Bold, GraphicsUnit.Point);
			using Font bodyFont = new Font(Font.FontFamily, Math.Max(6.75f, Font.Size - 0.1f), FontStyle.Regular, GraphicsUnit.Point);
			string text = TrimOrNull(entry.Name) ?? string.Empty;
			string title = text;
			string body = string.Empty;
			if (string.Equals(text, "CONNECTING", StringComparison.Ordinal) || string.Equals(text, "CONECTANDO", StringComparison.Ordinal))
			{
				body = LocalizedText.IsSpanish ? "Esperando inventario de unidades." : "Waiting for drive inventory.";
			}
			else if (string.Equals(text, "LOADING", StringComparison.Ordinal) || string.Equals(text, "CARGANDO", StringComparison.Ordinal))
			{
				body = LocalizedText.IsSpanish ? "Leyendo inventario de unidades." : "Reading drive inventory.";
			}
			else if (string.Equals(text, "Connect to view drives.", StringComparison.Ordinal) || string.Equals(text, "No drives detected", StringComparison.Ordinal) || string.Equals(text, "No se detectaron unidades", StringComparison.Ordinal) || string.Equals(text, "No se detectaron unidades activas", StringComparison.Ordinal))
			{
				title = LocalizedText.IsSpanish ? "UNIDADES" : "NO DRIVES";
				body = LocalizedText.IsSpanish ? "Conecta para ver almacenamiento y tamano." : "Connect to inspect storage and sizes.";
			}
			if (string.IsNullOrWhiteSpace(title))
			{
				return;
			}
			Rectangle titleRect = new Rectangle(content.Left, content.Top + 4, content.Width, Math.Max(18, content.Height / 3));
			Rectangle bodyRect = new Rectangle(content.Left + 4, titleRect.Bottom + 4, Math.Max(1, content.Width - 8), Math.Max(1, content.Bottom - titleRect.Bottom - 4));
			TextRenderer.DrawText(graphics, title, titleFont, titleRect, AccentGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
			if (!string.IsNullOrWhiteSpace(body))
			{
				TextRenderer.DrawText(graphics, body, bodyFont, bodyRect, AccentDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
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
			return 40;
		}

		private int GetEntryIndexAt(int y)
		{
			int rowHeight = GetRowHeight();
			if (rowHeight <= 0 || y < 2)
			{
				return -1;
			}
			return firstVisibleIndex + (y - 2) / rowHeight;
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
		private const int ContentPaddingTop = 18;
		private const int ContentPaddingBottom = 6;
		private const int RowLeft = 5;
		private const int RowRightPadding = 16;
		private const int ScrollTrackInset = 6;

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
				return num * GetLineHeight() + ContentPaddingTop + ContentPaddingBottom;
			}
		}

		public SuggestionListPanel()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			SetStyle(ControlStyles.Selectable, value: true);
			DoubleBuffered = true;
			BackColor = TerminalBackground;
			ForeColor = PrimaryTextColor;
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
				selectedIndex = 0;
				firstVisibleIndex = 0;
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
			Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, base.Width - 1), Math.Max(1, base.Height - 1));
			using GraphicsPath shell = CreateRoundedRectanglePath(rectangle, 10);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(226, GetSuggestionSurfaceColor()));
			using Pen pen = new Pen(Color.FromArgb(112, BorderColor), 1f);
			e.Graphics.FillPath(brush, shell);
			e.Graphics.DrawPath(pen, shell);
			if (items.Count == 0)
			{
				return;
			}
			int lineHeight = GetLineHeight();
			int visibleLineCount = GetVisibleLineCount();
			int num = Math.Min(items.Count, firstVisibleIndex + visibleLineCount);
			for (int i = firstVisibleIndex; i < num; i++)
			{
				int num2 = (i - firstVisibleIndex) * lineHeight + ContentPaddingTop;
				Rectangle rectangle2 = new Rectangle(RowLeft, num2, Math.Max(1, base.Width - RowRightPadding), lineHeight - 1);
				bool flag = i == selectedIndex;
				using (SolidBrush brush2 = new SolidBrush(GetAlternatingRowBackgroundColor(i)))
				{
					e.Graphics.FillRectangle(brush2, rectangle2);
				}
				if (flag)
				{
					using SolidBrush brush3 = new SolidBrush(GetSelectedRowBackgroundColor());
					using Pen pen2 = new Pen(Color.FromArgb(220, AccentGreen), 1f);
					e.Graphics.FillRectangle(brush3, rectangle2);
					e.Graphics.DrawRectangle(pen2, rectangle2.X, rectangle2.Y, rectangle2.Width - 1, rectangle2.Height - 1);
				}
				Rectangle rectangle3 = new Rectangle(rectangle2.Left + 6, rectangle2.Top, 14, rectangle2.Height);
				Color color = flag ? GetSelectedRowTextColor() : AccentDim;
				TextRenderer.DrawText(e.Graphics, "·", Font, rectangle3, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				Rectangle rectangle4 = new Rectangle(rectangle2.Left + 18, rectangle2.Top, Math.Max(1, rectangle2.Width - 18), rectangle2.Height);
				TextRenderer.DrawText(e.Graphics, items[i], Font, rectangle4, flag ? GetSelectedRowTextColor() : ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
			using SolidBrush brush = new SolidBrush(GetScrollTrackBackgroundColor());
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
			return Math.Max(1, (base.Height - ContentPaddingTop - ContentPaddingBottom) / GetLineHeight());
		}

		private int GetItemIndexAt(int y)
		{
			int lineHeight = GetLineHeight();
			if (lineHeight <= 0)
			{
				return -1;
			}
			if (y < ContentPaddingTop)
			{
				return -1;
			}
			int num = (y - ContentPaddingTop) / lineHeight;
			return firstVisibleIndex + num;
		}

		private Rectangle GetScrollTrackRect()
		{
			return new Rectangle(base.Width - 8, ScrollTrackInset, 4, Math.Max(10, base.Height - ScrollTrackInset - ContentPaddingBottom));
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
			using (SolidBrush brush = new SolidBrush(TerminalBackground))
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
			for (int i = 0; i < entries.Count; i++)
			{
				TransferQueueEntry transferQueueEntry = entries[i];
				(Rectangle rectangle2, Rectangle detailBounds, Rectangle progressBounds) = CalculateQueueEntryLayout(rectangle, entries.Count, i);
				if (rectangle2.Height < 44 || rectangle2.Top >= rectangle.Bottom - 8)
				{
					break;
				}
				bool flag = string.Equals(transferQueueEntry.State, "running", StringComparison.OrdinalIgnoreCase) || string.Equals(transferQueueEntry.State, "queued", StringComparison.OrdinalIgnoreCase);
				Color color = ResolveQueueStateColor(transferQueueEntry.State);
				using (SolidBrush brush2 = new SolidBrush(GetAlternatingRowBackgroundColor(i)))
				using (Pen pen3 = new Pen(Color.FromArgb(flag ? 128 : 72, color), 1f))
				{
					e.Graphics.FillRectangle(brush2, rectangle2);
					e.Graphics.DrawRectangle(pen3, rectangle2);
				}
				TextRenderer.DrawText(e.Graphics, transferQueueEntry.UpdatedAtLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Font, new Rectangle(rectangle2.X + 8, rectangle2.Y + 5, 66, 18), AccentDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, transferQueueEntry.State.ToUpperInvariant(), Font, new Rectangle(rectangle2.X + 78, rectangle2.Y + 5, 86, 18), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				string text = transferQueueEntry.ProgressPercent.HasValue ? (transferQueueEntry.ProgressPercent.Value.ToString("0") + "%") : "--";
				TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(rectangle2.Right - 58, rectangle2.Y + 5, 50, 18), PrimaryTextColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, transferQueueEntry.CommandText, Font, new Rectangle(rectangle2.X + 8, rectangle2.Y + 24, rectangle2.Width - 16, 18), PrimaryTextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				string text2 = TrimOrNull(transferQueueEntry.Detail) ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(text2))
				{
					using Font font3 = new Font("Consolas", 7.75f, FontStyle.Regular, GraphicsUnit.Point);
					TextRenderer.DrawText(e.Graphics, text2, font3, detailBounds, AccentDim, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
				}
				using (SolidBrush brush3 = new SolidBrush(GetProgressTrackBackgroundColor()))
				using (SolidBrush brush4 = new SolidBrush(Color.FromArgb(190, color)))
				{
					e.Graphics.FillRectangle(brush3, progressBounds);
					if (transferQueueEntry.ProgressPercent.HasValue)
					{
						int width = (int)Math.Round(progressBounds.Width * Math.Clamp(transferQueueEntry.ProgressPercent.Value / 100.0, 0.0, 1.0));
						if (width > 0)
						{
							e.Graphics.FillRectangle(brush4, new Rectangle(progressBounds.X, progressBounds.Y, width, progressBounds.Height));
						}
					}
				}
				if (rectangle2.Bottom >= rectangle.Bottom - 8)
				{
					break;
				}
			}
		}

		private static (Rectangle RowBounds, Rectangle DetailBounds, Rectangle ProgressBounds) CalculateQueueEntryLayout(Rectangle outerBounds, int entryCount, int index)
		{
			int safeCount = Math.Max(1, entryCount);
			int rowStride = Math.Max(66, Math.Min(94, (outerBounds.Height - 20) / safeCount));
			int rowWidth = Math.Max(1, outerBounds.Width - 20);
			int rowY = outerBounds.Y + 10 + Math.Max(0, index) * rowStride;
			int rowHeight = Math.Max(60, rowStride - 6);
			rowHeight = Math.Min(rowHeight, Math.Max(0, outerBounds.Bottom - rowY - 4));
			Rectangle rowBounds = new Rectangle(outerBounds.X + 10, rowY, rowWidth, rowHeight);
			Rectangle detailBounds = new Rectangle(rowBounds.X + 8, rowBounds.Y + 41, Math.Max(1, rowBounds.Width - 16), 12);
			Rectangle progressBounds = new Rectangle(rowBounds.X + 8, Math.Max(rowBounds.Y + 54, rowBounds.Bottom - 6), Math.Max(1, rowBounds.Width - 16), 4);
			return (rowBounds, detailBounds, progressBounds);
		}

		private static Color ResolveQueueStateColor(string state)
		{
			if (string.Equals(state, "complete", StringComparison.OrdinalIgnoreCase))
			{
				return AccentGreen;
			}
			if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "error", StringComparison.OrdinalIgnoreCase))
			{
				return FailureStateColor;
			}
			if (string.Equals(state, "cancelled", StringComparison.OrdinalIgnoreCase))
			{
				return WarningColor;
			}
			return AccentCyan;
		}
	}

	private sealed class TerminalOutputBox : RichTextBox
	{
		private const int WsHScroll = 0x00100000;

		private const int WsVScroll = 0x00200000;

		private const int SbHorz = 0;

		private const int SbVert = 1;

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

		public TerminalOutputBox()
		{
			BackColor = GetTerminalOutputSurfaceColor();
			BorderStyle = BorderStyle.None;
			DetectUrls = false;
			HideSelection = false;
			ScrollBars = RichTextBoxScrollBars.None;
			WordWrap = true;
		}

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams createParams = base.CreateParams;
				createParams.Style &= ~WsHScroll;
				createParams.Style &= ~WsVScroll;
				return createParams;
			}
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			ApplyNativeScrollbarPolicy();
		}

		protected override void OnTextChanged(EventArgs e)
		{
			base.OnTextChanged(e);
			ApplyNativeScrollbarPolicy();
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			ApplyNativeScrollbarPolicy();
		}

		private void ApplyNativeScrollbarPolicy()
		{
			if (!IsHandleCreated || IsDisposed)
			{
				return;
			}
			ShowScrollBar(Handle, SbHorz, false);
			ShowScrollBar(Handle, SbVert, false);
		}
	}

	private sealed class TerminalScrollIndicator : Control
	{
		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

		private const int EmGetFirstVisibleLine = 0x00CE;

		private const int EmLineScroll = 0x00B6;

		private const int MouseWheelDelta = 120;

		private RichTextBox? source;

		private bool draggingScrollThumb;

		private int dragScrollOffsetY;

		public TerminalScrollIndicator()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
			BackColor = GetTerminalOutputSurfaceColor();
			Cursor = Cursors.Hand;
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
			Rectangle rectangle = GetScrollTrackRect();
			using (SolidBrush brush = new SolidBrush(GetScrollTrackBackgroundColor()))
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
			Rectangle rectangle2 = GetScrollThumbRect(visibleLines);
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(220, AccentGreen));
			using Pen pen = new Pen(Color.FromArgb(160, PrimaryTextColor), 1f);
			e.Graphics.FillRectangle(brush3, rectangle2);
			e.Graphics.DrawLine(pen, rectangle2.Left, rectangle2.Top, rectangle2.Right - 1, rectangle2.Top);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			ScrollByWheelDelta(e.Delta);
			if (e is HandledMouseEventArgs handled)
			{
				handled.Handled = true;
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button == MouseButtons.Left)
			{
				TryStartScrollDrag(e.Location);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (draggingScrollThumb && e.Button == MouseButtons.Left)
			{
				UpdateScrollDrag(e.Y);
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (e.Button == MouseButtons.Left)
			{
				EndScrollDrag();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			if (!draggingScrollThumb)
			{
				Invalidate();
			}
		}

		protected override void OnLostFocus(EventArgs e)
		{
			base.OnLostFocus(e);
			EndScrollDrag();
		}

		private void ScrollByWheelDelta(int delta)
		{
			if (delta == 0)
			{
				return;
			}
			int notches = Math.Abs(delta) >= MouseWheelDelta ? delta / MouseWheelDelta : Math.Sign(delta);
			int linesPerNotch = SystemInformation.MouseWheelScrollLines;
			if (linesPerNotch <= 0 || linesPerNotch == int.MaxValue)
			{
				linesPerNotch = Math.Max(1, GetVisibleLineCount() - 1);
			}
			ScrollByLines(-notches * Math.Max(1, linesPerNotch));
		}

		private void ScrollByLines(int lineDelta)
		{
			if (source == null || !source.IsHandleCreated || source.IsDisposed || lineDelta == 0)
			{
				return;
			}
			SendMessage(source.Handle, EmLineScroll, 0, lineDelta);
			Invalidate();
		}

		private Rectangle GetScrollTrackRect()
		{
			return new Rectangle(Math.Max(0, (base.Width - 4) / 2), 4, 4, Math.Max(0, base.Height - 8));
		}

		private Rectangle GetScrollThumbRect(int visibleLines)
		{
			Rectangle trackRect = GetScrollTrackRect();
			int lineCount = GetLineCount();
			if (lineCount <= visibleLines || trackRect.Height <= 0)
			{
				return trackRect;
			}
			double ratio = (double)visibleLines / (double)lineCount;
			int height = Math.Min(trackRect.Height, Math.Max(18, (int)Math.Round(trackRect.Height * ratio)));
			double position = (double)GetFirstVisibleLine() / (double)Math.Max(1, lineCount - visibleLines);
			int y = trackRect.Top + (int)Math.Round((trackRect.Height - height) * position);
			return new Rectangle(trackRect.X, y, trackRect.Width, height);
		}

		private bool TryStartScrollDrag(Point location)
		{
			if (source == null || !source.IsHandleCreated || source.IsDisposed)
			{
				return false;
			}
			Rectangle trackRect = GetScrollTrackRect();
			if (!trackRect.Contains(location))
			{
				return false;
			}
			int visibleLines = GetVisibleLineCount();
			if (GetLineCount() <= visibleLines)
			{
				return false;
			}
			Rectangle thumbRect = GetScrollThumbRect(visibleLines);
			draggingScrollThumb = true;
			Capture = true;
			if (thumbRect.Contains(location))
			{
				dragScrollOffsetY = location.Y - thumbRect.Top;
			}
			else
			{
				dragScrollOffsetY = thumbRect.Height / 2;
				UpdateScrollFromThumbTop(location.Y - dragScrollOffsetY, visibleLines, trackRect, thumbRect.Height);
			}
			return true;
		}

		private void UpdateScrollDrag(int mouseY)
		{
			int visibleLines = GetVisibleLineCount();
			Rectangle trackRect = GetScrollTrackRect();
			Rectangle thumbRect = GetScrollThumbRect(visibleLines);
			UpdateScrollFromThumbTop(mouseY - dragScrollOffsetY, visibleLines, trackRect, thumbRect.Height);
		}

		private void UpdateScrollFromThumbTop(int thumbTop, int visibleLines, Rectangle trackRect, int thumbHeight)
		{
			if (trackRect.Height <= thumbHeight)
			{
				return;
			}
			int minTop = trackRect.Top;
			int maxTop = trackRect.Bottom - thumbHeight;
			int top = Math.Clamp(thumbTop, minTop, maxTop);
			double position = (double)(top - trackRect.Top) / (double)Math.Max(1, trackRect.Height - thumbHeight);
			int maxFirstLine = Math.Max(0, GetLineCount() - visibleLines);
			ScrollToFirstVisibleLine((int)Math.Round(maxFirstLine * position));
		}

		private void ScrollToFirstVisibleLine(int line)
		{
			if (source == null || !source.IsHandleCreated || source.IsDisposed)
			{
				return;
			}
			int maxFirstLine = Math.Max(0, GetLineCount() - GetVisibleLineCount());
			int targetLine = Math.Clamp(line, 0, maxFirstLine);
			int delta = targetLine - GetFirstVisibleLine();
			if (delta != 0)
			{
				SendMessage(source.Handle, EmLineScroll, 0, delta);
			}
			Invalidate();
		}

		private void EndScrollDrag()
		{
			if (!draggingScrollThumb)
			{
				return;
			}
			draggingScrollThumb = false;
			Capture = false;
			Invalidate();
		}

		private int GetLineCount()
		{
			if (source == null || !source.IsHandleCreated || source.IsDisposed || source.TextLength == 0)
			{
				return 1;
			}
			return Math.Max(1, source.GetLineFromCharIndex(source.TextLength) + 1);
		}

		private int GetVisibleLineCount()
		{
			if (source == null || !source.IsHandleCreated || source.IsDisposed)
			{
				return 1;
			}
			int lineHeight = Math.Max(1, source.Font.Height + 1);
			return Math.Max(1, source.ClientSize.Height / lineHeight);
		}

		private int GetFirstVisibleLine()
		{
			if (source == null || !source.IsHandleCreated || source.IsDisposed)
			{
				return 0;
			}
			return Math.Max(0, SendMessage(source.Handle, EmGetFirstVisibleLine, 0, 0));
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

		private int modifiedWidth = 176;

		private int sizeWidth = 104;

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
				DrawPlaceholderMessage(e.Graphics, message, new Rectangle(6, headerHeight + 6, bounds.Width - 16, bodyRect.Height - 12), GetStateMessageColor(message));
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
				if (!flag)
				{
					using SolidBrush brush2 = new SolidBrush(GetAlternatingRowBackgroundColor(drawRow));
					e.Graphics.FillRectangle(brush2, rowRect);
				}
				if (flag)
				{
					using SolidBrush brush3 = new SolidBrush(GetSelectedRowBackgroundColor());
					using Pen pen2 = new Pen(Color.FromArgb(214, AccentGreen), 1f);
					e.Graphics.FillRectangle(brush3, rowRect);
					e.Graphics.DrawRectangle(pen2, rowRect.X, rowRect.Y, rowRect.Width - 1, rowRect.Height - 1);
				}
				FileEntryView entry = entries[i];
				string prefix = entry.IsDirectory ? "[DIR] " : "      ";
				string modified = entry.ModifiedUtc.HasValue ? entry.ModifiedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "--";
				string size = FormatEntrySize(entry);
				TextRenderer.DrawText(e.Graphics, prefix + entry.Name, Font, new Rectangle(6, y, nameWidth - 8, rowHeight), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, modified, Font, new Rectangle(6 + nameWidth, y, modifiedWidth2 - 8, rowHeight), PrimaryTextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				TextRenderer.DrawText(e.Graphics, size, Font, new Rectangle(6 + nameWidth + modifiedWidth2, y, sizeWidth2 - 8, rowHeight), PrimaryTextColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				drawRow++;
			}
			DrawScrollBar(e.Graphics, visibleRowCount, headerHeight);
		}

		private void DrawPlaceholderMessage(Graphics graphics, string text, Rectangle bounds, Color color)
		{
			if (bounds.Width <= 0 || bounds.Height <= 0)
			{
				return;
			}
			int lineHeight = Math.Max(1, TextRenderer.MeasureText(graphics, "W", Font).Height + 1);
			int maxLines = Math.Max(1, bounds.Height / lineHeight);
			IReadOnlyList<string> lines = BuildVisibleMessageLines(text, Font, bounds.Width, maxLines);
			TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
			for (int i = 0; i < lines.Count; i++)
			{
				Rectangle lineRect = new Rectangle(bounds.X, bounds.Y + i * lineHeight, bounds.Width, lineHeight);
				TextRenderer.DrawText(graphics, lines[i], Font, lineRect, color, flags);
			}
		}

		private static IReadOnlyList<string> BuildVisibleMessageLines(string text, Font font, int width, int maxLines)
		{
			List<string> lines = new List<string>();
			if (string.IsNullOrWhiteSpace(text) || width <= 0 || maxLines <= 0)
			{
				return lines;
			}
			string[] paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
			bool truncated = false;
			for (int paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
			{
				if (lines.Count >= maxLines)
				{
					truncated = true;
					break;
				}
				string paragraph = paragraphs[paragraphIndex].Trim();
				if (paragraph.Length == 0)
				{
					lines.Add(string.Empty);
					continue;
				}
				string currentLine = string.Empty;
				foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
				{
					string candidate = currentLine.Length == 0 ? word : currentLine + " " + word;
					if (currentLine.Length == 0 || TextRenderer.MeasureText(candidate, font).Width <= width)
					{
						currentLine = candidate;
						continue;
					}
					if (lines.Count >= maxLines)
					{
						truncated = true;
						break;
					}
					lines.Add(currentLine);
					currentLine = word;
				}
				if (truncated)
				{
					break;
				}
				if (currentLine.Length > 0)
				{
					if (lines.Count >= maxLines)
					{
						truncated = paragraphIndex < paragraphs.Length - 1;
						break;
					}
					lines.Add(currentLine);
				}
			}
			if (truncated && lines.Count > 0)
			{
				lines[lines.Count - 1] = AppendEllipsisToFit(lines[lines.Count - 1], font, width);
			}
			return lines;
		}

		private static string AppendEllipsisToFit(string text, Font font, int width)
		{
			const string ellipsis = "...";
			string candidate = text.TrimEnd();
			while (candidate.Length > 0 && TextRenderer.MeasureText(candidate + ellipsis, font).Width > width)
			{
				candidate = candidate.Substring(0, candidate.Length - 1).TrimEnd();
			}
			return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
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
			using SolidBrush brush = new SolidBrush(GetScrollTrackBackgroundColor());
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
			int availableWidth = Math.Max(220, totalWidth - scrollWidth - 10);
			int minimumNameWidth = availableWidth >= 560 ? 230 : (availableWidth >= 460 ? 200 : 160);
			int minimumModifiedWidth = availableWidth >= 560 ? 154 : (availableWidth >= 460 ? 136 : 118);
			int minimumSizeWidth = availableWidth >= 560 ? 96 : (availableWidth >= 460 ? 84 : 72);
			int num = Math.Clamp(modifiedWidth, minimumModifiedWidth, Math.Max(minimumModifiedWidth, availableWidth - minimumNameWidth - minimumSizeWidth));
			int num2 = Math.Clamp(sizeWidth, minimumSizeWidth, Math.Max(minimumSizeWidth, availableWidth - num - minimumNameWidth));
			int num3 = availableWidth - num - num2;
			if (num3 < minimumNameWidth)
			{
				int num4 = minimumNameWidth - num3;
				if (num2 - num4 >= minimumSizeWidth)
				{
					num2 -= num4;
				}
				else if (num - num4 >= minimumModifiedWidth)
				{
					num -= num4;
				}
				num3 = availableWidth - num - num2;
			}
			modifiedWidth = num;
			sizeWidth = num2;
			return (Math.Max(minimumNameWidth, num3), num, num2);
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
			int availableWidth = Math.Max(220, base.Width - 8 - 10);
			int num2 = location.X - resizeAnchorX;
			switch (activeResizeColumn)
			{
			case ResizeColumn.NameToModified:
				modifiedWidth = Math.Clamp(resizeAnchorModifiedWidth - num2, 144, Math.Max(144, availableWidth - sizeWidth - 160));
				break;
			case ResizeColumn.ModifiedToSize:
			{
				int num3 = Math.Clamp(resizeAnchorSizeWidth - num2, 72, Math.Max(72, availableWidth - resizeAnchorModifiedWidth - 160));
				int num4 = availableWidth - resizeAnchorModifiedWidth - num3;
				if (num4 < 160)
				{
					num3 = Math.Max(72, availableWidth - resizeAnchorModifiedWidth - 160);
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

	private sealed class TrafficRateLabel : Label
	{
		private bool rateMode;

		private string rxValue = "--";

		private string txValue = "--";

		internal void SetStateText(string value)
		{
			rateMode = false;
			SetControlText(this, value);
			AccessibleDescription = value;
			Invalidate();
		}

		internal void SetRates(string receiveRate, string sendRate)
		{
			rateMode = true;
			rxValue = receiveRate;
			txValue = sendRate;
			SetControlText(this, "RX " + receiveRate + Environment.NewLine + "TX " + sendRate);
			AccessibleDescription = "Receive " + receiveRate + "; send " + sendRate + ".";
			Invalidate();
		}

		internal void SetUnavailable()
		{
			SetRates("--", "--");
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			if (!rateMode)
			{
				base.OnPaint(e);
				return;
			}

			e.Graphics.Clear(BackColor);
			int lineHeight = Math.Max(13, (ClientSize.Height - Padding.Vertical) / 2);
			int top = Padding.Top;
			DrawRateLine(e.Graphics, "RX", rxValue, top, lineHeight, AccentGreen);
			DrawRateLine(e.Graphics, "TX", txValue, top + lineHeight, lineHeight, AccentPink);
		}

		private void DrawRateLine(Graphics graphics, string label, string value, int top, int height, Color labelColor)
		{
			Rectangle labelBounds = new Rectangle(Padding.Left, top, 27, height);
			Rectangle valueBounds = new Rectangle(labelBounds.Right + 3, top, Math.Max(1, ClientSize.Width - labelBounds.Right - Padding.Right - 3), height);
			TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
			TextRenderer.DrawText(graphics, label, Font, labelBounds, labelColor, flags);
			TextRenderer.DrawText(graphics, value, Font, valueBounds, PrimaryTextColor, flags | TextFormatFlags.EndEllipsis);
		}
	}

	private sealed class FtpTrafficGraphControl : Control
	{
		private readonly Queue<(double Rx, double Tx)> samples = new Queue<(double, double)>();

		private string statusText = "IDLE";

		private Color statusColor = AccentDim;

		public FtpTrafficGraphControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			DoubleBuffered = true;
			ResizeRedraw = true;
		}

		public void SetStatus(string text, Color color)
		{
			string normalizedText = TrimOrNull(text) ?? "IDLE";
			bool clearedSamples = false;
			bool available = string.Equals(normalizedText, "IDLE", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(normalizedText, "LIVE", StringComparison.OrdinalIgnoreCase);
			if (!available && samples.Count > 0)
			{
				samples.Clear();
				clearedSamples = true;
			}
			if (!clearedSamples && string.Equals(statusText, normalizedText, StringComparison.Ordinal) && statusColor.ToArgb() == color.ToArgb())
			{
				return;
			}
			statusText = normalizedText;
			statusColor = color;
			Invalidate();
		}

		public void ClearSamples()
		{
			if (samples.Count == 0)
			{
				return;
			}
			samples.Clear();
			Invalidate();
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
			using Font legendFont = new Font("Segoe UI", 7.25f, FontStyle.Bold, GraphicsUnit.Point);
			int measuredLegendHeight = TextRenderer.MeasureText("CONNECTING", legendFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
			int legendHeight = Math.Clamp(measuredLegendHeight + 3, 14, Math.Max(14, base.Height / 2));
			int legendTop = 1;
			using Pen rxLegend = new Pen(AccentGreen, 1.8f);
			using Pen txLegend = new Pen(AccentPink, 1.5f);
			int legendCenterY = legendTop + legendHeight / 2;
			e.Graphics.DrawLine(rxLegend, 6, legendCenterY, 14, legendCenterY);
			TextRenderer.DrawText(e.Graphics, "RX", legendFont, new Rectangle(17, legendTop, 24, legendHeight), AccentGreen, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
			e.Graphics.DrawLine(txLegend, 45, legendCenterY, 53, legendCenterY);
			TextRenderer.DrawText(e.Graphics, "TX", legendFont, new Rectangle(56, legendTop, 24, legendHeight), AccentPink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
			Rectangle statusBounds = new Rectangle(84, legendTop, Math.Max(1, base.Width - 91), legendHeight);
			TextRenderer.DrawText(e.Graphics, statusText, legendFont, statusBounds, statusColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

			Rectangle rectangle = new Rectangle(5, legendTop + legendHeight + 3, Math.Max(1, base.Width - 10), Math.Max(1, base.Height - legendTop - legendHeight - 6));
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
				using Pen ghostPen = new Pen(Color.FromArgb(90, statusColor), 1.3f);
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
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(42, AccentGreen));
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(28, AccentPink));
			e.Graphics.FillPolygon(brush3, array4);
			e.Graphics.FillPolygon(brush2, array3);
			using Pen pen2 = new Pen(Color.FromArgb(228, AccentGreen), 1.8f);
			using Pen pen3 = new Pen(Color.FromArgb(214, AccentPink), 1.5f);
			e.Graphics.DrawLines(pen2, array);
			e.Graphics.DrawLines(pen3, array2);
		}
	}

}
