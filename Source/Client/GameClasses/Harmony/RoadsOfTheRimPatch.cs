using HarmonyLib;
using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    /// <summary>
    /// Динамічний патч сумісності з модом Roads of the Rim (Continued) (Mlie.RoadsOfTheRim).
    /// Синхронізує заплановані маршрути (RoadConstructionLeg), активні будівельні майданчики (RoadConstructionSite),
    /// словники прогресу будівництва та прогрес-бар (WorldObjectComp_ConstructionSite) і побудовані дороги (WorldGrid).
    /// </summary>
    internal static class RoadsOfTheRimPatch
    {
        public static bool IsModActive { get; private set; }

        private static Type _siteType;
        private static Type _legType;
        private static Type _siteCompType;

        private static FieldInfo _siteRoadToBuildField;
        private static FieldInfo _siteProgressBarMatField;
        private static MethodInfo _siteUpdateProgressBarMatMethod;
        private static Action<WorldObject> _siteUpdateProgressBarMatAction;

        private static FieldInfo _legSiteField;
        private static FieldInfo _legPrevField;
        private static FieldInfo _legNextField;

        private static readonly List<FieldInfo> _compDictFields = new List<FieldInfo>();
        private static readonly List<(MethodInfo Method, Func<object, float> FastGetter)> _progressGetters =
            new List<(MethodInfo, Func<object, float>)>();
        private static MethodInfo _compSetCostsMethod;
        private static Action<object> _compSetCostsAction;

        private static Func<object, WorldObject> _getLegSite;
        private static Func<object, WorldObject> _getLegPrev;
        private static Func<object, WorldObject> _getLegNext;
        private static Action<object, WorldObject> _setLegSite;
        private static Action<object, WorldObject> _setLegPrev;
        private static Action<object, WorldObject> _setLegNext;
        private static Func<object, Def> _getSiteRoadToBuild;
        private static Action<object, Def> _setSiteRoadToBuild;

        private static readonly List<WorldObject> _mySitesBuffer = new List<WorldObject>(16);
        private static readonly Dictionary<WorldObject, List<WorldObject>> _legsBySiteBuffer =
            new Dictionary<WorldObject, List<WorldObject>>(16);
        private static readonly Stack<List<WorldObject>> _legListPool = new Stack<List<WorldObject>>(16);
        private static readonly HashSet<int> _currentMySiteIdsBuffer = new HashSet<int>();
        private static readonly List<int> _orderedLegTilesBuffer = new List<int>(16);
        private static readonly HashSet<WorldObject> _legSetBuffer = new HashSet<WorldObject>();
        private static readonly HashSet<WorldObject> _visitedLegsBuffer = new HashSet<WorldObject>();
        private static readonly StringBuilder _signatureSb = new StringBuilder(256);
        private static readonly StringBuilder _progressSb = new StringBuilder(192);
        private static readonly List<int> _watchedTilesListBuffer = new List<int>(32);
        private static readonly List<int> _removedIdsBuffer = new List<int>(8);

        private static bool _isApplyingFromServer;
        private static bool _isExtractingLocalProgress;

        private static readonly object SyncLock = new object();
        private static readonly Dictionary<(int From, int To), string> PendingRoads = new Dictionary<(int From, int To), string>();
        private static readonly Dictionary<(int From, int To), string> KnownLocalRoadLinks = new Dictionary<(int From, int To), string>();
        private static readonly HashSet<int> WatchedConstructionTiles = new HashSet<int>();
        private static readonly List<int> NeighborsBuffer = new List<int>(8);

        private class LocalSiteSnapshot
        {
            public int Tile;
            public string Signature;
        }
        private static readonly Dictionary<int, LocalSiteSnapshot> KnownMySitesSnapshot = new Dictionary<int, LocalSiteSnapshot>();
        private static readonly List<WorldRoadSiteOnline> PendingSitesToSync = new List<WorldRoadSiteOnline>();

        private class RemoteSiteProxy
        {
            public string Owner;
            public WorldObject SiteObject;
            public readonly List<WorldObject> LegObjects = new List<WorldObject>();
            public string LastLegSignature = "";
            public float ProgressPct = 0f;
            public float WorkDone = 0f;
            public float WorkTotal = 0f;
        }

        private static readonly Dictionary<(string Owner, int Id), RemoteSiteProxy> RemoteProxies =
            new Dictionary<(string Owner, int Id), RemoteSiteProxy>();

        private static readonly Dictionary<int, RemoteSiteProxy> RemoteProxyByWorldObjectId =
            new Dictionary<int, RemoteSiteProxy>();

        private static readonly List<WorldObject> DetachedBeforeSave = new List<WorldObject>();

        public static void TryPatch(HarmonyLib.Harmony harmony)
        {
            try
            {
                _siteType = AccessTools.TypeByName("RoadsOfTheRim.RoadConstructionSite");
                _legType = AccessTools.TypeByName("RoadsOfTheRim.RoadConstructionLeg");
                _siteCompType = AccessTools.TypeByName("RoadsOfTheRim.WorldObjectComp_ConstructionSite");

                if (_siteType == null || _legType == null)
                {
                    IsModActive = false;
                    return;
                }

                IsModActive = true;

                _siteRoadToBuildField = AccessTools.Field(_siteType, "roadDef")
                    ?? AccessTools.Field(_siteType, "roadToBuild");
                if (_siteRoadToBuildField == null)
                {
                    foreach (var f in _siteType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (typeof(Def).IsAssignableFrom(f.FieldType))
                        {
                            _siteRoadToBuildField = f;
                            break;
                        }
                    }
                }

                _siteProgressBarMatField = AccessTools.Field(_siteType, "ProgressBarMaterial");
                _siteUpdateProgressBarMatMethod = AccessTools.Method(_siteType, "UpdateProgressBarMaterial");
                if (_siteUpdateProgressBarMatMethod != null && _siteUpdateProgressBarMatMethod.GetParameters().Length == 0)
                {
                    try
                    {
                        var del = AccessTools.MethodDelegate<Action<object>>(_siteUpdateProgressBarMatMethod);
                        if (del != null) _siteUpdateProgressBarMatAction = del;
                    }
                    catch { _siteUpdateProgressBarMatAction = null; }
                }

                _legSiteField = AccessTools.Field(_legType, "site");
                _legPrevField = AccessTools.Field(_legType, "previous");
                _legNextField = AccessTools.Field(_legType, "next");

                _getLegSite = BuildObjectGetter<WorldObject>(_legSiteField);
                _getLegPrev = BuildObjectGetter<WorldObject>(_legPrevField);
                _getLegNext = BuildObjectGetter<WorldObject>(_legNextField);
                _setLegSite = BuildObjectSetter<WorldObject>(_legSiteField);
                _setLegPrev = BuildObjectSetter<WorldObject>(_legPrevField);
                _setLegNext = BuildObjectSetter<WorldObject>(_legNextField);
                _getSiteRoadToBuild = BuildObjectGetter<Def>(_siteRoadToBuildField);
                _setSiteRoadToBuild = BuildObjectSetter<Def>(_siteRoadToBuildField);

                _compDictFields.Clear();
                _progressGetters.Clear();
                _compSetCostsAction = null;

                if (_siteCompType != null)
                {
                    foreach (var f in _siteCompType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (typeof(IDictionary).IsAssignableFrom(f.FieldType))
                        {
                            _compDictFields.Add(f);
                        }
                    }

                    _compSetCostsMethod = AccessTools.Method(_siteCompType, "setCosts")
                        ?? AccessTools.Method(_siteCompType, "SetCosts");
                    if (_compSetCostsMethod != null && _compSetCostsMethod.GetParameters().Length == 0)
                    {
                        try
                        {
                            var del = AccessTools.MethodDelegate<Action<object>>(_compSetCostsMethod);
                            if (del != null) _compSetCostsAction = del;
                        }
                        catch { _compSetCostsAction = null; }
                    }

                    var progDescMethod = AccessTools.Method(_siteCompType, "ProgressDescription");
                    var progDescPrefix = AccessTools.Method(typeof(RoadsOfTheRimPatch), nameof(ProgressDescription_Prefix));
                    if (progDescMethod != null && progDescPrefix != null)
                    {
                        harmony.Patch(progDescMethod, prefix: new HarmonyMethod(progDescPrefix));
                    }
                }

                var drawMethod = AccessTools.Method(_siteType, "Draw");
                var drawPrefix = AccessTools.Method(typeof(RoadsOfTheRimPatch), nameof(SiteDraw_Prefix));
                if (drawMethod != null && drawPrefix != null)
                {
                    harmony.Patch(drawMethod, prefix: new HarmonyMethod(drawPrefix));
                }

                var overlayMethod = AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.OverlayRoad));
                var overlayPostfix = AccessTools.Method(typeof(RoadsOfTheRimPatch), nameof(OverlayRoad_Postfix));
                if (overlayMethod != null && overlayPostfix != null)
                {
                    harmony.Patch(overlayMethod, postfix: new HarmonyMethod(overlayPostfix));
                }

                var getGizmosMethod = AccessTools.Method(_siteType, "GetGizmos");
                var getGizmosPostfix = AccessTools.Method(typeof(RoadsOfTheRimPatch), nameof(SiteGetGizmos_Postfix));
                if (getGizmosMethod != null && getGizmosPostfix != null)
                {
                    harmony.Patch(getGizmosMethod, postfix: new HarmonyMethod(getGizmosPostfix));
                }

                PatchProgressMethods(harmony, _siteType);
                if (_siteCompType != null)
                {
                    PatchProgressMethods(harmony, _siteCompType);
                }

                Loger.Log($"Client RoadsOfTheRim compatibility patch activated (roadField={_siteRoadToBuildField?.Name}, dictFields={_compDictFields.Count}, progressGetters={_progressGetters.Count}).");
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch TryPatch Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        public static void SiteDraw_Prefix(WorldObject __instance)
        {
            if (__instance != null && TryGetRemoteProxy(__instance, out var proxy))
            {
                RefreshProxyVisualMaterial(__instance, proxy);
            }
        }

        private static void RefreshProxyVisualMaterial(WorldObject siteObj, RemoteSiteProxy proxy)
        {
            if (siteObj == null || proxy == null) return;
            try
            {
                // Гарантуємо актуальність словників costs/left перед викликом UpdateProgressBarMaterial
                if (_siteCompType != null)
                {
                    var comp = siteObj.GetComponent(_siteCompType);
                    if (comp != null)
                    {
                        EnsureWorkEntriesInCompDictionaries(comp, proxy);
                    }
                }

                if (_siteProgressBarMatField != null)
                {
                    _siteProgressBarMatField.SetValue(siteObj, null);
                }

                if (_siteUpdateProgressBarMatAction != null)
                {
                    _siteUpdateProgressBarMatAction(siteObj);
                }
                else if (_siteUpdateProgressBarMatMethod != null && _siteUpdateProgressBarMatMethod.GetParameters().Length == 0)
                {
                    _siteUpdateProgressBarMatMethod.Invoke(siteObj, null);
                }
            }
            catch { }
        }

        private static void EnsureWorkEntriesInCompDictionaries(object comp, RemoteSiteProxy proxy)
        {
            if (comp == null || proxy == null || _compDictFields.Count == 0) return;

            float total = proxy.WorkTotal > 0f ? proxy.WorkTotal : 100f;
            float leftVal = Math.Max(0f, total - Math.Max(0f, Math.Min(total, proxy.ProgressPct * total)));

            for (int f = 0; f < _compDictFields.Count; f++)
            {
                var field = _compDictFields[f];
                var dict = field.GetValue(comp) as IDictionary;
                if (dict == null) continue;

                Type valType = typeof(float);
                if (field.FieldType.IsGenericType)
                {
                    var args = field.FieldType.GetGenericArguments();
                    if (args.Length >= 2) valType = args[1];
                }

                bool isCost = field.Name.IndexOf("cost", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isLeft = field.Name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isCost)
                {
                    dict["Work"] = valType == typeof(int) ? (object)(int)Math.Round(total) : total;
                }
                else if (isLeft)
                {
                    dict["Work"] = valType == typeof(int) ? (object)(int)Math.Round(leftVal) : leftVal;
                }
            }
        }

        private static Func<object, T> BuildObjectGetter<T>(FieldInfo field)
        {
            if (field == null) return null;
            try
            {
                var method = new DynamicMethod(
                    "OC_Get_" + field.DeclaringType.Name + "_" + field.Name,
                    typeof(T),
                    new[] { typeof(object) },
                    field.DeclaringType,
                    true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, field.DeclaringType);
                il.Emit(OpCodes.Ldfld, field);
                if (field.FieldType.IsValueType && typeof(T) == typeof(object))
                {
                    il.Emit(OpCodes.Box, field.FieldType);
                }
                il.Emit(OpCodes.Ret);
                return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
            }
            catch
            {
                return target => target != null ? (T)field.GetValue(target) : default;
            }
        }

        private static Action<object, T> BuildObjectSetter<T>(FieldInfo field)
        {
            if (field == null || field.IsInitOnly) return null;
            try
            {
                var method = new DynamicMethod(
                    "OC_Set_" + field.DeclaringType.Name + "_" + field.Name,
                    null,
                    new[] { typeof(object), typeof(T) },
                    field.DeclaringType,
                    true);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, field.DeclaringType);
                il.Emit(OpCodes.Ldarg_1);
                if (typeof(T) == typeof(object) && field.FieldType.IsValueType)
                {
                    il.Emit(OpCodes.Unbox_Any, field.FieldType);
                }
                else if (typeof(T) != field.FieldType && !field.FieldType.IsAssignableFrom(typeof(T)))
                {
                    il.Emit(OpCodes.Castclass, field.FieldType);
                }
                il.Emit(OpCodes.Stfld, field);
                il.Emit(OpCodes.Ret);
                return (Action<object, T>)method.CreateDelegate(typeof(Action<object, T>));
            }
            catch
            {
                return (target, val) => { if (target != null) field.SetValue(target, val); };
            }
        }

        private static Func<object, float> BuildFloatGetter(MethodInfo method)
        {
            if (method == null || method.ReturnType != typeof(float) || method.GetParameters().Length != 0)
                return null;
            try
            {
                var dm = new DynamicMethod(
                    "OC_CallFloat_" + method.DeclaringType.Name + "_" + method.Name,
                    typeof(float),
                    new[] { typeof(object) },
                    method.DeclaringType,
                    true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, method.DeclaringType);
                il.Emit(OpCodes.Callvirt, method);
                il.Emit(OpCodes.Ret);
                return (Func<object, float>)dm.CreateDelegate(typeof(Func<object, float>));
            }
            catch
            {
                return target => target != null ? (float)method.Invoke(target, null) : 0f;
            }
        }

        private static void PatchProgressMethods(HarmonyLib.Harmony harmony, Type targetType)
        {
            if (targetType == null) return;

            var postfix = AccessTools.Method(typeof(RoadsOfTheRimPatch), nameof(GetProgress_Postfix));
            if (postfix == null) return;

            foreach (var m in targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.ReturnType == typeof(float) && m.GetParameters().Length <= 1)
                {
                    string name = m.Name;
                    if (name.IndexOf("progress", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("percent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("done", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("work", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            harmony.Patch(m, postfix: new HarmonyMethod(postfix));
                            if (m.GetParameters().Length == 0)
                            {
                                _progressGetters.Add((m, BuildFloatGetter(m)));
                            }
                            Loger.Log($"RoadsOfTheRimPatch hooked progress method: {targetType.Name}.{m.Name}");
                        }
                        catch { }
                    }
                }
            }
        }

        public static void GetProgress_Postfix(object __instance, ref float __result)
        {
            if (_isExtractingLocalProgress || __instance == null) return;

            WorldObject wo = __instance as WorldObject;
            if (wo == null && __instance is WorldObjectComp comp)
            {
                wo = comp.parent;
            }

            if (wo != null && TryGetRemoteProxy(wo, out var proxy))
            {
                __result = proxy.ProgressPct;
            }
        }

        public static bool ProgressDescription_Prefix(WorldObjectComp __instance, ref string __result)
        {
            var wo = __instance?.parent;
            if (wo != null && TryGetRemoteProxy(wo, out var proxy))
            {
                var sb = new StringBuilder(64);
                if (!string.IsNullOrEmpty(proxy.Owner))
                {
                    sb.AppendLine($"Будівельник: {proxy.Owner}");
                }
                sb.Append($"Прогрес: {proxy.ProgressPct.ToStringPercent()}");
                if (proxy.WorkTotal > 0f)
                {
                    sb.Append($" ({Math.Round(proxy.WorkDone)} / {Math.Round(proxy.WorkTotal)})");
                }
                __result = sb.ToString();
                return false;
            }
            return true;
        }

        private static bool TryGetRemoteProxy(WorldObject wo, out RemoteSiteProxy proxy)
        {
            proxy = null;
            if (wo == null) return false;
            lock (SyncLock)
            {
                return RemoteProxyByWorldObjectId.TryGetValue(wo.ID, out proxy);
            }
        }

        public static bool IsRoadConstructionObject(WorldObject wo)
        {
            if (wo == null) return false;
            if (IsModActive)
            {
                var type = wo.GetType();
                if (type == _siteType || type == _legType) return true;
                if (type.Namespace == "RoadsOfTheRim") return true;
            }

            var defName = wo.def?.defName;
            return defName != null && defName.IndexOf("RoadConstruction", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsRemoteProxyObject(WorldObject wo)
        {
            if (wo == null) return false;
            lock (SyncLock)
            {
                return RemoteProxyByWorldObjectId.ContainsKey(wo.ID);
            }
        }

        public static IEnumerable<Gizmo> SiteGetGizmos_Postfix(IEnumerable<Gizmo> values, WorldObject __instance)
        {
            bool isRemote = IsRemoteProxyObject(__instance);
            foreach (var g in values)
            {
                if (isRemote && g is Command) continue;
                yield return g;
            }
        }

        public static void OverlayRoad_Postfix(int fromTile, int toTile, RoadDef roadDef)
        {
            if (!IsModActive || _isApplyingFromServer || roadDef == null) return;
            if (Current.Game == null || SessionClient.Get?.IsLogined != true) return;

            QueueRoadChange(fromTile, toTile, roadDef.defName);
        }

        private static void QueueRoadChange(int tileA, int tileB, string roadDefName)
        {
            if (tileA < 0 || tileB < 0 || tileA == tileB) return;

            int minTile = Math.Min(tileA, tileB);
            int maxTile = Math.Max(tileA, tileB);
            var key = (minTile, maxTile);
            string defName = roadDefName ?? string.Empty;

            lock (SyncLock)
            {
                if (KnownLocalRoadLinks.TryGetValue(key, out string existing) && string.Equals(existing, defName, StringComparison.Ordinal))
                {
                    return;
                }

                KnownLocalRoadLinks[key] = defName;
                PendingRoads[key] = defName;
            }
        }

        private static List<WorldObject> RentLegList()
        {
            if (_legListPool.Count > 0)
            {
                var list = _legListPool.Pop();
                list.Clear();
                return list;
            }
            return new List<WorldObject>(8);
        }

        private static void ReturnAllLegLists()
        {
            foreach (var kvp in _legsBySiteBuffer)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.Clear();
                    _legListPool.Push(kvp.Value);
                }
            }
            _legsBySiteBuffer.Clear();
        }

        public static void TrackConstructionObjectsOnMainThread(List<WorldObject> allWorldObjects)
        {
            if (!IsModActive || allWorldObjects == null || Find.WorldGrid == null) return;

            try
            {
                _mySitesBuffer.Clear();
                ReturnAllLegLists();

                for (int i = 0; i < allWorldObjects.Count; i++)
                {
                    var wo = allWorldObjects[i];
                    if (wo == null || IsRemoteProxyObject(wo)) continue;

                    var type = wo.GetType();
                    if (type == _siteType)
                    {
                        _mySitesBuffer.Add(wo);
                        if (wo.Tile >= 0 && WatchedConstructionTiles.Add(wo.Tile))
                        {
                            SnapshotTileRoads(wo.Tile, queueDiff: false);
                        }
                    }
                    else if (type == _legType)
                    {
                        if (wo.Tile >= 0 && WatchedConstructionTiles.Add(wo.Tile))
                        {
                            SnapshotTileRoads(wo.Tile, queueDiff: false);
                        }

                        var parentSite = _getLegSite != null ? _getLegSite(wo) : _legSiteField?.GetValue(wo) as WorldObject;
                        if (parentSite != null && !IsRemoteProxyObject(parentSite))
                        {
                            if (!_legsBySiteBuffer.TryGetValue(parentSite, out var list))
                            {
                                list = RentLegList();
                                _legsBySiteBuffer[parentSite] = list;
                            }
                            list.Add(wo);
                        }
                    }
                }

                _currentMySiteIdsBuffer.Clear();

                for (int i = 0; i < _mySitesBuffer.Count; i++)
                {
                    var site = _mySitesBuffer[i];
                    int siteId = site.ID;
                    _currentMySiteIdsBuffer.Add(siteId);

                    var roadDefObj = _getSiteRoadToBuild != null ? _getSiteRoadToBuild(site) : _siteRoadToBuildField?.GetValue(site) as Def;
                    string roadDefName = roadDefObj?.defName ?? string.Empty;

                    _legsBySiteBuffer.TryGetValue(site, out var rawLegs);
                    FillOrderedLegTiles(rawLegs, _orderedLegTilesBuffer);

                    if (_orderedLegTilesBuffer.Count == 0 && string.IsNullOrEmpty(roadDefName))
                        continue;

                    string progressData = ExtractSiteCompProgress(site);

                    _signatureSb.Clear();
                    _signatureSb.Append(site.Tile).Append('|').Append(roadDefName).Append('|');
                    for (int t = 0; t < _orderedLegTilesBuffer.Count; t++)
                    {
                        if (t > 0) _signatureSb.Append(',');
                        _signatureSb.Append(_orderedLegTilesBuffer[t]);
                    }
                    _signatureSb.Append('|').Append(progressData);
                    string signature = _signatureSb.ToString();

                    lock (SyncLock)
                    {
                        if (!KnownMySitesSnapshot.TryGetValue(siteId, out var prevSnap) || !string.Equals(prevSnap.Signature, signature, StringComparison.Ordinal))
                        {
                            KnownMySitesSnapshot[siteId] = new LocalSiteSnapshot { Tile = site.Tile, Signature = signature };
                            PendingSitesToSync.Add(new WorldRoadSiteOnline
                            {
                                LocalSiteId = siteId,
                                Tile = site.Tile,
                                RoadDefName = roadDefName,
                                LegTiles = new List<int>(_orderedLegTilesBuffer),
                                CompProgressData = progressData,
                                IsDeleted = false
                            });
                        }
                    }
                }

                lock (SyncLock)
                {
                    _removedIdsBuffer.Clear();
                    foreach (var oldId in KnownMySitesSnapshot.Keys)
                    {
                        if (!_currentMySiteIdsBuffer.Contains(oldId))
                        {
                            _removedIdsBuffer.Add(oldId);
                        }
                    }

                    for (int i = 0; i < _removedIdsBuffer.Count; i++)
                    {
                        int delId = _removedIdsBuffer[i];
                        int oldTile = KnownMySitesSnapshot[delId].Tile;
                        KnownMySitesSnapshot.Remove(delId);
                        PendingSitesToSync.Add(new WorldRoadSiteOnline
                        {
                            LocalSiteId = delId,
                            Tile = oldTile,
                            IsDeleted = true
                        });
                    }
                }

                if (WatchedConstructionTiles.Count > 0)
                {
                    ScanWatchedTilesForRoadChanges();
                }
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch TrackConstructionObjects Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }
            finally
            {
                _mySitesBuffer.Clear();
                ReturnAllLegLists();
            }
        }

        private static void FillOrderedLegTiles(List<WorldObject> rawLegs, List<int> outTiles)
        {
            outTiles.Clear();
            if (rawLegs == null || rawLegs.Count == 0) return;

            if (_legPrevField == null || _legNextField == null)
            {
                for (int i = 0; i < rawLegs.Count; i++)
                {
                    outTiles.Add(rawLegs[i].Tile);
                }
                return;
            }

            WorldObject head = null;
            _legSetBuffer.Clear();
            for (int i = 0; i < rawLegs.Count; i++)
            {
                _legSetBuffer.Add(rawLegs[i]);
            }

            for (int i = 0; i < rawLegs.Count; i++)
            {
                var prev = _getLegPrev != null ? _getLegPrev(rawLegs[i]) : _legPrevField.GetValue(rawLegs[i]) as WorldObject;
                if (prev == null || !_legSetBuffer.Contains(prev))
                {
                    head = rawLegs[i];
                    break;
                }
            }

            if (head == null) head = rawLegs[0];

            _visitedLegsBuffer.Clear();
            var cur = head;
            while (cur != null && _visitedLegsBuffer.Add(cur))
            {
                outTiles.Add(cur.Tile);
                cur = _getLegNext != null ? _getLegNext(cur) : _legNextField.GetValue(cur) as WorldObject;
            }

            for (int i = 0; i < rawLegs.Count; i++)
            {
                if (_visitedLegsBuffer.Add(rawLegs[i]))
                {
                    outTiles.Add(rawLegs[i].Tile);
                }
            }
        }

        private static string ExtractSiteCompProgress(WorldObject site)
        {
            if (_siteCompType == null) return string.Empty;

            var comp = site.GetComponent(_siteCompType);
            if (comp == null) return string.Empty;

            _progressSb.Clear();
            float workCost = 0f;
            float workLeft = -1f;

            for (int i = 0; i < _compDictFields.Count; i++)
            {
                var field = _compDictFields[i];
                if (field.GetValue(comp) is IDictionary dict && dict.Count > 0)
                {
                    if (_progressSb.Length > 0) _progressSb.Append(';');
                    _progressSb.Append(field.Name).Append(':');
                    bool firstPair = true;

                    bool isCostDict = field.Name.IndexOf("cost", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isLeftDict = field.Name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0;

                    foreach (DictionaryEntry entry in dict)
                    {
                        if (entry.Key == null || entry.Value == null) continue;
                        string kStr = (entry.Key as Def)?.defName ?? entry.Key.ToString();
                        float val = Convert.ToSingle(entry.Value, CultureInfo.InvariantCulture);

                        if (!firstPair) _progressSb.Append(',');
                        firstPair = false;
                        _progressSb.Append(kStr).Append('=').Append(val.ToString("G", CultureInfo.InvariantCulture));

                        if (string.Equals(kStr, "Work", StringComparison.OrdinalIgnoreCase))
                        {
                            if (isCostDict && val > 0f) workCost = val;
                            else if (isLeftDict && val >= 0f) workLeft = val;
                        }
                    }
                }
            }

            float pct = 0f;
            if (workCost > 0f && workLeft >= 0f)
            {
                pct = Math.Max(0f, Math.Min(1f, (workCost - workLeft) / workCost));
            }

            if (_progressGetters.Count > 0)
            {
                _isExtractingLocalProgress = true;
                try
                {
                    for (int i = 0; i < _progressGetters.Count; i++)
                    {
                        var entry = _progressGetters[i];
                        object target = entry.Method.DeclaringType == _siteType ? (object)site : comp;
                        if (target != null && entry.Method.DeclaringType.IsInstanceOfType(target))
                        {
                            float val = entry.FastGetter != null ? entry.FastGetter(target) : (float)entry.Method.Invoke(target, null);
                            if (val >= 0f && val <= 1f)
                            {
                                pct = val;
                                break;
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    _isExtractingLocalProgress = false;
                }
            }

            if (_progressSb.Length > 0) _progressSb.Append(';');
            _progressSb.Append("__pct:").Append(pct.ToString("F3", CultureInfo.InvariantCulture));

            return _progressSb.ToString();
        }

        private static void ApplySiteCompProgress(WorldObject site, string progressData, out float parsedPct, out float workDone, out float workTotal)
        {
            parsedPct = 0f;
            workDone = 0f;
            workTotal = 0f;
            if (_siteCompType == null || string.IsNullOrEmpty(progressData)) return;

            var comp = site.GetComponent(_siteCompType);
            if (comp == null) return;

            float workCost = 0f;
            float workLeft = -1f;
            bool hasExplicitPct = false;

            var sections = progressData.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int s = 0; s < sections.Length; s++)
            {
                if (sections[s].StartsWith("__pct:", StringComparison.Ordinal))
                {
                    string pctStr = sections[s].Substring("__pct:".Length);
                    if (float.TryParse(pctStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pVal))
                    {
                        parsedPct = Math.Max(0f, Math.Min(1f, pVal));
                        hasExplicitPct = true;
                    }
                    break;
                }
            }

            for (int s = 0; s < sections.Length; s++)
            {
                int colonIdx = sections[s].IndexOf(':');
                if (colonIdx <= 0) continue;

                string fieldName = sections[s].Substring(0, colonIdx);
                if (string.Equals(fieldName, "__pct", StringComparison.Ordinal)) continue;

                string pairsStr = sections[s].Substring(colonIdx + 1);

                FieldInfo targetField = null;
                for (int f = 0; f < _compDictFields.Count; f++)
                {
                    if (string.Equals(_compDictFields[f].Name, fieldName, StringComparison.Ordinal))
                    {
                        targetField = _compDictFields[f];
                        break;
                    }
                }
                if (targetField == null) continue;

                var dict = targetField.GetValue(comp) as IDictionary;
                if (dict == null)
                {
                    try
                    {
                        dict = Activator.CreateInstance(targetField.FieldType) as IDictionary;
                        targetField.SetValue(comp, dict);
                    }
                    catch { continue; }
                }
                else
                {
                    dict.Clear();
                }

                Type keyType = typeof(string);
                Type valType = typeof(int);
                if (targetField.FieldType.IsGenericType)
                {
                    var genArgs = targetField.FieldType.GetGenericArguments();
                    if (genArgs.Length >= 2)
                    {
                        keyType = genArgs[0];
                        valType = genArgs[1];
                    }
                }

                bool isCostDict = fieldName.IndexOf("cost", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isLeftDict = fieldName.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0;

                var pairs = pairsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < pairs.Length; p++)
                {
                    int eqIdx = pairs[p].IndexOf('=');
                    if (eqIdx <= 0) continue;

                    string kStr = pairs[p].Substring(0, eqIdx);
                    if (!float.TryParse(pairs[p].Substring(eqIdx + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float rawVal))
                        continue;

                    object parsedKey = null;
                    object parsedVal = null;
                    try
                    {
                        if (keyType == typeof(string))
                        {
                            parsedKey = kStr;
                        }
                        else if (keyType.IsEnum)
                        {
                            parsedKey = Enum.Parse(keyType, kStr, true);
                        }
                        else if (typeof(Def).IsAssignableFrom(keyType))
                        {
                            parsedKey = GenDefDatabase.GetDefSilentFail(keyType, kStr, false);
                        }
                        else
                        {
                            parsedKey = Convert.ChangeType(kStr, keyType, CultureInfo.InvariantCulture);
                        }

                        if (valType == typeof(float))
                            parsedVal = rawVal;
                        else if (valType == typeof(int))
                            parsedVal = (int)Math.Round(rawVal);
                        else if (valType == typeof(double))
                            parsedVal = (double)rawVal;
                        else
                            parsedVal = Convert.ChangeType(rawVal, valType, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        parsedKey = kStr;
                        parsedVal = rawVal;
                    }

                    if (parsedKey != null && parsedVal != null)
                    {
                        try
                        {
                            dict[parsedKey] = parsedVal;
                        }
                        catch { }

                        if (string.Equals(kStr, "Work", StringComparison.OrdinalIgnoreCase))
                        {
                            if (isCostDict && rawVal > 0f) workCost = rawVal;
                            else if (isLeftDict && rawVal >= 0f) workLeft = rawVal;
                        }
                    }
                }
            }

            if (workCost > 0f && workLeft >= 0f)
            {
                workTotal = workCost;
                workDone = Math.Max(0f, workCost - workLeft);
                float calcPct = Math.Max(0f, Math.Min(1f, workDone / workCost));
                if (!hasExplicitPct) parsedPct = calcPct;
            }
            else if (!hasExplicitPct)
            {
                parsedPct = 0f;
            }
        }

        private static void SetWorldObjectsLayerDirty()
        {
            try
            {
                if (Find.World?.renderer != null)
                {
                    Find.World.renderer.SetDirty<WorldLayer_WorldObjects>();
                    Find.World.renderer.SetDirty<WorldLayer_Paths>();
                }
            }
            catch { }
        }

        public static void ApplyRoadSitesFromServer(List<WorldRoadSiteOnline> remoteSites)
        {
            if (!IsModActive || remoteSites == null || remoteSites.Count == 0) return;
            if (Find.WorldObjects == null) return;

            var siteDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("RoadConstructionSite");
            var legDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("RoadConstructionLeg");
            if (siteDef == null || legDef == null) return;

            bool anyVisualChange = false;

            for (int i = 0; i < remoteSites.Count; i++)
            {
                var info = remoteSites[i];
                if (info == null || string.IsNullOrEmpty(info.OwnerLogin)) continue;
                if (string.Equals(info.OwnerLogin, SessionClientController.My?.Login, StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = (info.OwnerLogin, info.LocalSiteId);

                if (info.IsDeleted || info.Tile < 0)
                {
                    RemoveRemoteProxy(key, info.Tile);
                    anyVisualChange = true;
                    continue;
                }

                try
                {
                    bool isNewSite = false;
                    if (!RemoteProxies.TryGetValue(key, out var proxy) || proxy.SiteObject == null || !Find.WorldObjects.Contains(proxy.SiteObject))
                    {
                        RemoveRemoteProxy(key, info.Tile);
                        var newSite = WorldObjectMaker.MakeWorldObject(siteDef);
                        newSite.Tile = info.Tile;
                        if (Faction.OfPlayer != null)
                        {
                            try { newSite.SetFaction(Faction.OfPlayer); } catch { }
                        }

                        SetSiteRoadToBuild(newSite, info.RoadDefName);
                        Find.WorldObjects.Add(newSite);

                        proxy = new RemoteSiteProxy
                        {
                            Owner = info.OwnerLogin,
                            SiteObject = newSite
                        };
                        lock (SyncLock)
                        {
                            RemoteProxies[key] = proxy;
                            RemoteProxyByWorldObjectId[newSite.ID] = proxy;
                        }
                        isNewSite = true;
                        anyVisualChange = true;
                    }
                    else
                    {
                        proxy.Owner = info.OwnerLogin;
                        if (proxy.SiteObject.Tile != info.Tile)
                        {
                            proxy.SiteObject.Tile = info.Tile;
                            anyVisualChange = true;
                        }
                        SetSiteRoadToBuild(proxy.SiteObject, info.RoadDefName);
                    }

                    string legSig = info.LegTiles != null ? string.Join(",", info.LegTiles) : string.Empty;
                    if (isNewSite || !string.Equals(proxy.LastLegSignature, legSig, StringComparison.Ordinal))
                    {
                        proxy.LastLegSignature = legSig;
                        RebuildProxyLegs(proxy, legDef, info.LegTiles);
                        anyVisualChange = true;

                        if (_siteCompType != null && (_compSetCostsAction != null || _compSetCostsMethod != null))
                        {
                            try
                            {
                                var comp = proxy.SiteObject.GetComponent(_siteCompType);
                                if (comp != null)
                                {
                                    if (_compSetCostsAction != null)
                                        _compSetCostsAction(comp);
                                    else if (_compSetCostsMethod.GetParameters().Length == 0)
                                        _compSetCostsMethod.Invoke(comp, null);
                                }
                            }
                            catch { }
                        }
                    }

                    ApplySiteCompProgress(proxy.SiteObject, info.CompProgressData, out float pct, out float workDone, out float workTotal);
                    if (Math.Abs(proxy.ProgressPct - pct) > 0.0005f)
                    {
                        anyVisualChange = true;
                    }
                    proxy.ProgressPct = pct;
                    proxy.WorkDone = workDone;
                    proxy.WorkTotal = workTotal;
                    RefreshProxyVisualMaterial(proxy.SiteObject, proxy);
                }
                catch (Exception ex)
                {
                    Loger.Log("RoadsOfTheRimPatch ApplyRoadSitesFromServer Exception: " + ex.Message, Loger.LogLevel.WARNING);
                }
            }

            if (anyVisualChange)
            {
                SetWorldObjectsLayerDirty();
            }
        }

        private static void SetSiteRoadToBuild(WorldObject siteObj, string roadDefName)
        {
            if (siteObj == null || _siteRoadToBuildField == null || string.IsNullOrEmpty(roadDefName)) return;

            try
            {
                var fieldType = _siteRoadToBuildField.FieldType;
                if (typeof(Def).IsAssignableFrom(fieldType))
                {
                    var roadDef = GenDefDatabase.GetDefSilentFail(fieldType, roadDefName, false);
                    if (roadDef != null)
                    {
                        if (_setSiteRoadToBuild != null)
                            _setSiteRoadToBuild(siteObj, roadDef);
                        else
                            _siteRoadToBuildField.SetValue(siteObj, roadDef);
                    }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch SetSiteRoadToBuild warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        private static void RebuildProxyLegs(RemoteSiteProxy proxy, WorldObjectDef legDef, List<int> legTiles)
        {
            for (int i = proxy.LegObjects.Count - 1; i >= 0; i--)
            {
                var oldLeg = proxy.LegObjects[i];
                if (oldLeg != null)
                {
                    lock (SyncLock)
                    {
                        RemoteProxyByWorldObjectId.Remove(oldLeg.ID);
                    }
                    if (Find.WorldObjects != null && Find.WorldObjects.Contains(oldLeg))
                    {
                        Find.WorldObjects.Remove(oldLeg);
                    }
                }
            }
            proxy.LegObjects.Clear();

            if (legTiles == null || legTiles.Count == 0 || Find.WorldObjects == null) return;

            WorldObject prevLeg = null;
            for (int i = 0; i < legTiles.Count; i++)
            {
                int tile = legTiles[i];
                if (tile < 0) continue;

                var legObj = WorldObjectMaker.MakeWorldObject(legDef);
                legObj.Tile = tile;
                if (Faction.OfPlayer != null)
                {
                    try { legObj.SetFaction(Faction.OfPlayer); } catch { }
                }

                if (_setLegSite != null) _setLegSite(legObj, proxy.SiteObject);
                else _legSiteField?.SetValue(legObj, proxy.SiteObject);

                if (_setLegPrev != null) _setLegPrev(legObj, prevLeg);
                else _legPrevField?.SetValue(legObj, prevLeg);

                if (_setLegNext != null) _setLegNext(legObj, null);
                else _legNextField?.SetValue(legObj, null);

                if (prevLeg != null)
                {
                    if (_setLegNext != null) _setLegNext(prevLeg, legObj);
                    else _legNextField?.SetValue(prevLeg, legObj);
                }

                Find.WorldObjects.Add(legObj);
                proxy.LegObjects.Add(legObj);

                lock (SyncLock)
                {
                    RemoteProxyByWorldObjectId[legObj.ID] = proxy;
                }

                prevLeg = legObj;
            }
        }

        private static void RemoveRemoteProxy((string Owner, int Id) key, int fallbackTile = -1)
        {
            RemoteSiteProxy proxy;
            lock (SyncLock)
            {
                RemoteProxies.TryGetValue(key, out proxy);
                RemoteProxies.Remove(key);
            }

            if (proxy != null)
            {
                for (int i = proxy.LegObjects.Count - 1; i >= 0; i--)
                {
                    var leg = proxy.LegObjects[i];
                    if (leg != null)
                    {
                        lock (SyncLock) { RemoteProxyByWorldObjectId.Remove(leg.ID); }
                        if (Find.WorldObjects != null && Find.WorldObjects.Contains(leg))
                        {
                            Find.WorldObjects.Remove(leg);
                        }
                    }
                }
                proxy.LegObjects.Clear();

                if (proxy.SiteObject != null)
                {
                    lock (SyncLock) { RemoteProxyByWorldObjectId.Remove(proxy.SiteObject.ID); }
                    if (Find.WorldObjects != null && Find.WorldObjects.Contains(proxy.SiteObject))
                    {
                        Find.WorldObjects.Remove(proxy.SiteObject);
                    }
                    proxy.SiteObject = null;
                }
            }

            if (fallbackTile >= 0 && Find.WorldObjects != null && _siteType != null)
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var wo = all[i];
                    if (wo != null && wo.Tile == fallbackTile && _siteType.IsInstanceOfType(wo) && IsRemoteProxyObject(wo))
                    {
                        lock (SyncLock) { RemoteProxyByWorldObjectId.Remove(wo.ID); }
                        Find.WorldObjects.Remove(wo);
                    }
                }
            }
        }

        public static void OnBeforeGameSave()
        {
            if (!IsModActive || Find.WorldObjects == null) return;

            DetachedBeforeSave.Clear();
            lock (SyncLock)
            {
                foreach (var proxy in RemoteProxies.Values)
                {
                    for (int i = 0; i < proxy.LegObjects.Count; i++)
                    {
                        var leg = proxy.LegObjects[i];
                        if (leg != null && Find.WorldObjects.Contains(leg))
                        {
                            Find.WorldObjects.Remove(leg);
                            DetachedBeforeSave.Add(leg);
                        }
                    }

                    if (proxy.SiteObject != null && Find.WorldObjects.Contains(proxy.SiteObject))
                    {
                        Find.WorldObjects.Remove(proxy.SiteObject);
                        DetachedBeforeSave.Add(proxy.SiteObject);
                    }
                }
            }
        }

        public static void OnAfterGameSave()
        {
            if (!IsModActive || Find.WorldObjects == null || DetachedBeforeSave.Count == 0) return;

            for (int i = 0; i < DetachedBeforeSave.Count; i++)
            {
                var wo = DetachedBeforeSave[i];
                if (wo != null && !Find.WorldObjects.Contains(wo))
                {
                    Find.WorldObjects.Add(wo);
                }
            }
            DetachedBeforeSave.Clear();
        }

        private static void SnapshotTileRoads(int tileId, bool queueDiff)
        {
            var grid = Find.WorldGrid;
            if (grid == null || tileId < 0 || tileId >= grid.TilesCount) return;

            var tile = grid[tileId];
            if (tile == null) return;

            NeighborsBuffer.Clear();
            grid.GetTileNeighbors(tileId, NeighborsBuffer);

            for (int i = 0; i < NeighborsBuffer.Count; i++)
            {
                int neighborId = NeighborsBuffer[i];
                string foundRoadDef = string.Empty;

                if (tile.potentialRoads != null)
                {
                    RoadDef bestRoad = null;
                    for (int r = 0; r < tile.potentialRoads.Count; r++)
                    {
                        var link = tile.potentialRoads[r];
                        if (link.neighbor == neighborId && link.road != null)
                        {
                            if (bestRoad == null || link.road.priority >= bestRoad.priority)
                            {
                                bestRoad = link.road;
                            }
                        }
                    }
                    foundRoadDef = bestRoad?.defName ?? string.Empty;
                }

                int minTile = Math.Min(tileId, neighborId);
                int maxTile = Math.Max(tileId, neighborId);
                var key = (minTile, maxTile);

                lock (SyncLock)
                {
                    if (!KnownLocalRoadLinks.TryGetValue(key, out string prevRoad))
                    {
                        KnownLocalRoadLinks[key] = foundRoadDef;
                        if (queueDiff && !string.IsNullOrEmpty(foundRoadDef))
                        {
                            PendingRoads[key] = foundRoadDef;
                        }
                    }
                    else if (!string.Equals(prevRoad, foundRoadDef, StringComparison.Ordinal))
                    {
                        KnownLocalRoadLinks[key] = foundRoadDef;
                        if (queueDiff)
                        {
                            PendingRoads[key] = foundRoadDef;
                        }
                    }
                }
            }
        }

        private static void ScanWatchedTilesForRoadChanges()
        {
            if (WatchedConstructionTiles.Count == 0 || Find.WorldGrid == null) return;

            _watchedTilesListBuffer.Clear();
            foreach (int tileId in WatchedConstructionTiles)
            {
                _watchedTilesListBuffer.Add(tileId);
            }

            for (int i = 0; i < _watchedTilesListBuffer.Count; i++)
            {
                SnapshotTileRoads(_watchedTilesListBuffer[i], queueDiff: true);
            }
        }

        public static List<WorldRoadOnline> FlushPendingRoadsForServer()
        {
            if (!IsModActive) return null;

            lock (SyncLock)
            {
                if (PendingRoads.Count == 0) return null;

                var list = new List<WorldRoadOnline>(PendingRoads.Count);
                foreach (var kvp in PendingRoads)
                {
                    list.Add(new WorldRoadOnline
                    {
                        FromTile = kvp.Key.From,
                        ToTile = kvp.Key.To,
                        RoadDefName = kvp.Value
                    });
                }
                PendingRoads.Clear();
                return list;
            }
        }

        public static List<WorldRoadSiteOnline> FlushPendingRoadSitesForServer()
        {
            if (!IsModActive) return null;

            lock (SyncLock)
            {
                if (PendingSitesToSync.Count == 0) return null;

                var list = new List<WorldRoadSiteOnline>(PendingSitesToSync);
                PendingSitesToSync.Clear();
                return list;
            }
        }

        public static void ApplyRoadsFromServer(List<WorldRoadOnline> serverRoads)
        {
            if (!IsModActive || serverRoads == null || serverRoads.Count == 0) return;

            var grid = Find.WorldGrid;
            if (grid == null) return;

            _isApplyingFromServer = true;
            bool anyChanged = false;

            try
            {
                for (int i = 0; i < serverRoads.Count; i++)
                {
                    var roadInfo = serverRoads[i];
                    if (roadInfo == null) continue;

                    int tileAId = roadInfo.FromTile;
                    int tileBId = roadInfo.ToTile;
                    if (tileAId < 0 || tileBId < 0 || tileAId >= grid.TilesCount || tileBId >= grid.TilesCount || tileAId == tileBId)
                        continue;

                    int minTile = Math.Min(tileAId, tileBId);
                    int maxTile = Math.Max(tileAId, tileBId);
                    string targetDefName = roadInfo.RoadDefName ?? string.Empty;

                    lock (SyncLock)
                    {
                        KnownLocalRoadLinks[(minTile, maxTile)] = targetDefName;
                    }

                    RoadDef roadDef = !string.IsNullOrEmpty(targetDefName)
                        ? DefDatabase<RoadDef>.GetNamedSilentFail(targetDefName)
                        : null;

                    if (string.IsNullOrEmpty(targetDefName))
                    {
                        if (RemoveRoadLink(grid[tileAId], tileBId) | RemoveRoadLink(grid[tileBId], tileAId))
                        {
                            RecalculateTiles(tileAId, tileBId);
                            anyChanged = true;
                        }
                        continue;
                    }

                    if (roadDef == null) continue;

                    if (SetOrUpgradeRoadLink(grid[tileAId], tileBId, roadDef) | SetOrUpgradeRoadLink(grid[tileBId], tileAId, roadDef))
                    {
                        RecalculateTiles(tileAId, tileBId);
                        anyChanged = true;
                    }
                }

                if (anyChanged && Find.World?.renderer != null)
                {
                    Find.World.renderer.SetDirty<WorldLayer_Roads>();
                    Find.World.renderer.SetDirty<WorldLayer_Paths>();
                }
            }
            catch (Exception ex)
            {
                Loger.Log("RoadsOfTheRimPatch ApplyRoadsFromServer Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }
            finally
            {
                _isApplyingFromServer = false;
            }
        }

        private static bool SetOrUpgradeRoadLink(Tile tile, int neighborTileId, RoadDef roadDef)
        {
            if (tile == null) return false;

            if (tile.potentialRoads == null)
            {
                tile.potentialRoads = new List<Tile.RoadLink>();
            }

            for (int i = 0; i < tile.potentialRoads.Count; i++)
            {
                if (tile.potentialRoads[i].neighbor == neighborTileId)
                {
                    if (tile.potentialRoads[i].road == roadDef)
                    {
                        return false;
                    }
                    tile.potentialRoads[i] = new Tile.RoadLink { neighbor = neighborTileId, road = roadDef };
                    return true;
                }
            }

            tile.potentialRoads.Add(new Tile.RoadLink { neighbor = neighborTileId, road = roadDef });
            return true;
        }

        private static bool RemoveRoadLink(Tile tile, int neighborTileId)
        {
            if (tile?.potentialRoads == null) return false;

            bool removed = false;
            for (int i = tile.potentialRoads.Count - 1; i >= 0; i--)
            {
                if (tile.potentialRoads[i].neighbor == neighborTileId)
                {
                    tile.potentialRoads.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }

        private static void RecalculateTiles(int tileA, int tileB)
        {
            try
            {
                Find.WorldPathGrid?.RecalculatePerceivedMovementDifficultyAt(tileA);
                Find.WorldPathGrid?.RecalculatePerceivedMovementDifficultyAt(tileB);
            }
            catch { }
        }

        public static void ClearCache()
        {
            lock (SyncLock)
            {
                PendingRoads.Clear();
                KnownLocalRoadLinks.Clear();
                WatchedConstructionTiles.Clear();
                KnownMySitesSnapshot.Clear();
                PendingSitesToSync.Clear();
                RemoteProxies.Clear();
                RemoteProxyByWorldObjectId.Clear();
                DetachedBeforeSave.Clear();

                _mySitesBuffer.Clear();
                ReturnAllLegLists();
                _currentMySiteIdsBuffer.Clear();
                _orderedLegTilesBuffer.Clear();
                _legSetBuffer.Clear();
                _visitedLegsBuffer.Clear();
                _watchedTilesListBuffer.Clear();
                _removedIdsBuffer.Clear();
            }
        }
    }
}