using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses.Harmony;
using System;
using System.Collections.Generic;
using Transfer;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Утиліти для обробки біржових операцій, переміщення предметів,
    /// розрахунку логістики та взаємодії між караванами й поселеннями.
    /// </summary>
    public static class ExchengeUtils
    {
        public static IEnumerable<WorldObject> GetWorldObjectsForTrade()
        {
            return UpdateWorldController.WorldObject_TradeOrdersOnline;
        }

        public static List<WorldObject> WorldObjectsByTile(int tileID)
        {
            var result = new List<WorldObject>();
            foreach (var obj in Find.WorldObjects.ObjectsAt(tileID))
            {
                result.Add(obj);
            }
            return result;
        }

        /// <summary>
        /// Повертає всі ігрові об'єкти гравця на карті світу (поселення та каравани).
        /// ОПТИМІЗАЦІЯ: прямий цикл замість ланцюжка Where().ToList().
        /// </summary>
        public static List<WorldObject> WorldObjectsPlayer()
        {
            var all = UpdateWorldController.allWorldObjects;
            if (all == null) return new List<WorldObject>(0);

            var list = new List<WorldObject>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o != null && (o is Settlement || o is Caravan) && (o.Faction?.IsPlayer ?? false))
                {
                    list.Add(o);
                }
            }
            return list;
        }

        /// <summary>
        /// Розраховує дистанцію та вартість доставки вантажу між об'єктами світу.
        /// </summary>
        public static string CargoDeliveryCalc(WorldObject fromWorldObject, WorldObject toWorldObject, List<ThingTrade> things, out int cost, out int dist)
        {
            dist = GameUtils.DistanceBetweenTile(fromWorldObject.Tile, toWorldObject.Tile);

            float totalCost = 0;
            if (things != null)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    totalCost += t.GameCost * t.Count;
                }
            }

            cost = (int)(totalCost * SessionClientController.Data.GeneralSettings.ExchengeCostCargoDelivery / 1000f * dist / 100f);
            if (dist > 0 && cost <= 0) cost = 1;

            return $"{fromWorldObject.LabelShortCap} -> {toWorldObject?.LabelShortCap} "
                + "OCity_ExchengeUtils_Distance".Translate() + " " + dist + ", "
                + "OCity_ExchengeUtils_Cost".Translate() + " " + cost;
        }

        public static bool CargoDelivery(WorldObject fromWorldObject, WorldObject toWorldObject, List<ThingTrade> things, Action finish = null)
        {
            try
            {
                Loger.Log("Client CargoDelivery: " + CargoDeliveryCalc(fromWorldObject, toWorldObject, things, out int cost, out int dist), Loger.LogLevel.EXCHANGE);
                return CargoDelivery(fromWorldObject.Tile, toWorldObject.Tile, things, cost, dist);
            }
            finally
            {
                finish?.Invoke();
            }
        }

        private static bool CargoDelivery(int tileFrom, int tileTo, List<ThingTrade> things, int cost, int dist)
        {
            if (tileFrom == tileTo || tileFrom == 0 || tileTo == 0 || cost == 0 || dist == 0) return false;

            bool result = false;
            SessionClientController.Command((connect) =>
            {
                result = connect.ExchengeStorage(null, things, tileFrom, tileTo, cost, dist);
            });

            return result;
        }

        public static bool MoveSelectThings(WorldObject fromWorldObject, WorldObject toWorldObject, List<TransferableOneWay> selectTow, Action finish = null)
        {
            var select = selectTow.TransferableOneWaysToDictionary();
            return MoveSelectThings(fromWorldObject, toWorldObject, select, finish);
        }

        /// <summary>
        /// Переміщує вибрані речі між об'єктами світу (поселення, каравани, біржові сховища).
        /// </summary>
        public static bool MoveSelectThings(WorldObject fromWorldObject, WorldObject toWorldObject, Dictionary<Thing, int> select, Action finish = null)
        {
            try
            {
                if (SessionClientController.Data.BackgroundSaveGameOff)
                {
                    Loger.Log("Client ExchengeEdit Cancel BackgroundSaveGameOff", Loger.LogLevel.EXCHANGE);
                    return false;
                }
                Loger.Log($"Client ExchengeEdit MoveSelectThings: {fromWorldObject.LabelShortCap} -> {toWorldObject?.LabelShortCap} ", Loger.LogLevel.EXCHANGE);

                List<Thing> toTargetThing;
                List<ThingTrade> toTargetEntry = null;

                if (fromWorldObject is TradeThingsOnline)
                {
                    toTargetThing = new List<Thing>(select.Count);
                    toTargetEntry = new List<ThingTrade>(select.Count);

                    foreach (var pair in select)
                    {
                        var thing = pair.Key;
                        var count = pair.Value;
                        if (thing.stackCount != count) thing.stackCount = count;
                        toTargetThing.Add(thing);
                        toTargetEntry.Add(ThingTrade.CreateTrade(thing, count));
                    }
                }
                else
                {
                    toTargetThing = fromWorldObject is Caravan caravan
                        ? DeSpawnCaravan(select, caravan)
                        : DeSpawnMap(select);

                    if (toWorldObject == null || toWorldObject is TradeThingsOnline || toWorldObject is CaravanOnline)
                    {
                        toTargetEntry = CreateTradeAndDestroy(toTargetThing);
                    }
                }

                if (toWorldObject is Caravan targetCaravan)
                {
                    Loger.Log("Client ExchengeEdit MoveSelectThings SpawnThings Caravan", Loger.LogLevel.EXCHANGE);
                    SpawnThings(toTargetThing, targetCaravan);
                }
                else if (toWorldObject is Settlement settlement && settlement.Map != null)
                {
                    Loger.Log("Client ExchengeEdit MoveSelectThings SpawnThings Map", Loger.LogLevel.EXCHANGE);
                    SpawnThings(toTargetThing, settlement.Map);
                }

                if (toWorldObject is TradeThingsOnline)
                {
                    Loger.Log("Client ExchengeEdit MoveSelectThings SaveGame and ExchengeStorage", Loger.LogLevel.EXCHANGE);
                    SessionClientController.SaveGameNowSingleAndCommandSafely(
                        (connect) => connect.ExchengeStorage(toTargetEntry, null, fromWorldObject.Tile),
                        () => finish?.Invoke(),
                        null,
                        false);
                }
                else if (fromWorldObject is TradeThingsOnline)
                {
                    Loger.Log("Client ExchengeEdit MoveSelectThings ExchengeStorage", Loger.LogLevel.EXCHANGE);
                    var errorMessage = SessionClientController.CommandSafely((connect) =>
                        connect.ExchengeStorage(null, toTargetEntry, fromWorldObject.Tile));

                    if (toWorldObject is CaravanOnline destOnline)
                    {
                        if (errorMessage == null)
                        {
                            var entries = new List<ThingEntry>(toTargetEntry.Count);
                            for (int i = 0; i < toTargetEntry.Count; i++) entries.Add(toTargetEntry[i]);

                            SendThingsAndSave(entries,
                                destOnline,
                                () => finish?.Invoke(),
                                () => SessionClientController.Disconnected("OCity_SessionCC_Disconnected".TranslateCache())
                            );
                            return true;
                        }
                    }
                    else if (toWorldObject == null)
                    {
                        finish?.Invoke();
                        return true;
                    }

                    if (errorMessage != null)
                    {
                        SessionClientController.Disconnected("OCity_SessionCC_Disconnected".TranslateCache() + " " + errorMessage);
                    }
                    else
                    {
                        SessionClientController.SaveGameNow(false, () => finish?.Invoke());
                    }
                }
                else if (toWorldObject is CaravanOnline targetOnlineCaravan)
                {
                    var entries = new List<ThingEntry>(toTargetEntry.Count);
                    for (int i = 0; i < toTargetEntry.Count; i++) entries.Add(toTargetEntry[i]);

                    SendThingsAndSave(entries,
                        targetOnlineCaravan,
                        () => finish?.Invoke(),
                        () => SessionClientController.Disconnected("OCity_SessionCC_Disconnected".TranslateCache())
                    );
                    return true;
                }
                else
                {
                    Loger.Log("Client ExchengeEdit MoveSelectThings complete local", Loger.LogLevel.EXCHANGE);
                    finish?.Invoke();
                }

                Loger.Log("Client ExchengeEdit MoveSelectThings end", Loger.LogLevel.EXCHANGE);
            }
            catch (Exception exp)
            {
                ExceptionUtil.ExceptionLog(exp, "MoveSelectThings");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Відокремлює та відв'язує вибрані речі від домашньої карти гравця.
        /// </summary>
        public static List<Thing> DeSpawnMap(Dictionary<Thing, int> select)
        {
            var freeThings = new List<Thing>(select.Count);
            foreach (var pair in select)
            {
                var thing = pair.Key;
                var numToTake = pair.Value;

                if (thing is Pawn pawn)
                {
                    pawn.DeSpawn();
                    freeThings.Add(pawn);
                }
                else
                {
                    Thing freeThing = thing.SplitOff(numToTake);
                    freeThings.Add(freeThing);
                }
            }
            return freeThings;
        }

        /// <summary>
        /// Відокремлює та відв'язує вибрані речі від каравану.
        /// ОПТИМІЗАЦІЯ: повне усунення LINQ OrderBy та зайвих словників інвентарю.
        /// </summary>
        public static List<Thing> DeSpawnCaravan(Dictionary<Thing, int> select, Caravan caravan)
        {
            var freeThings = new List<Thing>(select.Count);

            int pawnSelectCount = 0;
            foreach (var k in select.Keys)
            {
                if (k is Pawn) pawnSelectCount++;
            }

            bool selectAllCaravan = caravan.PawnsListForReading.Count == pawnSelectCount;
            if (selectAllCaravan)
            {
                Loger.Log("DeSpawnCaravan. Select all Caravan");
                select = new Dictionary<Thing, int>();
                var pawnsList = caravan.PawnsListForReading;
                for (int i = 0; i < pawnsList.Count; i++)
                {
                    var p = pawnsList[i];
                    var inner = p.inventory.innerContainer;
                    for (int j = 0; j < inner.Count; j++)
                    {
                        var item = inner[j];
                        select[item] = item.stackCount;
                    }
                    select[p] = 1;
                    inner.Clear();
                }
            }

            // 1-й прохід: обробка пішаків без алокації OrderBy
            foreach (var pair in select)
            {
                if (!(pair.Key is Pawn pawn)) continue;

                var things = new List<Thing>(pawn.inventory.innerContainer);
                pawn.inventory.innerContainer.Clear();

                GameUtils.DeSpawnSetupOnCaravan(caravan, pawn);
                for (int j = 0; j < things.Count; j++)
                {
                    var thin = things[j];
                    var recipient = CaravanInventoryUtility.FindPawnToMoveInventoryTo(thin, caravan.PawnsListForReading, null);
                    recipient?.inventory.innerContainer.TryAdd(thin, true);
                }
                freeThings.Add(pawn);
            }

            // 2-й прохід: обробка звичайних предметів
            foreach (var pair in select)
            {
                if (pair.Key is Pawn) continue;
                var thing = pair.Key;
                var numToTake = pair.Value;

                Thing freeThing = thing.SplitOff(numToTake);
                freeThings.Add(freeThing);
            }

            if (selectAllCaravan)
            {
                Find.WorldObjects.Remove(caravan);
            }
            return freeThings;
        }

        /// <summary>
        /// Повне знищення речей (пішаків та предметів) без LINQ-сортування.
        /// </summary>
        public static void DestroyThings(List<Thing> things)
        {
            if (things == null) return;

            // 1-й прохід: спочатку звичайні речі
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null || thing is Pawn) continue;
                thing.Destroy();
            }

            // 2-й прохід: знищення пішаків
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing is Pawn pawn)
                {
                    GameUtils.PawnDestroy(pawn);
                }
            }
        }

        public static List<ThingEntry> CreateEntryAndDestroy(List<Thing> things)
        {
            var sendThings = new List<ThingEntry>(things.Count);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                sendThings.Add(ThingEntry.CreateEntry(t, t.stackCount));
            }
            DestroyThings(things);
            return sendThings;
        }

        public static List<ThingTrade> CreateTradeAndDestroy(List<Thing> things)
        {
            var sendThings = new List<ThingTrade>(things.Count);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                sendThings.Add(ThingTrade.CreateTrade(t, t.stackCount));
            }
            DestroyThings(things);
            return sendThings;
        }

        /// <summary>
        /// Пошук цільового об'єкта світу за даними моделі без LINQ OrderByDescending.
        /// </summary>
        public static WorldObject GetPlace(IModelPlace modelPlace, bool softSettlement = true, bool softNewCaravan = false)
        {
            if (modelPlace.PlaceServerId <= 0)
            {
                Loger.Log("GetPlace fail: no data", Loger.LogLevel.ERROR);
                return null;
            }

            var placeId = UpdateWorldController.GetLocalIdByServerId(modelPlace.PlaceServerId);
            WorldObject place = null;

            if (placeId == 0)
            {
                if (softNewCaravan)
                {
                    Loger.Log($"GetPlace softNewCaravan Tile={modelPlace.Tile}");
                    var all = Find.WorldObjects.AllWorldObjects;
                    int maxId = -1;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var o = all[i];
                        if (o is Caravan && o.Tile == modelPlace.Tile && o.Faction == Faction.OfPlayer)
                        {
                            if (o.ID > maxId)
                            {
                                maxId = o.ID;
                                place = o;
                            }
                        }
                    }
                }
                else if (softSettlement)
                {
                    Loger.Log("GetPlace softSettlement");
                    var settlements = Find.WorldObjects.Settlements;
                    for (int i = 0; i < settlements.Count; i++)
                    {
                        var s = settlements[i];
                        if (s.Faction == Faction.OfPlayer && s is MapParent mp && mp.Map != null && mp.Map.IsPlayerHome)
                        {
                            place = s;
                            break;
                        }
                    }
                }
            }
            else
            {
                var all = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < all.Count; i++)
                {
                    var o = all[i];
                    if (o.ID == placeId && o.Faction == Faction.OfPlayer)
                    {
                        place = o;
                        break;
                    }
                }
            }

            if (place == null)
            {
                Loger.Log("GetPlace fail: place is null", Loger.LogLevel.ERROR);
                return null;
            }

            return place;
        }

        public static void SpawnThings(List<Thing> things, Map map, IntVec3 cell = default)
        {
            if (cell == default) cell = GameUtils.GetTradeCell(map);
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing is Pawn pawn)
                {
                    GenSpawn.Spawn(pawn, cell, map);
                    if (pawn.Dead && !Find.WorldPawns.AllPawnsDead.Contains(pawn))
                    {
                        Find.WorldPawns.AllPawnsDead.Add(pawn);
                    }
                }
                else
                {
                    GenDrop.TryDropSpawn(thing, cell, map, ThingPlaceMode.Near, out _, null);
                }
            }
        }

        public static void SpawnThings(List<Thing> things, Caravan caravan)
        {
            var pawns = caravan.PawnsListForReading;
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing is Pawn pawn)
                {
                    caravan.AddPawn(pawn, true);
                    GameUtils.SpawnSetupOnCaravan(pawn);
                    if (pawn.Dead && !Find.WorldPawns.AllPawnsDead.Contains(pawn))
                    {
                        Find.WorldPawns.AllPawnsDead.Add(pawn);
                    }
                }
                else
                {
                    var recipient = CaravanInventoryUtility.FindPawnToMoveInventoryTo(thing, pawns, null);
                    recipient?.inventory.innerContainer.TryAdd(thing, true);
                }
            }
        }

        public static void SpawnToWorldObject(WorldObject place, List<ThingEntry> things, string text = null)
        {
            GlobalTargetInfo targetInfo = new GlobalTargetInfo(place);

            using (GameUtils.NormalGameError())
            {
                if (place is Settlement settlement && settlement.Map != null)
                {
                    var map = settlement.Map;
                    var cell = GameUtils.GetTradeCell(map);
                    targetInfo = new GlobalTargetInfo(cell, map);

                    var toSpawn = new List<Thing>(things.Count);
                    for (int i = 0; i < things.Count; i++)
                    {
                        toSpawn.Add(things[i].CreateThing());
                    }
                    SpawnThings(toSpawn, map, cell);
                }
                else if (place is Caravan caravan)
                {
                    var toSpawn = new List<Thing>(things.Count);
                    for (int i = 0; i < things.Count; i++)
                    {
                        toSpawn.Add(things[i].CreateThing());
                    }
                    SpawnThings(toSpawn, caravan);
                }
            }

            if (text != null)
            {
                Find.LetterStack.ReceiveLetter("OCity_UpdateWorld_Trade".Translate(),
                    text,
                    LetterDefOf.PositiveEvent,
                    targetInfo,
                    null);
            }
        }

        public static void SendThingsAndSave(List<ThingEntry> sendThings,
            CaravanOnline destination,
            Action finishGood = null,
            Action finishBad = null)
        {
            if ((sendThings?.Count ?? 0) == 0)
            {
                Loger.Log("Client SendThings Not SendThings");
                return;
            }

            SessionClientController.SaveGameNowSingleAndCommandSafely(
                (connect) => connect.SendThings(sendThings,
                    SessionClientController.My.Login,
                    destination.OnlinePlayerLogin,
                    destination.OnlineWObject.PlaceServerId,
                    destination.Tile),
                finishGood,
                finishBad,
                false);
        }

        public static void SendThingsWithDestroy(Dictionary<Thing, int> select,
            Caravan caravan,
            CaravanOnline destination)
        {
            if (!SessionClientController.Data.BackgroundSaveGameOff)
            {
                List<ThingEntry> sendThings;
                using (var gameError = new CatchGameError())
                {
                    var freeThing = caravan == null ? DeSpawnMap(select) : DeSpawnCaravan(select, caravan);
                    if (gameError.GameError != null) Loger.Log("Client SendThingsWithDestroy GameError DeSpawn");
                    gameError.GameError = null;

                    sendThings = CreateEntryAndDestroy(freeThing);
                    if (gameError.GameError != null) Loger.Log("Client SendThingsWithDestroy GameError CreateEntryAndDestroy");
                }

                SendThingsAndSave(sendThings, destination);
            }
        }

        public static FloatMenuOption ExchangeOfGoods_GetFloatMenu(CaravanOnline that, Action actionFloatMenu)
        {
            bool disTrade = GameUtils.IsProtectingNovice();
            var fmoTrade = new FloatMenuOption("OCity_Caravan_Trade".Translate(that.OnlinePlayerLogin + " " + that.OnlineName)
                + (disTrade ? "OCity_Caravan_Abort".Translate().ToString() + " " + MainHelper.MinCostForTrade.ToString() : ""),
                actionFloatMenu, MenuOptionPriority.Default, null, null, 0f, null, that);

            if (disTrade)
            {
                fmoTrade.Disabled = true;
            }
            return fmoTrade;
        }

        public static void ExchangeOfGoods_DoAction(CaravanOnline destination, Caravan source)
        {
            if (destination.OnlineWObject == null)
            {
                Log.Error("OCity_Caravan_LOGNoData".Translate());
                return;
            }

            var goods = GameUtils.GetAllThings(source);
            Dialog_TradeOnline form = null;
            form = new Dialog_TradeOnline(goods,
                destination.OnlinePlayerLogin,
                destination.OnlineWObject.FreeWeight,
                () => ExchangeOfGoods_DoAction(destination, source, form.GetSelect()));

            Find.WindowStack.Add(form);
        }

        public static void ExchangeOfGoods_DoAction(CaravanOnline destination, Caravan source, List<TransferableOneWay> goods)
            => ExchangeOfGoods_DoAction(destination, source, goods.TransferableOneWaysToDictionary());

        public static void ExchangeOfGoods_DoAction(CaravanOnline destination, Caravan source, Dictionary<Thing, int> goods)
        {
            if (destination.OnlineWObject == null)
            {
                Log.Error("OCity_Caravan_LOGNoData".Translate());
                return;
            }
            SendThingsWithDestroy(goods, source, destination);
        }
    }
}