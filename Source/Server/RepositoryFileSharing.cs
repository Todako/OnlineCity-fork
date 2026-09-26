using OCUnion;
using OCUnion.Common;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ServerOnlineCity
{
    public class RepositoryFileSharing
    {
        private readonly Repository MainRepository;

        private ConcurrentDictionary<string, ModelFileSharing> CacheFileDataByFileName = new ConcurrentDictionary<string, ModelFileSharing>();

        private long CacheSize;
        private const long CacheSizeMax = 10 * 1024 * 1024; // 10 МБ ліміт кешу
        private DateTime CacheClear;
        private const int CacheClearMaxMinute = 30;
        private readonly object CacheLock = new object();

        private ConcurrentDictionary<FileSharingCategory, IFileSharingWorker> Workers = null;
        private readonly IFileSharingWorker WorkersDefault = new WorkerDefault();

        public RepositoryFileSharing(Repository repository)
        {
            MainRepository = repository;
        }

        /// <summary>
        /// Зберігає надісланий файл у сховищі на диску та розраховує для нього контрольний хеш.
        /// </summary>
        public bool SaveFileSharing(PlayerServer player, ModelFileSharing fileSharing)
        {
            Loger.Log($"SaveFileSharing {player.Public.Login} {fileSharing.Category} {fileSharing.Name} {fileSharing.Data?.Length ?? 0}");
            var worker = GetWorker(fileSharing.Category);
            var fileName = worker.CheckAndGetFileNameUpload(player, fileSharing);
            if (string.IsNullOrEmpty(fileName)) return false;

            fileName = Path.Combine(GetFolderName(fileSharing.Category), fileName).NormalizePath();
            if (!CheckFileName(fileName)) return false;

            try
            {
                var dir = Path.GetDirectoryName(fileName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(fileName, fileSharing.Data);
                fileSharing.Hash = GetHash(fileSharing.Data);

                CacheFileDataByFileName.TryRemove(fileName, out _);
                return true;
            }
            catch (Exception ext)
            {
                Loger.Log("FileSharing Exception " + ext.ToString(), Loger.LogLevel.ERROR);
                return false;
            }
        }

        /// <summary>
        /// Зчитує файл зі сховища. Якщо наданий клієнтом хеш збігається з файлом на сервері,
        /// повторна передача байтів не виконується (економія трафіку).
        /// </summary>
        public void LoadFileSharing(PlayerServer player, ModelFileSharing fileSharing)
        {
            var fileSharingHash = fileSharing.Hash;
            fileSharing.Hash = null;

            var worker = GetWorker(fileSharing.Category);
            var fileName = worker.CheckAndGetFileNameDownload(player, fileSharing);
            if (string.IsNullOrEmpty(fileName)) return;

            fileName = Path.Combine(GetFolderName(fileSharing.Category), fileName);
            if (!CheckFileName(fileName)) return;

            lock (CacheLock)
            {
                if ((DateTime.UtcNow - CacheClear).TotalMinutes > CacheClearMaxMinute || CacheSize > CacheSizeMax)
                {
                    CacheClear = DateTime.UtcNow;
                    CacheSize = 0;
                    CacheFileDataByFileName = new ConcurrentDictionary<string, ModelFileSharing>();
                }
            }

            var fileData = CacheFileDataByFileName.GetOrAdd(fileName, _ =>
            {
                var fd = GetDataFileSharing(fileName);
                if (fd == null)
                {
                    lock (CacheLock) { CacheSize += 1024; }
                    return null;
                }
                fd.Name = fileSharing.Name;
                fd.Category = fileSharing.Category;

                lock (CacheLock) { CacheSize += fd.Data.Length + 1024; }
                return fd;
            });

            if (fileData == null) return;

            // Якщо хеш клієнта збігається — не пересилаємо сирі байти
            if (!string.IsNullOrEmpty(fileSharingHash) && fileSharingHash == fileData.Hash)
            {
                fileSharing.Hash = fileSharingHash;
                return;
            }

            fileSharing.Hash = fileData.Hash;
            fileSharing.Data = fileData.Data;
        }

        private bool CheckFileName(string fileName)
        {
            if (!string.IsNullOrWhiteSpace(fileName)
                && !fileName.Contains("..")
                && !fileName.Contains(@"\\")
                && !fileName.Contains(@"//"))
            {
                return true;
            }

            Loger.Log("FileSharing: некоректне ім'я файлу або спроба виходу з директорії: " + fileName, Loger.LogLevel.WARNING);
            return false;
        }

        private ModelFileSharing GetDataFileSharing(string fileName)
        {
            try
            {
                if (!File.Exists(fileName)) return null;

                var data = File.ReadAllBytes(fileName);
                return new ModelFileSharing()
                {
                    Data = data,
                    Hash = GetHash(data),
                };
            }
            catch (Exception ext)
            {
                Loger.Log("FileSharing GetData Exception: " + ext.ToString(), Loger.LogLevel.ERROR);
                return null;
            }
        }

        private string GetHash(byte[] data)
        {
            return FileChecker.GetCheckSum(data);
        }

        internal string GetFolderName(FileSharingCategory category)
        {
            var categoryName = Repository.NormalizeLogin(Enum.GetName(typeof(FileSharingCategory), category));
            return Path.Combine(MainRepository.SaveFolderDataPlayers, categoryName);
        }

        private IFileSharingWorker GetWorker(FileSharingCategory category)
        {
            if (Workers == null)
            {
                var categories = Enum.GetValues(typeof(FileSharingCategory))
                    .Cast<FileSharingCategory>()
                    .ToDictionary(c => Enum.GetName(typeof(FileSharingCategory), c));

                var workers = new ConcurrentDictionary<FileSharingCategory, IFileSharingWorker>();
                foreach (var type in Assembly.GetAssembly(typeof(RepositoryFileSharing)).GetTypes())
                {
                    if (!type.IsClass) continue;

                    if (type.GetInterfaces().Any(x => x == typeof(IFileSharingWorker)))
                    {
                        var worker = (IFileSharingWorker)Activator.CreateInstance(type);
                        var workerName = worker.GetType().Name;
                        var workerCategory = categories.Keys.FirstOrDefault(cName => workerName.EndsWith(cName));
                        if (workerCategory != null)
                        {
                            worker.Category = categories[workerCategory];
                            worker.FileSharing = this;
                            workers[categories[workerCategory]] = worker;
                        }
                    }
                }
                Workers = workers;
            }

            if (Workers.TryGetValue(category, out var res))
                return res;
            else
                return WorkersDefault;
        }

        private interface IFileSharingWorker
        {
            FileSharingCategory Category { get; set; }
            RepositoryFileSharing FileSharing { get; set; }
            string CheckAndGetFileNameDownload(PlayerServer player, ModelFileSharing info);
            string CheckAndGetFileNameUpload(PlayerServer player, ModelFileSharing info);
        }

        #region Обробники категорій файлів (Workers)

        private class WorkerDefault : IFileSharingWorker
        {
            public FileSharingCategory Category { get; set; }
            public RepositoryFileSharing FileSharing { get; set; }

            public virtual string CheckAndGetFileNameDownload(PlayerServer player, ModelFileSharing info)
            {
                return Repository.NormalizeLogin(info.Name);
            }

            public virtual string CheckAndGetFileNameUpload(PlayerServer player, ModelFileSharing info)
            {
                Loger.Log($"Server FileSharing Save WorkerDefault {player.Public.Login} size={info.Data?.Length}b name={info.Name}");
                return Repository.NormalizeLogin(info.Name);
            }
        }

        private class WorkerPlayerIcon : WorkerDefault
        {
            private const int NeedSize = 256;

            public override string CheckAndGetFileNameDownload(PlayerServer player, ModelFileSharing info)
            {
                return base.CheckAndGetFileNameDownload(player, info) + ".png";
            }

            public override string CheckAndGetFileNameUpload(PlayerServer player, ModelFileSharing info)
            {
                Loger.Log($"Server FileSharing Save PlayerIcon {player.Public.Login} size={info.Data?.Length}b");

                // Файли понад 2 МБ відхиляються
                if (info.Data == null || info.Data.Length == 0 || info.Data.Length > 2 * 1024 * 1024) return null;

                try
                {
                    using (var msInput = new MemoryStream(info.Data))
                    using (var imageData = new Bitmap(msInput))
                    {
                        Loger.Log($"Server FileSharing Save PlayerIcon {player.Public.Login} orig={imageData.Width}*{imageData.Height}");

                        if (imageData.Width < 10 || imageData.Height < 10 || imageData.Width > 2000 || imageData.Height > 2000)
                            return null;

                        var rectData = imageData.Width >= imageData.Height
                            ? new Rectangle((imageData.Width - imageData.Height) / 2, 0, imageData.Height - 1, imageData.Height - 1)
                            : new Rectangle(0, (imageData.Height - imageData.Width) / 2, imageData.Width - 1, imageData.Width - 1);

                        using (var imageEnd = new Bitmap(NeedSize, NeedSize))
                        {
                            using (var graphics = Graphics.FromImage(imageEnd))
                            {
                                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                graphics.DrawImage(imageData, new Rectangle(0, 0, NeedSize, NeedSize), rectData, GraphicsUnit.Pixel);
                            }

                            using (var msOutput = new MemoryStream())
                            {
                                imageEnd.Save(msOutput, ImageFormat.Png);
                                info.Data = msOutput.ToArray();
                            }
                        }

                        Loger.Log($"Server FileSharing Save PlayerIcon {player.Public.Login} end={NeedSize}*{NeedSize}");
                    }
                }
                catch (Exception ext)
                {
                    Loger.Log($"Server FileSharing Save PlayerIcon {player.Public.Login} Exception: " + ext.ToString(), Loger.LogLevel.ERROR);
                    return null;
                }

                return base.CheckAndGetFileNameUpload(player, info) + ".png";
            }
        }

        private class WorkerColonyScreen : IFileSharingWorker
        {
            public FileSharingCategory Category { get; set; }
            public RepositoryFileSharing FileSharing { get; set; }

            // В name передається логін@serverId колонії, знімок якої запитується.
            // При записі дописується поточний ігровий тік, при зчитуванні шукається файл із максимальним тіком.
            public string CheckAndGetFileNameDownload(PlayerServer player, ModelFileSharing info)
            {
                try
                {
                    string name = info.Name;
                    if (string.IsNullOrEmpty(name)) return null;

                    var namePart = name.Split('@');
                    if (namePart.Length < 2) return null;

                    if (!int.TryParse(namePart[1], out int serverId)) return null;
                    var login = Repository.NormalizeLogin(namePart[0]);

                    var mask = login + "_" + serverId.ToString() + "_*.png";
                    var folderName = FileSharing.GetFolderName(Category);
                    if (!Directory.Exists(folderName)) return null;

                    var files = Directory.GetFiles(folderName, mask);
                    if (files.Length == 0) return null;

                    // ОПТИМІЗАЦІЯ: однопрохідний пошук найновішого тіка без LINQ-сортування списку
                    string latestFile = null;
                    long maxTick = -1;

                    for (int i = 0; i < files.Length; i++)
                    {
                        var f = files[i];
                        int i0 = f.LastIndexOf('_');
                        int i1 = f.LastIndexOf('.');
                        if (i0 >= 0 && i1 > i0)
                        {
                            var tickStr = f.Substring(i0 + 1, i1 - i0 - 1);
                            if (long.TryParse(tickStr, out long tick) && tick > maxTick)
                            {
                                maxTick = tick;
                                latestFile = f;
                            }
                        }
                    }

                    return latestFile == null ? null : Path.GetFileName(latestFile);
                }
                catch (Exception ext)
                {
                    Loger.Log("FileSharing CheckFileNameDownload Exception: " + ext.ToString(), Loger.LogLevel.ERROR);
                    return null;
                }
            }

            public string CheckAndGetFileNameUpload(PlayerServer player, ModelFileSharing info)
            {
                try
                {
                    Loger.Log($"Server FileSharing Save ColonyScreen {player.Public.Login} size={info.Data?.Length}b name={info.Name}");

                    // Файли понад 35 МБ відхиляються
                    if (info.Data == null || info.Data.Length == 0 || info.Data.Length > 35 * 1024 * 1024) return null;

                    string name = info.Name;
                    if (string.IsNullOrEmpty(name)) return null;

                    var namePart = name.Split('@');
                    if (namePart.Length < 2) return null;

                    if (!int.TryParse(namePart[1], out int serverId)) return null;
                    var login = Repository.NormalizeLogin(namePart[0]);
                    if (login != Repository.NormalizeLogin(player.Public.Login)) return null;

                    ControlSizeFolder();
                    var tick = player.Public.LastTick;

                    return login + "_" + serverId.ToString() + "_" + tick.ToString() + ".png";
                }
                catch (Exception ext)
                {
                    Loger.Log("FileSharing CheckFileNameUpload Exception: " + ext.ToString(), Loger.LogLevel.ERROR);
                    return null;
                }
            }

            /// <summary>
            /// Контроль загального розміру папки зі знімками поселень.
            /// Зберігає найновіший кадр для кожної колонії, видаляючи старіші дублікати при перевищенні квоти.
            /// </summary>
            private void ControlSizeFolder()
            {
                if (ServerManager.ServerSettings.ColonyScreenFolderMaxMb == 0) return;

                string folderPath = FileSharing.GetFolderName(Category);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    return;
                }

                var fileNames = Directory.GetFiles(folderPath, "*_*_*.png");
                if (fileNames.Length < 2) return;

                long totalBytes = 0;
                var filesInfo = new List<FileInfo>(fileNames.Length);
                for (int i = 0; i < fileNames.Length; i++)
                {
                    var fi = new FileInfo(fileNames[i]);
                    totalBytes += fi.Length;
                    filesInfo.Add(fi);
                }

                long maxAllowedBytes = (long)ServerManager.ServerSettings.ColonyScreenFolderMaxMb * 1024 * 1024;
                long needClear = totalBytes - maxAllowedBytes;
                if (needClear <= 0) return;

                // Групуємо файли за базовим ім'ям (login_serverId)
                var groups = new Dictionary<string, List<(FileInfo Info, long Tick)>>();
                for (int i = 0; i < filesInfo.Count; i++)
                {
                    var fi = filesInfo[i];
                    var fName = fi.Name;
                    int i0 = fName.LastIndexOf('_');
                    int i1 = fName.LastIndexOf('.');
                    if (i0 > 0 && i1 > i0 && long.TryParse(fName.Substring(i0 + 1, i1 - i0 - 1), out long tick))
                    {
                        var prefix = fName.Substring(0, i0);
                        if (!groups.TryGetValue(prefix, out var list))
                        {
                            list = new List<(FileInfo Info, long Tick)>();
                            groups[prefix] = list;
                        }
                        list.Add((fi, tick));
                    }
                }

                // Визначаємо кандидати на видалення (усі, крім останнього знімка кожної колонії)
                var candidates = new List<FileInfo>();
                foreach (var group in groups.Values)
                {
                    if (group.Count <= 1) continue;

                    long maxTick = -1;
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (group[i].Tick > maxTick) maxTick = group[i].Tick;
                    }

                    for (int i = 0; i < group.Count; i++)
                    {
                        if (group[i].Tick < maxTick)
                        {
                            candidates.Add(group[i].Info);
                        }
                    }
                }

                // Видаляємо починаючи з найстаріших за часом запису
                candidates.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (needClear <= 0) break;
                    needClear -= candidates[i].Length;
                    try
                    {
                        candidates[i].Delete();
                    }
                    catch { }
                }
            }
        }

        #endregion
    }
}