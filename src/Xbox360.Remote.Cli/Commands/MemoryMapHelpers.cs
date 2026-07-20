using System.IO;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal static class MemoryMapHelpers {
    private static readonly MemoryBandDefinition[] Bands = [
        new MemoryBandDefinition(
            0x00000000,
            0x0000FFFF,
            "low/null guard band",
            "guard/null",
            "Read-only inspection is usually meaningless here; treat it as guard space."),
        new MemoryBandDefinition(
            0x00010000,
            0x7FFFFFFF,
            "user/title band",
            "title memory",
            "Safe to inspect read-only, but contents are volatile and game-specific."),
        new MemoryBandDefinition(
            0x80000000,
            0x8FFFFFFF,
            "kernel/debug band",
            "kernel/debug",
            "Kernel and debugger space. Keep inspection read-only."),
        new MemoryBandDefinition(
            0x90000000,
            0x9FFFFFFF,
            "system/title band",
            "system/title",
            "Dashboard and system-service space. Inspect only."),
        new MemoryBandDefinition(
            0xA0000000,
            0xBFFFFFFF,
            "physical alias band",
            "physical alias",
            "Alias or MMIO-like space. Inspect only and expect platform-specific behavior."),
        new MemoryBandDefinition(
            0xC0000000,
            0xFFFFFFFF,
            "high virtual band",
            "high virtual",
            "High virtual map. Keep it read-only.")
    ];

    internal static IReadOnlyList<MemoryBandDefinition> GetBands() => Bands;

    internal static MemoryBandDefinition ClassifyBand(uint address) {
        foreach (MemoryBandDefinition band in Bands) {
            if (address >= band.Start && address <= band.EndInclusive)
                return band;
        }

        return Bands[^1];
    }

    internal static uint ComputeEnd(uint baseAddress, uint size) {
        if (size == 0)
            return baseAddress;

        ulong end = (ulong) baseAddress + size - 1;
        return end > uint.MaxValue ? uint.MaxValue : (uint) end;
    }

    internal static MemoryMapSnapshot BuildSnapshot(IReadOnlyList<XbdmMemoryRegion> regions, IReadOnlyList<XbdmModuleInfo>? modules = null) {
        return new MemoryMapSnapshot(
            BuildBandSnapshots(),
            BuildRegionSnapshots(regions, modules));
    }

    internal static IReadOnlyList<XbdmMemoryRegion> BuildRawRegionsJsonPayload(IReadOnlyList<XbdmMemoryRegion> regions) {
        return regions;
    }

    internal static MemoryMapSnapshot BuildMapJsonPayload(IReadOnlyList<XbdmMemoryRegion> regions, IReadOnlyList<XbdmModuleInfo>? modules = null) {
        return BuildSnapshot(regions, modules);
    }

    internal static IReadOnlyList<MemoryBandSnapshot> BuildBandSnapshots() {
        return Bands.Select(band => new MemoryBandSnapshot(
            FormatHex(band.Start),
            FormatHex(band.EndInclusive),
            band.Name,
            band.Label,
            band.SafetyNote)).ToArray();
    }

    internal static IReadOnlyList<MemoryRegionSnapshot> BuildRegionSnapshots(IReadOnlyList<XbdmMemoryRegion> regions, IReadOnlyList<XbdmModuleInfo>? modules = null) {
        List<MemoryRegionSnapshot> snapshots = new List<MemoryRegionSnapshot>(regions.Count);
        foreach (XbdmMemoryRegion region in regions.OrderBy(region => region.BaseAddress)) {
            MemoryBandDefinition band = ClassifyBand(region.BaseAddress);
            IReadOnlyList<string> mappedModules = FindMappedModules(region, modules);
            snapshots.Add(new MemoryRegionSnapshot(
                FormatHex(region.BaseAddress),
                FormatHex(ComputeEnd(region.BaseAddress, region.Size)),
                FormatHex(region.Size),
                band.Name,
                BuildRegionLabel(band, mappedModules),
                band.SafetyNote,
                FormatHex(region.Protect),
                FormatHex(region.Phys),
                mappedModules));
        }

        return snapshots;
    }

    private static IReadOnlyList<string> FindMappedModules(XbdmMemoryRegion region, IReadOnlyList<XbdmModuleInfo>? modules) {
        if (modules == null || modules.Count == 0 || region.Size == 0)
            return Array.Empty<string>();

        ulong regionStart = region.BaseAddress;
        ulong regionEnd = ComputeEnd(region.BaseAddress, region.Size);
        List<string> mapped = new List<string>();
        foreach (XbdmModuleInfo module in modules.OrderBy(module => module.BaseAddress)) {
            uint moduleSize = XbdmAddressResolutionHelpers.GetModuleSize(module);
            ulong moduleStart = module.BaseAddress;
            ulong moduleEnd = ComputeEnd(module.BaseAddress, moduleSize);
            if (moduleEnd < regionStart || moduleStart > regionEnd)
                continue;

            mapped.Add(GetModuleLeafName(module.Name));
        }

        return mapped;
    }

    private static string BuildRegionLabel(MemoryBandDefinition band, IReadOnlyList<string> modules) {
        if (modules.Count == 0)
            return band.Label;

        if (modules.Count == 1)
            return $"module: {modules[0]}";

        return $"modules: {modules[0]} +{modules.Count - 1} more";
    }

    private static string FormatHex(uint value) {
        return $"0x{value:X8}";
    }

    private static string GetModuleLeafName(string value) {
        string normalized = value.Replace('/', '\\');
        string leaf = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(leaf) ? value.Trim() : leaf;
    }

    internal sealed record MemoryBandDefinition(
        uint Start,
        uint EndInclusive,
        string Name,
        string Label,
        string SafetyNote);

    internal sealed record MemoryBandSnapshot(
        string Start,
        string End,
        string Name,
        string Label,
        string SafetyNote);

    internal sealed record MemoryRegionSnapshot(
        string Base,
        string End,
        string Size,
        string Band,
        string Label,
        string SafetyNote,
        string Protect,
        string Phys,
        IReadOnlyList<string> Modules);

    internal sealed record MemoryMapSnapshot(
        IReadOnlyList<MemoryBandSnapshot> Bands,
        IReadOnlyList<MemoryRegionSnapshot> Regions);
}
