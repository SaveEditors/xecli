using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDebugAddressCheckCommand : AsyncCommand<XbdmDebugAddressCheckCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [LocalizedDescription("Live console address. Use this when you already have the XBDM address.")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Live console module name. Exact full module path is safest; unique leaf or suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address inside the live console module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra-translated address for the selected module.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Ghidra image base used to translate addresses back to the live console.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--sections")]
        [LocalizedDescription("Include section rows for the resolved live console module.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(
                client,
                settings,
                cts.Token,
                printResolution: false,
                includeSections: true);
            if (resolved == null)
                return 1;

            IReadOnlyList<XbdmModuleInfo> modules = resolved.Module == null
                ? await client.GetModulesAsync(includeSections: true, cancellationToken: cts.Token)
                : new[] { resolved.Module };
            XbdmModuleInfo? module = ResolveModule(resolved, modules);
            uint? rva = ResolveRva(resolved.Address, resolved.Rva, module);
            XbdmResolvedSection? section = ResolveSection(resolved, module);
            string? warning = BuildWarning(resolved.Warning, resolved.Address, rva, module, section);
            bool executable = XbdmAddressResolutionHelpers.IsExecutableSection(section);
            bool safeForCodeBreakpoint = section != null && executable;
            string safetyReason = BuildSafetyReason(module, section, executable);
            string? ghidraDelta = module != null && resolved.GhidraBase.HasValue
                ? XbdmAddressResolutionHelpers.FormatSignedDelta((long)module.BaseAddress - resolved.GhidraBase.Value)
                : null;

            if (settings.Json)
                WriteJson(settings, resolved, module, rva, section, executable, safeForCodeBreakpoint, safetyReason, ghidraDelta, warning);
            else
                WriteTable(settings, resolved, module, rva, section, executable, safeForCodeBreakpoint, safetyReason, ghidraDelta, warning);

            return 0;
        }, CancellationToken.None);
    }

    private static XbdmModuleInfo? ResolveModule(XbdmResolvedAddress resolved, IReadOnlyList<XbdmModuleInfo> modules) {
        if (string.Equals(resolved.Source, "direct", StringComparison.OrdinalIgnoreCase))
            return XbdmAddressResolutionHelpers.FindContainingModule(modules, resolved.Address);

        if (resolved.Module == null)
            return null;

        try {
            return XbdmAddressResolutionHelpers.FindModule(modules, resolved.Module.Name) ?? resolved.Module;
        }
        catch (InvalidOperationException) {
            return resolved.Module;
        }
    }

    private static uint? ResolveRva(uint liveAddress, uint? resolvedRva, XbdmModuleInfo? module) {
        if (resolvedRva.HasValue)
            return resolvedRva.Value;

        if (module == null || liveAddress < module.BaseAddress)
            return null;

        return liveAddress - module.BaseAddress;
    }

    private static XbdmResolvedSection? ResolveSection(XbdmResolvedAddress resolved, XbdmModuleInfo? module) {
        if (module == null)
            return resolved.Section;

        return module.Sections.Count > 0
            ? XbdmAddressResolutionHelpers.FindContainingSection(module, resolved.Address)
            : resolved.Section;
    }

    private static string? BuildWarning(string? existingWarning, uint liveAddress, uint? rva, XbdmModuleInfo? module, XbdmResolvedSection? section) {
        string? warning = existingWarning;
        if (module == null)
            return AppendWarning(warning, "Live console address does not fall inside a reported module.");

        if (rva.HasValue)
            warning = AppendWarning(warning, XbdmAddressResolutionHelpers.BuildResolutionWarning(module, rva.Value, section));

        uint size = XbdmAddressResolutionHelpers.GetModuleSize(module);
        ulong moduleEnd = (ulong)module.BaseAddress + size;
        if (size != 0 && (liveAddress < module.BaseAddress || liveAddress >= moduleEnd))
            warning = AppendWarning(warning, "Live console address is outside the reported module range.");

        return warning;
    }

    private static string? AppendWarning(string? current, string? next) {
        if (string.IsNullOrWhiteSpace(next))
            return current;

        if (string.IsNullOrWhiteSpace(current))
            return next;
        return current.Contains(next, StringComparison.Ordinal) ? current : $"{current} {next}";
    }

    private static string BuildSafetyReason(
        XbdmModuleInfo? module,
        XbdmResolvedSection? section,
        bool executable) {
        if (module == null)
            return "Address is outside every reported module.";
        if (section == null)
            return "Address is outside every known module section.";
        if (!executable)
            return $"Section '{section.Name ?? "unnamed"}' is not executable.";
        return $"Address is inside executable section '{section.Name ?? "unnamed"}'.";
    }

    private static void WriteJson(
        Settings settings,
        XbdmResolvedAddress resolved,
        XbdmModuleInfo? module,
        uint? rva,
        XbdmResolvedSection? sectionMatch,
        bool executable,
        bool safeForCodeBreakpoint,
        string safetyReason,
        string? ghidraDelta,
        string? warning) {
        object? moduleJson = module == null
            ? null
            : new {
                module.Name,
                Base = XbdmAddressResolutionHelpers.Hex(module.BaseAddress),
                Size = XbdmAddressResolutionHelpers.Hex(XbdmAddressResolutionHelpers.GetModuleSize(module)),
                ModuleSize = XbdmAddressResolutionHelpers.Hex(XbdmAddressResolutionHelpers.GetModuleSize(module)),
                    OriginalSize = XbdmAddressResolutionHelpers.Hex(module.OriginalModuleSize),
                    Sections = settings.Sections
                        ? module.Sections.Select(section => new {
                            Name = section.Name,
                        Base = XbdmAddressResolutionHelpers.Hex(section.BaseAddress),
                        Size = XbdmAddressResolutionHelpers.Hex(section.Size),
                        Rva = section.BaseAddress >= module.BaseAddress ? XbdmAddressResolutionHelpers.Hex(section.BaseAddress - module.BaseAddress) : null,
                        Flags = XbdmAddressResolutionHelpers.Hex(section.Flags)
                    }).ToArray()
                    : null
            };

        CliOutput.EmitJson(new {
            Source = resolved.Source,
            LiveAddress = XbdmAddressResolutionHelpers.Hex(resolved.Address),
            Module = moduleJson,
            Rva = XbdmAddressResolutionHelpers.Hex(rva),
            Section = sectionMatch == null
                ? null
                : new {
                    sectionMatch.Name,
                    Base = XbdmAddressResolutionHelpers.Hex(sectionMatch.BaseAddress),
                    End = XbdmAddressResolutionHelpers.Hex(sectionMatch.EndAddress),
                    Size = XbdmAddressResolutionHelpers.Hex(sectionMatch.Size),
                    Rva = XbdmAddressResolutionHelpers.Hex(sectionMatch.Rva),
                    EndRva = sectionMatch.EndRva.HasValue ? XbdmAddressResolutionHelpers.Hex(sectionMatch.EndRva.Value) : null,
                    Flags = XbdmAddressResolutionHelpers.Hex(sectionMatch.Flags)
                },
            Safety = new {
                ModuleKnown = module != null,
                SectionKnown = sectionMatch != null,
                Executable = executable,
                SafeForCodeBreakpoint = safeForCodeBreakpoint,
                Reason = safetyReason,
                SectionFlags = sectionMatch == null
                    ? null
                    : XbdmAddressResolutionHelpers.Hex(sectionMatch.Flags)
            },
            GhidraBase = XbdmAddressResolutionHelpers.Hex(resolved.GhidraBase),
            GhidraAddress = XbdmAddressResolutionHelpers.Hex(resolved.GhidraAddress),
            GhidraDelta = ghidraDelta,
            Warning = warning
        });
    }

    private static void WriteTable(
        Settings settings,
        XbdmResolvedAddress resolved,
        XbdmModuleInfo? module,
        uint? rva,
        XbdmResolvedSection? sectionMatch,
        bool executable,
        bool safeForCodeBreakpoint,
        string safetyReason,
        string? ghidraDelta,
        string? warning) {
        Table table = CliOutput.CreateTable();
        table.AddColumn("[grey]Field[/]");
        table.AddColumn("[grey]Value[/]");
        table.AddRow("[grey]Source[/]", Markup.Escape(resolved.Source));
        table.AddRow("[grey]Live console address[/]", $"[cyan]{XbdmAddressResolutionHelpers.Hex(resolved.Address)}[/]");
        table.AddRow("[grey]Module[/]", module == null ? "[yellow]not found[/]" : $"[green]{Markup.Escape(module.Name)}[/]");
        table.AddRow("[grey]Base[/]", module == null ? "[grey]n/a[/]" : $"[cyan]{XbdmAddressResolutionHelpers.Hex(module.BaseAddress)}[/]");
        table.AddRow("[grey]Size[/]", module == null ? "[grey]n/a[/]" : $"[cyan]{XbdmAddressResolutionHelpers.Hex(XbdmAddressResolutionHelpers.GetModuleSize(module))}[/]");
        table.AddRow("[grey]RVA[/]", rva.HasValue ? $"[gold1]{XbdmAddressResolutionHelpers.Hex(rva.Value)}[/]" : "[grey]n/a[/]");
        table.AddRow("[grey]Ghidra base[/]", resolved.GhidraBase.HasValue ? $"[cyan]{XbdmAddressResolutionHelpers.Hex(resolved.GhidraBase.Value)}[/]" : "[grey]n/a[/]");
        table.AddRow("[grey]Ghidra translated address[/]", resolved.GhidraAddress.HasValue ? $"[cyan]{XbdmAddressResolutionHelpers.Hex(resolved.GhidraAddress.Value)}[/]" : "[grey]n/a[/]");
        table.AddRow("[grey]Ghidra translation delta[/]", string.IsNullOrWhiteSpace(ghidraDelta) ? "[grey]n/a[/]" : $"[yellow]{Markup.Escape(ghidraDelta)}[/]");
        table.AddRow("[grey]Section[/]", FormatSection(sectionMatch));
        table.AddRow("[grey]Executable[/]", executable ? "[green]yes[/]" : "[yellow]no[/]");
        table.AddRow("[grey]Code breakpoint safety[/]", safeForCodeBreakpoint ? "[green]verified[/]" : "[red]not verified[/]");
        table.AddRow("[grey]Safety reason[/]", Markup.Escape(safetyReason));
        table.AddRow("[grey]Warning[/]", string.IsNullOrWhiteSpace(warning) ? "[green]none[/]" : $"[yellow]{Markup.Escape(warning)}[/]");
        AnsiConsole.Write(table);

        if (!settings.Sections || module == null)
            return;

        Table sections = CliOutput.CreateTable();
        sections.AddColumn("Section");
        sections.AddColumn("Base");
        sections.AddColumn("RVA");
        sections.AddColumn("Size");
        sections.AddColumn("Flags");
        foreach (XbdmSectionInfo section in module.Sections) {
            string sectionRva = section.BaseAddress >= module.BaseAddress
                ? XbdmAddressResolutionHelpers.Hex(section.BaseAddress - module.BaseAddress)
                : "n/a";
            sections.AddRow(
                Markup.Escape(section.Name ?? $"#{section.Index}"),
                $"[cyan]{XbdmAddressResolutionHelpers.Hex(section.BaseAddress)}[/]",
                $"[gold1]{sectionRva}[/]",
                $"[cyan]{XbdmAddressResolutionHelpers.Hex(section.Size)}[/]",
                $"[grey]{XbdmAddressResolutionHelpers.Hex(section.Flags)}[/]");
        }

        AnsiConsole.Write(sections);
    }

    private static string FormatSection(XbdmResolvedSection? section) {
        if (section == null)
            return "[grey]n/a[/]";

        string name = string.IsNullOrWhiteSpace(section.Name) ? "(unnamed)" : section.Name;
        return $"[green]{Markup.Escape(name)}[/] [grey]{XbdmAddressResolutionHelpers.Hex(section.BaseAddress)}..{XbdmAddressResolutionHelpers.Hex(section.EndAddress)} flags {XbdmAddressResolutionHelpers.Hex(section.Flags)}[/]";
    }
}
