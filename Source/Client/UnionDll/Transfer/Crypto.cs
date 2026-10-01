using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Util
{
    /// <summary>
    /// Провайдер криптографічних операцій.
    /// Забезпечує асиметричне шифрування (RSA) для початкового рукостискання
    /// та апаратно прискорене симетричне шифрування (AES-256) мережевих пакетів.
    /// </summary>
    public class CryptoProvider
    {
        public const int KeyBitSize = 2048;
        public const int PartByteSize = KeyBitSize / 8 - 11 - 30 - 1;

        #region Дані ключів

        public string OpenKey;
        public string PrivateKey;
        public string SymmetricKey;

        public string OpenKeyBase64
        {
            get => ToBase64(OpenKey);
            set => OpenKey = FromBase64(value);
        }

        public string PrivateKeyBase64
        {
            get => ToBase64(PrivateKey);
            set => PrivateKey = FromBase64(value);
        }

        public string ToBase64(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sourceByte = Encoding.UTF8.GetBytes(source);
            return Convert.ToBase64String(sourceByte);
        }

        public string FromBase64(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sourceByte = Convert.FromBase64String(source);
            return Encoding.UTF8.GetString(sourceByte);
        }

        #endregion

        #region Асиметричне шифрування (RSA)

        public void GenerateKeys()
        {
            using (var rsa = new RSACryptoServiceProvider(KeyBitSize))
            {
                OpenKey = rsa.ToXmlString(false);
                PrivateKey = rsa.ToXmlString(true);
            }
        }

        public string Encrypt(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            return Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(message)));
        }

        public byte[] Encrypt(byte[] message)
        {
            if (message == null || message.Length == 0) return new byte[0];
            using (var rsa = new RSACryptoServiceProvider(KeyBitSize))
            {
                rsa.FromXmlString(OpenKey);
                return rsa.Encrypt(message, false);
            }
        }

        public string Decrypt(string criptMessage)
        {
            if (string.IsNullOrEmpty(criptMessage)) return string.Empty;
            return Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(criptMessage)));
        }

        public byte[] Decrypt(byte[] criptMessage)
        {
            if (criptMessage == null || criptMessage.Length == 0) return new byte[0];
            using (var rsa = new RSACryptoServiceProvider(KeyBitSize))
            {
                rsa.FromXmlString(PrivateKey);
                return rsa.Decrypt(criptMessage, false);
            }
        }

        #endregion

        #region Розбиття на блоки для асиметричного шифрування

        public List<string> GetListPart(string source)
        {
            if (string.IsNullOrEmpty(source)) return new List<string>(0);

            int count = (source.Length / PartByteSize) + 1;
            var list = new List<string>(count);

            for (int i = 0; i <= source.Length / PartByteSize; i++)
            {
                var stradd = i == source.Length / PartByteSize
                    ? source.Substring(i * PartByteSize)
                    : source.Substring(i * PartByteSize, PartByteSize);

                if (!string.IsNullOrEmpty(stradd)) list.Add(stradd);
            }
            return list;
        }

        #endregion

        #region Симетричне шифрування (Апаратно прискорений AES-256)

        private static readonly byte[] SALT = new byte[] { 0x94, 0xF8, 0xEE, 0xA0, 0x00, 0x01, 0xFF, 0xE6, 0x74, 0x35, 0x07, 0xFE, 0x9D, 0x1E, 0x47, 0xBB };
        private static readonly Encoding KeyEncoding = Encoding.ASCII;

        private class DerivedKey
        {
            public byte[] Key;
            public byte[] IV;
        }

        /// <summary>
        /// Потокобезпечний кеш ключів PBKDF2.
        /// Усуває виконання 1000 ітерацій HMAC-SHA1 на кожному мережевому пакеті.
        /// </summary>
        private static readonly ConcurrentDictionary<string, DerivedKey> KeyIvCache =
            new ConcurrentDictionary<string, DerivedKey>(StringComparer.Ordinal);

        private static DerivedKey GetDerivedKey(string password)
        {
            if (password == null) password = string.Empty;

            if (KeyIvCache.TryGetValue(password, out var cached))
            {
                return cached;
            }

            using (var pdb = new Rfc2898DeriveBytes(password, SALT, 1000))
            {
                var derived = new DerivedKey
                {
                    Key = pdb.GetBytes(32),
                    IV = pdb.GetBytes(16)
                };
                KeyIvCache[password] = derived;
                return derived;
            }
        }

        public string SymmetricEncrypt(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            return Convert.ToBase64String(SymmetricEncrypt(Encoding.UTF8.GetBytes(message), SymmetricKey));
        }

        public string SymmetricDecrypt(string criptMessage)
        {
            if (string.IsNullOrEmpty(criptMessage)) return string.Empty;
            return Encoding.UTF8.GetString(SymmetricDecrypt(Convert.FromBase64String(criptMessage), SymmetricKey));
        }

        public static byte[] SymmetricEncrypt(byte[] plain, byte[] password)
        {
            return SymmetricEncrypt(plain, KeyEncoding.GetString(password));
        }

        /// <summary>
        /// Апаратно прискорене симетричне шифрування AES-256-CBC.
        /// ОПТИМІЗАЦІЯ: прямий виклик TransformFinalBlock виключає виділення CryptoStream та MemoryStream.
        /// </summary>
        public static byte[] SymmetricEncrypt(byte[] plain, string password)
        {
            if (plain == null) plain = new byte[0];

            var derived = GetDerivedKey(password);
            using (var aes = Aes.Create())
            {
                aes.Key = derived.Key;
                aes.IV = derived.IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encryptor = aes.CreateEncryptor())
                {
                    return encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
            }
        }

        public static byte[] SymmetricDecrypt(byte[] cipher, byte[] password)
        {
            return SymmetricDecrypt(cipher, KeyEncoding.GetString(password));
        }

        /// <summary>
        /// Апаратно прискорене симетричне дешифрування AES-256-CBC.
        /// ОПТИМІЗАЦІЯ: прямий виклик TransformFinalBlock без зайвих алокацій потоків.
        /// </summary>
        public static byte[] SymmetricDecrypt(byte[] cipher, string password)
        {
            if (cipher == null || cipher.Length == 0) return new byte[0];

            var derived = GetDerivedKey(password);
            using (var aes = Aes.Create())
            {
                aes.Key = derived.Key;
                aes.IV = derived.IV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                }
            }
        }

        #endregion

        #region Хешування (SHA-512)

        public byte[] GetHash(byte[] data)
        {
            if (data == null || data.Length == 0) return new byte[0];
            using (var sha = SHA512.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        public string GetHash(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;
            using (var sha = SHA512.Create())
            {
                return KeyEncoding.GetString(sha.ComputeHash(KeyEncoding.GetBytes(data)));
            }
        }

        #endregion
    }
}