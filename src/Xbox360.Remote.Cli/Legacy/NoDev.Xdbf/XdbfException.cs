using System;

namespace NoDev.Xdbf
{
    internal class XdbfException : Exception
    {
        internal XdbfException(string message)
            : base("XDBF: " + message)
        {

        }
    }
}
