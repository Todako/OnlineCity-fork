using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Автономні налаштування OnlineCity.
    /// Зберігаються виключно в підпапці Config/OnlineCity/ без участі LoadedModManager RimWorld.
    /// Пароль шифрується алгоритмом AES-256 (потокобезпечно у фонових потоках на Windows та Linux).
    /// </summary>
    public class OCSettings
    {
        public string LastIP = "";
        public string LastLoginName = "";
        public string LastPassword = "";
        public string LastCash = "";

        public static string FolderPath => Path.Combine(GenFilePaths.ConfigFolderPath, "OnlineCity");
        public static string FilePath => Path.Combine(FolderPath, "Mod_OnlineCity_ModBaseData.xml");

        #region Потокобезпечне шифрування AES-256

        private static byte[] _cachedMachineKey;

        private static byte[] GetMachineKey()
        {
            if (_cachedMachineKey != null) return _cachedMachineKey;

            string machine = "";
            string user = "";
            try { machine = Environment.MachineName ?? ""; } catch { }
            try { user = Environment.UserName ?? ""; } catch { }

            string rawKey = $"{machine}_{user}_@#OnlineCity_Salt_v2!";
            using (var sha = SHA256.Create())
            {
                _cachedMachineKey = sha.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
                return _cachedMachineKey;
            }
        }

        private static string EncryptPassword(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] key = GetMachineKey();

                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.GenerateIV();

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    {
                        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                        byte[] result = new byte[aes.IV.Length + cipherBytes.Length];

                        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[OnlineCity] Помилка шифрування пароля: " + ex.Message);
                return string.Empty;
            }
        }

        private static string DecryptPassword(string cipherTextBase64)
        {
            if (string.IsNullOrWhiteSpace(cipherTextBase64)) return string.Empty;

            try
            {
                byte[] fullCipher = Convert.FromBase64String(cipherTextBase64.Trim());
                byte[] key = GetMachineKey();

                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    int ivLength = aes.BlockSize / 8; // 16 байтів
                    if (fullCipher.Length <= ivLength) return string.Empty;

                    byte[] iv = new byte[ivLength];
                    byte[] cipherBytes = new byte[fullCipher.Length - ivLength];

                    Buffer.BlockCopy(fullCipher, 0, iv, 0, ivLength);
                    Buffer.BlockCopy(fullCipher, ivLength, cipherBytes, 0, cipherBytes.Length);

                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                    {
                        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                        return Encoding.UTF8.GetString(plainBytes);
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        public void Save()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                var doc = new XmlDocument();
                var root = doc.CreateElement("ModSettings");
                doc.AppendChild(root);

                void AddNode(string name, string val)
                {
                    var node = doc.CreateElement(name);
                    node.InnerText = val ?? "";
                    root.AppendChild(node);
                }

                AddNode("ocLastIP", (LastIP ?? "").Trim());
                AddNode("ocLastLoginName", (LastLoginName ?? "").Trim());
                AddNode("ocLastPassword", EncryptPassword(LastPassword));
                AddNode("ocLastCash", LastCash ?? "");

                doc.Save(FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("[OnlineCity] Не вдалося зберегти налаштування в " + FilePath + ": " + ex);
            }
        }

        public static OCSettings Load()
        {
            var settings = new OCSettings();

            if (!File.Exists(FilePath))
            {
                return settings;
            }

            try
            {
                var doc = new XmlDocument();
                doc.Load(FilePath);
                var root = doc["ModSettings"];
                if (root != null)
                {
                    settings.LastIP = (root["ocLastIP"]?.InnerText ?? "").Trim();
                    settings.LastLoginName = (root["ocLastLoginName"]?.InnerText ?? "").Trim();
                    string encryptedPass = root["ocLastPassword"]?.InnerText ?? "";
                    settings.LastPassword = DecryptPassword(encryptedPass);
                    settings.LastCash = root["ocLastCash"]?.InnerText ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[OnlineCity] Не вдалося завантажити налаштування з " + FilePath + ": " + ex);
            }

            return settings;
        }
    }
}