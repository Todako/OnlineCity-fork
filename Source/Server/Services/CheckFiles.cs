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
                // Все гаразд, синхронізація не потрібна
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
                var allServerFiles = new HashSet<string>(checkedDirAndFile.HashFiles.Keys, StringComparer.Ordinal);
                var packetFiles = packet.Files;
                long packetSize = 0;
                long totalSize = 0;

                if (packetFiles != null)
                {
                    for (int i = 0; i < packetFiles.Count; i++)
                    {
                        var modelFile = packetFiles[i];
                        if (modelFile?.FileName == null) continue;

                        var modelFileFileName = modelFile.FileName.ToLowerInvariant();
                        if (FileHashChecker.FileNameContainsIgnored(modelFileFileName, checkedDirAndFile.IgnoredFiles, checkedDirAndFile.IgnoredFolder))
                        {
                            continue;
                        }

                        if (checkedDirAndFile.HashFiles.TryGetValue(modelFileFileName, out ModelFileInfo fileInfo))
                        {
                            allServerFiles.Remove(modelFileFileName);

                            if (!ModelFileInfo.UnsafeByteArraysEquale(modelFile.Hash, fileInfo.Hash))
                            {
                                // Файл знайдено, але хеші не збігаються — потрібна заміна
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
                            // Файл на сервері не знайдено — позначаємо на видалення
                            modelFile.Hash = null;
                            modelFile.NeedReplace = checkedDirAndFile.Settings.NeedReplace;
                            result.Add(modelFile);
                        }
                    }
                }

                lock (context.Player)
                {
                    // Перевіряємо у зворотному порядку: чи є у клієнта всі файли
                    if (allServerFiles.Count > 0)
                    {
                        foreach (var fileName in allServerFiles)
                        {
                            if (FileHashChecker.FileNameContainsIgnored(fileName, checkedDirAndFile.IgnoredFiles, checkedDirAndFile.IgnoredFolder))
                            {
                                continue;
                            }

                            context.Player.ApproveLoadWorldReason = false;

                            if (checkedDirAndFile.HashFiles.TryGetValue(fileName, out var serverFileInfo))
                            {
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
                    }

                    // Якщо файли не пройшли перевірку, блокуємо завантаження світу
                    if (result.Count > 0)
                    {
                        context.Player.ApproveLoadWorldReason = false;
                    }
                }

                return new ModelModsFilesResponse()
                {
                    Folder = checkedDirAndFile.Settings,
                    Files = result,
                    // Якщо файли не відновлюватимуться, дерево папок не передаємо
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
            var newFile = new ModelFileInfo() { FileName = fileName, NeedReplace = needReplace };
            if (needReplace)
            {
                var fullname = Path.Combine(rootDir, fileName);
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
                var fullname = Path.Combine(rootDir, fileName);
                return new FileInfo(fullname).Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}