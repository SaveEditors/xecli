using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmScreenshotCommand : AsyncCommand<XbdmScreenshotCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--out <FILE>")]
		[Description("Output file path (.bmp or .raw).")]
		public string? Output { get; init; }

		[CommandOption("--format <FORMAT>")]
		[Description("Output format: bmp or raw.")]
		public string? Format { get; init; }

		[CommandOption("--raw")]
		[Description("Force raw output (same as --format raw).")]
		public bool Raw { get; init; }

		[CommandOption("--force")]
		[Description("Overwrite the output file if it exists.")]
		public bool Force { get; init; }

		[CommandOption("--decode <MODE>")]
		[Description("Force decode mode: auto, linear, tiled-v1, tiled-v2, tiled-v3, tiled-xenia.")]
		public string? Decode { get; init; }

		[CommandOption("--endianness <MODE>")]
		[Description("Force endianness: auto, none, swap8-16, swap8-32, swap16-32.")]
		public string? Endianness { get; init; }

		[CommandOption("--order <ORDER>")]
		[Description("Force 32bpp channel order: auto, bgra, rgba, argb, abgr, bgrx, rgbx, xrgb, xbgr.")]
		public string? Order { get; init; }

		[CommandOption("--dump-variants")]
		[Description("Dump all decode variants to a folder next to the output file.")]
		public bool DumpVariants { get; init; }

		[CommandOption("--crop-right <PX>")]
		[Description("Crop N pixels from the right edge (default: 0).")]
		public int CropRight { get; init; }

		[CommandOption("--crop-right-percent <PCT>")]
		[Description("Crop a percentage from the right edge (0-50).")]
		public double? CropRightPercent { get; init; }

		[CommandOption("--xenia-bank-xor <N>")]
		[Description("Force Xenia tile bank XOR (0-1) when using tiled-xenia.")]
		public int? XeniaBankXor { get; init; }

		[CommandOption("--xenia-pipe-xor <N>")]
		[Description("Force Xenia tile pipe XOR (0-3) when using tiled-xenia.")]
		public int? XeniaPipeXor { get; init; }
	}

	private enum EndianMode
	{
		None,
		Swap8In16,
		Swap8In32,
		Swap16In32
	}

	private enum ChannelOrder32
	{
		BGRA,
		RGBA,
		ARGB,
		ABGR,
		BGRX,
		RGBX,
		XRGB,
		XBGR
	}

	private sealed class DecodeCandidate
	{
		public string Label { get; }

		public byte[] Rgba { get; }

		public double GradientAvg { get; }

		public double ChannelPenalty { get; }

		public double Score { get; set; }

		public DecodeCandidate(string label, byte[] rgba, double gradientAvg, double channelPenalty)
		{
			Label = label;
			Rgba = rgba;
			GradientAvg = gradientAvg;
			ChannelPenalty = channelPenalty;
		}
	}

	private delegate uint Tiler(uint x, uint y, uint width, uint height, uint logBpp);

	private readonly struct DecodeLayout
	{
		public string Label { get; }

		public Tiler? Mapper { get; }

		public DecodeLayout(string label, Tiler? mapper)
		{
			Label = label;
			Mapper = mapper;
		}
	}

	private readonly struct DecodeOverrides
	{
		public List<DecodeLayout> Layouts { get; }

		public EndianMode? Endian { get; }

		public ChannelOrder32? Order { get; }

		public DecodeOverrides(List<DecodeLayout> layouts, EndianMode? endian, ChannelOrder32? order)
		{
			Layouts = layouts;
			Endian = endian;
			Order = order;
		}

		public static DecodeOverrides FromSettings(Settings settings)
		{
			return new DecodeOverrides(ParseLayouts(settings), ParseEndian(settings.Endianness), ParseOrder(settings.Order));
		}

		private static List<DecodeLayout> ParseLayouts(Settings settings)
		{
			List<DecodeLayout> list = new List<DecodeLayout>();
			switch (string.IsNullOrWhiteSpace(settings.Decode) ? "auto" : settings.Decode.Trim().ToLowerInvariant())
			{
			case "auto":
				list.Add(new DecodeLayout("linear", null));
				list.Add(new DecodeLayout("tiled-v1", XgAddress2DTiledV1));
				list.Add(new DecodeLayout("tiled-v2", XgAddress2DTiledV2));
				list.Add(new DecodeLayout("tiled-v3", XgAddress2DTiledV3));
				AddXeniaLayouts(list, settings.XeniaBankXor, settings.XeniaPipeXor);
				return list;
			case "linear":
				list.Add(new DecodeLayout("linear", null));
				break;
			case "tiled-v1":
				list.Add(new DecodeLayout("tiled-v1", XgAddress2DTiledV1));
				break;
			case "tiled-v2":
				list.Add(new DecodeLayout("tiled-v2", XgAddress2DTiledV2));
				break;
			case "tiled-v3":
				list.Add(new DecodeLayout("tiled-v3", XgAddress2DTiledV3));
				break;
			case "tiled-xenia":
				AddXeniaLayouts(list, settings.XeniaBankXor, settings.XeniaPipeXor);
				break;
			default:
				list.Add(new DecodeLayout("linear", null));
				list.Add(new DecodeLayout("tiled-v1", XgAddress2DTiledV1));
				list.Add(new DecodeLayout("tiled-v2", XgAddress2DTiledV2));
				list.Add(new DecodeLayout("tiled-v3", XgAddress2DTiledV3));
				AddXeniaLayouts(list, settings.XeniaBankXor, settings.XeniaPipeXor);
				break;
			}
			return list;
		}

		private static void AddXeniaLayouts(List<DecodeLayout> layouts, int? bankOverride, int? pipeOverride)
		{
			if (bankOverride.HasValue || pipeOverride.HasValue)
			{
				int bankXor = Math.Clamp(bankOverride.GetValueOrDefault(), 0, 1);
				int pipeXor = Math.Clamp(pipeOverride.GetValueOrDefault(), 0, 3);
				layouts.Add(CreateXeniaLayout(bankXor, pipeXor));
				return;
			}
			for (int i = 0; i <= 1; i++)
			{
				for (int j = 0; j <= 3; j++)
				{
					layouts.Add(CreateXeniaLayout(i, j));
				}
			}
		}

		private static DecodeLayout CreateXeniaLayout(int bankXor, int pipeXor)
		{
			return new DecodeLayout($"tiled-xenia-b{bankXor}-p{pipeXor}", (uint x, uint y, uint width, uint height, uint logBpp) => XgAddress2DTiledXenia(x, y, width, height, logBpp, bankXor, pipeXor));
		}

		private static EndianMode? ParseEndian(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}
			return value.Trim().ToLowerInvariant() switch
			{
				"auto" => null, 
				"none" => EndianMode.None, 
				"swap8-16" => EndianMode.Swap8In16, 
				"swap8-32" => EndianMode.Swap8In32, 
				"swap16-32" => EndianMode.Swap16In32, 
				_ => null, 
			};
		}

		private static ChannelOrder32? ParseOrder(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}
			return value.Trim().ToLowerInvariant() switch
			{
				"auto" => null, 
				"bgra" => ChannelOrder32.BGRA, 
				"rgba" => ChannelOrder32.RGBA, 
				"argb" => ChannelOrder32.ARGB, 
				"abgr" => ChannelOrder32.ABGR, 
				"bgrx" => ChannelOrder32.BGRX, 
				"rgbx" => ChannelOrder32.RGBX, 
				"xrgb" => ChannelOrder32.XRGB, 
				"xbgr" => ChannelOrder32.XBGR, 
				_ => null, 
			};
		}
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]--out is required.[/]");
			return 1;
		}
		string outputPath = Path.GetFullPath(settings.Output);
		if (File.Exists(outputPath) && !settings.Force)
		{
			AnsiConsole.MarkupLine("[red]File already exists:[/] " + Markup.Escape(outputPath));
			return 1;
		}
		string format = ResolveFormat(settings, outputPath);
		if (!string.Equals(format, "bmp", StringComparison.OrdinalIgnoreCase) && !string.Equals(format, "raw", StringComparison.OrdinalIgnoreCase))
		{
			AnsiConsole.MarkupLine("[red]Invalid format. Use bmp or raw.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			XbdmScreenshot xbdmScreenshot = await client.CaptureScreenshotAsync(CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(new
				{
					Output = outputPath,
					Format = format,
					Info = xbdmScreenshot.Info,
					Size = xbdmScreenshot.Data.Length
				});
				return 0;
			}
			bool flag;
			if (string.Equals(format, "bmp", StringComparison.OrdinalIgnoreCase))
			{
				DecodeOverrides overrides = DecodeOverrides.FromSettings(settings);
				if (TryWriteBmp(outputPath, xbdmScreenshot.Info, xbdmScreenshot.Data, overrides, settings, out string reason, out string decodeInfo))
				{
					flag = true;
					if (!string.IsNullOrWhiteSpace(decodeInfo))
					{
						AnsiConsole.MarkupLine("[grey]Decode:[/] " + Markup.Escape(decodeInfo));
					}
				}
				else
				{
					AnsiConsole.MarkupLine("[yellow]BMP conversion failed:[/] " + Markup.Escape(reason ?? "unknown error"));
					string text = Path.ChangeExtension(outputPath, ".raw");
					File.WriteAllBytes(text, xbdmScreenshot.Data);
					outputPath = text;
					flag = true;
				}
			}
			else
			{
				File.WriteAllBytes(outputPath, xbdmScreenshot.Data);
				flag = true;
			}
			if (flag)
			{
				AnsiConsole.MarkupLine("[green]Screenshot saved:[/] " + Markup.Escape(outputPath));
			}
			EmitInfoTable(xbdmScreenshot.Info);
			return 0;
		}, CancellationToken.None);
	}

	private static string ResolveFormat(Settings settings, string outputPath)
	{
		if (settings.Raw)
		{
			return "raw";
		}
		if (!string.IsNullOrWhiteSpace(settings.Format))
		{
			return settings.Format.Trim();
		}
		string text = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return "bmp";
	}

	private static void EmitInfoTable(XbdmScreenshotInfo info)
	{
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Screenshot[/]").RuleStyle("grey"));
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[grey]Field[/]"));
		table.AddColumn(new TableColumn("[white]Value[/]"));
		table.AddRow("[grey]Width[/]", info.Width.ToString(CultureInfo.InvariantCulture));
		table.AddRow("[grey]Height[/]", info.Height.ToString(CultureInfo.InvariantCulture));
		table.AddRow("[grey]Pitch[/]", info.Pitch.ToString(CultureInfo.InvariantCulture));
		table.AddRow("[grey]Format[/]", $"0x{info.Format:X8}");
		table.AddRow("[grey]Offset X[/]", info.OffsetX.ToString(CultureInfo.InvariantCulture));
		table.AddRow("[grey]Offset Y[/]", info.OffsetY.ToString(CultureInfo.InvariantCulture));
		table.AddRow("[grey]Framebuffer Size[/]", info.FramebufferSize.ToString(CultureInfo.InvariantCulture));
		AnsiConsole.Write(table);
	}

	private static bool TryWriteBmp(string outputPath, XbdmScreenshotInfo info, byte[] data, DecodeOverrides overrides, Settings settings, out string? reason, out string? decodeInfo)
	{
		reason = null;
		decodeInfo = null;
		if (info.Width == 0 || info.Height == 0)
		{
			reason = "Invalid width/height.";
			return false;
		}
		if (info.Pitch == 0)
		{
			reason = "Invalid pitch.";
			return false;
		}
		int width = (int)info.Width;
		int height = (int)info.Height;
		int pitch = (int)info.Pitch;
		if (width <= 0 || height <= 0 || pitch <= 0)
		{
			reason = "Invalid dimensions.";
			return false;
		}
		if (data.Length < pitch * height)
		{
			reason = "Screenshot buffer is smaller than expected.";
			return false;
		}
		if (pitch % width != 0)
		{
			reason = "Pitch is not aligned to width.";
			return false;
		}
		int num = pitch / width;
		if (num != 2 && num != 4)
		{
			reason = $"Unsupported bytes-per-pixel: {num}";
			return false;
		}
		int num2 = pitch / num;
		int num3 = data.Length / pitch;
		int offsetX = (int)info.OffsetX;
		int offsetY = (int)info.OffsetY;
		if (offsetX < 0 || offsetY < 0 || offsetX >= num2 || offsetY >= num3)
		{
			reason = "Offsets are outside the framebuffer bounds.";
			return false;
		}
		int num4 = Math.Min(width, num2 - offsetX);
		int num5 = Math.Min(height, num3 - offsetY);
		if (num4 <= 0 || num5 <= 0)
		{
			reason = "Invalid output dimensions after offset cropping.";
			return false;
		}
		int num6 = ResolveCropRight(settings, num4);
		if (num6 > 0)
		{
			num4 = Math.Max(1, num4 - num6);
		}
		int num7 = num4 * 4;
		List<DecodeCandidate> list = BuildCandidates(data, num, pitch, num2, num3, offsetX, offsetY, num4, num5, num7, overrides);
		if (list.Count == 0)
		{
			reason = "No valid decode candidates.";
			return false;
		}
		if (settings.DumpVariants)
		{
			DumpCandidateBitmaps(outputPath, num4, num5, num7, list);
		}
		DecodeCandidate decodeCandidate = list.OrderBy((DecodeCandidate c) => c.Score).First();
		decodeInfo = decodeCandidate.Label;
		int num8 = num7 * num5;
		int value = 54 + num8;
		using FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
		using BinaryWriter binaryWriter = new BinaryWriter(output, Encoding.ASCII, leaveOpen: false);
		binaryWriter.Write((ushort)19778);
		binaryWriter.Write(value);
		binaryWriter.Write(0);
		binaryWriter.Write(54);
		binaryWriter.Write(40);
		binaryWriter.Write(num4);
		binaryWriter.Write(-num5);
		binaryWriter.Write((ushort)1);
		binaryWriter.Write((ushort)32);
		binaryWriter.Write(0);
		binaryWriter.Write(num8);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(decodeCandidate.Rgba);
		return true;
	}

	private static int ResolveCropRight(Settings settings, int width)
	{
		int num = Math.Max(0, settings.CropRight);
		if (settings.CropRightPercent.HasValue)
		{
			double num2 = Math.Clamp(settings.CropRightPercent.Value, 0.0, 50.0);
			int val = (int)Math.Round((double)width * (num2 / 100.0));
			num = Math.Max(num, val);
		}
		if (num >= width)
		{
			num = Math.Max(0, width - 1);
		}
		return num;
	}

	private static List<DecodeCandidate> BuildCandidates(byte[] data, int bytesPerPixel, int pitch, int framebufferWidth, int framebufferHeight, int startX, int startY, int outWidth, int outHeight, int outStride, DecodeOverrides overrides)
	{
		List<DecodeCandidate> list = new List<DecodeCandidate>();
		if (bytesPerPixel == 4)
		{
			ChannelOrder32[] array = ((!overrides.Order.HasValue) ? new ChannelOrder32[8]
			{
				ChannelOrder32.BGRA,
				ChannelOrder32.RGBA,
				ChannelOrder32.ARGB,
				ChannelOrder32.ABGR,
				ChannelOrder32.BGRX,
				ChannelOrder32.RGBX,
				ChannelOrder32.XRGB,
				ChannelOrder32.XBGR
			} : new ChannelOrder32[1] { overrides.Order.Value });
			EndianMode[] array2 = ((!overrides.Endian.HasValue) ? new EndianMode[3]
			{
				EndianMode.None,
				EndianMode.Swap8In32,
				EndianMode.Swap16In32
			} : new EndianMode[1] { overrides.Endian.Value });
			foreach (EndianMode endian in array2)
			{
				ChannelOrder32[] array3 = array;
				foreach (ChannelOrder32 value in array3)
				{
					foreach (DecodeLayout layout in overrides.Layouts)
					{
						list.Add(BuildCandidate(layout.Label, endian, value, layout.Mapper));
					}
				}
			}
		}
		else if (bytesPerPixel == 2)
		{
			EndianMode[] array2 = ((!overrides.Endian.HasValue) ? new EndianMode[2]
			{
				EndianMode.None,
				EndianMode.Swap8In16
			} : new EndianMode[1] { overrides.Endian.Value });
			foreach (EndianMode endian2 in array2)
			{
				foreach (DecodeLayout layout2 in overrides.Layouts)
				{
					list.Add(BuildCandidate(layout2.Label, endian2, null, layout2.Mapper));
				}
			}
		}
		if (list.Count == 0)
		{
			return list;
		}
		double num = ComputeMedian(list.Select((DecodeCandidate c) => c.GradientAvg));
		foreach (DecodeCandidate item in list)
		{
			item.Score = Math.Abs(item.GradientAvg - num) * 5.0 + item.ChannelPenalty;
		}
		return list;
		DecodeCandidate BuildCandidate(string layout, EndianMode endianMode, ChannelOrder32? order, Tiler? tiler)
		{
			byte[] rgba = BuildRgbaBuffer(data, bytesPerPixel, pitch, framebufferWidth, framebufferHeight, startX, startY, outWidth, outHeight, outStride, tiler, endianMode, order);
			AnalyzeBuffer(rgba, outWidth, outHeight, outStride, out var gradientAvg, out var channelPenalty);
			return new DecodeCandidate(order.HasValue ? $"{layout} + {endianMode} + {order.Value}" : $"{layout} + {endianMode}", rgba, gradientAvg, channelPenalty);
		}
	}

	private static byte[] BuildRgbaBuffer(byte[] data, int bytesPerPixel, int pitch, int framebufferWidth, int framebufferHeight, int startX, int startY, int outWidth, int outHeight, int outStride, Tiler? tiler, EndianMode endian, ChannelOrder32? order)
	{
		byte[] array = new byte[outStride * outHeight];
		int logBpp = ((bytesPerPixel != 4) ? 1 : 2);
		for (int i = 0; i < outHeight; i++)
		{
			int num = i * outStride;
			for (int j = 0; j < outWidth; j++)
			{
				int num2 = ((int?)tiler?.Invoke((uint)(startX + j), (uint)(startY + i), (uint)framebufferWidth, (uint)framebufferHeight, (uint)logBpp)) ?? ((startY + i) * pitch + (startX + j) * bytesPerPixel);
				int num3 = num + j * 4;
				if (num2 < 0 || num2 + bytesPerPixel > data.Length)
				{
					array[num3] = 0;
					array[num3 + 1] = 0;
					array[num3 + 2] = 0;
					array[num3 + 3] = byte.MaxValue;
				}
				else if (bytesPerPixel == 4)
				{
					byte b = data[num2];
					byte b2 = data[num2 + 1];
					byte b3 = data[num2 + 2];
					byte b4 = data[num2 + 3];
					var (b5, b6, b7, b8) = endian switch
					{
						EndianMode.Swap8In32 => (b4, b3, b2, b), 
						EndianMode.Swap16In32 => (b3, b4, b, b2), 
						_ => (b, b2, b3, b4), 
					};
					var (b9, b10, b11, b12) = order.GetValueOrDefault() switch
					{
						ChannelOrder32.RGBA => (b5, b6, b7, b8), 
						ChannelOrder32.ARGB => (b6, b7, b8, b5), 
						ChannelOrder32.ABGR => (b8, b7, b6, b5), 
						ChannelOrder32.BGRX => (b7, b6, b5, byte.MaxValue), 
						ChannelOrder32.RGBX => (b5, b6, b7, byte.MaxValue), 
						ChannelOrder32.XRGB => (b6, b7, b8, byte.MaxValue), 
						ChannelOrder32.XBGR => (b8, b7, b6, byte.MaxValue), 
						_ => (b7, b6, b5, b8), 
					};
					array[num3] = b11;
					array[num3 + 1] = b10;
					array[num3 + 2] = b9;
					array[num3 + 3] = b12;
				}
				else
				{
					byte b13 = data[num2];
					byte b14 = data[num2 + 1];
					if (endian == EndianMode.Swap8In16)
					{
						byte num4 = b14;
						b14 = b13;
						b13 = num4;
					}
					ushort num5 = (ushort)(b13 | (b14 << 8));
					byte b15 = (byte)(((num5 >> 11) & 0x1F) * 255 / 31);
					byte b16 = (byte)(((num5 >> 5) & 0x3F) * 255 / 63);
					byte b17 = (byte)((num5 & 0x1F) * 255 / 31);
					array[num3] = b17;
					array[num3 + 1] = b16;
					array[num3 + 2] = b15;
					array[num3 + 3] = byte.MaxValue;
				}
			}
		}
		return array;
	}

	private static void AnalyzeBuffer(byte[] rgba, int width, int height, int stride, out double gradientAvg, out double channelPenalty)
	{
		long num = 0L;
		long num2 = 0L;
		long num3 = 0L;
		long num4 = 0L;
		long num5 = 0L;
		long num6 = 0L;
		long num7 = 0L;
		for (int i = 0; i < height; i++)
		{
			int num8 = i * stride;
			for (int j = 1; j < width; j++)
			{
				int num9 = num8 + j * 4;
				int num10 = num8 + (j - 1) * 4;
				int num11 = rgba[num9 + 2] * 77 + rgba[num9 + 1] * 150 + rgba[num9] * 29;
				int num12 = rgba[num10 + 2] * 77 + rgba[num10 + 1] * 150 + rgba[num10] * 29;
				num += Math.Abs(num11 - num12);
			}
		}
		for (int k = 1; k < height; k++)
		{
			int num13 = k * stride;
			int num14 = (k - 1) * stride;
			for (int l = 0; l < width; l++)
			{
				int num15 = num13 + l * 4;
				int num16 = num14 + l * 4;
				int num17 = rgba[num15 + 2] * 77 + rgba[num15 + 1] * 150 + rgba[num15] * 29;
				int num18 = rgba[num16 + 2] * 77 + rgba[num16 + 1] * 150 + rgba[num16] * 29;
				num += Math.Abs(num17 - num18);
				byte b = rgba[num15];
				byte b2 = rgba[num15 + 1];
				byte b3 = rgba[num15 + 2];
				num2 += b3;
				num3 += b2;
				num4 += b;
				num5 += b3 * b3;
				num6 += b2 * b2;
				num7 += b * b;
			}
		}
		int num19 = width * height;
		if (num19 <= 0)
		{
			gradientAvg = double.MaxValue;
			channelPenalty = double.MaxValue;
			return;
		}
		gradientAvg = (double)num / (double)(num19 * 2);
		double num20 = (double)num2 / (double)num19;
		double num21 = (double)num3 / (double)num19;
		double num22 = (double)num4 / (double)num19;
		double val = (double)num5 / (double)num19 - num20 * num20;
		double val2 = (double)num6 / (double)num19 - num21 * num21;
		double val3 = (double)num7 / (double)num19 - num22 * num22;
		val = Math.Max(0.0, val);
		val2 = Math.Max(0.0, val2);
		val3 = Math.Max(0.0, val3);
		double num23 = Math.Max(val, Math.Max(val2, val3));
		double num24 = Math.Min(val, Math.Min(val2, val3));
		channelPenalty = 0.0;
		if (num23 > 0.0 && num24 < num23 * 0.05)
		{
			channelPenalty += (num23 * 0.05 - num24) * 0.01;
		}
		double[] source = new double[3] { num20, num21, num22 };
		double num25 = source.Max();
		double num26 = source.Min();
		double num27 = source.OrderBy((double v) => v).ElementAt(1);
		double num28 = num25 - (num27 + num26) / 2.0;
		if (num28 > 60.0 && num26 < 80.0)
		{
			channelPenalty += num28 * 2.0;
		}
	}

	private static uint XgAddress2DTiledV1(uint x, uint y, uint width, uint height, uint logBpp)
	{
		uint num = (width + 31) & 0xFFFFFFE0u;
		uint num2 = (x >> 5) + (y >> 5) * (num >> 5) << (int)(logBpp + 7);
		uint num3 = (x & 7) + ((y & 0xE) << 2) + ((y & 1) << 3) << (int)logBpp;
		return num2 + num3 + ((x & 0x18) << (int)(logBpp + 2)) + ((y & 0x10) << (int)(logBpp + 3));
	}

	private static uint XgAddress2DTiledV2(uint x, uint y, uint width, uint height, uint logBpp)
	{
		uint num = (width + 31) & 0xFFFFFFE0u;
		uint num2 = (x >> 5) + (y >> 5) * (num >> 5) << (int)(logBpp + 7);
		uint num3 = (x & 7) + ((y & 0xE) << 2) << (int)logBpp;
		return num2 + num3 + ((x & 0x18) << (int)(logBpp + 2)) + ((y & 0x10) << (int)(logBpp + 3));
	}

	private static uint XgAddress2DTiledV3(uint x, uint y, uint width, uint height, uint logBpp)
	{
		uint num = (width + 31) & 0xFFFFFFE0u;
		uint num2 = (y >> 4) * (num >> 5);
		uint num3 = (((x >> 5) + num2 << (int)(logBpp + 6)) & 0xFFFFFFF) << 1;
		uint num4 = (x & 7) + ((y & 6) << 2) << (int)(logBpp + 6) >> 6;
		uint num5 = (y >> 3) & 1;
		uint num6 = num5 + ((((x >> 3) + (num5 << 1)) & 3) << 1);
		uint num7 = (num3 + (num4 & 0xFFFFFFF0u) << 1) + (num4 & 0xF) + ((y & 1) << 4);
		return (uint)((((int)((num6 & 1) << 3) + (((int)num7 >> 6) & 7) << 3) + (int)(num6 & 0xFFFFFFFEu) << 2) + (int)(num7 & 0xFFFFFE00u) << 3) + (num7 & 0x3F);
	}

	private static uint XgAddress2DTiledXenia(uint x, uint y, uint width, uint height, uint logBpp, int bankXor, int pipeXor)
	{
		uint num = (width + 31) & 0xFFFFFFE0u;
		uint num2 = (y >> 5) * (num >> 5) + (x >> 5) << 6;
		int num3 = (int)((((y >> 1) & 7) << 3) | (x & 7));
		uint outerInnerBytes = (num2 | (uint)num3) << (int)logBpp;
		uint num4 = (y >> 4) & 1;
		uint num5 = ((x >> 3) & 3) ^ (((y >> 3) & 1) << 1);
		num4 ^= (uint)(bankXor & 1);
		num5 ^= (uint)(pipeXor & 3);
		return (uint)TiledCombine((int)outerInnerBytes, num4, num5, (int)(y & 1));
	}

	private static int TiledCombine(int outerInnerBytes, uint bank, uint pipe, int yLsb)
	{
		return (int)((uint)(yLsb << 4) | (pipe << 6) | (bank << 11) | (uint)(outerInnerBytes & 0xF) | (uint)(((outerInnerBytes >> 4) & 1) << 5) | (uint)(((outerInnerBytes >> 5) & 7) << 8)) | (outerInnerBytes >> 8 << 12);
	}

	private static void DumpCandidateBitmaps(string outputPath, int width, int height, int stride, IReadOnlyList<DecodeCandidate> candidates)
	{
		string text = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", Path.GetFileNameWithoutExtension(outputPath) + "_variants");
		Directory.CreateDirectory(text);
		foreach (DecodeCandidate candidate in candidates)
		{
			string text2 = string.Concat(candidate.Label.Select((char c) => (!char.IsLetterOrDigit(c)) ? '_' : c));
			WriteBmp(Path.Combine(text, text2 + ".bmp"), width, height, stride, candidate.Rgba);
		}
	}

	private static void WriteBmp(string outputPath, int width, int height, int stride, byte[] rgba)
	{
		int num = stride * height;
		int value = 54 + num;
		using FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
		using BinaryWriter binaryWriter = new BinaryWriter(output, Encoding.ASCII, leaveOpen: false);
		binaryWriter.Write((ushort)19778);
		binaryWriter.Write(value);
		binaryWriter.Write(0);
		binaryWriter.Write(54);
		binaryWriter.Write(40);
		binaryWriter.Write(width);
		binaryWriter.Write(-height);
		binaryWriter.Write((ushort)1);
		binaryWriter.Write((ushort)32);
		binaryWriter.Write(0);
		binaryWriter.Write(num);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(0);
		binaryWriter.Write(rgba);
	}

	private static double ComputeMedian(IEnumerable<double> values)
	{
		double[] array = values.OrderBy((double v) => v).ToArray();
		if (array.Length == 0)
		{
			return 0.0;
		}
		int num = array.Length / 2;
		if (array.Length % 2 == 1)
		{
			return array[num];
		}
		return (array[num - 1] + array[num]) / 2.0;
	}
}
