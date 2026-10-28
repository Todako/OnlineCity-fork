using HarmonyLib;
using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    /// <summary>
    /// Динамічний міст сумісності між OnlineCity та модом Roads of the Rim / Roads of the Rim (Continued).
    /// Працює через Reflection без жорстких посилань на DLL мода.
    /// Синхронізує:
    /// 1) Побудовані, модернізовані та видалені (ISR2G) ділянки доріг у Find.WorldGrid,
    /// 2) Перерахунок швидкості руху караванів (WorldPathGrid) і візуальний шар WorldLayer_Roads,
    /// 3) Будівельні майданчики (RoadConstructionSite), пунктирні лінії маршруту (RoadConstructionLeg)
    ///    та прогрес ресурсів у WorldObjectComp_ConstructionSite.
    /// </summary>
    internal static class RoadsOfTheRimPatch
    {
        private static bool _patched;
        private static bool _isApplyingFromServer;

        private static Type _siteType;
        private static Type _legType;
        private static Type _compSiteType;

        private static FieldInfo _siteRoadField;
        private static FieldInfo _siteLastTileField;
        private static FieldInfo _legSiteField;
        private static FieldInfo _legPrevField;
        private static FieldInfo _legNextField;

        private static FieldInfo _compLeftToDoField;
        private static FieldInfo _compCostsField;

        private static readonly object SyncLock = new object();
        private static readonly Dictionary<(int A, int B), WorldRoadOnline> PendingRoads = new Dictionary<(int A, int B), WorldRoadOnline>();
        private static readonly List<WorldRoadSiteOnline> PendingSites = new List<WorldRoadSiteOnline>();

        // Відстеження локальних майданчиків гравця
        private class LocalSiteTracker
        {
            public int LocalSiteId;
            public int LastTile;
            public string RoadDefName;
            public List<int> LegTiles = new List<int>();
            public string CompProgressData;
            public string LastSignature;
        }

        private static readonly Dictionary<int, LocalSiteTracker> LocalSitesById = new Dictionary<int, LocalSiteTracker>();
        private static readonly Dictionary<(int A, int B), string> WatchedTilePairRoadDefs = new Dictionary<(int A, int B), string>();
        private static readonly List<int> NeighborsBuffer = new List<int>(8);

        // Відстеження віддалених майданчиків інших гравців (проксі-об'єкти на карті)
        private class RemoteSiteProxy
        {
            public WorldRoadSiteOnline Data;
            public WorldObject SiteObj;
            public readonly List<WorldObject> LegObjs = new List<WorldObject>();
        }

        private static readonly Dictionary<string, RemoteSiteProxy> RemoteSitesByKey = new Dictionary<string, RemoteSiteProxy>(StringComparer.Ordinal);
        private static readonly HashSet<int> RemoteWorldObjectIds = new HashSet<int>();
        private static readonly List<WorldObject> TemporarilyRemovedForSave = new List<WorldObject>();

        public static void TryPatch(HarmonyLib.Harmony harmony)
        {
            if (_patched) return;
            _patched = true;

            try
            {
                // 1. Патч на стандартний метод прокладання доріг RimWorld
                var overlayMethod = AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.OverlayRoad), new[] { typeof(int), typeof(int), typeof(RoadDef) });
                if (overlayMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(RoadsOfTheRimPatch), nameof(OverlayRoad_Postfix));
                    harmony.Patch(overlayMethod, postfix: postfix);
                }

                // 2. Пошук класів Roads of the Rim у завантажених збірках
                _siteType = GenTypes.GetTypeInAnyAssembly("RoadsOfTheRim.RoadConstructionSite");
                _legType = GenTypes.GetTypeInAnyAssembly("RoadsOfTheRim.RoadConstructionLeg");
                _compSiteType = GenTypes.GetTypeInAnyAssembly("RoadsOfTheRim.WorldObjectComp_ConstructionSite");

                if (_siteType != null)
                {
                    _siteRoadField = FindFieldByNameOrType(_siteType, "roadToBuild", typeof(Def));
                    _siteLastTileField = AccessTools.Field(_siteType, "lastTile");
                }

                if (_legType != null)
                {
                    _legSiteField = AccessTools.Field(_legType, "site") ?? FindFieldByType(_legType, _siteType);
                    _legPrevField = AccessTools.Field(_legType, "previous");
                    _legNextField = AccessTools.Field(_legType, "next");
                }

                if (_compSiteType != null)
                {
                    _compLeftToDoField = AccessTools.Field(_compSiteType, "leftToDo");
                    _compCostsField = AccessTools.Field(_compSiteType, "costs");

                    // Додатково перехоплюємо метод завершення ділянки в RoadsOfTheRim, якщо він є
                    var finishWorkMethod = AccessTools.Method(_compSiteType, "finishWork")
                        ?? AccessTools.Method(_compSiteType, "FinishWork")
                        ?? AccessTools.Method(_compSiteType, " EndConstruction");
                    if (finishWorkMethod != null)
                    {
                        var prefix = new HarmonyMethod(typeof(RoadsOfTheRimPatch), nameof(CompFinishWork_Prefix));
                        var postfix = new HarmonyMethod(typeof(RoadsOfTheRimPatch), nameof(CompFinishWork_Postfix));
                        harmony.Patch(finishWorkMethod, prefix: prefix, postfix: postfix);
                    }
                }

                if (_siteType != null)
                {
                    Loger.Log("Client RoadsOfTheRimPatch initialized: Roads of the Rim detected.");
                }
            }
            catch (Exception ex)
            {
                Loger.Log("Client RoadsOfTheRimPatch.TryPatch warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        public static bool IsRoadConstructionObject(WorldObject wo)
        {
            if (wo == null) return false;
            if (_siteType != null && (_siteType.IsInstanceOfType(wo) || (_legType != null && _legType.IsInstanceOfType(wo))))
            {
                return true;
            }
            var typeName = wo.GetType().Name;
            return typeName == "RoadConstructionSite" || typeName == "RoadConstructionLeg";
        }

        public static void ClearCache()
        {
            lock (SyncLock)
            {
                PendingRoads.Clear();
                PendingSites.Clear();
                LocalSitesById.Clear();
                WatchedTilePairRoadDefs.Clear();
                RemoteSitesByKey.Clear();
                RemoteWorldObjectIds.Clear();
                TemporarilyRemovedForSave.Clear();
            }
        }

        #region Захист локального сейву від чужих проксі-майданчиків

        public static void OnBeforeGameSave()
        {
            try
            {
                TemporarilyRemovedForSave.Clear();
                if (Find.WorldObjects == null || RemoteSitesByKey.Count == 0) return;

                foreach (var kvp in RemoteSitesByKey)
                {
                    var proxy = kvp.Value;
                    if (proxy == null) continue;

                    for (int i = 0; i < proxy.LegObjs.Count; i++)
                    {
                        var leg = proxy.LegObjs[i];
                        if (leg != null && Find.WorldObjects.Contains(leg))
                        {
                            Find.WorldObjects.Remove(leg);
                            TemporarilyRemovedForSave.Add(leg);
                        }
                    }

                    if (proxy.SiteObj != null && Find.WorldObjects.Contains(proxy.SiteObj))
                    {
                        Find.WorldObjects.Remove(proxy.SiteObj);
                        TemporarilyRemovedForSave.Add(proxy.SiteObj);
                    }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch.OnBeforeGameSave warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        public static void OnAfterGameSave()
        {
            try
            {
                if (Find.WorldObjects == null || TemporarilyRemovedForSave.Count == 0) return;

                for (int i = 0; i < TemporarilyRemovedForSave.Count; i++)
                {
                    var wo = TemporarilyRemovedForSave[i];
                    if (wo != null && !Find.WorldObjects.Contains(wo))
                    {
                        Find.WorldObjects.Add(wo);
                    }
                }
                TemporarilyRemovedForSave.Clear();
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch.OnAfterGameSave warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        #endregion

        #region Перехоплення побудови доріг

        public static void OverlayRoad_Postfix(int fromTile, int toTile, RoadDef roadDef)
        {
            if (_isApplyingFromServer) return;
            if (fromTile == toTile || fromTile < 0 || toTile < 0) return;

            QueueRoadChange(fromTile, toTile, roadDef?.defName ?? string.Empty);
        }

        public static void CompFinishWork_Prefix(WorldObjectComp __instance, out int __state)
        {
            __state = __instance?.parent?.Tile ?? -1;
        }

        public static void CompFinishWork_Postfix(WorldObjectComp __instance, int __state)
        {
            if (_isApplyingFromServer) return;
            try
            {
                var site = __instance?.parent;
                if (site == null) return;

                int oldTile = __state;
                int newTile = site.Tile;
                if (oldTile >= 0 && newTile >= 0 && oldTile != newTile)
                {
                    InspectTilePairForRoadChange(oldTile, newTile);
                }
                else if (oldTile >= 0 && _siteLastTileField != null)
                {
                    if (_siteLastTileField.GetValue(site) is int lastTile && lastTile >= 0 && lastTile != oldTile)
                    {
                        InspectTilePairForRoadChange(lastTile, oldTile);
                    }
                }
            }
            catch { }
        }

        private static void QueueRoadChange(int tileA, int tileB, string roadDefName)
        {
            int a = Math.Min(tileA, tileB);
            int b = Math.Max(tileA, tileB);
            var key = (a, b);
            string normalizedDef = roadDefName ?? string.Empty;

            lock (SyncLock)
            {
                WatchedTilePairRoadDefs[key] = normalizedDef;
                PendingRoads[key] = new WorldRoadOnline
                {
                    FromTile = a,
                    ToTile = b,
                    RoadDefName = normalizedDef,
                    UpdateTime = DateTime.UtcNow
                };
            }
        }

        private static void InspectTilePairForRoadChange(int tileA, int tileB)
        {
            if (Find.WorldGrid == null || tileA < 0 || tileB < 0 || tileA == tileB) return;
            string currentRoad = GetRoadDefNameBetween(tileA, tileB);
            int a = Math.Min(tileA, tileB);
            int b = Math.Max(tileA, tileB);
            var key = (a, b);

            lock (SyncLock)
            {
                if (!WatchedTilePairRoadDefs.TryGetValue(key, out string previousRoad) || previousRoad != currentRoad)
                {
                    QueueRoadChange(a, b, currentRoad);
                }
            }
        }

        private static string GetRoadDefNameBetween(int tileA, int tileB)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tileA < 0 || tileB < 0 || tileA >= grid.TilesCount || tileB >= grid.TilesCount)
                return string.Empty;

            var tile = grid[tileA];
            if (tile?.potentialRoads == null) return string.Empty;

            RoadDef bestRoad = null;
            for (int i = 0; i < tile.potentialRoads.Count; i++)
            {
                var link = tile.potentialRoads[i];
                if (link.neighbor == tileB && link.road != null)
                {
                    if (bestRoad == null || link.road.priority >= bestRoad.priority)
                    {
                        bestRoad = link.road;
                    }
                }
            }
            return bestRoad?.defName ?? string.Empty;
        }

        private static void WatchTileAndNeighbors(int tile)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tile < 0 || tile >= grid.TilesCount) return;

            NeighborsBuffer.Clear();
            grid.GetTileNeighbors(tile, NeighborsBuffer);
            for (int i = 0; i < NeighborsBuffer.Count; i++)
            {
                int n = NeighborsBuffer[i];
                int a = Math.Min(tile, n);
                int b = Math.Max(tile, n);
                var key = (a, b);
                if (!WatchedTilePairRoadDefs.ContainsKey(key))
                {
                    WatchedTilePairRoadDefs[key] = GetRoadDefNameBetween(a, b);
                }
            }
        }

        private static void CheckAllWatchedTilePairs()
        {
            if (WatchedTilePairRoadDefs.Count == 0 || Find.WorldGrid == null) return;

            List<((int A, int B) Key, string NewRoad)> changes = null;
            foreach (var kvp in WatchedTilePairRoadDefs)
            {
                string nowRoad = GetRoadDefNameBetween(kvp.Key.A, kvp.Key.B);
                if (!string.Equals(nowRoad, kvp.Value, StringComparison.Ordinal))
                {
                    if (changes == null) changes = new List<((int A, int B), string)>();
                    changes.Add((kvp.Key, nowRoad));
                }
            }

            if (changes != null)
            {
                for (int i = 0; i < changes.Count; i++)
                {
                    var ch = changes[i];
                    QueueRoadChange(ch.Key.A, ch.Key.B, ch.NewRoad);
                }
            }
        }

        #endregion

        #region Відстеження будівельних майданчиків у головному потоці

        public static void TrackConstructionObjectsOnMainThread(List<WorldObject> allWorldObjects)
        {
            if (_siteType == null || allWorldObjects == null || Find.WorldGrid == null) return;

            try
            {
                var seenLocalSiteIds = new HashSet<int>();

                for (int i = 0; i < allWorldObjects.Count; i++)
                {
                    var wo = allWorldObjects[i];
                    if (wo == null || RemoteWorldObjectIds.Contains(wo.ID)) continue;

                    if (_siteType.IsInstanceOfType(wo))
                    {
                        seenLocalSiteIds.Add(wo.ID);
                        WatchTileAndNeighbors(wo.Tile);

                        var legTiles = ExtractOrderedLegTiles(wo, allWorldObjects);
                        for (int l = 0; l < legTiles.Count; l++)
                        {
                            WatchTileAndNeighbors(legTiles[l]);
                        }

                        string roadDefName = GetSiteRoadDefName(wo);
                        string compData = SerializeSiteCompProgress(wo);
                        string signature = $"{wo.Tile}|{roadDefName}|{string.Join(",", legTiles)}|{compData}";

                        if (!LocalSitesById.TryGetValue(wo.ID, out var tracker))
                        {
                            tracker = new LocalSiteTracker { LocalSiteId = wo.ID };
                            LocalSitesById[wo.ID] = tracker;
                        }

                        if (tracker.LastSignature != signature)
                        {
                            tracker.LastTile = wo.Tile;
                            tracker.RoadDefName = roadDefName;
                            tracker.LegTiles = legTiles;
                            tracker.CompProgressData = compData;
                            tracker.LastSignature = signature;

                            lock (SyncLock)
                            {
                                PendingSites.Add(new WorldRoadSiteOnline
                                {
                                    LocalSiteId = wo.ID,
                                    Tile = wo.Tile,
                                    RoadDefName = roadDefName,
                                    LegTiles = new List<int>(legTiles),
                                    CompProgressData = compData,
                                    IsDeleted = false,
                                    UpdateTime = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }

                // Перевіряємо, чи якісь локальні майданчики були завершені або скасовані
                if (LocalSitesById.Count > 0)
                {
                    List<int> removedIds = null;
                    foreach (var kvp in LocalSitesById)
                    {
                        if (!seenLocalSiteIds.Contains(kvp.Key))
                        {
                            if (removedIds == null) removedIds = new List<int>();
                            removedIds.Add(kvp.Key);
                        }
                    }

                    if (removedIds != null)
                    {
                        for (int i = 0; i < removedIds.Count; i++)
                        {
                            int id = removedIds[i];
                            var oldTracker = LocalSitesById[id];
                            LocalSitesById.Remove(id);

                            lock (SyncLock)
                            {
                                PendingSites.Add(new WorldRoadSiteOnline
                                {
                                    LocalSiteId = id,
                                    Tile = oldTracker.LastTile,
                                    RoadDefName = oldTracker.RoadDefName,
                                    IsDeleted = true,
                                    UpdateTime = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }

                // Перевіряємо всі контрольовані сусідні тайли на появу/модернізацію/видалення доріг
                CheckAllWatchedTilePairs();
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch.TrackConstructionObjectsOnMainThread warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        public static List<WorldRoadOnline> FlushPendingRoadsForServer()
        {
            lock (SyncLock)
            {
                if (PendingRoads.Count == 0) return null;
                var list = new List<WorldRoadOnline>(PendingRoads.Values);
                PendingRoads.Clear();
                return list;
            }
        }

        public static List<WorldRoadSiteOnline> FlushPendingRoadSitesForServer()
        {
            lock (SyncLock)
            {
                if (PendingSites.Count == 0) return null;
                var list = new List<WorldRoadSiteOnline>(PendingSites);
                PendingSites.Clear();
                return list;
            }
        }

        #endregion

        #region Застосування доріг та будівельних майданчиків із сервера

        public static void ApplyRoadsFromServer(List<WorldRoadOnline> roads)
        {
            if (roads == null || roads.Count == 0 || Find.WorldGrid == null) return;

            var grid = Find.WorldGrid;
            bool anyChanged = false;
            _isApplyingFromServer = true;

            try
            {
                for (int i = 0; i < roads.Count; i++)
                {
                    var r = roads[i];
                    if (r == null || r.FromTile < 0 || r.ToTile < 0 || r.FromTile == r.ToTile) continue;
                    if (r.FromTile >= grid.TilesCount || r.ToTile >= grid.TilesCount) continue;

                    int a = Math.Min(r.FromTile, r.ToTile);
                    int b = Math.Max(r.FromTile, r.ToTile);
                    string targetDefName = r.RoadDefName ?? string.Empty;

                    lock (SyncLock)
                    {
                        WatchedTilePairRoadDefs[(a, b)] = targetDefName;
                    }

                    string currentDefName = GetRoadDefNameBetween(a, b);
                    if (string.Equals(currentDefName, targetDefName, StringComparison.Ordinal))
                        continue;

                    if (string.IsNullOrEmpty(targetDefName) || string.Equals(targetDefName, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        RemoveRoadBetween(grid, a, b);
                        anyChanged = true;
                    }
                    else
                    {
                        var roadDef = DefDatabase<RoadDef>.GetNamedSilentFail(targetDefName);
                        if (roadDef != null)
                        {
                            SetExactRoadBetween(grid, a, b, roadDef);
                            anyChanged = true;
                        }
                    }

                    Find.WorldPathGrid?.RecalculatePerceivedMovementDifficultyAt(a);
                    Find.WorldPathGrid?.RecalculatePerceivedMovementDifficultyAt(b);
                }

                if (anyChanged)
                {
                    RegenerateRoadLayer();
                }
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch.ApplyRoadsFromServer error: " + ex.Message, Loger.LogLevel.WARNING);
            }
            finally
            {
                _isApplyingFromServer = false;
            }
        }

        public static void ApplyRoadSitesFromServer(List<WorldRoadSiteOnline> sites)
        {
            if (sites == null || sites.Count == 0 || _siteType == null || Find.WorldObjects == null) return;

            try
            {
                string myLogin = SessionClientController.My?.Login;
                for (int i = 0; i < sites.Count; i++)
                {
                    var s = sites[i];
                    if (s == null || string.IsNullOrEmpty(s.OwnerLogin) || s.OwnerLogin == myLogin) continue;

                    string key = s.OwnerLogin + "#" + s.LocalSiteId;
                    if (s.IsDeleted)
                    {
                        RemoveRemoteSiteProxy(key);
                        continue;
                    }

                    UpsertRemoteSiteProxy(key, s);
                }
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch.ApplyRoadSitesFromServer warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        private static void RemoveRemoteSiteProxy(string key)
        {
            if (!RemoteSitesByKey.TryGetValue(key, out var proxy)) return;
            RemoteSitesByKey.Remove(key);

            if (Find.WorldObjects != null)
            {
                for (int i = 0; i < proxy.LegObjs.Count; i++)
                {
                    var leg = proxy.LegObjs[i];
                    if (leg != null)
                    {
                        RemoteWorldObjectIds.Remove(leg.ID);
                        if (Find.WorldObjects.Contains(leg)) Find.WorldObjects.Remove(leg);
                    }
                }

                if (proxy.SiteObj != null)
                {
                    RemoteWorldObjectIds.Remove(proxy.SiteObj.ID);
                    if (Find.WorldObjects.Contains(proxy.SiteObj)) Find.WorldObjects.Remove(proxy.SiteObj);
                }
            }
        }

        private static void UpsertRemoteSiteProxy(string key, WorldRoadSiteOnline data)
        {
            RemoveRemoteSiteProxy(key);

            var siteDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("RoadConstructionSite");
            var legDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("RoadConstructionLeg");
            if (siteDef == null || data.Tile < 0) return;

            var siteObj = WorldObjectMaker.MakeWorldObject(siteDef);
            if (siteObj == null) return;

            siteObj.Tile = data.Tile;
            SetSiteRoadDef(siteObj, data.RoadDefName);
            Find.WorldObjects.Add(siteObj);
            RemoteWorldObjectIds.Add(siteObj.ID);

            ApplySiteCompProgress(siteObj, data.CompProgressData);

            var proxy = new RemoteSiteProxy
            {
                Data = data,
                SiteObj = siteObj
            };

            if (legDef != null && data.LegTiles != null && data.LegTiles.Count > 0 && _legType != null)
            {
                WorldObject prevObj = siteObj;
                for (int i = 0; i < data.LegTiles.Count; i++)
                {
                    int legTile = data.LegTiles[i];
                    if (legTile < 0) continue;

                    var legObj = WorldObjectMaker.MakeWorldObject(legDef);
                    if (legObj == null) continue;

                    legObj.Tile = legTile;
                    _legSiteField?.SetValue(legObj, siteObj);
                    _legPrevField?.SetValue(legObj, prevObj);

                    if (prevObj != siteObj)
                    {
                        _legNextField?.SetValue(prevObj, legObj);
                    }

                    Find.WorldObjects.Add(legObj);
                    RemoteWorldObjectIds.Add(legObj.ID);
                    proxy.LegObjs.Add(legObj);
                    prevObj = legObj;
                }
            }

            RemoteSitesByKey[key] = proxy;
        }

        #endregion

        #region Допоміжні методи роботи з WorldGrid та Reflection

        private static void RemoveRoadBetween(WorldGrid grid, int a, int b)
        {
            var tileA = grid[a];
            if (tileA?.potentialRoads != null)
            {
                tileA.potentialRoads.RemoveAll(link => link.neighbor == b);
            }

            var tileB = grid[b];
            if (tileB?.potentialRoads != null)
            {
                tileB.potentialRoads.RemoveAll(link => link.neighbor == a);
            }
        }

        private static void SetExactRoadBetween(WorldGrid grid, int a, int b, RoadDef roadDef)
        {
            RemoveRoadBetween(grid, a, b);

            var tileA = grid[a];
            if (tileA.potentialRoads == null) tileA.potentialRoads = new List<Tile.RoadLink>();
            tileA.potentialRoads.Add(new Tile.RoadLink { neighbor = b, road = roadDef });

            var tileB = grid[b];
            if (tileB.potentialRoads == null) tileB.potentialRoads = new List<Tile.RoadLink>();
            tileB.potentialRoads.Add(new Tile.RoadLink { neighbor = a, road = roadDef });
        }

        private static void RegenerateRoadLayer()
        {
            try
            {
                Find.World?.renderer?.SetDirty<WorldLayer_Roads>();
            }
            catch { }
        }

        private static List<int> ExtractOrderedLegTiles(WorldObject siteObj, List<WorldObject> allWorldObjects)
        {
            var result = new List<int>();
            if (_legType == null || _legSiteField == null) return result;

            var siteLegs = new List<WorldObject>();
            for (int i = 0; i < allWorldObjects.Count; i++)
            {
                var wo = allWorldObjects[i];
                if (wo != null && _legType.IsInstanceOfType(wo))
                {
                    var parentSite = _legSiteField.GetValue(wo) as WorldObject;
                    if (parentSite == siteObj)
                    {
                        siteLegs.Add(wo);
                    }
                }
            }

            if (siteLegs.Count == 0) return result;

            if (_legPrevField != null && _legNextField != null)
            {
                WorldObject firstLeg = null;
                for (int i = 0; i < siteLegs.Count; i++)
                {
                    var prev = _legPrevField.GetValue(siteLegs[i]) as WorldObject;
                    if (prev == siteObj || prev == null)
                    {
                        firstLeg = siteLegs[i];
                        break;
                    }
                }

                if (firstLeg != null)
                {
                    var visited = new HashSet<WorldObject>();
                    var cur = firstLeg;
                    while (cur != null && visited.Add(cur))
                    {
                        result.Add(cur.Tile);
                        cur = _legNextField.GetValue(cur) as WorldObject;
                    }
                    return result;
                }
            }

            for (int i = 0; i < siteLegs.Count; i++)
            {
                result.Add(siteLegs[i].Tile);
            }
            return result;
        }

        private static string GetSiteRoadDefName(WorldObject siteObj)
        {
            if (siteObj == null || _siteRoadField == null) return string.Empty;
            var def = _siteRoadField.GetValue(siteObj) as Def;
            return def?.defName ?? string.Empty;
        }

        private static void SetSiteRoadDef(WorldObject siteObj, string roadDefName)
        {
            if (siteObj == null || _siteRoadField == null || string.IsNullOrEmpty(roadDefName)) return;
            try
            {
                var fieldType = _siteRoadField.FieldType;
                var roadDef = GenDefDatabase.GetDefSilentFail(fieldType, roadDefName, false);
                if (roadDef != null)
                {
                    _siteRoadField.SetValue(siteObj, roadDef);
                }
            }
            catch { }
        }

        private static WorldObjectComp GetConstructionComp(WorldObject siteObj)
        {
            if (siteObj?.AllComps == null || _compSiteType == null) return null;
            for (int i = 0; i < siteObj.AllComps.Count; i++)
            {
                var c = siteObj.AllComps[i];
                if (c != null && _compSiteType.IsInstanceOfType(c))
                    return c;
            }
            return null;
        }

        private static string SerializeSiteCompProgress(WorldObject siteObj)
        {
            var comp = GetConstructionComp(siteObj);
            if (comp == null || _compLeftToDoField == null) return string.Empty;

            try
            {
                var leftDict = _compLeftToDoField.GetValue(comp) as IDictionary;
                var costDict = _compCostsField?.GetValue(comp) as IDictionary;
                if (leftDict == null || leftDict.Count == 0) return string.Empty;

                var sb = new StringBuilder(128);
                foreach (DictionaryEntry entry in leftDict)
                {
                    string k = entry.Key?.ToString();
                    if (string.IsNullOrEmpty(k)) continue;
                    int leftVal = Convert.ToInt32(entry.Value);
                    int costVal = (costDict != null && costDict.Contains(entry.Key)) ? Convert.ToInt32(costDict[entry.Key]) : leftVal;

                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(k).Append('=').Append(leftVal).Append(':').Append(costVal);
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ApplySiteCompProgress(WorldObject siteObj, string progressData)
        {
            if (string.IsNullOrEmpty(progressData)) return;
            var comp = GetConstructionComp(siteObj);
            if (comp == null || _compLeftToDoField == null) return;

            try
            {
                var leftDict = _compLeftToDoField.GetValue(comp) as IDictionary;
                var costDict = _compCostsField?.GetValue(comp) as IDictionary;
                if (leftDict == null) return;

                var pairs = progressData.Split(';');
                for (int i = 0; i < pairs.Length; i++)
                {
                    var p = pairs[i];
                    int eqIdx = p.IndexOf('=');
                    if (eqIdx <= 0) continue;

                    string k = p.Substring(0, eqIdx);
                    string valPart = p.Substring(eqIdx + 1);
                    int colonIdx = valPart.IndexOf(':');
                    if (colonIdx <= 0) continue;

                    if (int.TryParse(valPart.Substring(0, colonIdx), out int leftVal)
                        && int.TryParse(valPart.Substring(colonIdx + 1), out int costVal))
                    {
                        leftDict[k] = leftVal;
                        if (costDict != null) costDict[k] = costVal;
                    }
                }
            }
            catch { }
        }

        private static FieldInfo FindFieldByNameOrType(Type type, string fieldName, Type assignableBaseType)
        {
            var f = AccessTools.Field(type, fieldName);
            if (f != null) return f;

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (assignableBaseType.IsAssignableFrom(fields[i].FieldType))
                    return fields[i];
            }
            return null;
        }

        private static FieldInfo FindFieldByType(Type type, Type fieldType)
        {
            if (type == null || fieldType == null) return null;
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType == fieldType)
                    return fields[i];
            }
            return null;
        }

        #endregion
    }
}