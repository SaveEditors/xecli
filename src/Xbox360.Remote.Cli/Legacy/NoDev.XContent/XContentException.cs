using System;

namespace NoDev.XContent
{
    internal class XContentException : Exception
    {
        internal XContentException(string message)
            : base("XContent: " + message)
        {

        }
    }
}
