using GameClasses;
using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses.Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using Transfer;
using UnityEngine;
using Verse;
using Random = System.Random;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер синхронізації об'єктів планети та карт гравців.
    /// Відповідає за збір стану колонії в головному потоці, розрахунок багатства,
    /// формування вихідних даних на сервер та відображення караванів/баз інших гравців.
    /// </summary>
    static class UpdateWorldController
    {
        private static Dictionary<long, int> ConverterServerId { get; set; }
        public static Dictionary<int, WorldObjectEntry> WorldObjectEntrys { get; private set; }
        private static List<WorldObjectEntry> ToDelete { get; set; }
        public static HashSet<TradeOrdersOnline> WorldObject_TradeOrdersOnline { get; set; }

        private static List<WorldObjectEntry> LastSendMyWorldObjects { get; set; }
        private static List<WorldObjectOnline> LastWorldObjectOnline { get; set; }
        private static List<FactionOnline> LastFactionOnline { get; set; }

        private static Dictionary<int, WorldObjectBaseOnline> LastCatchAllWorldObjectsByID;

        public static bool ExistsEnemyPawns => gameProgress?.ExistsEnemyPawns == true;

        #region Збір даних у головному потоці гри

        public static List<WorldObject> allWorldObjects;
        public static List<WorldObjectEntry> WObjects;
        public static PlayerGameProgress gameProgress;

        private class CacheMap
        {
            public List<Pawn> Colonists;
            public bool ExistsEnemyPawns;
        }

        /// <summary>
        /// Збирає стан власних караванів і поселень гравця безпосередньо з пам'яті гри.
        /// Виконується синхронно в головному потоці Unity.
        /// </summary>
        public static void PrepareInMainThread()
        {
            try
            {
                gameProgress = new PlayerGameProgress() { Pawns = new List<PawnStat>() };

                allWorldObjects = GameUtils.GetAllWorldObjects();
                var cacheColonists = new Dictionary<Map, CacheMap>();
                var tmpMap = new Dictionary<WorldObjectEntry, Map>();
                WObjects = new List<WorldObjectEntry>(allWorldObjects.Count);

                // Однопрохідний збір власних об'єктів без зайвих проміжних LINQ-масивів
                for (int i = 0; i < allWorldObjects.Count; i++)
                {
                    var o = allWorldObjects[i];
                    if ((o.Faction?.IsPlayer ?? false) && (o is Settlement || o is Caravan))
                    {
                        var entry = GetWorldObjectEntry(o, gameProgress, cacheColonists);
                        if (o is MapParent mp && mp.Map != null)
                        {
                            tmpMap.Add(entry, mp.Map);
                        }
                        WObjects.Add(entry);
                    }
                }

                // Розподіл безготівкового балансу пропорційно сумарній вартості поселень
                float totalMarketValue = 0f;
                for (int i = 0; i < WObjects.Count; i++)
                {
                    totalMarketValue += WObjects[i].MarketValue + WObjects[i].MarketValuePawn;
                }

                if (totalMarketValue > 0)
                {
                    var cashlessBalance = Math.Abs(SessionClientController.Data.CashlessBalance);
                    var storageBalance = Math.Abs(SessionClientController.Data.StorageBalance);
                    for (int i = 0; i < WObjects.Count; i++)
                    {
                        var wo = WObjects[i];
                        var val = wo.MarketValue + wo.MarketValuePawn;
                        wo.MarketValueBalance = cashlessBalance * val / totalMarketValue;
                        wo.MarketValueStorage = storageBalance * val / totalMarketValue;
                    }
                }
                else
                {
                    for (int i = 0; i < WObjects.Count; i++)
                    {
                        WObjects[i].MarketValueBalance = 0;
                        WObjects[i].MarketValueStorage = 0;
                    }
                }

                // Встановлення скоригованої вартості карти для розрахунку сили рейдів
                var patchMap = new Dictionary<Map, float>();
                var wealthFactor = (float)SessionClientController.Data.GeneralSettings.ExchengePrecentWealthForIncident / 1000f;
                for (int i = 0; i < WObjects.Count; i++)
                {
                    var wo = WObjects[i];
                    if (wo.Type == WorldObjectEntryType.Base && tmpMap.TryGetValue(wo, out var map) && map != null)
                    {
                        patchMap[map] = (wo.MarketValueBalance + wo.MarketValueStorage) * wealthFactor;
                    }
                }
                MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth = patchMap;
            }
            catch (Exception ex)
            {
                Loger.Log("Exception PrepareInMainThread: " + ex.ToString(), Loger.LogLevel.ERROR);
            }
        }
        #endregion

        /// <summary>
        /// Формування вихідного пакета даних для відправки на сервер.
        /// </summary>
        public static void SendToServer(ModelPlayToServer toServ, bool firstRun, ModelGameServerInfo modelGameServerInfo)
        {
            toServ.LastTick = (long)Find.TickManager.TicksGame;
            List<Faction> factionList = Find.FactionManager.AllFactionsListForReading;

            if (SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                #region Первинна синхронізація об'єктів світу (EquableWorldObjects)
                try
                {
                    if (firstRun && modelGameServerInfo != null)
                    {
                        if (modelGameServerInfo.WObjectOnlineList.Count > 0)
                        {
                            var list = new List<WorldObjectOnline>();
                            for (int i = 0; i < allWorldObjects.Count; i++)
                            {
                                var wo = allWorldObjects[i];
                                if (wo is Settlement && wo.HasName && !wo.Faction.IsPlayer)
                                {
                                    list.Add(GetWorldObjects(wo));
                                }
                            }
                            toServ.WObjectOnlineList = list;
                        }

                        if (modelGameServerInfo.FactionOnlineList.Count > 0)
                        {
                            var factions = Find.FactionManager.AllFactionsListForReading;
                            var fList = new List<FactionOnline>(factions.Count);
                            for (int i = 0; i < factions.Count; i++)
                            {
                                fList.Add(GetFactions(factions[i]));
                            }
                            toServ.FactionOnlineList = fList;
                        }
                        return;
                    }
                }
                catch (Exception e)
                {
                    Loger.Log("Exception SendToServer FirstRun: " + e, Loger.LogLevel.ERROR);
                    return;
                }
                #endregion
            }

            if (!firstRun)
            {
                toServ.WObjects = WObjects;
                LastSendMyWorldObjects = toServ.WObjects;

                // ОПТИМІЗАЦІЯ: швидкий пошук видалених гравцем об'єктів O(N + M) через HashSet замість O(N * M)
                if (ToDelete != null && WorldObjectEntrys != null && WorldObjectEntrys.Count > 0)
                {
                    var presentIds = new HashSet<int>();
                    if (allWorldObjects != null)
                    {
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            presentIds.Add(allWorldObjects[i].ID);
                        }
                    }

                    foreach (var p in WorldObjectEntrys)
                    {
                        if (!presentIds.Contains(p.Key))
                        {
                            ToDelete.Add(p.Value);
                        }
                    }
                }

                toServ.WObjectsToDelete = ToDelete;
            }

            gameProgress.TransLog = Loger.GetTransLog();
            toServ.GameProgress = gameProgress;

            if (SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                #region Відправка неігрових поселень (NPC)
                try
                {
                    var onlineWObjList = new List<WorldObject>();
                    if (allWorldObjects != null)
                    {
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            var wo = allWorldObjects[i];
                            if (wo is Settlement && wo.HasName && !wo.Faction.IsPlayer)
                            {
                                onlineWObjList.Add(wo);
                            }
                        }
                    }

                    if (!firstRun && LastWorldObjectOnline != null && LastWorldObjectOnline.Count > 0)
                    {
                        toServ.WObjectOnlineToDelete = LastWorldObjectOnline
                            .Where(WOnline => !onlineWObjList.Any(wo => ValidateOnlineWorldObject(WOnline, wo)))
                            .ToList();

                        toServ.WObjectOnlineToAdd = onlineWObjList
                            .Where(wo => !LastWorldObjectOnline.Any(WOnline => ValidateOnlineWorldObject(WOnline, wo)))
                            .Select(obj => GetWorldObjects(obj))
                            .ToList();
                    }

                    toServ.WObjectOnlineList = onlineWObjList.Select(obj => GetWorldObjects(obj)).ToList();
                    LastWorldObjectOnline = toServ.WObjectOnlineList;
                }
                catch (Exception e)
                {
                    Loger.Log("Exception SendToServer WorldObject Online: " + e, Loger.LogLevel.ERROR);
                }
                #endregion

                #region Відправка неігрових фракцій
                try
                {
                    if (!firstRun && LastFactionOnline != null && LastFactionOnline.Count > 0)
                    {
                        toServ.FactionOnlineToDelete = LastFactionOnline
                            .Where(FOnline => !factionList.Any(f => ValidateFaction(FOnline, f)))
                            .ToList();

                        toServ.FactionOnlineToAdd = factionList
                            .Where(f => !LastFactionOnline.Any(FOnline => ValidateFaction(FOnline, f)))
                            .Select(obj => GetFactions(obj))
                            .ToList();
                    }

                    toServ.FactionOnlineList = factionList.Select(obj => GetFactions(obj)).ToList();
                    LastFactionOnline = toServ.FactionOnlineList;
                }
                catch (Exception e)
                {
                    Loger.Log("Exception SendToServer Faction Online: " + e, Loger.LogLevel.ERROR);
                }
                #endregion
            }
        }

        /// <summary>
        /// Застосування отриманих від сервера даних про колонії, каравани та пошту.
        /// </summary>
        public static void LoadFromServer(ModelPlayToClient fromServ, bool removeMissing)
        {
            if (SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                ApplyFactionsToWorld(fromServ);
                ApplyNonPlayerWorldObject(fromServ);
            }

            if (removeMissing)
            {
                var allWorldObjectsList = Find.WorldObjects.AllWorldObjects;
                for (int i = allWorldObjectsList.Count - 1; i >= 0; i--)
                {
                    var o = allWorldObjectsList[i];
                    if (o is CaravanOnline || o is WorldObjectBaseOnline)
                    {
                        Find.WorldObjects.Remove(o);
                    }
                }
                Loger.Log("RemoveMissing виконано");
            }

            ToDelete = new List<WorldObjectEntry>();

            // ОПТИМІЗАЦІЯ: швидке заповнення словника за один прохід без LINQ
            var catchAllWorldObjects = Find.WorldObjects.AllWorldObjects;
            var catchAllWorldObjectsByID = new Dictionary<int, WorldObjectBaseOnline>(catchAllWorldObjects.Count);
            for (int i = 0; i < catchAllWorldObjects.Count; i++)
            {
                if (catchAllWorldObjects[i] is WorldObjectBaseOnline wobo && wobo.ID != 0)
                {
                    catchAllWorldObjectsByID[wobo.ID] = wobo;
                }
            }

            var wObjectsList = catchAllWorldObjects.ToList();

            if (fromServ.WObjects != null && fromServ.WObjects.Count > 0)
            {
                for (int i = 0; i < fromServ.WObjects.Count; i++)
                    ApplyWorldObject(fromServ.WObjects[i], ref wObjectsList, ref catchAllWorldObjectsByID);
            }
            if (fromServ.WObjectsToDelete != null && fromServ.WObjectsToDelete.Count > 0)
            {
                for (int i = 0; i < fromServ.WObjectsToDelete.Count; i++)
                    DeleteWorldObject(fromServ.WObjectsToDelete[i], ref wObjectsList, ref catchAllWorldObjectsByID);
            }

            if (fromServ.WTObjects != null && fromServ.WTObjects.Count > 0)
            {
                for (int i = 0; i < fromServ.WTObjects.Count; i++)
                    ApplyTradeWorldObject(fromServ.WTObjects[i], ref wObjectsList, ref catchAllWorldObjectsByID);
            }
            if (fromServ.WTObjectsToDelete != null && fromServ.WTObjectsToDelete.Count > 0)
            {
                for (int i = 0; i < fromServ.WTObjectsToDelete.Count; i++)
                    DeleteTradeWorldObject(fromServ.WTObjectsToDelete[i], ref wObjectsList, ref catchAllWorldObjectsByID);
            }
            LastCatchAllWorldObjectsByID = catchAllWorldObjectsByID;

            // Локальні поселення заповнюємо останніми відправленими даними
            if (!removeMissing && SessionClientController.Data.Players.ContainsKey(SessionClientController.My.Login) && LastSendMyWorldObjects != null)
            {
                var myWObjects = new List<CaravanOnline>(LastSendMyWorldObjects.Count);
                for (int i = 0; i < LastSendMyWorldObjects.Count; i++)
                {
                    var wo = LastSendMyWorldObjects[i];
                    if (wo.Type == WorldObjectEntryType.Base)
                        myWObjects.Add(new BaseOnline { Tile = wo.Tile, OnlineWObject = wo });
                    else
                        myWObjects.Add(new CaravanOnline { Tile = wo.Tile, OnlineWObject = wo });
                }
                SessionClientController.Data.Players[SessionClientController.My.Login].WObjects = myWObjects;
            }

            // Обробка поштових посилок від інших гравців
            if (fromServ.Mails != null && fromServ.Mails.Count > 0)
            {
                LongEventHandler.QueueLongEvent(delegate
                {
                    foreach (var mail in fromServ.Mails)
                    {
                        MailController.MailArrived(mail);
                    }
                }, "", false, null);
            }
        }

        public static void ClearWorld()
        {
            var worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = worldObjects.Count - 1; i >= 0; i--)
            {
                if (worldObjects[i] is CaravanOnline)
                {
                    Find.WorldObjects.Remove(worldObjects[i]);
                }
            }
        }

        #region WorldObject допоміжні методи

        public static int GetLocalIdByServerId(long serverId)
        {
            if (ConverterServerId == null || !ConverterServerId.TryGetValue(serverId, out int objId))
            {
                return 0;
            }
            return objId;
        }

        public static WorldObjectEntry GetMyByServerId(long serverId)
        {
            if (ConverterServerId == null || !ConverterServerId.TryGetValue(serverId, out int objId)
                || WorldObjectEntrys == null || !WorldObjectEntrys.TryGetValue(objId, out var storeWO))
            {
                return null;
            }
            return storeWO;
        }

        public static WorldObjectEntry GetMyByLocalId(int id)
        {
            if (WorldObjectEntrys == null || !WorldObjectEntrys.TryGetValue(id, out var storeWO))
            {
                return null;
            }
            return storeWO;
        }

        public static WorldObjectBaseOnline GetOtherByServerIdDirtyRead(long serverId) =>
            GetOtherByServerId(serverId, LastCatchAllWorldObjectsByID);

        public static WorldObjectBaseOnline GetOtherByServerId(long serverId, Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID = null)
        {
            if (ConverterServerId == null || !ConverterServerId.TryGetValue(serverId, out int objId))
            {
                return null;
            }

            if (allWorldObjectsByID == null)
            {
                var allObjects = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < allObjects.Count; i++)
                {
                    if (allObjects[i].ID == objId && allObjects[i] is WorldObjectBaseOnline wobo)
                    {
                        return wobo;
                    }
                }
                return null;
            }
            else
            {
                return allWorldObjectsByID.TryGetValue(objId, out var worldObject) ? worldObject : null;
            }
        }

        public static WorldObject GetWOByServerId(long serverId, List<WorldObject> allObjects = null)
        {
            if (ConverterServerId == null || !ConverterServerId.TryGetValue(serverId, out int objId))
            {
                return null;
            }

            var list = allObjects ?? Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ID == objId)
                {
                    return list[i];
                }
            }
            return null;
        }

        public static string GetTestText()
        {
            var text = "ConverterServerId.";
            foreach (var item in ConverterServerId)
            {
                text += Environment.NewLine + item.Key + ", " + item.Value;
            }

            text += Environment.NewLine + Environment.NewLine + "MyWorldObjectEntry.";
            foreach (var item in WorldObjectEntrys)
            {
                text += Environment.NewLine + item.Key + ", " + item.Value.PlaceServerId + " " + item.Value.Name;
            }

            text += Environment.NewLine + Environment.NewLine + "ToDelete.";
            foreach (var item in ToDelete)
            {
                text += Environment.NewLine + item.PlaceServerId + " " + item.Name;
            }
            return text;
        }

        public static WorldObjectEntry GetServerInfo(WorldObject myWorldObject)
        {
            if (WorldObjectEntrys == null || !WorldObjectEntrys.TryGetValue(myWorldObject.ID, out var storeWO))
            {
                return null;
            }
            return storeWO;
        }

        private static void GameProgressAdd(PlayerGameProgress gameProgress, Pawn pawn)
        {
            if (pawn.Dead) return;
            if (pawn.IsFreeColonist && !pawn.IsPrisoner && !pawn.IsPrisonerOfColony && pawn.RaceProps.Humanlike)
            {
                gameProgress.ColonistsCount++;
                gameProgress.Pawns.Add(PawnStat.CreateTrade(pawn));

                if (pawn.Downed) gameProgress.ColonistsDownCount++;
                if (pawn.health.hediffSet.BleedRateTotal > 0) gameProgress.ColonistsBleedCount++;
                if (pawn.health.HasHediffsNeedingTend()) gameProgress.ColonistsNeedingTend++;

                int maxSkill = 0;
                var skillList = pawn.skills.skills;
                for (int i = 0; i < skillList.Count; i++)
                {
                    if (skillList[i].Level == 20) maxSkill++;
                }
                if (maxSkill >= 8) gameProgress.PawnMaxSkill++;

                var kh = pawn.records.GetAsInt(RecordDefOf.KillsHumanlikes);
                var km = pawn.records.GetAsInt(RecordDefOf.KillsMechanoids);

                gameProgress.KillsHumanlikes += kh;
                gameProgress.KillsMechanoids += km;
                if (gameProgress.KillsBestHumanlikesPawnName == null || kh > gameProgress.KillsBestHumanlikes)
                {
                    gameProgress.KillsBestHumanlikesPawnName = pawn.LabelCapNoCount;
                    gameProgress.KillsBestHumanlikes = kh;
                }
                if (gameProgress.KillsBestMechanoidsPawnName == null || km > gameProgress.KillsBestMechanoids)
                {
                    gameProgress.KillsBestMechanoidsPawnName = pawn.LabelCapNoCount;
                    gameProgress.KillsBestMechanoids = km;
                }
            }
            else if (pawn.RaceProps.Animal && pawn.training?.HasLearned(TrainableDefOf.Obedience) == true)
            {
                gameProgress.AnimalObedienceCount++;
            }
        }

        public static Dictionary<int, DateTime> LastForceRecount = new Dictionary<int, DateTime>();

        /// <summary>
        /// Розрахунок параметрів і вартості поселення або каравану гравця.
        /// ОПТИМІЗАЦІЯ: видалено Thread.Sleep(100) та копіювання списку пішаків.
        /// </summary>
        private static WorldObjectEntry GetWorldObjectEntry(WorldObject worldObject, PlayerGameProgress gameProgress, Dictionary<Map, CacheMap> cacheColonists)
        {
            var worldObjectEntry = new WorldObjectEntry
            {
                Type = worldObject is Caravan ? WorldObjectEntryType.Caravan : WorldObjectEntryType.Base,
                Tile = worldObject.Tile,
                Name = worldObject.LabelCap,
                LoginOwner = SessionClientController.My.Login,
                FreeWeight = 999999
            };

            if (worldObject is Caravan caravan)
            {
                var transferables = GameUtils.GetAllThings(caravan, true, false).DistinctToTransferableOneWays();
                var stackParts = new List<ThingCount>(transferables.Count);

                for (int i = 0; i < transferables.Count; i++)
                {
                    var allCount = transferables[i].MaxCount;
                    var things = transferables[i].things;
                    for (int ti = 0; ti < things.Count; ti++)
                    {
                        int cnt = Mathf.Min(things[ti].stackCount, allCount);
                        allCount -= cnt;
                        stackParts.Add(new ThingCount(things[ti], cnt));
                        if (allCount <= 0) break;
                    }
                }

                worldObjectEntry.FreeWeight = CollectionsMassCalculator.Capacity(stackParts)
                    - CollectionsMassCalculator.MassUsage(stackParts, IgnorePawnsInventoryMode.Ignore, false, false);

                worldObjectEntry.MarketValue = 0f;
                worldObjectEntry.MarketValuePawn = 0f;
                for (int i = 0; i < stackParts.Count; i++)
                {
                    int count = stackParts[i].Count;
                    if (count > 0)
                    {
                        Thing thing = stackParts[i].Thing;
                        if (thing is Pawn p)
                        {
                            worldObjectEntry.MarketValuePawn += p.MarketValue;
                            GameProgressAdd(gameProgress, p);
                        }
                        else
                        {
                            worldObjectEntry.MarketValue += thing.MarketValue * (float)count;
                        }
                    }
                }
            }
            else if (worldObject is Settlement settlement)
            {
                var map = settlement.Map;
                if (map != null)
                {
                    try
                    {
                        if (!LastForceRecount.TryGetValue(map.uniqueID, out var lastForceRecount))
                        {
                            LastForceRecount[map.uniqueID] = DateTime.UtcNow.AddSeconds(new Random(map.uniqueID * 7).Next(0, 10));
                        }
                        else if ((DateTime.UtcNow - lastForceRecount).TotalSeconds > 30)
                        {
                            LastForceRecount[map.uniqueID] = DateTime.UtcNow;
                            ModBaseData.RunMainThread(() => map.wealthWatcher.ForceRecount());
                        }
                        worldObjectEntry.MarketValue = map.wealthWatcher.WealthTotal;
                    }
                    catch
                    {
                        try
                        {
                            worldObjectEntry.MarketValue = map.wealthWatcher.WealthTotal;
                        }
                        catch { }
                    }

                    worldObjectEntry.MarketValuePawn = 0;

                    // ОПТИМІЗАЦІЯ: обхід AllPawnsSpawned безпосередньо без копіювання масивів та LINQ
                    if (!cacheColonists.TryGetValue(map, out var ps))
                    {
                        ps = new CacheMap();
                        var allSpawned = map.mapPawns.AllPawnsSpawned;
                        ps.Colonists = new List<Pawn>(allSpawned.Count);
                        bool hasEnemy = false;

                        for (int i = 0; i < allSpawned.Count; i++)
                        {
                            var p = allSpawned[i];
                            if (p == null) continue;

                            if (p.Faction == Faction.OfPlayer)
                            {
                                ps.Colonists.Add(p);
                            }
                            else if (!hasEnemy && !p.Dead && !p.Downed && !p.IsPrisoner && p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer))
                            {
                                hasEnemy = true;
                            }
                        }

                        ps.ExistsEnemyPawns = hasEnemy;
                        cacheColonists[map] = ps;
                    }

                    for (int i = 0; i < ps.Colonists.Count; i++)
                    {
                        var current = ps.Colonists[i];
                        if (current.RaceProps.Humanlike) worldObjectEntry.MarketValuePawn += current.MarketValue;
                        GameProgressAdd(gameProgress, current);
                    }

                    gameProgress.ExistsEnemyPawns |= ps.ExistsEnemyPawns;
                }
            }

            if (WorldObjectEntrys.TryGetValue(worldObject.ID, out var storeWO))
            {
                worldObjectEntry.PlaceServerId = storeWO.PlaceServerId;
            }

            return worldObjectEntry;
        }

        /// <summary>
        /// Застосування опису поселення або каравану іншого гравця.
        /// ОПТИМІЗАЦІЯ: усунено подвійні сканування словника через LINQ Any/First.
        /// </summary>
        public static void ApplyWorldObject(WorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                if (worldObjectEntry.LoginOwner == SessionClientController.My?.Login)
                {
                    int existingKey = -1;
                    foreach (var kvp in WorldObjectEntrys)
                    {
                        if (kvp.Value.PlaceServerId == worldObjectEntry.PlaceServerId)
                        {
                            existingKey = kvp.Key;
                            break;
                        }
                    }

                    if (existingKey < 0)
                    {
                        bool matched = false;
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            var wo = allWorldObjects[i];
                            if (!WorldObjectEntrys.ContainsKey(wo.ID)
                                && wo.Tile == worldObjectEntry.Tile
                                && ((wo is Caravan && worldObjectEntry.Type == WorldObjectEntryType.Caravan)
                                    || (wo is MapParent && worldObjectEntry.Type == WorldObjectEntryType.Base)))
                            {
                                var id = wo.ID;
                                Loger.Log("SetMyID " + id + " ServerId " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                                WorldObjectEntrys.Add(id, worldObjectEntry);

                                ConverterServerId[worldObjectEntry.PlaceServerId] = id;
                                matched = true;
                                return;
                            }
                        }

                        if (!matched)
                        {
                            Loger.Log("ToDel " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                            if (ToDelete != null) ToDelete.Add(worldObjectEntry);
                        }
                    }
                    else
                    {
                        WorldObjectEntrys[existingKey] = worldObjectEntry;
                    }
                    return;
                }

                var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID) as CaravanOnline;

                // Якщо на тайлі з'явилася база іншого гравця — видаляємо сторонні NPC-об'єкти
                if (worldObjectEntry.Type == WorldObjectEntryType.Base)
                {
                    for (int i = allWorldObjects.Count - 1; i >= 0; i--)
                    {
                        var cur = allWorldObjects[i];
                        if (cur.Tile == worldObjectEntry.Tile && cur != worldObject
                            && !(cur is Caravan) && !(cur is CaravanOnline)
                            && (cur.Faction == null || !cur.Faction.IsPlayer))
                        {
                            Loger.Log("Remove " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                            Find.WorldObjects.Remove(cur);
                        }
                    }
                }

                if (worldObject == null)
                {
                    worldObject = worldObjectEntry.Type == WorldObjectEntryType.Base
                        ? (CaravanOnline)WorldObjectMaker.MakeWorldObject(ModDefOf.BaseOnline)
                        : (CaravanOnline)WorldObjectMaker.MakeWorldObject(ModDefOf.CaravanOnline);

                    worldObject.SetFaction(Faction.OfPlayer);
                    worldObject.Tile = worldObjectEntry.Tile;
                    Find.WorldObjects.Add(worldObject);

                    ConverterServerId.Add(worldObjectEntry.PlaceServerId, worldObject.ID);
                    allWorldObjectsByID.Add(worldObject.ID, worldObject);
                    allWorldObjects.Add(worldObject);
                    Loger.Log("Add " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name + " " + worldObjectEntry.LoginOwner);
                }
                else
                {
                    ConverterServerId[worldObjectEntry.PlaceServerId] = worldObject.ID;
                }

                worldObject.Tile = worldObjectEntry.Tile;
                worldObject.OnlineWObject = worldObjectEntry;
            }
            catch (Exception ex)
            {
                Loger.Log("ApplyWorldObject Exception: " + ex.Message, Loger.LogLevel.ERROR);
                throw;
            }
        }

        public static void DrawTerritory(int centralTile)
        {
            List<int> neighbors = new List<int>(0);
            Find.WorldGrid.GetTileNeighbors(centralTile, neighbors);
            foreach (var tile in neighbors)
            {
                Find.WorldDebugDrawer.FlashTile(tile, WorldMaterials.DebugTileRenderQueue, null, 999999);
            }
            Find.WorldDebugDrawer.FlashTile(centralTile, WorldMaterials.DebugTileRenderQueue, null, 999999);
        }

        public static void DeleteWorldObject(WorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID) as CaravanOnline;
            if (worldObject != null)
            {
                allWorldObjectsByID.Remove(worldObject.ID);
                allWorldObjects.Remove(worldObject);
                ConverterServerId.Remove(worldObjectEntry.PlaceServerId);
                Find.WorldObjects.Remove(worldObject);
            }
        }

        public static void ApplyTradeWorldObject(TradeWorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID);

                if (worldObject == null)
                {
                    if (worldObjectEntry.Type == TradeWorldObjectEntryType.TradeOrder)
                    {
                        worldObject = (WorldObjectBaseOnline)WorldObjectMaker.MakeWorldObject(ModDefOf.TradeOrdersOnline);
                        ((TradeOrdersOnline)worldObject).TradeOrders = new List<TradeOrderShort> { (TradeOrderShort)worldObjectEntry };
                        WorldObject_TradeOrdersOnline.Add((TradeOrdersOnline)worldObject);
                        if (MainHelper.DebugMode) Loger.Log($"Client WorldObject_TradeOrdersOnline.Add Tile={worldObject.Tile} load={worldObjectEntry.Tile}");
                    }
                    else
                    {
                        worldObject = (WorldObjectBaseOnline)WorldObjectMaker.MakeWorldObject(ModDefOf.TradeThingsOnline);
                        ((TradeThingsOnline)worldObject).TradeThings = (TradeThingStorage)worldObjectEntry;
                    }

                    worldObject.SetFaction(Faction.OfPlayer);
                    worldObject.Tile = worldObjectEntry.Tile;
                    Find.WorldObjects.Add(worldObject);
                    if (MainHelper.DebugMode) Loger.Log($"Client WorldObject_TradeOrdersOnline Set0 Tile={worldObject.Tile} load={worldObjectEntry.Tile}");

                    ConverterServerId.Add(worldObjectEntry.PlaceServerId, worldObject.ID);
                    allWorldObjectsByID.Add(worldObject.ID, worldObject);
                    allWorldObjects.Add(worldObject);
                    Loger.Log("Add " + (worldObjectEntry.Type == TradeWorldObjectEntryType.TradeOrder ? "TradeOrderShort greenApp " : "TradeThingStorage redApp")
                        + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name + " " + worldObjectEntry.LoginOwner);
                }
                else
                {
                    ConverterServerId[worldObjectEntry.PlaceServerId] = worldObject.ID;
                    if (worldObjectEntry.Type == TradeWorldObjectEntryType.TradeOrder)
                    {
                        var worldObjectTO = worldObject as TradeOrdersOnline;
                        int i = 0;
                        for (; i < worldObjectTO.TradeOrders.Count; i++)
                        {
                            if (worldObjectTO.TradeOrders[i].Id == worldObjectEntry.Id)
                            {
                                worldObjectTO.TradeOrders[i] = (TradeOrderShort)worldObjectEntry;
                                break;
                            }
                        }
                        if (i == worldObjectTO.TradeOrders.Count)
                            worldObjectTO.TradeOrders.Add((TradeOrderShort)worldObjectEntry);
                    }
                    else
                    {
                        ((TradeThingsOnline)worldObject).TradeThings = (TradeThingStorage)worldObjectEntry;
                    }
                }

                worldObject.Tile = worldObjectEntry.Tile;
                if (MainHelper.DebugMode) Loger.Log($"Client WorldObject_TradeOrdersOnline Set Tile={worldObject.Tile} load={worldObjectEntry.Tile}");
            }
            catch (Exception ex)
            {
                Loger.Log("ApplyTradeWorldObject Exception: " + ex.Message, Loger.LogLevel.ERROR);
                throw;
            }
        }

        public static void DeleteTradeWorldObject(TradeWorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID);
            if (worldObject != null)
            {
                if (worldObjectEntry.Type == TradeWorldObjectEntryType.TradeOrder)
                {
                    var worldObjectTO = worldObject as TradeOrdersOnline;
                    for (int i = 0; i < worldObjectTO.TradeOrders.Count; i++)
                    {
                        if (worldObjectTO.TradeOrders[i].Id == worldObjectEntry.Id)
                        {
                            worldObjectTO.TradeOrders.RemoveAt(i--);
                            break;
                        }
                    }
                    if (worldObjectTO.TradeOrders.Count == 0)
                    {
                        allWorldObjectsByID.Remove(worldObject.ID);
                        allWorldObjects.Remove(worldObject);
                        ConverterServerId.Remove(worldObjectEntry.PlaceServerId);
                        Find.WorldObjects.Remove(worldObject);
                        WorldObject_TradeOrdersOnline.Remove(worldObjectTO);
                    }
                }
                else
                {
                    allWorldObjectsByID.Remove(worldObject.ID);
                    allWorldObjects.Remove(worldObject);
                    ConverterServerId.Remove(worldObjectEntry.PlaceServerId);
                    Find.WorldObjects.Remove(worldObject);
                }
            }
        }
        #endregion

        public static void InitGame()
        {
            WorldObjectEntrys = new Dictionary<int, WorldObjectEntry>();
            ConverterServerId = new Dictionary<long, int>();
            WorldObject_TradeOrdersOnline = new HashSet<TradeOrdersOnline>();
            ToDelete = null;
            LastCatchAllWorldObjectsByID = null;
        }

        #region Неігрові об'єкти планети
        private static void ApplyNonPlayerWorldObject(ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.WObjectOnlineToDelete != null && fromServ.WObjectOnlineToDelete.Count > 0)
                {
                    var objectToDelete = Find.WorldObjects.AllWorldObjects.Where(wo => wo is Settlement)
                        .Where(wo => wo.HasName && !wo.Faction.IsPlayer)
                        .Where(o => fromServ.WObjectOnlineToDelete.Any(fs => ValidateOnlineWorldObject(fs, o)))
                        .ToList();

                    objectToDelete.ForEach(o =>
                    {
                        Find.WorldObjects.SettlementAt(o.Tile).Destroy();
                        Find.World.WorldUpdate();
                    });

                    if (LastWorldObjectOnline != null && LastWorldObjectOnline.Count > 0)
                    {
                        LastWorldObjectOnline.RemoveAll(WOnline => objectToDelete.Any(o => ValidateOnlineWorldObject(WOnline, o)));
                    }
                }

                if (fromServ.WObjectOnlineToAdd != null && fromServ.WObjectOnlineToAdd.Count > 0)
                {
                    for (var i = 0; i < fromServ.WObjectOnlineToAdd.Count; i++)
                    {
                        var toAdd = fromServ.WObjectOnlineToAdd[i];
                        if (!Find.WorldObjects.AnySettlementAt(toAdd.Tile))
                        {
                            Faction faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(fm =>
                                fm.def.LabelCap == toAdd.FactionGroup &&
                                fm.loadID == toAdd.loadID);

                            if (faction != null)
                            {
                                var npcBase = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                                npcBase.SetFaction(faction);
                                npcBase.Tile = toAdd.Tile;
                                npcBase.Name = toAdd.Name;
                                Find.WorldObjects.Add(npcBase);
                            }
                            else
                            {
                                Log.Warning("Faction is missing or not found : " + toAdd.FactionGroup);
                                Loger.Log("Skipping ToAdd Settlement : " + toAdd.Name);
                            }
                        }
                        else
                        {
                            Loger.Log("Can't Add Settlement. Tile is already occupied " + Find.WorldObjects.SettlementAt(toAdd.Tile), Loger.LogLevel.WARNING);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Exception LoadFromServer ApplyNonPlayerWorldObject >> " + e);
            }
        }

        public static WorldObjectOnline GetWorldObjects(WorldObject obj)
        {
            return new WorldObjectOnline
            {
                Name = obj.LabelCap,
                Tile = obj.Tile,
                FactionGroup = obj?.Faction?.def?.LabelCap,
                FactionDef = obj?.Faction?.def?.defName,
                loadID = obj.Faction != null ? obj.Faction.loadID : 0
            };
        }

        private static bool ValidateOnlineWorldObject(WorldObjectOnline WObjectOnline1, WorldObject WObjectOnline2)
        {
            return WObjectOnline1.Name == WObjectOnline2.LabelCap && WObjectOnline1.Tile == WObjectOnline2.Tile;
        }
        #endregion

        #region Фракції
        private static void ApplyFactionsToWorld(ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.FactionOnlineToDelete != null && fromServ.FactionOnlineToDelete.Count > 0)
                {
                    var factionToDelete = Find.FactionManager.AllFactionsListForReading.Where(f => !f.IsPlayer)
                        .Where(obj => fromServ.FactionOnlineToDelete.Any(fs => ValidateFaction(fs, obj))).ToList();

                    OCFactionManager.UpdateFactionIDS(fromServ.FactionOnlineList);
                    for (var i = 0; i < factionToDelete.Count; i++)
                    {
                        OCFactionManager.DeleteFaction(factionToDelete[i]);
                    }

                    if (LastFactionOnline != null && LastFactionOnline.Count > 0)
                    {
                        LastFactionOnline.RemoveAll(FOnline => factionToDelete.Any(obj => ValidateFaction(FOnline, obj)));
                    }
                }

                if (fromServ.FactionOnlineToAdd != null && fromServ.FactionOnlineToAdd.Count > 0)
                {
                    for (var i = 0; i < fromServ.FactionOnlineToAdd.Count; i++)
                    {
                        var toAdd = fromServ.FactionOnlineToAdd[i];
                        try
                        {
                            var existingFaction = Find.FactionManager.AllFactionsListForReading.Where(f => ValidateFaction(toAdd, f)).ToList();
                            if (existingFaction.Count == 0)
                            {
                                OCFactionManager.UpdateFactionIDS(fromServ.FactionOnlineList);
                                OCFactionManager.AddNewFaction(toAdd);
                            }
                            else
                            {
                                Loger.Log("Failed to add faction. Faction already exists. > " + toAdd.LabelCap, Loger.LogLevel.ERROR);
                            }
                        }
                        catch
                        {
                            Loger.Log("Error faction to add LabelCap >> " + toAdd.LabelCap, Loger.LogLevel.ERROR);
                            Loger.Log("Error faction to add DefName >> " + toAdd.DefName, Loger.LogLevel.ERROR);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("OnlineCity: Error Apply new faction to world >> " + e);
            }
        }

        public static FactionOnline GetFactions(Faction obj)
        {
            return new FactionOnline
            {
                Name = obj.Name,
                LabelCap = obj.def.LabelCap,
                DefName = obj.def.defName,
                loadID = obj.loadID
            };
        }

        private static bool ValidateFaction(FactionOnline fOnline1, Faction fOnline2)
        {
            return fOnline1.LabelCap == fOnline2.def.LabelCap &&
                fOnline1.DefName == fOnline2.def.defName &&
                fOnline1.loadID == fOnline2.loadID;
        }
        #endregion
    }
}