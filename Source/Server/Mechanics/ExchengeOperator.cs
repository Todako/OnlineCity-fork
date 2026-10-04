using Model;
using OCUnion;
using ServerOnlineCity.Common;
using ServerOnlineCity.Model;
using ServerOnlineCity.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Mechanics
{
    public class ExchengeOperator
    {
        public BaseContainer Data;

        public List<TradeWorldObjectEntry> TradeWorldObjects; // calc from Data.Orders

        public List<TradeWorldObjectEntry> TradeWorldObjectsDeleted;

        public ConcurrentDictionary<long, TradeOrder> OrdersById; // calc from Data.Orders

        public ConcurrentDictionary<int, HashSet<TradeOrder>> OrdersByTile; // calc from Data.Orders

        private readonly object _tradeSync = new object();
        private bool ImplementTradeCalcing = false;

        private ConcurrentDictionary<string, ConcurrentDictionary<int, TradeThingStorage>> CacheStorage =
            new ConcurrentDictionary<string, ConcurrentDictionary<int, TradeThingStorage>>();

        public ExchengeOperator(BaseContainer data)
        {
            Data = data;
            int ordersCount = Data.Orders?.Count ?? 0;

            TradeWorldObjects = new List<TradeWorldObjectEntry>(ordersCount);
            TradeWorldObjectsDeleted = new List<TradeWorldObjectEntry>();
            OrdersById = new ConcurrentDictionary<long, TradeOrder>(Environment.ProcessorCount, Math.Max(32, ordersCount));
            OrdersByTile = new ConcurrentDictionary<int, HashSet<TradeOrder>>(Environment.ProcessorCount, Math.Max(16, ordersCount / 2));

            if (Data.Orders != null)
            {
                for (int i = 0; i < Data.Orders.Count; i++)
                {
                    var order = Data.Orders[i];
                    if (order == null) continue;

                    TradeWorldObjects.Add(new TradeOrderShort(order));
                    OrdersById.TryAdd(order.Id, order);

                    var set = OrdersByTile.GetOrAdd(order.Tile, _ => new HashSet<TradeOrder>());
                    lock (set)
                    {
                        set.Add(order);
                    }

                    if (order.SellThings != null)
                    {
                        for (int j = 0; j < order.SellThings.Count; j++)
                        {
                            var st = order.SellThings[j];
                            if (st != null && !string.IsNullOrEmpty(st.Data))
                            {
                                st.DataHash = Data.SetInUploadService(st.Data);
                            }
                        }
                    }
                }
            }
        }

        public bool OrderAdd(TradeOrder order)
        {
            if (order == null || order.Owner == null) return false;

            if (GetFromStorage(order.Tile, order.Owner.GetPlayerServer(), order.SellThings, order.CountReady) == null) return false;

            order.Id = ChatManager.Instance.GetChatId();
            order.UpdateTime = DateTime.UtcNow;
            Data.Orders.Add(order);
            OrdersById.TryAdd(order.Id, order);

            var set = OrdersByTile.GetOrAdd(order.Tile, _ => new HashSet<TradeOrder>());
            lock (set)
            {
                set.Add(order);
            }

            if (order.SellThings != null)
            {
                for (int i = 0; i < order.SellThings.Count; i++)
                {
                    var st = order.SellThings[i];
                    if (st != null && !string.IsNullOrEmpty(st.Data))
                    {
                        st.DataHash = Data.SetInUploadService(st.Data);
                    }
                }
            }

            if (Data.OrdersPlaceServerIdByTile.TryGetValue(order.Tile, out long placeServerId))
                order.PlaceServerId = placeServerId;
            else
                Data.OrdersPlaceServerIdByTile.Add(order.Tile, order.PlaceServerId = Data.GetWorldObjectEntryId());

            var trade = new TradeOrderShort(order);
            TradeWorldObjects.Add(trade);

            ImplementTrade(order);

            return true;
        }

        public void OrderRemove(TradeOrder order)
        {
            if (order == null) return;

            order.UpdateTime = DateTime.UtcNow;
            Data.Orders.Remove(order);
            OrdersById.TryRemove(order.Id, out _);

            if (OrdersByTile.TryGetValue(order.Tile, out var obt))
            {
                lock (obt)
                {
                    obt.Remove(order);
                    if (obt.Count == 0) OrdersByTile.TryRemove(order.Tile, out _);
                }
            }

            for (int i = 0; i < TradeWorldObjects.Count; i++)
            {
                if (TradeWorldObjects[i].Id == order.Id)
                {
                    var trade = TradeWorldObjects[i];
                    TradeWorldObjects.RemoveAt(i);
                    TradeWorldObjectsDeleted.Add(trade);
                    break;
                }
            }

            // Повертаємо речі до торгового сховища лише якщо залишився невикуплений залишок
            if (order.SellThings != null && order.CountReady > 0)
            {
                for (int i = 0; i < order.SellThings.Count; i++)
                {
                    if (order.SellThings[i] != null)
                    {
                        order.SellThings[i].Count *= order.CountReady;
                    }
                }
                SendToStorage(order.Tile, order.Owner?.GetPlayerServer(), order.SellThings);
            }
        }

        public bool OrderUpdate(TradeOrder newOrder, TradeOrder oldOrder)
        {
            if (newOrder == null || oldOrder == null) return false;

            var newThings = newOrder.SellThings.OrderByDescendingCost();
            var oldThings = oldOrder.SellThings.OrderByCost();
            var newThingsOrig = new List<ThingTrade>(newThings.Count);

            for (int si = 0; si < oldThings.Count; si++)
            {
                oldThings[si] = (ThingTrade)oldThings[si].Clone();
                oldThings[si].Count *= oldOrder.CountReady;
            }

            for (int bi = 0; bi < newThings.Count; bi++)
            {
                var thing = newThings[bi];
                int newThingsCount = thing.Count * newOrder.CountReady;
                for (int si = 0; si < oldThings.Count; si++)
                {
                    var item = oldThings[si];
                    if (!thing.MatchesThingTrade(item)) continue;

                    if (item.Count > newThingsCount)
                    {
                        item.Count -= newThingsCount;
                        newThingsCount = 0;
                        break;
                    }
                    else
                    {
                        newThingsCount -= item.Count;
                        oldThings.RemoveAt(si--);
                    }
                }
                if (newThingsCount > 0)
                {
                    var ot = thing.Clone() as ThingTrade;
                    ot.Count = newThingsCount;
                    newThingsOrig.Add(ot);
                }
            }

            var playerServer = oldOrder.Owner?.GetPlayerServer();
            if (newThingsOrig.Count > 0 && GetFromStorage(oldOrder.Tile, playerServer, newThingsOrig) == null) return false;
            if (oldThings.Count > 0) SendToStorage(oldOrder.Tile, playerServer, oldThings);

            newOrder.UpdateTime = DateTime.UtcNow;
            int oldIdx = Data.Orders.IndexOf(oldOrder);
            if (oldIdx >= 0) Data.Orders[oldIdx] = newOrder;

            OrdersById[newOrder.Id] = newOrder;
            if (OrdersByTile.TryGetValue(newOrder.Tile, out var obt))
            {
                lock (obt)
                {
                    obt.Remove(oldOrder);
                    obt.Add(newOrder);
                }
            }

            if (newOrder.SellThings != null)
            {
                for (int i = 0; i < newOrder.SellThings.Count; i++)
                {
                    var st = newOrder.SellThings[i];
                    if (st != null && !string.IsNullOrEmpty(st.Data))
                    {
                        st.DataHash = Data.SetInUploadService(st.Data);
                    }
                }
            }

            for (int i = 0; i < TradeWorldObjects.Count; i++)
            {
                if (TradeWorldObjects[i].Id == newOrder.Id)
                {
                    TradeWorldObjects[i] = new TradeOrderShort(newOrder);
                    break;
                }
            }

            ImplementTrade(newOrder);
            return true;
        }

        public Dictionary<ThingTrade, TradeThingStorage> FindThingDef(PlayerServer player, ThingTrade thingDef)
        {
            var result = new Dictionary<ThingTrade, TradeThingStorage>();
            if (player?.TradeThingStorages == null || thingDef == null) return result;

            var storages = player.TradeThingStorages;
            for (int i = 0; i < storages.Count; i++)
            {
                var tts = storages[i];
                if (tts?.Things == null) continue;

                for (int j = 0; j < tts.Things.Count; j++)
                {
                    var t = tts.Things[j];
                    if (t != null && t.DefName == thingDef.DefName)
                    {
                        result[t] = tts;
                    }
                }
            }
            return result;
        }

        public int CountThingDef(PlayerServer player, ThingTrade thingDef)
        {
            if (player?.TradeThingStorages == null || thingDef == null) return 0;

            int sum = 0;
            var storages = player.TradeThingStorages;
            for (int i = 0; i < storages.Count; i++)
            {
                var tts = storages[i];
                if (tts?.Things == null) continue;

                for (int j = 0; j < tts.Things.Count; j++)
                {
                    var t = tts.Things[j];
                    if (t != null && t.DefName == thingDef.DefName)
                    {
                        sum += t.Count;
                    }
                }
            }
            return sum;
        }

        public TradeThingStorage GetStorage(int tile, Player player, bool needAdd)
        {
            if (player?.Login == null) return null;

            var dicPl = CacheStorage.GetOrAdd(player.Login, login =>
            {
                var pl = Repository.GetPlayerByLogin(login);
                int count = pl?.TradeThingStorages?.Count ?? 0;
                var dict = new ConcurrentDictionary<int, TradeThingStorage>(Environment.ProcessorCount, Math.Max(8, count));
                if (pl?.TradeThingStorages != null)
                {
                    lock (pl.TradeThingStorages)
                    {
                        for (int i = 0; i < pl.TradeThingStorages.Count; i++)
                        {
                            var st = pl.TradeThingStorages[i];
                            if (st != null) dict[st.Tile] = st;
                        }
                    }
                }
                return dict;
            });

            if (needAdd)
            {
                return dicPl.GetOrAdd(tile, t =>
                {
                    var ns = new TradeThingStorage
                    {
                        Id = 0,
                        PlaceServerId = Data.GetWorldObjectEntryId(),
                        Tile = t,
                        LoginOwner = player.Login,
                        Things = new List<ThingTrade>(),
                        Type = TradeWorldObjectEntryType.ThingsPlayer,
                        UpdateTime = DateTime.UtcNow
                    };
                    var pl = Repository.GetPlayerByLogin(player.Login);
                    if (pl != null)
                    {
                        if (pl.TradeThingStorages == null) pl.TradeThingStorages = new List<TradeThingStorage>();
                        lock (pl.TradeThingStorages)
                        {
                            pl.TradeThingStorages.Add(ns);
                        }
                    }
                    return ns;
                });
            }

            dicPl.TryGetValue(tile, out var storage);
            return storage;
        }

        public void SendToStorage(int tile, PlayerServer player, List<ThingTrade> things)
        {
            if (player?.Public == null || things == null || things.Count == 0) return;

            things = things.OrderByDescendingCost();
            if (things.Count == 0) return;

            if (Loger.Enable)
            {
                Loger.Log($"Server SendToStorage tile={tile} pl={player.Public.Login} things=" + things.ToStringLabel());
            }

            var storage = GetStorage(tile, player.Public, true);
            if (storage == null) return;

            var storageRead = storage.Things.OrderByCost();

            for (int bi = 0; bi < things.Count; bi++)
            {
                var thing = things[bi];
                if (thing == null) continue;

                if (thing.DefName == MainHelper.CashlessThingDefName)
                {
                    player.CashlessBalance += thing.Count;
                    continue;
                }

                ThingTrade found = null;
                for (int si = 0; si < storageRead.Count; si++)
                {
                    var item = storageRead[si];
                    if (!thing.MatchesThingTrade(item, true)) continue;
                    found = item;
                    break;
                }
                if (found != null)
                {
                    found.Count += thing.Count;
                }
                else
                {
                    storage.Things.Add(thing);
                    storageRead.Add(thing);
                }
            }
            storage.UpdateTime = DateTime.UtcNow;
        }

        public List<ThingTrade> GetFromStorage(int tile, PlayerServer player, List<ThingTrade> filter, int filterRate = 1)
        {
            if (player?.Public == null || filter == null || filter.Count == 0) return null;
            if (filterRate <= 0) filterRate = 1;

            if (Loger.Enable)
            {
                Loger.Log($"Server GetFromStorage tile={tile} pl={player.Public.Login} rate={filterRate} filter=" + filter.ToStringThing());
            }

            filter = filter.OrderByDescendingCost();
            var storage = GetStorage(tile, player.Public, false);
            var storageThings = storage?.Things ?? new List<ThingTrade>();
            var storageRead = storageThings.OrderByCost();

            var select = new List<ThingTrade>();
            var selectCashless = 0;
            var newCountStorage = new Dictionary<ThingTrade, int>();

            for (int bi = 0; bi < filter.Count; bi++)
            {
                var need = filter[bi];
                if (need == null) continue;
                var countSelected = 0;

                if (need.DefName == MainHelper.CashlessThingDefName)
                {
                    if (need.Count * filterRate >= player.CashlessBalance)
                    {
                        selectCashless = (int)player.CashlessBalance;
                        countSelected = selectCashless;
                    }
                    else
                    {
                        selectCashless = need.Count * filterRate;
                        countSelected = selectCashless;
                    }
                }
                else
                {
                    for (int si = 0; si < storageRead.Count; si++)
                    {
                        var item = storageRead[si];
                        if (item == null) continue;

                        if (!newCountStorage.TryGetValue(item, out var itemCount)) itemCount = item.Count;
                        if (itemCount == 0) continue;

                        if (!need.MatchesThingTrade(item)) continue;

                        if (need.Count * filterRate - countSelected >= itemCount)
                        {
                            select.Add(item);
                            newCountStorage[item] = 0;
                            countSelected += itemCount;
                        }
                        else
                        {
                            var itemOut = (ThingTrade)item.Clone();
                            itemOut.Count = need.Count * filterRate - countSelected;
                            newCountStorage[item] = itemCount - itemOut.Count;
                            select.Add(itemOut);
                            countSelected += itemOut.Count;
                        }
                    }
                }
                if (countSelected < need.Count * filterRate) return null;
            }

            if (selectCashless > 0)
            {
                player.CashlessBalance -= selectCashless;
                select.Add(ThingTrade.CreateTradeServer(MainHelper.CashlessThingDefName, selectCashless));
            }

            foreach (var nc in newCountStorage)
            {
                if (nc.Value > 0) nc.Key.Count = nc.Value;
                else
                {
                    storageRead.Remove(nc.Key);
                    storageThings.Remove(nc.Key);
                }
            }

            if (newCountStorage.Count > 0 && storage != null)
            {
                storage.UpdateTime = DateTime.UtcNow;
            }
            return select;
        }

        private List<ThingTrade> CompareListTrade(List<ThingTrade> sellThings, List<ThingTrade> buyThings
            , ref int sellRepeat, ref int buyRepeat
            , int sellAllCount, int buyAllCount
            , out List<ThingTrade> sellByBuyThings)
        {
            buyThings = buyThings.OrderByDescendingCost();
            var sts = sellThings.OrderByCost();
            var excess = new List<ThingTrade>();
            sellByBuyThings = new List<ThingTrade>();

            for (int bi = 0; bi < buyThings.Count; bi++)
            {
                bool isOk = false;
                for (int si = 0; si < sts.Count; si++)
                {
                    var buy = buyThings[bi];
                    var sell = sts[si];
                    if (!buy.MatchesThingTrade(sell)) continue;

                    if (sell.Count * sellRepeat < buy.Count * buyRepeat)
                    {
                        if (sellRepeat != 1 || buyRepeat != 1) continue;

                        var cnt = NOK(sell.Count, buy.Count);
                        if ((long)sellAllCount * sell.Count < cnt || (long)buyAllCount * buy.Count < cnt) continue;

                        sellRepeat = cnt / sell.Count;
                        buyRepeat = cnt / buy.Count;

                        if (sellRepeat > 1)
                        {
                            for (int e = 0; e < excess.Count; e++)
                            {
                                excess[e].Count *= sellRepeat;
                            }
                        }
                    }
                    if (sell.Count * sellRepeat > buy.Count * buyRepeat)
                    {
                        var sellClone = (ThingTrade)sell.Clone();
                        sellClone.Count = sell.Count * sellRepeat - buy.Count * buyRepeat;
                        excess.Add(sellClone);
                    }

                    var sellByBuyThing = (ThingTrade)sell.Clone();
                    sellByBuyThing.Count = buy.Count * buyRepeat;
                    sellByBuyThings.Add(sellByBuyThing);
                    isOk = true;
                    sts.RemoveAt(si--);
                    break;
                }
                if (!isOk) return null;
            }
            return excess;
        }

        public static int NOD(int a, int b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a == 0 ? 1 : a;
        }

        public static int NOK(int a, int b)
        {
            if (a == 0 || b == 0) return 0;
            int gcd = NOD(a, b);
            return (int)Math.Abs((long)a * b / gcd);
        }

        private class ImplementSelectTrade
        {
            public float Cost { get; set; }
            public TradeOrder Order1 { get; set; }
            public TradeOrder Order2 { get; set; }
            public int Count1 { get; set; }
            public int Count2 { get; set; }
            public List<ThingTrade> SellEnd1 { get; set; }
            public List<ThingTrade> SellEnd2 { get; set; }
            public List<ThingTrade> ThingsFor1 { get; set; }
            public List<ThingTrade> ThingsFor2 { get; set; }
        }

        private void ImplementTrade(TradeOrder o1)
        {
            if (o1 == null) return;

            lock (_tradeSync)
            {
                if (ImplementTradeCalcing) return;
                ImplementTradeCalcing = true;

                try
                {
                    if (!OrdersByTile.TryGetValue(o1.Tile, out var to) || to == null) return;

                    List<TradeOrder> tradeOrders;
                    lock (to)
                    {
                        tradeOrders = new List<TradeOrder>(to);
                    }
                    tradeOrders.Remove(o1);

                    ImplementSelectTrade best;
                    do
                    {
                        best = null;
                        for (int io = 0; io < tradeOrders.Count; io++)
                        {
                            var o2 = tradeOrders[io];
                            if (o2 == null) continue;
                            if (o1.SellThingsHash != o2.BuyThingsHash
                                || o2.SellThingsHash != o1.BuyThingsHash) continue;

                            int repeat1 = 1;
                            int repeat2 = 1;
                            var sellEnd1 = CompareListTrade(o1.SellThings, o2.BuyThings, ref repeat1, ref repeat2, o1.CountReady, o2.CountReady, out var thingsFor2);
                            if (sellEnd1 == null) continue;
                            var sellEnd2 = CompareListTrade(o2.SellThings, o1.BuyThings, ref repeat2, ref repeat1, o2.CountReady, o1.CountReady, out var thingsFor1);
                            if (sellEnd2 == null) continue;

                            var repeatRepeat = o1.CountReady / repeat1 < o2.CountReady / repeat2 ? o1.CountReady / repeat1 : o2.CountReady / repeat2;

                            if (repeatRepeat > 1)
                            {
                                for (int k = 0; k < sellEnd1.Count; k++) sellEnd1[k].Count *= repeatRepeat;
                                for (int k = 0; k < sellEnd2.Count; k++) sellEnd2[k].Count *= repeatRepeat;
                                for (int k = 0; k < thingsFor1.Count; k++) thingsFor1[k].Count *= repeatRepeat;
                                for (int k = 0; k < thingsFor2.Count; k++) thingsFor2[k].Count *= repeatRepeat;
                            }

                            float cost = 0f;
                            for (int k = 0; k < sellEnd1.Count; k++)
                            {
                                cost += sellEnd1[k].Count * sellEnd1[k].GameCost;
                            }

                            var that = new ImplementSelectTrade
                            {
                                Cost = cost,
                                Count1 = repeat1 * repeatRepeat,
                                Count2 = repeat2 * repeatRepeat,
                                Order1 = o1,
                                Order2 = o2,
                                SellEnd1 = sellEnd1,
                                SellEnd2 = sellEnd2,
                                ThingsFor1 = thingsFor1,
                                ThingsFor2 = thingsFor2
                            };

                            if (best == null || best.Cost < that.Cost) best = that;
                        }

                        if (best != null)
                        {
                            if (Loger.Enable)
                            {
                                Loger.Log("Server Trade! " + Environment.NewLine
                                    + "trade x" + best.Count1 + " " + best.Order1.ToString() + Environment.NewLine
                                    + "trade x" + best.Count2 + " " + best.Order2.ToString());
                            }

                            var msg0 = "OC_ExchengeOperator_tradeSold0 {0} OC_ExchengeOperator_tradeSold1 {1}. OC_ExchengeOperator_tradeSold2 {2}";
                            var msg1 = string.Format(msg0
                                , best.Count1
                                , best.Order1.CountReady == best.Count1 ? "OC_ExchengeOperator_Closed" : "OC_ExchengeOperator_left" + (best.Order1.CountReady - best.Count1).ToString()
                                , best.ThingsFor1.ToStringLabel());
                            var msg2 = string.Format(msg0
                                , best.Count2
                                , best.Order2.CountReady == best.Count2 ? "OC_ExchengeOperator_Closed" : "OC_ExchengeOperator_left" + (best.Order2.CountReady - best.Count2).ToString()
                                , best.ThingsFor2.ToStringLabel());

                            var playerServer1 = best.Order1.Owner?.GetPlayerServer();
                            var playerServer2 = best.Order2.Owner?.GetPlayerServer();

                            SendToStorage(o1.Tile, playerServer1, best.ThingsFor1);
                            SendToStorage(o1.Tile, playerServer2, best.ThingsFor2);

                            msg0 = "OC_ExchengeOperator_OrderClosedBetter";
                            if (best.SellEnd1.Count > 0)
                            {
                                msg1 += Environment.NewLine + msg0 + best.SellEnd1.ToStringLabel();
                                SendToStorage(o1.Tile, playerServer1, best.SellEnd1);
                            }
                            if (best.SellEnd2.Count > 0)
                            {
                                msg1 += Environment.NewLine + msg0 + best.SellEnd2.ToStringLabel();
                                SendToStorage(o1.Tile, playerServer2, best.SellEnd2);
                            }

                            best.Order1.CountReady -= best.Count1;
                            best.Order2.CountReady -= best.Count2;

                            if (best.Order2.CountReady <= 0)
                            {
                                OrderRemove(best.Order2);
                                tradeOrders.Remove(best.Order2);
                            }
                            if (best.Order1.CountReady <= 0)
                            {
                                OrderRemove(best.Order1);
                                break;
                            }

                            var pl1 = best.Order1.Owner?.GetPlayerServer();
                            var pl2 = best.Order2.Owner?.GetPlayerServer();

                            HelperMailMessadge.Send(
                                Repository.GetData.PlayerSystem,
                                pl1,
                                "OC_ExchengeOperator_OrderDone",
                                msg1,
                                ModelMailMessadge.MessadgeTypes.GoldenLetter,
                                best.Order1.Tile);

                            HelperMailMessadge.Send(
                                Repository.GetData.PlayerSystem,
                                pl2,
                                "OC_ExchengeOperator_OrderDone",
                                msg2,
                                ModelMailMessadge.MessadgeTypes.GoldenLetter,
                                best.Order1.Tile);
                        }
                    } while (best != null);
                }
                finally
                {
                    ImplementTradeCalcing = false;
                }
            }
        }

        public bool ImplementTradeByStorage(TradeOrder order, PlayerServer player, int needRepeat)
        {
            if (order == null || player == null || order.CountReady < needRepeat) return false;
            var thingsForOrder = GetFromStorage(order.Tile, player, order.BuyThings, needRepeat);
            if (thingsForOrder == null) return false;

            if (Loger.Enable)
            {
                Loger.Log("Server Trade! " + player.Public?.Login + " x" + needRepeat + " buy " + order.ToString());
            }

            var orderPlayer = order.Owner?.GetPlayerServer();
            SendToStorage(order.Tile, orderPlayer, thingsForOrder);

            var msg1 = string.Format("OC_ExchengeOperator_tradeSold0 {0} OC_ExchengeOperator_tradeSold1 {1}. OC_ExchengeOperator_tradeSold2 {2}"
                , needRepeat
                , order.CountReady == needRepeat ? "OC_ExchengeOperator_Closed" : "OC_ExchengeOperator_left" + (order.CountReady - needRepeat).ToString()
                , thingsForOrder.ToStringLabel());

            var thingsFromOrder = new List<ThingTrade>(order.SellThings.Count);
            for (int i = 0; i < order.SellThings.Count; i++)
            {
                var tt = (ThingTrade)order.SellThings[i].Clone();
                tt.Count *= needRepeat;
                thingsFromOrder.Add(tt);
            }

            SendToStorage(order.Tile, player, thingsFromOrder);

            var msg2 = string.Format("OC_ExchengeOperator_tradeBought0 {0} OC_ExchengeOperator_tradeBought1 {1}"
                , needRepeat
                , thingsFromOrder.ToStringLabel());

            order.CountReady -= needRepeat;

            if (order.CountReady <= 0)
            {
                OrderRemove(order);
            }

            HelperMailMessadge.Send(
                Repository.GetData.PlayerSystem,
                orderPlayer,
                "OC_ExchengeOperator_OrderDone",
                msg1,
                ModelMailMessadge.MessadgeTypes.GoldenLetter,
                order.Tile);

            HelperMailMessadge.Send(
                Repository.GetData.PlayerSystem,
                player,
                "OC_ExchengeOperator_YouBought",
                msg2,
                ModelMailMessadge.MessadgeTypes.GoldenLetter,
                order.Tile);

            return true;
        }

        /// <summary>
        /// Швидкий підрахунок балансу товарів у сховищах та активних ордерах без алокацій Tuple та LINQ Sum.
        /// </summary>
        public float CalcStorageBalance(PlayerServer player)
        {
            if (player == null) return 0f;
            float balance = 0f;

            var storages = player.TradeThingStorages;
            if (storages != null)
            {
                for (int i = 0; i < storages.Count; i++)
                {
                    var st = storages[i];
                    var things = st?.Things;
                    if (things == null) continue;

                    for (int j = 0; j < things.Count; j++)
                    {
                        var t = things[j];
                        if (t != null)
                        {
                            balance += t.GameCost * t.Count;
                        }
                    }
                }
            }

            var orders = Data?.Orders;
            string login = player.Public?.Login;
            if (orders != null && login != null)
            {
                for (int i = 0; i < orders.Count; i++)
                {
                    var o = orders[i];
                    if (o != null && o.Owner?.Login == login && o.SellThings != null)
                    {
                        int ready = o.CountReady;
                        for (int j = 0; j < o.SellThings.Count; j++)
                        {
                            var t = o.SellThings[j];
                            if (t != null)
                            {
                                balance += t.GameCost * t.Count * ready;
                            }
                        }
                    }
                }
            }

            return balance;
        }

        internal IEnumerable<Tuple<ThingTrade, int>> GetThingsInServer(PlayerServer player)
        {
            var result = new List<Tuple<ThingTrade, int>>();
            if (player?.TradeThingStorages != null)
            {
                for (int i = 0; i < player.TradeThingStorages.Count; i++)
                {
                    var st = player.TradeThingStorages[i];
                    if (st?.Things == null) continue;
                    for (int j = 0; j < st.Things.Count; j++)
                    {
                        var t = st.Things[j];
                        if (t != null)
                        {
                            result.Add(new Tuple<ThingTrade, int>(t, 1));
                        }
                    }
                }
            }

            if (Data?.Orders != null && player?.Public?.Login != null)
            {
                for (int i = 0; i < Data.Orders.Count; i++)
                {
                    var o = Data.Orders[i];
                    if (o != null && o.Owner?.Login == player.Public.Login && o.SellThings != null)
                    {
                        int ready = o.CountReady;
                        for (int j = 0; j < o.SellThings.Count; j++)
                        {
                            var t = o.SellThings[j];
                            if (t != null)
                            {
                                result.Add(new Tuple<ThingTrade, int>(t, ready));
                            }
                        }
                    }
                }
            }

            return result;
        }

        public void DayPassed(PlayerServer player)
        {
            if (player == null) return;
            double totalCost = 0;

            if (player.TradeThingStorages != null)
            {
                for (int i = 0; i < player.TradeThingStorages.Count; i++)
                {
                    var storage = player.TradeThingStorages[i];
                    if (storage?.Things == null) continue;

                    for (int j = 0; j < storage.Things.Count; j++)
                    {
                        var t = storage.Things[j];
                        if (t != null && (t.IsPawn || t.Rottable))
                        {
                            totalCost += (double)t.GameCost * t.Count;
                        }
                    }
                }
            }

            if (Data?.Orders != null && player.Public?.Login != null)
            {
                for (int i = 0; i < Data.Orders.Count; i++)
                {
                    var o = Data.Orders[i];
                    if (o != null && o.Owner?.Login == player.Public.Login && o.SellThings != null)
                    {
                        int ready = o.CountReady;
                        for (int j = 0; j < o.SellThings.Count; j++)
                        {
                            var t = o.SellThings[j];
                            if (t != null && (t.IsPawn || t.Rottable))
                            {
                                totalCost += (double)t.GameCost * t.Count * ready;
                            }
                        }
                    }
                }
            }

            totalCost *= 25.0 / 5000.0;
            player.CashlessBalance -= (int)totalCost;
        }
    }
}