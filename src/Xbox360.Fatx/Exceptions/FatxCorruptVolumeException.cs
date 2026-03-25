namespace Xbox360.Fatx.Exceptions;

public sealed class FatxCorruptVolumeException : FatxException
{
    public FatxCorruptVolumeException(string message) : base(message)
    {
    }
}
