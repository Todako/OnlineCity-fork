using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OCUnion.Common
{
    public static class FileChecker
    {
        public static readonly List<string> IgnoredModFiles = new List<string>
            { ".cs", ".csproj", ".sln", ".gitignore", ".gitattributes", ".DS_Store", ".ogg", ".wav", ".mp3" };

        public static readonly List<string> IgnoredModFolders = new List<string>
            { "bin", "obj", ".vs" };

        public static readonly List<string> IgnoredConfigFiles = new List<string>
            { "KeyPrefs.xml", "Knowledge.xml", "LastPlayedVersion.txt", "Prefs.xml", ".DS_Store" };

        private static readonly HashSet<string> IgnoredModFilesSet =
            new HashSet<string>(IgnoredModFiles, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> IgnoredConfigFilesSet =
            new HashSet<string>(IgnoredConfigFiles, StringComparer.OrdinalIgnoreCase);

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

                using (var f = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
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

                var nativeRelPath = serverFile.FileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fullName = Path.Combine(modsDir, nativeRelPath);

                if (serverFile.Hash == null)
                {
                    if (File.Exists(fullName))
                    {
                        try
                        {
                            File.Delete(fullName);
                        }
                        catch (Exception ex)
                        {
                            Loger.Log($"FileSynchronization: failed to delete {fullName}: {ex.Message}", Loger.LogLevel.WARNING);
                        }
                    }
                    continue;
                }

                try
                {
                    var dir = Path.GetDirectoryName(fullName);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
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
                catch (Exception ex)
                {
                    Loger.Log($"FileSynchronization: failed to write {fullName}: {ex.Message}", Loger.LogLevel.ERROR);
                }
            }
        }

        private static int nnnn = 0;

        private static ModelFileInfo GenerateHashXMLString(string XML, List<string> ignoreTag)
        {
            if (string.IsNullOrEmpty(XML)) return new ModelFileInfo { FileName = "" };

            if (ignoreTag != null && ignoreTag.Count > 0)
            {
                for (int i = 0; i < ignoreTag.Count; i++)
                {
                    var item = ignoreTag[i];
                    if (string.IsNullOrEmpty(item)) continue;

                    if (item.StartsWith("{lineWith}", StringComparison.OrdinalIgnoreCase))
                    {
                        var lineKey = item.Substring("{lineWith}".Length);
                        if (lineKey.Length > 0 && XML.IndexOf(lineKey, StringComparison.Ordinal) >= 0)
                        {
                            var sb = new StringBuilder(XML.Length);
                            int currentPos = 0;
                            while (currentPos < XML.Length)
                            {
                                int lineEnd = XML.IndexOf('\n', currentPos);
                                int nextPos = lineEnd >= 0 ? lineEnd + 1 : XML.Length;
                                int lineLen = lineEnd >= 0 ? lineEnd - currentPos : XML.Length - currentPos;

                                if (XML.IndexOf(lineKey, currentPos, lineLen, StringComparison.Ordinal) < 0)
                                {
                                    sb.Append(XML, currentPos, nextPos - currentPos);
                                }
                                currentPos = nextPos;
                            }
                            XML = sb.ToString();
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
            if (XMLByte == null || XMLByte.Length == 0) return new ModelFileInfo { FileName = "" };

            if (ignoreTag == null || ignoreTag.Count == 0)
            {
                int offset = 0;
                int count = XMLByte.Length;

                if (XMLByte.Length >= 3 && XMLByte[0] == 0xEF && XMLByte[1] == 0xBB && XMLByte[2] == 0xBF)
                {
                    offset = 3;
                    count -= 3;
                }

                var directResult = new ModelFileInfo { FileName = "" };
                using (var sha = SHA512.Create())
                {
                    directResult.Hash = sha.ComputeHash(XMLByte, offset, count);
                }
                return directResult;
            }

            var XML = Encoding.UTF8.GetString(XMLByte);
            return GenerateHashXMLString(XML, ignoreTag);
        }

        public static ModelFileInfo GenerateHashXML(string XMLFileName, List<string> ignoreTag)
        {
            if (!File.Exists(XMLFileName)) return null;
            var XML = File.ReadAllText(XMLFileName, Encoding.UTF8);
            return GenerateHashXMLString(XML, ignoreTag);
        }

        public static bool IsIgnoreFolder(string path, List<string> ignoreFolder)
        {
            if (ignoreFolder == null || ignoreFolder.Count == 0 || string.IsNullOrEmpty(path)) return false;

            char sep = Path.DirectorySeparatorChar;
            char altSep = Path.AltDirectorySeparatorChar;

            for (int i = 0; i < ignoreFolder.Count; i++)
            {
                var f = ignoreFolder[i];
                if (string.IsNullOrEmpty(f)) continue;

                int start = 0;
                int end = f.Length;
                while (start < end && (f[start] == '/' || f[start] == '\\')) start++;
                while (end > start && (f[end - 1] == '/' || f[end - 1] == '\\')) end--;
                int len = end - start;
                if (len == 0) continue;

                int index = 0;
                while ((index = IndexOfSegment(path, f, start, len, index)) >= 0)
                {
                    bool startOk = index == 0 || path[index - 1] == sep || path[index - 1] == altSep;
                    int endIdx = index + len;
                    bool endOk = endIdx == path.Length || path[endIdx] == sep || path[endIdx] == altSep;

                    if (startOk && endOk) return true;

                    index += len;
                }
            }

            return false;
        }

        private static int IndexOfSegment(string source, string target, int targetStart, int targetLen, int startIndex)
        {
            if (startIndex + targetLen > source.Length) return -1;
            int max = source.Length - targetLen;
            for (int i = startIndex; i <= max; i++)
            {
                if (string.Compare(source, i, target, targetStart, targetLen, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return i;
                }
            }
            return -1;
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

            int folderIndex = 0;
            foreach (var folder in Directory.EnumerateDirectories(rootFolder))
            {
                if (IsIgnoreFolder(folder, ignoreFolder))
                {
                    folderIndex++;
                    continue;
                }

                onStartUpdateFolder?.Invoke(folder, folderIndex);

#if DEBUG
                var di = new DirectoryInfo(folder);
                if ("OnlineCity".Equals(di.Name, StringComparison.OrdinalIgnoreCase))
                {
                    folderIndex++;
                    continue;
                }
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

                folderIndex++;
            }

            return result;
        }

        private static void restoreFolderTree(string modsDir, FoldersTree foldersTree)
        {
            if (foldersTree?.SubDirs == null) return;

            for (int i = 0; i < foldersTree.SubDirs.Count; i++)
            {
                var folder = foldersTree.SubDirs[i];
                var nativeDir = folder.directoryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var dirName = Path.Combine(modsDir, nativeDir);
                if (!Directory.Exists(dirName))
                {
                    Loger.Log($"Create directory: {dirName}");
                    Directory.CreateDirectory(dirName);
                }

                restoreFolderTree(dirName, folder);
            }
        }

        private static void generateHashFiles(List<ModelFileInfo> result, ref string rootFolder, string folder, List<string> ignoreFolder, bool level0 = false)
        {
            if (!level0)
            {
                foreach (var subDir in Directory.EnumerateDirectories(folder))
                {
                    if (IsIgnoreFolder(subDir, ignoreFolder)) continue;
                    generateHashFiles(result, ref rootFolder, subDir, ignoreFolder);
                }
            }

            int fileNamePos = rootFolder.Length;
            if (!rootFolder.EndsWith("\\") && !rootFolder.EndsWith("/"))
            {
                fileNamePos++;
            }

            var targetFiles = new List<(string FullPath, string RelPath)>();
            foreach (var filePath in Directory.EnumerateFiles(folder))
            {
                if (ApproveExt(filePath))
                {
                    var relPath = filePath.Length > fileNamePos ? filePath.Substring(fileNamePos) : string.Empty;
                    relPath = relPath.Replace('\\', '/');
                    targetFiles.Add((filePath, relPath));
                }
            }

            if (targetFiles.Count == 0) return;

            var batchResults = new ModelFileInfo[targetFiles.Count];

            if (targetFiles.Count < 4)
            {
                using (var sha = SHA512.Create())
                {
                    for (int i = 0; i < targetFiles.Count; i++)
                    {
                        var tf = targetFiles[i];
                        var mfi = new ModelFileInfo { FileName = tf.RelPath };
                        ComputeHashDirect(mfi, tf.FullPath, sha);
                        batchResults[i] = mfi;
                    }
                }
            }
            else
            {
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
            }

            for (int i = 0; i < batchResults.Length; i++)
            {
                if (batchResults[i] != null && batchResults[i].Hash != null)
                {
                    result.Add(batchResults[i]);
                }
            }
        }

        public static void ReHashFiles(List<ModelFileInfo> rep, string folder, List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0 || rep == null) return;

            var dir = new Dictionary<string, ModelFileInfo>(rep.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rep.Count; i++)
            {
                if (rep[i]?.FileName != null)
                {
                    rep[i].FileName = rep[i].FileName.Replace('\\', '/');
                    dir[rep[i].FileName] = rep[i];
                }
            }

            var itemsToHash = new List<(ModelFileInfo Mfi, string FullPath)>(fileNames.Count);

            for (int i = 0; i < fileNames.Count; i++)
            {
                var rawName = fileNames[i];
                if (string.IsNullOrEmpty(rawName)) continue;

                var fileName = rawName.Replace('\\', '/');
                if (!dir.TryGetValue(fileName, out var mfi))
                {
                    mfi = new ModelFileInfo { FileName = fileName };
                    rep.Add(mfi);
                    dir[fileName] = mfi;
                }
                else
                {
                    mfi.FileName = fileName;
                }

                var nativeRelPath = fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(folder, nativeRelPath);
                itemsToHash.Add((mfi, fullPath));
            }

            if (itemsToHash.Count < 4)
            {
                using (var sha = SHA512.Create())
                {
                    for (int i = 0; i < itemsToHash.Count; i++)
                    {
                        ComputeHashDirect(itemsToHash[i].Mfi, itemsToHash[i].FullPath, sha);
                    }
                }
            }
            else
            {
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
            }

            rep.RemoveAll(x => x.Hash == null);
        }

        private static bool ApproveExt(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && IgnoredModFilesSet.Contains(ext)) return false;

            var name = Path.GetFileName(fileName);
            if (!string.IsNullOrEmpty(name))
            {
                if (IgnoredModFilesSet.Contains(name)) return false;
                if (IgnoredConfigFilesSet.Contains(name)) return false;
            }

            return true;
        }
    }
}