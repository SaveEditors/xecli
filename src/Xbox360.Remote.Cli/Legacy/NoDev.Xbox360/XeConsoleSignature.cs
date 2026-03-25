using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    public class XeConsoleSignature
    {
        public readonly XeConsoleCertificate Certificate;
        public readonly byte[] Signature;

        public XeConsoleSignature(EndianIO io)
        {
            this.Certificate = new XeConsoleCertificate(io.ReadByteArray(0x1a8));
            this.Signature = io.ReadByteArray(128);

            Array.Reverse(this.Signature);
        }

        [Obfuscation]
        public bool Verify(byte[] data)
        {
            var rsa = new RSAPKCS1SignatureDeformatter(this.Certificate.CreateRSACryptoServiceProvider());
            rsa.SetHashAlgorithm("SHA1");
            return rsa.VerifySignature(SHA1.Create().ComputeHash(data), this.Signature);
        }
    }

    public class XeConsoleCertificate
    {
        public readonly ushort CertSize;
        public readonly byte[] ConsoleID;
        public readonly string ConsolePartNumber;
        public readonly byte[] Reserved;
        public readonly ushort Privileges;
        public readonly uint ConsoleType;
        public readonly long ManufacturingDate;
        public readonly byte[] Signature;

        public readonly byte[] PublicExponent;
        public readonly byte[] Modulus;

        public XeConsoleCertificate(byte[] data)
        {
            var io = new EndianIO(new MemoryStream(data), EndianType.Big);
            CertSize = io.ReadUInt16();
            ConsoleID = io.ReadByteArray(5);
            ConsolePartNumber = io.ReadAsciiString(11);
            Reserved = io.ReadByteArray(4);
            Privileges = io.ReadUInt16();
            ConsoleType = io.ReadUInt32();
            ManufacturingDate = io.ReadInt64();
            PublicExponent = io.ReadByteArray(4);
            Modulus = io.ReadByteArray(128);
            Signature = io.ReadByteArray(256);
            io.Close();
        }

        public RSACryptoServiceProvider CreateRSACryptoServiceProvider()
        {
            var rsaParams = new RSAParameters();
            rsaParams.Exponent = this.PublicExponent;
            rsaParams.Modulus = XeCrypt.ReverseQwCopy(this.Modulus);

            var sp = new RSACryptoServiceProvider();
            sp.ImportParameters(rsaParams);

            return sp;
        }
    }
}
