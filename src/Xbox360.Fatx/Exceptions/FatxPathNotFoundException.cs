namespace Xbox360.Fatx.Exceptions;

public sealed class FatxPathNotFoundException : FatxException
{
    public FatxPathNotFoundException(string path)
        : base($"FATX path not found: {path}")
    {
        Path = path;
    }

    public string Path { get; }
}
