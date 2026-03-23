namespace Xbox360.Remote;

public sealed record XbdmModuleInfo {
    public required string Name { get; init; }
    public uint BaseAddress { get; init; }
    public uint ModuleSize { get; init; }
    public uint OriginalModuleSize { get; init; }
    public DateTime? Timestamp { get; init; }
    public uint? EntryPoint { get; init; }
    public List<XbdmSectionInfo> Sections { get; } = new();
}

public sealed record XbdmSectionInfo {
    public string? Name { get; init; }
    public uint BaseAddress { get; init; }
    public uint Size { get; init; }
    public uint Index { get; init; }
    public uint Flags { get; init; }
}

public sealed record XbdmFileEntry {
    public required string Name { get; init; }
    public ulong Size { get; init; }
    public DateTime? CreatedUtc { get; init; }
    public DateTime? ModifiedUtc { get; init; }
    public bool IsDirectory { get; init; }
}

public sealed record XbdmDriveEntry {
    public required string Name { get; init; }
    public ulong? TotalBytes { get; init; }
    public ulong? FreeBytes { get; init; }
}

public sealed record XbdmUserInfo {
    public string? Gamertag { get; init; }
    public ulong? Xuid { get; init; }
    public uint? SignInState { get; init; }
    public string RawLine { get; init; } = "";
}

public sealed record XbdmMemoryRegion {
    public uint BaseAddress { get; init; }
    public uint Size { get; init; }
    public uint Protect { get; init; }
    public uint Phys { get; init; }
}

public sealed record XbdmThreadInfo {
    public uint Id { get; init; }
    public uint SuspendCount { get; init; }
    public uint Priority { get; init; }
    public uint TlsBaseAddress { get; init; }
    public uint BaseAddress { get; init; }
    public uint StartAddress { get; init; }
    public uint StackLimit { get; init; }
    public uint StackSlack { get; init; }
    public uint NameAddress { get; init; }
    public uint NameLength { get; init; }
    public uint CurrentProcessor { get; init; }
    public uint LastError { get; init; }
    public string? Name { get; init; }
}

public sealed record XbdmConsoleInfo {
    public string? ConsoleId { get; init; }
    public string? DebugName { get; init; }
    public string? ExecutionState { get; init; }
    public string? TitleIp { get; init; }
    public uint? ProcessId { get; init; }
}
