namespace Xbox360.Fatx.Exceptions;

public class FatxException : Exception
{
    public FatxException(string message) : base(message)
    {
    }

    public FatxException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
