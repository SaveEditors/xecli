using System;
using System.Security.Cryptography;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    public class KeyVault
    {
        public readonly RSAParameters SigningParameters;
        public readonly byte[] ConsoleCertificate;

        public KeyVault(string fileName)
        {
            var io = new EndianIO(fileName, EndianType.Big);

            switch (io.Length)
            {
                case 0x4000:
                    io.Position = 0x18;
                    break;
                case 0x3FF0:
                    io.Position = 0x08;
                    break;
                default:
                    throw new Exception("Invalid keyvault loaded.");
            }

            io.Position += 0x9b0;

            this.ConsoleCertificate = io.ReadByteArray(0x1a8);

            io.Position += 0x284;

            var sigParams = new RSAParameters();

            sigParams.Exponent = io.ReadByteArray(4);
            sigParams.D = new byte[128];

            io.Position += 0x08;

            sigParams.Modulus = io.ReadByteArray(128);
            sigParams.P = io.ReadByteArray(64);
            sigParams.Q = io.ReadByteArray(64);
            sigParams.DP = io.ReadByteArray(64);
            sigParams.DQ = io.ReadByteArray(64);
            sigParams.InverseQ = io.ReadByteArray(64);

            io.Close();

            XeCrypt.ReverseQw(sigParams.Modulus);
            XeCrypt.ReverseQw(sigParams.P);
            XeCrypt.ReverseQw(sigParams.Q);
            XeCrypt.ReverseQw(sigParams.DP);
            XeCrypt.ReverseQw(sigParams.DQ);
            XeCrypt.ReverseQw(sigParams.InverseQ);

            this.SigningParameters = sigParams;
        }
    }
}
