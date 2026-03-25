using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmScreenshotCommand : AsyncCommand<XbdmScreenshotCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Output file path (.bmp or .raw).")]
        public string? Output { get; init; }

        [CommandOption("--format <FORMAT>")]
        [LocalizedDescription("Output format: bmp or raw.")]
        public string? Format { get; init; }

        [CommandOption("--raw")]
        [LocalizedDescription("Force raw output (same as --format raw).")]
        public bool Raw { get; init; }

        [CommandOption("--force")]
        [LocalizedDescription("Overwrite the output file if it exists.")]
        public bool Force { get; init; }

        [CommandOption("--decode <MODE>")]
        [LocalizedDescription("Force decode mode: auto, linear, tiled-v1, tiled-v2, tiled-v3, tiled-xenia.")]
        public string? Decode { get; init; }

        [CommandOption("--endianness <MODE>")]
        [LocalizedDescription("Force endianness: auto, none, swap8-16, swap8-32, swap16-32.")]
        public string? Endianness { get; init; }

        [CommandOption("--order <ORDER>")]
        [LocalizedDescription("Force 32bpp channel order: auto, bgra, rgba, argb, abgr, bgrx, rgbx, xrgb, xbgr.")]
        public string? Order { get; init; }

        [CommandOption("--dump-variants")]
        [LocalizedDescription("Dump all decode variants to a folder next to the output file.")]
        public bool DumpVariants { get; init; }

        [CommandOption("--crop-right <PX>")]
        [LocalizedDescription("Crop N pixels from the right edge (default: 0).")]
        public int CropRight { get; init; }

        [CommandOption("--crop-right-percent <PCT>")]
        [LocalizedDescription("Crop a percentage from the right edge (0-50).")]
        public double? CropRightPercent { get; init; }

        [CommandOption("--xenia-bank-xor <N>")]
        [LocalizedDescription("Force Xenia tile bank XOR (0-1) when using tiled-xenia.")]
        public int? XeniaBankXor { get; init; }

        [CommandOption("--xenia-pipe-xor <N>")]
        [LocalizedDescription("Force Xenia tile pipe XOR (0-3) when using tiled-xenia.")]
        public int? XeniaPipeXor { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--out is required.[/]");
            return 1;
        }

        string outputPath = Path.GetFullPath(settings.Output);
        if (File.Exists(outputPath) && !settings.Force) {
            AnsiConsole.MarkupLine($"[red]File already exists:[/] {Markup.Escape(outputPath)}");
            return 1;
        }

        string format = ResolveFormat(settings, outputPath);
        if (!string.Equals(format, "bmp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, "raw", StringComparison.OrdinalIgnoreCase)) {
            AnsiConsole.MarkupLine("[red]Invalid format. Use bmp or raw.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            XbdmScreenshot shot = await client.CaptureScreenshotAsync(CancellationToken.None);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Output = outputPath,
                    Format = format,
                    shot.Info,
                    Size = shot.Data.Length
                });
                return 0;
            }

            bool wrote = false;
            if (string.Equals(format, "bmp", StringComparison.OrdinalIgnoreCase)) {
                DecodeOverrides overrides = DecodeOverrides.FromSettings(settings);
                if (TryWriteBmp(outputPath, shot.Info, shot.Data, overrides, settings, out string? reason, out string? decodeInfo)) {
                    wrote = true;
                    if (!string.IsNullOrWhiteSpace(decodeInfo)) {
                        AnsiConsole.MarkupLine($"[grey]Decode:[/] {Markup.Escape(decodeInfo)}");
                    }
                }
                else {
                    AnsiConsole.MarkupLine($"[yellow]BMP conversion failed:[/] {Markup.Escape(reason ?? "unknown error")}");
                    string rawPath = Path.ChangeExtension(outputPath, ".raw");
                    File.WriteAllBytes(rawPath, shot.Data);
                    outputPath = rawPath;
                    wrote = true;
                }
            }
            else {
                File.WriteAllBytes(outputPath, shot.Data);
                wrote = true;
            }

            if (wrote) {
                AnsiConsole.MarkupLine($"[green]Screenshot saved:[/] {Markup.Escape(outputPath)}");
            }

            EmitInfoTable(shot.Info);
            return 0;
        }, CancellationToken.None);
    }

    private static string ResolveFormat(Settings settings, string outputPath) {
        if (settings.Raw)
            return "raw";
        if (!string.IsNullOrWhiteSpace(settings.Format))
            return settings.Format.Trim();
        string ext = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(ext) ? "bmp" : ext;
    }

    private static void EmitInfoTable(XbdmScreenshotInfo info) {
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

    private static bool TryWriteBmp(
        string outputPath,
        XbdmScreenshotInfo info,
        byte[] data,
        DecodeOverrides overrides,
        Settings settings,
        out string? reason,
        out string? decodeInfo) {
        reason = null;
        decodeInfo = null;
        if (info.Width == 0 || info.Height == 0) {
            reason = "Invalid width/height.";
            return false;
        }

        if (info.Pitch == 0) {
            reason = "Invalid pitch.";
            return false;
        }

        int width = (int) info.Width;
        int height = (int) info.Height;
        int pitch = (int) info.Pitch;
        if (width <= 0 || height <= 0 || pitch <= 0) {
            reason = "Invalid dimensions.";
            return false;
        }

        if (data.Length < pitch * height) {
            reason = "Screenshot buffer is smaller than expected.";
            return false;
        }

        if (pitch % width != 0) {
            reason = "Pitch is not aligned to width.";
            return false;
        }

        int bytesPerPixel = pitch / width;
        if (bytesPerPixel != 2 && bytesPerPixel != 4) {
            reason = $"Unsupported bytes-per-pixel: {bytesPerPixel}";
            return false;
        }

        int framebufferWidth = pitch / bytesPerPixel;
        int framebufferHeight = data.Length / pitch;
        int startX = (int) info.OffsetX;
        int startY = (int) info.OffsetY;
        if (startX < 0 || startY < 0 || startX >= framebufferWidth || startY >= framebufferHeight) {
            reason = "Offsets are outside the framebuffer bounds.";
            return false;
        }

        int outWidth = Math.Min(width, framebufferWidth - startX);
        int outHeight = Math.Min(height, framebufferHeight - startY);
        if (outWidth <= 0 || outHeight <= 0) {
            reason = "Invalid output dimensions after offset cropping.";
            return false;
        }

        int cropRight = ResolveCropRight(settings, outWidth);
        if (cropRight > 0) {
            outWidth = Math.Max(1, outWidth - cropRight);
        }

        int outStride = outWidth * 4;
        List<DecodeCandidate> candidates = BuildCandidates(
            data,
            bytesPerPixel,
            pitch,
            framebufferWidth,
            framebufferHeight,
            startX,
            startY,
            outWidth,
            outHeight,
            outStride,
            overrides);

        if (candidates.Count == 0) {
            reason = "No valid decode candidates.";
            return false;
        }

        if (settings.DumpVariants) {
            DumpCandidateBitmaps(outputPath, outWidth, outHeight, outStride, candidates);
        }

        DecodeCandidate best = candidates.OrderBy(c => c.Score).First();
        decodeInfo = best.Label;

        int imageSize = outStride * outHeight;
        int fileSize = 14 + 40 + imageSize;

        using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

        writer.Write((ushort) 0x4D42);
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(14 + 40);

        writer.Write(40);
        writer.Write(outWidth);
        writer.Write(-outHeight);
        writer.Write((ushort) 1);
        writer.Write((ushort) 32);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        writer.Write(best.Rgba);

        return true;
    }

    private static int ResolveCropRight(Settings settings, int width) {
        int crop = Math.Max(0, settings.CropRight);
        if (settings.CropRightPercent.HasValue) {
            double pct = Math.Clamp(settings.CropRightPercent.Value, 0, 50);
            int percentPixels = (int) Math.Round(width * (pct / 100.0));
            crop = Math.Max(crop, percentPixels);
        }

        if (crop >= width)
            crop = Math.Max(0, width - 1);

        return crop;
    }

    private enum EndianMode {
        None,
        Swap8In16,
        Swap8In32,
        Swap16In32
    }

    private enum ChannelOrder32 {
        BGRA,
        RGBA,
        ARGB,
        ABGR,
        BGRX,
        RGBX,
        XRGB,
        XBGR
    }

    private sealed class DecodeCandidate {
        public DecodeCandidate(string label, byte[] rgba, double gradientAvg, double channelPenalty) {
            Label = label;
            Rgba = rgba;
            GradientAvg = gradientAvg;
            ChannelPenalty = channelPenalty;
        }

        public string Label { get; }
        public byte[] Rgba { get; }
        public double GradientAvg { get; }
        public double ChannelPenalty { get; }
        public double Score { get; set; }
    }

    private static List<DecodeCandidate> BuildCandidates(
        byte[] data,
        int bytesPerPixel,
        int pitch,
        int framebufferWidth,
        int framebufferHeight,
        int startX,
        int startY,
        int outWidth,
        int outHeight,
        int outStride,
        DecodeOverrides overrides) {
        List<DecodeCandidate> candidates = new List<DecodeCandidate>();
        if (bytesPerPixel == 4) {
            ChannelOrder32[] orders = overrides.Order.HasValue
                ? new[] { overrides.Order.Value }
                : new[] {
                    ChannelOrder32.BGRA,
                    ChannelOrder32.RGBA,
                    ChannelOrder32.ARGB,
                    ChannelOrder32.ABGR,
                    ChannelOrder32.BGRX,
                    ChannelOrder32.RGBX,
                    ChannelOrder32.XRGB,
                    ChannelOrder32.XBGR
                };

            EndianMode[] endians = overrides.Endian.HasValue
                ? new[] { overrides.Endian.Value }
                : new[] { EndianMode.None, EndianMode.Swap8In32, EndianMode.Swap16In32 };

            foreach (EndianMode endian in endians) {
                foreach (ChannelOrder32 order in orders) {
                    foreach (DecodeLayout layout in overrides.Layouts) {
                        candidates.Add(BuildCandidate(layout.Label, endian, order, layout.Mapper));
                    }
                }
            }
        }
        else if (bytesPerPixel == 2) {
            EndianMode[] endians = overrides.Endian.HasValue
                ? new[] { overrides.Endian.Value }
                : new[] { EndianMode.None, EndianMode.Swap8In16 };

            foreach (EndianMode endian in endians) {
                foreach (DecodeLayout layout in overrides.Layouts) {
                    candidates.Add(BuildCandidate(layout.Label, endian, null, layout.Mapper));
                }
            }
        }

        if (candidates.Count == 0)
            return candidates;

        double median = ComputeMedian(candidates.Select(c => c.GradientAvg));
        foreach (DecodeCandidate candidate in candidates) {
            candidate.Score = Math.Abs(candidate.GradientAvg - median) * 5.0 + candidate.ChannelPenalty;
        }

        return candidates;

        DecodeCandidate BuildCandidate(string layout, EndianMode endian, ChannelOrder32? order, Tiler? tiler) {
            byte[] rgba = BuildRgbaBuffer(
                data,
                bytesPerPixel,
                pitch,
                framebufferWidth,
                framebufferHeight,
                startX,
                startY,
                outWidth,
                outHeight,
                outStride,
                tiler,
                endian,
                order);
            AnalyzeBuffer(rgba, outWidth, outHeight, outStride, out double gradientAvg, out double channelPenalty);
            string label = order.HasValue
                ? $"{layout} + {endian} + {order.Value}"
                : $"{layout} + {endian}";
            return new DecodeCandidate(label, rgba, gradientAvg, channelPenalty);
        }
    }

    private static byte[] BuildRgbaBuffer(
        byte[] data,
        int bytesPerPixel,
        int pitch,
        int framebufferWidth,
        int framebufferHeight,
        int startX,
        int startY,
        int outWidth,
        int outHeight,
        int outStride,
        Tiler? tiler,
        EndianMode endian,
        ChannelOrder32? order) {
        byte[] output = new byte[outStride * outHeight];
        int logBpp = bytesPerPixel == 4 ? 2 : 1;
        for (int y = 0; y < outHeight; y++) {
            int dstRow = y * outStride;
            for (int x = 0; x < outWidth; x++) {
                int srcOffset;
                if (tiler != null) {
                    uint tiled = tiler((uint) (startX + x), (uint) (startY + y), (uint) framebufferWidth, (uint) framebufferHeight, (uint) logBpp);
                    srcOffset = (int) tiled;
                }
                else {
                    srcOffset = (startY + y) * pitch + (startX + x) * bytesPerPixel;
                }

                int dst = dstRow + x * 4;
                if (srcOffset < 0 || srcOffset + bytesPerPixel > data.Length) {
                    output[dst] = 0;
                    output[dst + 1] = 0;
                    output[dst + 2] = 0;
                    output[dst + 3] = 0xFF;
                    continue;
                }

                if (bytesPerPixel == 4) {
                    byte b0 = data[srcOffset];
                    byte b1 = data[srcOffset + 1];
                    byte b2 = data[srcOffset + 2];
                    byte b3 = data[srcOffset + 3];
                    (byte o0, byte o1, byte o2, byte o3) = endian switch {
                        EndianMode.Swap8In32 => (b3, b2, b1, b0),
                        EndianMode.Swap16In32 => (b2, b3, b0, b1),
                        _ => (b0, b1, b2, b3)
                    };

                    ChannelOrder32 orderValue = order ?? ChannelOrder32.BGRA;
                    (byte r, byte g, byte b, byte a) = orderValue switch {
                        ChannelOrder32.RGBA => (o0, o1, o2, o3),
                        ChannelOrder32.ARGB => (o1, o2, o3, o0),
                        ChannelOrder32.ABGR => (o3, o2, o1, o0),
                        ChannelOrder32.BGRX => (o2, o1, o0, (byte) 0xFF),
                        ChannelOrder32.RGBX => (o0, o1, o2, (byte) 0xFF),
                        ChannelOrder32.XRGB => (o1, o2, o3, (byte) 0xFF),
                        ChannelOrder32.XBGR => (o3, o2, o1, (byte) 0xFF),
                        _ => (o2, o1, o0, o3)
                    };

                    output[dst] = b;
                    output[dst + 1] = g;
                    output[dst + 2] = r;
                    output[dst + 3] = a;
                }
                else {
                    byte lo = data[srcOffset];
                    byte hi = data[srcOffset + 1];
                    if (endian == EndianMode.Swap8In16) {
                        (lo, hi) = (hi, lo);
                    }
                    ushort value = (ushort) (lo | (hi << 8));
                    byte r = (byte) (((value >> 11) & 0x1F) * 255 / 31);
                    byte g = (byte) (((value >> 5) & 0x3F) * 255 / 63);
                    byte b = (byte) ((value & 0x1F) * 255 / 31);
                    output[dst] = b;
                    output[dst + 1] = g;
                    output[dst + 2] = r;
                    output[dst + 3] = 0xFF;
                }
            }
        }

        return output;
    }

    private static void AnalyzeBuffer(byte[] rgba, int width, int height, int stride, out double gradientAvg, out double channelPenalty) {
        long gradSum = 0;
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long sumR2 = 0;
        long sumG2 = 0;
        long sumB2 = 0;
        for (int y = 0; y < height; y++) {
            int row = y * stride;
            for (int x = 1; x < width; x++) {
                int idx = row + x * 4;
                int prev = row + (x - 1) * 4;
                int lum = rgba[idx + 2] * 77 + rgba[idx + 1] * 150 + rgba[idx] * 29;
                int lumPrev = rgba[prev + 2] * 77 + rgba[prev + 1] * 150 + rgba[prev] * 29;
                gradSum += Math.Abs(lum - lumPrev);
            }
        }

        for (int y = 1; y < height; y++) {
            int row = y * stride;
            int prevRow = (y - 1) * stride;
            for (int x = 0; x < width; x++) {
                int idx = row + x * 4;
                int prev = prevRow + x * 4;
                int lum = rgba[idx + 2] * 77 + rgba[idx + 1] * 150 + rgba[idx] * 29;
                int lumPrev = rgba[prev + 2] * 77 + rgba[prev + 1] * 150 + rgba[prev] * 29;
                gradSum += Math.Abs(lum - lumPrev);

                byte b = rgba[idx];
                byte g = rgba[idx + 1];
                byte r = rgba[idx + 2];
                sumR += r;
                sumG += g;
                sumB += b;
                sumR2 += r * r;
                sumG2 += g * g;
                sumB2 += b * b;
            }
        }

        int pixels = width * height;
        if (pixels <= 0) {
            gradientAvg = double.MaxValue;
            channelPenalty = double.MaxValue;
            return;
        }

        gradientAvg = gradSum / (double) (pixels * 2);

        double meanR = sumR / (double) pixels;
        double meanG = sumG / (double) pixels;
        double meanB = sumB / (double) pixels;
        double varR = sumR2 / (double) pixels - meanR * meanR;
        double varG = sumG2 / (double) pixels - meanG * meanG;
        double varB = sumB2 / (double) pixels - meanB * meanB;
        varR = Math.Max(0, varR);
        varG = Math.Max(0, varG);
        varB = Math.Max(0, varB);

        double maxVar = Math.Max(varR, Math.Max(varG, varB));
        double minVar = Math.Min(varR, Math.Min(varG, varB));

        channelPenalty = 0;
        if (maxVar > 0 && minVar < maxVar * 0.05) {
            channelPenalty += (maxVar * 0.05 - minVar) * 0.01;
        }

        double[] means = { meanR, meanG, meanB };
        double maxMean = means.Max();
        double minMean = means.Min();
        double midMean = means.OrderBy(v => v).ElementAt(1);
        double dominance = maxMean - (midMean + minMean) / 2.0;
        if (dominance > 60 && minMean < 80) {
            channelPenalty += dominance * 2.0;
        }
    }

    private static uint XgAddress2DTiledV1(uint x, uint y, uint width, uint height, uint logBpp) {
        _ = height;
        uint alignedWidth = (width + 31) & ~31u;
        uint macro = ((x >> 5) + (y >> 5) * (alignedWidth >> 5)) << (int) (logBpp + 7);
        uint micro = ((x & 7) + ((y & 0xE) << 2) + ((y & 1) << 3)) << (int) logBpp;
        uint offset = macro + micro + ((x & 0x18) << (int) (logBpp + 2)) + ((y & 0x10) << (int) (logBpp + 3));
        return offset;
    }

    private static uint XgAddress2DTiledV2(uint x, uint y, uint width, uint height, uint logBpp) {
        _ = height;
        uint alignedWidth = (width + 31) & ~31u;
        uint macro = ((x >> 5) + (y >> 5) * (alignedWidth >> 5)) << (int) (logBpp + 7);
        uint micro = ((x & 7) + ((y & 0xE) << 2)) << (int) logBpp;
        uint offset = macro + micro + ((x & 0x18) << (int) (logBpp + 2)) + ((y & 0x10) << (int) (logBpp + 3));
        return offset;
    }

    private static uint XgAddress2DTiledV3(uint x, uint y, uint width, uint height, uint logBpp) {
        uint pitchH = (width + 31) & ~31u;
        _ = (height + 31) & ~31u;
        uint macroOuter = (y >> 4) * (pitchH >> 5);
        uint macro = ((((x >> 5) + macroOuter) << (int) (logBpp + 6)) & 0xFFFFFFFu) << 1;
        uint micro = (((x & 7) + ((y & 6) << 2)) << (int) (logBpp + 6)) >> 6;
        uint offsetOuter = (y >> 3) & 1;
        uint offset1 = offsetOuter + ((((x >> 3) + (offsetOuter << 1)) & 3) << 1);
        uint offset2 = ((macro + (micro & ~15u)) << 1) + (micro & 15) + ((y & 1) << 4);
        uint address = (offset1 & 1) << 3;
        address += (uint) (((int) offset2 >> 6) & 7);
        address <<= 3;
        address += offset1 & ~1u;
        address <<= 2;
        address += offset2 & ~511u;
        address <<= 3;
        address += offset2 & 63;
        return address;
    }

    private static uint XgAddress2DTiledXenia(uint x, uint y, uint width, uint height, uint logBpp, int bankXor, int pipeXor) {
        _ = height;
        uint pitchAligned = (width + 31) & ~31u;
        int outerBlocks = ((int) (y >> 5) * (int) (pitchAligned >> 5) + (int) (x >> 5)) << 6;
        int innerBlocks = (((int) (y >> 1) & 0b111) << 3) | ((int) x & 0b111);
        int outerInnerBytes = (outerBlocks | innerBlocks) << (int) logBpp;
        uint bank = (y >> 4) & 0b1u;
        uint pipe = ((x >> 3) & 0b11u) ^ (((y >> 3) & 0b1u) << 1);
        bank ^= (uint) (bankXor & 1);
        pipe ^= (uint) (pipeXor & 3);
        int address = TiledCombine(outerInnerBytes, bank, pipe, (int) (y & 1));
        return (uint) address;
    }

    private static int TiledCombine(int outerInnerBytes, uint bank, uint pipe, int yLsb) {
        return (yLsb << 4) | ((int) pipe << 6) | ((int) bank << 11) |
               (outerInnerBytes & 0b1111) |
               (((outerInnerBytes >> 4) & 0b1) << 5) |
               (((outerInnerBytes >> 5) & 0b111) << 8) |
               (outerInnerBytes >> 8 << 12);
    }

    private delegate uint Tiler(uint x, uint y, uint width, uint height, uint logBpp);

    private readonly struct DecodeLayout {
        public DecodeLayout(string label, Tiler? mapper) {
            Label = label;
            Mapper = mapper;
        }

        public string Label { get; }
        public Tiler? Mapper { get; }
    }

    private readonly struct DecodeOverrides {
        public DecodeOverrides(
            List<DecodeLayout> layouts,
            EndianMode? endian,
            ChannelOrder32? order) {
            Layouts = layouts;
            Endian = endian;
            Order = order;
        }

        public List<DecodeLayout> Layouts { get; }
        public EndianMode? Endian { get; }
        public ChannelOrder32? Order { get; }

        public static DecodeOverrides FromSettings(Settings settings) {
            List<DecodeLayout> layouts = ParseLayouts(settings);
            return new DecodeOverrides(layouts, ParseEndian(settings.Endianness), ParseOrder(settings.Order));
        }

        private static List<DecodeLayout> ParseLayouts(Settings settings) {
            List<DecodeLayout> layouts = new List<DecodeLayout>();
            string mode = string.IsNullOrWhiteSpace(settings.Decode) ? "auto" : settings.Decode.Trim().ToLowerInvariant();
            if (mode == "auto") {
                layouts.Add(new DecodeLayout("linear", null));
                layouts.Add(new DecodeLayout("tiled-v1", XgAddress2DTiledV1));
                layouts.Add(new DecodeLayout("tiled-v2", XgAddress2DTiledV2));
                layouts.Add(new DecodeLayout("tiled-v3", XgAddress2DTiledV3));
                AddXeniaLayouts(layouts, settings.XeniaBankXor, settings.XeniaPipeXor);
                return layouts;
            }

            switch (mode) {
                case "linear":
                    layouts.Add(new DecodeLayout("linear", null));
                    break;
                case "tiled-v1":
                    layouts.Add(new DecodeLayout("tiled-v1", XgAddress2DTiledV1));
                    break;
                case "tiled-v2":
                    layouts.Add(new DecodeLayout("tiled-v2", XgAddress2DTiledV2));
                    break;
                case "tiled-v3":
                    layouts.Add(new DecodeLayout("tiled-v3", XgAddress2DTiledV3));
                    break;
                case "tiled-xenia":
                    AddXeniaLayouts(layouts, settings.XeniaBankXor, settings.XeniaPipeXor);
                    break;
                default:
                    layouts.Add(new DecodeLayout("linear", null));
                    layouts.Add(new DecodeLayout("tiled-v1", XgAddress2DTiledV1));
                    layouts.Add(new DecodeLayout("tiled-v2", XgAddress2DTiledV2));
                    layouts.Add(new DecodeLayout("tiled-v3", XgAddress2DTiledV3));
                    AddXeniaLayouts(layouts, settings.XeniaBankXor, settings.XeniaPipeXor);
                    break;
            }

            return layouts;
        }

        private static void AddXeniaLayouts(List<DecodeLayout> layouts, int? bankOverride, int? pipeOverride) {
            if (bankOverride.HasValue || pipeOverride.HasValue) {
                int bank = Math.Clamp(bankOverride ?? 0, 0, 1);
                int pipe = Math.Clamp(pipeOverride ?? 0, 0, 3);
                layouts.Add(CreateXeniaLayout(bank, pipe));
                return;
            }

            for (int bankXor = 0; bankXor <= 1; bankXor++) {
                for (int pipeXor = 0; pipeXor <= 3; pipeXor++) {
                    layouts.Add(CreateXeniaLayout(bankXor, pipeXor));
                }
            }
        }

        private static DecodeLayout CreateXeniaLayout(int bankXor, int pipeXor) {
            return new DecodeLayout(
                $"tiled-xenia-b{bankXor}-p{pipeXor}",
                (x, y, width, height, logBpp) => XgAddress2DTiledXenia(x, y, width, height, logBpp, bankXor, pipeXor));
        }

        private static EndianMode? ParseEndian(string? value) {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string mode = value.Trim().ToLowerInvariant();
            return mode switch {
                "auto" => null,
                "none" => EndianMode.None,
                "swap8-16" => EndianMode.Swap8In16,
                "swap8-32" => EndianMode.Swap8In32,
                "swap16-32" => EndianMode.Swap16In32,
                _ => null
            };
        }

        private static ChannelOrder32? ParseOrder(string? value) {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string mode = value.Trim().ToLowerInvariant();
            return mode switch {
                "auto" => null,
                "bgra" => ChannelOrder32.BGRA,
                "rgba" => ChannelOrder32.RGBA,
                "argb" => ChannelOrder32.ARGB,
                "abgr" => ChannelOrder32.ABGR,
                "bgrx" => ChannelOrder32.BGRX,
                "rgbx" => ChannelOrder32.RGBX,
                "xrgb" => ChannelOrder32.XRGB,
                "xbgr" => ChannelOrder32.XBGR,
                _ => null
            };
        }
    }

    private static void DumpCandidateBitmaps(string outputPath, int width, int height, int stride, IReadOnlyList<DecodeCandidate> candidates) {
        string dir = Path.Combine(Path.GetDirectoryName(outputPath) ?? ".", Path.GetFileNameWithoutExtension(outputPath) + "_variants");
        Directory.CreateDirectory(dir);
        foreach (DecodeCandidate candidate in candidates) {
            string safeLabel = string.Concat(candidate.Label.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            string filePath = Path.Combine(dir, safeLabel + ".bmp");
            WriteBmp(filePath, width, height, stride, candidate.Rgba);
        }
    }

    private static void WriteBmp(string outputPath, int width, int height, int stride, byte[] rgba) {
        int imageSize = stride * height;
        int fileSize = 14 + 40 + imageSize;
        using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

        writer.Write((ushort) 0x4D42);
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(14 + 40);

        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort) 1);
        writer.Write((ushort) 32);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        writer.Write(rgba);
    }

    private static double ComputeMedian(IEnumerable<double> values) {
        double[] sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return 0;
        int mid = sorted.Length / 2;
        if (sorted.Length % 2 == 1)
            return sorted[mid];
        return (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}

