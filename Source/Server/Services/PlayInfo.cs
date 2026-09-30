using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Common;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Services
{
    /// <summary>
    /// Головна служба циклічної синхронізації стану світу, поселень та статистики між клієнтом і сервером.
    /// Викликається кожні кілька секунд під час активної гри.
    /// </summary>
    internal sealed class PlayInfo : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request11;

        public int ResponseTypePackage => (int)PackageType.Response12;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = playInfo((ModelPlayToServer)request.Packet, context);
            return result;
        }

        public ModelPlayToClient playInfo(ModelPlayToServer packet, ServiceContext context)
        {
            if (Repository.CheckIsBanIP(context.AddrIP))
            {
                context.Disconnect("New BanIP " + context.AddrIP);
                return null;
            }
            if (context.PossiblyIntruder)
            {
                context.Disconnect("Possibly intruder");
                return null;
            }

            lock (context.Player)
            {
                var data = Repository.GetData;
                var timeNow = DateTime.UtcNow;
                var toClient = new ModelPlayToClient
                {
                    UpdateTime = timeNow
                };

                // 1. Отримання публічної інформації про інших гравців (виправлено тип на Model.Player)
                if (packet.GetPlayersInfo != null && packet.GetPlayersInfo.Count > 0)
                {
                    var pSee = StaticHelper.PartyLoginSee(context.Player);
                    var playersInfo = new List<Player>(packet.GetPlayersInfo.Count);

                    for (int i = 0; i < packet.GetPlayersInfo.Count; i++)
                    {
                        var login = packet.GetPlayersInfo[i];
                        if (pSee.Contains(login))
                        {
                            var player = Repository.GetPlayerByLogin(login);
                            if (player?.Public != null)
                            {
                                playersInfo.Add(player.Public);
                            }
                        }
                    }
                    toClient.PlayersInfo = playersInfo;
                }

                // 2. Збереження ігрових даних гравця
                if (packet.SaveFileData != null && packet.SaveFileData.Length > 0)
                {
                    Repository.GetSaveData.SavePlayerData(context.Player.Public.Login, packet.SaveFileData, packet.SingleSave);
                    context.Player.Public.LastSaveTime = timeNow;

                    // Дії при збереженні (виконуються виключно тут)
                    context.Player.MailsConfirmationSave = new List<ModelMail>();
                    Repository.Get.ChangeData = true;
                }

                if (context.Player.GetKeyReconnect())
                {
                    toClient.KeyReconnect = context.Player.KeyReconnect1;
                }

                var pLogin = context.Player.Public.Login;
                var pWOs = packet.WObjects ?? new List<WorldObjectEntry>();
                var pDs = packet.WObjectsToDelete ?? new List<WorldObjectEntry>();

                var outWO = new List<WorldObjectEntry>();
                var outWOD = new List<WorldObjectEntry>();
                bool first = pWOs.Count == 0;

                lock (data)
                {
                    // 3. Обробка видалених об'єктів гравця
                    for (int i = 0; i < pDs.Count; i++)
                    {
                        if (pDs[i].LoginOwner != context.Player.Public.Login) continue;
                        var sid = pDs[i].PlaceServerId;

                        for (int j = 0; j < data.WorldObjects.Count; j++)
                        {
                            if (data.WorldObjects[j].PlaceServerId == sid)
                            {
                                var pD = data.WorldObjects[j];
                                pD.UpdateTime = timeNow;
                                data.WorldObjects.RemoveAt(j);
                                data.WorldObjectsDeleted.Add(pD);
                                break;
                            }
                        }
                    }

                    // 4. Розрахунок вартості безготівкових активів
                    float totalMarketValue = 0f;
                    for (int i = 0; i < pWOs.Count; i++)
                    {
                        if (pWOs[i].LoginOwner != context.Player.Public.Login) continue;
                        totalMarketValue += pWOs[i].MarketValue + pWOs[i].MarketValuePawn;
                    }

                    var cashlessBalance = context.Player.CashlessBalance;
                    context.Player.UpdateStorageBalance();
                    var storageBalance = context.Player.StorageBalance;

                    // 5. Обробка та оновлення наявних або нових об'єктів світу
                    for (int i = 0; i < pWOs.Count; i++)
                    {
                        var curWo = pWOs[i];
                        if (curWo.LoginOwner != context.Player.Public.Login) continue;

                        if (totalMarketValue > 0)
                        {
                            float weightRatio = (curWo.MarketValue + curWo.MarketValuePawn) / totalMarketValue;
                            curWo.MarketValueBalance = cashlessBalance * weightRatio;
                            curWo.MarketValueStorage = storageBalance * weightRatio;
                        }

                        var sid = curWo.PlaceServerId;
                        if (sid == 0)
                        {
                            // Новий об'єкт: додаємо в базу сервера
                            curWo.UpdateTime = timeNow;
                            curWo.PlaceServerId = data.GetWorldObjectEntryId();
                            data.WorldObjects.Add(curWo);
                            outWO.Add(curWo);
                            continue;
                        }

                        WorldObjectEntry targetWo = null;
                        for (int j = 0; j < data.WorldObjects.Count; j++)
                        {
                            if (data.WorldObjects[j].PlaceServerId == sid)
                            {
                                targetWo = data.WorldObjects[j];
                                break;
                            }
                        }

                        if (targetWo != null)
                        {
                            if (targetWo.Name != curWo.Name) { targetWo.UpdateTime = timeNow; targetWo.Name = curWo.Name; }
                            if (targetWo.FreeWeight != curWo.FreeWeight) { targetWo.UpdateTime = timeNow; targetWo.FreeWeight = curWo.FreeWeight; }
                            if (targetWo.MarketValue != curWo.MarketValue) { targetWo.UpdateTime = timeNow; targetWo.MarketValue = curWo.MarketValue; }
                            if (targetWo.MarketValuePawn != curWo.MarketValuePawn) { targetWo.UpdateTime = timeNow; targetWo.MarketValuePawn = curWo.MarketValuePawn; }
                            if (targetWo.MarketValueBalance != curWo.MarketValueBalance) { targetWo.UpdateTime = timeNow; targetWo.MarketValueBalance = curWo.MarketValueBalance; }
                            if (targetWo.MarketValueStorage != curWo.MarketValueStorage) { targetWo.UpdateTime = timeNow; targetWo.MarketValueStorage = curWo.MarketValueStorage; }
                            if (targetWo.Tile != curWo.Tile) { targetWo.UpdateTime = timeNow; targetWo.Tile = curWo.Tile; }
                        }
                        else
                        {
                            Loger.Log($"PlayInfo find error add WO: {curWo.Name} sid={sid}", Loger.LogLevel.WARNING);
                        }
                    }

                    // 6. Передаємо всі змінені об'єкти (у перший запуск виключаємо свої)
                    for (int i = 0; i < data.WorldObjects.Count; i++)
                    {
                        var wo = data.WorldObjects[i];
                        if (wo.UpdateTime < packet.UpdateTime) continue;
                        if (!first && wo.LoginOwner == pLogin) continue;
                        outWO.Add(wo);
                    }

                    // 7. Передаємо видалені об'єкти інших гравців із коректним очищенням за 2 хвилини (120 с)
                    if (packet.UpdateTime > DateTime.MinValue && data.WorldObjectsDeleted != null)
                    {
                        for (int i = 0; i < data.WorldObjectsDeleted.Count; i++)
                        {
                            var delWo = data.WorldObjectsDeleted[i];
                            if (delWo.UpdateTime < packet.UpdateTime)
                            {
                                // Виправлено: видаляємо старі записи після 120 секунд
                                if ((timeNow - delWo.UpdateTime).TotalSeconds > 120)
                                {
                                    data.WorldObjectsDeleted.RemoveAt(i--);
                                }
                                continue;
                            }
                            if (delWo.LoginOwner == pLogin) continue;
                            outWOD.Add(delWo);
                        }
                    }

                    // 8. Передача держав (виправлено доступ до HashSet через foreach)
                    if (data.StateUpdateTime > packet.UpdateTime)
                    {
                        var statesList = new List<StateInfo>(data.States.Count);
                        for (int s = 0; s < data.States.Count; s++)
                        {
                            var st = data.States[s];
                            var resState = new StateInfo(st);
                            var players = data.GetStatePlayers(st.Name);

                            foreach (var pl in players)
                            {
                                if (resState.Head == null && Repository.GetStatePosition(pl.Public)?.RightHead == true)
                                {
                                    resState.Head = pl.Public.Login;
                                }
                                resState.Players.Add(pl.Public.Login);
                            }
                            statesList.Add(resState);
                        }
                        toClient.States = statesList;
                    }

                    #region Об'єкти та фракції інших гравців онлайн
                    if (ServerManager.ServerSettings.GeneralSettings.EquableWorldObjects)
                    {
                        try
                        {
                            if (packet.WObjectOnlineToDelete != null && packet.WObjectOnlineToDelete.Count > 0)
                            {
                                data.WorldObjectOnlineList.RemoveAll(d => packet.WObjectOnlineToDelete.Any(pkt => ValidateWorldObject(pkt, d)));
                            }
                            if (packet.WObjectOnlineToAdd != null && packet.WObjectOnlineToAdd.Count > 0)
                            {
                                data.WorldObjectOnlineList.AddRange(packet.WObjectOnlineToAdd);
                            }
                            if (packet.WObjectOnlineList != null && packet.WObjectOnlineList.Count > 0)
                            {
                                if (data.WorldObjectOnlineList.Count == 0)
                                {
                                    data.WorldObjectOnlineList = packet.WObjectOnlineList;
                                }
                                else
                                {
                                    toClient.WObjectOnlineToDelete = packet.WObjectOnlineList.Where(pkt => !data.WorldObjectOnlineList.Any(d => ValidateWorldObject(pkt, d))).ToList();
                                    toClient.WObjectOnlineToAdd = data.WorldObjectOnlineList.Where(d => !packet.WObjectOnlineList.Any(pkt => ValidateWorldObject(pkt, d))).ToList();
                                }
                            }
                            toClient.WObjectOnlineList = data.WorldObjectOnlineList;
                        }
                        catch
                        {
                            Loger.Log("ERROR PLAYINFO World Object Online", Loger.LogLevel.ERROR);
                        }

                        try
                        {
                            if (packet.FactionOnlineToDelete != null && packet.FactionOnlineToDelete.Count > 0)
                            {
                                data.FactionOnlineList.RemoveAll(d => packet.FactionOnlineToDelete.Any(pkt => ValidateFaction(pkt, d)));
                            }
                            if (packet.FactionOnlineToAdd != null && packet.FactionOnlineToAdd.Count > 0)
                            {
                                data.FactionOnlineList.AddRange(packet.FactionOnlineToAdd);
                            }
                            if (packet.FactionOnlineList != null && packet.FactionOnlineList.Count > 0)
                            {
                                if (data.FactionOnlineList.Count == 0)
                                {
                                    data.FactionOnlineList = packet.FactionOnlineList;
                                }
                                else
                                {
                                    toClient.FactionOnlineToDelete = packet.FactionOnlineList.Where(pkt => !data.FactionOnlineList.Any(d => ValidateFaction(pkt, d))).ToList();
                                    toClient.FactionOnlineToAdd = data.FactionOnlineList.Where(d => !packet.FactionOnlineList.Any(pkt => ValidateFaction(pkt, d))).ToList();
                                }
                            }
                            toClient.FactionOnlineList = data.FactionOnlineList;
                        }
                        catch
                        {
                            Loger.Log("ERROR PLAYINFO Faction Online", Loger.LogLevel.ERROR);
                        }
                    }
                    #endregion

                    // 9. Торгові точки біржі
                    var outWTO = new List<TradeWorldObjectEntry>();
                    var outWTOD = new List<TradeWorldObjectEntry>();

                    for (int i = 0; i < context.Player.TradeThingStorages.Count; i++)
                    {
                        if (context.Player.TradeThingStorages[i].UpdateTime < packet.UpdateTime) continue;
                        outWTO.Add(context.Player.TradeThingStorages[i]);
                    }
                    for (int i = 0; i < data.OrderOperator.TradeWorldObjects.Count; i++)
                    {
                        if (data.OrderOperator.TradeWorldObjects[i].UpdateTime < packet.UpdateTime) continue;
                        outWTO.Add(data.OrderOperator.TradeWorldObjects[i]);
                    }

                    if (packet.UpdateTime > DateTime.MinValue)
                    {
                        for (int i = 0; i < data.OrderOperator.TradeWorldObjectsDeleted.Count; i++)
                        {
                            var delTrade = data.OrderOperator.TradeWorldObjectsDeleted[i];
                            if (delTrade.UpdateTime < packet.UpdateTime)
                            {
                                if ((timeNow - delTrade.UpdateTime).TotalSeconds > 120)
                                {
                                    data.OrderOperator.TradeWorldObjectsDeleted.RemoveAt(i--);
                                }
                                continue;
                            }
                            if (delTrade.LoginOwner == pLogin) continue;
                            outWTOD.Add(delTrade);
                        }
                    }

                    toClient.WObjects = outWO;
                    toClient.WObjectsToDelete = outWOD;
                    toClient.WTObjects = outWTO;
                    toClient.WTObjectsToDelete = outWTOD;

                    context.Player.GameProgressLast = context.Player.GameProgress;
                    context.Player.GameProgress = packet.GameProgress;

                    context.Player.WLastUpdateTime = timeNow;
                    context.Player.WLastTick = packet.LastTick;

                    // 10. Оновлення економічної статистики
                    var costAll = context.Player.CostWorldObjects();
                    if (context.Player.StartMarketValuePawn == 0)
                    {
                        context.Player.StartMarketValue = costAll.MarketValue;
                        context.Player.StartMarketValuePawn = costAll.MarketValuePawn;

                        context.Player.DeltaMarketValue = 0;
                        context.Player.DeltaMarketValuePawn = 0;
                        context.Player.DeltaMarketValueBalance = 0;
                        context.Player.DeltaMarketValueStorage = 0;
                    }
                    else if (context.Player.LastUpdateIsGood && (costAll.MarketValue > 0 || costAll.MarketValuePawn > 0))
                    {
                        context.Player.DeltaMarketValue = costAll.MarketValue - context.Player.LastMarketValue;
                        context.Player.DeltaMarketValuePawn = costAll.MarketValuePawn - context.Player.LastMarketValuePawn;
                        context.Player.DeltaMarketValueBalance = costAll.MarketValueBalance - context.Player.LastMarketValueBalance;
                        context.Player.DeltaMarketValueStorage = costAll.MarketValueStorage - context.Player.LastMarketValueStorage;

                        context.Player.SumDeltaGameMarketValue += context.Player.DeltaMarketValue;
                        context.Player.SumDeltaGameMarketValuePawn += context.Player.DeltaMarketValuePawn;
                        context.Player.SumDeltaGameMarketValueBalance += context.Player.DeltaMarketValueBalance;
                        context.Player.SumDeltaGameMarketValueStorage += context.Player.DeltaMarketValueStorage;

                        context.Player.SumDeltaRealMarketValue += context.Player.DeltaMarketValue;
                        context.Player.SumDeltaRealMarketValuePawn += context.Player.DeltaMarketValuePawn;
                        context.Player.SumDeltaRealMarketValueBalance += context.Player.DeltaMarketValueBalance;
                        context.Player.SumDeltaRealMarketValueStorage += context.Player.DeltaMarketValueStorage;

                        if (packet.LastTick - context.Player.StatLastTick > 15 * 60000)
                        {
                            if (context.Player.StatMaxDeltaGameMarketValue < context.Player.SumDeltaGameMarketValue)
                                context.Player.StatMaxDeltaGameMarketValue = context.Player.SumDeltaGameMarketValue;
                            if (context.Player.StatMaxDeltaGameMarketValuePawn < context.Player.SumDeltaGameMarketValuePawn)
                                context.Player.StatMaxDeltaGameMarketValuePawn = context.Player.SumDeltaGameMarketValuePawn;
                            if (context.Player.StatMaxDeltaGameMarketValueBalance < context.Player.SumDeltaGameMarketValueBalance)
                                context.Player.StatMaxDeltaGameMarketValueBalance = context.Player.SumDeltaGameMarketValueBalance;
                            if (context.Player.StatMaxDeltaGameMarketValueStorage < context.Player.SumDeltaGameMarketValueStorage)
                                context.Player.StatMaxDeltaGameMarketValueStorage = context.Player.SumDeltaGameMarketValueStorage;

                            float totalDelta = context.Player.SumDeltaGameMarketValue + context.Player.SumDeltaGameMarketValueBalance + context.Player.SumDeltaGameMarketValuePawn + context.Player.SumDeltaGameMarketValueStorage;
                            if (context.Player.StatMaxDeltaGameMarketValueTotal < totalDelta)
                                context.Player.StatMaxDeltaGameMarketValueTotal = totalDelta;

                            context.Player.SumDeltaGameMarketValue = 0;
                            context.Player.SumDeltaGameMarketValuePawn = 0;
                            context.Player.SumDeltaGameMarketValueBalance = 0;
                            context.Player.SumDeltaGameMarketValueStorage = 0;
                            context.Player.StatLastTick = packet.LastTick;
                        }

                        if (context.Player.SumDeltaRealSecond > 60 * 60)
                        {
                            if (context.Player.StatMaxDeltaRealMarketValue < context.Player.SumDeltaRealMarketValue)
                                context.Player.StatMaxDeltaRealMarketValue = context.Player.SumDeltaRealMarketValue;
                            if (context.Player.StatMaxDeltaRealMarketValuePawn < context.Player.SumDeltaRealMarketValuePawn)
                                context.Player.StatMaxDeltaRealMarketValuePawn = context.Player.SumDeltaRealMarketValuePawn;
                            if (context.Player.StatMaxDeltaRealMarketValueBalance < context.Player.SumDeltaRealMarketValueBalance)
                                context.Player.StatMaxDeltaRealMarketValueBalance = context.Player.SumDeltaRealMarketValueBalance;
                            if (context.Player.StatMaxDeltaRealMarketValueStorage < context.Player.SumDeltaRealMarketValueStorage)
                                context.Player.StatMaxDeltaRealMarketValueStorage = context.Player.SumDeltaRealMarketValueStorage;

                            float totalRealDelta = context.Player.SumDeltaRealMarketValue + context.Player.SumDeltaRealMarketValueBalance + context.Player.SumDeltaRealMarketValuePawn + context.Player.SumDeltaRealMarketValueStorage;
                            if (context.Player.StatMaxDeltaRealMarketValueTotal < totalRealDelta)
                                context.Player.StatMaxDeltaRealMarketValueTotal = totalRealDelta;

                            if (context.Player.StatMaxDeltaRealTicks < context.Player.SumDeltaRealTicks)
                                context.Player.StatMaxDeltaRealTicks = context.Player.SumDeltaRealTicks;

                            context.Player.SumDeltaRealMarketValue = 0;
                            context.Player.SumDeltaRealMarketValuePawn = 0;
                            context.Player.SumDeltaRealMarketValueBalance = 0;
                            context.Player.SumDeltaRealMarketValueStorage = 0;
                            context.Player.SumDeltaRealTicks = 0;
                            context.Player.SumDeltaRealSecond = 0;
                        }
                    }

                    context.Player.LastUpdateIsGood = costAll.MarketValue > 0 || costAll.MarketValuePawn > 0;
                    if (context.Player.LastUpdateIsGood)
                    {
                        context.Player.LastMarketValue = costAll.MarketValue;
                        context.Player.LastMarketValuePawn = costAll.MarketValuePawn;
                        context.Player.LastMarketValueBalance = costAll.MarketValueBalance;
                        context.Player.LastMarketValueStorage = costAll.MarketValueStorage;
                    }

                    var dt = packet.LastTick - context.Player.Public.LastTick;
                    context.Player.SumDeltaRealTicks += dt;
                    if (dt > 0)
                    {
                        var ds = (long)(timeNow - context.Player.LastUpdateTime).TotalSeconds;
                        context.Player.SumDeltaRealSecond += ds;
                        context.Player.TotalRealSecond += ds;
                    }

                    context.Player.WLastUpdateTime = context.Player.LastUpdateTime;
                    context.Player.WLastTick = context.Player.Public.LastTick;
                    context.Player.LastUpdateTime = timeNow;
                    context.Player.Public.LastTick = packet.LastTick;
                    context.Player.Public.ExistsEnemyPawns = context.Player.GameProgressLast?.ExistsEnemyPawns == true;

                    if (CalcUtils.OnMidday(context.Player.WLastTick, context.Player.Public.LastTick))
                    {
                        data.OrderOperator.DayPassed(context.Player);
                        context.Player.MarketValueHistoryAdd(context.Player.LastMarketValue);
                    }
                }

                // 11. Оновлення стану відкладених дій
                if (context.Player.FunctionMails.Count > 0)
                {
                    for (int i = 0; i < context.Player.FunctionMails.Count; i++)
                    {
                        bool needRemove = context.Player.FunctionMails[i].Run(context);
                        if (needRemove) context.Player.FunctionMails.RemoveAt(i--);
                    }
                }

                // 12. Прикріплення листів
                var mails = context.Player.Mails;
                ModelMail cancelMail = null;
                for (int i = 0; i < mails.Count; i++)
                {
                    if (mails[i] is ModelMailAttackCancel)
                    {
                        cancelMail = mails[i];
                        break;
                    }
                }

                if (cancelMail == null)
                {
                    toClient.Mails = mails;
                    for (int i = 0; i < mails.Count; i++)
                    {
                        if (mails[i].NeedSaveGame)
                        {
                            context.Player.MailsConfirmationSave.Add(mails[i]);
                        }
                    }
                    context.Player.Mails = new List<ModelMail>();
                }
                else
                {
                    toClient.Mails = new List<ModelMail> { cancelMail };
                    context.Player.Mails.Remove(cancelMail);
                }

                toClient.NeedSaveAndExit = !context.Player.IsAdmin && data.EverybodyLogoff;

                toClient.AreAttacking = context.Player.AttackData != null
                    && context.Player.AttackData.Host == context.Player
                    && context.Player.AttackData.State == 1;

                if (context.Player.LastUpdateWithMail = (toClient.Mails.Count > 0))
                {
                    for (int i = 0; i < toClient.Mails.Count; i++)
                    {
                        var mail = toClient.Mails[i];
                        Loger.Log($"DownloadMail {mail.GetType().Name} {mail.From?.Login ?? "-"}->{mail.To?.Login ?? "-"} {mail.ContentString()}");
                    }
                }

                toClient.CashlessBalance = context.Player.CashlessBalance;
                toClient.StorageBalance = context.Player.StorageBalance;

                return toClient;
            }
        }

        private static bool ValidateWorldObject(WorldObjectOnline pkt, WorldObjectOnline data)
        {
            return pkt.Name == data.Name && pkt.Tile == data.Tile;
        }

        private static bool ValidateFaction(FactionOnline pkt, FactionOnline data)
        {
            return pkt.DefName == data.DefName && pkt.LabelCap == data.LabelCap && pkt.loadID == data.loadID;
        }
    }
}