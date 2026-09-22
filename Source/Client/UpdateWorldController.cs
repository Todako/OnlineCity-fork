using GameClasses;
using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses.Harmony;
using System;
using System.Collections.Generic;
using Transfer;
using UnityEngine;
using Verse;

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

        // Постійні буфери для усунення виділень пам'яті кожні 5 секунд
        private static readonly HashSet<int> _presentIdsBuffer = new HashSet<int>();
        private static readonly Dictionary<WorldObjectEntry, Map> _tmpMapBuffer = new Dictionary<WorldObjectEntry, Map>(16);
        private static readonly Dictionary<Map, float> _patchMapBuffer = new Dictionary<Map, float>(16);

        #region Ключі швидкого хешування (O(1) замість LINQ Any)

        private readonly struct WorldObjectKey : IEquatable<WorldObjectKey>
        {
            public readonly int Tile;
            public readonly string Name;

            public WorldObjectKey(int tile, string name)
            {
                Tile = tile;
                Name = name ?? string.Empty;
            }

            public bool Equals(WorldObjectKey other) => Tile == other.Tile && string.Equals(Name, other.Name, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is WorldObjectKey other && Equals(other);
            public override int GetHashCode() => (Tile * 397) ^ StringComparer.Ordinal.GetHashCode(Name);
        }

        private readonly struct FactionKey : IEquatable<FactionKey>
        {
            public readonly int LoadID;
            public readonly string DefName;

            public FactionKey(int loadID, string defName)
            {
                LoadID = loadID;
                DefName = defName ?? string.Empty;
            }

            public bool Equals(FactionKey other) => LoadID == other.LoadID && string.Equals(DefName, other.DefName, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is FactionKey other && Equals(other);
            public override int GetHashCode() => (LoadID * 397) ^ StringComparer.Ordinal.GetHashCode(DefName);
        }

        #endregion

        #region Збір даних у головному потоці гри

        public static List<WorldObject> allWorldObjects;
        public static List<WorldObjectEntry> WObjects;
        public static PlayerGameProgress gameProgress;

        /// <summary>
        /// Збирає стан власних караванів і поселень гравця безпосередньо з пам'яті гри.
        /// Виконується синхронно в головному потоці Unity.
        /// ОПТИМІЗАЦІЯ: повністю усунено тимчасовий клас CacheMap та списки пішаків.
        /// </summary>
        public static void PrepareInMainThread()
        {
            try
            {
                gameProgress = new PlayerGameProgress { Pawns = new List<PawnStat>(32) };

                allWorldObjects = GameUtils.GetAllWorldObjects();
                _tmpMapBuffer.Clear();
                WObjects = new List<WorldObjectEntry>(allWorldObjects.Count);

                float totalMarketValue = 0f;

                // Збір об'єктів та підсумовування вартості за один прохід
                for (int i = 0; i < allWorldObjects.Count; i++)
                {
                    var o = allWorldObjects[i];
                    if ((o.Faction?.IsPlayer ?? false) && (o is Settlement || o is Caravan))
                    {
                        var entry = GetWorldObjectEntry(o, gameProgress);
                        if (o is MapParent mp && mp.Map != null)
                        {
                            _tmpMapBuffer[entry] = mp.Map;
                        }
                        WObjects.Add(entry);
                        totalMarketValue += entry.MarketValue + entry.MarketValuePawn;
                    }
                }

                _patchMapBuffer.Clear();
                var wealthFactor = (float)SessionClientController.Data.GeneralSettings.ExchengePrecentWealthForIncident / 1000f;

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

                        if (wo.Type == WorldObjectEntryType.Base && _tmpMapBuffer.TryGetValue(wo, out var map) && map != null)
                        {
                            _patchMapBuffer[map] = (wo.MarketValueBalance + wo.MarketValueStorage) * wealthFactor;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < WObjects.Count; i++)
                    {
                        var wo = WObjects[i];
                        wo.MarketValueBalance = 0;
                        wo.MarketValueStorage = 0;

                        if (wo.Type == WorldObjectEntryType.Base && _tmpMapBuffer.TryGetValue(wo, out var map) && map != null)
                        {
                            _patchMapBuffer[map] = 0f;
                        }
                    }
                }

                MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth = _patchMapBuffer;
            }
            catch (Exception ex)
            {
                Loger.Log("Exception PrepareInMainThread: " + ex, Loger.LogLevel.ERROR);
            }
        }
        #endregion

        /// <summary>
        /// Формування вихідного пакета даних для відправки на сервер.
        /// </summary>
        public static void SendToServer(ModelPlayToServer toServ, bool firstRun, ModelGameServerInfo modelGameServerInfo)
        {
            toServ.LastTick = (long)Find.TickManager.TicksGame;

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

                // ОПТИМІЗАЦІЯ: швидкий пошук видалених об'єктів без виділення нових HashSet
                if (ToDelete != null && WorldObjectEntrys != null && WorldObjectEntrys.Count > 0)
                {
                    _presentIdsBuffer.Clear();
                    if (allWorldObjects != null)
                    {
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            _presentIdsBuffer.Add(allWorldObjects[i].ID);
                        }
                    }

                    foreach (var p in WorldObjectEntrys)
                    {
                        if (!_presentIdsBuffer.Contains(p.Key))
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
                #region Відправка неігрових поселень (NPC) - ОПТИМІЗАЦІЯ O(N+M)
                try
                {
                    var onlineWObjList = new List<WorldObject>();
                    var currentKeySet = new HashSet<WorldObjectKey>();

                    if (allWorldObjects != null)
                    {
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            var wo = allWorldObjects[i];
                            if (wo is Settlement && wo.HasName && !wo.Faction.IsPlayer)
                            {
                                onlineWObjList.Add(wo);
                                currentKeySet.Add(new WorldObjectKey(wo.Tile, wo.LabelCap));
                            }
                        }
                    }

                    if (!firstRun && LastWorldObjectOnline != null && LastWorldObjectOnline.Count > 0)
                    {
                        var lastKeySet = new HashSet<WorldObjectKey>();
                        for (int i = 0; i < LastWorldObjectOnline.Count; i++)
                        {
                            var obj = LastWorldObjectOnline[i];
                            lastKeySet.Add(new WorldObjectKey(obj.Tile, obj.Name));
                        }

                        var toDelete = new List<WorldObjectOnline>();
                        for (int i = 0; i < LastWorldObjectOnline.Count; i++)
                        {
                            var item = LastWorldObjectOnline[i];
                            if (!currentKeySet.Contains(new WorldObjectKey(item.Tile, item.Name)))
                            {
                                toDelete.Add(item);
                            }
                        }
                        toServ.WObjectOnlineToDelete = toDelete;

                        var toAdd = new List<WorldObjectOnline>();
                        for (int i = 0; i < onlineWObjList.Count; i++)
                        {
                            var item = onlineWObjList[i];
                            if (!lastKeySet.Contains(new WorldObjectKey(item.Tile, item.LabelCap)))
                            {
                                toAdd.Add(GetWorldObjects(item));
                            }
                        }
                        toServ.WObjectOnlineToAdd = toAdd;
                    }

                    var resultList = new List<WorldObjectOnline>(onlineWObjList.Count);
                    for (int i = 0; i < onlineWObjList.Count; i++)
                    {
                        resultList.Add(GetWorldObjects(onlineWObjList[i]));
                    }
                    toServ.WObjectOnlineList = resultList;
                    LastWorldObjectOnline = toServ.WObjectOnlineList;
                }
                catch (Exception e)
                {
                    Loger.Log("Exception SendToServer WorldObject Online: " + e, Loger.LogLevel.ERROR);
                }
                #endregion

                #region Відправка неігрових фракцій - ОПТИМІЗАЦІЯ O(N+M)
                try
                {
                    List<Faction> factionList = Find.FactionManager.AllFactionsListForReading;
                    if (!firstRun && LastFactionOnline != null && LastFactionOnline.Count > 0)
                    {
                        var currentFactionKeys = new HashSet<FactionKey>();
                        for (int i = 0; i < factionList.Count; i++)
                        {
                            var f = factionList[i];
                            currentFactionKeys.Add(new FactionKey(f.loadID, f.def.defName));
                        }

                        var lastFactionKeys = new HashSet<FactionKey>();
                        for (int i = 0; i < LastFactionOnline.Count; i++)
                        {
                            var f = LastFactionOnline[i];
                            lastFactionKeys.Add(new FactionKey(f.loadID, f.DefName));
                        }

                        var fToDelete = new List<FactionOnline>();
                        for (int i = 0; i < LastFactionOnline.Count; i++)
                        {
                            var item = LastFactionOnline[i];
                            if (!currentFactionKeys.Contains(new FactionKey(item.loadID, item.DefName)))
                            {
                                fToDelete.Add(item);
                            }
                        }
                        toServ.FactionOnlineToDelete = fToDelete;

                        var fToAdd = new List<FactionOnline>();
                        for (int i = 0; i < factionList.Count; i++)
                        {
                            var item = factionList[i];
                            if (!lastFactionKeys.Contains(new FactionKey(item.loadID, item.def.defName)))
                            {
                                fToAdd.Add(GetFactions(item));
                            }
                        }
                        toServ.FactionOnlineToAdd = fToAdd;
                    }

                    var resultFactions = new List<FactionOnline>(factionList.Count);
                    for (int i = 0; i < factionList.Count; i++)
                    {
                        resultFactions.Add(GetFactions(factionList[i]));
                    }
                    toServ.FactionOnlineList = resultFactions;
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

            if (ToDelete == null) ToDelete = new List<WorldObjectEntry>(8);
            else ToDelete.Clear();

            var catchAllWorldObjects = Find.WorldObjects.AllWorldObjects;

            // ОПТИМІЗАЦІЯ: повторне використання словника без алокації нового що-5 секунд
            if (LastCatchAllWorldObjectsByID == null)
            {
                LastCatchAllWorldObjectsByID = new Dictionary<int, WorldObjectBaseOnline>(catchAllWorldObjects.Count);
            }
            else
            {
                LastCatchAllWorldObjectsByID.Clear();
            }

            for (int i = 0; i < catchAllWorldObjects.Count; i++)
            {
                if (catchAllWorldObjects[i] is WorldObjectBaseOnline wobo && wobo.ID != 0)
                {
                    LastCatchAllWorldObjectsByID[wobo.ID] = wobo;
                }
            }

            var wObjectsList = catchAllWorldObjects;

            if (fromServ.WObjects != null && fromServ.WObjects.Count > 0)
            {
                for (int i = 0; i < fromServ.WObjects.Count; i++)
                    ApplyWorldObject(fromServ.WObjects[i], ref wObjectsList, ref LastCatchAllWorldObjectsByID);
            }
            if (fromServ.WObjectsToDelete != null && fromServ.WObjectsToDelete.Count > 0)
            {
                for (int i = 0; i < fromServ.WObjectsToDelete.Count; i++)
                    DeleteWorldObject(fromServ.WObjectsToDelete[i], ref wObjectsList, ref LastCatchAllWorldObjectsByID);
            }

            if (fromServ.WTObjects != null && fromServ.WTObjects.Count > 0)
            {
                for (int i = 0; i < fromServ.WTObjects.Count; i++)
                    ApplyTradeWorldObject(fromServ.WTObjects[i], ref wObjectsList, ref LastCatchAllWorldObjectsByID);
            }
            if (fromServ.WTObjectsToDelete != null && fromServ.WTObjectsToDelete.Count > 0)
            {
                for (int i = 0; i < fromServ.WTObjectsToDelete.Count; i++)
                    DeleteTradeWorldObject(fromServ.WTObjectsToDelete[i], ref wObjectsList, ref LastCatchAllWorldObjectsByID);
            }

            // ОПТИМІЗАЦІЯ: оновлення myWObjects за місцем замість постійного створення нових BaseOnline/CaravanOnline
            if (!removeMissing && SessionClientController.Data.Players.TryGetValue(SessionClientController.My.Login, out var myPlayerClient) && LastSendMyWorldObjects != null)
            {
                UpdateMyWObjectsInPlace(myPlayerClient, LastSendMyWorldObjects);
            }

            // Обробка поштових посилок від інших гравців
            if (fromServ.Mails != null && fromServ.Mails.Count > 0)
            {
                LongEventHandler.QueueLongEvent(delegate
                {
                    for (int i = 0; i < fromServ.Mails.Count; i++)
                    {
                        MailController.MailArrived(fromServ.Mails[i]);
                    }
                }, "", false, null);
            }
        }

        /// <summary>
        /// Оновлює список WObjects власного гравця без перестворення об'єктів у купі.
        /// </summary>
        private static void UpdateMyWObjectsInPlace(PlayerClient myPlayerClient, List<WorldObjectEntry> sendObjects)
        {
            if (myPlayerClient.WObjects == null)
            {
                myPlayerClient.WObjects = new List<CaravanOnline>(sendObjects.Count);
            }

            bool canReuse = myPlayerClient.WObjects.Count == sendObjects.Count;
            if (canReuse)
            {
                for (int i = 0; i < sendObjects.Count; i++)
                {
                    var wo = sendObjects[i];
                    var existing = myPlayerClient.WObjects[i];
                    bool isBase = wo.Type == WorldObjectEntryType.Base;
                    if ((isBase && !(existing is BaseOnline)) || (!isBase && (existing is BaseOnline)))
                    {
                        canReuse = false;
                        break;
                    }
                }
            }

            if (canReuse)
            {
                for (int i = 0; i < sendObjects.Count; i++)
                {
                    myPlayerClient.WObjects[i].Tile = sendObjects[i].Tile;
                    myPlayerClient.WObjects[i].OnlineWObject = sendObjects[i];
                }
            }
            else
            {
                myPlayerClient.WObjects.Clear();
                for (int i = 0; i < sendObjects.Count; i++)
                {
                    var wo = sendObjects[i];
                    if (wo.Type == WorldObjectEntryType.Base)
                        myPlayerClient.WObjects.Add(new BaseOnline { Tile = wo.Tile, OnlineWObject = wo });
                    else
                        myPlayerClient.WObjects.Add(new CaravanOnline { Tile = wo.Tile, OnlineWObject = wo });
                }
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

            return allWorldObjectsByID.TryGetValue(objId, out var worldObject) ? worldObject : null;
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

        public static WorldObjectEntry GetServerInfo(WorldObject myWorldObject)
        {
            if (WorldObjectEntrys == null || !WorldObjectEntrys.TryGetValue(myWorldObject.ID, out var storeWO))
            {
                return null;
            }
            return storeWO;
        }

        private static void GameProgressAdd(PlayerGameProgress progress, Pawn pawn)
        {
            if (pawn.Dead) return;
            if (pawn.IsFreeColonist && !pawn.IsPrisoner && !pawn.IsPrisonerOfColony && pawn.RaceProps.Humanlike)
            {
                progress.ColonistsCount++;
                progress.Pawns.Add(PawnStat.CreateTrade(pawn));

                if (pawn.Downed) progress.ColonistsDownCount++;
                if (pawn.health.hediffSet.BleedRateTotal > 0) progress.ColonistsBleedCount++;
                if (pawn.health.HasHediffsNeedingTend()) progress.ColonistsNeedingTend++;

                int maxSkill = 0;
                var skillList = pawn.skills.skills;
                for (int i = 0; i < skillList.Count; i++)
                {
                    if (skillList[i].Level == 20) maxSkill++;
                }
                if (maxSkill >= 8) progress.PawnMaxSkill++;

                var kh = pawn.records.GetAsInt(RecordDefOf.KillsHumanlikes);
                var km = pawn.records.GetAsInt(RecordDefOf.KillsMechanoids);

                progress.KillsHumanlikes += kh;
                progress.KillsMechanoids += km;
                if (progress.KillsBestHumanlikesPawnName == null || kh > progress.KillsBestHumanlikes)
                {
                    progress.KillsBestHumanlikesPawnName = pawn.LabelCapNoCount;
                    progress.KillsBestHumanlikes = kh;
                }
                if (progress.KillsBestMechanoidsPawnName == null || km > progress.KillsBestMechanoids)
                {
                    progress.KillsBestMechanoidsPawnName = pawn.LabelCapNoCount;
                    progress.KillsBestMechanoids = km;
                }
            }
            else if (pawn.RaceProps.Animal && pawn.training?.HasLearned(TrainableDefOf.Obedience) == true)
            {
                progress.AnimalObedienceCount++;
            }
        }

        public static readonly Dictionary<int, DateTime> LastForceRecount = new Dictionary<int, DateTime>();

        /// <summary>
        /// Розрахунок параметрів і вартості поселення або каравану гравця.
        /// ОПТИМІЗАЦІЯ: ліквідовано проміжний список CacheMap і new Random() при перерахунку багатства.
        /// </summary>
        private static WorldObjectEntry GetWorldObjectEntry(WorldObject worldObject, PlayerGameProgress progress)
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
                            GameProgressAdd(progress, p);
                        }
                        else
                        {
                            worldObjectEntry.MarketValue += thing.MarketValue * count;
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
                        // ОПТИМІЗАЦІЯ: детермінований зсув перерахунку без виділення new Random()
                        if (!LastForceRecount.TryGetValue(map.uniqueID, out var lastForceRecount))
                        {
                            LastForceRecount[map.uniqueID] = DateTime.UtcNow.AddSeconds(map.uniqueID % 10);
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

                    worldObjectEntry.MarketValuePawn = 0f;

                    // ОПТИМІЗАЦІЯ: обробка пішаків мапи за один лінійний прохід без виділення проміжних списків
                    var allSpawned = map.mapPawns.AllPawnsSpawned;
                    bool hasEnemy = false;

                    for (int i = 0; i < allSpawned.Count; i++)
                    {
                        var p = allSpawned[i];
                        if (p == null) continue;

                        if (p.Faction == Faction.OfPlayer)
                        {
                            if (p.RaceProps.Humanlike)
                            {
                                worldObjectEntry.MarketValuePawn += p.MarketValue;
                            }
                            GameProgressAdd(progress, p);
                        }
                        else if (!hasEnemy && !p.Dead && !p.Downed && !p.IsPrisoner && p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer))
                        {
                            hasEnemy = true;
                        }
                    }

                    progress.ExistsEnemyPawns |= hasEnemy;
                }
            }

            if (WorldObjectEntrys != null && WorldObjectEntrys.TryGetValue(worldObject.ID, out var storeWO))
            {
                worldObjectEntry.PlaceServerId = storeWO.PlaceServerId;
            }

            return worldObjectEntry;
        }

        /// <summary>
        /// Застосування опису поселення або каравану іншого гравця.
        /// </summary>
        public static void ApplyWorldObject(WorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                if (worldObjectEntry.LoginOwner == SessionClientController.My?.Login)
                {
                    int existingKey = -1;

                    if (ConverterServerId != null && ConverterServerId.TryGetValue(worldObjectEntry.PlaceServerId, out int mappedId) && WorldObjectEntrys.ContainsKey(mappedId))
                    {
                        existingKey = mappedId;
                    }
                    else if (WorldObjectEntrys != null)
                    {
                        foreach (var kvp in WorldObjectEntrys)
                        {
                            if (kvp.Value.PlaceServerId == worldObjectEntry.PlaceServerId)
                            {
                                existingKey = kvp.Key;
                                break;
                            }
                        }
                    }

                    if (existingKey < 0)
                    {
                        bool matched = false;
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            var wo = allWorldObjects[i];
                            if (WorldObjectEntrys != null && !WorldObjectEntrys.ContainsKey(wo.ID)
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
                    else if (WorldObjectEntrys != null)
                    {
                        WorldObjectEntrys[existingKey] = worldObjectEntry;
                    }
                    return;
                }

                var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID) as CaravanOnline;

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
            List<int> neighbors = new List<int>(8);
            Find.WorldGrid.GetTileNeighbors(centralTile, neighbors);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Find.WorldDebugDrawer.FlashTile(neighbors[i], WorldMaterials.DebugTileRenderQueue, null, 999999);
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
                            worldObjectTO.TradeOrders.RemoveAt(i);
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
        /// <summary>
        /// Видалення та додавання поселень NPC без множинних WorldUpdate() та подвійного пошуку.
        /// </summary>
        private static void ApplyNonPlayerWorldObject(ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.WObjectOnlineToDelete != null && fromServ.WObjectOnlineToDelete.Count > 0)
                {
                    var deleteKeys = new HashSet<WorldObjectKey>();
                    for (int i = 0; i < fromServ.WObjectOnlineToDelete.Count; i++)
                    {
                        var item = fromServ.WObjectOnlineToDelete[i];
                        deleteKeys.Add(new WorldObjectKey(item.Tile, item.Name));
                    }

                    var all = Find.WorldObjects.AllWorldObjects;
                    var objectToDelete = new List<WorldObject>();

                    for (int i = 0; i < all.Count; i++)
                    {
                        var wo = all[i];
                        if (wo is Settlement && wo.HasName && !wo.Faction.IsPlayer)
                        {
                            if (deleteKeys.Contains(new WorldObjectKey(wo.Tile, wo.LabelCap)))
                            {
                                objectToDelete.Add(wo);
                            }
                        }
                    }

                    bool anyDestroyed = false;
                    for (int i = 0; i < objectToDelete.Count; i++)
                    {
                        objectToDelete[i].Destroy();
                        anyDestroyed = true;
                    }

                    if (anyDestroyed)
                    {
                        Find.World.WorldUpdate();
                    }

                    if (LastWorldObjectOnline != null && LastWorldObjectOnline.Count > 0)
                    {
                        var deletedSet = new HashSet<WorldObjectKey>();
                        for (int i = 0; i < objectToDelete.Count; i++)
                        {
                            deletedSet.Add(new WorldObjectKey(objectToDelete[i].Tile, objectToDelete[i].LabelCap));
                        }
                        LastWorldObjectOnline.RemoveAll(wo => deletedSet.Contains(new WorldObjectKey(wo.Tile, wo.Name)));
                    }
                }

                if (fromServ.WObjectOnlineToAdd != null && fromServ.WObjectOnlineToAdd.Count > 0)
                {
                    var factions = Find.FactionManager.AllFactionsListForReading;

                    for (int i = 0; i < fromServ.WObjectOnlineToAdd.Count; i++)
                    {
                        var toAdd = fromServ.WObjectOnlineToAdd[i];
                        if (!Find.WorldObjects.AnySettlementAt(toAdd.Tile))
                        {
                            Faction faction = null;
                            for (int f = 0; f < factions.Count; f++)
                            {
                                var fm = factions[f];
                                if (fm.loadID == toAdd.loadID && fm.def.LabelCap == toAdd.FactionGroup)
                                {
                                    faction = fm;
                                    break;
                                }
                            }

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
        #endregion

        #region Фракції
        /// <summary>
        /// Оновлення списку фракцій на карті за O(N + M) без вкладених LINQ-викликів.
        /// </summary>
        private static void ApplyFactionsToWorld(ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.FactionOnlineToDelete != null && fromServ.FactionOnlineToDelete.Count > 0)
                {
                    var deleteKeys = new HashSet<FactionKey>();
                    for (int i = 0; i < fromServ.FactionOnlineToDelete.Count; i++)
                    {
                        var fo = fromServ.FactionOnlineToDelete[i];
                        deleteKeys.Add(new FactionKey(fo.loadID, fo.DefName));
                    }

                    var allFactions = Find.FactionManager.AllFactionsListForReading;
                    var factionToDelete = new List<Faction>();

                    for (int i = 0; i < allFactions.Count; i++)
                    {
                        var f = allFactions[i];
                        if (!f.IsPlayer && deleteKeys.Contains(new FactionKey(f.loadID, f.def.defName)))
                        {
                            factionToDelete.Add(f);
                        }
                    }

                    OCFactionManager.UpdateFactionIDS(fromServ.FactionOnlineList);
                    for (int i = 0; i < factionToDelete.Count; i++)
                    {
                        OCFactionManager.DeleteFaction(factionToDelete[i]);
                    }

                    if (LastFactionOnline != null && LastFactionOnline.Count > 0)
                    {
                        var deletedSet = new HashSet<FactionKey>();
                        for (int i = 0; i < factionToDelete.Count; i++)
                        {
                            deletedSet.Add(new FactionKey(factionToDelete[i].loadID, factionToDelete[i].def.defName));
                        }
                        LastFactionOnline.RemoveAll(fo => deletedSet.Contains(new FactionKey(fo.loadID, fo.DefName)));
                    }
                }

                if (fromServ.FactionOnlineToAdd != null && fromServ.FactionOnlineToAdd.Count > 0)
                {
                    var existingFactions = Find.FactionManager.AllFactionsListForReading;
                    var existingKeys = new HashSet<FactionKey>();
                    for (int i = 0; i < existingFactions.Count; i++)
                    {
                        var f = existingFactions[i];
                        existingKeys.Add(new FactionKey(f.loadID, f.def.defName));
                    }

                    for (int i = 0; i < fromServ.FactionOnlineToAdd.Count; i++)
                    {
                        var toAdd = fromServ.FactionOnlineToAdd[i];
                        try
                        {
                            if (!existingKeys.Contains(new FactionKey(toAdd.loadID, toAdd.DefName)))
                            {
                                OCFactionManager.UpdateFactionIDS(fromServ.FactionOnlineList);
                                OCFactionManager.AddNewFaction(toAdd);
                                existingKeys.Add(new FactionKey(toAdd.loadID, toAdd.DefName));
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
        #endregion
    }
}