using OCUnion;
using OCUnion.Common;
using OCUnion.Transfer.Model;
using OCUnion.Transfer.Types;
using RimWorldOnlineCity.ClientHashCheck;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Verse;
using RimWorldOnlineCity.Model;

namespace RimWorldOnlineCity.Services
{
    sealed class ClientHashChecker : IOnlineCityClientService<bool>
    {
        public PackageType RequestTypePackage => PackageType.Request35ListFiles;
        public PackageType ResponseTypePackage => PackageType.Response36ListFiles;

        private readonly Transfer.SessionClient _sessionClient;

        public ClientHashCheckerResult Report { get; set; }

        /// <summary>
        /// null - вибір ще не зроблено;
        /// true - користувач підтвердив заміну;
        /// false - користувач скасував синхронізацію (вихід у меню).
        /// </summary>
        public bool? UserConfirmedReplacement { get; private set; } = null;

        public ClientHashChecker(Transfer.SessionClient sessionClient)
        {
            _sessionClient = sessionClient;
        }

        public bool GenerateRequestAndDoJob(object context)
        {
            // Якщо користувач раніше обрав вихід у меню — припиняємо обробку наступних папок
            if (UserConfirmedReplacement == false)
            {
                return false;
            }

            bool result = true;
            UpdateModsWindow.Title = "OC_Hash_Downloading".Translate();
            UpdateModsWindow.HashStatus = "";
            UpdateModsWindow.SummaryList = null;

            var clientFileChecker = (ClientFileChecker)context;
            var model = new ModelModsFilesRequest()
            {
                FolderType = clientFileChecker.Folder.FolderType,
                Files = clientFileChecker.FilesHash,
                NumberFileRequest = 0
            };

            long totalSize = 0;
            long downloadSize = 0;

            try
            {
                while (true)
                {
                    if (UserConfirmedReplacement == false)
                    {
                        return false;
                    }

                    Loger.Log($"Send hash {clientFileChecker.Folder.FolderType} N{model.NumberFileRequest}");

                    var res = _sessionClient.TransObject2<ModelModsFilesResponse>(model, RequestTypePackage, ResponseTypePackage);

                    if (res.Files == null || res.Files.Count == 0)
                    {
                        if (model.NumberFileRequest == 0)
                        {
                            model.NumberFileRequest = 1;
                            continue;
                        }
                        break;
                    }

                    if (res.IgnoreTag != null && res.IgnoreTag.Count > 0)
                    {
                        var XMLFileName = Path.Combine(clientFileChecker.FolderPath, res.Files[0].FileName);
                        var xmlServer = FileChecker.GenerateHashXML(res.Files[0].Hash, res.IgnoreTag);
                        var xmlClient = FileChecker.GenerateHashXML(XMLFileName, res.IgnoreTag);

                        if (xmlClient != null && xmlServer.Equals(xmlClient))
                        {
                            Loger.Log("File XML good: " + res.Files[0].FileName);
                            res.Files.RemoveAt(0);
                        }
                        else
                        {
                            Loger.Log("File XML need for a change: " + res.Files[0].FileName
                                + $" {(xmlClient?.Hash == null ? "" : Convert.ToBase64String(xmlClient.Hash).Substring(0, 6))} "
                                + $"-> {(xmlServer?.Hash == null ? "" : Convert.ToBase64String(xmlServer.Hash).Substring(0, 6))} "
                                + " withoutTag: " + res.IgnoreTag[0], Loger.LogLevel.WARNING);
                        }
                    }

                    if (res.Files.Count > 0)
                    {
                        if (totalSize == 0) totalSize = res.TotalSize;

                        for (int i = 0; i < res.Files.Count; i++)
                        {
                            downloadSize += res.Files[i].Size;
                        }

                        Loger.Log($"Files that need for a change: {downloadSize}/{totalSize} count={res.Files.Count}", Loger.LogLevel.WARNING);
                        var pr = downloadSize > totalSize || totalSize == 0 ? 100 : (int)(downloadSize * 100 / totalSize);
                        UpdateModsWindow.HashStatus = "OC_Hash_Downloading_Finish".Translate() + pr.ToString() + "%";

                        result = false;

                        // Перевіряємо чи є файли, які потребують заміни/видалення
                        bool hasNeedReplace = false;
                        for (int i = 0; i < res.Files.Count; i++)
                        {
                            if (res.Files[i].NeedReplace)
                            {
                                hasNeedReplace = true;
                                break;
                            }
                        }

                        if (hasNeedReplace)
                        {
                            if (UserConfirmedReplacement == null)
                            {
                                UserConfirmedReplacement = AskUserForFileReplacement();
                            }

                            // Користувач відмовився: тихо перериваємо з'єднання без показу додаткових вікон
                            if (UserConfirmedReplacement == false)
                            {
                                Loger.Log("ClientHashChecker: User declined file replacement. Aborting to main menu.", Loger.LogLevel.INFO);
                                ModBaseData.RunMainThread(() =>
                                {
                                    UpdateModsWindow.CompletedAndClose = true;
                                    SessionClientController.Disconnected(null); // null = вихід у головне меню без діалогового вікна
                                });
                                return false;
                            }

                            FileChecker.FileSynchronization(clientFileChecker.FolderPath, res);
                        }

                        var changedFileNames = new List<string>(res.Files.Count);
                        for (int i = 0; i < res.Files.Count; i++)
                        {
                            changedFileNames.Add(res.Files[i].FileName);
                        }
                        clientFileChecker.RecalculateHash(changedFileNames);

                        Report.FileSynchronization(res.Files);

                        if (UpdateModsWindow.SummaryList == null)
                        {
                            UpdateModsWindow.SummaryList = new List<string>();
                        }

                        var existingDirs = new HashSet<string>(UpdateModsWindow.SummaryList, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < res.Files.Count; i++)
                        {
                            var fileName = res.Files[i].FileName;
                            var slashPos = fileName.IndexOf('\\');
                            if (slashPos > 0)
                            {
                                var topFolder = fileName.Substring(0, slashPos);
                                if (existingDirs.Add(topFolder))
                                {
                                    UpdateModsWindow.SummaryList.Add(topFolder);
                                }
                            }
                        }
                    }

                    if (res.TotalSize == 0
                        || (res.IgnoreTag != null && res.IgnoreTag.Count > 0)
                        || res.Files.Any(f => !f.NeedReplace))
                    {
                        model.NumberFileRequest++;
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Loger.Log("ClientHashChecker Exception: " + ex.ToString());
                SessionClientController.Disconnected("Error " + ex.Message);
                return false;
            }
        }

        private bool AskUserForFileReplacement()
        {
            bool userDecision = false;

            using (var waitEvent = new ManualResetEvent(false))
            {
                ModBaseData.RunMainThread(() =>
                {
                    try
                    {
                        var title = "OC_Hash_ConfirmReplace_Title".Translate();
                        if (title == "OC_Hash_ConfirmReplace_Title")
                        {
                            title = "Synchronize files";
                        }

                        var text = "OC_Hash_ConfirmReplace_Text".Translate();
                        if (text == "OC_Hash_ConfirmReplace_Text")
                        {
                            text = "The game or mod files differ from those installed on the server.\n\n" +
                                   "Full file compatibility is required to connect.\n" +
                                   "The game will automatically restart after your files are updated.\n" +
                                   "Do you want to sync the files or cancel the connection and return to the main menu?";
                        }

                        var btnReplace = "OC_Hash_Btn_Replace".Translate();
                        if (btnReplace == "OC_Hash_Btn_Replace")
                        {
                            btnReplace = "Synchronize files";
                        }

                        var btnCancel = "OC_Hash_Btn_Cancel".Translate();
                        if (btnCancel == "OC_Hash_Btn_Cancel")
                        {
                            btnCancel = "Main menu";
                        }

                        var dialog = new Dialog_MessageBox(
                            text: text,
                            buttonAText: btnReplace,
                            buttonAAction: () =>
                            {
                                userDecision = true;
                                waitEvent.Set();
                            },
                            buttonBText: btnCancel,
                            buttonBAction: () =>
                            {
                                userDecision = false;
                                waitEvent.Set();
                            },
                            title: title,
                            buttonADestructive: true
                        );

                        dialog.closeOnCancel = false;
                        dialog.closeOnAccept = false;

                        Find.WindowStack.Add(dialog);
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("AskUserForFileReplacement Exception: " + ex.Message, Loger.LogLevel.ERROR);
                        userDecision = false;
                        waitEvent.Set();
                    }
                });

                waitEvent.WaitOne();
            }

            return userDecision;
        }
    }
}