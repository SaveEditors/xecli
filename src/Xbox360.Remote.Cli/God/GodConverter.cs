using Xbox360.Remote.Cli;

namespace Xbox360.Remote.Cli.God;

internal enum TrimMode {
    FromEnd,
    None
}

internal sealed record GodConvertOptions(
    TrimMode TrimMode,
    int Threads,
    string? GameTitle);

internal sealed record GodConversionResult(
    TitleExecutionInfo ExecutionInfo,
    ContentType ContentType,
    ulong DataSize,
    ulong BlockCount,
    ulong PartCount,
    string OutputDir,
    string ConHeaderPath);

internal static class GodConverter {
    public static GodConversionResult Convert(
        string sourceIso,
        string destDir,
        GodConvertOptions options,
        Action<int, int>? onPartComplete,
        CancellationToken cancellationToken = default) {
        using FileStream isoFile = new FileStream(sourceIso, FileMode.Open, FileAccess.Read, FileShare.Read);
        IsoReader iso = IsoReader.Read(isoFile);
        TitleInfo titleInfo = TitleInfo.FromImage(iso);

        ulong dataSize = options.TrimMode == TrimMode.FromEnd
            ? iso.GetMaxUsedPrefixSize()
            : (ulong) isoFile.Length - iso.Volume.RootOffset;

        if (dataSize == 0)
            throw new InvalidDataException("ISO contains no readable data.");

        ulong blockCount = (dataSize + GodConstants.BlockSize - 1) / GodConstants.BlockSize;
        ulong partCount = (blockCount + GodConstants.BlocksPerPart - 1) / GodConstants.BlocksPerPart;
        if (partCount == 0)
            partCount = 1;

        GodFileLayout layout = new GodFileLayout(destDir, titleInfo.ExecutionInfo, titleInfo.ContentType);
        EnsureEmptyDir(layout.DataDirPath());

        int partCountInt = checked((int) partCount);
        int completed = 0;
        ParallelOptions parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Math.Max(1, options.Threads),
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(Enumerable.Range(0, partCountInt), parallelOptions, partIndex => {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream dataVolume = new FileStream(sourceIso, FileMode.Open, FileAccess.Read, FileShare.Read);
            dataVolume.Seek((long) iso.Volume.RootOffset, SeekOrigin.Begin);

            using FileStream partFile = new FileStream(layout.PartFilePath(partIndex), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            WritePart(dataVolume, partIndex, partFile, cancellationToken);

            int done = Interlocked.Increment(ref completed);
            onPartComplete?.Invoke(done, partCountInt);
        });

        cancellationToken.ThrowIfCancellationRequested();
        HashList mht = ReadPartMht(layout, partCountInt - 1);
        for (int index = partCountInt - 2; index >= 0; index--) {
            cancellationToken.ThrowIfCancellationRequested();
            HashList prev = ReadPartMht(layout, index);
            prev.AddHash(mht.Digest());
            WritePartMht(layout, index, prev);
            mht = prev;
        }

        long lastPartSize = new FileInfo(layout.PartFilePath(partCountInt - 1)).Length;
        ulong partsTotalSize = (ulong) lastPartSize + (ulong) (partCountInt - 1) * GodConstants.BlockSize * GodConstants.PartSizeBlocks;

        string? gameTitle = options.GameTitle;
        if (string.IsNullOrWhiteSpace(gameTitle) &&
            TitleIdDatabase.Instance.TryResolve(titleInfo.ExecutionInfo.TitleId, null, out TitleIdEntry? entry)) {
            gameTitle = entry?.Name;
        }
        gameTitle ??= TitleIdDatabase.FormatTitleId(titleInfo.ExecutionInfo.TitleId);

        cancellationToken.ThrowIfCancellationRequested();
        ConHeaderBuilder builder = new ConHeaderBuilder()
            .WithExecutionInfo(titleInfo.ExecutionInfo)
            .WithBlockCounts((uint) blockCount, 0)
            .WithDataPartsInfo((uint) partCountInt, partsTotalSize)
            .WithContentType(titleInfo.ContentType)
            .WithMhtHash(mht.Digest());

        if (!string.IsNullOrWhiteSpace(gameTitle)) {
            builder = builder.WithGameTitle(gameTitle);
        }

        byte[] conHeader = builder.FinalizeHeader();
        string conHeaderPath = layout.ConHeaderFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(conHeaderPath) ?? destDir);
        File.WriteAllBytes(conHeaderPath, conHeader);

        return new GodConversionResult(
            titleInfo.ExecutionInfo,
            titleInfo.ContentType,
            dataSize,
            blockCount,
            partCount,
            layout.DataDirPath(),
            conHeaderPath);
    }

    private static void EnsureEmptyDir(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
        Directory.CreateDirectory(path);
    }

    private static void WritePart(Stream dataVolume, int partIndex, Stream partFile, CancellationToken cancellationToken) {
        long skip = (long) ((ulong) partIndex * GodConstants.BlocksPerPart * GodConstants.BlockSize);
        if (skip > 0)
            dataVolume.Seek(skip, SeekOrigin.Current);

        HashList masterHashList = new HashList();
        long masterPosition = partFile.Position;
        masterHashList.Write(partFile);

        byte[] subpartBuffer = new byte[GodConstants.SubpartSize];
        for (int subpartIndex = 0; subpartIndex < GodConstants.SubpartsPerPart; subpartIndex++) {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesRead = ReadUpTo(dataVolume, subpartBuffer, cancellationToken);
            if (bytesRead == 0)
                break;

            HashList subHash = new HashList();
            int offset = 0;
            while (offset < bytesRead) {
                int size = (int) Math.Min((ulong) GodConstants.BlockSize, (ulong) (bytesRead - offset));
                subHash.AddBlockHash(subpartBuffer.AsSpan(offset, size));
                offset += size;
            }

            subHash.Write(partFile);
            masterHashList.AddBlockHash(subHash.Bytes);
            partFile.Write(subpartBuffer, 0, bytesRead);

            if (bytesRead < subpartBuffer.Length)
                break;
        }

        partFile.Seek(masterPosition, SeekOrigin.Begin);
        masterHashList.Write(partFile);
    }

    private static int ReadUpTo(Stream stream, byte[] buffer, CancellationToken cancellationToken) {
        int total = 0;
        while (total < buffer.Length) {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static HashList ReadPartMht(GodFileLayout layout, int partIndex) {
        using FileStream stream = new FileStream(layout.PartFilePath(partIndex), FileMode.Open, FileAccess.Read, FileShare.Read);
        return HashList.Read(stream);
    }

    private static void WritePartMht(GodFileLayout layout, int partIndex, HashList mht) {
        using FileStream stream = new FileStream(layout.PartFilePath(partIndex), FileMode.Open, FileAccess.Write, FileShare.None);
        mht.Write(stream);
    }
}
