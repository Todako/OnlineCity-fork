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
using System.Threading;
using Transfer;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    static class UpdateWorldController
    {
        /// <summary>
        /// Для пошуку об'єктів, які вже були створені раніше (PlaceServerId -> LocalId)
        /// </summary>
        private static Dictionary<long, int> ConverterServerId { get; set; }
        public static Dictionary<int, WorldObjectEntry> WorldObjectEntrys { get; private set; }
        private static List<WorldObjectEntry> ToDelete { get; set; }

        /// <summary>
        /// Список об'єктів TradeOrdersOnline для швидкого доступу
        /// </summary>
        public static HashSet<TradeOrdersOnline> WorldObject_TradeOrdersOnline { get; set; }

        private static List<WorldObjectEntry> LastSendMyWorldObjects { get; set; }
        private static List<WorldObjectOnline> LastWorldObjectOnline { get; set; }
        private static List<FactionOnline> LastFactionOnline { get; set; }

        private static Dictionary<int, WorldObjectBaseOnline> LastCatchAllWorldObjectsByID;

        // Кешовані колекції для усунення частих алокацій у GC
        private static readonly HashSet<int> ReusableExistingIds = new HashSet<int>();
        private static readonly HashSet<int> ReusableNpcTiles = new HashSet<int>();
        private static readonly HashSet<int> ReusableLastNpcTiles = new HashSet<int>();
        private static readonly HashSet<int> ReusableFactionIds = new HashSet<int>();

        public static bool ExistsEnemyPawns => gameProgress?.ExistsEnemyPawns == true;

        #region PrepareInMainThread

        public static List<WorldObject> allWorldObjects;
        public static List<WorldObjectEntry> WObjects;
        public static PlayerGameProgress gameProgress;

        private class CacheMap
        {
            public List<Pawn> Colonists;
            public bool ExistsEnemyPawns;
        }

        public static void PrepareInMainThread()
        {
            try
            {
                gameProgress = new PlayerGameProgress { Pawns = new List<PawnStat>() };

                allWorldObjects = GameUtils.GetAllWorldObjects();
                var cacheColonists = new Dictionary<Map, CacheMap>();
                var tmpMap = new Dictionary<WorldObjectEntry, Map>();
                WObjects = new List<WorldObjectEntry>();

                for (int i = 0; i < allWorldObjects.Count; i++)
                {
                    var o = allWorldObjects[i];

                    if ((o.Faction?.IsPlayer ?? false) &&
                        (o is Settlement || o is Caravan))
                    {
                        var oo = GetWorldObjectEntry(o, gameProgress, cacheColonists);

                        if (o is MapParent mapParent && mapParent.Map != null)
                        {
                            tmpMap.Add(oo, mapParent.Map);
                        }

                        WObjects.Add(oo);
                    }
                }

                float totalMarketValue = 0f;

                for (int i = 0; i < WObjects.Count; i++)
                {
                    totalMarketValue +=
                        WObjects[i].MarketValue +
                        WObjects[i].MarketValuePawn;
                }

                if (totalMarketValue > 0f)
                {
                    var cashlessBalance =
                        Math.Abs(SessionClientController.Data.CashlessBalance);

                    var storageBalance =
                        Math.Abs(SessionClientController.Data.StorageBalance);

                    for (int i = 0; i < WObjects.Count; i++)
                    {
                        var wo = WObjects[i];

                        var objValue =
                            wo.MarketValue +
                            wo.MarketValuePawn;

                        wo.MarketValueBalance =
                            cashlessBalance * objValue / totalMarketValue;

                        wo.MarketValueStorage =
                            storageBalance * objValue / totalMarketValue;
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

                var patchWealth = new Dictionary<Map, float>();

                var wealthMultiplier =
                    (float)SessionClientController.Data.GeneralSettings.ExchengePrecentWealthForIncident
                    / 1000f;

                for (int i = 0; i < WObjects.Count; i++)
                {
                    var o = WObjects[i];

                    if (o.Type == WorldObjectEntryType.Base &&
                        tmpMap.TryGetValue(o, out var map))
                    {
                        patchWealth[map] =
                            (o.MarketValueBalance + o.MarketValueStorage) *
                            wealthMultiplier;
                    }
                }

                MainTabWindow_DoStatisticsPage_Patch.PatchColonyWealth = patchWealth;
            }
            catch (Exception ex)
            {
                Loger.Log("Exception PrepareInMainThread " + ex);
            }
        }

        #endregion

        public static void SendToServer(
            ModelPlayToServer toServ,
            bool firstRun,
            ModelGameServerInfo modelGameServerInfo)
        {
            toServ.LastTick = (long)Find.TickManager.TicksGame;

            List<Faction> factionList =
                Find.FactionManager.AllFactionsListForReading;

            if (SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                #region Send to Server: firstRun EquableWorldObjects

                try
                {
                    if (firstRun && modelGameServerInfo != null)
                    {
                        if (modelGameServerInfo.WObjectOnlineList != null &&
                            modelGameServerInfo.WObjectOnlineList.Count > 0)
                        {
                            var wobjList = new List<WorldObjectOnline>();

                            for (int i = 0; i < allWorldObjects.Count; i++)
                            {
                                var wo = allWorldObjects[i];

                                if (wo is Settlement &&
                                    wo.HasName &&
                                    wo.Faction != null &&
                                    !wo.Faction.IsPlayer)
                                {
                                    wobjList.Add(GetWorldObjects(wo));
                                }
                            }

                            toServ.WObjectOnlineList = wobjList;
                        }

                        if (modelGameServerInfo.FactionOnlineList != null &&
                            modelGameServerInfo.FactionOnlineList.Count > 0)
                        {
                            var fList =
                                new List<FactionOnline>(factionList.Count);

                            for (int i = 0; i < factionList.Count; i++)
                            {
                                fList.Add(GetFactions(factionList[i]));
                            }

                            toServ.FactionOnlineList = fList;
                        }

                        return;
                    }
                }
                catch (Exception e)
                {
                    Loger.Log(
                        "Exception >> " + e,
                        Loger.LogLevel.ERROR);

                    Log.Error("SendToServer FirstRun error");
                    return;
                }

                #endregion
            }

            if (!firstRun)
            {
                toServ.WObjects = WObjects;
                LastSendMyWorldObjects = toServ.WObjects;

                if (ToDelete != null &&
                    WorldObjectEntrys != null)
                {
                    ReusableExistingIds.Clear();

                    for (int i = 0; i < allWorldObjects.Count; i++)
                    {
                        ReusableExistingIds.Add(
                            allWorldObjects[i].ID);
                    }

                    foreach (var p in WorldObjectEntrys)
                    {
                        if (!ReusableExistingIds.Contains(p.Key))
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
                #region Send to Server: Non-Player World Objects

                try
                {
                    var onlineWObjList = new List<WorldObject>();

                    for (int i = 0; i < allWorldObjects.Count; i++)
                    {
                        var wo = allWorldObjects[i];

                        if (wo is Settlement &&
                            wo.HasName &&
                            wo.Faction != null &&
                            !wo.Faction.IsPlayer)
                        {
                            onlineWObjList.Add(wo);
                        }
                    }

                    if (!firstRun &&
                        LastWorldObjectOnline != null &&
                        LastWorldObjectOnline.Count > 0)
                    {
                        ReusableNpcTiles.Clear();

                        for (int i = 0; i < onlineWObjList.Count; i++)
                        {
                            ReusableNpcTiles.Add(
                                onlineWObjList[i].Tile);
                        }

                        toServ.WObjectOnlineToDelete =
                            new List<WorldObjectOnline>();

                        for (int i = 0;
                             i < LastWorldObjectOnline.Count;
                             i++)
                        {
                            if (!ReusableNpcTiles.Contains(
                                LastWorldObjectOnline[i].Tile))
                            {
                                toServ.WObjectOnlineToDelete.Add(
                                    LastWorldObjectOnline[i]);
                            }
                        }

                        ReusableLastNpcTiles.Clear();

                        for (int i = 0;
                             i < LastWorldObjectOnline.Count;
                             i++)
                        {
                            ReusableLastNpcTiles.Add(
                                LastWorldObjectOnline[i].Tile);
                        }

                        toServ.WObjectOnlineToAdd =
                            new List<WorldObjectOnline>();

                        for (int i = 0;
                             i < onlineWObjList.Count;
                             i++)
                        {
                            if (!ReusableLastNpcTiles.Contains(
                                onlineWObjList[i].Tile))
                            {
                                toServ.WObjectOnlineToAdd.Add(
                                    GetWorldObjects(
                                        onlineWObjList[i]));
                            }
                        }
                    }

                    toServ.WObjectOnlineList =
                        new List<WorldObjectOnline>(
                            onlineWObjList.Count);

                    for (int i = 0;
                         i < onlineWObjList.Count;
                         i++)
                    {
                        toServ.WObjectOnlineList.Add(
                            GetWorldObjects(
                                onlineWObjList[i]));
                    }

                    LastWorldObjectOnline =
                        toServ.WObjectOnlineList;
                }
                catch (Exception e)
                {
                    Loger.Log("Exception >> " + e);
                    Log.Error(
                        "ERROR SendToServer WorldObject Online");
                }

                #endregion

                #region Send to Server: Non-Player Factions

                try
                {
                    if (!firstRun &&
                        LastFactionOnline != null &&
                        LastFactionOnline.Count > 0)
                    {
                        // Оптимізація: перевірка фракцій через HashSet O(1) замість LINQ
                        ReusableFactionIds.Clear();
                        for (int i = 0; i < factionList.Count; i++)
                        {
                            ReusableFactionIds.Add(factionList[i].loadID);
                        }

                        toServ.FactionOnlineToDelete = new List<FactionOnline>();
                        for (int i = 0; i < LastFactionOnline.Count; i++)
                        {
                            if (!ReusableFactionIds.Contains(LastFactionOnline[i].loadID))
                            {
                                toServ.FactionOnlineToDelete.Add(LastFactionOnline[i]);
                            }
                        }

                        var lastFactions = new HashSet<int>();
                        for (int i = 0; i < LastFactionOnline.Count; i++)
                        {
                            lastFactions.Add(LastFactionOnline[i].loadID);
                        }

                        toServ.FactionOnlineToAdd = new List<FactionOnline>();
                        for (int i = 0; i < factionList.Count; i++)
                        {
                            if (!lastFactions.Contains(factionList[i].loadID))
                            {
                                toServ.FactionOnlineToAdd.Add(GetFactions(factionList[i]));
                            }
                        }
                    }

                    toServ.FactionOnlineList =
                        new List<FactionOnline>(
                            factionList.Count);

                    for (int i = 0;
                         i < factionList.Count;
                         i++)
                    {
                        toServ.FactionOnlineList.Add(
                            GetFactions(
                                factionList[i]));
                    }

                    LastFactionOnline =
                        toServ.FactionOnlineList;
                }
                catch (Exception e)
                {
                    Loger.Log("Exception >> " + e);
                    Log.Error(
                        "ERROR SendToServer Faction Online");
                }

                #endregion
            }
        }

        public static void LoadFromServer(
            ModelPlayToClient fromServ,
            bool removeMissing)
        {
            if (SessionClientController.Data.GeneralSettings.EquableWorldObjects)
            {
                ApplyFactionsToWorld(fromServ);
                ApplyNonPlayerWorldObject(fromServ);
            }

            if (removeMissing)
            {
                var missingWObjects =
                    Find.WorldObjects.AllWorldObjects
                        .Where(o =>
                            o is CaravanOnline ||
                            o is WorldObjectBaseOnline)
                        .ToList();

                for (int i = 0;
                     i < missingWObjects.Count;
                     i++)
                {
                    Find.WorldObjects.Remove(
                        missingWObjects[i]);
                }

                Loger.Log(
                    "RemoveMissing " +
                    missingWObjects.Count);
            }

            ToDelete =
                new List<WorldObjectEntry>();

            List<WorldObject> catchAllWorldObjects =
                Find.WorldObjects.AllWorldObjects.ToList();

            var catchAllWorldObjectsByID =
                new Dictionary<int, WorldObjectBaseOnline>(
                    catchAllWorldObjects.Count);

            for (int i = 0;
                 i < catchAllWorldObjects.Count;
                 i++)
            {
                if (catchAllWorldObjects[i]
                    is WorldObjectBaseOnline wo &&
                    wo.ID != 0)
                {
                    catchAllWorldObjectsByID[wo.ID] = wo;
                }
            }

            if (fromServ.WObjects != null &&
                fromServ.WObjects.Count > 0)
            {
                for (int i = 0;
                     i < fromServ.WObjects.Count;
                     i++)
                {
                    ApplyWorldObject(
                        fromServ.WObjects[i],
                        ref catchAllWorldObjects,
                        ref catchAllWorldObjectsByID);
                }
            }

            if (fromServ.WObjectsToDelete != null &&
                fromServ.WObjectsToDelete.Count > 0)
            {
                for (int i = 0;
                     i < fromServ.WObjectsToDelete.Count;
                     i++)
                {
                    DeleteWorldObject(
                        fromServ.WObjectsToDelete[i],
                        ref catchAllWorldObjects,
                        ref catchAllWorldObjectsByID);
                }
            }

            if (fromServ.WTObjects != null &&
                fromServ.WTObjects.Count > 0)
            {
                for (int i = 0;
                     i < fromServ.WTObjects.Count;
                     i++)
                {
                    ApplyTradeWorldObject(
                        fromServ.WTObjects[i],
                        ref catchAllWorldObjects,
                        ref catchAllWorldObjectsByID);
                }
            }

            if (fromServ.WTObjectsToDelete != null &&
                fromServ.WTObjectsToDelete.Count > 0)
            {
                for (int i = 0;
                     i < fromServ.WTObjectsToDelete.Count;
                     i++)
                {
                    DeleteTradeWorldObject(
                        fromServ.WTObjectsToDelete[i],
                        ref catchAllWorldObjects,
                        ref catchAllWorldObjectsByID);
                }
            }

            LastCatchAllWorldObjectsByID =
                catchAllWorldObjectsByID;

            if (!removeMissing &&
                SessionClientController.Data.Players.ContainsKey(
                    SessionClientController.My.Login) &&
                LastSendMyWorldObjects != null)
            {
                var myWObjects =
                    new List<CaravanOnline>(
                        LastSendMyWorldObjects.Count);

                for (int i = 0;
                     i < LastSendMyWorldObjects.Count;
                     i++)
                {
                    var wo = LastSendMyWorldObjects[i];

                    if (wo.Type ==
                        WorldObjectEntryType.Base)
                    {
                        myWObjects.Add(
                            new BaseOnline
                            {
                                Tile = wo.Tile,
                                OnlineWObject = wo
                            });
                    }
                    else
                    {
                        myWObjects.Add(
                            new CaravanOnline
                            {
                                Tile = wo.Tile,
                                OnlineWObject = wo
                            });
                    }
                }

                SessionClientController
                    .Data.Players[
                        SessionClientController.My.Login]
                    .WObjects = myWObjects;
            }

            if (fromServ.Mails != null &&
                fromServ.Mails.Count > 0)
            {
                LongEventHandler.QueueLongEvent(
                    delegate
                    {
                        for (int i = 0;
                             i < fromServ.Mails.Count;
                             i++)
                        {
                            MailController.MailArrived(
                                fromServ.Mails[i]);
                        }
                    },
                    "",
                    false,
                    null);
            }
        }

        public static void ClearWorld()
        {
            var deleteWObjects =
                Find.WorldObjects.AllWorldObjects
                    .Where(o => o is CaravanOnline)
                    .ToList();

            for (int i = 0;
                 i < deleteWObjects.Count;
                 i++)
            {
                Find.WorldObjects.Remove(
                    deleteWObjects[i]);
            }
        }

        #region WorldObject

        public static int GetLocalIdByServerId(long serverId)
        {
            if (ConverterServerId == null ||
                !ConverterServerId.TryGetValue(
                    serverId,
                    out int objId))
            {
                return 0;
            }

            return objId;
        }

        public static WorldObjectEntry GetMyByServerId(long serverId)
        {
            if (ConverterServerId == null ||
                !ConverterServerId.TryGetValue(
                    serverId,
                    out int objId) ||
                WorldObjectEntrys == null ||
                !WorldObjectEntrys.TryGetValue(
                    objId,
                    out var storeWO))
            {
                return null;
            }

            return storeWO;
        }

        public static WorldObjectEntry GetMyByLocalId(int id)
        {
            if (WorldObjectEntrys == null ||
                !WorldObjectEntrys.TryGetValue(
                    id,
                    out var storeWO))
            {
                return null;
            }

            return storeWO;
        }

        public static WorldObjectBaseOnline GetOtherByServerIdDirtyRead(
            long serverId) =>
            GetOtherByServerId(
                serverId,
                LastCatchAllWorldObjectsByID);

        public static WorldObjectBaseOnline GetOtherByServerId(
            long serverId,
            Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID = null)
        {
            if (ConverterServerId == null ||
                !ConverterServerId.TryGetValue(
                    serverId,
                    out int objId))
            {
                return null;
            }

            if (allWorldObjectsByID == null)
            {
                var allObjs =
                    Find.WorldObjects.AllWorldObjects;

                for (int i = 0;
                     i < allObjs.Count;
                     i++)
                {
                    if (allObjs[i].ID == objId &&
                        allObjs[i] is WorldObjectBaseOnline baseOnline)
                    {
                        return baseOnline;
                    }
                }

                return null;
            }

            return allWorldObjectsByID.TryGetValue(
                objId,
                out var worldObject)
                ? worldObject
                : null;
        }

        public static WorldObject GetWOByServerId(
            long serverId,
            List<WorldObject> allWorldObjs = null)
        {
            if (ConverterServerId == null ||
                !ConverterServerId.TryGetValue(
                    serverId,
                    out int objId))
            {
                return null;
            }

            if (allWorldObjs == null)
            {
                allWorldObjs =
                    Find.WorldObjects.AllWorldObjects;
            }

            for (int i = 0;
                 i < allWorldObjs.Count;
                 i++)
            {
                if (allWorldObjs[i].ID == objId)
                {
                    return allWorldObjs[i];
                }
            }

            return null;
        }

        public static string GetTestText()
        {
            var text = "ConverterServerId.";

            foreach (var item in ConverterServerId)
            {
                text +=
                    Environment.NewLine +
                    item.Key +
                    ", " +
                    item.Value;
            }

            text +=
                Environment.NewLine +
                Environment.NewLine +
                "MyWorldObjectEntry.";

            foreach (var item in WorldObjectEntrys)
            {
                text +=
                    Environment.NewLine +
                    item.Key +
                    ", " +
                    item.Value.PlaceServerId +
                    " " +
                    item.Value.Name;
            }

            text +=
                Environment.NewLine +
                Environment.NewLine +
                "ToDelete.";

            foreach (var item in ToDelete)
            {
                text +=
                    Environment.NewLine +
                    item.PlaceServerId +
                    " " +
                    item.Name;
            }

            return text;
        }

        public static WorldObjectEntry GetServerInfo(
            WorldObject myWorldObject)
        {
            if (WorldObjectEntrys == null ||
                !WorldObjectEntrys.TryGetValue(
                    myWorldObject.ID,
                    out var storeWO))
            {
                return null;
            }

            return storeWO;
        }

        private static void GameProgressAdd(
            PlayerGameProgress gameProgress,
            Pawn pawn)
        {
            if (pawn.Dead)
                return;

            if (pawn.IsFreeColonist &&
                !pawn.IsPrisoner &&
                !pawn.IsPrisonerOfColony &&
                pawn.RaceProps.Humanlike)
            {
                gameProgress.ColonistsCount++;
                gameProgress.Pawns.Add(
                    PawnStat.CreateTrade(pawn));

                if (pawn.Downed)
                    gameProgress.ColonistsDownCount++;

                if (pawn.health.hediffSet.BleedRateTotal > 0)
                    gameProgress.ColonistsBleedCount++;

                if (pawn.health.HasHediffsNeedingTend())
                    gameProgress.ColonistsNeedingTend++;

                int maxSkill = 0;
                var skills = pawn.skills.skills;

                for (int i = 0;
                     i < skills.Count;
                     i++)
                {
                    if (skills[i].Level == 20)
                        maxSkill++;
                }

                if (maxSkill >= 8)
                    gameProgress.PawnMaxSkill++;

                var kh =
                    pawn.records.GetAsInt(
                        RecordDefOf.KillsHumanlikes);

                var km =
                    pawn.records.GetAsInt(
                        RecordDefOf.KillsMechanoids);

                gameProgress.KillsHumanlikes += kh;
                gameProgress.KillsMechanoids += km;

                if (gameProgress.KillsBestHumanlikesPawnName == null ||
                    kh > gameProgress.KillsBestHumanlikes)
                {
                    gameProgress.KillsBestHumanlikesPawnName =
                        pawn.LabelCapNoCount;

                    gameProgress.KillsBestHumanlikes = kh;
                }

                if (gameProgress.KillsBestMechanoidsPawnName == null ||
                    km > gameProgress.KillsBestMechanoids)
                {
                    gameProgress.KillsBestMechanoidsPawnName =
                        pawn.LabelCapNoCount;

                    gameProgress.KillsBestMechanoids = km;
                }
            }
            else if (pawn.RaceProps.Animal &&
                     pawn.training?.HasLearned(
                         TrainableDefOf.Obedience) == true)
            {
                gameProgress.AnimalObedienceCount++;
            }
        }

        /// <summary>
        /// Формування даних об'єкта карти для відправки на сервер
        /// </summary>
        private static WorldObjectEntry GetWorldObjectEntry(
            WorldObject worldObject,
            PlayerGameProgress gameProgress,
            Dictionary<Map, CacheMap> cacheColonists)
        {
            var worldObjectEntry = new WorldObjectEntry
            {
                Type = worldObject is Caravan
                    ? WorldObjectEntryType.Caravan
                    : WorldObjectEntryType.Base,

                Tile = worldObject.Tile,
                Name = worldObject.LabelCap,
                LoginOwner = SessionClientController.My.Login,
                FreeWeight = 999999
            };

            // Оптимізований розрахунок каравану без створення важких UI-обгорток
            if (worldObject is Caravan caravan)
            {
                worldObjectEntry.FreeWeight =
                    caravan.MassCapacity -
                    caravan.MassUsage;

                worldObjectEntry.MarketValue = 0f;
                worldObjectEntry.MarketValuePawn = 0f;

                var pawns =
                    caravan.PawnsListForReading;

                for (int i = 0;
                     i < pawns.Count;
                     i++)
                {
                    var p = pawns[i];

                    worldObjectEntry.MarketValuePawn +=
                        p.MarketValue;

                    GameProgressAdd(
                        gameProgress,
                        p);

                    // Підрахунок інвентарю
                    if (p.inventory?.innerContainer != null)
                    {
                        var container =
                            p.inventory.innerContainer;

                        for (int j = 0;
                             j < container.Count;
                             j++)
                        {
                            worldObjectEntry.MarketValue +=
                                container[j].MarketValue *
                                container[j].stackCount;
                        }
                    }

                    // Підрахунок одягу
                    if (p.apparel?.WornApparel != null)
                    {
                        var apparel = p.apparel.WornApparel;
                        for (int j = 0; j < apparel.Count; j++)
                        {
                            worldObjectEntry.MarketValue +=
                                apparel[j].MarketValue *
                                apparel[j].stackCount;
                        }
                    }

                    // Підрахунок спорядження/зброї
                    if (p.equipment != null)
                    {
                        var equipment = p.equipment.AllEquipmentListForReading;
                        for (int j = 0; j < equipment.Count; j++)
                        {
                            worldObjectEntry.MarketValue +=
                                equipment[j].MarketValue *
                                equipment[j].stackCount;
                        }
                    }
                }
            }
            else if (worldObject is Settlement settlement)
            {
                var map = settlement.Map;

                if (map != null)
                {
                    worldObjectEntry.MarketValue =
                        map.wealthWatcher.WealthTotal;

                    worldObjectEntry.MarketValuePawn = 0;

                    if (!cacheColonists.TryGetValue(
                        map,
                        out var ps))
                    {
                        var allSpawned =
                            map.mapPawns.AllPawnsSpawned;

                        ps = new CacheMap
                        {
                            Colonists =
                                new List<Pawn>(
                                    allSpawned.Count),

                            ExistsEnemyPawns = false
                        };

                        var playerFaction =
                            Faction.OfPlayer;

                        for (int i = 0;
                             i < allSpawned.Count;
                             i++)
                        {
                            var p = allSpawned[i];

                            if (p.Faction == playerFaction)
                            {
                                ps.Colonists.Add(p);
                            }
                            else if (!p.Dead &&
                                     !p.Downed &&
                                     !p.IsPrisoner &&
                                     p.Faction?.HostileTo(
                                         playerFaction) == true)
                            {
                                ps.ExistsEnemyPawns = true;
                            }
                        }

                        cacheColonists[map] = ps;
                    }

                    for (int i = 0;
                         i < ps.Colonists.Count;
                         i++)
                    {
                        var current =
                            ps.Colonists[i];

                        if (current.RaceProps.Humanlike)
                        {
                            worldObjectEntry.MarketValuePawn +=
                                current.MarketValue;
                        }

                        GameProgressAdd(
                            gameProgress,
                            current);
                    }

                    gameProgress.ExistsEnemyPawns |=
                        ps.ExistsEnemyPawns;
                }
            }

            if (WorldObjectEntrys != null &&
                WorldObjectEntrys.TryGetValue(
                    worldObject.ID,
                    out var storeWO))
            {
                worldObjectEntry.PlaceServerId =
                    storeWO.PlaceServerId;
            }

            return worldObjectEntry;
        }

        public static void ApplyWorldObject(
            WorldObjectEntry worldObjectEntry,
            ref List<WorldObject> allWorldObjects,
            ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                if (worldObjectEntry.LoginOwner ==
                    SessionClientController.My.Login)
                {
                    if (ConverterServerId.TryGetValue(
                            worldObjectEntry.PlaceServerId,
                            out int existingId) &&
                        WorldObjectEntrys.ContainsKey(existingId))
                    {
                        WorldObjectEntrys[existingId] =
                            worldObjectEntry;

                        return;
                    }

                    for (int i = 0;
                         i < allWorldObjects.Count;
                         i++)
                    {
                        var obj =
                            allWorldObjects[i];

                        if (!WorldObjectEntrys.ContainsKey(
                                obj.ID) &&
                            obj.Tile ==
                                worldObjectEntry.Tile &&
                            ((obj is Caravan &&
                              worldObjectEntry.Type ==
                                  WorldObjectEntryType.Caravan) ||
                             (obj is MapParent &&
                              worldObjectEntry.Type ==
                                  WorldObjectEntryType.Base)))
                        {
                            var id = obj.ID;

                            Loger.Log(
                                "SetMyID " +
                                id +
                                " ServerId " +
                                worldObjectEntry.PlaceServerId +
                                " " +
                                worldObjectEntry.Name);

                            WorldObjectEntrys.Add(
                                id,
                                worldObjectEntry);

                            ConverterServerId[
                                worldObjectEntry.PlaceServerId] =
                                id;

                            return;
                        }
                    }

                    Loger.Log(
                        "ToDel " +
                        worldObjectEntry.PlaceServerId +
                        " " +
                        worldObjectEntry.Name);

                    if (ToDelete != null)
                    {
                        ToDelete.Add(
                            worldObjectEntry);
                    }

                    return;
                }

                var worldObject =
                    GetOtherByServerId(
                        worldObjectEntry.PlaceServerId,
                        allWorldObjectsByID)
                    as CaravanOnline;

                if (worldObjectEntry.Type ==
                    WorldObjectEntryType.Base)
                {
                    for (int i = allWorldObjects.Count - 1;
                         i >= 0;
                         i--)
                    {
                        var obj =
                            allWorldObjects[i];

                        if (obj.Tile ==
                                worldObjectEntry.Tile &&
                            obj != worldObject &&
                            !(obj is Caravan) &&
                            !(obj is CaravanOnline) &&
                            (obj.Faction == null ||
                             !obj.Faction.IsPlayer))
                        {
                            Loger.Log(
                                "Remove " +
                                worldObjectEntry.PlaceServerId +
                                " " +
                                worldObjectEntry.Name);

                            Find.WorldObjects.Remove(obj);
                        }
                    }
                }

                if (worldObject == null)
                {
                    worldObject =
                        worldObjectEntry.Type ==
                            WorldObjectEntryType.Base
                        ? (CaravanOnline)
                            WorldObjectMaker.MakeWorldObject(
                                ModDefOf.BaseOnline)
                        : (CaravanOnline)
                            WorldObjectMaker.MakeWorldObject(
                                ModDefOf.CaravanOnline);

                    worldObject.SetFaction(
                        Faction.OfPlayer);

                    worldObject.Tile =
                        worldObjectEntry.Tile;

                    Find.WorldObjects.Add(
                        worldObject);

                    ConverterServerId.Add(
                        worldObjectEntry.PlaceServerId,
                        worldObject.ID);

                    allWorldObjectsByID.Add(
                        worldObject.ID,
                        worldObject);

                    allWorldObjects.Add(
                        worldObject);

                    Loger.Log(
                        "Add " +
                        worldObjectEntry.PlaceServerId +
                        " " +
                        worldObjectEntry.Name +
                        " " +
                        worldObjectEntry.LoginOwner);
                }
                else
                {
                    ConverterServerId[
                        worldObjectEntry.PlaceServerId] =
                        worldObject.ID;
                }

                worldObject.Tile =
                    worldObjectEntry.Tile;

                worldObject.OnlineWObject =
                    worldObjectEntry;
            }
            catch
            {
                Loger.Log(
                    "ApplyWorldObject Error");
                throw;
            }
        }

        public static void DrawTerritory(int centralTile)
        {
            var neighbors = new List<int>();

            Find.WorldGrid.GetTileNeighbors(
                centralTile,
                neighbors);

            for (int i = 0;
                 i < neighbors.Count;
                 i++)
            {
                Find.WorldDebugDrawer.FlashTile(
                    neighbors[i],
                    WorldMaterials.DebugTileRenderQueue,
                    null,
                    999999);
            }

            Find.WorldDebugDrawer.FlashTile(
                centralTile,
                WorldMaterials.DebugTileRenderQueue,
                null,
                999999);
        }

        public static void DeleteWorldObject(
            WorldObjectEntry worldObjectEntry,
            ref List<WorldObject> allWorldObjects,
            ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            var worldObject =
                GetOtherByServerId(
                    worldObjectEntry.PlaceServerId,
                    allWorldObjectsByID)
                as CaravanOnline;

            if (worldObject != null)
            {
                allWorldObjectsByID.Remove(
                    worldObject.ID);

                allWorldObjects.Remove(
                    worldObject);

                ConverterServerId.Remove(
                    worldObjectEntry.PlaceServerId);

                Find.WorldObjects.Remove(
                    worldObject);
            }
        }

        public static void ApplyTradeWorldObject(
            TradeWorldObjectEntry worldObjectEntry,
            ref List<WorldObject> allWorldObjects,
            ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            try
            {
                var worldObject =
                    GetOtherByServerId(
                        worldObjectEntry.PlaceServerId,
                        allWorldObjectsByID);

                if (worldObject == null)
                {
                    if (worldObjectEntry.Type ==
                        TradeWorldObjectEntryType.TradeOrder)
                    {
                        worldObject =
                            (WorldObjectBaseOnline)
                                WorldObjectMaker.MakeWorldObject(
                                    ModDefOf.TradeOrdersOnline);

                        ((TradeOrdersOnline)worldObject)
                            .TradeOrders =
                            new List<TradeOrderShort>
                            {
                                (TradeOrderShort)worldObjectEntry
                            };

                        WorldObject_TradeOrdersOnline.Add(
                            (TradeOrdersOnline)worldObject);

                        if (MainHelper.DebugMode)
                        {
                            Loger.Log(
                                "Client WorldObject_TradeOrdersOnline.Add Tile=" +
                                worldObject.Tile +
                                " load=" +
                                worldObjectEntry.Tile);
                        }
                    }
                    else
                    {
                        worldObject =
                            (WorldObjectBaseOnline)
                                WorldObjectMaker.MakeWorldObject(
                                    ModDefOf.TradeThingsOnline);

                        ((TradeThingsOnline)worldObject)
                            .TradeThings =
                            (TradeThingStorage)worldObjectEntry;
                    }

                    worldObject.SetFaction(
                        Faction.OfPlayer);

                    worldObject.Tile =
                        worldObjectEntry.Tile;

                    Find.WorldObjects.Add(
                        worldObject);

                    ConverterServerId.Add(
                        worldObjectEntry.PlaceServerId,
                        worldObject.ID);

                    allWorldObjectsByID.Add(
                        worldObject.ID,
                        worldObject);

                    allWorldObjects.Add(
                        worldObject);

                    Loger.Log(
                        "Add " +
                        (worldObjectEntry.Type ==
                            TradeWorldObjectEntryType.TradeOrder
                            ? "TradeOrderShort greenApp "
                            : "TradeThingStorage redApp") +
                        worldObjectEntry.PlaceServerId +
                        " " +
                        worldObjectEntry.Name +
                        " " +
                        worldObjectEntry.LoginOwner);
                }
                else
                {
                    ConverterServerId[
                        worldObjectEntry.PlaceServerId] =
                        worldObject.ID;

                    if (worldObjectEntry.Type ==
                        TradeWorldObjectEntryType.TradeOrder)
                    {
                        var worldObjectTO =
                            worldObject as TradeOrdersOnline;

                        int i = 0;

                        for (;
                             i < worldObjectTO.TradeOrders.Count;
                             i++)
                        {
                            if (worldObjectTO.TradeOrders[i].Id ==
                                worldObjectEntry.Id)
                            {
                                worldObjectTO.TradeOrders[i] =
                                    (TradeOrderShort)worldObjectEntry;

                                break;
                            }
                        }

                        if (i ==
                            worldObjectTO.TradeOrders.Count)
                        {
                            worldObjectTO.TradeOrders.Add(
                                (TradeOrderShort)worldObjectEntry);
                        }
                    }
                    else
                    {
                        ((TradeThingsOnline)worldObject)
                            .TradeThings =
                            (TradeThingStorage)worldObjectEntry;
                    }
                }

                worldObject.Tile =
                    worldObjectEntry.Tile;

                if (MainHelper.DebugMode)
                {
                    Loger.Log(
                        "Client WorldObject_TradeOrdersOnline Set Tile=" +
                        worldObject.Tile +
                        " load=" +
                        worldObjectEntry.Tile);
                }
            }
            catch
            {
                Loger.Log(
                    "ApplyTradeWorldObject Error",
                    Loger.LogLevel.ERROR);

                throw;
            }
        }

        public static void DeleteTradeWorldObject(
            TradeWorldObjectEntry worldObjectEntry,
            ref List<WorldObject> allWorldObjects,
            ref Dictionary<int, WorldObjectBaseOnline> allWorldObjectsByID)
        {
            var worldObject =
                GetOtherByServerId(
                    worldObjectEntry.PlaceServerId,
                    allWorldObjectsByID);

            if (worldObject != null)
            {
                if (worldObjectEntry.Type ==
                    TradeWorldObjectEntryType.TradeOrder)
                {
                    var worldObjectTO =
                        worldObject as TradeOrdersOnline;

                    for (int i = 0;
                         i < worldObjectTO.TradeOrders.Count;
                         i++)
                    {
                        if (worldObjectTO.TradeOrders[i].Id ==
                            worldObjectEntry.Id)
                        {
                            worldObjectTO.TradeOrders.RemoveAt(i);
                            break;
                        }
                    }

                    if (worldObjectTO.TradeOrders.Count == 0)
                    {
                        allWorldObjectsByID.Remove(
                            worldObject.ID);

                        allWorldObjects.Remove(
                            worldObject);

                        ConverterServerId.Remove(
                            worldObjectEntry.PlaceServerId);

                        Find.WorldObjects.Remove(
                            worldObject);

                        WorldObject_TradeOrdersOnline.Remove(
                            worldObjectTO);
                    }
                }
                else
                {
                    allWorldObjectsByID.Remove(
                        worldObject.ID);

                    allWorldObjects.Remove(
                        worldObject);

                    ConverterServerId.Remove(
                        worldObjectEntry.PlaceServerId);

                    Find.WorldObjects.Remove(
                        worldObject);
                }
            }
        }

        #endregion

        public static void InitGame()
        {
            WorldObjectEntrys =
                new Dictionary<int, WorldObjectEntry>();

            ConverterServerId =
                new Dictionary<long, int>();

            WorldObject_TradeOrdersOnline =
                new HashSet<TradeOrdersOnline>();

            ToDelete = null;
            LastCatchAllWorldObjectsByID = null;

            ReusableExistingIds.Clear();
            ReusableNpcTiles.Clear();
            ReusableLastNpcTiles.Clear();
            ReusableFactionIds.Clear();
        }

        #region Non-Player World Objects

        private static void ApplyNonPlayerWorldObject(
            ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.WObjectOnlineToDelete != null &&
                    fromServ.WObjectOnlineToDelete.Count > 0)
                {
                    var allObjs =
                        Find.WorldObjects.AllWorldObjects;

                    var objectToDelete =
                        new List<WorldObject>();

                    for (int i = 0;
                         i < allObjs.Count;
                         i++)
                    {
                        var wo = allObjs[i];

                        if (wo is Settlement &&
                            wo.HasName &&
                            wo.Faction != null &&
                            !wo.Faction.IsPlayer)
                        {
                            for (int j = 0;
                                 j < fromServ.WObjectOnlineToDelete.Count;
                                 j++)
                            {
                                if (ValidateOnlineWorldObject(
                                    fromServ.WObjectOnlineToDelete[j],
                                    wo))
                                {
                                    objectToDelete.Add(wo);
                                    break;
                                }
                            }
                        }
                    }

                    bool anyDestroyed = false;

                    // Оптимізація: пряме знищення через екземпляр об'єкта без зайвого SettlementAt
                    for (int i = 0;
                         i < objectToDelete.Count;
                         i++)
                    {
                        var obj = objectToDelete[i];
                        if (obj != null && !obj.Destroyed)
                        {
                            obj.Destroy();
                            anyDestroyed = true;
                        }
                    }

                    if (anyDestroyed)
                    {
                        Find.World.WorldUpdate();
                    }

                    // Оптимізація: швидке видалення через HashSet замість подвійного Any()
                    if (LastWorldObjectOnline != null &&
                        LastWorldObjectOnline.Count > 0)
                    {
                        var deletedTiles = new HashSet<int>();
                        for (int i = 0; i < objectToDelete.Count; i++)
                        {
                            deletedTiles.Add(objectToDelete[i].Tile);
                        }

                        LastWorldObjectOnline.RemoveAll(
                            wOnline => deletedTiles.Contains(wOnline.Tile));
                    }
                }

                if (fromServ.WObjectOnlineToAdd != null &&
                    fromServ.WObjectOnlineToAdd.Count > 0)
                {
                    var factions =
                        Find.FactionManager.AllFactionsListForReading;

                    for (int i = 0;
                         i < fromServ.WObjectOnlineToAdd.Count;
                         i++)
                    {
                        var toAdd =
                            fromServ.WObjectOnlineToAdd[i];

                        if (!Find.WorldObjects.AnySettlementAt(
                            toAdd.Tile))
                        {
                            Faction faction = null;

                            for (int fi = 0;
                                 fi < factions.Count;
                                 fi++)
                            {
                                if (factions[fi].def.LabelCap ==
                                        toAdd.FactionGroup &&
                                    factions[fi].loadID ==
                                        toAdd.loadID)
                                {
                                    faction =
                                        factions[fi];

                                    break;
                                }
                            }

                            if (faction != null)
                            {
                                var npcBase =
                                    (Settlement)
                                        WorldObjectMaker.MakeWorldObject(
                                            WorldObjectDefOf.Settlement);

                                npcBase.SetFaction(
                                    faction);

                                npcBase.Tile =
                                    toAdd.Tile;

                                npcBase.Name =
                                    toAdd.Name;

                                Find.WorldObjects.Add(
                                    npcBase);
                            }
                            else
                            {
                                Log.Warning(
                                    "Faction is missing or not found : " +
                                    toAdd.FactionGroup);

                                Loger.Log(
                                    "Skipping ToAdd Settlement : " +
                                    toAdd.Name);
                            }
                        }
                        else
                        {
                            Loger.Log(
                                "Can't Add Settlement. Tile is already occupied " +
                                Find.WorldObjects.SettlementAt(
                                    toAdd.Tile),
                                Loger.LogLevel.WARNING);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    "Exception LoadFromServer ApplyNonPlayerWorldObject >> " +
                    e);
            }
        }

        public static WorldObjectOnline GetWorldObjects(
            WorldObject obj)
        {
            return new WorldObjectOnline
            {
                Name = obj.LabelCap,
                Tile = obj.Tile,
                FactionGroup =
                    obj?.Faction?.def?.LabelCap,
                FactionDef =
                    obj?.Faction?.def?.defName,
                loadID =
                    obj?.Faction?.loadID ?? 0
            };
        }

        private static bool ValidateOnlineWorldObject(
            WorldObjectOnline WObjectOnline1,
            WorldObject WObjectOnline2)
        {
            return
                WObjectOnline1.Name ==
                    WObjectOnline2.LabelCap &&
                WObjectOnline1.Tile ==
                    WObjectOnline2.Tile;
        }

        #endregion

        #region Factions

        private static void ApplyFactionsToWorld(
            ModelPlayToClient fromServ)
        {
            try
            {
                if (fromServ.FactionOnlineToDelete != null &&
                    fromServ.FactionOnlineToDelete.Count > 0)
                {
                    var allFactions =
                        Find.FactionManager.AllFactionsListForReading;

                    var factionToDelete =
                        new List<Faction>();

                    for (int i = 0;
                         i < allFactions.Count;
                         i++)
                    {
                        var f = allFactions[i];

                        if (!f.IsPlayer)
                        {
                            for (int j = 0;
                                 j < fromServ.FactionOnlineToDelete.Count;
                                 j++)
                            {
                                if (ValidateFaction(
                                    fromServ.FactionOnlineToDelete[j],
                                    f))
                                {
                                    factionToDelete.Add(f);
                                    break;
                                }
                            }
                        }
                    }

                    OCFactionManager.UpdateFactionIDS(
                        fromServ.FactionOnlineList);

                    for (int i = 0;
                         i < factionToDelete.Count;
                         i++)
                    {
                        OCFactionManager.DeleteFaction(
                            factionToDelete[i]);
                    }

                    // Оптимізація: видалення через HashSet за loadID замість LINQ Any
                    if (LastFactionOnline != null &&
                        LastFactionOnline.Count > 0)
                    {
                        var deletedIds = new HashSet<int>();
                        for (int i = 0; i < factionToDelete.Count; i++)
                        {
                            deletedIds.Add(factionToDelete[i].loadID);
                        }

                        LastFactionOnline.RemoveAll(
                            fOnline => deletedIds.Contains(fOnline.loadID));
                    }
                }

                if (fromServ.FactionOnlineToAdd != null &&
                    fromServ.FactionOnlineToAdd.Count > 0)
                {
                    var allFactions =
                        Find.FactionManager.AllFactionsListForReading;

                    for (int i = 0;
                         i < fromServ.FactionOnlineToAdd.Count;
                         i++)
                    {
                        var toAdd =
                            fromServ.FactionOnlineToAdd[i];

                        try
                        {
                            bool exists = false;

                            for (int fi = 0;
                                 fi < allFactions.Count;
                                 fi++)
                            {
                                if (ValidateFaction(
                                    toAdd,
                                    allFactions[fi]))
                                {
                                    exists = true;
                                    break;
                                }
                            }

                            if (!exists)
                            {
                                OCFactionManager.UpdateFactionIDS(
                                    fromServ.FactionOnlineList);

                                OCFactionManager.AddNewFaction(
                                    toAdd);
                            }
                            else
                            {
                                Loger.Log(
                                    "Failed to add faction. Faction already exists. > " +
                                    toAdd.LabelCap,
                                    Loger.LogLevel.ERROR);
                            }
                        }
                        catch
                        {
                            Loger.Log(
                                "Error faction to add LabelCap >> " +
                                toAdd.LabelCap,
                                Loger.LogLevel.ERROR);

                            Loger.Log(
                                "Error faction to add DefName >> " +
                                toAdd.DefName,
                                Loger.LogLevel.ERROR);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    "OnlineCity: Error Apply new faction to world >> " +
                    e);
            }
        }

        public static FactionOnline GetFactions(
            Faction obj)
        {
            return new FactionOnline
            {
                Name = obj.Name,
                LabelCap = obj.def.LabelCap,
                DefName = obj.def.defName,
                loadID = obj.loadID
            };
        }

        private static bool ValidateFaction(
            FactionOnline fOnline1,
            Faction fOnline2)
        {
            return
                fOnline1.LabelCap ==
                    fOnline2.def.LabelCap &&
                fOnline1.DefName ==
                    fOnline2.def.defName &&
                fOnline1.loadID ==
                    fOnline2.loadID;
        }

        #endregion
    }
}
