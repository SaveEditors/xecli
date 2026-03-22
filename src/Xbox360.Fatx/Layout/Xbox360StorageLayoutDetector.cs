namespace Xbox360.Fatx.Layout;

public static class Xbox360StorageLayoutDetector
{
    private const long SectorSize = 0x200;

    public static Task<IReadOnlyList<FatxPartition>> DetectAsync(Stream stream, FatxOpenOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.PartitionOffset is long offset)
        {
            var length = options.PartitionLength ?? Math.Max(0, stream.Length - offset);
            return Task.FromResult<IReadOnlyList<FatxPartition>>(new[] {
                new FatxPartition {
                    Index = 0,
                    Name = "Custom",
                    Kind = FatxPartitionKind.Unknown,
                    Offset = offset,
                    Length = length,
                }
            });
        }

        List<Xbox360PartitionDefinition> definitions = DetectRetailLayout(stream.Length);
        if (definitions.Count == 0)
        {
            definitions.Add(new Xbox360PartitionDefinition {
                Index = 0,
                Name = "WholeDisk",
                Kind = FatxPartitionKind.WholeDisk,
                Offset = 0,
                Length = stream.Length,
            });
        }

        IReadOnlyList<FatxPartition> partitions = definitions
            .Select(definition => new FatxPartition
            {
                Index = definition.Index,
                Name = definition.Name,
                Kind = definition.Kind,
                Offset = definition.Offset,
                Length = definition.Length,
            })
            .ToArray();

        return Task.FromResult(partitions);
    }

    private static List<Xbox360PartitionDefinition> DetectRetailLayout(long streamLength)
    {
        List<Xbox360PartitionDefinition> definitions = new List<Xbox360PartitionDefinition>();
        if (streamLength < 0x01180000L * SectorSize)
            return definitions;

        AddIfWithinBounds(definitions, streamLength, 0, "System Extended", FatxPartitionKind.SystemExtended, 0x00080000L, 0x00020000L);
        AddIfWithinBounds(definitions, streamLength, 1, "System Auxiliary", FatxPartitionKind.SystemAuxiliary, 0x000A0000L, 0x00020000L);
        AddIfWithinBounds(definitions, streamLength, 2, "Compatibility", FatxPartitionKind.Compatibility, 0x00120000L, 0x00FE0000L);

        long contentOffsetSectors = 0x01180000L;
        long contentOffset = contentOffsetSectors * SectorSize;
        if (streamLength > contentOffset)
        {
            definitions.Add(new Xbox360PartitionDefinition {
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

        definitions.Add(new Xbox360PartitionDefinition {
            Index = index,
            Name = name,
            Kind = kind,
            Offset = offset,
            Length = boundedLength,
        });
    }
}
