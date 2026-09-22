using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerCore.Model;
using ServerOnlineCity.Mechanics;
using ServerOnlineCity.Model;
using ServerOnlineCity.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using Transfer;
using Util;

namespace ServerOnlineCity
{
    /// <summary>
    /// Головне сховище стану сервера: керує завантаженням та збереженням світу,
    /// обліковими записами гравців, блокуваннями (IP/Hardware Key) та чергою завдань таймера.
    /// </summary>
    public class Repository
    {
        private static readonly Repository Current = new Repository();
        public static Repository Get => Current;

        public static BaseContainer GetData => Get.Data;
        public static RepositorySaveData GetSaveData => Get.RepSaveData;
        public static RepositoryFileSharing GetFileSharing => Get.RepFileSharing;

        public BaseContainer Data;
        public bool ChangeData;

        public readonly WorkTimer Timer;

        public string SaveFileName;
        public string SaveFolderDataPlayers => Path.Combine(Path.GetDirectoryName(SaveFileName.NormalizePath()), "DataPlayers");

        private readonly RepositorySaveData RepSaveData;
        private readonly RepositoryFileSharing RepFileSharing;

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public static PlayerServer GetPlayerByLogin(string login, bool withNotApprove = false)
        {
            if (string.IsNullOrEmpty(login)) return null;

            PlayerServer res;
            if (withNotApprove) Repository.GetData.PlayersAllDicWithNotApprove.TryGetValue(login, out res);
            else Repository.GetData.PlayersAllDic.TryGetValue(login, out res);

            return res;
        }

        public static State GetStateByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Repository.GetData.StatesDic.TryGetValue(name, out var res);
            return res;
        }

        public static StatePosition GetStatePosition(Player player) => GetStatePositionByName(player?.StateName, player?.StatePositionName);

        public static StatePosition GetStatePositionByName(string nameState, string namePosition)
        {
            if (string.IsNullOrEmpty(nameState) || string.IsNullOrEmpty(namePosition)) return null;

            if (Repository.GetData.StatePositionsDic.TryGetValue(nameState, out var resState)
                && resState.TryGetValue(namePosition, out var res))
            {
                return res;
            }
            return null;
        }

        /// <summary>
        /// Перевірка клієнта на блокування за апаратним ключем (Hardware ID).
        /// </summary>
        public static string CheckIsIntruder(ServiceContext context, string key, string login)
        {
            if (key == null || !key.Contains("@@@"))
            {
                context.PossiblyIntruder = true;
                Loger.Log($"Is possibly intruder or not update {login} (empty key)", Loger.LogLevel.WARNING);
                return key;
            }

            string isIntruder = "";
            var keys = key.Split(new[] { "@@@" }, StringSplitOptions.None);
            var keysCleaned = new List<string>(keys.Length);
            var intruderKeysSb = new StringBuilder();

            for (int i = 1; i < keys.Length; i++)
            {
                var k = keys[i]?.Trim();
                if (string.IsNullOrEmpty(k) || k.Length <= 3) continue;

                keysCleaned.Add(k);
                intruderKeysSb.Append("@@@").Append(k);

                if (string.IsNullOrEmpty(isIntruder) && CheckIsIntruder(k))
                {
                    Loger.Log($"Is intruder {login} key={k}", Loger.LogLevel.WARNING);
                    isIntruder = k;
                }
            }

            context.IntruderKeys = intruderKeysSb.ToString();

            if (context.IntruderKeys.Length == 0)
            {
                context.PossiblyIntruder = true;
                Loger.Log($"Is possibly intruder or not update {login} (empty keys)", Loger.LogLevel.WARNING);
                return key;
            }

            if (string.IsNullOrEmpty(isIntruder) && !string.IsNullOrEmpty(login) && CheckIsIntruder(login))
            {
                Loger.Log($"Is intruder {login} by login", Loger.LogLevel.WARNING);
                isIntruder = login;
            }

            if (!string.IsNullOrEmpty(isIntruder))
            {
                if (!string.IsNullOrEmpty(login)) keysCleaned.Add(login);
                AddIntruder(keysCleaned, $" auto add by {isIntruder} (login {login} IP {context.AddrIP})");
                context.Disconnect("intruder");
            }

            Loger.Log($"Checked {login} key={key.Substring(key.IndexOf("@@@") + 3)}");
            return keys[0];
        }

        private static HashSet<string> Blockkey = null;
        private static DateTime BlockkeyUpdate = DateTime.MinValue;
        private static DateTime BlockkeyLastWriteTime = DateTime.MinValue;

        /// <summary>
        /// Перевіряє ключ у списку блокувань blockkey.txt.
        /// ОПТИМІЗАЦІЯ: файл перечитується лише за умови реальної зміни мітки часу на диску.
        /// </summary>
        public static bool CheckIsIntruder(string key)
        {
            if ((DateTime.UtcNow - BlockkeyUpdate).TotalSeconds > 30)
            {
                BlockkeyUpdate = DateTime.UtcNow;
                var fileName = Loger.PathLog + "blockkey.txt";

                if (!File.Exists(fileName))
                {
                    Blockkey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    BlockkeyLastWriteTime = DateTime.MinValue;
                }
                else
                {
                    try
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(fileName);
                        if (Blockkey == null || lastWrite != BlockkeyLastWriteTime)
                        {
                            BlockkeyLastWriteTime = lastWrite;

                            var lines = File.ReadAllLines(fileName, Encoding.UTF8);
                            var keySet = new HashSet<string>(lines.Length, StringComparer.OrdinalIgnoreCase);
                            var addPl = new List<PlayerServer>();

                            for (int i = 0; i < lines.Length; i++)
                            {
                                var line = lines[i].Replace("@@@", "").Trim();
                                if (line.Length == 0) continue;

                                int spaceIdx = line.IndexOf(' ');
                                if (spaceIdx > 0) line = line.Substring(0, spaceIdx);
                                if (line.Length == 0) continue;

                                keySet.Add(line);

                                if (line.Length != 20)
                                {
                                    var pl = GetPlayerByLogin(line);
                                    if (pl != null) addPl.Add(pl);
                                }
                            }

                            Blockkey = keySet;

                            foreach (var pl in addPl)
                            {
                                if (string.IsNullOrEmpty(pl.IntruderKeys)) continue;
                                var add = pl.IntruderKeys.Split(new[] { "@@@" }, StringSplitOptions.None)
                                    .Where(k => k.Length > 3 && !Blockkey.Contains(k))
                                    .ToList();
                                if (add.Count > 0) AddIntruder(add, $" auto add by login {pl.Public.Login}");
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        Loger.Log("CheckIsIntruder error: " + exp.Message, Loger.LogLevel.ERROR);
                        if (Blockkey == null) Blockkey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        return false;
                    }
                }
            }

            if (string.IsNullOrEmpty(key) || Blockkey == null) return false;
            var kk = key.Replace("@@@", "").Trim();
            return Blockkey.Contains(kk);
        }

        public static void AddIntruder(List<string> keys, string comment)
        {
            if (Blockkey == null) CheckIsIntruder("");

            var sb = new StringBuilder();
            foreach (var key in keys)
            {
                var k = key.Replace("@@@", "").Trim();
                if (k.Length > 0 && (Blockkey == null || !Blockkey.Contains(k)))
                {
                    sb.AppendLine(k + " //" + comment.Replace("\r", "").Replace("\n", " "));
                }
            }

            if (sb.Length > 0)
            {
                var fileName = Loger.PathLog + "blockkey.txt";
                File.AppendAllText(fileName, sb.ToString(), Encoding.UTF8);

                // Негайно скидаємо таймер для оновлення кешу
                BlockkeyUpdate = DateTime.MinValue;
                CheckIsIntruder("");
            }
        }

        private static HashSet<string> Blockip = null;
        private static DateTime BlockipUpdate = DateTime.MinValue;
        private static DateTime BlockipLastWriteTime = DateTime.MinValue;

        /// <summary>
        /// Перевіряє IP-адресу клієнта у списку заблокованих blockip.txt.
        /// ОПТИМІЗАЦІЯ: файл перечитується лише у разі зміни файлу на диску.
        /// </summary>
        public static bool CheckIsBanIP(string IP)
        {
            if (string.IsNullOrEmpty(IP)) return false;

            if ((DateTime.UtcNow - BlockipUpdate).TotalSeconds > 30)
            {
                BlockipUpdate = DateTime.UtcNow;
                var fileName = Loger.PathLog + "blockip.txt";

                if (!File.Exists(fileName))
                {
                    Blockip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    BlockipLastWriteTime = DateTime.MinValue;
                }
                else
                {
                    try
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(fileName);
                        if (Blockip == null || lastWrite != BlockipLastWriteTime)
                        {
                            BlockipLastWriteTime = lastWrite;

                            var lines = File.ReadAllLines(fileName, Encoding.UTF8);
                            bool hasSubnet = false;
                            for (int i = 0; i < lines.Length; i++)
                            {
                                if (lines[i].Contains("/"))
                                {
                                    hasSubnet = true;
                                    break;
                                }
                            }

                            if (hasSubnet)
                            {
                                var expanded = new List<string>(lines.Length);
                                foreach (var b in lines)
                                {
                                    var bb = b.Trim();
                                    var comment = "";
                                    var ic = bb.IndexOf(" ");
                                    if (ic > 0)
                                    {
                                        comment = bb.Substring(ic);
                                        bb = bb.Substring(0, ic);
                                    }
                                    if (bb.Any(c => !char.IsDigit(c) && c != '.' && c != '/')) continue;
                                    var ls = bb.LastIndexOf("/");
                                    if (ls < 0) { expanded.Add(bb + comment); continue; }
                                    var lp = bb.LastIndexOf(".");
                                    if (lp <= 0) continue;
                                    if (!int.TryParse(bb.Substring(lp + 1, ls - (lp + 1)), out int ib) ||
                                        !int.TryParse(bb.Substring(ls + 1), out int ie)) continue;
                                    var s = bb.Substring(0, lp + 1);
                                    for (int i = ib; i <= ie; i++)
                                        expanded.Add(s + i.ToString() + comment);
                                }
                                lines = expanded.ToArray();
                                File.WriteAllLines(fileName, lines, Encoding.Default);
                                BlockipLastWriteTime = File.GetLastWriteTimeUtc(fileName);
                            }

                            var ipSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < lines.Length; i++)
                            {
                                var line = lines[i].Trim();
                                if (line.Length == 0) continue;

                                int spaceIdx = line.IndexOf(' ');
                                if (spaceIdx > 0) line = line.Substring(0, spaceIdx);
                                ipSet.Add(line);
                            }
                            Blockip = ipSet;
                        }
                    }
                    catch (Exception exp)
                    {
                        Loger.Log("CheckIsBanIP error: " + exp.Message, Loger.LogLevel.ERROR);
                        if (Blockip == null) Blockip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        return false;
                    }
                }
            }

            return Blockip != null && Blockip.Contains(IP.Trim());
        }

        /// <summary>
        /// Повне очищення світу від об'єктів, угод та скріншотів вказаного гравця.
        /// ОПТИМІЗАЦІЯ: видалення зі списків виконується з кінця за O(N) замість O(N^2).
        /// </summary>
        public static void DropUserFromMap(string login)
        {
            var data = Repository.GetData;
            lock (data)
            {
                if (data.WorldObjectsDeleted == null) data.WorldObjectsDeleted = new List<WorldObjectEntry>();

                var now = DateTime.UtcNow;
                for (int i = data.WorldObjects.Count - 1; i >= 0; i--)
                {
                    var item = data.WorldObjects[i];
                    if (item.LoginOwner != login) continue;

                    item.UpdateTime = now;
                    data.WorldObjects.RemoveAt(i);
                    data.WorldObjectsDeleted.Add(item);
                }

                for (int i = data.Orders.Count - 1; i >= 0; i--)
                {
                    var item = data.Orders[i];
                    if (item.Owner.Login != login) continue;
                    data.OrderOperator.OrderRemove(item);
                }

                var folderName = Get.RepFileSharing.GetFolderName(FileSharingCategory.ColonyScreen);
                if (Directory.Exists(folderName))
                {
                    var files = Directory.GetFiles(folderName, login + "_*_*.png");
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            if (File.Exists(files[i])) File.Delete(files[i]);
                        }
                        catch { }
                    }
                }

                for (int i = 0; i < data.PlayersAll.Count; i++)
                {
                    var fMails = data.PlayersAll[i].FunctionMails;
                    if (fMails == null) continue;

                    for (int ii = fMails.Count - 1; ii >= 0; ii--)
                    {
                        if (fMails[ii] is FMailIncident fmail && fmail.Mail?.From?.Login == login)
                        {
                            fMails.RemoveAt(ii);
                        }
                    }
                }
            }
        }

        public Repository()
        {
            Timer = new WorkTimer();
            RepSaveData = new RepositorySaveData(this);
            RepFileSharing = new RepositoryFileSharing(this);
        }

        public void Load()
        {
            bool needResave = false;
            if (!Directory.Exists(SaveFolderDataPlayers))
                Directory.CreateDirectory(SaveFolderDataPlayers);

            if (!File.Exists(SaveFileName))
            {
                Data = new BaseContainer();
                Save();
                Data.PostLoad();
                Loger.Log("Server Create Data");
            }
            else
            {
                using (var fs = new FileStream(SaveFileName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                {
                    var bf = new BinaryFormatter { Binder = new ServerCoreSerializationBinder() };
                    Loger.Log("Server Load... " + (new FileInfo(SaveFileName).FullName));
                    Data = (BaseContainer)bf.Deserialize(fs);

                    Loger.Log("Server Version data: " + Data.Version + " Current version: " + MainHelper.VersionInfo);
                    Loger.Log($"Server unified version {MainHelper.VersionNum}");

                    if (Data.Version != MainHelper.VersionInfo || Data.VersionNum < MainHelper.VersionNum + 1)
                    {
                        needResave = true;
                    }

                    Data.PostLoad();

                    Loger.Log("Server Load done. Users " + Data.GetPlayersAll.Count + ": "
                        + string.Join(", ", Data.GetPlayerLoginsAll));

                    ChatManager.Instance.NewChatManager(Data.MaxIdChat, Data.PlayerSystem.Chats.Keys.First());
                }

                Loger.Log($"Server local time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}");
                Loger.Log($"The difference between time zones: {(DateTime.UtcNow - DateTime.Now):g}");
            }

            if (needResave) Save();
            ChangeData = false;
        }

        /// <summary>
        /// Збереження глобального стану сервера з підтримкою резервної копії .bak.
        /// ОПТИМІЗАЦІЯ: використання 64-КБ буфера та FileMode.Create для надійного блокового запису.
        /// </summary>
        public void Save(bool onlyChangeData = false)
        {
            if (onlyChangeData && !ChangeData) return;
            Loger.Log("Server Saving");

            try
            {
                if (File.Exists(SaveFileName))
                {
                    if (File.Exists(SaveFileName + ".bak")) File.Delete(SaveFileName + ".bak");
                    File.Move(SaveFileName, SaveFileName + ".bak");
                }

                Data.MaxIdChat = ChatManager.Instance.MaxChatId;

                using (var fs = new FileStream(SaveFileName, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                {
                    var bf = new BinaryFormatter();
                    bf.Serialize(fs, Data);
                }

                ChangeData = false;
            }
            catch
            {
                if (File.Exists(SaveFileName + ".bak"))
                    File.Copy(SaveFileName + ".bak", SaveFileName, true);
                throw;
            }

            Loger.Log("Server Saved");
        }

        /// <summary>
        /// Очищає логін від неприпустимих символів для файлової системи.
        /// ОПТИМІЗАЦІЯ: швидкий прохід через StringBuilder без викликів Path.GetInvalidFileNameChars() на кожен запуск.
        /// </summary>
        public static string NormalizeLogin(string login)
        {
            if (string.IsNullOrEmpty(login)) return string.Empty;

            var sb = new StringBuilder(login.Length);
            for (int i = 0; i < login.Length; i++)
            {
                char c = login[i];
                bool invalid = false;
                for (int j = 0; j < InvalidFileNameChars.Length; j++)
                {
                    if (c == InvalidFileNameChars[j])
                    {
                        invalid = true;
                        break;
                    }
                }
                sb.Append(invalid ? '_' : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}