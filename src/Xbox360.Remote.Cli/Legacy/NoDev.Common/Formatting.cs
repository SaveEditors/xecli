using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace NoDev.Common
{
    public static class Formatting
    {
        public static string GetSizeFromBytes(decimal bytes)
        {
            var orders = new[] { "TB", "GB", "MB", "KB", "B" };
            var max = (long)Math.Pow(1024, orders.Length - 1);
            for (int x = 0; x < orders.Length; x++)
            {
                if (bytes >= max)
                    return string.Format(((x == orders.Length - 1) ? "{0:0} {1}" : "{0:0.00} {1}"), bytes / max, orders[x]);
                max /= 1024;
            }
            return "0 B";
        }

        public static string ByteArrayToHexString(byte[] arr, bool upperCase = false)
        {
            string format = upperCase ? "X2" : "x2";
            int arrLen = arr.Length;
            var sb = new StringBuilder(arrLen * 2);
            for (int x = 0; x < arrLen; x++)
                sb.Append(arr[x].ToString(format));
            return sb.ToString();
        }

        public static byte[] HexStringToByteArray(string hexString)
        {
            int strLen = hexString.Length;
            var output = new byte[strLen / 2];
            for (int x = 0, i = 0; x < strLen; x += 2, i++)
                output[i] = Convert.ToByte(hexString.Substring(x, 2), 16);
            return output;
        }

        #pragma warning disable SYSLIB0011
        public static byte[] SerializeToByteArray(object obj)
        {
            var ms = new MemoryStream();
            var bin = new BinaryFormatter();
            bin.AssemblyFormat = System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple;
            bin.Serialize(ms, obj);
            ms.Close();
            return ms.ToArray();
        }

        public static object DeserializeToObject(byte[] arr)
        {
            var bin = new BinaryFormatter();
            bin.AssemblyFormat = System.Runtime.Serialization.Formatters.FormatterAssemblyStyle.Simple;
            var ms = new MemoryStream();
            ms.Write(arr, 0, arr.Length);
            object obj = bin.Deserialize(ms);
            ms.Dispose();
            return obj;
        }
        #pragma warning restore SYSLIB0011
    }
}
