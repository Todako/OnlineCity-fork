using Model;
using OCUnion;
using OCUnion.Common;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.ClientHashCheck;
using RimWorldOnlineCity.GameClasses;
using RimWorldOnlineCity.GameClasses.Harmony;
using RimWorldOnlineCity.Model;
using RimWorldOnlineCity.Services;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Transfer;
using UnityEngine;
using Util;
using Verse;
using Verse.Profile;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контейнер спільних даних і
    /// стандартні повторювані інструкції під час роботи з класом SessionClient.
    /// </summary>
    public static class SessionClientController
    {
        public static ClientData Data { get; set; }
        // Локальні дані поточної гри
        public static string ConnectAddr { get; set; }

        public static WorkTimer Timers { get; set; }
        public static WorkTimer TimerReconnect { get; set; }
        public static Player My { get; set; }

        public static TimeSpan ServerTimeDelta { get; set; }

        private const string SaveNameBase = "onlineCity";
        private static string SaveName => SaveNameBase + "_" + Data.ServerName.NormalizeFileNameChars();
        private static string SaveFullName => GenFilePaths.FilePathForSavedGame(SaveName);

        public static StorytellerDef ChoosedTeller { get; set; }

        public static string ConfigPath { get; private set; }

        public static bool LoginInNewServerIP { get; set; }

        public static ClientFileChecker[] ClientFileCheckers { get; private set; }
        public static bool ClientFileCheckersComplete { get; private set; }

        public static Action UpdateWorldSafelyRun { get; set; }

        private static int textureUpdateCounter = 0;

        /// <summary>
        /// Ініціалізація під час старту гри. Якомога раніше
        /// </summary>
        public static void Init()
        {
            MainHelper.InGame = true;

            ConfigPath = Path.Combine(GenFilePaths.ConfigFolderPath, "OnlineCity");
            if (!Directory.Exists(ConfigPath))
            {
                Directory.CreateDirectory(ConfigPath);
            }

            MainHelper.CultureFromGame = Prefs.LangFolderName ?? "";

            try
            {
                var path = new DirectoryInfo(GenFilePaths.ConfigFolderPath).Parent.FullName;
                var workPath = Path.Combine(path, "OnlineCity");
                Directory.CreateDirectory(workPath);
                Loger.PathLog = workPath;
                Loger.Enable = true;

                // Видаляємо логи, старіші за 7 днів
                var now = DateTime.UtcNow;
                foreach (var old in Directory.GetDirectories(workPath, "Log_*", SearchOption.TopDirectoryOnly))
                {
                    var info = new DirectoryInfo(old);
                    if (info.Exists && (now - info.CreationTimeUtc).TotalDays > 7d) info.Delete(true);
                }
                foreach (var old in Directory.GetFiles(workPath, "Log_*.*", SearchOption.TopDirectoryOnly))
                {
                    var info = new FileInfo(old);
                    if (info.Exists && (now - info.CreationTimeUtc).TotalDays > 7d) info.Delete();
                }
            }
            catch { }

            CacheResource.Init();

            Loger.Log("Client Init " + MainHelper.VersionInfo);
            Loger.Log("Client Language: " + Prefs.LangFolderName);
            Loger.Log($"Client local time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}");
            Loger.Log($"The difference between time zones: {(DateTime.UtcNow - DateTime.Now).ToString("g")}");

            Task.Factory.StartNew(() => SessionClientController.CalculateHash());
        }

        public static void CalculateHash()
        {
            try
            {
                Loger.Log("Client CalculateHash start");
                UpdateModsWindow.Title = "OC_Hash_CalculateLocalFiles".Translate();
                var factory = new ClientFileCheckerFactory();

                var folderTypeValues = Enum.GetValues(typeof(FolderType));
                ClientFileCheckersComplete = false;
                ClientFileCheckers = new ClientFileChecker[folderTypeValues.Length];
                var filesCount = 0;
                foreach (FolderType folderType in folderTypeValues)
                {
                    UpdateModsWindow.Title = "OC_Hash_CalculateFor".Translate() + folderType.ToString();
                    ClientFileCheckers[(int)folderType] = factory.GetFileChecker(folderType);
                    ClientFileCheckers[(int)folderType].CalculateHash();
                    filesCount += ClientFileCheckers[(int)folderType].FilesHash.Count;
                }

                UpdateModsWindow.Title = "OC_Hash_CalculateComplete".Translate();
                UpdateModsWindow.HashStatus = "OC_Hash_CalculateConfFile".Translate() + ClientFileCheckers[(int)FolderType.ModsConfigPath].FilesHash.Count.ToString() + "\n" +
                "Mods files: " + ClientFileCheckers[(int)FolderType.ModsFolder].FilesHash.Count.ToString();
                Loger.Log("Client CalculateHash end");
                ClientFileCheckersComplete = true;
            }
            catch (Exception exp)
            {
                Loger.Log("Client CalculateHash Exception " + exp.ToString());
            }
        }

        private static object UpdatingWorld = new object();
        private static int GetPlayersInfoCountRequest = 0;

        private static void UpdateWorld(bool firstRun = false)
        {
            lock (UpdatingWorld)
            {
                Command((connect) =>
                {
                    var errorNum = "0 ";
                    try
                    {
                        // Збираємо пакет на сервер
                        var toServ = new ModelPlayToServer()
                        {
                            UpdateTime = Data.UpdateTime, // Час попереднього запиту
                        };

                        // Дані збереження гри
                        if (Data.SaveFileData != null)
                        {
                            Data.AddTimeCheckTimerFail = true;
                            toServ.SaveFileData = Data.SaveFileData;
                            toServ.SingleSave = Data.SingleSave;
                            Data.SaveFileData = null;
                        }
                        errorNum += "00 ";

                        // Метод не виконується, коли гра згорнута
                        if (!ModBaseData.RunMainThreadSync(UpdateWorldController.PrepareInMainThread, 1, true)) return;

                        errorNum += "1 ";
                        // Збираємо дані з планети
                        if (!firstRun) UpdateWorldController.SendToServer(toServ, firstRun, null);

                        // Надіслати на сервер
                        errorNum += "2 ";
                        if (firstRun)
                        {
                            GetPlayersInfoCountRequest = 0;
                            ModelGameServerInfo gameServerInfo = connect.GetGameServerInfo();
                            errorNum += "3 ";
                            UpdateWorldController.SendToServer(toServ, firstRun, gameServerInfo);
                            errorNum += "4 ";
                        }

                        errorNum += "5 ";
                        // Запит на інформацію про гравців
                        if (Data.Chats != null && Data.Chats.Count > 0 && Data.Chats[0].PartyLogin != null)
                        {
                            if (Data.Players == null || Data.Players.Count == 0 || GetPlayersInfoCountRequest % 5 == 0)
                            {
                                // На початку та раз на пів хвилини (5 сек між UpdateWorld * 5) отримуємо інформацію про всіх
                                toServ.GetPlayersInfo = Data.Chats[0].PartyLogin;
                            }
                            else
                            {
                                // У проміжках — про тих, хто онлайн (без виділення замикань LINQ)
                                var onlineLogins = new List<string>(Data.Players.Count);
                                foreach (var p in Data.Players.Values)
                                {
                                    if (p.Online && p.Public?.Login != null)
                                    {
                                        onlineLogins.Add(p.Public.Login);
                                    }
                                }
                                toServ.GetPlayersInfo = onlineLogins;
                            }
                            GetPlayersInfoCountRequest++;
                        }

                        errorNum += "6 ";
                        // Відправляємо на сервер, отримуємо відповідь
                        ModelPlayToClient fromServ = connect.PlayInfo(toServ);
                        if (Data.AddTimeCheckTimerFail)
                        {
                            if (Timers != null) Timers.LastLoop = DateTime.UtcNow; // Скидаємо, щоб перевірка розриву з'єднання не розірвала підключення
                            Data.AddTimeCheckTimerFail = false;
                        }

                        errorNum += "7 ";
                        Loger.Log($"Client {My.Login} UpdateWorld myWO->{toServ.WObjects?.Count}"
                            + ((toServ.WObjectsToDelete?.Count ?? 0) > 0 ? " myWOToDelete->" + toServ.WObjectsToDelete.Count : "")
                            + (toServ.SaveFileData == null || toServ.SaveFileData.Length == 0 ? "" : " SaveData->" + toServ.SaveFileData.Length)
                            + ((fromServ.Mails?.Count ?? 0) > 0 ? " Mail<-" + fromServ.Mails.Count : "")
                            + (fromServ.AreAttacking ? " Attacking!" : "")
                            + (fromServ.NeedSaveAndExit ? " Disconnect command!" : "")
                            + (fromServ.PlayersInfo != null ? " Players<-" + fromServ.PlayersInfo.Count : "")
                            + (fromServ.States != null ? " States<-" + fromServ.States.Count : "")
                            + ((fromServ.WObjects?.Count ?? 0) > 0 ? " WO<-" + fromServ.WObjects.Count : "")
                            + ((fromServ.WObjectsToDelete?.Count ?? 0) > 0 ? " WOToDelete<-" + fromServ.WObjectsToDelete.Count : "")
                            + ((fromServ.FactionOnlineList?.Count ?? 0) > 0 ? " Faction<-" + fromServ.FactionOnlineList.Count : "")
                            + ((fromServ.WObjectOnlineList?.Count ?? 0) > 0 ? " NonPWO<-" + fromServ.WObjectOnlineList.Count : "")
                            );

                        // Зберігаємо час актуальності даних
                        Data.UpdateTime = fromServ.UpdateTime;
                        Data.UpdateTimeLocalTime = DateTime.UtcNow;

                        if (!string.IsNullOrEmpty(fromServ.KeyReconnect)) Data.KeyReconnect = fromServ.KeyReconnect;

                        // Оновлюємо інформацію про гравців
                        if (fromServ.PlayersInfo != null && fromServ.PlayersInfo.Count > 0)
                        {
                            for (int i = 0; i < fromServ.PlayersInfo.Count; i++)
                            {
                                var pi = fromServ.PlayersInfo[i];
                                if (pi.Login == null) continue;
                                Data.Players[pi.Login] = new PlayerClient() { Public = pi };
                                if (pi.Login == My.Login)
                                {
                                    My = pi;
                                    Data.MyEx = Data.Players[pi.Login];
                                }
                            }
                        }

                        // Приватна інформація про самого гравця
                        Data.CashlessBalance = fromServ.CashlessBalance;
                        Data.StorageBalance = fromServ.StorageBalance;

                        // Оновлюємо інформацію про держави
                        if (fromServ.States != null)
                        {
                            var statesDict = new Dictionary<string, StateInfo>(fromServ.States.Count);
                            for (int i = 0; i < fromServ.States.Count; i++)
                            {
                                var s = fromServ.States[i];
                                if (s?.Name != null) statesDict[s.Name] = s;
                            }
                            Data.States = statesDict;
                        }

                        errorNum += "8 ";
                        // Оновлюємо планету
                        UpdateWorldController.LoadFromServer(fromServ, firstRun);

                        errorNum += "9 ";
                        // Оновлюємо інформацію про поселення: однопрохідне оновлення караванів без N*M фільтрацій LINQ
                        foreach (var pair in Data.Players)
                        {
                            if (pair.Value.Public.Login == My.Login) continue;
                            if (pair.Value.WObjects == null)
                                pair.Value.WObjects = new List<CaravanOnline>();
                            else
                                pair.Value.WObjects.Clear();
                        }

                        var allWorldObjects = Find.WorldObjects.AllWorldObjects;
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            if (allWorldObjects[i] is CaravanOnline caravan && !string.IsNullOrEmpty(caravan.OnlinePlayerLogin))
                            {
                                if (caravan.OnlinePlayerLogin != My.Login && Data.Players.TryGetValue(caravan.OnlinePlayerLogin, out var pClient))
                                {
                                    if (pClient.WObjects == null) pClient.WObjects = new List<CaravanOnline>();
                                    pClient.WObjects.Add(caravan);
                                }
                            }
                        }

                        errorNum += "10 ";
                        // Зберігаємо та виходимо
                        if (fromServ.NeedSaveAndExit)
                        {
                            if (!SessionClientController.Data.BackgroundSaveGameOff)
                            {
                                SessionClientController.SaveGameNow(false, () =>
                                {
                                    SessionClientController.Disconnected("OCity_SessionCC_Shutdown_Command_ProgressSaved".Translate());
                                });
                            }
                            else
                                SessionClientController.Disconnected("OCity_SessionCC_Shutdown_Command".Translate());
                        }

                        // Якщо на нас напали, запускаємо процес
                        if (fromServ.AreAttacking && GameAttackHost.AttackMessage())
                        {
                            GameAttackHost.Get.Start(connect);
                        }

                        Data.CountReconnectBeforeUpdate = 0;
                    }
                    catch
                    {
                        Loger.Log("Client Exception errorNum = " + errorNum);
                        throw;
                    }
                });
            }
        }

        public static string CommandSafely(Func<SessionClient, bool> ActionCommand)
        {
            var errorMessage = "";
            Loger.Log("Client CommandSafely try ");
            int repeat = 0;
            do
            {
                Command((connect) =>
                {
                    if (ActionCommand(connect))
                    {
                        repeat = 1000;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(connect.ErrorMessage)) errorMessage = connect.ErrorMessage?.ServerTranslate();
                        Thread.Sleep(1000);
                        if (repeat > 0) Thread.Sleep(3000);
                        Loger.Log($"Client CommandSafely try again (error: {errorMessage})");
                    }
                });
            }
            while (++repeat < 3); // Робимо 3 спроби, враховуючи першу
            return repeat >= 1000 ? null : errorMessage;
        }

        public static void SaveGameNowSingleAndCommandSafely(
            Func<SessionClient, bool> ActionCommand,
            Action FinishGood,
            Action FinishBad,
            bool single = true)
        {
            bool actIsRuned = false;
            Action act = () =>
            {
                Data.ActionAfterReconnect = null;
                if (actIsRuned)
                {
                    Loger.Log("Client SaveGameNowSingleAndCommandSafely cancel by actIsRuned ");
                    return;
                }
                actIsRuned = true;

                if (CommandSafely(ActionCommand) != null)
                {
                    if (FinishBad != null) FinishBad();
                }
                else
                {
                    if (FinishGood != null) FinishGood();
                }
            };
            Data.ActionAfterReconnect = () =>
            {
                // Повторити, якщо під час збереження сталася помилка; якщо не вдасться — помилка
                Data.ActionAfterReconnect = FinishBad;
                SaveGameNow(single, act);
            };
            SaveGameNow(single, act);
        }

        private static byte[] SaveGameCore()
        {
            byte[] content;
            try
            {
                ScribeSaver_InitSaving_Patch.Enable = true;
                GameDataSaveLoader.SaveGame(SaveName);
                content = ScribeSaver_InitSaving_Patch.SaveData.ToArray();
            }
            finally
            {
                ScribeSaver_InitSaving_Patch.Enable = false;
                ScribeSaver_InitSaving_Patch.SaveData = null;
            }
            try
            {
                File.WriteAllBytes(SaveFullName, content);
            }
            catch
            {
            }
            return content;
        }

        private static void SaveGame(Action<byte[]> saved)
        {
            Loger.Log("Client SaveGame() ");
            LongEventHandler.QueueLongEvent(() =>
            {
                saved(SaveGameCore());
            }, "Autosaving", false, null);
        }

        public static void SaveGameNow(bool single = false, Action after = null)
        {
            Loger.Log("Client SaveGameNow single=" + single.ToString());
            SaveGame((content) =>
            {
                if (content.Length > 1024)
                {
                    Data.SaveFileData = content;
                    Data.SingleSave = single;
                    UpdateWorld(false);

                    Loger.Log("Client SaveGameNow OK");
                }
                if (after != null) after();
            });
        }

        public static void SaveGameNowInEvent(bool single = false)
        {
            Loger.Log($"Client {SessionClientController.My.Login} SaveGameNowInEvent single=" + single.ToString());

            var content = SaveGameCore();

            if (content.Length > 1024)
            {
                Data.SaveFileData = content;
                Data.SingleSave = single;
                UpdateWorld(false);

                // Записуємо файл лише для зручності гравця, щоб він у нього був
                File.WriteAllBytes(SaveFullName, content);
                Loger.Log($"Client {SessionClientController.My.Login} SaveGameNowInEvent OK");
            }
        }

        private static void BackgroundSaveGame()
        {
            if (Data.BackgroundSaveGameOff) return;

            var tick = (long)Find.TickManager.TicksGame;
            if (Data.LastSaveTick == tick)
            {
                Loger.Log($"Client {SessionClientController.My.Login} BackgroundSaveGame() Cancel in pause");
                return;
            }
            Loger.Log($"Client {SessionClientController.My.Login} BackgroundSaveGame()");
            Data.LastSaveTick = tick;

            SaveGame((content) =>
            {
                Data.SaveFileData = content;
                Data.SingleSave = false;
            });
        }

        private static void PingServer()
        {
            try
            {
                Command((connect) =>
                {
                    connect.ServicePing();
                    Data.CountReconnectBeforeUpdate = 0;
                });
            }
            catch
            {
            }
        }

        private static void UpdateGlobalTooltip()
        {
            try
            {
                GlobalControlsUtility_DoDate_Patch.Update = DateTime.UtcNow;
                GlobalControlsUtility_DoDate_Patch.OutText =
                    new List<string>() {
                        (SessionClient.IsRelogin || Data.LastServerConnectFail
                            ? (string)("(!) " + "OCity_Dialog_Connecting".TranslateCache() + " ")
                            : (SessionClient.Get?.IsLogined ?? false)
                            ? (string)($"✔ " + (int)Data.Ping.TotalMilliseconds + new TaggedString("ms")) : "X ")
                        + " " + SessionClientController.My.Login
                        };
                GlobalControlsUtility_DoDate_Patch.TooltipText = string.Format(
                    "OC_SessionCC_Info".Translate() + "." + "OC_SessionCC_Balance".Translate()
                    , Data.ServerName + " (" + (ModBaseData.GlobalData?.LastIP?.Value ?? "") + ")", My.Login);
                if (!SessionClient.IsRelogin && !Data.LastServerConnectFail && Data.CashlessBalance != 0)
                {
                    var mon = Data.CashlessBalance.ToString();
                    GlobalControlsUtility_DoDate_Patch.OutText.Add(mon + " ");
                    GlobalControlsUtility_DoDate_Patch.OutInLastLine = GameUtils.TextureCashlessBalance;
                    GlobalControlsUtility_DoDate_Patch.TooltipText += mon;
                }
                else
                {
                    GlobalControlsUtility_DoDate_Patch.OutInLastLine = null;
                    GlobalControlsUtility_DoDate_Patch.TooltipText += "-";
                }

                if ((DateTime.UtcNow - SessionClientController.My.LastSaveTime).TotalMinutes < 60)
                {
                    GlobalControlsUtility_DoDate_Patch.TooltipText += " " + Environment.NewLine
                        + "OC_SessionCC_MinSave".Translate() + " "
                        + (int)(DateTime.UtcNow - SessionClientController.My.LastSaveTime).TotalMinutes;
                }
            }
            catch (Exception ex)
            {
                GlobalControlsUtility_DoDate_Patch.Update = DateTime.MinValue;
                Loger.Log("Exception UpdateGlobalTooltip " + ex.ToString());
            }
            try
            {
                SetPauseWithRelogin();
            }
            catch { }
        }

        private static bool ReloginPauseActived = false;
        private static bool ReloginPauseShowDialog = false;
        private static void SetPauseWithRelogin()
        {
            if (SessionClient.IsRelogin
                || Data.LastServerConnectFail
                || Data.Ping.TotalMilliseconds == 0
                || Data.Ping.TotalMilliseconds > 9000)
            {
                // У разі проблем із підключенням
                if (!Find.TickManager.Paused)
                {
                    Find.TickManager.Pause();
                    if (!ReloginPauseActived)
                    {
                        // Перший раз при виявленні поточної гри ставимо паузу мовчки
                        ReloginPauseActived = true;
                    }
                    else
                    {
                        // Другий раз виводимо попередження
                        if (!ReloginPauseShowDialog)
                        {
                            ReloginPauseShowDialog = true;
                            var form = new Dialog_Input("OCity_SessionCC_Synchronization".TranslateCache(), "OCity_SessionCC_SynchronizationText".TranslateCache(), true);
                            form.PostCloseAction = () =>
                            {
                                ReloginPauseShowDialog = false;
                            };
                            Find.WindowStack.Add(form);
                        }
                    }
                }
            }
            else
            {
                ReloginPauseActived = false;
            }
        }

        private static volatile bool ChatIsUpdating = false;
        private static void UpdateChats()
        {
            Command((connect) =>
            {
                // Доки не обробили старий запит, новий не відправляємо
                if (ChatIsUpdating)
                {
                    return;
                }

                ChatIsUpdating = true;
                try
                {
                    var timeFrom = DateTime.UtcNow;
                    var test = connect.ServiceCheck();
                    Data.Ping = DateTime.UtcNow - timeFrom;

                    if (test != null)
                    {
                        Data.LastServerConnectFail = false;
                        Data.LastServerConnect = DateTime.UtcNow;

                        // Оновлюємо чат (оскільки інтервал 1000мс, 30 пропусків = раз на 30 секунд)
                        if (test.Value || Data.ChatCountSkipUpdate > 30)
                        {
                            var dc = connect.UpdateChat(Data.ChatsTime);
                            if (dc != null)
                            {
                                Data.ServetTimeDelta = dc.Time - DateTime.UtcNow;
                                Data.ChatsTime.Time = dc.Time;

                                if (Data.ApplyChats(dc) && !test.Value)
                                {
                                    Loger.Log("Client UpdateChats: ServiceCheck fail ");
                                }
                            }
                            else
                            {
                                Disconnected("Unknown error in UpdateChats");
                            }

                            Data.ChatCountSkipUpdate = 0;
                        }
                        else
                            Data.ChatCountSkipUpdate++;

                        // Оптимізація: не опитувати текстури двічі на секунду, а раз на 5 секунд
                        if (textureUpdateCounter++ % 5 == 0)
                        {
                            GeneralTexture.Get.Update(connect);
                        }
                    }
                    else
                    {
                        Data.LastServerConnectFail = true;
                        if (!Data.ServerConnected)
                        {
                            Thread.Sleep(5000); // Чекаємо, коли CheckReconnectTimer зупинить цей потік основного таймера
                        }
                    }

                    UpdateColonyScreen();
                }
                catch (Exception ex)
                {
                    Loger.Log(ex.ToString());
                }
                finally
                {
                    ChatIsUpdating = false;
                    UpdateGlobalTooltip();
                }
            });
        }

        private static Dictionary<long, long> UpdateColonyScreenLastTickBySettlementID;
        private static void UpdateColonyScreen()
        {
            if (!SessionClientController.Data.GeneralSettings.ColonyScreenEnable) return;

            // Оновлюємо лише між запитами оновлень
            if (Data.UpdateTimeLocalTime == DateTime.MinValue) return;
            var msUpdate = (int)((DateTime.UtcNow - Data.UpdateTimeLocalTime).TotalMilliseconds);
            if (msUpdate < 2000 || msUpdate > 3000) return;

            // Робимо скріншоти колонії вдень, враховуючи їхній ігровий часовий пояс
            var ticksGame = GenTicks.TicksAbs; // Особливі тіки, не загальні (long)Find.TickManager.TicksGame;
            var worldObjects = Find.WorldObjects.AllWorldObjects;

            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is Settlement settlement && settlement.Faction == Faction.OfPlayer)
                {
                    var vector = Find.WorldGrid.LongLatOf(settlement.Tile);
                    var settlementTick = ticksGame + GenDate.LocalTicksOffsetFromLongitude(vector.x);

                    long lastTick;
                    if (!UpdateColonyScreenLastTickBySettlementID.TryGetValue(settlement.ID, out lastTick)) lastTick = 0;
                    UpdateColonyScreenLastTickBySettlementID[settlement.ID] = settlementTick;

                    if (lastTick == 0) continue;

                    if (CalcUtils.OnMidday(lastTick, settlementTick) // Після полудня
                        || lastTick / 60000 < settlementTick / 60000 // Або вже минула доба без скріншота і зараз світлий час доби після полудня
                            && settlementTick % 60000 > 60000 / 2
                            && settlementTick % 60000 < 60000 * 3 / 4)
                    {
                        var dayByYear = (settlementTick / 60000) % 60;
                        if (SessionClientController.Data.GeneralSettings.ColonyScreenDelayDays > 1
                            && dayByYear % SessionClientController.Data.GeneralSettings.ColonyScreenDelayDays != 0) continue;

                        // Оптимізація: не блокувати головний потік очікуванням lock (UpdatingWorld).
                        // Якщо світ зараз оновлюється, пропускаємо кадр і повторюємо пізніше без 5-секундного фрізу.
                        if (Monitor.TryEnter(UpdatingWorld, 50))
                        {
                            try
                            {
                                Loger.Log($"UpdateColonyScreen Screen {settlement.Name} (localID={settlement.ID})");
                                var sc = new SnapshotColony();
                                sc.Background = true;
                                sc.HighQuality = SessionClientController.Data.GeneralSettings.ColonyScreenHighQuality;
                                sc.Exec(settlement);
                                Loger.Log($"UpdateColonyScreen Screen OK");
                            }
                            catch (Exception ex)
                            {
                                Loger.Log(ex.ToString());
                            }
                            finally
                            {
                                Monitor.Exit(UpdatingWorld);
                            }
                        }
                    }
                }
            }
        }

        public static string Connect(string addr)
        {
            TimersStop();

            int port = 0;
            if (addr.Contains(":")
                && int.TryParse(addr.Substring(addr.LastIndexOf(":") + 1), out port))
            {
                addr = addr.Substring(0, addr.LastIndexOf(":"));
            }

            var logMsg = "Connecting to server. Addr: " + addr + ". Port: " + (port == 0 ? SessionClient.DefaultPort : port).ToString();
            Loger.Log("Client " + logMsg);
            Log.Warning(logMsg);
            var connect = SessionClient.Get;
            if (!connect.Connect(addr, port))
            {
                logMsg = "Connection fail: " + connect.ErrorMessage?.ServerTranslate();
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                Find.WindowStack.Add(new Dialog_Input("OCity_SessionCC_ConnectionFailTitle".Translate(), connect.ErrorMessage?.ServerTranslate(), true));
                return connect.ErrorMessage?.ServerTranslate();
            }
            else
            {
                logMsg = "Connection OK";
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
            }

            return null;
        }

        private static string GetSaffix()
        {
            return "@@@"
                + "11" + FileChecker.GetCheckSum("y39¤`"
                    + Environment.UserName + "*" + Environment.MachineName
                    ).Replace("==", "").Substring(4, 19);
        }

        public static string Login(string addr, string login, string password, Func<bool, bool> LoginOK)
        {
            var msgError = Connect(addr);
            if (msgError != null) return msgError;

            ConnectAddr = addr;

            var logMsg = "Login: " + login;
            Loger.Log("Client " + logMsg);
            Log.Warning(logMsg);
            My = null;
            var pass = new CryptoProvider().GetHash(password);

            var connect = SessionClient.Get;
            if (!connect.Login(login, pass, GetSaffix()))
            {
                if (connect.ErrorMessage == "User not approve")
                {
                    Loger.Log("Client Login: User not approve");
                    LoginOK(true);
                    return "";
                }

                logMsg = "Login fail: " + connect.ErrorMessage?.ServerTranslate();
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                Find.WindowStack.Add(new Dialog_Input("OCity_SessionCC_LoginFailTitle".Translate(), connect.ErrorMessage?.ServerTranslate(), true));
                return connect.ErrorMessage?.ServerTranslate();
            }
            else
            {
                logMsg = "Login OK";
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                if (LoginOK(false))
                    InitConnectedIntro();
                else
                    return "";
            }

            return null;
        }

        public static string Registration(string addr, string login, string password, string email, string discord, Action LoginOK)
        {
            var msgError = Connect(addr);
            if (msgError != null) return msgError;

            ConnectAddr = addr;

            var logMsg = "Registration. Login: " + login;
            Loger.Log("Client " + logMsg);
            Log.Warning(logMsg);
            My = null;
            var pass = new CryptoProvider().GetHash(password);

            var connect = SessionClient.Get;
            if (!connect.Registration(login, pass, email + GetSaffix(), discord))
            {
                if (connect.ErrorMessage == "User not approve")
                {
                    Loger.Log("Client Registration: User not approve");
                    Find.WindowStack.Add(new Dialog_LoginForm(true));
                    return null;
                }

                logMsg = "Registration fail: " + connect.ErrorMessage?.ServerTranslate();
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                Find.WindowStack.Add(new Dialog_Input("OCity_SessionCC_RegFailTitle".Translate(), connect.ErrorMessage?.ServerTranslate(), true));
                return connect.ErrorMessage?.ServerTranslate();
            }
            else
            {
                MainMenuDrawer_DoMainMenuControls_Patch.DontDisconnectTime = DateTime.UtcNow;
                logMsg = "Registration OK";
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                LoginOK();
                InitConnectedIntro();
            }

            return null;
        }

        public static bool Reconnect()
        {
            if (string.IsNullOrEmpty(Data?.KeyReconnect))
            {
                Loger.Log("Client Reconnect fail: no KeyReconnect ");
                return false;
            }
            if (string.IsNullOrEmpty(My?.Login))
            {
                Loger.Log("Client Reconnect fail: no Login ");
                return false;
            }
            if (string.IsNullOrEmpty(ConnectAddr))
            {
                Loger.Log("Client Reconnect fail: no ConnectAddr ");
                return false;
            }

            var addr = ConnectAddr;
            int port = 0;
            if (addr.Contains(":")
                && int.TryParse(addr.Substring(addr.LastIndexOf(":") + 1), out port))
            {
                addr = addr.Substring(0, addr.LastIndexOf(":"));
            }
            var logMsg = "Reconnect to server. Addr: " + addr + ". Port: " + (port == 0 ? SessionClient.DefaultPort : port).ToString();
            Loger.Log("Client " + logMsg);
            Log.Warning(logMsg);
            var connect = new SessionClient();
            if (!connect.Connect(addr, port))
            {
                logMsg = "Reconnect net fail: " + connect.ErrorMessage?.ServerTranslate();
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                return false;
            }
            SessionClient.Recreate(connect);

            logMsg = "Reconnect login: " + My.Login;
            Loger.Log("Client " + logMsg);
            Log.Warning(logMsg);
            if (!connect.Reconnect(My.Login, Data.KeyReconnect, GetSaffix()))
            {
                logMsg = "Reconnect login fail: " + connect.ErrorMessage?.ServerTranslate();
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                return false;
            }
            else
            {
                logMsg = "Reconnect OK";
                Loger.Log("Client " + logMsg);
                Log.Warning(logMsg);
                return true;
            }
        }

        public static void Command(Action<SessionClient> netAct)
        {
            int time = 0;
            while (SessionClient.IsRelogin && time < 40000)
            {
                UpdateGlobalTooltip();
                Thread.Sleep(500);
                time += 500;
            }
            if (SessionClient.IsRelogin)
            {
                Loger.Log("Client Command: fail wait IsRelogin. Try to continue", Loger.LogLevel.WARNING);
            }
            var connect = SessionClient.Get;
            netAct(connect);
        }

        private static Thread SingleCommandThread = null;

        public static bool SingleCommandIsBusy => SingleCommandThread != null;

        public static bool SingleCommand(Action<SessionClient> netAct)
        {
            if (SingleCommandIsBusy) return false;
            SingleCommandThread = new Thread(() =>
            {
                try
                {
                    Command(netAct);
                }
                catch (Exception ext)
                {
                    Loger.Log("Exception SingleCommand: " + ext.ToString());
                }
                finally
                {
                    SingleCommandThread = null;
                }
            });
            SingleCommandThread.IsBackground = true;
            SingleCommandThread.Start();
            return true;
        }

        private static void TimersStop()
        {
            ReconnectSupportRuning = false;
            Loger.Log("Client TimersStop b");
            if (TimerReconnect != null) TimerReconnect.Stop();
            TimerReconnect = null;

            if (Timers != null) Timers.Stop();
            Timers = null;
            Loger.Log("Client TimersStop e");
        }

        public static Scenario GetScenarioByName(string scenarioName)
        {
            var list = GameUtils.AllowedScenarios();

            var scenario = list.FirstOrDefault(s => s.Value.name == scenarioName).Value;
            if (scenario == null)
            {
                Loger.Log("error: scenario not found: " + scenarioName);
            }
            return scenario;
        }

        public static string GetScenarioPlayer(Action<string> selected)
        {
            string scenario = null;
            var form = new Dialog_Scenario();
            form.PostCloseAction = () =>
            {
                if (form.ResultOK)
                {
                    scenario = form.InputScenario;
                    selected(scenario);
                }
            };
            Find.WindowStack.Add(form);
            return scenario;
        }

        public static Storyteller GetStoryteller(string difficultyName, StorytellerDef teller = null)
        {
            var list = DefDatabase<DifficultyDef>.AllDefs.ToList();
            var difficulty = list.FirstOrDefault(d => d.defName == difficultyName);
            if (difficulty == null)
            {
                difficulty = DifficultyDefOf.Easy;
            }

            if (teller == null) teller = StorytellerDefOf.Cassandra;
            return new Storyteller(teller, difficulty);
        }

        public static void InitConnectedIntro()
        {
            MainMenuDrawer_DoMainMenuControls_Patch.DontDisconnectTime = DateTime.UtcNow;
            if (SessionClientController.LoginInNewServerIP)
            {
                var form = new Dialog_Input("OCity_SessionCC_InitConnectedIntro_Title".Translate()
                    , "OCity_SessionCC_InitConnectedIntro_Text".Translate());
                form.PostCloseAction = () =>
                {
                    if (form.ResultOK)
                    {
                        InitConnected();
                    }
                    else
                    {
                        Disconnected("OCity_DialogInput_Cancele".Translate());
                    }
                };
                Find.WindowStack.Add(form);
            }
            else
            {
                InitConnected();
            }
        }

        public static void SetFullInfo(ModelInfo serverInfo)
        {
            My = serverInfo.My;
            Data.ServerName = serverInfo.ServerName;
            Data.DelaySaveGame = serverInfo.DelaySaveGame;
            if (Data.DelaySaveGame == 0) Data.DelaySaveGame = 15;
            if (Data.DelaySaveGame < 5) Data.DelaySaveGame = 5;
            Data.IsAdmin = serverInfo.IsAdmin;
            Data.DisableDevMode = !serverInfo.IsAdmin && serverInfo.DisableDevMode;
            Data.MinutesIntervalBetweenPVP = serverInfo.MinutesIntervalBetweenPVP;
            Data.TimeChangeEnablePVP = serverInfo.TimeChangeEnablePVP;
            Data.GeneralSettings = serverInfo.GeneralSettings;
            Data.ProtectingNovice = serverInfo.ProtectingNovice;
            MainHelper.OffAllLog = serverInfo.EnableFileLog;
        }

        public static void InitConnected()
        {
            try
            {
                Loger.Log("Client InitConnected()");
                Data = new ClientData();
                TimersStop();
                Timers = new WorkTimer();
                TimerReconnect = new WorkTimer();

                var connect = SessionClient.Get;
                ModelInfo serverInfo = connect.GetInfo(ServerInfoType.Full);
                ServerTimeDelta = serverInfo.ServerTime - DateTime.UtcNow;
                SetFullInfo(serverInfo);

                Loger.Log($"Server time difference {ServerTimeDelta:hh\\:mm\\:ss\\.ffff}. Server UTC time: {serverInfo.ServerTime:yyyy-MM-dd HH:mm:ss.ffff}");
                Loger.Log("Client ServerName=" + serverInfo.ServerName);
                Loger.Log("Client ServerVersion=" + serverInfo.VersionInfo + " (" + serverInfo.VersionNum + ")");
                Loger.Log("Client IsAdmin=" + serverInfo.IsAdmin
                    + " Seed=" + serverInfo.Seed
                    + " Scenario=" + serverInfo.ScenarioName
                    + " NeedCreateWorld=" + serverInfo.NeedCreateWorld
                    + " DelaySaveGame=" + Data.DelaySaveGame
                    + " DisableDevMode=" + Data.DisableDevMode);
                Loger.Log("Client Grants=" + serverInfo.My.Grants.ToString());

                if (SessionClientController.Data.DisableDevMode)
                {
                    if (Prefs.DevMode) Prefs.DevMode = false;
                    if (IdeoUIUtility.devEditMode) IdeoUIUtility.devEditMode = false;
                    DebugSettingsDefault.SetDefault();
                }

                if (!serverInfo.IsModsWhitelisted)
                {
                    InitConnectedPart2(serverInfo, null);
                    return;
                }

                if (!string.IsNullOrEmpty(Data.GeneralSettings.EntranceWarning))
                {
                    var entranceWarning = MainHelper.CultureFromGame.StartsWith("Russian")
                        ? Data.GeneralSettings.EntranceWarningRussian
                        : null;
                    if (string.IsNullOrEmpty(entranceWarning))
                    {
                        entranceWarning = Data.GeneralSettings.EntranceWarning;
                    }

                    var form = new Dialog_Input(serverInfo.ServerName + " (" + (ModBaseData.GlobalData?.LastIP?.Value ?? "") + ")", entranceWarning, false);
                    Find.WindowStack.Add(form);
                    form.PostCloseAction = () =>
                    {
                        if (!form.ResultOK)
                        {
                            Disconnected("OCity_SessionCC_MsgCanceledCreateW".Translate(), () => ModsConfig.RestartFromChangedMods());
                            return;
                        }

                        CheckFiles((resultCheckFiles) => InitConnectedPart2(serverInfo, resultCheckFiles));
                    };
                }
                else
                {
                    CheckFiles((resultCheckFiles) => InitConnectedPart2(serverInfo, resultCheckFiles));
                }
            }
            catch (Exception ext)
            {
                Loger.Log("Exception InitConnected: " + ext.ToString());
            }
        }

        private static void InitConnectedPart2(ModelInfo serverInfo, string resultCheckFiles)
        {
            try
            {
                if (resultCheckFiles != null)
                {
                    Disconnected(resultCheckFiles, () => ModsConfig.RestartFromChangedMods());
                    return;
                }

                if (MainHelper.VersionNum < serverInfo.VersionNum)
                {
                    Disconnected("OCity_SessionCC_Client_UpdateNeeded".Translate() + serverInfo.VersionInfo);
                    return;
                }

                if (serverInfo.IsAdmin && serverInfo.Seed == "")
                {
                    Loger.Log("Client InitConnected() IsAdmin");
                    var form = new Dialog_CreateWorld();
                    form.PostCloseAction = () =>
                    {
                        if (!form.ResultOK)
                        {
                            GetScenarioByName(form.InputScenario);
                            Disconnected("OCity_SessionCC_MsgCanceledCreateW".Translate());
                            return;
                        }

                        GameStarter.SetMapSize = int.Parse(form.InputMapSize);
                        GameStarter.SetPlanetCoverage = form.InputPlanetCoverage / 100f;
                        GameStarter.SetSeed = form.InputSeed;
                        GameStarter.SetDifficulty = form.InputDifficultyDefName;
                        GameStarter.SetScenario = GetScenarioByName(form.InputScenario);
                        GameStarter.SetScenarioName = form.InputScenarioKey;
                        ChoosedTeller = form.InputStorytellerDef;

                        GameStarter.AfterStart = CreatingServerWorld;
                        GameStarter.GameGeneration();
                    };

                    Find.WindowStack.Add(form);
                    return;
                }

                if (serverInfo.NeedCreateWorld)
                {
                    CreatePlayerWorld(serverInfo);
                    return;
                }

                LoadPlayerWorld(serverInfo);
            }
            catch (Exception ext)
            {
                Loger.Log("Exception InitConnectedPart2: " + ext.ToString());
            }
        }

        private static void LoadPlayerWorld(ModelInfo serverInfo)
        {
            Loger.Log("Client LoadPlayerWorld");

            var connect = SessionClient.Get;
            var worldData = connect.WorldLoad();

            Action loadAction = () =>
            {
                LongEventHandler.QueueLongEvent(delegate
                {
                    Current.Game = new Game { InitData = new GameInitData { gameToLoad = SaveName } };

                    Current.Game.storyteller = GetStoryteller(Data.GeneralSettings.Difficulty, GameUtils.GetStorytallerByName(Data.GeneralSettings.StorytellerDef));
                    Loger.Log($"storyteller: {Current.Game.storyteller.def.defName}      difficulty: {Current.Game.storyteller.difficultyDef.defName}");
                    GameLoades.AfterLoad = () =>
                    {
                        GameLoades.AfterLoad = null;

                        ScribeLoader_InitLoading_Patch.Enable = false;
                        ScribeLoader_InitLoading_Patch.LoadData = null;

                        InitGame();
                    };
                }, "Play", "LoadingLongEvent", false, null);
            };

            ScribeLoader_InitLoading_Patch.Enable = true;
            ScribeLoader_InitLoading_Patch.LoadData = worldData.SaveFileData;

            PreLoadUtility.CheckVersionAndLoad(SaveFullName, ScribeMetaHeaderUtility.ScribeHeaderMode.Map, loadAction);
        }

        private static void CreatePlayerWorld(ModelInfo serverInfo)
        {
            Loger.Log("Client InitConnected() ExistMap0");
            if (SessionClientController.Data.GeneralSettings.ScenarioAviable)
            {
                GetScenarioPlayer((sce) => CreatePlayerWorldPart1(serverInfo, sce));
            }
            else
            {
                CreatePlayerWorldPart1(serverInfo, serverInfo.ScenarioName);
            }
        }
        private static void CreatePlayerWorldPart1(ModelInfo serverInfo, string choosed_scenario)
        {
            GameStarter.SetMapSize = serverInfo.MapSize;
            GameStarter.SetPlanetCoverage = serverInfo.PlanetCoverage;
            GameStarter.SetSeed = serverInfo.Seed;
            if (choosed_scenario == null)
            {
                GameStarter.SetScenario = GetScenarioByName(serverInfo.ScenarioName);
            }
            else
            {
                GameStarter.SetScenario = GetScenarioByName(choosed_scenario);
                GameStarter.SetScenarioName = choosed_scenario;
            }
            GameStarter.SetDifficulty = serverInfo.Difficulty;
            GameStarter.AfterStart = CreatePlayerMap;
            ChoosedTeller = GameUtils.GetStorytallerByName(serverInfo.Storyteller);

            GameStarter.GameGeneration(false);

            Loger.Log($"Client InitConnected() ExistMap1 Scenario={choosed_scenario ?? serverInfo.ScenarioName}({GameStarter.SetScenario.name}/{GameStarter.SetScenario.fileName})" +
                $" Difficulty={GameStarter.SetDifficulty}" + $" Storyteller={serverInfo.Storyteller}");

            MainMenuDrawer_DoMainMenuControls_Patch.DontDisconnectTime = DateTime.UtcNow;

            Current.Game = new Game();
            Current.Game.InitData = new GameInitData();
            Current.Game.Scenario = GameStarter.SetScenario;
            Current.Game.Scenario.PreConfigure();
            Current.Game.storyteller = GetStoryteller(GameStarter.SetDifficulty, ChoosedTeller);

            Loger.Log("Client InitConnected() ExistMap2");
            Current.Game.World = WorldGenerator.GenerateWorld(
                GameStarter.SetPlanetCoverage,
                GameStarter.SetSeed,
                GameStarter.SetOverallRainfall,
                GameStarter.SetOverallTemperature,
                OverallPopulation.Little
                );

            Loger.Log("Client InitConnected() ExistMap3");
            UpdateWorldController.InitGame();
            UpdateWorld(true);

            Timers.Add(10000, PingServer);

            Loger.Log("Client InitConnected() ExistMap4");
            var form = GetFirstConfigPage();
            Find.WindowStack.Add(form);

            Loger.Log("Client InitConnected() ExistMap5");
            MemoryUtility.UnloadUnusedUnityAssets();

            Loger.Log("Client InitConnected() ExistMap6");
            Find.World.renderer.RegenerateAllLayersNow();

            Loger.Log("Client InitConnected() ExistMap7");
        }

        public static WorldObjectOnline GetWorldObjects(WorldObject obj)
        {
            var worldObject = new WorldObjectOnline();
            worldObject.Name = obj.LabelCap;
            worldObject.Tile = obj.Tile;
            worldObject.FactionGroup = obj?.Faction?.def?.LabelCap;
            return worldObject;
        }

        public static void CheckFiles(Action<string> done)
        {
            if (ClientFileCheckers == null || ClientFileCheckers.Any(x => x == null))
            {
                done("Error not files");
                return;
            }

            var form = new UpdateModsWindow()
            {
                doCloseX = false
            };
            Find.WindowStack.Add(form);
            form.HideOK = true;

            Task.Factory.StartNew(() =>
            {
                var fc = new ClientHashChecker(SessionClient.Get);
                fc.Report = new ClientHashCheckerResult();
                var approveModList = true;
                foreach (var clientFileChecker in ClientFileCheckers)
                {
                    var res = fc.GenerateRequestAndDoJob(clientFileChecker);
                    approveModList = approveModList && res;
                }

                UpdateModsWindow.CompletedAndClose = true;
                form.OnCloseed = () =>
                {
                    done(fc.Report.ReportComplete());
                };
            });
        }

        public static Page GetFirstConfigPage()
        {
            List<Page> list = new List<Page>();
            list.Add(new Page_SelectStartingSite());
            if (ModsConfig.IdeologyActive)
            {
                list.Add(new Page_ChooseIdeoPreset());
            }
            list.Add(new Page_ConfigureStartingPawns());
            Page page = PageUtility.StitchedPages(list);
            if (page != null)
            {
                Page page2 = page;
                while (page2.next != null)
                {
                    page2 = page2.next;
                }
                page2.nextAct = delegate
                {
                    PageUtility.InitGameStart();
                };
            }

            return page;
        }

        private static void CreatingServerWorld()
        {
            Loger.Log("Client CreatingServerWorld()");
            var toServ = new ModelCreateWorld();
            toServ.MapSize = GameStarter.SetMapSize;
            toServ.PlanetCoverage = GameStarter.SetPlanetCoverage;
            toServ.Seed = GameStarter.SetSeed;
            toServ.ScenarioName = GameStarter.SetScenarioName;
            toServ.Difficulty = GameStarter.SetDifficulty;
            toServ.Storyteller = ChoosedTeller.defName;

            var connect = SessionClient.Get;
            string msg;
            if (!connect.CreateWorld(toServ))
            {
                msg = "OCity_SessionCC_MsgCreateWorlErr".Translate()
                    + Environment.NewLine + connect.ErrorMessage?.ServerTranslate();
            }
            else
            {
                msg = "OCity_SessionCC_MsgCreateWorlGood".Translate();
            }

            Find.WindowStack.Add(new Dialog_Input("OCity_SessionCC_MsgCreatingServer".Translate(), msg, true)
            {
                PostCloseAction = () =>
                {
                    GenScene.GoToMainMenu();
                }
            });
        }

        private static void CreatePlayerMap()
        {
            Loger.Log("Client CreatePlayerMap()");
            GameStarter.AfterStart = null;
            SaveGame((content) =>
            {
                Data.SaveFileData = content;
                Data.SingleSave = true;
                InitGame();
            });
        }

        public static void Disconnected(string msg, Action actionOnDisctonnect = null)
        {
            if (actionOnDisctonnect == null)
            {
                actionOnDisctonnect = () => GenScene.GoToMainMenu();
            }

            Loger.Log("Client Disconected :( " + msg);
            GameExit.BeforeExit = null;
            TimersStop();
            SessionClient.Get.Disconnect();
            if (msg == null)
                actionOnDisctonnect();
            else
                Find.WindowStack.Add(new Dialog_Input("OCity_SessionCC_Disconnect".Translate(), msg, true)
                {
                    PostCloseAction = actionOnDisctonnect
                });
        }

        private static void UpdateFastTimer()
        {
            if (Current.Game == null) return;
            if (!SessionClient.Get.IsLogined) return;

            if (SessionClientController.Data.DisableDevMode)
            {
                if (Prefs.DevMode) Prefs.DevMode = false;
                if (IdeoUIUtility.devEditMode) IdeoUIUtility.devEditMode = false;
            }

            if (UpdateWorldSafelyRun != null)
            {
                Loger.Log("Client UpdateWorldSafelyRun() b");
                var fun = UpdateWorldSafelyRun;
                UpdateWorldSafelyRun = null;
                UpdateWorld(false);
                fun();
                Loger.Log("Client UpdateWorldSafelyRun() e");
            }
        }

        #region На час реконнекту запускаємо потік, який оновлюватиме статус в інтерфейсі, викликаючи UpdateGlobalTooltip();
        private static Thread ReconnectSupportThread = null;
        private static bool ReconnectSupportRuning = false;
        private static void ReconnectSupportInit()
        {
            if (ReconnectSupportThread == null)
            {
                ReconnectSupportThread = new Thread(() =>
                {
                    while (true)
                    {
                        Thread.Sleep(900);
                        if (!SessionClient.Get.IsLogined)
                        {
                            ReconnectSupportRuning = false;
                            ReconnectSupportThread = null;
                            return;
                        }
                        if (!ReconnectSupportRuning) continue;

                        UpdateGlobalTooltip();
                    }
                });
                ReconnectSupportThread.IsBackground = true;
                ReconnectSupportThread.Start();
            }
        }
        #endregion

        public static bool ReconnectWithTimers()
        {
            Timers.Pause = true;
            SessionClient.IsRelogin = true;
            ReconnectSupportInit();
            ReconnectSupportRuning = true;
            try
            {
                var repeat = 3;
                while (repeat-- > 0)
                {
                    Loger.Log("Client CheckReconnectTimer() " + (3 - repeat).ToString());
                    try
                    {
                        if (Reconnect())
                        {
                            Data.LastServerConnectFail = false;
                            Loger.Log($"Client CheckReconnectTimer() OK #{Data.CountReconnectBeforeUpdate}");
                            if (Data.ActionAfterReconnect != null)
                            {
                                var aar = Data.ActionAfterReconnect;
                                Data.ActionAfterReconnect = null;
                                ModBaseData.RunMainThread(aar);
                            }
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("Client CheckReconnectTimer() Exception:" + ex.ToString());
                    }
                    var sleep = 7000;
                    while (sleep > 0)
                    {
                        Data.Ping = DateTime.UtcNow - Data.LastServerConnect;
                        UpdateGlobalTooltip();
                        Thread.Sleep(500);
                        sleep -= 500;
                    }
                }
                return false;
            }
            finally
            {
                Timers.Pause = false;
                SessionClient.IsRelogin = false;
                ReconnectSupportRuning = false;
            }
        }

        public static void CheckReconnectTimer()
        {
            try
            {
                if ((DateTime.UtcNow - Data.LastServerConnect).TotalMilliseconds > 1000)
                {
                    Data.Ping = DateTime.UtcNow - Data.LastServerConnect;
                    UpdateGlobalTooltip();
                }

                var connect = SessionClient.Get;
                var needReconnect = false;
                if (connect.Client.CurrentRequestStart != DateTime.MinValue)
                {
                    var sec = (long)(DateTime.UtcNow - connect.Client.CurrentRequestStart).TotalSeconds;
                    var len = connect.Client.CurrentRequestLength;
                    if (len < 1024 * 512 && sec > 15
                        || len < 1024 * 1024 * 2 && sec > 30
                        || sec > 120)
                    {
                        needReconnect = true;
                        Loger.Log($"Client ReconnectWithTimers len={len} sec={sec} noPing={Data.LastServerConnectFail}");
                    }
                }
                if (!needReconnect && Data.LastServerConnectFail)
                {
                    needReconnect = true;
                    Loger.Log($"Client ReconnectWithTimers noPing");
                }
                if (!needReconnect && !Data.DontCheckTimerFail && !Timers.IsStop && Timers.LastLoop != DateTime.MinValue)
                {
                    var sec = (long)(DateTime.UtcNow - Timers.LastLoop).TotalSeconds;
                    if (sec > (Data.AddTimeCheckTimerFail ? 120 : 30))
                    {
                        needReconnect = true;
                        Loger.Log($"Client ReconnectWithTimers timerFail {sec}");
                        Timers.LastLoop = DateTime.UtcNow;
                    }
                }
                if (needReconnect)
                {
                    if (++Data.CountReconnectBeforeUpdate > 4 || !ReconnectWithTimers())
                    {
                        Loger.Log("Client CheckReconnectTimer Disconnected after try reconnect");
                        Disconnected("OCity_SessionCC_Disconnected".Translate()
                            , Data.CountReconnectBeforeUpdate > 4 ? () =>
                            {
                                Environment.Exit(0);
                            }
                        : (Action)null);
                    }
                }
            }
            catch (Exception e)
            {
                Loger.Log("Client CheckReconnectTimer exception: " + e.ToString(), Loger.LogLevel.ERROR);
                try
                {
                    Disconnected("OCity_SessionCC_Disconnected".Translate());
                }
                catch
                {
                    Environment.Exit(0);
                }
            }
        }

        public static void InitGame()
        {
            try
            {
                Loger.Log("Client InitGame()");

                ModBaseData.RunMainThreadSync(() => Loger.Log("Client InitGame MainThread check OK"), 60 * 5);
                Loger.Log("Client InitGame MainThread check end");

                GeneralTexture.Init();

                UpdateColonyScreenLastTickBySettlementID = new Dictionary<long, long>();
                textureUpdateCounter = 0;

                DebugTools.curTool = null;

                MainButtonWorker_OC.ShowOnStart();
                UpdateWorldController.ClearWorld();
                UpdateWorldController.InitGame();
                ChatController.Init(true);
                Data.UpdateTime = DateTime.MinValue;
                UpdateWorld(true);
                Data.LastServerConnect = DateTime.MinValue;

                Timers.Add(100, UpdateFastTimer);

                // Оптимізація: опитування чату кожні 1000 мс замість 500 мс
                Timers.Add(1000, UpdateChats);

                // Оновлення світу раз на 5 сек
                Timers.Add(5000, () => UpdateWorld(false));

                // Пінг раз на 10 сек для підтримки з'єднання
                Timers.Add(10000, PingServer);

                // Збереження гри
                Timers.Add(60000 * Data.DelaySaveGame, BackgroundSaveGame);

                // Таймер реконнекту: стежить за основним таймером і створює нове з'єднання
                TimerReconnect.Add(1000, CheckReconnectTimer);

                // Встановлюємо подію на вихід із гри
                GameExit.BeforeExit = () =>
                {
                    try
                    {
                        Loger.Log("Client BeforeExit ");
                        GameExit.BeforeExit = null;
                        TimersStop();
                        if (Current.Game == null) return;

                        if (!Data.BackgroundSaveGameOff)
                        {
                            Loger.Log($"Client {SessionClientController.My.Login} SaveGameBeforeExit ");
                            SaveGameNowInEvent();
                        }
                        SessionClient.Get.Disconnect();
                    }
                    catch (Exception e)
                    {
                        Loger.Log("Client BeforeExit Exception: " + e.ToString(), Loger.LogLevel.ERROR);
                        throw;
                    }
                };
            }
            catch (Exception e)
            {
                ExceptionUtil.ExceptionLog(e, "Client InitGame Error");
                Disconnected(null);
            }
        }
    }
}
