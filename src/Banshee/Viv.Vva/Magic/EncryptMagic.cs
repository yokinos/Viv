using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Viv.Vva.Enums;

namespace Viv.Vva.Magic
{
    public static class EncryptMagic
    {
        private static readonly Encoding Utf8 = Encoding.UTF8;

        public static string HashMD5(string input, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            encoding ??= Utf8;
            var bytes = encoding.GetBytes(input);
            byte[] hashBytes = MD5.HashData(bytes);
            static char GetHexChar(int value) => (char)(value < 10 ? value + '0' : value - 10 + 'a');
            return string.Create(32, hashBytes, (span, bytesArr) =>
            {
                for (int i = 0; i < bytesArr.Length; i++)
                {
                    byte b = bytesArr[i];
                    span[2 * i] = GetHexChar(b >> 4);
                    span[2 * i + 1] = GetHexChar(b & 0x0F);
                }
            });
        }

        public static string HashSHA256(string input, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(input);
            encoding ??= Utf8;
            byte[] buffer = SHA256.HashData(encoding.GetBytes(input));
            return Convert.ToBase64String(buffer);
        }

        [return: MaybeNull]
        public static string EncryptDES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.DES, key, text, options, true);

        [return: MaybeNull]
        public static string DecryptDES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.DES, key, text, options, false);

        [return: MaybeNull]
        public static string Encrypt3DES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.TripleDES, key, text, options, true);

        [return: MaybeNull]
        public static string Decrypt3DES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.TripleDES, key, text, options, false);

        [return: MaybeNull]
        public static string EncryptAES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.AES, key, text, options, true);

        [return: MaybeNull]
        public static string DecryptAES(string key, string text, EncrypOptions? options = default) => SymmetricTransform(EncrypType.AES, key, text, options, false);


        private static string? SymmetricTransform(EncrypType type, string key, string text, EncrypOptions? options, bool encrypt)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return default;
            options ??= new EncrypOptions(type);

            try
            {
                using SymmetricAlgorithm alg = CreateAlgorithm(type);
                alg.Mode = options.Mode;
                alg.Padding = options.PaddingMode;

                int keyBytesRequired = alg.KeySize / 8;
                int ivBytesRequired = alg.BlockSize / 8;

                byte[] keyBytes = DeriveKeyBytes(key, keyBytesRequired);
                byte[] ivBytes = DeriveIVBytes(options.IV ?? string.Empty, ivBytesRequired);

                alg.Key = keyBytes;
                alg.IV = ivBytes;

                if (encrypt)
                {
                    byte[] plain = Utf8.GetBytes(text);
                    using var encryptor = alg.CreateEncryptor();
                    byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                    return Convert.ToBase64String(cipher);
                }
                else
                {
                    byte[] cipher = Convert.FromBase64String(text);
                    using var decryptor = alg.CreateDecryptor();
                    byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Utf8.GetString(plain);
                }
            }
            catch
            {
                return default;
            }
        }

        private static SymmetricAlgorithm CreateAlgorithm(EncrypType type) => type switch
        {
            EncrypType.DES => DES.Create(),
            EncrypType.TripleDES => TripleDES.Create(),
            EncrypType.AES => Aes.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static byte[] DeriveKeyBytes(string key, int requiredBytes)
        {
            byte[] hash = SHA256.HashData(Utf8.GetBytes(key));
            if (requiredBytes == hash.Length) return hash;
            var result = new byte[requiredBytes];
            Buffer.BlockCopy(hash, 0, result, 0, Math.Min(requiredBytes, hash.Length));
            if (requiredBytes > hash.Length)
            {
                int offset = hash.Length;
                while (offset < requiredBytes)
                {
                    int copy = Math.Min(hash.Length, requiredBytes - offset);
                    Buffer.BlockCopy(hash, 0, result, offset, copy);
                    offset += copy;
                }
            }
            return result;
        }

        private static byte[] DeriveIVBytes(string ivCandidate, int requiredBytes)
        {
            if (!string.IsNullOrEmpty(ivCandidate))
            {
                var ivBytes = Utf8.GetBytes(ivCandidate);
                if (ivBytes.Length == requiredBytes) return ivBytes;
                var iv = new byte[requiredBytes];
                int copy = Math.Min(ivBytes.Length, requiredBytes);
                Buffer.BlockCopy(ivBytes, 0, iv, 0, copy);
                return iv;
            }

            byte[] fallback = SHA256.HashData(Array.Empty<byte>());
            var res = new byte[requiredBytes];
            Buffer.BlockCopy(fallback, 0, res, 0, Math.Min(requiredBytes, fallback.Length));
            return res;
        }
    } 
}