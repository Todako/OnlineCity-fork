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
        // ОПТИМІЗАЦІЯ ТА ВИПРАВЛЕННЯ: окремі словники для власних, чужих і торгових об'єктів,
        // щоб їхні PlaceServerId ніколи не перезаписували один одного.
        private static Dictionary<long, int> MyServerToLocalId { get; set; }
        private static Dictionary<long, int> OtherServerToLocalId { get; set; }
        private static Dictionary<long, int> TradeServerToLocalId { get; set; }

        public static Dictionary<int, WorldObjectEntry> WorldObjectEntrys { get; private set; }
        private static List<WorldObjectEntry> ToDelete { get; set; }
        public static HashSet<TradeOrdersOnline> WorldObject_TradeOrdersOnline { get; set; }

        private static List<WorldObjectEntry> LastSendMyWorldObjects { get; set; }
        private static List<WorldObjectOnline> LastWorldObjectOnline { get; set; }
        private static List<FactionOnline> LastFactionOnline { get; set; }

        private static Dictionary<int, WorldObjectBaseOnline> LastCatchAllWorldObjectsByID;

        public static bool ExistsEnemyPawns => gameProgress?.ExistsEnemyPawns == true;

        private const long RecountIntervalTicks = 30L * TimeSpan.TicksPerSecond;

        // Постійні буфери для усунення регулярних алокацій у купі GC
        private static readonly HashSet<int> _presentIdsBuffer = new HashSet<int>();
        private static readonly Dictionary<WorldObjectEntry, Map> _tmpMapBuffer = new Dictionary<WorldObjectEntry, Map>(16);
        private static readonly Dictionary<Map, float> _patchMapBuffer = new Dictionary<Map, float>(16);
        private static readonly List<int> _neighborsBuffer = new List<int>(8);
        private static readonly List<ThingCount> _stackPartsBuffer = new List<ThingCount>(64);

        // Буфери для синхронізації EquableWorldObjects
        private static readonly List<WorldObject> _reusableOnlineWObjList = new List<WorldObject>(32);
        private static readonly HashSet<WorldObjectKey> _reusableCurrentWObjKeySet = new HashSet<WorldObjectKey>();
        private static readonly HashSet<WorldObjectKey> _reusableLastWObjKeySet = new HashSet<WorldObjectKey>();
        private static readonly HashSet<FactionKey> _reusableCurrentFactionKeySet = new HashSet<FactionKey>();
        private static readonly HashSet<FactionKey> _reusableLastFactionKeySet = new HashSet<FactionKey>();
        private static readonly HashSet<WorldObjectKey> _reusableWObjDeleteKeys = new HashSet<WorldObjectKey>();
        private static readonly HashSet<FactionKey> _reusableFactionDeleteKeys = new HashSet<FactionKey>();

        #region Ключі швидкого хешування

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

        public static void PrepareInMainThread()
        {
            try
            {
                gameProgress = new PlayerGameProgress { Pawns = new List<PawnStat>(32) };

                allWorldObjects = GameUtils.GetAllWorldObjects();
                _tmpMapBuffer.Clear();
                WObjects = new List<WorldObjectEntry>(allWorldObjects != null ? allWorldObjects.Count : 16);

                float totalMarketValue = 0f;

                if (allWorldObjects != null)
                {
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
                }

                _patchMapBuffer.Clear();
                var wealthFactor = SessionClientController.Data != null
                    ? (float)SessionClientController.Data.GeneralSettings.ExchengePrecentWealthForIncident / 1000f
                    : 0f;

                if (totalMarketValue > 0)
                {
                    var data = SessionClientController.Data;
                    var cashlessBalance = data != null ? Math.Abs(data.CashlessBalance) : 0f;
                    var storageBalance = data != null ? Math.Abs(data.StorageBalance) : 0f;

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

                // Roads of the Rim: відстеження будівельних ділянок, маршрутів та прогресу в головному потоці
                RoadsOfTheRimPatch.TrackConstructionObjectsOnMainThread(allWorldObjects);
            }
            catch (Exception ex)
            {
                Loger.Log("Exception PrepareInMainThread: " + ex, Loger.LogLevel.ERROR);
            }
        }
        #endregion

        public static void SendToServer(ModelPlayToServer toServ, bool firstRun, ModelGameServerInfo modelGameServerInfo)
        {
            if (Find.TickManager != null)
            {
                toServ.LastTick = (long)Find.TickManager.TicksGame;
            }

            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                #region Первинна синхронізація об'єктів світу (EquableWorldObjects)
                try
                {
                    if (firstRun && modelGameServerInfo != null)
                    {
                        if (modelGameServerInfo.WObjectOnlineList != null && modelGameServerInfo.WObjectOnlineList.Count > 0 && allWorldObjects != null)
                        {
                            var list = new List<WorldObjectOnline>();
                            for (int i = 0; i < allWorldObjects.Count; i++)
                            {
                                var wo = allWorldObjects[i];
                                if (wo is Settlement && wo.HasName && wo.Faction != null && !wo.Faction.IsPlayer)
                                {
                                    list.Add(GetWorldObjects(wo));
                                }
                            }
                            toServ.WObjectOnlineList = list;
                        }

                        if (modelGameServerInfo.FactionOnlineList != null && modelGameServerInfo.FactionOnlineList.Count > 0 && Find.FactionManager != null)
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

                // Roads of the Rim: відправка побудованих доріг і запланованих будівельних майданчиків на сервер
                toServ.RoadsToAdd = RoadsOfTheRimPatch.FlushPendingRoadsForServer();
                toServ.RoadSitesToSync = RoadsOfTheRimPatch.FlushPendingRoadSitesForServer();

                if (ToDelete == null) ToDelete = new List<WorldObjectEntry>(8);

                bool inActivePvp = SessionClientController.Data?.AttackModule != null
                    || SessionClientController.Data?.AttackUsModule != null;

                if (!inActivePvp && WorldObjectEntrys != null && WorldObjectEntrys.Count > 0)
                {
                    _presentIdsBuffer.Clear();
                    if (allWorldObjects != null)
                    {
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            _presentIdsBuffer.Add(allWorldObjects[i].ID);
                        }
                    }

                    List<int> removedLocalKeys = null;
                    foreach (var p in WorldObjectEntrys)
                    {
                        if (!_presentIdsBuffer.Contains(p.Key))
                        {
                            ToDelete.Add(p.Value);
                            if (removedLocalKeys == null) removedLocalKeys = new List<int>(4);
                            removedLocalKeys.Add(p.Key);
                        }
                    }

                    if (removedLocalKeys != null)
                    {
                        for (int i = 0; i < removedLocalKeys.Count; i++)
                        {
                            int k = removedLocalKeys[i];
                            if (WorldObjectEntrys.TryGetValue(k, out var entry))
                            {
                                MyServerToLocalId?.Remove(entry.PlaceServerId);
                            }
                            WorldObjectEntrys.Remove(k);
                        }
                    }
                }

                if (!inActivePvp && ToDelete.Count > 0)
                {
                    toServ.WObjectsToDelete = new List<WorldObjectEntry>(ToDelete);
                    ToDelete.Clear();
                }
                else
                {
                    toServ.WObjectsToDelete = null;
                }
            }

            if (gameProgress != null)
            {
                gameProgress.TransLog = Loger.GetTransLog();
                toServ.GameProgress = gameProgress;
            }

            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                #region Відправка неігрових поселень (NPC)
                try
                {
                    _reusableOnlineWObjList.Clear();
                    _reusableCurrentWObjKeySet.Clear();

                    if (allWorldObjects != null)
                    {
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            var wo = allWorldObjects[i];
                            if (wo is Settlement && wo.HasName && wo.Faction != null && !wo.Faction.IsPlayer)
                            {
                                _reusableOnlineWObjList.Add(wo);
                                _reusableCurrentWObjKeySet.Add(new WorldObjectKey(wo.Tile, wo.LabelCap));
                            }
                        }
                    }

                    if (!firstRun && LastWorldObjectOnline != null && LastWorldObjectOnline.Count > 0)
                    {
                        _reusableLastWObjKeySet.Clear();
                        for (int i = 0; i < LastWorldObjectOnline.Count; i++)
                        {
                            var obj = LastWorldObjectOnline[i];
                            _reusableLastWObjKeySet.Add(new WorldObjectKey(obj.Tile, obj.Name));
                        }

                        var toDelete = new List<WorldObjectOnline>();
                        for (int i = 0; i < LastWorldObjectOnline.Count; i++)
                        {
                            var item = LastWorldObjectOnline[i];
                            if (!_reusableCurrentWObjKeySet.Contains(new WorldObjectKey(item.Tile, item.Name)))
                            {
                                toDelete.Add(item);
                            }
                        }
                        toServ.WObjectOnlineToDelete = toDelete;

                        var toAdd = new List<WorldObjectOnline>();
                        for (int i = 0; i < _reusableOnlineWObjList.Count; i++)
                        {
                            var item = _reusableOnlineWObjList[i];
                            if (!_reusableLastWObjKeySet.Contains(new WorldObjectKey(item.Tile, item.LabelCap)))
                            {
                                toAdd.Add(GetWorldObjects(item));
                            }
                        }
                        toServ.WObjectOnlineToAdd = toAdd;
                    }

                    var resultList = new List<WorldObjectOnline>(_reusableOnlineWObjList.Count);
                    for (int i = 0; i < _reusableOnlineWObjList.Count; i++)
                    {
                        resultList.Add(GetWorldObjects(_reusableOnlineWObjList[i]));
                    }
                    toServ.WObjectOnlineList = resultList;
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
                    if (Find.FactionManager != null)
                    {
                        List<Faction> factionList = Find.FactionManager.AllFactionsListForReading;
                        if (!firstRun && LastFactionOnline != null && LastFactionOnline.Count > 0)
                        {
                            _reusableCurrentFactionKeySet.Clear();
                            for (int i = 0; i < factionList.Count; i++)
                            {
                                var f = factionList[i];
                                _reusableCurrentFactionKeySet.Add(new FactionKey(f.loadID, f.def.defName));
                            }

                            _reusableLastFactionKeySet.Clear();
                            for (int i = 0; i < LastFactionOnline.Count; i++)
                            {
                                var f = LastFactionOnline[i];
                                _reusableLastFactionKeySet.Add(new FactionKey(f.loadID, f.DefName));
                            }

                            var fToDelete = new List<FactionOnline>();
                            for (int i = 0; i < LastFactionOnline.Count; i++)
                            {
                                var item = LastFactionOnline[i];
                                if (!_reusableCurrentFactionKeySet.Contains(new FactionKey(item.loadID, item.DefName)))
                                {
                                    fToDelete.Add(item);
                                }
                            }
                            toServ.FactionOnlineToDelete = fToDelete;

                            var fToAdd = new List<FactionOnline>();
                            for (int i = 0; i < factionList.Count; i++)
                            {
                                var item = factionList[i];
                                if (!_reusableLastFactionKeySet.Contains(new FactionKey(item.loadID, item.def.defName)))
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
                }
                catch (Exception e)
                {
                    Loger.Log("Exception SendToServer Faction Online: " + e, Loger.LogLevel.ERROR);
                }
                #endregion
            }
        }

        public static void LoadFromServer(ModelPlayToClient fromServ, bool removeMissing)
        {
            if (fromServ == null) return;

            // Roads of the Rim: застосування нових доріг та будівельних майданчиків, отриманих від сервера
            if (fromServ.Roads != null && fromServ.Roads.Count > 0)
            {
                RoadsOfTheRimPatch.ApplyRoadsFromServer(fromServ.Roads);
            }
            if (fromServ.RoadSites != null && fromServ.RoadSites.Count > 0)
            {
                RoadsOfTheRimPatch.ApplyRoadSitesFromServer(fromServ.RoadSites);
            }

            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                ApplyFactionsToWorld(fromServ);
                ApplyNonPlayerWorldObject(fromServ);
            }

            if (removeMissing && Find.WorldObjects != null)
            {
                var allWorldObjectsList = Find.WorldObjects.AllWorldObjects;
                for (int i = allWorldObjectsList.Count - 1; i >= 0; i--)
                {
                    var o = allWorldObjectsList[i];
                    if (o is WorldObjectBaseOnline)
                    {
                        Find.WorldObjects.Remove(o);
                    }
                }
                MyServerToLocalId?.Clear();
                OtherServerToLocalId?.Clear();
                TradeServerToLocalId?.Clear();
                Loger.Log("RemoveMissing виконано");
            }

            if (ToDelete == null) ToDelete = new List<WorldObjectEntry>(8);

            var catchAllWorldObjects = Find.WorldObjects?.AllWorldObjects;
            if (catchAllWorldObjects == null) return;

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

            if (!removeMissing && SessionClientController.My?.Login != null && SessionClientController.Data?.Players != null
                && SessionClientController.Data.Players.TryGetValue(SessionClientController.My.Login, out var myPlayerClient) && LastSendMyWorldObjects != null)
            {
                UpdateMyWObjectsInPlace(myPlayerClient, LastSendMyWorldObjects);
            }

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
            if (Find.WorldObjects == null) return;
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
            if (MyServerToLocalId != null && MyServerToLocalId.TryGetValue(serverId, out int myId))
                return myId;
            if (OtherServerToLocalId != null && OtherServerToLocalId.TryGetValue(serverId, out int otherId))
                return otherId;
            if (TradeServerToLocalId != null && TradeServerToLocalId.TryGetValue(serverId, out int tradeId))
                return tradeId;
            return 0;
        }

        public static WorldObjectEntry GetMyByServerId(long serverId)
        {
            if (MyServerToLocalId != null && MyServerToLocalId.TryGetValue(serverId, out int objId)
                && WorldObjectEntrys != null && WorldObjectEntrys.TryGetValue(objId, out var storeWO))
            {
                return storeWO;
            }

            if (WorldObjectEntrys != null)
            {
                foreach (var kvp in WorldObjectEntrys)
                {
                    if (kvp.Value.PlaceServerId == serverId)
                    {
                        if (MyServerToLocalId != null) MyServerToLocalId[serverId] = kvp.Key;
                        return kvp.Value;
                    }
                }
            }
            return null;
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
            if (OtherServerToLocalId != null && OtherServerToLocalId.TryGetValue(serverId, out int objId))
            {
                if (allWorldObjectsByID != null)
                {
                    if (allWorldObjectsByID.TryGetValue(objId, out var wo) && wo is CaravanOnline) return wo;
                }
                else
                {
                    var all = Find.WorldObjects?.AllWorldObjects;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            if (all[i].ID == objId && all[i] is CaravanOnline wobo) return wobo;
                        }
                    }
                }
            }

            var allWorld = Find.WorldObjects?.AllWorldObjects;
            if (allWorld != null)
            {
                for (int i = 0; i < allWorld.Count; i++)
                {
                    if (allWorld[i] is CaravanOnline wobo && wobo.Place?.PlaceServerId == serverId)
                    {
                        if (OtherServerToLocalId != null) OtherServerToLocalId[serverId] = wobo.ID;
                        return wobo;
                    }
                }
            }

            return null;
        }

        private static WorldObjectBaseOnline GetTradeByServerId(long serverId, Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID = null)
        {
            if (TradeServerToLocalId != null && TradeServerToLocalId.TryGetValue(serverId, out int objId))
            {
                if (allWorldObjectsByID != null)
                {
                    if (allWorldObjectsByID.TryGetValue(objId, out var wo) && !(wo is CaravanOnline)) return wo;
                }
                else
                {
                    var all = Find.WorldObjects?.AllWorldObjects;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            if (all[i].ID == objId && all[i] is WorldObjectBaseOnline wobo && !(wobo is CaravanOnline)) return wobo;
                        }
                    }
                }
            }

            var allWorld = Find.WorldObjects?.AllWorldObjects;
            if (allWorld != null)
            {
                for (int i = 0; i < allWorld.Count; i++)
                {
                    if (allWorld[i] is WorldObjectBaseOnline wobo && !(wobo is CaravanOnline) && wobo.Place?.PlaceServerId == serverId)
                    {
                        if (TradeServerToLocalId != null) TradeServerToLocalId[serverId] = wobo.ID;
                        return wobo;
                    }
                }
            }

            return null;
        }

        public static WorldObject GetWOByServerId(long serverId, List<WorldObject> allObjects = null)
        {
            var list = allObjects ?? Find.WorldObjects?.AllWorldObjects;
            if (list == null) return null;

            if (MyServerToLocalId != null && MyServerToLocalId.TryGetValue(serverId, out int myObjId))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].ID == myObjId) return list[i];
                }
            }

            if (WorldObjectEntrys != null)
            {
                foreach (var kvp in WorldObjectEntrys)
                {
                    if (kvp.Value.PlaceServerId == serverId)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i].ID == kvp.Key) return list[i];
                        }
                    }
                }
            }

            if (OtherServerToLocalId != null && OtherServerToLocalId.TryGetValue(serverId, out int otherObjId))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].ID == otherObjId) return list[i];
                }
            }

            return null;
        }

        public static WorldObjectEntry GetServerInfo(WorldObject myWorldObject)
        {
            if (myWorldObject == null || WorldObjectEntrys == null || !WorldObjectEntrys.TryGetValue(myWorldObject.ID, out var storeWO))
            {
                return null;
            }
            return storeWO;
        }

        private static void GameProgressAdd(PlayerGameProgress progress, Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return;
            if (pawn.IsFreeColonist && !pawn.IsPrisoner && !pawn.IsPrisonerOfColony && (pawn.RaceProps?.Humanlike ?? false))
            {
                progress.ColonistsCount++;
                progress.Pawns.Add(PawnStat.CreateTrade(pawn));

                if (pawn.Downed) progress.ColonistsDownCount++;
                if (pawn.health?.hediffSet?.BleedRateTotal > 0) progress.ColonistsBleedCount++;
                if (pawn.health?.HasHediffsNeedingTend() ?? false) progress.ColonistsNeedingTend++;

                int maxSkill = 0;
                var skillList = pawn.skills?.skills;
                if (skillList != null)
                {
                    for (int i = 0; i < skillList.Count; i++)
                    {
                        if (skillList[i].Level == 20) maxSkill++;
                    }
                }
                if (maxSkill >= 8) progress.PawnMaxSkill++;

                var kh = pawn.records?.GetAsInt(RecordDefOf.KillsHumanlikes) ?? 0;
                var km = pawn.records?.GetAsInt(RecordDefOf.KillsMechanoids) ?? 0;

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
            else if ((pawn.RaceProps?.Animal ?? false) && pawn.training?.HasLearned(TrainableDefOf.Obedience) == true)
            {
                progress.AnimalObedienceCount++;
            }
        }

        public static readonly Dictionary<int, long> LastForceRecountTicks = new Dictionary<int, long>();

        private static WorldObjectEntry GetWorldObjectEntry(WorldObject worldObject, PlayerGameProgress progress)
        {
            var worldObjectEntry = new WorldObjectEntry
            {
                Type = worldObject is Caravan ? WorldObjectEntryType.Caravan : WorldObjectEntryType.Base,
                Tile = worldObject.Tile,
                Name = worldObject.LabelCap,
                LoginOwner = SessionClientController.My?.Login,
                FreeWeight = 999999
            };

            if (worldObject is Caravan caravan)
            {
                var transferables = GameUtils.GetAllThings(caravan, true, false).DistinctToTransferableOneWays();
                _stackPartsBuffer.Clear();

                for (int i = 0; i < transferables.Count; i++)
                {
                    var allCount = transferables[i].MaxCount;
                    var things = transferables[i].things;
                    for (int ti = 0; ti < things.Count; ti++)
                    {
                        int cnt = Mathf.Min(things[ti].stackCount, allCount);
                        allCount -= cnt;
                        _stackPartsBuffer.Add(new ThingCount(things[ti], cnt));
                        if (allCount <= 0) break;
                    }
                }

                worldObjectEntry.FreeWeight = CollectionsMassCalculator.Capacity(_stackPartsBuffer)
                    - CollectionsMassCalculator.MassUsage(_stackPartsBuffer, IgnorePawnsInventoryMode.Ignore, false, false);

                worldObjectEntry.MarketValue = 0f;
                worldObjectEntry.MarketValuePawn = 0f;
                for (int i = 0; i < _stackPartsBuffer.Count; i++)
                {
                    int count = _stackPartsBuffer[i].Count;
                    if (count > 0)
                    {
                        Thing thing = _stackPartsBuffer[i].Thing;
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
                        long nowTicks = DateTime.UtcNow.Ticks;
                        if (!LastForceRecountTicks.TryGetValue(map.uniqueID, out long lastRecountTicks))
                        {
                            LastForceRecountTicks[map.uniqueID] = nowTicks + (map.uniqueID % 10) * TimeSpan.TicksPerSecond;
                        }
                        else if (nowTicks - lastRecountTicks > RecountIntervalTicks)
                        {
                            LastForceRecountTicks[map.uniqueID] = nowTicks;
                            map.wealthWatcher.ForceRecount();
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

                    var allSpawned = map.mapPawns.AllPawnsSpawned;
                    bool hasEnemy = false;

                    for (int i = 0; i < allSpawned.Count; i++)
                    {
                        var p = allSpawned[i];
                        if (p == null) continue;

                        if (p.Faction == Faction.OfPlayer)
                        {
                            if (p.RaceProps?.Humanlike ?? false)
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

        public static void ApplyWorldObject(WorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                if (worldObjectEntry.LoginOwner == SessionClientController.My?.Login)
                {
                    int existingKey = -1;

                    if (MyServerToLocalId != null && MyServerToLocalId.TryGetValue(worldObjectEntry.PlaceServerId, out int mappedId) && WorldObjectEntrys != null && WorldObjectEntrys.ContainsKey(mappedId))
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
                        // 1. Точний пошук за клітинкою (Tile)
                        for (int i = 0; i < allWorldObjects.Count; i++)
                        {
                            var wo = allWorldObjects[i];
                            if (WorldObjectEntrys != null && !WorldObjectEntrys.ContainsKey(wo.ID)
                                && wo.Tile == worldObjectEntry.Tile
                                && (wo.Faction?.IsPlayer ?? false)
                                && ((wo is Caravan && worldObjectEntry.Type == WorldObjectEntryType.Caravan)
                                    || (wo is MapParent && !(wo is WorldObjectBaseOnline) && worldObjectEntry.Type == WorldObjectEntryType.Base)))
                            {
                                var id = wo.ID;
                                if (Loger.Enable) Loger.Log("SetMyID " + id + " ServerId " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                                WorldObjectEntrys.Add(id, worldObjectEntry);

                                if (MyServerToLocalId != null) MyServerToLocalId[worldObjectEntry.PlaceServerId] = id;
                                matched = true;
                                return;
                            }
                        }

                        // 2. Резервний пошук каравану за назвою (якщо караван щойно перейшов на сусідній тайл RoadConstructionLeg)
                        if (!matched && worldObjectEntry.Type == WorldObjectEntryType.Caravan)
                        {
                            for (int i = 0; i < allWorldObjects.Count; i++)
                            {
                                var wo = allWorldObjects[i];
                                if (wo is Caravan && (wo.Faction?.IsPlayer ?? false)
                                    && WorldObjectEntrys != null && !WorldObjectEntrys.ContainsKey(wo.ID)
                                    && string.Equals(wo.LabelCap, worldObjectEntry.Name, StringComparison.Ordinal))
                                {
                                    var id = wo.ID;
                                    worldObjectEntry.Tile = wo.Tile;
                                    if (Loger.Enable) Loger.Log("SetMyID (by name) " + id + " ServerId " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                                    WorldObjectEntrys.Add(id, worldObjectEntry);

                                    if (MyServerToLocalId != null) MyServerToLocalId[worldObjectEntry.PlaceServerId] = id;
                                    matched = true;
                                    return;
                                }
                            }
                        }

                        if (!matched)
                        {
                            if (Loger.Enable) Loger.Log("ToDel " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                            if (ToDelete != null) ToDelete.Add(worldObjectEntry);
                        }
                    }
                    else if (WorldObjectEntrys != null)
                    {
                        WorldObjectEntrys[existingKey] = worldObjectEntry;
                        if (MyServerToLocalId != null) MyServerToLocalId[worldObjectEntry.PlaceServerId] = existingKey;
                    }
                    return;
                }

                var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID) as CaravanOnline;

                if (worldObjectEntry.Type == WorldObjectEntryType.Base)
                {
                    for (int i = allWorldObjects.Count - 1; i >= 0; i--)
                    {
                        var cur = allWorldObjects[i];
                        if (cur.Tile == worldObjectEntry.Tile && cur != worldObject)
                        {
                            bool isStaleBaseOnline = cur is BaseOnline staleBase
                                && staleBase.OnlineWObject?.PlaceServerId != worldObjectEntry.PlaceServerId;

                            // Захист об'єктів будівництва доріг Roads of the Rim від видалення
                            bool isNpcObject = !(cur is Caravan) && !(cur is CaravanOnline)
                                && !RoadsOfTheRimPatch.IsRoadConstructionObject(cur)
                                && (cur.Faction == null || !cur.Faction.IsPlayer);

                            if (isStaleBaseOnline || isNpcObject)
                            {
                                if (Loger.Enable) Loger.Log("Remove " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name);
                                if (cur is BaseOnline sb && sb.OnlineWObject != null)
                                {
                                    OtherServerToLocalId?.Remove(sb.OnlineWObject.PlaceServerId);
                                    allWorldObjectsByID?.Remove(sb.ID);
                                }
                                Find.WorldObjects.Remove(cur);
                            }
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

                    if (OtherServerToLocalId != null) OtherServerToLocalId[worldObjectEntry.PlaceServerId] = worldObject.ID;
                    if (allWorldObjectsByID != null) allWorldObjectsByID[worldObject.ID] = worldObject;
                    if (Loger.Enable) Loger.Log("Add " + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name + " " + worldObjectEntry.LoginOwner);
                }
                else
                {
                    if (OtherServerToLocalId != null) OtherServerToLocalId[worldObjectEntry.PlaceServerId] = worldObject.ID;
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
            _neighborsBuffer.Clear();
            Find.WorldGrid.GetTileNeighbors(centralTile, _neighborsBuffer);
            for (int i = 0; i < _neighborsBuffer.Count; i++)
            {
                Find.WorldDebugDrawer.FlashTile(_neighborsBuffer[i], WorldMaterials.DebugTileRenderQueue, null, 999999);
            }
            Find.WorldDebugDrawer.FlashTile(centralTile, WorldMaterials.DebugTileRenderQueue, null, 999999);
        }

        public static void DeleteWorldObject(WorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            var worldObject = GetOtherByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID) as CaravanOnline;
            if (worldObject != null)
            {
                allWorldObjectsByID?.Remove(worldObject.ID);
                OtherServerToLocalId?.Remove(worldObjectEntry.PlaceServerId);

                if (allWorldObjects != null && allWorldObjects != Find.WorldObjects.AllWorldObjects)
                {
                    allWorldObjects.Remove(worldObject);
                }
                Find.WorldObjects.Remove(worldObject);
            }
        }

        public static void ApplyTradeWorldObject(TradeWorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                var worldObject = GetTradeByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID);

                if (worldObjectEntry.Type != TradeWorldObjectEntryType.TradeOrder)
                {
                    var storage = worldObjectEntry as TradeThingStorage;
                    bool isEmpty = storage?.Things == null || storage.Things.Count == 0;

                    bool hasSettlementOnTile = false;
                    for (int sIdx = 0; sIdx < allWorldObjects.Count; sIdx++)
                    {
                        var wo = allWorldObjects[sIdx];
                        if (wo is Settlement s && wo.Tile == worldObjectEntry.Tile && (s.Faction?.IsPlayer ?? false))
                        {
                            hasSettlementOnTile = true;
                            break;
                        }
                    }

                    if (hasSettlementOnTile || isEmpty)
                    {
                        for (int i = allWorldObjects.Count - 1; i >= 0; i--)
                        {
                            var wo = allWorldObjects[i];
                            if (wo is TradeThingsOnline tto && tto.Tile == worldObjectEntry.Tile)
                            {
                                allWorldObjectsByID?.Remove(tto.ID);
                                TradeServerToLocalId?.Remove(worldObjectEntry.PlaceServerId);

                                if (allWorldObjects != Find.WorldObjects.AllWorldObjects)
                                {
                                    allWorldObjects.RemoveAt(i);
                                }
                                Find.WorldObjects.Remove(tto);
                            }
                        }
                        return;
                    }
                }

                if (worldObject == null)
                {
                    if (worldObjectEntry.Type == TradeWorldObjectEntryType.TradeOrder)
                    {
                        worldObject = (WorldObjectBaseOnline)WorldObjectMaker.MakeWorldObject(ModDefOf.TradeOrdersOnline);
                        ((TradeOrdersOnline)worldObject).TradeOrders = new List<TradeOrderShort> { (TradeOrderShort)worldObjectEntry };
                        WorldObject_TradeOrdersOnline.Add((TradeOrdersOnline)worldObject);
                    }
                    else
                    {
                        worldObject = (WorldObjectBaseOnline)WorldObjectMaker.MakeWorldObject(ModDefOf.TradeThingsOnline);
                        ((TradeThingsOnline)worldObject).TradeThings = (TradeThingStorage)worldObjectEntry;
                    }

                    worldObject.SetFaction(Faction.OfPlayer);
                    worldObject.Tile = worldObjectEntry.Tile;
                    Find.WorldObjects.Add(worldObject);

                    if (TradeServerToLocalId != null) TradeServerToLocalId[worldObjectEntry.PlaceServerId] = worldObject.ID;
                    if (allWorldObjectsByID != null) allWorldObjectsByID[worldObject.ID] = worldObject;

                    if (Loger.Enable)
                    {
                        Loger.Log("Add " + (worldObjectEntry.Type == TradeWorldObjectEntryType.TradeOrder ? "TradeOrderShort greenApp " : "TradeThingStorage redApp ")
                            + worldObjectEntry.PlaceServerId + " " + worldObjectEntry.Name + " " + worldObjectEntry.LoginOwner);
                    }
                }
                else
                {
                    if (TradeServerToLocalId != null) TradeServerToLocalId[worldObjectEntry.PlaceServerId] = worldObject.ID;
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
            }
            catch (Exception ex)
            {
                Loger.Log("ApplyTradeWorldObject Exception: " + ex.Message, Loger.LogLevel.ERROR);
                throw;
            }
        }

        public static void DeleteTradeWorldObject(TradeWorldObjectEntry worldObjectEntry, ref List<WorldObject> allWorldObjects, ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            var worldObject = GetTradeByServerId(worldObjectEntry.PlaceServerId, allWorldObjectsByID);
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
                        allWorldObjectsByID?.Remove(worldObject.ID);
                        TradeServerToLocalId?.Remove(worldObjectEntry.PlaceServerId);

                        if (allWorldObjects != null && allWorldObjects != Find.WorldObjects.AllWorldObjects)
                        {
                            allWorldObjects.Remove(worldObject);
                        }
                        Find.WorldObjects.Remove(worldObject);
                        WorldObject_TradeOrdersOnline.Remove(worldObjectTO);
                    }
                }
                else
                {
                    allWorldObjectsByID?.Remove(worldObject.ID);
                    TradeServerToLocalId?.Remove(worldObjectEntry.PlaceServerId);

                    if (allWorldObjects != null && allWorldObjects != Find.WorldObjects.AllWorldObjects)
                    {
                        allWorldObjects.Remove(worldObject);
                    }
                    Find.WorldObjects.Remove(worldObject);
                }
            }
        }
        #endregion

        public static void InitGame()
        {
            WorldObjectEntrys = new Dictionary<int, WorldObjectEntry>();
            MyServerToLocalId = new Dictionary<long, int>();
            OtherServerToLocalId = new Dictionary<long, int>();
            TradeServerToLocalId = new Dictionary<long, int>();
            WorldObject_TradeOrdersOnline = new HashSet<TradeOrdersOnline>();
            ToDelete = new List<WorldObjectEntry>(8);
            LastCatchAllWorldObjectsByID = null;
            LastSendMyWorldObjects = null;
            LastWorldObjectOnline = null;
            LastFactionOnline = null;
            LastForceRecountTicks.Clear();

            // Скидання кешу доріг та проксі-майданчиків Roads of the Rim
            RoadsOfTheRimPatch.ClearCache();

            _presentIdsBuffer.Clear();
            _tmpMapBuffer.Clear();
            _patchMapBuffer.Clear();
            _neighborsBuffer.Clear();
            _stackPartsBuffer.Clear();
            _reusableOnlineWObjList.Clear();
            _reusableCurrentWObjKeySet.Clear();
            _reusableLastWObjKeySet.Clear();
            _reusableCurrentFactionKeySet.Clear();
            _reusableLastFactionKeySet.Clear();
            _reusableWObjDeleteKeys.Clear();
            _reusableFactionDeleteKeys.Clear();
        }

        #region Неігрові об'єкти планети
        private static void ApplyNonPlayerWorldObject(ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.WObjectOnlineToDelete != null && fromServ.WObjectOnlineToDelete.Count > 0)
                {
                    _reusableWObjDeleteKeys.Clear();
                    for (int i = 0; i < fromServ.WObjectOnlineToDelete.Count; i++)
                    {
                        var item = fromServ.WObjectOnlineToDelete[i];
                        _reusableWObjDeleteKeys.Add(new WorldObjectKey(item.Tile, item.Name));
                    }

                    var all = Find.WorldObjects.AllWorldObjects;
                    var objectToDelete = new List<WorldObject>();

                    for (int i = 0; i < all.Count; i++)
                    {
                        var wo = all[i];
                        if (wo is Settlement && wo.HasName && !wo.Faction.IsPlayer)
                        {
                            if (_reusableWObjDeleteKeys.Contains(new WorldObjectKey(wo.Tile, wo.LabelCap)))
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
                        LastWorldObjectOnline.RemoveAll(wo => _reusableWObjDeleteKeys.Contains(new WorldObjectKey(wo.Tile, wo.Name)));
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
        private static void ApplyFactionsToWorld(ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.FactionOnlineToDelete != null && fromServ.FactionOnlineToDelete.Count > 0)
                {
                    _reusableFactionDeleteKeys.Clear();
                    for (int i = 0; i < fromServ.FactionOnlineToDelete.Count; i++)
                    {
                        var fo = fromServ.FactionOnlineToDelete[i];
                        _reusableFactionDeleteKeys.Add(new FactionKey(fo.loadID, fo.DefName));
                    }

                    var allFactions = Find.FactionManager.AllFactionsListForReading;
                    var factionToDelete = new List<Faction>();

                    for (int i = 0; i < allFactions.Count; i++)
                    {
                        var f = allFactions[i];
                        if (!f.IsPlayer && _reusableFactionDeleteKeys.Contains(new FactionKey(f.loadID, f.def.defName)))
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
                        LastFactionOnline.RemoveAll(fo => _reusableFactionDeleteKeys.Contains(new FactionKey(fo.loadID, fo.DefName)));
                    }
                }

                if (fromServ.FactionOnlineToAdd != null && fromServ.FactionOnlineToAdd.Count > 0)
                {
                    var existingFactions = Find.FactionManager.AllFactionsListForReading;
                    _reusableCurrentFactionKeySet.Clear();
                    for (int i = 0; i < existingFactions.Count; i++)
                    {
                        var f = existingFactions[i];
                        _reusableCurrentFactionKeySet.Add(new FactionKey(f.loadID, f.def.defName));
                    }

                    for (int i = 0; i < fromServ.FactionOnlineToAdd.Count; i++)
                    {
                        var toAdd = fromServ.FactionOnlineToAdd[i];
                        try
                        {
                            var key = new FactionKey(toAdd.loadID, toAdd.DefName);
                            if (!_reusableCurrentFactionKeySet.Contains(key))
                            {
                                OCFactionManager.UpdateFactionIDS(fromServ.FactionOnlineList);
                                OCFactionManager.AddNewFaction(toAdd);
                                _reusableCurrentFactionKeySet.Add(key);
                            }
                        }
                        catch
                        {
                            Loger.Log("Error faction to add LabelCap >> " + toAdd.LabelCap, Loger.LogLevel.ERROR);
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