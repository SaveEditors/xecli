using Xbox360.Fatx.Entries;

namespace Xbox360.Fatx.Reading;

public sealed class FatxTreeWalker
{
    private readonly FatxVolume _volume;

    public FatxTreeWalker(FatxVolume volume)
    {
        _volume = volume;
    }

    public async IAsyncEnumerable<FatxEntry> WalkAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await _volume.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            yield return entry;
        }
    }
}
