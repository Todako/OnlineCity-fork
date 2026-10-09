using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using System.IO;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class CheckFiles : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request35ListFiles;

        public int ResponseTypePackage => (int)PackageType.Response36ListFiles;

        private const long MaxPacketSize = 5000000;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = checkFiles((ModelModsFilesRequest)request.Packet, context);
            return result;
        }

        private ModelModsFilesResponse checkFiles(ModelModsFilesRequest packet, ServiceContext context)
        {
            if (!ServerManager.FileHashChecker.CheckedDirAndFiles.TryGetValue(
                packet.CodeRequest,
                out var checkedDirAndFile))
            {
                return new ModelModsFilesResponse()
                {
                    Folder = new FolderCheck() { FolderType = packet.FolderType },
                    Files = new List<ModelFileInfo>(0),
                    FoldersTree = new FoldersTree(),
                    TotalSize = 0
                };
            }

            var result = new List<ModelFileInfo>(32);

            if (packet.CodeRequest % 1000 == 0)
            {
                // Нормалізуємо серверний список файлів під '/' для гарантованого збігу
                var allServerFiles = new Dictionary<string, ModelFileInfo>(checkedDirAndFile.HashFiles.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in checkedDirAndFile.HashFiles)
                {
                    if (kvp.Value == null) continue;
                    var normKey = kvp.Key.Replace('\\', '/').ToLowerInvariant();
                    allServerFiles[normKey] = kvp.Value;
                }

                var packetFiles = packet.Files;
                long packetSize = 0;
                long totalSize = 0;

                if (packetFiles != null)
                {
                    for (int i = 0; i < packetFiles.Count; i++)
                    {
                        var modelFile = packetFiles[i];
                        if (modelFile?.FileName == null) continue;

                        var modelFileFileName = modelFile.FileName.Replace('\\', '/').ToLowerInvariant();
                        if (FileHashChecker.FileNameContainsIgnored(modelFileFileName, checkedDirAndFile.IgnoredFiles, checkedDirAndFile.IgnoredFolder))
                        {
                            continue;
                        }

                        if (allServerFiles.TryGetValue(modelFileFileName, out ModelFileInfo fileInfo))
                        {
                            allServerFiles.Remove(modelFileFileName);

                            if (!ModelFileInfo.UnsafeByteArraysEquale(modelFile.Hash, fileInfo.Hash))
                            {
                                if (packetSize < MaxPacketSize)
                                {
                                    var addFile = GetFile(checkedDirAndFile.Settings.ServerPath, fileInfo.FileName, checkedDirAndFile.Settings.NeedReplace, fileInfo.Size);
                                    result.Add(addFile);
                                    packetSize += addFile.Size;
                                    totalSize += addFile.Size;
                                }
                                else
                                {
                                    var size = fileInfo.Size > 0 ? fileInfo.Size : GetFileSize(checkedDirAndFile.Settings.ServerPath, fileInfo.FileName);
                                    totalSize += size;
                                }
                            }
                        }
                        else
                        {
                            // Файл є у клієнта, але відсутній на сервері
                            modelFile.FileName = modelFile.FileName.Replace('\\', '/');
                            modelFile.Hash = null;
                            modelFile.NeedReplace = checkedDirAndFile.Settings.NeedReplace;
                            result.Add(modelFile);
                        }
                    }
                }

                lock (context.Player)
                {
                    // Файли, які є на сервері, але відсутні у клієнта
                    if (allServerFiles.Count > 0)
                    {
                        foreach (var kvp in allServerFiles)
                        {
                            var normName = kvp.Key;
                            var serverFileInfo = kvp.Value;

                            if (FileHashChecker.FileNameContainsIgnored(normName, checkedDirAndFile.IgnoredFiles, checkedDirAndFile.IgnoredFolder))
                            {
                                continue;
                            }

                            context.Player.ApproveLoadWorldReason = false;

                            if (packetSize < MaxPacketSize)
                            {
                                var addFile = GetFile(checkedDirAndFile.Settings.ServerPath, serverFileInfo.FileName, checkedDirAndFile.Settings.NeedReplace, serverFileInfo.Size);
                                result.Add(addFile);
                                packetSize += addFile.Size;
                                totalSize += addFile.Size;
                            }
                            else
                            {
                                var size = serverFileInfo.Size > 0 ? serverFileInfo.Size : GetFileSize(checkedDirAndFile.Settings.ServerPath, serverFileInfo.FileName);
                                totalSize += size;
                            }
                        }
                    }

                    if (result.Count > 0)
                    {
                        context.Player.ApproveLoadWorldReason = false;
                    }
                }

                return new ModelModsFilesResponse()
                {
                    Folder = checkedDirAndFile.Settings,
                    Files = result,
                    FoldersTree = result.Count > 0 ? checkedDirAndFile.FolderTree : new FoldersTree(),
                    TotalSize = totalSize,
                };
            }
            else
            {
                var addFile = GetFile(checkedDirAndFile.Settings.ServerPath, checkedDirAndFile.Settings.XMLFileName, true);
                addFile.NeedReplace = checkedDirAndFile.Settings.NeedReplace;
                result.Add(addFile);

                return new ModelModsFilesResponse()
                {
                    Folder = checkedDirAndFile.Settings,
                    Files = result,
                    FoldersTree = new FoldersTree(),
                    TotalSize = 0,
                    IgnoreTag = checkedDirAndFile.Settings.IgnoreTag
                };
            }
        }

        private ModelFileInfo GetFile(string rootDir, string fileName, bool needReplace, long knownSize = 0)
        {
            var newFile = new ModelFileInfo()
            {
                FileName = fileName.Replace('\\', '/'),
                NeedReplace = needReplace
            };

            var nativeRelPath = fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var fullname = Path.Combine(rootDir, nativeRelPath);

            if (needReplace)
            {
                newFile.Hash = File.ReadAllBytes(fullname);
                newFile.Size = newFile.Hash.Length;
            }
            else
            {
                newFile.Size = knownSize > 0 ? knownSize : GetFileSize(rootDir, fileName);
            }
            return newFile;
        }

        private long GetFileSize(string rootDir, string fileName)
        {
            try
            {
                var nativeRelPath = fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fullname = Path.Combine(rootDir, nativeRelPath);
                return new FileInfo(fullname).Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}