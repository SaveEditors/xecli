using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    public static class XeCrypt
    {
        public static byte[] XeCryptSha(byte[] pbInp1, byte[] pbInp2 = null, byte[] pbInp3 = null)
        {
            if (pbInp1 == null || pbInp1.Length == 0)
                throw new Exception("Attempted to hash an invalid buffer.");

            var sha = new SHA1Managed();

            sha.TransformBlock(pbInp1, 0, pbInp1.Length, null, 0);

            if (pbInp2 != null)
                sha.TransformBlock(pbInp2, 0, pbInp2.Length, null, 0);

            if (pbInp3 != null)
                sha.TransformBlock(pbInp3, 0, pbInp3.Length, null, 0);

            sha.TransformFinalBlock(pbInp1, 0, 0);

            return sha.Hash;
        }

        public static byte[] FormatSignature(RSAParameters rsaParameters, byte[] input)
        {
            var rsaCryptProvider = new RSACryptoServiceProvider();
            rsaCryptProvider.ImportParameters(rsaParameters);

            var sigFormatter = new RSAPKCS1SignatureFormatter(rsaCryptProvider);
            sigFormatter.SetHashAlgorithm("SHA1");

            byte[] signature = sigFormatter.CreateSignature(input.ComputeSHA1());

            Array.Reverse(signature);

            return signature;
        }

        public static byte[] XeKeysUnObfuscate(int index, byte[] encryptedBuffer, bool dev)
        {
            int bufferSize = encryptedBuffer.Length;

            int keyIndex = ((-index * 2) & 2) + 0x17;

            byte[] baseKey = new byte[0x10], body = new byte[bufferSize - 0x10];

            Array.Copy(encryptedBuffer, 0, baseKey, 0, 0x10);

            Array.Copy(encryptedBuffer, 0x10, body, 0, bufferSize - 0x10);

            byte[] rc4Key = XeKeysHmacSha(keyIndex, baseKey, 0x10, 0x10, dev);

            RC4(body, rc4Key);

            byte[] hmac = XeKeysHmacSha(keyIndex, body, bufferSize - 0x10, 0x10, dev);
 
            if (!hmac.SequenceEqual(baseKey))
                return null;

            var realData = new byte[bufferSize - 0x18];

            Array.Copy(body, 8, realData, 0, realData.Length);

            return realData;
        }

        public static byte[] XeKeysObfuscate(int index, byte[] buffer, bool dev)
        {
            var io = new EndianIO(new MemoryStream(), EndianType.Big);
            int bufferSize = buffer.Length;

            int tempIndex = -index;
            int keyIndex = ((tempIndex + tempIndex) & 2) + 0x17;

            var confounder = new byte[0x8];
            new Random().NextBytes(confounder);
            var body = new byte[bufferSize + 8];
            Array.Copy(confounder, body, 08);
            Array.Copy(buffer, 0, body, 8, bufferSize);
            byte[] headerKey = XeKeysHmacSha(keyIndex, body, bufferSize + 8, 0x10, dev);
            io.Write(headerKey);

            byte[] rc4Key = XeKeysHmacSha(keyIndex, headerKey, 0x10, 0x10, dev);
            RC4(body, rc4Key);

            io.Write(body);
            io.Close();

            return io.ToArray();
        }

        private static byte[] XeKeysHmacSha(int keyIndex, byte[] buffer, int bufferSize, int digestSize, bool dev)
        {
            var hmac = new HMACSHA1(HvpGetKey(keyIndex, dev));
            hmac.TransformFinalBlock(buffer, 0, bufferSize);
            byte[] hash = hmac.Hash;
            if (digestSize > 0 && digestSize != 0x14)
                Array.Resize(ref hash, digestSize);
            return hash;
        }

        private static byte[] HvpGetKey(int keyIndex, bool dev)
        {
            switch (keyIndex)
            {
                case 0x19:
                    if (dev)
                        return new byte[] { 0xDA, 0xB6, 0x9A, 0xD9, 0x8E, 0x28, 0x76, 0x4F, 0x97, 0x7E, 0xE2, 0x48, 0x7E, 0x4F, 0x3F, 0x68 };

                    return new byte[] { 0xE1, 0xBC, 0x15, 0x9C, 0x73, 0xB1, 0xEA, 0xE9, 0xAB, 0x31, 0x70, 0xF3, 0xAD, 0x47, 0xEB, 0xF3 };
                default:
                    throw new Exception(string.Format("HvpGetKey: Unknown key index ({0}).", keyIndex));
            }
        }

        private static void RC4(byte[] data, byte[] key)
        {
            byte num;
            int num2;
            byte[] buffer = new byte[0x100], buffer2 = new byte[0x100];
            for (num2 = 0; num2 < 0x100; num2++)
            {
                buffer[num2] = (byte)num2;
                buffer2[num2] = key[num2 % key.Length];
            }
            int index = 0;
            for (num2 = 0; num2 < 0x100; num2++)
            {
                index = ((index + buffer[num2]) + buffer2[num2]) % 0x100;
                num = buffer[num2];
                buffer[num2] = buffer[index];
                buffer[index] = num;
            }
            num2 = index = 0;
            for (int i = 0; i < data.Length; i++)
            {
                num2 = (num2 + 1) % 0x100;
                index = (index + buffer[num2]) % 0x100;
                num = buffer[num2];
                buffer[num2] = buffer[index];
                buffer[index] = num;
                int num5 = (buffer[num2] + buffer[index]) % 0x100;
                data[i] = (byte)(data[i] ^ buffer[num5]);
            }
        }

        public static void ReverseQw(byte[] input)
        {
            int bufferLength = input.Length;
            for (int x = 0; x < bufferLength; x += 8)
                Array.Reverse(input, x, 8);
            Array.Reverse(input);
        }

        public static byte[] ReverseQwCopy(byte[] input)
        {
            var buffer = new byte[input.Length];
            Array.Copy(input, buffer, input.Length);
            ReverseQw(buffer);
            return buffer;
        }
    }
}