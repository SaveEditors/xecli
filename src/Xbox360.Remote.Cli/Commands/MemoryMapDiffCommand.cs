using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class MemoryMapDiffCommand : Command<MemoryMapDiffCommand.Settings> {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public sealed class Settings : CommandSettings {
        [CommandOption("--left <FILE>")]
        [LocalizedDescription("Left memory map JSON capture produced by `rgh mem map --json`.")]
        public string? Left { get; init; }

        [CommandOption("--right <FILE>")]
        [LocalizedDescription("Right memory map JSON capture produced by `rgh mem map --json`.")]
        public string? Right { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Left) || string.IsNullOrWhiteSpace(settings.Right))
            return WriteError(settings.Json, "Memory map diff failed", "Provide --left <FILE> and --right <FILE>.", "MEM_MAP_DIFF_INVALID_ARGS", "Save two `rgh mem map --json` captures and try again.");

        if (!MemorySessionStore.TryResolveCaptureReference(settings.Left, out MemorySessionPathResolution leftResolution, out string leftRefIssue))
            return WriteError(settings.Json, "Memory map diff failed", leftRefIssue, "MEM_MAP_DIFF_INPUT_INVALID", "Verify the left file exists, or use @<session>:<label> for a saved session capture.");

        if (!MemorySessionStore.TryResolveCaptureReference(settings.Right, out MemorySessionPathResolution rightResolution, out string rightRefIssue))
            return WriteError(settings.Json, "Memory map diff failed", rightRefIssue, "MEM_MAP_DIFF_INPUT_INVALID", "Verify the right file exists, or use @<session>:<label> for a saved session capture.");

        if (!MemoryMapDiffHelpers.TryLoadSnapshot(leftResolution.FullPath, out MemoryMapDiffHelpers.MemoryMapCapture left, out string leftIssue))
            return WriteError(settings.Json, "Memory map diff failed", leftIssue, "MEM_MAP_DIFF_INPUT_INVALID", "Verify the left file exists and is a saved `rgh mem map --json` capture.");

        if (!MemoryMapDiffHelpers.TryLoadSnapshot(rightResolution.FullPath, out MemoryMapDiffHelpers.MemoryMapCapture right, out string rightIssue))
            return WriteError(settings.Json, "Memory map diff failed", rightIssue, "MEM_MAP_DIFF_INPUT_INVALID", "Verify the right file exists and is a saved `rgh mem map --json` capture.");

        MemoryMapDiffHelpers.MemoryMapDiffResult diff;
        try {
            diff = MemoryMapDiffHelpers.Compare(leftResolution.DisplayPath, rightResolution.DisplayPath, left, right);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or OverflowException) {
            return WriteError(settings.Json, "Memory map diff failed", $"File is not a valid saved memory map capture: {ex.Message}", "MEM_MAP_DIFF_INPUT_INVALID", "Verify both files are saved `rgh mem map --json` captures.");
        }

        if (settings.Json) {
            CliOutput.EmitJson(MemoryMapDiffHelpers.ToJsonPayload(diff));
            return 0;
        }

        RenderHuman(diff);
        return 0;
    }

    private static void RenderHuman(MemoryMapDiffHelpers.MemoryMapDiffResult diff) {
        AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Map Diff[/]").RuleStyle("grey"));

        Table summary = CliOutput.CreateTable();
        summary.AddColumn("[grey]Field[/]");
        summary.AddColumn("[grey]Left[/]");
        summary.AddColumn("[grey]Right[/]");
        summary.AddColumn("[grey]Delta[/]");
        summary.AddRow("Regions", diff.Left.RegionCount.ToString(CultureInfo.InvariantCulture), diff.Right.RegionCount.ToString(CultureInfo.InvariantCulture), DescribeDelta(diff.AddedRegions.Count, diff.RemovedRegions.Count, diff.ChangedRegions.Count));
        summary.AddRow("Modules", diff.Left.ModuleCount.ToString(CultureInfo.InvariantCulture), diff.Right.ModuleCount.ToString(CultureInfo.InvariantCulture), DescribeDelta(diff.AddedModules.Count, diff.RemovedModules.Count, diff.ChangedModules.Count));
        AnsiConsole.Write(summary);

        RenderRegionSection("Added regions", diff.AddedRegions);
        RenderRegionSection("Removed regions", diff.RemovedRegions);
        RenderChangedRegionSection(diff.ChangedRegions);
        RenderModuleSection("Added modules", diff.AddedModules);
        RenderModuleSection("Removed modules", diff.RemovedModules);
        RenderChangedModuleSection(diff.ChangedModules);
    }

    private static void RenderRegionSection(string title, IReadOnlyList<MemoryMapDiffHelpers.MemoryRegionView> regions) {
        if (regions.Count == 0)
            return;

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]{Markup.Escape(title)}[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Base");
        table.AddColumn("Range");
        table.AddColumn("Band");
        table.AddColumn("Label");
        table.AddColumn("Modules");
        foreach (MemoryMapDiffHelpers.MemoryRegionView region in regions) {
            table.AddRow(
                $"[cyan]{region.Base}[/]",
                $"[cyan]{region.Range}[/]",
                $"[green]{Markup.Escape(region.Band)}[/]",
                $"[yellow]{Markup.Escape(region.Label)}[/]",
                FormatModules(region.Modules));
        }

        AnsiConsole.Write(table);
    }

    private static void RenderChangedRegionSection(IReadOnlyList<MemoryMapDiffHelpers.MemoryRegionChange> changes) {
        if (changes.Count == 0)
            return;

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Changed regions[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Base");
        table.AddColumn("Before");
        table.AddColumn("After");
        table.AddColumn("Changes");
        table.AddColumn("Modules");
        foreach (MemoryMapDiffHelpers.MemoryRegionChange change in changes) {
            table.AddRow(
                $"[cyan]{change.Before.Base}[/]",
                $"[grey]{Markup.Escape(change.Before.Range)}[/]",
                $"[grey]{Markup.Escape(change.After.Range)}[/]",
                $"[yellow]{Markup.Escape(string.Join(", ", change.FieldChanges.Select(field => field.Field)))}[/]",
                $"{FormatModules(change.Before.Modules)}\n{FormatModules(change.After.Modules)}");
        }

        AnsiConsole.Write(table);
    }

    private static void RenderModuleSection(string title, IReadOnlyList<MemoryMapDiffHelpers.MemoryModuleOverlapView> modules) {
        if (modules.Count == 0)
            return;

        AnsiConsole.Write(new Rule($"[bold deepskyblue1]{Markup.Escape(title)}[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Name");
        table.AddColumn("Regions");
        foreach (MemoryMapDiffHelpers.MemoryModuleOverlapView module in modules) {
            table.AddRow(
                $"[green]{Markup.Escape(module.Name)}[/]",
                FormatRegionRefs(module.Regions));
        }

        AnsiConsole.Write(table);
    }

    private static void RenderChangedModuleSection(IReadOnlyList<MemoryMapDiffHelpers.MemoryModuleOverlapChange> changes) {
        if (changes.Count == 0)
            return;

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Changed modules[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn("Name");
        table.AddColumn("Before");
        table.AddColumn("After");
        foreach (MemoryMapDiffHelpers.MemoryModuleOverlapChange change in changes) {
            table.AddRow(
                $"[green]{Markup.Escape(change.Name)}[/]",
                FormatRegionRefs(change.Before.Regions),
                FormatRegionRefs(change.After.Regions));
        }

        AnsiConsole.Write(table);
    }

    private static string DescribeDelta(int added, int removed, int changed) {
        List<string> parts = [];
        if (added > 0)
            parts.Add($"+{added}");
        if (removed > 0)
            parts.Add($"-{removed}");
        if (changed > 0)
            parts.Add($"~{changed}");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    private static string FormatModules(IReadOnlyList<string> modules) {
        return modules.Count == 0
            ? "[grey]-[/]"
            : $"[green]{Markup.Escape(string.Join(", ", modules))}[/]";
    }

    private static string FormatRegionRefs(IReadOnlyList<MemoryMapDiffHelpers.MemoryRegionRef> regions) {
        if (regions.Count == 0)
            return "[grey]-[/]";

        return $"[cyan]{Markup.Escape(string.Join(", ", regions.Select(region => region.Range)))}[/]";
    }

    private static int WriteError(bool json, string title, string message, string code, string nextStep) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] { nextStep }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}

internal static class MemoryMapDiffHelpers {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    internal static bool TryLoadSnapshot(string path, out MemoryMapCapture capture, out string issue) {
        capture = new MemoryMapCapture();
        issue = string.Empty;

        if (string.IsNullOrWhiteSpace(path)) {
            issue = "File path is required.";
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string displayPath = GetDisplayPath(path);
        if (!File.Exists(fullPath)) {
            issue = $"File not found: {displayPath}";
            return false;
        }

        try {
            string text = File.ReadAllText(fullPath);
            MemorySnapshotEnvelope<MemoryMapCaptureEnvelope>? wrapped = JsonSerializer.Deserialize<MemorySnapshotEnvelope<MemoryMapCaptureEnvelope>>(text, JsonOptions);
            if (wrapped is not null && wrapped.SchemaVersion > 0 && wrapped.Payload?.Regions is not null) {
                capture = Normalize(new MemoryMapCapture {
                    Path = displayPath,
                    Regions = wrapped.Payload.Regions
                });
                return true;
            }

            MemoryMapCaptureEnvelope? loaded = JsonSerializer.Deserialize<MemoryMapCaptureEnvelope>(text, JsonOptions);
            if (loaded?.Regions is null) {
                issue = "File is not a valid saved memory map capture: Regions must be present and contain an array.";
                return false;
            }

            capture = Normalize(new MemoryMapCapture {
                Path = displayPath,
                Regions = loaded.Regions
            });
            return true;
        }
        catch (JsonException ex) {
            issue = $"File is not a valid saved memory map capture: {ex.Message}";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            issue = BuildReadFailureMessage(displayPath);
            return false;
        }
    }

    internal static MemoryMapDiffResult Compare(string leftPath, string rightPath, MemoryMapCapture left, MemoryMapCapture right) {
        List<MemoryRegionView> leftRegions = left.Regions.Select(ToRegionView).OrderBy(region => region.BaseValue).ToList();
        List<MemoryRegionView> rightRegions = right.Regions.Select(ToRegionView).OrderBy(region => region.BaseValue).ToList();

        Dictionary<uint, MemoryRegionView> leftByBase = leftRegions.ToDictionary(region => region.BaseValue);
        Dictionary<uint, MemoryRegionView> rightByBase = rightRegions.ToDictionary(region => region.BaseValue);
        SortedSet<uint> allBases = new(leftByBase.Keys.Concat(rightByBase.Keys));

        List<MemoryRegionView> addedRegions = [];
        List<MemoryRegionView> removedRegions = [];
        List<MemoryRegionChange> changedRegions = [];

        foreach (uint baseAddress in allBases) {
            bool inLeft = leftByBase.TryGetValue(baseAddress, out MemoryRegionView? leftRegion);
            bool inRight = rightByBase.TryGetValue(baseAddress, out MemoryRegionView? rightRegion);
            if (inLeft && inRight) {
                List<MemoryFieldChange> fields = CompareRegionFields(leftRegion!, rightRegion!);
                if (fields.Count > 0) {
                    changedRegions.Add(new MemoryRegionChange(baseAddress, leftRegion!, rightRegion!, fields));
                }
            }
            else if (inLeft) {
                removedRegions.Add(leftRegion!);
            }
            else if (inRight) {
                addedRegions.Add(rightRegion!);
            }
        }

        Dictionary<string, MemoryModuleOverlapView> leftModules = BuildModules(leftRegions);
        Dictionary<string, MemoryModuleOverlapView> rightModules = BuildModules(rightRegions);
        SortedSet<string> allModules = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in leftModules.Keys)
            allModules.Add(name);
        foreach (string name in rightModules.Keys)
            allModules.Add(name);

        List<MemoryModuleOverlapView> addedModules = [];
        List<MemoryModuleOverlapView> removedModules = [];
        List<MemoryModuleOverlapChange> changedModules = [];

        foreach (string name in allModules) {
            bool inLeft = leftModules.TryGetValue(name, out MemoryModuleOverlapView? leftModule);
            bool inRight = rightModules.TryGetValue(name, out MemoryModuleOverlapView? rightModule);
            if (inLeft && inRight) {
                if (!RegionRefSetsEqual(leftModule!.Regions, rightModule!.Regions))
                    changedModules.Add(new MemoryModuleOverlapChange(name, leftModule!, rightModule!));
            }
            else if (inLeft) {
                removedModules.Add(leftModule!);
            }
            else if (inRight) {
                addedModules.Add(rightModule!);
            }
        }

        return new MemoryMapDiffResult(
            new MemoryMapSide(leftPath, leftRegions.Count, leftModules.Count),
            new MemoryMapSide(rightPath, rightRegions.Count, rightModules.Count),
            addedRegions,
            removedRegions,
            changedRegions,
            addedModules,
            removedModules,
            changedModules);
    }

    internal static object ToJsonPayload(MemoryMapDiffResult diff) {
        return new {
            Left = new {
                diff.Left.Path,
                diff.Left.RegionCount,
                diff.Left.ModuleCount
            },
            Right = new {
                diff.Right.Path,
                diff.Right.RegionCount,
                diff.Right.ModuleCount
            },
            Summary = new {
                AddedRegions = diff.AddedRegions.Count,
                RemovedRegions = diff.RemovedRegions.Count,
                ChangedRegions = diff.ChangedRegions.Count,
                AddedModules = diff.AddedModules.Count,
                RemovedModules = diff.RemovedModules.Count,
                ChangedModules = diff.ChangedModules.Count
            },
            Regions = new {
                Added = diff.AddedRegions.Select(ToJsonRegion).ToArray(),
                Removed = diff.RemovedRegions.Select(ToJsonRegion).ToArray(),
                Changed = diff.ChangedRegions.Select(change => new {
                    Base = Hex(change.BaseValue),
                    Before = ToJsonRegion(change.Before),
                    After = ToJsonRegion(change.After),
                    Changes = change.FieldChanges.Select(field => new {
                        field.Field,
                        field.Left,
                        field.Right
                    }).ToArray()
                }).ToArray()
            },
            Modules = new {
                Added = diff.AddedModules.Select(ToJsonModule).ToArray(),
                Removed = diff.RemovedModules.Select(ToJsonModule).ToArray(),
                Changed = diff.ChangedModules.Select(change => new {
                    change.Name,
                    Before = new {
                        Regions = change.Before.Regions.Select(ToJsonRegionRef).ToArray()
                    },
                    After = new {
                        Regions = change.After.Regions.Select(ToJsonRegionRef).ToArray()
                    }
                }).ToArray()
            }
        };
    }

    private static MemoryMapCapture Normalize(MemoryMapCapture capture) {
        capture.Regions ??= [];
        foreach (MemoryMapCaptureRegion region in capture.Regions) {
            if (region is null)
                throw new JsonException("Regions must not contain null entries.");

            region.Modules ??= [];
            region.Modules = region.Modules
                .Where(module => !string.IsNullOrWhiteSpace(module))
                .Select(module => module.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(module => module, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return capture;
    }

    private static string GetDisplayPath(string path) {
        string fileName = Path.GetFileName(path.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? "input" : fileName;
    }

    private static string BuildReadFailureMessage(string displayPath) {
        return string.IsNullOrWhiteSpace(displayPath)
            ? "Failed to read memory map capture."
            : $"Failed to read memory map capture '{displayPath}'.";
    }

    private static MemoryRegionView ToRegionView(MemoryMapCaptureRegion region) {
        uint baseValue = ParseHex32(region.Base, nameof(region.Base));
        uint endValue = ParseHex32(region.End, nameof(region.End));
        uint sizeValue = ParseHex32(region.Size, nameof(region.Size));
        return new MemoryRegionView(
            baseValue,
            Hex(baseValue),
            Hex(endValue),
            Hex(sizeValue),
            region.Band?.Trim() ?? string.Empty,
            region.Label?.Trim() ?? string.Empty,
            region.SafetyNote?.Trim() ?? string.Empty,
            Hex(ParseHex32(region.Protect, nameof(region.Protect))),
            Hex(ParseHex32(region.Phys, nameof(region.Phys))),
            region.Modules?.Where(module => !string.IsNullOrWhiteSpace(module)).Select(module => module.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(module => module, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>());
    }

    private static List<MemoryFieldChange> CompareRegionFields(MemoryRegionView left, MemoryRegionView right) {
        List<MemoryFieldChange> fields = [];
        CompareField(fields, "End", left.End, right.End);
        CompareField(fields, "Size", left.Size, right.Size);
        CompareField(fields, "Band", left.Band, right.Band);
        CompareField(fields, "Label", left.Label, right.Label);
        CompareField(fields, "SafetyNote", left.SafetyNote, right.SafetyNote);
        CompareField(fields, "Protect", left.Protect, right.Protect);
        CompareField(fields, "Phys", left.Phys, right.Phys);
        if (!RegionRefSetsEqual(left.Modules, right.Modules)) {
            fields.Add(new MemoryFieldChange("Modules", string.Join(", ", left.Modules), string.Join(", ", right.Modules)));
        }

        return fields;
    }

    private static void CompareField(List<MemoryFieldChange> fields, string field, string left, string right) {
        if (!string.Equals(left, right, StringComparison.Ordinal))
            fields.Add(new MemoryFieldChange(field, left, right));
    }

    private static Dictionary<string, MemoryModuleOverlapView> BuildModules(IReadOnlyList<MemoryRegionView> regions) {
        Dictionary<string, List<MemoryRegionRef>> moduleMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (MemoryRegionView region in regions) {
            foreach (string module in region.Modules) {
                if (!moduleMap.TryGetValue(module, out List<MemoryRegionRef>? refs)) {
                    refs = [];
                    moduleMap[module] = refs;
                }

                refs.Add(new MemoryRegionRef(region.Base, region.End, region.Range, region.Band, region.Label));
            }
        }

        Dictionary<string, MemoryModuleOverlapView> modules = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, List<MemoryRegionRef> refs) in moduleMap) {
            List<MemoryRegionRef> orderedRefs = refs
                .Distinct()
                .OrderBy(region => region.BaseValue)
                .ThenBy(region => region.EndValue)
                .ToList();
            modules[name] = new MemoryModuleOverlapView(name, orderedRefs);
        }

        return modules;
    }

    private static bool RegionRefSetsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++) {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool RegionRefSetsEqual(IReadOnlyList<MemoryRegionRef> left, IReadOnlyList<MemoryRegionRef> right) {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++) {
            if (!left[i].Equals(right[i]))
                return false;
        }

        return true;
    }

    private static MemoryMapRegionJson ToJsonRegion(MemoryRegionView region) {
        return new MemoryMapRegionJson(
            region.Base,
            region.End,
            region.Size,
            region.Band,
            region.Label,
            region.SafetyNote,
            region.Protect,
            region.Phys,
            region.Modules);
    }

    private static object ToJsonModule(MemoryModuleOverlapView module) {
        return new {
            module.Name,
            Regions = module.Regions.Select(ToJsonRegionRef).ToArray()
        };
    }

    private static object ToJsonRegionRef(MemoryRegionRef region) {
        return new {
            region.Base,
            region.End,
            region.Range,
            region.Band,
            region.Label
        };
    }

    private static uint ParseHex32(string? value, string fieldName) {
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException($"Missing {fieldName}.");

        string text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed))
            throw new JsonException($"Invalid hex value for {fieldName}: {value}");

        return parsed;
    }

    private static string Hex(uint value) {
        return $"0x{value:X8}";
    }

    internal sealed class MemoryMapCapture {
        public string? Path { get; set; }

        public List<MemoryMapCaptureRegion> Regions { get; set; } = [];

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class MemoryMapCaptureEnvelope {
        public List<MemoryMapCaptureRegion>? Regions { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed class MemoryMapCaptureRegion {
        public string? Base { get; set; }
        public string? End { get; set; }
        public string? Size { get; set; }
        public string? Band { get; set; }
        public string? Label { get; set; }
        public string? SafetyNote { get; set; }
        public string? Protect { get; set; }
        public string? Phys { get; set; }
        public List<string> Modules { get; set; } = [];

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraData { get; set; }
    }

    internal sealed record MemoryMapDiffResult(
        MemoryMapSide Left,
        MemoryMapSide Right,
        IReadOnlyList<MemoryRegionView> AddedRegions,
        IReadOnlyList<MemoryRegionView> RemovedRegions,
        IReadOnlyList<MemoryRegionChange> ChangedRegions,
        IReadOnlyList<MemoryModuleOverlapView> AddedModules,
        IReadOnlyList<MemoryModuleOverlapView> RemovedModules,
        IReadOnlyList<MemoryModuleOverlapChange> ChangedModules);

    internal sealed record MemoryMapSide(string Path, int RegionCount, int ModuleCount);

    internal sealed record MemoryRegionView(
        uint BaseValue,
        string Base,
        string End,
        string Size,
        string Band,
        string Label,
        string SafetyNote,
        string Protect,
        string Phys,
        IReadOnlyList<string> Modules) {
        public string Range => $"{Base}..{End}";
    }

    internal sealed record MemoryRegionChange(
        uint BaseValue,
        MemoryRegionView Before,
        MemoryRegionView After,
        IReadOnlyList<MemoryFieldChange> FieldChanges);

    internal sealed record MemoryFieldChange(string Field, string Left, string Right);

    internal sealed record MemoryModuleOverlapView(string Name, IReadOnlyList<MemoryRegionRef> Regions);

    internal sealed record MemoryModuleOverlapChange(string Name, MemoryModuleOverlapView Before, MemoryModuleOverlapView After);

    internal sealed record MemoryRegionRef(
        string Base,
        string End,
        string Range,
        string Band,
        string Label) {
        public uint BaseValue => ParseHex32(Base, nameof(Base));
        public uint EndValue => ParseHex32(End, nameof(End));
    }

    internal sealed record MemoryMapRegionJson(
        string Base,
        string End,
        string Size,
        string Band,
        string Label,
        string SafetyNote,
        string Protect,
        string Phys,
        IReadOnlyList<string> Modules);
}
