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
using System.Xml;
using UnityEngine;
using Util;
using Verse;
using Verse.AI;

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

        private static readonly HashSet<ThingTrade> ExceptionDravLineThingTrade = new HashSet<ThingTrade>();
        private static readonly HashSet<Thing> ExceptionDravLineThing = new HashSet<Thing>();

        private static int BugNum = 0;

        public static void GetBug()
        {
            var dir = Loger.PathLog.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            return texture;
        }

        public static void DravLineThing(Rect rect, ThingTrade thing, bool withInfo)
        {
            DravLineThing(rect, thing, withInfo, Color.white);
        }

        public static void DravLineThing(Rect rect, ThingTrade thing, bool withInfo, Color labelColor, float xi = 24f, float yi = 0)
        {
            if (thing == null || ExceptionDravLineThingTrade.Contains(thing)) return;

            try
            {
                if (thing.Def?.race?.Humanlike ?? false)
                {
                    var position = new Rect(rect.x, rect.y, 24f, 24f);
                    GUI.DrawTexture(position, GeneralTexture.IconHuman);
                }
                else if (thing.Def != null)
                {
                    Widgets.ThingIcon(rect, thing.Def);
                }

                if (thing.Def != null)
                {
                    if (Mouse.IsOver(rect))
                    {
                        string tip = string.IsNullOrEmpty(thing.StuffName)
                            ? thing.Def.LabelCap.ToString()
                            : (thing.Def.LabelCap + "OCity_GameUtils_From".Translate() + thing.StuffDef.LabelAsStuff).ToString();
                        TooltipHandler.TipRegion(rect, tip);
                    }

                    if (withInfo)
                    {
                        GUI.color = labelColor;
                        if (string.IsNullOrEmpty(thing.StuffName))
                            Widgets.InfoCardButton(rect.x + xi, rect.y + yi, thing.Def);
                        else
                            Widgets.InfoCardButton(rect.x + xi, rect.y + yi, thing.Def, thing.StuffDef);
                        GUI.color = Color.white;
                    }
                }
            }
            catch
            {
                ExceptionDravLineThingTrade.Add(thing);
                throw;
            }
        }

        public static void DravLineThing(Rect rect, Thing thing, bool withInfo, float xi = 24f, float yi = 0)
        {
            if (thing == null || ExceptionDravLineThing.Contains(thing)) return;

            try
            {
                Widgets.ThingIcon(rect, thing);
                if (withInfo) Widgets.InfoCardButton(rect.x + xi, rect.y + yi, thing);

                if (Mouse.IsOver(rect))
                {
                    string text = thing.LabelCapNoCount.ToString();
                    string tipDescription = thing.DescriptionFlavor.ToString();
                    string fullText = !string.IsNullOrEmpty(tipDescription) ? text + ": " + tipDescription : text;
                    TooltipHandler.TipRegion(rect, fullText);
                }
            }
            catch
            {
                ExceptionDravLineThing.Add(thing);
                throw;
            }
        }

        public static void DravLineThing(Rect rect, ThingDef thing, bool withInfo)
        {
            if (thing == null) return;
            Widgets.ThingIcon(rect, thing);
            if (withInfo) Widgets.InfoCardButton(rect.x + 24f, rect.y, thing);

            if (Mouse.IsOver(rect))
            {
                string text = thing.LabelCap.ToString();
                string tipDescription = thing.DescriptionDetailed.ToString();
                string fullText = !string.IsNullOrEmpty(tipDescription) ? text + ": " + tipDescription : text;
                TooltipHandler.TipRegion(rect, fullText);
            }
        }

        public static void DravLineThing(Rect rectLine, Thing thing, Color labelColor)
        {
            if (thing == null) return;

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

            if (Mouse.IsOver(rectLine))
            {
                string text = thing.LabelCapNoCount.ToString();
                string tipDescription = thing.DescriptionFlavor.ToString();
                string fullText = !string.IsNullOrEmpty(tipDescription) ? text + ": " + tipDescription : text;
                TooltipHandler.TipRegion(rectLine, fullText);
            }
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

        private static List<TransferableOneWay> ChechToTradeDo(IEnumerable<ThingTrade> targets, IEnumerable<Thing> allThings, IEnumerable<Thing> altThings, ref int rate, bool setRect, bool incomplete = false, bool setTradeCount = true)
        {
            bool result = true;
            var selects = new List<TransferableOneWay>();

            var source = new Dictionary<Thing, int>();
            var sourceKeys = new List<Thing>();
            foreach (var item in allThings)
            {
                if (item == null) continue;
                if (!source.ContainsKey(item))
                {
                    source[item] = item.stackCount;
                    sourceKeys.Add(item);
                }
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
                    if (!sourcealt.ContainsKey(item))
                    {
                        sourcealt[item] = item.stackCount;
                        sourcealtKeys.Add(item);
                    }
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
            if (SessionClientController.Data == null || SessionClientController.Data.IsAdmin || !SessionClientController.Data.ProtectingNovice) return false;

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

        public static List<Thing> GetAllThings(Caravan caravan, bool thingOnPawn = false, bool withTransferFilter = true, ThingDef onlyDef = null)
        {
            var rawPawns = caravan.PawnsListForReading;
            var pawns = withTransferFilter ? FilterBeforeSendServer(rawPawns) : new List<Thing>(rawPawns);

            List<Thing> goods;
            if (thingOnPawn)
            {
                var onPawn = GetThingOnPawn(pawns, onlyDef);
                goods = new List<Thing>(onPawn.Count + pawns.Count);
                goods.AddRange(onPawn);
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (onlyDef == null || pawns[i].def == onlyDef) goods.Add(pawns[i]);
                }
            }
            else
            {
                var inv = CaravanInventoryUtility.AllInventoryItems(caravan);
                goods = new List<Thing>(inv.Count + pawns.Count);
                for (int i = 0; i < inv.Count; i++)
                {
                    if (onlyDef == null || inv[i].def == onlyDef) goods.Add(inv[i]);
                }
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (onlyDef == null || pawns[i].def == onlyDef) goods.Add(pawns[i]);
                }
            }

            return withTransferFilter ? FilterBeforeSendServer(goods) : goods;
        }

        public static List<Thing> GetAllThings(Map map, bool thingOnPawn = false, bool withTransferFilter = true, ThingDef onlyDef = null)
        {
            var rawPawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            var pawns = withTransferFilter ? FilterBeforeSendServer(rawPawns) : new List<Thing>(rawPawns);

            var reachableItems = CaravanFormingUtility.AllReachableColonyItems(map, allowEvenIfReserved: true);
            var goods = new List<Thing>(onlyDef != null ? 32 : reachableItems.Count + pawns.Count + (thingOnPawn ? pawns.Count * 2 : 0));

            for (int i = 0; i < reachableItems.Count; i++)
            {
                var item = reachableItems[i];
                if (onlyDef == null || item.def == onlyDef)
                {
                    goods.Add(item);
                }
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (onlyDef == null || pawns[i].def == onlyDef)
                {
                    goods.Add(pawns[i]);
                }
            }

            if (thingOnPawn)
            {
                goods.AddRange(GetThingOnPawn(pawns, onlyDef));
            }

            return withTransferFilter ? FilterBeforeSendServer(goods) : goods;
        }

        private static List<Thing> GetThingOnPawn(IEnumerable<Thing> pawns, ThingDef onlyDef = null)
        {
            var result = new List<Thing>();
            foreach (var thing in pawns)
            {
                if (thing is Pawn p)
                {
                    foreach (var item in p.EquippedWornOrInventoryThings)
                    {
                        if (onlyDef == null || item.def == onlyDef)
                        {
                            result.Add(item);
                        }
                    }

                    if (p.carryTracker?.CarriedThing != null && p.carryTracker.CarriedThing.def.category != ThingCategory.Pawn)
                    {
                        var carried = p.carryTracker.CarriedThing;
                        if (onlyDef == null || carried.def == onlyDef)
                        {
                            result.Add(carried);
                        }
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
                    var th = things[i].CreateThing();
                    if (th is Pawn p) ReassignUniquePawnSubIds(p);
                    res.Add(th);
                }
                return res;
            }
        }

        public static CatchGameError NormalGameError() => new CatchGameError(errorText =>
            !errorText.Contains("during LoadingVars. pathRelToParent=/leader, parent")
            && !errorText.Contains("PostLoadInit on RimWorld.Pawn_IdeoTracker: System.NullReferenceException")
            );

        public static void ReassignUniquePawnSubIds(Pawn pawn)
        {
            if (pawn == null || Find.UniqueIDsManager == null) return;
            try
            {
                var hediffs = pawn.health?.hediffSet?.hediffs;
                if (hediffs != null)
                {
                    for (int i = 0; i < hediffs.Count; i++)
                    {
                        if (hediffs[i] != null)
                        {
                            hediffs[i].loadID = Find.UniqueIDsManager.GetNextHediffID();
                        }
                    }
                }

                if (ModsConfig.BiotechActive && pawn.genes != null)
                {
                    var genes = pawn.genes.GenesListForReading;
                    if (genes != null)
                    {
                        for (int i = 0; i < genes.Count; i++)
                        {
                            if (genes[i] != null)
                            {
                                genes[i].loadID = Find.UniqueIDsManager.GetNextGeneID();
                            }
                        }
                    }
                }
            }
            catch { }
        }

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

        private static IntVec3 ClampAwayFromEdge(IntVec3 cell, Map map, int minBorder = 7)
        {
            int x = Mathf.Clamp(cell.x, minBorder, map.Size.x - 1 - minBorder);
            int z = Mathf.Clamp(cell.z, minBorder, map.Size.z - 1 - minBorder);
            var clamped = new IntVec3(x, 0, z);
            if (clamped.InBounds(map) && clamped.Standable(map) && !clamped.Fogged(map))
            {
                return clamped;
            }
            if (CellFinder.TryFindRandomCellNear(clamped, map, 6, c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map)
                && c.x >= 4 && c.x <= map.Size.x - 5 && c.z >= 4 && c.z <= map.Size.z - 5, out var valid))
            {
                return valid;
            }
            return cell;
        }

        public static Func<IntVec3> GetAttackCells(Map map)
        {
            IntVec3 enterCell = ClampAwayFromEdge(FindNearEdgeCell(map, null), map, 8);
            return () =>
            {
                var c = CellFinder.RandomSpawnCellForPawnNear(enterCell, map, 4);
                return ClampAwayFromEdge(c, map, 5);
            };
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
                using (NormalGameError())
                {
                    IntVec3 baseEdgeCell = IntVec3.Invalid;
                    if (attackCell && getCell == null)
                    {
                        if (!RCellFinder.TryFindRandomPawnEntryCell(out baseEdgeCell, map, CellFinder.EdgeRoadChance_Hostile))
                        {
                            baseEdgeCell = CellFinder.RandomEdgeCell(map);
                        }
                        baseEdgeCell = ClampAwayFromEdge(baseEdgeCell, map, 8);
                    }

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        var thing = pawns[i];
                        if (thing == null) continue;

                        try
                        {
                            if (thing.Affiliation != PawnAffiliation.Thing)
                            {
                                thing.Affiliation = getPirate(thing) ? PawnAffiliation.Enemy : PawnAffiliation.Colonist;

                                if (!string.IsNullOrEmpty(thing.Data) && thing.Data.IndexOf("<jobs>", StringComparison.Ordinal) >= 0)
                                {
                                    thing.Data = GameXMLUtils.ReplaceByTag(thing.Data, "jobs", "<jobQueue><jobs /></jobQueue>");
                                }
                            }

                            if (MainHelper.DebugMode) Loger.Log("Prepare... " + thing.Affiliation);
                            var thin = thing.CreateThing();
                            if (thin == null) continue;

                            if (thin is Pawn createdPawn)
                            {
                                ReassignUniquePawnSubIds(createdPawn);
                            }
                            else if (thin is Corpse createdCorpse && createdCorpse.InnerPawn != null)
                            {
                                ReassignUniquePawnSubIds(createdCorpse.InnerPawn);
                            }

                            IntVec3 cell;
                            if (getCell != null)
                            {
                                cell = getCell(thin);
                            }
                            else if (attackCell && baseEdgeCell.IsValid)
                            {
                                cell = ClampAwayFromEdge(CellFinder.RandomClosewalkCellNear(baseEdgeCell, map, 5), map, 5);
                            }
                            else
                            {
                                cell = thin.Position;
                            }

                            if (!cell.InBounds(map)) cell = map.Center;
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
                                    GenSpawn.Spawn(pawn, cell, map);
                                }

                                if (pawn.Faction == Faction.OfPlayer && map.fogGrid != null)
                                {
                                    try
                                    {
                                        if (map.fogGrid.IsFogged(pawn.Position))
                                        {
                                            FloodFillerFog.FloodUnfog(pawn.Position, map);
                                        }
                                        foreach (var adj in GenAdj.CellsAdjacent8Way(pawn))
                                        {
                                            if (adj.InBounds(map) && map.fogGrid.IsFogged(adj))
                                            {
                                                FloodFillerFog.FloodUnfog(adj, map);
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                            else
                            {
                                GenDrop.TryDropSpawn(thin, cell, map, ThingPlaceMode.Near, out _, null);
                            }

                            spawn?.Invoke(thin, thing);
                        }
                        catch (Exception exItem)
                        {
                            Loger.Log("SpawnList item skip exception (" + thing.Name + "): " + exItem, Loger.LogLevel.ERROR);
                        }
                    }
                }
            });
            return ret;
        }

        public static void PawnDestroy(Pawn pawn)
        {
            if (pawn == null) return;
            if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
            Find.WorldPawns?.RemovePawn(pawn);
        }

        /// <summary>
        /// Безпечне вивантаження тимчасової карти бою для RimWorld 1.1–1.4.
        /// </summary>
        private static MethodInfo _deinitMapMethod;
        private static int _deinitMapParamCount = -1;

        public static void DeinitAndRemoveMapSafe(Map map)
        {
            if (map == null || Current.Game == null) return;

            try
            {
                var parent = map.Parent;
                if (_deinitMapParamCount < 0)
                {
                    var methods = typeof(Game).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        if (methods[i].Name == "DeinitAndRemoveMap")
                        {
                            _deinitMapMethod = methods[i];
                            _deinitMapParamCount = methods[i].GetParameters().Length;
                            break;
                        }
                    }
                    if (_deinitMapParamCount < 0) _deinitMapParamCount = 0;
                }

                if (_deinitMapMethod != null && Current.Game.Maps.Contains(map))
                {
                    if (_deinitMapParamCount == 1)
                        _deinitMapMethod.Invoke(Current.Game, new object[] { map });
                    else if (_deinitMapParamCount == 2)
                        _deinitMapMethod.Invoke(Current.Game, new object[] { map, false });
                }

                if (parent != null && Find.WorldObjects != null && Find.WorldObjects.Contains(parent))
                {
                    Find.WorldObjects.Remove(parent);
                }
            }
            catch (Exception ex)
            {
                Loger.Log("DeinitAndRemoveMapSafe warning: " + ex.Message, Loger.LogLevel.WARNING);
                try
                {
                    if (map.Parent != null && Find.WorldObjects != null && Find.WorldObjects.Contains(map.Parent))
                    {
                        Find.WorldObjects.Remove(map.Parent);
                    }
                }
                catch { }
            }
        }

        public static void ApplyState(Thing thing, AttackThingState state, bool pawnHealthStateDead = false)
        {
            if (thing == null || state == null || thing.Destroyed) return;

            if (state.StackCount > 0 && thing.stackCount != state.StackCount)
            {
                thing.stackCount = state.StackCount;
            }

            if (thing.Spawned && thing.Map != null)
            {
                var targetPos = state.Position.Get();
                if (targetPos.InBounds(thing.Map) && (thing.Position.x != state.Position.x || thing.Position.z != state.Position.z))
                {
                    if (thing is Pawn pawn)
                    {
                        float distSq = pawn.Position.DistanceToSquared(targetPos);
                        bool isDownOrDead = pawn.Dead || pawn.Downed || state.DownState != AttackThingState.PawnHealthState.Mobile;

                        if (isDownOrDead || distSq > 64f)
                        {
                            pawn.Position = targetPos;
                            pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: true);
                        }
                        else if (distSq >= 1f && pawn.pather != null && !pawn.pather.Moving && pawn.jobs != null)
                        {
                            // Замість миттєвої зміни координат запускаємо плавний JobDefOf.Goto,
                            // щоб PawnTweener плавно анімував переміщення між клітинками
                            var gotoJob = new Job(JobDefOf.Goto, targetPos)
                            {
                                playerForced = false,
                                expiryInterval = 240,
                                checkOverrideOnExpire = true
                            };
                            pawn.jobs.StartJob(gotoJob, JobCondition.InterruptForced);
                        }
                    }
                    else
                    {
                        thing.Position = targetPos;
                    }
                }

                if (thing.Rotation.AsByte != state.Rotation && (!(thing is Pawn pRot) || pRot.pather == null || !pRot.pather.Moving))
                {
                    thing.Rotation = new Rot4(state.Rotation);
                }
            }

            if (thing is Fire fire)
            {
                fire.fireSize = (float)state.HitPoints / 10000f;
            }
            else if (thing.def.useHitPoints && state.HitPoints > 0)
            {
                thing.HitPoints = state.HitPoints;
            }

            if (thing is Pawn targetPawn && targetPawn.health != null)
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
                            targetPawn.pather?.StopDead();
                            HealthUtility.DamageUntilDead(targetPawn);
                        }
                    }
                    else if (state.DownState == AttackThingState.PawnHealthState.Down)
                    {
                        targetPawn.pather?.StopDead();
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
                if (DialodShowing && Find.WindowStack != null && !Find.WindowStack.IsOpen<Dialog_NodeTreeWithFactionInfo>())
                {
                    DialodShowing = false;
                }

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
                    var things = GetAllThings(m, onlyDef: def);
                    int c = 0;
                    for (int j = 0; j < things.Count; j++)
                    {
                        c += things[j].stackCount;
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
                    var things = GetAllThings(m, onlyDef: def);
                    int c = 0;
                    for (int j = 0; j < things.Count; j++)
                    {
                        c += things[j].stackCount;
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
            if (SessionClientController.Data != null && SessionClientController.Data.GeneralSettings.ColonyScreenEnable)
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
            var key = new Pair<int, int>(Math.Min(start, end), Math.Max(start, end));

            if (SessionClientController.Data?.DistanceBetweenTileCache != null)
            {
                if (!SessionClientController.Data.DistanceBetweenTileCache.TryGetValue(key, out int res))
                {
                    res = Find.WorldGrid.TraversalDistanceBetween(start, end);
                    SessionClientController.Data.DistanceBetweenTileCache[key] = res;
                }
                return res;
            }

            return Find.WorldGrid.TraversalDistanceBetween(start, end);
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