using System;
using System.Security.Cryptography;

namespace NoDev.Xbox360
{
    public static class KeyStorage
    {
        public const string MissingSigningMaterialMessage =
            "CON signing requires --keyvault <FILE> containing your own Xbox 360 keyvault.";

        private static readonly object SyncRoot = new object();
        private static byte[]? _publicKey;
        private static RSAParameters? _privateKeys;
        private static int _generation;

        public static byte[] PublicKey
        {
            get
            {
                lock (SyncRoot)
                {
                    EnsureLoadedCore();
                    return _publicKey!;
                }
            }
        }

        public static RSAParameters PrivateKeys
        {
            get
            {
                lock (SyncRoot)
                {
                    EnsureLoadedCore();
                    return _privateKeys!.Value;
                }
            }
        }

        public static bool IsLoaded
        {
            get
            {
                lock (SyncRoot)
                {
                    return _publicKey != null && _privateKeys.HasValue;
                }
            }
        }

        public static IDisposable Load(KeyVault keyVault)
        {
            ArgumentNullException.ThrowIfNull(keyVault);

            lock (SyncRoot)
            {
                ClearCore();
                _publicKey = (byte[])keyVault.ConsoleCertificate.Clone();
                _privateKeys = Clone(keyVault.SigningParameters);
                int generation = ++_generation;
                return new SigningMaterialScope(generation);
            }
        }

        public static void EnsureLoaded()
        {
            lock (SyncRoot)
            {
                EnsureLoadedCore();
            }
        }

        private static void EnsureLoadedCore()
        {
            if (_publicKey == null || !_privateKeys.HasValue)
            {
                throw new InvalidOperationException(MissingSigningMaterialMessage);
            }
        }

        private static RSAParameters Clone(RSAParameters source)
        {
            return new RSAParameters
            {
                Exponent = Clone(source.Exponent),
                Modulus = Clone(source.Modulus),
                P = Clone(source.P),
                Q = Clone(source.Q),
                DP = Clone(source.DP),
                DQ = Clone(source.DQ),
                InverseQ = Clone(source.InverseQ),
                D = Clone(source.D)
            };
        }

        private static byte[]? Clone(byte[]? source) => source == null ? null : (byte[])source.Clone();

        private static void ClearCore()
        {
            Zero(_publicKey);
            _publicKey = null;

            if (_privateKeys.HasValue)
            {
                RSAParameters parameters = _privateKeys.Value;
                Zero(parameters.Exponent);
                Zero(parameters.Modulus);
                Zero(parameters.P);
                Zero(parameters.Q);
                Zero(parameters.DP);
                Zero(parameters.DQ);
                Zero(parameters.InverseQ);
                Zero(parameters.D);
                _privateKeys = null;
            }
        }

        private static void Zero(byte[]? buffer)
        {
            if (buffer != null)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }

        private sealed class SigningMaterialScope : IDisposable
        {
            private readonly int _scopeGeneration;
            private bool _disposed;

            public SigningMaterialScope(int scopeGeneration)
            {
                _scopeGeneration = scopeGeneration;
            }

            public void Dispose()
            {
                lock (SyncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_scopeGeneration == _generation)
                    {
                        ClearCore();
                    }

                    _disposed = true;
                }
            }
        }
    }
}
