using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;

namespace OCUnion.Common
{
    public static class FileChecker
    {
        /// <summary>
        /// Розширення файлів, що виключаються з перевірки
        /// </summary>
        public static readonly List<string> IgnoredModFiles = new List<string>
            { ".cs", ".csproj", ".sln", ".gitignore", ".gitattributes", ".DS_Store" };

        public static readonly List<string> IgnoredModFolders = new List<string>
            { "bin", "obj", ".vs" };

        public static readonly List<string> IgnoredConfigFiles = new List<string>
            { "KeyPrefs.xml", "Knowledge.xml", "LastPlayedVersion.txt", "Prefs.xml", ".DS_Store" };

        public static string GetCheckSum(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            using (var sha = SHA512.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(data));
            }
        }

        public static string GetCheckSum(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;
            return GetCheckSum(Encoding.UTF8.GetBytes(data));
        }

        private static void ComputeHashDirect(ModelFileInfo mfi, string fileName, SHA512 sha)
        {
            try
            {
                if (!File.Exists(fileName))
                {
                    mfi.Hash = null;
                    return;
                }

                using (var f = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                {
                    mfi.Hash = sha.ComputeHash(f);
                    mfi.Size = f.Length;
                }
            }
            catch (Exception exp)
            {
                ExceptionUtil.ExceptionLog(exp, "GetCheckSum: " + fileName);
                mfi.Hash = null;
            }
        }

        public static void FileSynchronization(string modsDir, ModelModsFilesResponse serverFiles)
        {
            restoreFolderTree(modsDir, serverFiles.FoldersTree);

            if (serverFiles.Files == null) return;

            for (int i = 0; i < serverFiles.Files.Count; i++)
            {
                var serverFile = serverFiles.Files[i];
                if (!serverFile.NeedReplace) continue;

                var fullName = Path.Combine(modsDir, serverFile.FileName);

                if (serverFile.Hash == null)
                {
                    if (File.Exists(fullName))
                    {
                        File.Delete(fullName);
                    }
                    continue;
                }

                using (var fs = new FileStream(fullName, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                {
                    Loger.Log("Restore: " + fullName);
                    if (serverFile.Hash.Length > 0)
                    {
                        fs.Write(serverFile.Hash, 0, serverFile.Hash.Length);
                    }
                }
            }
        }

        private static int nnnn = 0;

        /// <summary>
        /// Розрахунок хешу XML без створення дублікатів масивів при обрізанні BOM.
        /// </summary>
        private static ModelFileInfo GenerateHashXMLString(string XML, List<string> ignoreTag)
        {
            if (ignoreTag != null)
            {
                for (int i = 0; i < ignoreTag.Count; i++)
                {
                    var item = ignoreTag[i];
                    if (item.StartsWith("{lineWith}", StringComparison.OrdinalIgnoreCase))
                    {
                        var lineKey = item.Substring("{lineWith}".Length);
                        if (lineKey.Length > 0)
                        {
                            int pos;
                            while ((pos = XML.IndexOf(lineKey, StringComparison.Ordinal)) >= 0)
                            {
                                var posB = XML.LastIndexOf('\n', pos);
                                if (posB < 0) posB = 0;
                                else posB++;

                                var posE = XML.IndexOf('\n', pos + lineKey.Length);
                                if (posE >= 0)
                                {
                                    posE++;
                                }
                                else
                                {
                                    posE = XML.Length;
                                    if (posB > 0)
                                    {
                                        posB--;
                                        if (posB > 0 && XML[posB] == '\r') posB--;
                                    }
                                }
                                XML = XML.Remove(posB, posE - posB);
                            }
                        }
                    }
                    else
                    {
                        XML = GameXMLUtils.ReplaceByTag(XML, item, "");
                    }
                }
            }

            var xb = Encoding.UTF8.GetBytes(XML);
            int offset = 0;
            int count = xb.Length;

            // ОПТИМІЗАЦІЯ: обрізання UTF-8 BOM через зсув без виділення нового масиву й Array.Copy
            if (xb.Length >= 3 && xb[0] == 0xEF && xb[1] == 0xBB && xb[2] == 0xBF)
            {
                offset = 3;
                count -= 3;
            }

            if (MainHelper.DebugMode)
            {
                File.WriteAllText(Loger.PathLog + "GenerateHashXMLString" + nnnn++ + ".txt", XML);
            }

            var result = new ModelFileInfo { FileName = "" };
            using (var sha = SHA512.Create())
            {
                result.Hash = sha.ComputeHash(xb, offset, count);
            }

            return result;
        }

        public static ModelFileInfo GenerateHashXML(byte[] XMLByte, List<string> ignoreTag)
        {
            var XML = Encoding.UTF8.GetString(XMLByte);
            return GenerateHashXMLString(XML, ignoreTag);
        }

        public static ModelFileInfo GenerateHashXML(string XMLFileName, List<string> ignoreTag)
        {
            if (!File.Exists(XMLFileName)) return null;
            var XML = File.ReadAllText(XMLFileName, Encoding.UTF8);
            return GenerateHashXMLString(XML, ignoreTag);
        }

        /// <summary>
        /// Перевірка чи належить шлях до ігнорованих каталогів без зайвих алокацій ToLower() та LINQ.
        /// </summary>
        public static bool IsIgnoreFolder(string path, List<string> ignoreFolder)
        {
            if (ignoreFolder == null || ignoreFolder.Count == 0 || string.IsNullOrEmpty(path)) return false;

            char sep = Path.DirectorySeparatorChar;
            string normPath = path.Replace('/', sep).Replace('\\', sep);
            if (normPath.Length == 0) return false;
            if (normPath[0] != sep) normPath = sep + normPath;
            if (normPath[normPath.Length - 1] != sep) normPath = normPath + sep;

            for (int i = 0; i < ignoreFolder.Count; i++)
            {
                var f = ignoreFolder[i];
                if (string.IsNullOrEmpty(f)) continue;

                var trimmed = f.Trim('/', '\\');
                if (trimmed.Length == 0) continue;

                var pattern = sep + trimmed + sep;
                if (normPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static List<ModelFileInfo> GenerateHashFiles(string rootFolder, Action<string, int> onStartUpdateFolder, List<string> ignoreFolder)
        {
            var result = new List<ModelFileInfo>();

            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder))
            {
                Loger.Log($"Directory not found {rootFolder}", Loger.LogLevel.ERROR);
                return result;
            }

            generateHashFiles(result, ref rootFolder, rootFolder, ignoreFolder, true);

            var checkFolders = Directory.GetDirectories(rootFolder);

            for (var i = 0; i < checkFolders.Length; i++)
            {
                var folder = checkFolders[i];
                if (IsIgnoreFolder(folder, ignoreFolder)) continue;
                onStartUpdateFolder?.Invoke(folder, i);

#if DEBUG
                var di = new DirectoryInfo(folder);
                if ("OnlineCity".Equals(di.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
#endif

                var checkFolder = Path.IsPathRooted(rootFolder) ? Path.Combine(rootFolder, folder) : folder;
                if (Directory.Exists(checkFolder))
                {
                    generateHashFiles(result, ref rootFolder, checkFolder, ignoreFolder);
                }
                else
                {
                    Loger.Log($"Directory not found {checkFolder}", Loger.LogLevel.ERROR);
                }
            }

            return result;
        }

        private static void restoreFolderTree(string modsDir, FoldersTree foldersTree)
        {
            if (foldersTree.SubDirs == null) return;

            for (int i = 0; i < foldersTree.SubDirs.Count; i++)
            {
                var folder = foldersTree.SubDirs[i];
                var dirName = Path.Combine(modsDir, folder.directoryName);
                if (!Directory.Exists(dirName))
                {
                    Loger.Log($"Create directory: {dirName}");
                    Directory.CreateDirectory(dirName);
                }

                restoreFolderTree(dirName, folder);
            }
        }

        /// <summary>
        /// Сканування та паралельний розрахунок контрольних сум файлів.
        /// ОПТИМІЗАЦІЯ: усунено цикл Thread.Sleep(0) та виділення задач на користь пулу SHA512 у Parallel.ForEach.
        /// </summary>
        private static void generateHashFiles(List<ModelFileInfo> result, ref string rootFolder, string folder, List<string> ignoreFolder, bool level0 = false)
        {
            if (!level0)
            {
                var dirs = Directory.GetDirectories(folder);
                for (int i = 0; i < dirs.Length; i++)
                {
                    var subDir = dirs[i];
                    if (IsIgnoreFolder(subDir, ignoreFolder)) continue;
                    generateHashFiles(result, ref rootFolder, subDir, ignoreFolder);
                }
            }

            var files = Directory.GetFiles(folder);
            int fileNamePos = rootFolder.Length + 1;

            var targetFiles = new List<(string FullPath, string RelPath)>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                if (ApproveExt(files[i]))
                {
                    targetFiles.Add((files[i], files[i].Substring(fileNamePos)));
                }
            }

            if (targetFiles.Count == 0) return;

            var batchResults = new ModelFileInfo[targetFiles.Count];

            Parallel.ForEach(
                Partitioner.Create(0, targetFiles.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
                () => SHA512.Create(),
                (range, loopState, sha) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        var tf = targetFiles[i];
                        var mfi = new ModelFileInfo { FileName = tf.RelPath };
                        ComputeHashDirect(mfi, tf.FullPath, sha);
                        batchResults[i] = mfi;
                    }
                    return sha;
                },
                sha => sha?.Dispose()
            );

            for (int i = 0; i < batchResults.Length; i++)
            {
                if (batchResults[i] != null)
                {
                    result.Add(batchResults[i]);
                }
            }
        }

        /// <summary>
        /// Повторний розрахунок хешів окремих файлів без блокуючих очікувань.
        /// </summary>
        public static void ReHashFiles(List<ModelFileInfo> rep, string folder, List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0) return;

            var dir = new Dictionary<string, ModelFileInfo>(rep.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rep.Count; i++)
            {
                if (rep[i]?.FileName != null)
                {
                    dir[rep[i].FileName] = rep[i];
                }
            }

            var itemsToHash = new List<(ModelFileInfo Mfi, string FullPath)>(fileNames.Count);

            for (int i = 0; i < fileNames.Count; i++)
            {
                var fileName = fileNames[i];
                if (!dir.TryGetValue(fileName, out var mfi))
                {
                    mfi = new ModelFileInfo { FileName = fileName };
                    rep.Add(mfi);
                    dir[fileName] = mfi;
                }

                var fullPath = Path.Combine(folder, fileName);
                itemsToHash.Add((mfi, fullPath));
            }

            Parallel.ForEach(
                itemsToHash,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) },
                () => SHA512.Create(),
                (item, loopState, sha) =>
                {
                    ComputeHashDirect(item.Mfi, item.FullPath, sha);
                    return sha;
                },
                sha => sha?.Dispose()
            );

            // ОПТИМІЗАЦІЯ: швидке лінійне O(N) очищення без RemoveAt
            rep.RemoveAll(x => x.Hash == null);
        }

        private static bool ApproveExt(string fileName)
        {
            for (int i = 0; i < IgnoredModFiles.Count; i++)
            {
                if (fileName.EndsWith(IgnoredModFiles[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }
}