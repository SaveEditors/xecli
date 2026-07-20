namespace Xbox360.Fatx.Layout;

public static class Xbox360RetailLayoutPlanner
{
    private const long SectorSize = 0x200;

    public static IReadOnlyList<Xbox360PartitionDefinition> Create(long streamLength)
    {
        List<Xbox360PartitionDefinition> definitions = new();
        if (streamLength < 0x01180000L * SectorSize)
            return definitions;

        AddIfWithinBounds(definitions, streamLength, 0, "System Extended", FatxPartitionKind.SystemExtended, 0x00080000L, 0x00020000L);
        AddIfWithinBounds(definitions, streamLength, 1, "System Auxiliary", FatxPartitionKind.SystemAuxiliary, 0x000A0000L, 0x00020000L);
        AddIfWithinBounds(definitions, streamLength, 2, "Compatibility", FatxPartitionKind.Compatibility, 0x00120000L, 0x00FE0000L);

        long contentOffsetSectors = 0x01180000L;
        long contentOffset = contentOffsetSectors * SectorSize;
        if (streamLength > contentOffset)
        {
            definitions.Add(new Xbox360PartitionDefinition
            {
                Index = definitions.Count,
                Name = "Content",
                Kind = FatxPartitionKind.Content,
                Offset = contentOffset,
                Length = streamLength - contentOffset,
            });
        }

        return definitions;
    }

    private static void AddIfWithinBounds(
        List<Xbox360PartitionDefinition> definitions,
        long streamLength,
        int index,
        string name,
        FatxPartitionKind kind,
        long offsetSectors,
        long lengthSectors)
    {
        long offset = offsetSectors * SectorSize;
        long length = lengthSectors * SectorSize;
        if (offset >= streamLength)
            return;

        long boundedLength = Math.Min(length, streamLength - offset);
        if (boundedLength <= 0)
            return;

        definitions.Add(new Xbox360PartitionDefinition
        {
            Index = index,
            Name = name,
            Kind = kind,
            Offset = offset,
            Length = boundedLength,
        });
    }
}
