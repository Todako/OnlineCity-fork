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
    /// та симетричне потокове шифрування (Rijndael / AES) мережевих пакетів.
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
            get { return ToBase64(OpenKey); }
            set { OpenKey = FromBase64(value); }
        }

        public string PrivateKeyBase64
        {
            get { return ToBase64(PrivateKey); }
            set { PrivateKey = FromBase64(value); }
        }

        public string ToBase64(string source)
        {
            var sourceByte = Encoding.UTF8.GetBytes(source);
            return Convert.ToBase64String(sourceByte);
        }

        public string FromBase64(string source)
        {
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
                rsa.Clear();
            }
        }

        public string Encrypt(string message)
        {
            return Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(message)));
        }

        public byte[] Encrypt(byte[] message)
        {
            using (var rsa = new RSACryptoServiceProvider(KeyBitSize))
            {
                rsa.FromXmlString(OpenKey);
                return rsa.Encrypt(message, false);
            }
        }

        public string Decrypt(string criptMessage)
        {
            return Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(criptMessage)));
        }

        public byte[] Decrypt(byte[] criptMessage)
        {
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
            var list = new List<string>();
            if (string.IsNullOrEmpty(source)) return list;
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

        #region Симетричне шифрування (Rijndael / AES)

        private static readonly byte[] SALT = new byte[] { 0x94, 0xF8, 0xEE, 0xA0, 0x00, 0x01, 0xFF, 0xE6, 0x74, 0x35, 0x07, 0xFE, 0x9D, 0x1E, 0x47, 0xBB };
        private static readonly Encoding KeyEncoding = Encoding.ASCII;

        /// <summary>
        /// Кешована пара згенерованого сесійного ключа та вектора ініціалізації (IV).
        /// </summary>
        private class DerivedKey
        {
            public byte[] Key;
            public byte[] IV;
        }

        /// <summary>
        /// Потокобезпечний кеш згенерованих ключів PBKDF2.
        /// Усуває виконання 1000 ітерацій HMAC-SHA1 на кожному мережевому пакеті.
        /// </summary>
        private static readonly ConcurrentDictionary<string, DerivedKey> KeyIvCache =
            new ConcurrentDictionary<string, DerivedKey>(StringComparer.Ordinal);

        /// <summary>
        /// Отримує або генерує 32-байтний ключ і 16-байтний вектор ініціалізації на основі пароля сесії.
        /// </summary>
        private static DerivedKey GetDerivedKey(string password)
        {
            if (password == null) password = string.Empty;

            if (KeyIvCache.TryGetValue(password, out var cached))
            {
                return cached;
            }

            using (var pdb = new Rfc2898DeriveBytes(password, SALT))
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
            return Convert.ToBase64String(SymmetricEncrypt(Encoding.UTF8.GetBytes(message), SymmetricKey));
        }

        public string SymmetricDecrypt(string criptMessage)
        {
            return Encoding.UTF8.GetString(SymmetricDecrypt(Convert.FromBase64String(criptMessage), SymmetricKey));
        }

        public static byte[] SymmetricEncrypt(byte[] plain, byte[] password)
        {
            return SymmetricEncrypt(plain, KeyEncoding.GetString(password));
        }

        /// <summary>
        /// Симетричне шифрування байтового масиву алгоритмом Rijndael.
        /// ОПТИМІЗАЦІЯ: деривація ключів береться з кешу, усуваючи затримку PBKDF2.
        /// </summary>
        public static byte[] SymmetricEncrypt(byte[] plain, string password)
        {
            if (plain == null) plain = new byte[0];

            var derived = GetDerivedKey(password);
            using (var rijndael = Rijndael.Create())
            {
                rijndael.Key = derived.Key;
                rijndael.IV = derived.IV;

                using (var memoryStream = new MemoryStream(plain.Length + 32))
                {
                    using (var cryptoStream = new CryptoStream(memoryStream, rijndael.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        if (plain.Length > 0)
                        {
                            cryptoStream.Write(plain, 0, plain.Length);
                        }
                        cryptoStream.FlushFinalBlock();
                    }
                    return memoryStream.ToArray();
                }
            }
        }

        public static byte[] SymmetricDecrypt(byte[] cipher, byte[] password)
        {
            return SymmetricDecrypt(cipher, KeyEncoding.GetString(password));
        }

        /// <summary>
        /// Симетричне дешифрування байтового масиву алгоритмом Rijndael.
        /// </summary>
        public static byte[] SymmetricDecrypt(byte[] cipher, string password)
        {
            if (cipher == null || cipher.Length == 0) return new byte[0];

            var derived = GetDerivedKey(password);
            using (var rijndael = Rijndael.Create())
            {
                rijndael.Key = derived.Key;
                rijndael.IV = derived.IV;

                using (var memoryStream = new MemoryStream(cipher.Length))
                {
                    using (var cryptoStream = new CryptoStream(memoryStream, rijndael.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(cipher, 0, cipher.Length);
                        cryptoStream.FlushFinalBlock();
                    }
                    return memoryStream.ToArray();
                }
            }
        }

        #endregion

        #region Хешування (SHA-512)

        public byte[] GetHash(byte[] data)
        {
            using (var sha = new SHA512Managed())
            {
                return sha.ComputeHash(data);
            }
        }

        public string GetHash(string data)
        {
            using (var sha = new SHA512Managed())
            {
                return KeyEncoding.GetString(sha.ComputeHash(KeyEncoding.GetBytes(data)));
            }
        }

        #endregion
    }
}