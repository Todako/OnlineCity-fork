using Model;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Common;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class ExchengeLoad : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request25;

        public int ResponseTypePackage => (int)PackageType.Response26;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = exchengeLoad(context, request.Packet as ModelOrderLoadRequest);
            return result;
        }

        private ModelOrderLoad exchengeLoad(ServiceContext context, ModelOrderLoadRequest filters)
        {
            lock (context.Player)
            {
                var res = new ModelOrderLoad()
                {
                    Status = 0,
                    Message = null
                };

                res.Orders = getOrders(context.Player, filters);

                return res;
            }
        }

        private List<TradeOrder> getOrders(PlayerServer player, ModelOrderLoadRequest filters)
        {
            var myLogin = player?.Public?.Login;
            if (string.IsNullOrEmpty(myLogin)) return new List<TradeOrder>(0);

            // Якщо передано порожній масив тайлів — жодна точка не може підійти
            if (filters?.Tiles != null && filters.Tiles.Count == 0)
            {
                return new List<TradeOrder>(0);
            }

            HashSet<int> tiles = null;
            if (filters?.Tiles != null && filters.Tiles.Count > 0)
            {
                tiles = new HashSet<int>(filters.Tiles);
            }

            string filterBuy = filters?.FilterBuy;
            string filterSell = filters?.FilterSell;
            bool hasFilterBuy = !string.IsNullOrEmpty(filterBuy);
            bool hasFilterSell = !string.IsNullOrEmpty(filterSell);

            // Список гравців, яких бачимо
            var ps = StaticHelper.PartyLoginSee(player);
            var data = Repository.GetData;
            var fillOrders = new List<TradeOrder>();

            lock (data)
            {
                var allOrders = data.Orders;
                if (allOrders == null || allOrders.Count == 0) return fillOrders;

                for (int i = 0; i < allOrders.Count; i++)
                {
                    var o = allOrders[i];
                    if (o == null) continue;

                    // 1. Перевірка видимості ордера
                    if (!CanSeeOrder(o, myLogin, ps)) continue;

                    // 2. Фільтр за точками на карті
                    if (tiles != null && !tiles.Contains(o.Tile)) continue;

                    // 3. Фільтр за назвами товарів (без виділення рядків .ToLower())
                    if (hasFilterBuy && hasFilterSell)
                    {
                        if (!HasMatchingDef(o.BuyThings, filterBuy) && !HasMatchingDef(o.SellThings, filterSell))
                            continue;
                    }
                    else if (hasFilterBuy)
                    {
                        if (!HasMatchingDef(o.BuyThings, filterBuy))
                            continue;
                    }
                    else if (hasFilterSell)
                    {
                        if (!HasMatchingDef(o.SellThings, filterSell))
                            continue;
                    }

                    // 4. Клонуємо та обрізаємо Data для скорочення трафіку мережі
                    var cloned = o.Clone();
                    if (cloned.SellThings != null)
                    {
                        for (int s = 0; s < cloned.SellThings.Count; s++)
                        {
                            if (cloned.SellThings[s] != null)
                            {
                                cloned.SellThings[s].Data = null;
                            }
                        }
                    }
                    fillOrders.Add(cloned);
                }
            }

            return fillOrders;
        }

        private static bool CanSeeOrder(TradeOrder order, string playerLogin, HashSet<string> partySee)
        {
            if (order?.Owner == null) return false;
            string ownerLogin = order.Owner.Login;

            if (ownerLogin == playerLogin) return true;

            if (partySee != null && partySee.Contains(ownerLogin))
            {
                var priv = order.PrivatPlayers;
                if (priv == null || priv.Count == 0) return true;

                for (int i = 0; i < priv.Count; i++)
                {
                    if (priv[i]?.Login == playerLogin) return true;
                }
            }

            return false;
        }

        private static bool HasMatchingDef(List<ThingTrade> things, string filterDef)
        {
            if (things == null || things.Count == 0) return false;

            for (int i = 0; i < things.Count; i++)
            {
                var def = things[i]?.DefName;
                if (def != null && string.Equals(def, filterDef, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}