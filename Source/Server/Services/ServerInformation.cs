using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using ServerCore.Model;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class ServerInformation : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request5UserInfo;

        public int ResponseTypePackage => (int)PackageType.Response6UserInfo;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = GetInfo((ModelInt)request.Packet, context);
            return result;
        }

        public ModelInfo GetInfo(ModelInt packet, ServiceContext context)
        {
            if (packet == null || context?.Player == null) return new ModelInfo();

            lock (context.Player)
            {
                switch (packet.Value)
                {
                    case (long)ServerInfoType.Full:
                    case (long)ServerInfoType.FullWithDescription:
                        {
                            return GetModelInfo(context.Player);
                        }

                    case (long)ServerInfoType.SendSave:
                        {
                            if (context.PossiblyIntruder)
                            {
                                context.Disconnect("Possibly intruder");
                                return null;
                            }

                            var result = new ModelInfo();

                            // Передача файлу збереження для завантаження гри WorldLoad()
                            if (ServerManager.ServerSettings.IsModsWhitelisted)
                            {
                                if (!context.Player.ApproveLoadWorldReason)
                                {
                                    context.Player.ExitReason = DisconnectReason.FilesMods;
                                    Loger.Log($"Login : {context.Player.Public?.Login} not all files checked, {context.Player.ApproveLoadWorldReason} Disconnect", Loger.LogLevel.WARNING);
                                    result.SaveFileData = null;
                                    return result;
                                }
                            }

                            var playerLogin = context.Player.Public?.Login;
                            if (string.IsNullOrEmpty(playerLogin)) return result;

                            result.SaveFileData = Repository.GetSaveData.LoadPlayerData(playerLogin, 1);

                            if (result.SaveFileData != null)
                            {
                                var confirmMails = context.Player.MailsConfirmationSave;
                                if (confirmMails != null && confirmMails.Count > 0)
                                {
                                    for (int i = 0; i < confirmMails.Count; i++)
                                    {
                                        if (confirmMails[i] != null) confirmMails[i].NeedSaveGame = false;
                                    }

                                    Loger.Log($"MailsConfirmationSave add {confirmMails.Count} (mails={context.Player.Mails.Count})");

                                    // Гравець не зберігся після отримання обов'язкового листа — відправляємо повторно без дублювання
                                    if (context.Player.Mails.Count == 0)
                                    {
                                        context.Player.Mails.AddRange(confirmMails);
                                    }
                                    else
                                    {
                                        // ОПТИМІЗАЦІЯ І ВИПРАВЛЕННЯ БАГУ: перевірка на дублікат за рівністю хешів (==), а не через Any(!=)
                                        for (int i = 0; i < confirmMails.Count; i++)
                                        {
                                            var mcs = confirmMails[i];
                                            if (mcs == null) continue;
                                            var hash = mcs.GetHashBase();

                                            bool alreadyExists = false;
                                            for (int j = 0; j < context.Player.Mails.Count; j++)
                                            {
                                                if (context.Player.Mails[j]?.GetHashBase() == hash)
                                                {
                                                    alreadyExists = true;
                                                    break;
                                                }
                                            }

                                            if (!alreadyExists)
                                            {
                                                context.Player.Mails.Add(mcs);
                                            }
                                        }
                                    }

                                    Loger.Log($"MailsConfirmationSave (mails={context.Player.Mails.Count})");
                                }
                            }

                            Loger.Log($"Load World for {playerLogin}. (mails={context.Player.Mails.Count}, fMails={context.Player.FunctionMails.Count})");
                            return result;
                        }

                    case (long)ServerInfoType.Short:
                    default:
                        {
                            return new ModelInfo();
                        }
                }
            }
        }

        private ModelInfo GetModelInfo(PlayerServer player)
        {
            var data = Repository.GetData;
            var playerLogin = player.Public?.Login;
            var needCreateWorld = string.IsNullOrEmpty(playerLogin) || Repository.GetSaveData.GetListPlayerDatas(playerLogin).Count == 0;

            if (needCreateWorld)
            {
                BeforeBeginSettlement(player);
            }

            var result = new ModelInfo
            {
                My = player.Public,
                IsAdmin = player.IsAdmin,
                VersionInfo = MainHelper.VersionInfo,
                VersionNum = MainHelper.VersionNum,
                Seed = data?.WorldSeed ?? "",
                ScenarioName = data?.WorldScenarioName,
                Storyteller = data?.WorldStoryteller,
                MapSize = data?.WorldMapSize ?? 0,
                PlanetCoverage = data?.WorldPlanetCoverage ?? 0f,
                Difficulty = data?.WorldDifficulty,
                NeedCreateWorld = needCreateWorld,
                ServerTime = DateTime.UtcNow,
                IsModsWhitelisted = ServerManager.ServerSettings.IsModsWhitelisted,
                ServerName = ServerManager.ServerSettings.ServerName,
                DelaySaveGame = player.SettingDelaySaveGame,
                DisableDevMode = ServerManager.ServerSettings.DisableDevMode,
                MinutesIntervalBetweenPVP = ServerManager.ServerSettings.MinutesIntervalBetweenPVP,
                EnableFileLog = player.SettingEnableFileLog,
                TimeChangeEnablePVP = player.TimeChangeEnablePVP,
                GeneralSettings = ServerManager.ServerSettings.GeneralSettings,
                ProtectingNovice = ServerManager.ServerSettings.ProtectingNovice,
            };

            return result;
        }

        private void BeforeBeginSettlement(PlayerServer player)
        {
            player.GameProgress = new PlayerGameProgress { Pawns = new List<PawnStat>(0) };
            player.AttacksWonCount = 0;
            player.AttacksInitiatorCount = 0;

            player.StartMarketValue = 0;
            player.StartMarketValuePawn = 0;
            player.TotalRealSecond = 0;
        }
    }
}