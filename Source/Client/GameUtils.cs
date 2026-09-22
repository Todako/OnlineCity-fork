using HarmonyLib;
using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses.Harmony;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml;
using UnityEngine;
using Util;
using Verse;

namespace RimWorldOnlineCity
{
    public static class ScribeSaverHelper
    {
        private static readonly AccessTools.FieldRef<ScribeSaver, XmlWriter> WriterRef =
            AccessTools.FieldRefAccess<ScribeSaver, XmlWriter>("writer");

        public static XmlWriter GetWriter(this ScribeSaver scribeSaver)
        {
            return WriterRef(scribeSaver);
        }

        public static void SetWriter(this ScribeSaver scribeSaver, XmlWriter writer)
        {
            WriterRef(scribeSaver) = writer;
        }
    }

    [StaticConstructorOnStartup]
    public static class GameUtils
    {
        internal static readonly Texture2D CircleFill = ContentFinder<Texture2D>.Get("circle-fill");
        private static readonly HashSet<string> ExceptionDravLineThing = new HashSet<string>();

        private static int BugNum = 0;
        public static void GetBug()
        {
            var dir = Loger.PathLog.Substring(0, Loger.PathLog.Length - 1);
            var fileName = $"Log_{DateTime.Now:yyyy-MM-dd}_*.txt";
            var list = Directory.GetFiles(dir, fileName, SearchOption.TopDirectoryOnly);
            var dataToSave = GZip.ZipMoreByteByte(list, name => File.ReadAllBytes(name.NormalizePath()));
            var code = $"{DateTime.Now:yyyy-MM-dd}_{MainHelper.LockCode}_{BugNum++}";
            var dataDir = Path.Combine(dir, "Log_" + code);
            Directory.CreateDirectory(dataDir);
            File.WriteAllBytes(Path.Combine(dataDir, $"BugReport_{code}.zip"), dataToSave);
            Process.Start(dataDir);
        }

        public static Texture2D GetTextureFromSaveData(byte[] data)
        {
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(data);
            texture.Apply();
            return texture;
        }

        public static void DravLineThing(Rect rect, ThingTrade thing, bool withInfo)
        {
            DravLineThing(rect, thing, withInfo, Color.white);
        }

        /// <summary>
        /// Відмальовка іконки та підказки для ThingTrade.
        /// ОПТИМІЗАЦІЯ: усунено щокадровий виклик thing.ToString() в OnGUI.
        /// </summary>
        public static void DravLineThing(Rect rect, ThingTrade thing, bool withInfo, Color labelColor, float xi = 24f, float yi = 0)
        {
            if (ExceptionDravLineThing.Count > 0 && ExceptionDravLineThing.Contains(thing.ToString())) return;
            try
            {
                if (thing.Def?.race?.Humanlike ?? false)
                {
                    var position = new Rect(rect.x, rect.y, 24f, 24f);
                    GUI.DrawTexture(position, GeneralTexture.IconHuman);
                }
                else
                {
                    Widgets.ThingIcon(rect, thing.Def);
                }

                if (string.IsNullOrEmpty(thing.StuffName))
                {
                    TooltipHandler.TipRegion(rect, thing.Def.LabelCap);
                    GUI.color = labelColor;
                    if (withInfo) Widgets.InfoCardButton(rect.x + xi, rect.y + yi, thing.Def);
                    GUI.color = Color.white;
                }
                else
                {
                    TooltipHandler.TipRegion(rect, thing.Def.LabelCap + "OCity_GameUtils_From".Translate() + thing.StuffDef.LabelAsStuff);
                    GUI.color = labelColor;
                    if (withInfo) Widgets.InfoCardButton(rect.x + xi, rect.y + yi, thing.Def, thing.StuffDef);
                    GUI.color = Color.white;
                }
            }
            catch
            {
                ExceptionDravLineThing.Add(thing.ToString());
                throw;
            }
        }

        public static void DravLineThing(Rect rect, Thing thing, bool withInfo, float xi = 24f, float yi = 0)
        {
            if (thing == null) return;
            Widgets.ThingIcon(rect, thing);
            if (withInfo) Widgets.InfoCardButton(rect.x + xi, rect.y + yi, thing);

            var localThing = thing;
            TooltipHandler.TipRegion(rect, new TipSignal(delegate
            {
                string text = localThing.LabelCapNoCount;
                string tipDescription = localThing.DescriptionFlavor;
                if (!tipDescription.NullOrEmpty())
                {
                    text = text + ": " + tipDescription;
                }
                return text;
            }, localThing.GetHashCode()));
        }

        public static void DravLineThing(Rect rect, ThingDef thing, bool withInfo)
        {
            if (thing == null) return;
            Widgets.ThingIcon(rect, thing);
            if (withInfo) Widgets.InfoCardButton(rect.x + 24f, rect.y, thing);

            var localThing = thing;
            TooltipHandler.TipRegion(rect, new TipSignal(delegate
            {
                string text = localThing.LabelCap;
                string tipDescription = localThing.DescriptionDetailed;
                if (!tipDescription.NullOrEmpty())
                {
                    text = text + ": " + tipDescription;
                }
                return text;
            }, localThing.GetHashCode()));
        }

        public static void DravLineThing(Rect rectLine, Thing thing, Color labelColor)
        {
            Rect rect = new Rect(0f, 0f, 24f, 24f);

            Widgets.ThingIcon(rect, thing, 1f);
            Widgets.InfoCardButton(30f, 0f, thing);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Rect rect2 = new Rect(55f, 0f, rectLine.width - 55f, rectLine.height);
            Text.WordWrap = false;
            GUI.color = labelColor;
            Widgets.Label(rect2, thing.LabelCapNoCount);
            Text.WordWrap = true;
            GenUI.ResetLabelAlign();
            GUI.color = Color.white;

            var localThing = thing;
            TooltipHandler.TipRegion(rectLine, new TipSignal(delegate
            {
                string text = localThing.LabelCapNoCount;
                string tipDescription = localThing.DescriptionFlavor;
                if (!tipDescription.NullOrEmpty())
                {
                    text = text + ": " + tipDescription;
                }
                return text;
            }, localThing.GetHashCode()));
        }

        public static List<WorldObject> GetAllWorldObjects()
        {
            var raw = Find.WorldObjects.AllWorldObjects;
            var list = new List<WorldObject>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var wo = raw[i];
                if (wo != null) list.Add(wo);
            }
            return list;
        }

        public static List<TransferableOneWay> DistinctToTransferableOneWays(this IEnumerable<Thing> things)
        {
            var transferables = new List<TransferableOneWay>();
            foreach (var item in things)
            {
                if (item == null) continue;
                TransferableOneWay transferableOneWay = TransferableUtility.TransferableMatching<TransferableOneWay>(item, transferables, TransferAsOneMode.Normal);
                if (transferableOneWay == null)
                {
                    transferableOneWay = new TransferableOneWay();
                    transferables.Add(transferableOneWay);
                }
                transferableOneWay.things.Add(item);
            }

            transferables.RemoveAll(t => t.MaxCount <= 0);
            return transferables;
        }

        public static Dictionary<Thing, int> TransferableOneWaysToDictionary(this IEnumerable<TransferableOneWay> selectByGroup, bool selectAll = false)
        {
            var dict = new Dictionary<Thing, int>();
            foreach (var tow in selectByGroup)
            {
                var needCount = selectAll ? tow.MaxCount : tow.CountToTransfer;
                if (needCount <= 0) continue;

                if (tow.AnyThing is Pawn pawn)
                {
                    dict[pawn] = 1;
                    continue;
                }

                for (int i = 0; i < tow.things.Count; i++)
                {
                    var thing = tow.things[i];
                    var cnt = thing.stackCount;
                    if (needCount > cnt)
                    {
                        dict[thing] = cnt;
                    }
                    else
                    {
                        dict[thing] = needCount;
                    }
                    needCount -= cnt;
                    if (needCount <= 0) break;
                }
            }
            return dict;
        }

        public static List<TransferableOneWay> ChechToTrade(IEnumerable<ThingTrade> targets, IEnumerable<Thing> allThings)
        {
            var rate = 1;
            return ChechToTradeDo(targets, allThings, null, ref rate, false);
        }

        public static List<TransferableOneWay> ChechToTrade(IEnumerable<ThingTrade> targets, IEnumerable<Thing> allThings, IEnumerable<Thing> altThings, out int rate, int incomplete = 0)
        {
            if (MainHelper.DebugMode)
            {
                Loger.Log("GameUtils.ChechToTrade "
                    + "targets: " + targets.ToList().ToStringLabel() + Environment.NewLine
                    + "allThings: " + allThings.Select(t => ThingTrade.CreateTrade(t, t.stackCount)).ToList().ToStringLabel() + Environment.NewLine);
            }

            var trs = targets.OrderByCost();
            if (incomplete == 0)
            {
                rate = 100000000;
                var res = ChechToTradeDo(trs, allThings, altThings, ref rate, true);
                if (res == null) return null;
                return ChechToTradeDo(trs, allThings, altThings, ref rate, false);
            }
            else
            {
                rate = incomplete;
                var res = ChechToTradeDo(trs, allThings, altThings, ref rate, true);
                if (res == null)
                {
                    rate = incomplete;
                    return ChechToTradeDo(trs, allThings, altThings, ref rate, false, true);
                }
                return ChechToTradeDo(trs, allThings, altThings, ref rate, false);
            }
        }

        /// <summary>
        /// Компаратор для сортування речей без конкатенації рядків та виділення пам'яті в купі (Zero GC).
        /// </summary>
        private sealed class TradeThingComparer : IComparer<Thing>
        {
            public static readonly TradeThingComparer Instance = new TradeThingComparer();

            public int Compare(Thing x, Thing y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int cmp = string.CompareOrdinal(x.def.defName, y.def.defName);
                if (cmp != 0) return cmp;

                QualityUtility.TryGetQuality(x, out var qX);
                QualityUtility.TryGetQuality(y, out var qY);
                cmp = ((int)qX).CompareTo((int)qY);
                if (cmp != 0) return cmp;

                cmp = (10000 - x.HitPoints).CompareTo(10000 - y.HitPoints);
                if (cmp != 0) return cmp;

                return x.stackCount.CompareTo(y.stackCount);
            }
        }

        /// <summary>
        /// Перевірка можливості торгової угоди.
        /// ОПТИМІЗАЦІЯ: повністю ліквідовано створення анонімних об'єктів та конкатенацію рядків під час сортування.
        /// </summary>
        private static List<TransferableOneWay> ChechToTradeDo(IEnumerable<ThingTrade> targets, IEnumerable<Thing> allThings, IEnumerable<Thing> altThings, ref int rate, bool setRect, bool incomplete = false, bool setTradeCount = true)
        {
            bool result = true;
            var selects = new List<TransferableOneWay>();

            var source = new Dictionary<Thing, int>();
            var sourceKeys = new List<Thing>();
            foreach (var item in allThings)
            {
                if (item == null) continue;
                source[item] = item.stackCount;
                sourceKeys.Add(item);
            }
            sourceKeys.Sort(TradeThingComparer.Instance);

            Dictionary<Thing, int> sourcealt = null;
            List<Thing> sourcealtKeys = null;
            if (altThings != null)
            {
                sourcealt = new Dictionary<Thing, int>();
                sourcealtKeys = new List<Thing>();
                foreach (var item in altThings)
                {
                    if (item == null) continue;
                    sourcealt[item] = item.stackCount;
                    sourcealtKeys.Add(item);
                }
                sourcealtKeys.Sort(TradeThingComparer.Instance);
            }

            foreach (var target in targets)
            {
                target.TradeCount = 0;
                if (target.Count == 0)
                {
                    target.NotTrade = false;
                    target.TradeCount = 0;
                    continue;
                }

                if (MainHelper.DebugMode) Log.Message("--- --- " + target.DefName + " " + target.Count + "*" + rate);
                if (setRect && target.Count > 100 && rate > 1000000) rate = 1000000;

                var select = new TransferableOneWay();
                var selectalt = new TransferableOneWay();

                for (int i = 0; i < sourceKeys.Count; i++)
                {
                    var thing = sourceKeys[i];
                    if (!setTradeCount && target.Count <= select.CountToTransfer) break;
                    if (source[thing] == 0) continue;
                    if (target.MatchesThing(thing))
                    {
                        target.TradeCount += source[thing];
                        if (target.Count <= select.CountToTransfer) continue;
                        select.things.Add(thing);
                        var count = target.Count * rate - select.CountToTransfer > source[thing]
                            ? source[thing]
                            : target.Count * rate - select.CountToTransfer;
                        select.ForceTo(select.CountToTransfer + count);
                        source[thing] -= count;
                    }
                }

                if (sourcealt != null && sourcealtKeys != null)
                {
                    for (int i = 0; i < sourcealtKeys.Count; i++)
                    {
                        var thing = sourcealtKeys[i];
                        if (!setTradeCount && target.Count <= select.CountToTransfer) break;
                        if (sourcealt[thing] == 0) continue;
                        if (target.MatchesThing(thing))
                        {
                            target.TradeCount += sourcealt[thing];
                            if (target.Count <= select.CountToTransfer) continue;
                            select.things.Add(thing);
                            var count = target.Count * rate - select.CountToTransfer > sourcealt[thing]
                                ? sourcealt[thing]
                                : target.Count * rate - select.CountToTransfer;
                            select.ForceTo(select.CountToTransfer + count);
                            sourcealt[thing] -= count;

                            selectalt.things.Add(thing);
                            selectalt.ForceTo(selectalt.CountToTransfer + count);
                        }
                    }
                }

                if (!incomplete && target.Count * (setRect ? 1 : rate) > select.CountToTransfer)
                {
                    result = false;
                    target.NotTrade = true;
                }
                else
                {
                    if (setRect && target.Count * rate > select.CountToTransfer)
                    {
                        rate = select.CountToTransfer / target.Count;
                    }
                    if (altThings == null)
                        selects.Add(select);
                    else
                        selects.Add(selectalt);
                    target.NotTrade = false;
                }
            }
            return result ? selects : null;
        }

        public static bool IsProtectingNovice()
        {
            if (SessionClientController.Data.IsAdmin || !SessionClientController.Data.ProtectingNovice) return false;

            var costAll = SessionClientController.Data.MyEx.CostAllWorldObjects();
            return SessionClientController.My.LastTick < 3600000 / 2 || costAll.MarketValueTotal < MainHelper.MinCostForTrade;
        }

        internal static List<Thing> FilterBeforeSendServer(this IEnumerable<Thing> list)
        {
            if (UpdateWorldController.ExistsEnemyPawns || list == null) return new List<Thing>(0);

            List<Precept_Role> roles = null;
            if (ModsConfig.IdeologyActive && Find.FactionManager?.OfPlayer?.ideos?.PrimaryIdeo != null)
            {
                var allRoles = Find.FactionManager.OfPlayer.ideos.PrimaryIdeo.RolesListForReading;
                for (int i = 0; i < allRoles.Count; i++)
                {
                    var r = allRoles[i];
                    if (r.def.defName == "IdeoRole_Leader" || r.def.defName == "IdeoRole_Moralist")
                    {
                        if (roles == null) roles = new List<Precept_Role>(2);
                        roles.Add(r);
                    }
                }
            }

            var data = SessionClientController.Data;
            var forbidden = data != null ? data.GeneralSettings.ExchengeForbiddenDefNamesList : null;
            bool isNovice = IsProtectingNovice();

            var result = new List<Thing>();
            foreach (var thing in list)
            {
                if (thing == null) continue;
                if (thing is Corpse) continue;
                if (thing.def.defName == "Wastepack") continue;
                if (forbidden != null && forbidden.Contains(thing.def.defName)) continue;
                if (isNovice && thing.def.stackLimit <= 1) continue;

                if (thing is Pawn p && roles != null)
                {
                    bool isRoleAssigned = false;
                    for (int r = 0; r < roles.Count; r++)
                    {
                        if (roles[r].IsAssigned(p))
                        {
                            isRoleAssigned = true;
                            break;
                        }
                    }
                    if (isRoleAssigned) continue;
                }

                result.Add(thing);
            }

            return result;
        }

        public static List<Thing> GetAllThings(Caravan caravan, bool thingOnPawn = false, bool withTransferFilter = true)
        {
            var rawPawns = caravan.PawnsListForReading;
            var pawns = withTransferFilter ? FilterBeforeSendServer(rawPawns) : new List<Thing>(rawPawns);

            List<Thing> goods;
            if (thingOnPawn)
            {
                var onPawn = GetThingOnPawn(pawns);
                goods = new List<Thing>(onPawn.Count + pawns.Count);
                goods.AddRange(onPawn);
                goods.AddRange(pawns);
            }
            else
            {
                var inv = CaravanInventoryUtility.AllInventoryItems(caravan);
                goods = new List<Thing>(inv.Count + pawns.Count);
                goods.AddRange(inv);
                goods.AddRange(pawns);
            }

            return withTransferFilter ? FilterBeforeSendServer(goods) : goods;
        }

        public static List<Thing> GetAllThings(Map map, bool thingOnPawn = false, bool withTransferFilter = true)
        {
            var rawPawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            var pawns = withTransferFilter ? FilterBeforeSendServer(rawPawns) : new List<Thing>(rawPawns);

            var reachableItems = CaravanFormingUtility.AllReachableColonyItems(map, allowEvenIfReserved: true);
            var goods = new List<Thing>(reachableItems.Count + pawns.Count + (thingOnPawn ? pawns.Count * 2 : 0));
            goods.AddRange(reachableItems);
            goods.AddRange(pawns);

            if (thingOnPawn)
            {
                goods.AddRange(GetThingOnPawn(pawns));
            }

            return withTransferFilter ? FilterBeforeSendServer(goods) : goods;
        }

        private static List<Thing> GetThingOnPawn(IEnumerable<Thing> pawns)
        {
            var result = new List<Thing>();
            foreach (var thing in pawns)
            {
                if (thing is Pawn p)
                {
                    foreach (var item in p.EquippedWornOrInventoryThings)
                    {
                        result.Add(item);
                    }

                    if (p.carryTracker?.CarriedThing != null && p.carryTracker.CarriedThing.def.category != ThingCategory.Pawn)
                    {
                        result.Add(p.carryTracker.CarriedThing);
                    }
                }
            }
            return result;
        }

        public static List<Thing> GetAllThings(TradeThingsOnline storage)
        {
            using (NormalGameError())
            {
                var things = storage.TradeThings.Things;
                var res = new List<Thing>(things.Count);
                for (int i = 0; i < things.Count; i++)
                {
                    res.Add(things[i].CreateThing());
                }
                return res;
            }
        }

        public static CatchGameError NormalGameError() => new CatchGameError(errorText =>
            !errorText.Contains("during LoadingVars. pathRelToParent=/leader, parent")
            && !errorText.Contains("PostLoadInit on RimWorld.Pawn_IdeoTracker: System.NullReferenceException")
            );

        public static void ShortSetupForQuickTestPlay()
        {
            Current.Game = new Game();
            Current.Game.InitData = new GameInitData();
            Current.Game.Scenario = ScenarioDefOf.Crashlanded.scenario;
            Find.Scenario.PreConfigure();
            Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
            Current.Game.World = WorldGenerator.GenerateWorld(
                0.05f,
                GenText.RandomSeedString(),
                OverallRainfall.Normal,
                OverallTemperature.Normal,
                OverallPopulation.Little
                );
        }

        public static void SpawnSetupOnCaravan(Pawn pawn)
        {
            if (!pawn.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
            }
        }

        public static void DeSpawnSetupOnCaravan(Caravan caravan, Pawn pawn)
        {
            caravan.PawnsListForReading.Remove(pawn);
            if (pawn is IThingHolder && Find.ColonistBar != null)
            {
                Find.ColonistBar.MarkColonistsDirty();
            }
        }

        /// <summary>
        /// Пошук клітинки для вивантаження вантажу.
        /// ОПТИМІЗАЦІЯ: прямий розрахунок середнього за O(N) замість LINQ Aggregate.
        /// </summary>
        public static IntVec3 GetTradeCell(Map map)
        {
            var labelDumping = "DumpingStockpile".Translate();
            var labelDumping2 = "DumpingStockpileLabel".Translate();

            Zone bestZone = null;
            int bestScore = int.MinValue;

            var zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                int score = 0;

                bool isTrade = z.label.IndexOf("торг", StringComparison.OrdinalIgnoreCase) >= 0
                    || z.label.IndexOf("trad", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isTrade) score += 1000000;

                bool isDumping = z.label.IndexOf(labelDumping, StringComparison.OrdinalIgnoreCase) == 0
                    || z.label.IndexOf(labelDumping2, StringComparison.OrdinalIgnoreCase) == 0;
                if (!isDumping) score += 100000;

                score += Mathf.Clamp(z.Cells.Count, 0, 10000);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestZone = z;
                }
            }

            if (bestZone == null) return map.Center;

            var cells = bestZone.Cells;
            int count = cells.Count;
            if (count == 0) return map.Center;

            int sumX = 0;
            int sumZ = 0;
            for (int i = 0; i < count; i++)
            {
                sumX += cells[i].x;
                sumZ += cells[i].z;
            }

            return new IntVec3(sumX / count, 0, sumZ / count);
        }

        public static Func<IntVec3> GetAttackCells(Map map)
        {
            IntVec3 enterCell = FindNearEdgeCell(map, null);
            return () => CellFinder.RandomSpawnCellForPawnNear(enterCell, map, 4);
        }

        private static IntVec3 FindNearEdgeCell(Map map, Predicate<IntVec3> extraCellValidator)
        {
            Predicate<IntVec3> baseValidator = (IntVec3 x) => x.Standable(map) && !x.Fogged(map);
            Faction hostFaction = map.ParentFaction;
            if (CellFinder.TryFindRandomEdgeCellWith((IntVec3 x) => baseValidator(x) && (extraCellValidator == null || extraCellValidator(x)) && ((hostFaction != null && map.reachability.CanReachFactionBase(x, hostFaction)) || (hostFaction == null && map.reachability.CanReachBiggestMapEdgeDistrict(x))), map, CellFinder.EdgeRoadChance_Neutral, out var result))
            {
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            }
            if (extraCellValidator != null && CellFinder.TryFindRandomEdgeCellWith((IntVec3 x) => baseValidator(x) && extraCellValidator(x), map, CellFinder.EdgeRoadChance_Neutral, out result))
            {
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            }
            if (CellFinder.TryFindRandomEdgeCellWith(baseValidator, map, CellFinder.EdgeRoadChance_Neutral, out result))
            {
                return CellFinder.RandomClosewalkCellNear(result, map, 5);
            }
            Log.Warning("Could not find any valid edge cell.");
            return CellFinder.RandomCell(map);
        }

        public static IntVec3 SpawnCaravanPirate(Map map, List<ThingEntry> pawns, Action<Thing, ThingEntry> spawn = null)
        {
            var nextCell = GameUtils.GetAttackCells(map);
            return SpawnList(map, pawns, true, (p) => true, spawn, (p) => nextCell());
        }

        public static IntVec3 SpawnList<TE>(Map map, List<TE> pawns, bool attackCell, Func<TE, bool> getPirate, Action<Thing, TE> spawn = null, Func<Thing, IntVec3> getCell = null)
            where TE : ThingEntry
        {
            if (MainHelper.DebugMode) Loger.Log("SpawnList...");

            IntVec3 ret = new IntVec3();
            ModBaseData.RunMainThreadSync(() =>
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    var thing = pawns[i];
                    if (getPirate(thing)) thing.Affiliation = PawnAffiliation.Enemy;
                    if (MainHelper.DebugMode) Loger.Log("Prepare... " + thing.Affiliation);
                    var thin = thing.CreateThing();

                    var cell = getCell != null ? getCell(thin) : thin.Position;
                    if (i == 0) ret = cell;

                    if (thin is Pawn pawn)
                    {
                        try
                        {
                            GenSpawn.Spawn(pawn, cell, map);
                        }
                        catch (Exception exp)
                        {
                            Loger.Log("SpawnList Exception " + thing.Name + ": " + exp, Loger.LogLevel.ERROR);
                            Thread.Sleep(5);
                            GenSpawn.Spawn(pawn, cell, map);
                        }
                    }
                    else
                    {
                        GenDrop.TryDropSpawn(thin, cell, map, ThingPlaceMode.Near, out _, null);
                    }

                    spawn?.Invoke(thin, thing);
                }
            });
            return ret;
        }

        public static void PawnDestroy(Pawn pawn)
        {
            pawn.Destroy(DestroyMode.Vanish);
            Find.WorldPawns.RemovePawn(pawn);
        }

        public static void ApplyState(Thing thing, AttackThingState state, bool pawnHealthStateDead = false)
        {
            if (state.StackCount > 0 && thing.stackCount != state.StackCount)
            {
                thing.stackCount = state.StackCount;
            }

            if (thing.Position.x != state.Position.x || thing.Position.z != state.Position.z)
            {
                thing.Position = state.Position.Get();
                if (thing is Pawn pawn)
                {
                    if (CellFinder.TryFindBestPawnStandCell(pawn, out var cell))
                    {
                        pawn.Position = cell;
                        pawn.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
                    }
                }
            }

            if (thing is Fire fire)
            {
                fire.fireSize = (float)state.HitPoints / 10000f;
            }
            else if (thing.def.useHitPoints)
            {
                thing.HitPoints = state.HitPoints;
            }

            if (thing is Pawn targetPawn)
            {
                if ((int)targetPawn.health.State != (int)state.DownState)
                {
                    if (targetPawn.health.State == PawnHealthState.Dead)
                    {
                        Loger.Log("Client ApplyState Set pawn state is Dead! Error to change on " + state.DownState);
                    }
                    else if (state.DownState == AttackThingState.PawnHealthState.Dead)
                    {
                        if (pawnHealthStateDead)
                        {
                            HealthUtility.DamageUntilDead(targetPawn);
                        }
                    }
                    else if (state.DownState == AttackThingState.PawnHealthState.Down)
                    {
                        HealthUtility.DamageUntilDowned(targetPawn, false);
                    }
                    else
                    {
                        targetPawn.health.Notify_Resurrected();
                    }
                }
            }
        }

        private static bool DialodShowing = false;
        private static readonly Queue<Action> DialodQueue = new Queue<Action>();

        public static void ShowDialodOKCancel(string title, string text, Action ActOK, Action ActCancel, GlobalTargetInfo? target = null, string AltText = null, Action ActAlt = null)
        {
            DiaNode diaNode = new DiaNode(text);

            if (target != null)
            {
                var diaOptionT = new DiaOption("JumpToLocation".Translate())
                {
                    action = () => CameraJumper.TryJumpAndSelect(target.Value)
                };
                diaNode.options.Add(diaOptionT);
            }

            DiaOption diaOption = new DiaOption("OCity_GameUtils_Ok".Translate())
            {
                action = () => { ActOK(); DialodQueueGoNext(); },
                resolveTree = true
            };
            diaNode.options.Add(diaOption);

            if (!string.IsNullOrEmpty(AltText) && ActAlt != null)
            {
                diaOption = new DiaOption(AltText)
                {
                    action = () => { ActAlt(); DialodQueueGoNext(); },
                    resolveTree = true
                };
                diaNode.options.Add(diaOption);
            }

            if (ActCancel != null)
            {
                diaOption = new DiaOption("RejectLetter".Translate())
                {
                    action = () => { ActCancel(); DialodQueueGoNext(); },
                    resolveTree = true
                };
                diaNode.options.Add(diaOption);
            }

            Action show = () => Find.WindowStack.Add(new Dialog_NodeTreeWithFactionInfo(diaNode, null, true, true, title));

            lock (DialodQueue)
            {
                if (!DialodShowing)
                {
                    DialodShowing = true;
                    show();
                }
                else
                {
                    DialodQueue.Enqueue(show);
                }
            }
        }

        private static void DialodQueueGoNext()
        {
            lock (DialodQueue)
            {
                if (DialodQueue.Count > 0)
                {
                    DialodQueue.Dequeue()();
                }
                else
                {
                    DialodShowing = false;
                }
            }
        }

        public static void DrawLabel(Rect canvas, Color main, Color background, int label)
        {
            GUI.color = main;
            GUI.DrawTexture(canvas, CircleFill);

            if (background != main)
            {
                GUI.color = background;
                GUI.DrawTexture(canvas.ContractedBy(2f), CircleFill);
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(canvas, label.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Пошук та вибір речей заданого def на домашніх картах гравця.
        /// ОПТИМІЗАЦІЯ: ліквідовано зайві виклики LINQ Where, Sum, OrderByDescending.
        /// </summary>
        public static int FindThings(ThingDef def, int select, bool getMaxByMap, out Dictionary<Thing, int> thingsSelect)
        {
            int countAll = 0;
            int countMax = 0;
            var maps = new List<Pair<List<Thing>, int>>();
            var gameMaps = Current.Game.Maps;

            for (int i = 0; i < gameMaps.Count; i++)
            {
                var m = gameMaps[i];
                if (m.IsPlayerHome)
                {
                    var allMThings = GetAllThings(m);
                    var things = new List<Thing>();
                    int c = 0;
                    for (int j = 0; j < allMThings.Count; j++)
                    {
                        var t = allMThings[j];
                        if (t.def == def)
                        {
                            things.Add(t);
                            c += t.stackCount;
                        }
                    }

                    maps.Add(new Pair<List<Thing>, int>(things, c));
                    countAll += c;
                    if (countMax < c) countMax = c;
                }
            }

            int count = getMaxByMap ? countMax : countAll;
            if (select == 0 || select > count) select = count;
            thingsSelect = new Dictionary<Thing, int>();

            if (select > 0)
            {
                var selectProcess = select;
                while (maps.Count > 0 && selectProcess > 0)
                {
                    int bestIdx = 0;
                    int bestVal = maps[0].Second;
                    for (int j = 1; j < maps.Count; j++)
                    {
                        if (maps[j].Second > bestVal)
                        {
                            bestVal = maps[j].Second;
                            bestIdx = j;
                        }
                    }

                    var m = maps[bestIdx];
                    maps.RemoveAt(bestIdx);

                    for (int j = 0; j < m.First.Count; j++)
                    {
                        var thing = m.First[j];
                        var sc = thing.stackCount;
                        if (sc < selectProcess)
                        {
                            thingsSelect.Add(thing, sc);
                            selectProcess -= sc;
                        }
                        else
                        {
                            thingsSelect.Add(thing, selectProcess);
                            selectProcess = 0;
                            break;
                        }
                    }
                }
            }
            return count;
        }

        public static int FindThings(ThingDef def, int destroy, bool getMaxByMap)
        {
            int countAll = 0;
            int countMax = 0;
            var maps = new List<Pair<List<Thing>, int>>();
            var gameMaps = Current.Game.Maps;

            for (int i = 0; i < gameMaps.Count; i++)
            {
                var m = gameMaps[i];
                if (m.IsPlayerHome)
                {
                    var allMThings = GetAllThings(m);
                    var things = new List<Thing>();
                    int c = 0;
                    for (int j = 0; j < allMThings.Count; j++)
                    {
                        var t = allMThings[j];
                        if (t.def == def)
                        {
                            things.Add(t);
                            c += t.stackCount;
                        }
                    }

                    maps.Add(new Pair<List<Thing>, int>(things, c));
                    countAll += c;
                    if (countMax < c) countMax = c;
                }
            }

            int count = getMaxByMap ? countMax : countAll;
            if (destroy > 0 && destroy < count)
            {
                var destroyProcess = destroy;
                while (maps.Count > 0 && destroyProcess > 0)
                {
                    int bestIdx = 0;
                    int bestVal = maps[0].Second;
                    for (int j = 1; j < maps.Count; j++)
                    {
                        if (maps[j].Second > bestVal)
                        {
                            bestVal = maps[j].Second;
                            bestIdx = j;
                        }
                    }

                    var m = maps[bestIdx];
                    maps.RemoveAt(bestIdx);

                    for (int j = 0; j < m.First.Count; j++)
                    {
                        var thing = m.First[j];
                        if (thing.stackCount < destroyProcess)
                        {
                            destroyProcess -= thing.stackCount;
                            thing.Destroy();
                        }
                        else
                        {
                            thing.SplitOff(destroyProcess);
                            destroyProcess = 0;
                            break;
                        }
                    }
                }
            }
            return count;
        }

        public static StorytellerDef GetStorytallerByName(string name)
        {
            var defs = DefDatabase<StorytellerDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                if (defs[i].defName == name)
                    return defs[i];
            }
            return null;
        }

        public static Dictionary<string, Scenario> AllowedScenarios()
        {
            var res = new Dictionary<string, Scenario>();
            var allDefs = DefDatabase<ScenarioDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                var allDef = allDefs[i];
                if (allDef.modContentPack?.Name != "OnlineCity fork") continue;
                if (!res.ContainsKey(allDef.defName))
                {
                    res.Add(allDef.defName, allDef.scenario);
                }
            }
            return res;
        }

        public static Command_Action CommandShowMap(BaseOnline that)
        {
            if (SessionClientController.Data.GeneralSettings.ColonyScreenEnable)
            {
                var command_Action = new Command_Action
                {
                    defaultLabel = "CommandShowMap".Translate(),
                    defaultDesc = "CommandShowMapDesc".Translate(),
                    icon = GeneralTexture.BaseOnlineButtonShowMap
                };

                var keyColonyScreen = "cs_" + that.OnlineWObject.LoginOwner + "@" + that.OnlineWObject.PlaceServerId;
                var time = GeneralTexture.Get.GetLoadTimeByName(keyColonyScreen);
                bool isNotCheck = GeneralTexture.Get.IsNotCheckByLoadTime(time);
                bool isNotData = GeneralTexture.Get.IsNotDataByLoadTime(time);

                if (!that.IsOnline && that.ImageBaseWhenOwnerOffline != null)
                {
                    command_Action.defaultDesc = "OC_ImageBase1".Translate() + " " + that.OnlineWObject.LoginOwner;
                }
                else if (isNotData)
                {
                    command_Action.defaultDesc = "OC_DataNotAvailable".Translate();
                    command_Action.disabled = true;
                }
                else if (isNotCheck)
                {
                    command_Action.defaultDesc = "OC_ImageBase2".Translate() + " " + that.OnlineWObject.LoginOwner;
                }
                else if (GeneralTexture.UpdateSecondColonyScreen - (int)time.TotalSeconds < 0)
                {
                    command_Action.defaultDesc = "OC_ImageBase1".Translate() + " " + that.OnlineWObject.LoginOwner
                        + " " + Environment.NewLine + "OC_ImageBase4".Translate();
                }
                else
                {
                    var showSec = GeneralTexture.UpdateSecondColonyScreen - (int)time.TotalSeconds;
                    showSec -= showSec % 5 + 5;
                    command_Action.defaultDesc = "OC_ImageBase1".Translate() + " " + that.OnlineWObject.LoginOwner
                        + ". " + Environment.NewLine + "OC_ImageBase6".Translate() + " " + showSec + " " + "OC_Seconds".Translate();
                }

                command_Action.action = delegate
                {
                    var formView = new Dialog_ViewImage();
                    formView.BeforeDrow = () =>
                    {
                        if (!that.IsOnline && that.ImageBaseWhenOwnerOffline != null)
                        {
                            formView.BeforeDrow = null;
                            formView.ImageShow = that.ImageBaseWhenOwnerOffline;
                        }
                        else
                        {
                            var iconImage = GeneralTexture.Get.ByName(keyColonyScreen);
                            var isLoading = GeneralTexture.Get.IsLoadingByName(keyColonyScreen);
                            if (iconImage != GeneralTexture.Null)
                            {
                                formView.ImageShow = iconImage;
                                that.ImageBaseWhenOwnerOffline = iconImage;
                                if (!isLoading)
                                {
                                    formView.TextShowOnUp = false;
                                    formView.BeforeDrow = null;
                                }
                                else
                                {
                                    formView.TextShowOnUp = true;
                                }
                            }
                            else
                            {
                                if (!isLoading)
                                {
                                    formView.TextShowOnUp = false;
                                    formView.BeforeDrow = null;
                                    formView.TextShow = "OC_DataNotAvailable".Translate();
                                }
                                else
                                {
                                    formView.TextShowOnUp = true;
                                }
                            }
                        }
                    };
                    Find.WindowStack.Add(formView);
                };
                return command_Action;
            }
            return null;
        }

        public static int DistanceBetweenTile(int start, int end)
        {
            var key = new Pair<int, int>(start, end);
            if (!SessionClientController.Data.DistanceBetweenTileCache.TryGetValue(key, out int res))
            {
                res = Find.WorldGrid.TraversalDistanceBetween(start, end);
                SessionClientController.Data.DistanceBetweenTileCache[key] = res;
            }
            return res;
        }

        public static List<Thing> GetCashlessBalanceThingList(float cashlessBalance)
        {
            if (cashlessBalance > 0) return new List<Thing>(1) { GetCashlessBalanceThing(cashlessBalance) };
            return new List<Thing>(0);
        }

        public static Thing GetCashlessBalanceThing(float cashlessBalance)
        {
            var thing = ThingMaker.MakeThing(MainHelper.CashlessThingDef);
            thing.stackCount = (int)Math.Truncate(cashlessBalance);
            return thing;
        }

        private static Texture2D _TextureCashlessBalance = null;
        public static Texture2D TextureCashlessBalance
        {
            get
            {
                if (_TextureCashlessBalance == null)
                {
                    _TextureCashlessBalance = Widgets.GetIconFor(MainHelper.CashlessThingDef);
                }
                return _TextureCashlessBalance;
            }
        }

        public static bool isBuilding(Thing thing)
        {
            if (thing.def.category == ThingCategory.Building && thing.def.destroyable)
            {
                if (thing.def.building.IsDeconstructible && thing.def.building.uninstallWork > 0)
                    return true;
            }
            return false;
        }

        public static string GetHillinessLabel(Hilliness h)
        {
            switch (h)
            {
                case Hilliness.Flat: return "Hilliness_Flat";
                case Hilliness.SmallHills: return "Hilliness_SmallHills";
                case Hilliness.LargeHills: return "Hilliness_LargeHills";
                case Hilliness.Mountainous: return "Hilliness_Mountainous";
                case Hilliness.Impassable: return "Hilliness_Impassable";
                default: return h.ToString();
            }
        }

        public static void CameraJump(WorldObject wo)
        {
            var ti = new GlobalTargetInfo(wo);
            CameraJumper.TryJumpAndSelect(ti);
        }

        public static void CameraJump(int tile)
        {
            var ti = new GlobalTargetInfo(tile);
            CameraJumper.TryJumpAndSelect(ti);
        }

        public static bool CameraJumpWorldObject(int tile)
        {
            var list = ExchengeUtils.WorldObjectsByTile(tile);
            WorldObject target = null;

            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o is TradeThingsOnline) { target = o; break; }
                if ((o.Faction?.IsPlayer ?? false) && (o is Settlement || o is Caravan)) { target = o; }
                else if (target == null && o is WorldObjectBaseOnline) { target = o; }
            }

            if (target == null && list.Count > 0) target = list[0];
            if (target == null) return false;

            CameraJump(target);
            return true;
        }
    }
}