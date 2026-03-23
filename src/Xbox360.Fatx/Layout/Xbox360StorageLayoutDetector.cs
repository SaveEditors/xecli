namespace Xbox360.Fatx.Layout;

public static class Xbox360StorageLayoutDetector
{
    public static async Task<IReadOnlyList<FatxPartition>> DetectAsync(Stream stream, FatxOpenOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.PartitionOffset is long offset)
        {
            var length = options.PartitionLength ?? Math.Max(0, stream.Length - offset);
            return new[] {
                new FatxPartition {
                    Index = 0,
                    Name = "Custom",
                    Kind = FatxPartitionKind.Unknown,
                    Offset = offset,
                    Length = length,
                }
            };
        }

        List<Xbox360PartitionDefinition> definitions = new();
        IReadOnlyList<Xbox360PartitionDefinition> retailPlan = Xbox360RetailLayoutPlanner.Create(stream.Length);
        foreach (Xbox360PartitionDefinition definition in retailPlan)
        {
            if (await LooksLikeFatxVolumeAsync(stream, definition.Offset, definition.Length, cancellationToken).ConfigureAwait(false))
            {
                definitions.Add(definition);
            }
        }

        if (definitions.Count == 0)
        {
            if (await LooksLikeFatxVolumeAsync(stream, 0, stream.Length, cancellationToken).ConfigureAwait(false))
            {
                definitions.Add(new Xbox360PartitionDefinition
                {
                    Index = 0,
                    Name = "WholeDisk",
                    Kind = FatxPartitionKind.WholeDisk,
                    Offset = 0,
                    Length = stream.Length,
                });
            }
            else
            {
                definitions.Add(new Xbox360PartitionDefinition
                {
                    Index = 0,
                    Name = "WholeDisk",
                    Kind = FatxPartitionKind.WholeDisk,
                    Offset = 0,
                    Length = stream.Length,
                });
            }
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

        return partitions;
    }

    private static async Task<bool> LooksLikeFatxVolumeAsync(Stream stream, long offset, long length, CancellationToken cancellationToken)
    {
        if (offset < 0 || length < 0x10 || offset > stream.Length - 0x10)
            return false;

        byte[] header = new byte[4];
        stream.Position = offset;
        int read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken).ConfigureAwait(false);
        if (read < 4)
            return false;

        return header[0] == (byte)'F' && header[1] == (byte)'A' && header[2] == (byte)'T' && header[3] == (byte)'X'
            || header[0] == (byte)'X' && header[1] == (byte)'T' && header[2] == (byte)'A' && header[3] == (byte)'F';
    }
}
