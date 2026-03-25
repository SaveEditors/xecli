using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.Entries;

namespace Xbox360.Fatx.Reading;

public sealed class FatxExtractionService
{
    private readonly FatxVolume _volume;

    public FatxExtractionService(FatxVolume volume)
    {
        _volume = volume;
    }

    public async Task ExtractFileAsync(string fatxPath, string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        byte[] payload = await _volume.ReadFileAsync(fatxPath, cancellationToken).ConfigureAwait(false);
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(outputPath, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExtractDirectoryAsync(string fatxPath, string outputDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        FatxEntry entry = await _volume.ResolvePathAsync(fatxPath, cancellationToken).ConfigureAwait(false);
        if (entry is not FatxDirectoryEntry)
        {
            throw new FatxException($"Path '{fatxPath}' does not reference a FATX directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        await ExtractDirectoryInternalAsync(fatxPath, outputDirectory, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExtractDirectoryInternalAsync(string fatxPath, string outputDirectory, CancellationToken cancellationToken)
    {
        IReadOnlyList<FatxEntry> entries = await _volume.ListDirectoryAsync(fatxPath, cancellationToken).ConfigureAwait(false);
        foreach (FatxEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string destinationPath = Path.Combine(outputDirectory, entry.Name);
            if (entry is FatxDirectoryEntry)
            {
                Directory.CreateDirectory(destinationPath);
                await ExtractDirectoryInternalAsync(entry.FullPath, destinationPath, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await ExtractFileAsync(entry.FullPath, destinationPath, cancellationToken).ConfigureAwait(false);
        }
    }
}
