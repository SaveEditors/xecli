using Spectre.Console;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal interface IXbdmAddressResolutionSettings {
    bool Json => false;

    string? Address { get; }

    string? Module { get; }

    string? Rva { get; }

    string? GhidraAddress { get; }

    string? GhidraBase { get; }
}

internal sealed record XbdmResolvedAddress(
    string Source,
    uint Address,
    XbdmModuleInfo? Module,
    uint? Rva,
    uint? GhidraAddress,
    uint? GhidraBase,
    string? Warning) {
    public XbdmResolvedSection? Section { get; init; }
}

internal sealed record XbdmResolvedSection(
    string? Name,
    uint BaseAddress,
    ulong EndAddress,
    uint Size,
    uint? Rva,
    ulong? EndRva,
    uint Flags);

internal static class XbdmAddressResolutionHelpers {
    internal const uint SectionExecuteFlag = 0x00000008;

    internal static bool IsExecutableSection(XbdmResolvedSection? section) {
        return section != null && (section.Flags & SectionExecuteFlag) != 0;
    }

    internal static bool IsRangeInsideSection(XbdmResolvedSection? section, uint address, uint size) {
        if (section == null || size == 0)
            return false;

        ulong rangeStart = address;
        ulong rangeEnd = rangeStart + size;
        return rangeStart >= section.BaseAddress && rangeEnd <= section.EndAddress;
    }

    internal static bool ValidateAddressOptions(IXbdmAddressResolutionSettings settings, string directOptionName = "--addr") {
        bool hasAddress = !string.IsNullOrWhiteSpace(settings.Address);
        bool hasModule = !string.IsNullOrWhiteSpace(settings.Module);
        bool hasRva = !string.IsNullOrWhiteSpace(settings.Rva);
        bool hasGhidra = !string.IsNullOrWhiteSpace(settings.GhidraAddress);
        bool hasGhidraBase = !string.IsNullOrWhiteSpace(settings.GhidraBase);

        if (hasAddress) {
            if (hasModule || hasRva || hasGhidra || hasGhidraBase) {
                return ReportValidationFailure(settings, $"Use {directOptionName} by itself, or use --module with --rva/--ghidra.");
            }

            if (!CliHelpers.TryParseUInt32(settings.Address?.Trim(), out _)) {
                return ReportValidationFailure(settings, $"Invalid {directOptionName}.");
            }

            return true;
        }

        if (!hasModule) {
            return ReportValidationFailure(settings, $"{directOptionName} is required, or use --module with --rva/--ghidra.");
        }

        if (hasRva && hasGhidra) {
            return ReportValidationFailure(settings, "Use either --rva or --ghidra, not both.");
        }

        if (!hasRva && !hasGhidra) {
            return ReportValidationFailure(settings, "--module requires --rva or --ghidra.");
        }

        if (hasRva && hasGhidraBase) {
            return ReportValidationFailure(settings, "--ghidra-base only applies with --ghidra.");
        }

        if (hasRva && !CliHelpers.TryParseUInt32(settings.Rva?.Trim(), out _)) {
            return ReportValidationFailure(settings, "Invalid --rva.");
        }

        uint ghidraAddress = 0;
        uint ghidraBase = 0;
        if (hasGhidra && !CliHelpers.TryParseUInt32(settings.GhidraAddress?.Trim(), out ghidraAddress)) {
            return ReportValidationFailure(settings, "Invalid --ghidra.");
        }

        if (hasGhidra && !CliHelpers.TryParseUInt32(settings.GhidraBase?.Trim(), out ghidraBase)) {
            return ReportValidationFailure(settings, "--ghidra-base is required when using --ghidra.");
        }

        if (hasGhidra && ghidraAddress < ghidraBase) {
            return ReportValidationFailure(settings, "--ghidra must be greater than or equal to --ghidra-base.");
        }

        return true;
    }

    internal static async Task<XbdmResolvedAddress?> ResolveAddressAsync(
        XbdmClient client,
        IXbdmAddressResolutionSettings settings,
        CancellationToken cancellationToken,
        bool printResolution = true,
        bool includeSections = false) {
        if (CliHelpers.TryParseUInt32(settings.Address?.Trim(), out uint directAddress)) {
            return new XbdmResolvedAddress("direct", directAddress, null, null, null, null, null);
        }

        IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(includeSections, cancellationToken);
        XbdmModuleInfo? module;
        try {
            module = FindModule(modules, settings.Module);
        }
        catch (InvalidOperationException ex) {
            ReportResolutionFailure(settings, ex.Message);
            return null;
        }

        if (module == null) {
            string sample = modules.Count > 0
                ? $" Loaded modules include: {string.Join(", ", modules.Take(8).Select(m => m.Name))}."
                : string.Empty;
            ReportResolutionFailure(settings, $"Module not found: {settings.Module ?? string.Empty}.{sample}");

            return null;
        }

        uint rva;
        uint? ghidraAddress = null;
        uint? ghidraBase = null;
        long? ghidraDelta = null;
        string? warning;
        if (!string.IsNullOrWhiteSpace(settings.Rva)) {
            if (!CliHelpers.TryParseUInt32(settings.Rva.Trim(), out rva)) {
                ReportResolutionFailure(settings, "Invalid --rva.");
                return null;
            }
        }
        else {
            if (!CliHelpers.TryParseUInt32(settings.GhidraAddress?.Trim(), out uint parsedGhidraAddress) ||
                !CliHelpers.TryParseUInt32(settings.GhidraBase?.Trim(), out uint parsedGhidraBase)) {
                ReportResolutionFailure(settings, "Invalid --ghidra or --ghidra-base.");
                return null;
            }
            ghidraAddress = parsedGhidraAddress;
            ghidraBase = parsedGhidraBase;
            if (!TryResolveModuleAddress(module, null, ghidraAddress, ghidraBase, out rva, out uint liveAddress, out ghidraDelta, out XbdmResolvedSection? section, out warning, out string? errorMessage)) {
                ReportResolutionFailure(settings, errorMessage ?? "Unable to resolve the requested Ghidra address.");
                return null;
            }

            if (printResolution && ghidraDelta.HasValue)
                AnsiConsole.MarkupLine($"[grey]Ghidra delta:[/] [yellow]{FormatSignedDelta(ghidraDelta.Value)}[/]");

            if (printResolution && !string.IsNullOrWhiteSpace(warning))
                OperationFeedback.WriteWarning(GetWarningTitle(warning), $"[yellow]{Markup.Escape(warning)}[/]");

            if (printResolution)
                AnsiConsole.MarkupLine($"[grey]Resolved:[/] [green]{Markup.Escape(module.Name)}[/] + [cyan]{Hex(rva)}[/] -> [springgreen3_1]{Hex(liveAddress)}[/]");

            return new XbdmResolvedAddress("module", liveAddress, module, rva, ghidraAddress, ghidraBase, warning) {
                Section = section
            };
        }

        if (!TryResolveModuleAddress(module, rva, null, null, out uint directRva, out uint directLiveAddress, out ghidraDelta, out XbdmResolvedSection? directSection, out warning, out string? directErrorMessage)) {
            ReportResolutionFailure(settings, directErrorMessage ?? "Unable to resolve the requested module RVA.");
            return null;
        }

        if (printResolution && !string.IsNullOrWhiteSpace(warning))
            OperationFeedback.WriteWarning(GetWarningTitle(warning), $"[yellow]{Markup.Escape(warning)}[/]");

        if (printResolution)
            AnsiConsole.MarkupLine($"[grey]Resolved:[/] [green]{Markup.Escape(module.Name)}[/] + [cyan]{Hex(directRva)}[/] -> [springgreen3_1]{Hex(directLiveAddress)}[/]");

        return new XbdmResolvedAddress("module", directLiveAddress, module, directRva, ghidraAddress, ghidraBase, warning) {
            Section = directSection
        };
    }

    private static bool ReportValidationFailure(IXbdmAddressResolutionSettings settings, string message) {
        CliValidationOutput.Write(
            settings.Json,
            "Address validation failed",
            message,
            "XBDM_ADDRESS_VALIDATION_FAILED",
            "Use --addr by itself, or use --module with --rva/--ghidra.");
        return false;
    }

    private static void ReportResolutionFailure(IXbdmAddressResolutionSettings settings, string message) {
        CliValidationOutput.Write(
            settings.Json,
            "Address resolution failed",
            message,
            "XBDM_ADDRESS_RESOLUTION_FAILED",
            "Check the live module name and address range, then retry.");
    }

    internal static XbdmModuleInfo? FindModule(IEnumerable<XbdmModuleInfo> modules, string? name) {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string trimmed = name.Trim();
        List<XbdmModuleInfo> exactMatches = new();
        List<XbdmModuleInfo> suffixMatches = new();

        foreach (XbdmModuleInfo module in modules) {
            if (string.Equals(module.Name, trimmed, StringComparison.OrdinalIgnoreCase)) {
                exactMatches.Add(module);
                continue;
            }

            if (string.Equals(Path.GetFileName(module.Name), trimmed, StringComparison.OrdinalIgnoreCase) ||
                module.Name.EndsWith("\\" + trimmed, StringComparison.OrdinalIgnoreCase) ||
                module.Name.EndsWith("/" + trimmed, StringComparison.OrdinalIgnoreCase)) {
                suffixMatches.Add(module);
            }
        }

        if (exactMatches.Count == 1)
            return exactMatches[0];

        if (exactMatches.Count > 1)
            throw new InvalidOperationException(BuildAmbiguousModuleMessage(trimmed, exactMatches));

        if (suffixMatches.Count == 1)
            return suffixMatches[0];

        if (suffixMatches.Count > 1)
            throw new InvalidOperationException(BuildAmbiguousModuleMessage(trimmed, suffixMatches));

        return null;
    }

    private static string BuildAmbiguousModuleMessage(string name, IEnumerable<XbdmModuleInfo> matches) {
        string moduleList = string.Join(", ",
            matches.Select(module => module.Name)
                .OrderBy(moduleName => moduleName, StringComparer.OrdinalIgnoreCase));

        return $"Module '{name}' matched multiple loaded modules: {moduleList}. Use the full module path or a more specific name.";
    }

    internal static XbdmModuleInfo? FindContainingModule(IEnumerable<XbdmModuleInfo> modules, uint address) {
        foreach (XbdmModuleInfo module in modules) {
            uint size = GetModuleSize(module);
            if (size == 0)
                continue;

            ulong start = module.BaseAddress;
            ulong end = start + size;
            if (address >= start && address < end)
                return module;
        }

        return null;
    }

    internal static uint GetModuleSize(XbdmModuleInfo module) =>
        module.ModuleSize != 0 ? module.ModuleSize : module.OriginalModuleSize;

    internal static bool TryResolveModuleAddress(
        XbdmModuleInfo module,
        uint? rva,
        uint? ghidraAddress,
        uint? ghidraBase,
        out uint resolvedRva,
        out uint liveAddress,
        out long? ghidraDelta,
        out string? warning,
        out string? errorMessage) =>
        TryResolveModuleAddress(
            module,
            rva,
            ghidraAddress,
            ghidraBase,
            out resolvedRva,
            out liveAddress,
            out ghidraDelta,
            out _,
            out warning,
            out errorMessage);

    internal static bool TryResolveModuleAddress(
        XbdmModuleInfo module,
        uint? rva,
        uint? ghidraAddress,
        uint? ghidraBase,
        out uint resolvedRva,
        out uint liveAddress,
        out long? ghidraDelta,
        out XbdmResolvedSection? section,
        out string? warning,
        out string? errorMessage) {
        resolvedRva = 0;
        liveAddress = 0;
        ghidraDelta = null;
        section = null;
        warning = null;
        errorMessage = null;

        if (ghidraAddress.HasValue || ghidraBase.HasValue) {
            if (!ghidraAddress.HasValue || !ghidraBase.HasValue) {
                errorMessage = "--ghidra-base is required when using --ghidra.";
                return false;
            }

            if (ghidraAddress.Value < ghidraBase.Value) {
                errorMessage = "--ghidra must be greater than or equal to --ghidra-base.";
                return false;
            }

            resolvedRva = ghidraAddress.Value - ghidraBase.Value;
            ghidraDelta = (long)module.BaseAddress - ghidraBase.Value;
        }
        else if (rva.HasValue) {
            resolvedRva = rva.Value;
        }
        else {
            errorMessage = "--module requires --rva or --ghidra.";
            return false;
        }

        ulong computed = (ulong)module.BaseAddress + resolvedRva;
        if (computed > uint.MaxValue) {
            errorMessage = "Translated live address exceeds 32-bit address space.";
            return false;
        }

        liveAddress = (uint)computed;
        section = FindContainingSection(module, liveAddress);
        warning = BuildResolutionWarning(module, resolvedRva, section);
        return true;
    }

    internal static XbdmResolvedSection? FindContainingSection(XbdmModuleInfo module, uint liveAddress) {
        foreach (XbdmSectionInfo section in module.Sections) {
            if (section.Size == 0)
                continue;

            ulong start = section.BaseAddress;
            ulong end = start + section.Size;
            if ((ulong)liveAddress < start || (ulong)liveAddress >= end)
                continue;

            uint? sectionRva = section.BaseAddress >= module.BaseAddress
                ? section.BaseAddress - module.BaseAddress
                : null;
            ulong? sectionEndRva = sectionRva.HasValue
                ? sectionRva.Value + (ulong)section.Size
                : null;

            return new XbdmResolvedSection(
                section.Name,
                section.BaseAddress,
                end,
                section.Size,
                sectionRva,
                sectionEndRva,
                section.Flags);
        }

        return null;
    }

    internal static string? BuildResolutionWarning(XbdmModuleInfo module, uint rva, XbdmResolvedSection? section) {
        uint moduleSize = GetModuleSize(module);
        if (moduleSize != 0 && rva >= moduleSize)
            return $"RVA {Hex(rva)} is outside reported module size {Hex(moduleSize)}.";

        if (moduleSize != 0 && module.Sections.Count > 0 && section == null)
            return $"RVA {Hex(rva)} is inside reported module size {Hex(moduleSize)} but outside all known module sections.";

        return null;
    }

    internal static string GetWarningTitle(string warning) =>
        warning.Contains("outside all known module sections", StringComparison.OrdinalIgnoreCase)
            ? "Address outside known section"
            : "Address outside module";

    internal static string Hex(uint value) => $"0x{value:X8}";

    internal static string Hex(ulong value) => value <= uint.MaxValue ? $"0x{value:X8}" : $"0x{value:X}";

    internal static string? Hex(uint? value) => value.HasValue ? Hex(value.Value) : null;

    internal static string FormatSignedDelta(long value) {
        string sign = value < 0 ? "-" : "+";
        ulong magnitude = value < 0 ? (ulong)-value : (ulong)value;
        return $"{sign}0x{magnitude:X}";
    }
}
