using OCUnion;
using OCUnion.Common;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Verse;

namespace RimWorldOnlineCity.ClientHashCheck
{
    /// <summary>
    /// Відповідає за сканування каталогів гри, формування списку файлів
    /// та швидкий розрахунок контрольних сум SHA-512 з використанням інкрементального бінарного кешу.
    /// </summary>
    public class ClientFileChecker
    {
        public FolderCheck Folder { get; }
        public string FolderPath { get; }

        public FolderType FolderType => Folder?.FolderType ?? FolderType.GamePath;

        public Action<string, int> OnChangeFolderAction { get; set; }
        public List<ModelFileInfo> FilesHash { get; private set; }
        public bool Complete { get; private set; }

        public ClientFileChecker(FolderCheck folder, string folderPath)
        {
            Folder = folder;
            FolderPath = folderPath.NormalizePath();
        }

        /// <summary>
        /// Запис у локальному бінарному кеші контрольних сум.
        /// </summary>
        private class FileHashCacheEntry
        {
            public string RelativePath;
            public long LastWriteTimeUtcTicks;
            public long FileSize;
            public byte[] Hash;
        }

        /// <summary>
        /// Повертає шлях до бінарного файлу кешу для поточного типу директорії.
        /// </summary>
        private string GetCacheFilePath()
        {
            try
            {
                var cacheDir = Path.Combine(GenFilePaths.ConfigFolderPath, "OnlineCity");
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }
                return Path.Combine(cacheDir, $"HashCache_{(int)FolderType}.bin");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Завантаження кешу з диска. Формат: Magic (0x4F434648 "OCFH"), Version, Count, Entries.
        /// </summary>
        private Dictionary<string, FileHashCacheEntry> LoadCache(string cacheFile)
        {
            var cache = new Dictionary<string, FileHashCacheEntry>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(cacheFile) || !File.Exists(cacheFile)) return cache;

            try
            {
                using (var fs = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                using (var reader = new BinaryReader(fs))
                {
                    var magic = reader.ReadInt32();
                    if (magic != 0x4F434648) return cache; // "OCFH"

                    var version = reader.ReadInt32();
                    if (version != 1) return cache;

                    var count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var relPath = reader.ReadString();
                        var ticks = reader.ReadInt64();
                        var size = reader.ReadInt64();
                        var hashLen = reader.ReadByte();
                        var hash = reader.ReadBytes(hashLen);

                        cache[relPath] = new FileHashCacheEntry
                        {
                            RelativePath = relPath,
                            LastWriteTimeUtcTicks = ticks,
                            FileSize = size,
                            Hash = hash
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("ClientFileChecker LoadCache Exception: " + ex.Message, Loger.LogLevel.WARNING);
                cache.Clear();
            }

            return cache;
        }

        /// <summary>
        /// Збереження актуального кешу на диск з атомарною заміною файлу.
        /// </summary>
        private void SaveCache(string cacheFile, Dictionary<string, FileHashCacheEntry> cache)
        {
            if (string.IsNullOrEmpty(cacheFile) || cache == null) return;

            try
            {
                var tempFile = cacheFile + ".tmp";
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write(0x4F434648); // Magic "OCFH"
                    writer.Write(1);          // Версія формату
                    writer.Write(cache.Count);
                    foreach (var kvp in cache)
                    {
                        var entry = kvp.Value;
                        writer.Write(entry.RelativePath);
                        writer.Write(entry.LastWriteTimeUtcTicks);
                        writer.Write(entry.FileSize);
                        writer.Write((byte)entry.Hash.Length);
                        writer.Write(entry.Hash);
                    }
                }

                if (File.Exists(cacheFile)) File.Delete(cacheFile);
                File.Move(tempFile, cacheFile);
            }
            catch (Exception ex)
            {
                Loger.Log("ClientFileChecker SaveCache Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        /// <summary>
        /// Головна процедура швидкого розрахунку хешів.
        /// Сканує файлову систему, зіставляє метадані з кешем та паралельно хешує лише нові/змінені файли.
        /// </summary>
        public void CalculateHash()
        {
            var sw = Stopwatch.StartNew();
            Loger.Log($"CalculateHash start for: {FolderPath} ({FolderType})");

            if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
            {
                Loger.Log($"Directory not found {FolderPath}", Loger.LogLevel.ERROR);
                FilesHash = new List<ModelFileInfo>();
                Complete = true;
                return;
            }

            var cacheFilePath = GetCacheFilePath();
            var cache = LoadCache(cacheFilePath);
            var cacheDirty = false;

            var candidateFiles = new List<string>();
            var ignoreFolders = FolderType != FolderType.GamePath ? new List<string>() : new List<string> { "mods" };

            CollectFilesRecursive(FolderPath, FolderPath, ignoreFolders, candidateFiles, OnChangeFolderAction);

            var resultList = new List<ModelFileInfo>(candidateFiles.Count);
            var misses = new List<(string FullPath, string RelPath, FileInfo Info)>();
            var visitedRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rootPrefixLen = FolderPath.Length;
            if (!FolderPath.EndsWith("\\") && !FolderPath.EndsWith("/")) rootPrefixLen++;

            for (int i = 0; i < candidateFiles.Count; i++)
            {
                var file = candidateFiles[i];
                var relPath = file.Substring(rootPrefixLen).NormalizePath();
                if (relPath.StartsWith("\\")) relPath = relPath.Substring(1);

                if (FolderType == FolderType.GamePath)
                {
                    var ignorePath = "mods\\".NormalizePath();
                    if (relPath.StartsWith(ignorePath, StringComparison.OrdinalIgnoreCase)
                        || relPath.Contains(("\\" + ignorePath).NormalizePath()))
                    {
                        continue;
                    }
                }

                visitedRelPaths.Add(relPath);

                var fi = new FileInfo(file);
                if (!fi.Exists) continue;

                if (cache.TryGetValue(relPath, out var cached)
                    && cached.LastWriteTimeUtcTicks == fi.LastWriteTimeUtc.Ticks
                    && cached.FileSize == fi.Length
                    && cached.Hash != null
                    && cached.Hash.Length == 64)
                {
                    resultList.Add(new ModelFileInfo
                    {
                        FileName = relPath,
                        Hash = cached.Hash,
                        Size = cached.FileSize
                    });
                }
                else
                {
                    misses.Add((file, relPath, fi));
                }
            }

            if (misses.Count > 0)
            {
                cacheDirty = true;
                var newHashed = new ConcurrentBag<(ModelFileInfo Mfi, FileHashCacheEntry Entry)>();

                Parallel.ForEach(misses, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, item =>
                {
                    try
                    {
                        using (var sha = SHA512.Create())
                        using (var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                        {
                            var hash = sha.ComputeHash(stream);
                            var size = stream.Length;

                            var mfi = new ModelFileInfo
                            {
                                FileName = item.RelPath,
                                Hash = hash,
                                Size = size
                            };

                            var entry = new FileHashCacheEntry
                            {
                                RelativePath = item.RelPath,
                                LastWriteTimeUtcTicks = item.Info.LastWriteTimeUtc.Ticks,
                                FileSize = size,
                                Hash = hash
                            };

                            newHashed.Add((mfi, entry));
                        }
                    }
                    catch (Exception ex)
                    {
                        Loger.Log($"Error hashing file {item.FullPath}: {ex.Message}", Loger.LogLevel.WARNING);
                    }
                });

                foreach (var pair in newHashed)
                {
                    resultList.Add(pair.Mfi);
                    cache[pair.Entry.RelativePath] = pair.Entry;
                }
            }

            var removedKeys = new List<string>();
            foreach (var key in cache.Keys)
            {
                if (!visitedRelPaths.Contains(key))
                {
                    removedKeys.Add(key);
                }
            }
            if (removedKeys.Count > 0)
            {
                cacheDirty = true;
                for (int i = 0; i < removedKeys.Count; i++)
                {
                    cache.Remove(removedKeys[i]);
                }
            }

            if (cacheDirty)
            {
                SaveCache(cacheFilePath, cache);
            }

            FilesHash = resultList;
            Complete = true;

            sw.Stop();
            Loger.Log($"CalculateHash completed for {FolderPath}: total={resultList.Count}, calculated={misses.Count}, cached={resultList.Count - misses.Count} in {sw.ElapsedMilliseconds} ms");
        }

        public void RecalculateHash(List<string> lists)
        {
            FileChecker.ReHashFiles(FilesHash, FolderPath, lists);
            UpdateCacheForSpecificFiles(lists);
        }

        private void UpdateCacheForSpecificFiles(List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0) return;
            try
            {
                var cacheFilePath = GetCacheFilePath();
                var cache = LoadCache(cacheFilePath);
                var fileDict = new Dictionary<string, ModelFileInfo>(StringComparer.OrdinalIgnoreCase);

                if (FilesHash != null)
                {
                    for (int i = 0; i < FilesHash.Count; i++)
                    {
                        if (FilesHash[i]?.FileName != null)
                            fileDict[FilesHash[i].FileName] = FilesHash[i];
                    }
                }

                bool updated = false;
                foreach (var fileName in fileNames)
                {
                    var normRel = fileName.NormalizePath();
                    if (normRel.StartsWith("\\")) normRel = normRel.Substring(1);

                    var fullPath = Path.Combine(FolderPath, normRel);
                    if (File.Exists(fullPath) && fileDict.TryGetValue(normRel, out var mfi) && mfi.Hash != null)
                    {
                        var fi = new FileInfo(fullPath);
                        cache[normRel] = new FileHashCacheEntry
                        {
                            RelativePath = normRel,
                            LastWriteTimeUtcTicks = fi.LastWriteTimeUtc.Ticks,
                            FileSize = fi.Length,
                            Hash = mfi.Hash
                        };
                        updated = true;
                    }
                    else
                    {
                        if (cache.Remove(normRel)) updated = true;
                    }
                }

                if (updated)
                {
                    SaveCache(cacheFilePath, cache);
                }
            }
            catch (Exception ex)
            {
                Loger.Log("ClientFileChecker UpdateCacheForSpecificFiles Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        private static void CollectFilesRecursive(
            string currentDir,
            string rootDir,
            List<string> ignoreFolders,
            List<string> collectedFiles,
            Action<string, int> onFolderChange)
        {
            try
            {
                var files = Directory.GetFiles(currentDir);
                for (int i = 0; i < files.Length; i++)
                {
                    if (ApproveFileExtension(files[i]))
                    {
                        collectedFiles.Add(files[i]);
                    }
                }
            }
            catch { }

            try
            {
                var subDirs = Directory.GetDirectories(currentDir);
                var isRoot = string.Equals(currentDir, rootDir, StringComparison.OrdinalIgnoreCase);

                for (int i = 0; i < subDirs.Length; i++)
                {
                    var dir = subDirs[i];
                    if (FileChecker.IsIgnoreFolder(dir, ignoreFolders)) continue;

                    if (isRoot)
                    {
                        onFolderChange?.Invoke(dir, i);
                    }

                    CollectFilesRecursive(dir, rootDir, ignoreFolders, collectedFiles, onFolderChange);
                }
            }
            catch { }
        }

        private static bool ApproveFileExtension(string filePath)
        {
            for (int i = 0; i < FileChecker.IgnoredModFiles.Count; i++)
            {
                if (filePath.EndsWith(FileChecker.IgnoredModFiles[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }
}