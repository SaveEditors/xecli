using System;
using System.IO;
using System.Security.Cryptography;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    public sealed class KeyVault : IDisposable
    {
        public const int DecryptedSize = 0x4000;
        public const int DecryptedPayloadSize = 0x3FF0;

        public RSAParameters SigningParameters { get; private set; }
        public byte[] ConsoleCertificate { get; private set; } = Array.Empty<byte>();

        public KeyVault(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                throw new FileNotFoundException("Keyvault file was not found.");
            }

            EndianIO? io = null;
            byte[]? consoleCertificate = null;
            RSAParameters signingParameters = default;
            try
            {
                io = new EndianIO(fileName, EndianType.Big, FileMode.Open, FileAccess.Read, FileShare.Read);
                io.Position = io.Length switch
                {
                    DecryptedSize => 0x18,
                    DecryptedPayloadSize => 0x08,
                    _ => throw new InvalidDataException("Keyvault size must be 0x4000 or 0x3FF0 bytes.")
                };

                io.Position += 0x9B0;
                consoleCertificate = io.ReadByteArray(0x1A8);
                io.Position += 0x284;

                signingParameters = new RSAParameters
                {
                    Exponent = io.ReadByteArray(4),
                    D = new byte[128]
                };

                io.Position += 0x08;
                signingParameters.Modulus = io.ReadByteArray(128);
                signingParameters.P = io.ReadByteArray(64);
                signingParameters.Q = io.ReadByteArray(64);
                signingParameters.DP = io.ReadByteArray(64);
                signingParameters.DQ = io.ReadByteArray(64);
                signingParameters.InverseQ = io.ReadByteArray(64);

                XeCrypt.ReverseQw(signingParameters.Modulus);
                XeCrypt.ReverseQw(signingParameters.P);
                XeCrypt.ReverseQw(signingParameters.Q);
                XeCrypt.ReverseQw(signingParameters.DP);
                XeCrypt.ReverseQw(signingParameters.DQ);
                XeCrypt.ReverseQw(signingParameters.InverseQ);

                Validate(consoleCertificate, signingParameters);
                ConsoleCertificate = consoleCertificate;
                SigningParameters = signingParameters;
                consoleCertificate = null;
                signingParameters = default;
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidDataException("Keyvault could not be parsed as an Xbox 360 keyvault.");
            }
            finally
            {
                io?.Close();
                Zero(consoleCertificate);
                Zero(ref signingParameters);
            }
        }

        public static bool IsSupportedLength(long length) =>
            length == DecryptedSize || length == DecryptedPayloadSize;

        public void Dispose()
        {
            Zero(ConsoleCertificate);
            ConsoleCertificate = Array.Empty<byte>();

            RSAParameters signingParameters = SigningParameters;
            Zero(ref signingParameters);
            SigningParameters = default;
        }

        private static void Validate(byte[] certificate, RSAParameters parameters)
        {
            if (certificate.Length != 0x1A8 || certificate[0] != 0x01 || certificate[1] != 0xA8)
            {
                throw new InvalidDataException("Keyvault console certificate is invalid.");
            }

            ValidateComponent(parameters.Exponent, 4);
            ValidateComponent(parameters.Modulus, 128);
            ValidateComponent(parameters.P, 64);
            ValidateComponent(parameters.Q, 64);
            ValidateComponent(parameters.DP, 64);
            ValidateComponent(parameters.DQ, 64);
            ValidateComponent(parameters.InverseQ, 64);

            if (!CryptographicOperations.FixedTimeEquals(certificate.AsSpan(0x24, 4), parameters.Exponent))
            {
                throw new InvalidDataException("Keyvault certificate does not match its signing key.");
            }

            byte[] certificateModulus = certificate.AsSpan(0x28, 128).ToArray();
            try
            {
                XeCrypt.ReverseQw(certificateModulus);
                if (!CryptographicOperations.FixedTimeEquals(certificateModulus, parameters.Modulus))
                {
                    throw new InvalidDataException("Keyvault certificate does not match its signing key.");
                }
            }
            finally
            {
                Zero(certificateModulus);
            }

            using var rsa = new RSACryptoServiceProvider { PersistKeyInCsp = false };
            try
            {
                rsa.ImportParameters(parameters);
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException("Keyvault signing key is invalid.");
            }
        }

        private static void ValidateComponent(byte[]? value, int expectedLength)
        {
            if (value == null || value.Length != expectedLength || IsAllZero(value))
            {
                throw new InvalidDataException("Keyvault signing key is incomplete.");
            }
        }

        private static bool IsAllZero(byte[] value)
        {
            int combined = 0;
            foreach (byte item in value)
            {
                combined |= item;
            }

            return combined == 0;
        }

        private static void Zero(ref RSAParameters parameters)
        {
            Zero(parameters.Exponent);
            Zero(parameters.Modulus);
            Zero(parameters.P);
            Zero(parameters.Q);
            Zero(parameters.DP);
            Zero(parameters.DQ);
            Zero(parameters.InverseQ);
            Zero(parameters.D);
            parameters = default;
        }

        private static void Zero(byte[]? buffer)
        {
            if (buffer != null)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }
}
