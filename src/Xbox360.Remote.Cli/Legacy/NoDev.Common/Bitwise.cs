
namespace NoDev.Common
{
    public static class Bitwise
    {
        public static bool IsFlagOn(ulong value, ulong flag)
        {
            return (value & flag) != 0x00;
        }

        public static bool IsFlagOn(long value, long flag)
        {
            return (value & flag) != 0x00;
        }
    }
}
