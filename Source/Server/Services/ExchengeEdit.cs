using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Common;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using System.Text;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Services
{
    internal sealed class ExchengeEdit : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request21;

        public int ResponseTypePackage => (int)PackageType.Response22;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = exchengeEdit((TradeOrder)request.Packet, context);
            return result;
        }

        private ModelStatus exchengeEdit(TradeOrder order, ServiceContext context)
        {
            try
            {
                if (context.Player == null) return null;

                lock (context.Player)
                {
                    if (order?.Owner == null || context.Player.Public == null || context.Player.Public.Login != order.Owner.Login)
                    {
                        return new ModelStatus()
                        {
                            Status = 1,
                            Message = "OC_ExchangeEdit_err1"
                        };
                    }

                    var timeNow = DateTime.UtcNow;
                    var data = Repository.GetData;

                    if (order.Id == 0)
                    {
                        // Створення нового ордера
                        order.Created = timeNow;
                        order.UpdateTime = timeNow;
                        order.Owner = context.Player.Public;

                        if (!ResolvePrivatPlayers(order))
                        {
                            return new ModelStatus()
                            {
                                Status = 2,
                                Message = "OC_ExchangeEdit_err2"
                            };
                        }

                        lock (data)
                        {
                            if (!data.OrderOperator.OrderAdd(order))
                            {
                                var storage = data.OrderOperator.GetStorage(order.Tile, context.Player.Public, false);
                                var storageStr = storage?.Things?.ToStringLabel() ?? "null";
                                Loger.Log($"Server exchengeEdit Cancel OrderAdd! order={order}\n\n storage={storageStr}", Loger.LogLevel.EXCHANGE);

                                return new ModelStatus()
                                {
                                    Status = 4,
                                    Message = "OC_ExchangeEdit_err4"
                                };
                            }
                            else
                            {
                                HelperMailMessadge.Send(
                                    Repository.GetData.PlayerSystem,
                                    context.Player,
                                    "OC_ExchangeEdit_OrderPlaced",
                                    BuildMailBody("OC_ExchangeEdit_OrderPlaced", order),
                                    ModelMailMessadge.MessadgeTypes.GreyGoldenLetter,
                                    order.Tile
                                );
                            }
                        }
                        Loger.Log("Server ExchengeEdit " + context.Player.Public.Login + " Add Id = " + order.Id.ToString(), Loger.LogLevel.EXCHANGE);
                    }
                    else
                    {
                        // Перевірка існування ордера
                        lock (data)
                        {
                            long id = order.Id > 0 ? order.Id : -order.Id;
                            if (!data.OrderOperator.OrdersById.TryGetValue(id, out var dataOrder)
                                || dataOrder == null
                                || context.Player.Public.Login != dataOrder.Owner?.Login)
                            {
                                return new ModelStatus()
                                {
                                    Status = 3,
                                    Message = "OC_ExchangeEdit_err3"
                                };
                            }

                            if (order.Id > 0)
                            {
                                // Редагування ордера
                                order.Created = timeNow;
                                order.Owner = context.Player.Public;

                                if (!ResolvePrivatPlayers(order))
                                {
                                    return new ModelStatus()
                                    {
                                        Status = 4,
                                        Message = "OC_ExchangeEdit_err2"
                                    };
                                }

                                Loger.Log("Server ExchengeEdit " + context.Player.Public.Login + " Edit Id = " + order.Id.ToString(), Loger.LogLevel.EXCHANGE);
                                if (!data.OrderOperator.OrderUpdate(order, dataOrder))
                                {
                                    var storage = data.OrderOperator.GetStorage(order.Tile, context.Player.Public, false);
                                    var storageStr = storage?.Things?.ToStringLabel() ?? "null";
                                    Loger.Log($"Server exchengeEdit Cancel OrderUpdate! order={order}\n\n storage={storageStr}", Loger.LogLevel.EXCHANGE);

                                    return new ModelStatus()
                                    {
                                        Status = 4,
                                        Message = "OC_ExchangeEdit_err4"
                                    };
                                }
                                else
                                {
                                    HelperMailMessadge.Send(
                                        Repository.GetData.PlayerSystem,
                                        context.Player,
                                        "OC_ExchangeEdit_OrderRedacted",
                                        BuildMailBody("OC_ExchangeEdit_OrderRedacted", order),
                                        ModelMailMessadge.MessadgeTypes.GreyGoldenLetter,
                                        order.Tile
                                    );
                                }
                            }
                            else
                            {
                                // Видалення ордера
                                Loger.Log("Server ExchengeEdit " + context.Player.Public.Login + " Delete Id = " + order.Id.ToString(), Loger.LogLevel.EXCHANGE);

                                data.OrderOperator.OrderRemove(dataOrder);

                                HelperMailMessadge.Send(
                                    Repository.GetData.PlayerSystem,
                                    context.Player,
                                    "OC_ExchangeEdit_OrderDeleted",
                                    BuildMailBody("OC_ExchangeEdit_OrderDeleted", order),
                                    ModelMailMessadge.MessadgeTypes.GreyGoldenLetter,
                                    order.Tile
                                );
                            }
                        }
                    }

                    Repository.Get.ChangeData = true;

                    return new ModelStatus()
                    {
                        Status = 0,
                        Message = null
                    };
                }
            }
            catch (Exception exp)
            {
                ExceptionUtil.ExceptionLog(exp, "Server ExchengeEdit login=" + context?.Player?.Public?.Login);
                throw;
            }
        }

        /// <summary>
        /// Валідація та актуалізація списку приватних гравців ордера без LINQ.
        /// </summary>
        private static bool ResolvePrivatPlayers(TradeOrder order)
        {
            if (order.PrivatPlayers == null)
            {
                order.PrivatPlayers = new List<Player>(0);
                return true;
            }

            var resolved = new List<Player>(order.PrivatPlayers.Count);
            for (int i = 0; i < order.PrivatPlayers.Count; i++)
            {
                var pp = order.PrivatPlayers[i];
                if (pp == null || string.IsNullOrEmpty(pp.Login)) return false;

                var serverPlayer = Repository.GetPlayerByLogin(pp.Login);
                if (serverPlayer?.Public == null) return false;

                resolved.Add(serverPlayer.Public);
            }

            order.PrivatPlayers = resolved;
            return true;
        }

        /// <summary>
        /// Формування тексту системного листа без створення квадратних алокацій рядків.
        /// </summary>
        private static string BuildMailBody(string actionKey, TradeOrder order)
        {
            var sb = new StringBuilder(256);
            sb.Append(actionKey).Append(".").AppendLine();
            sb.Append("OC_ExchangeEdit_Laps: ").Append(order.CountReady).Append(".\n").AppendLine();
            sb.Append("OC_ExchangeEdit_YouSell").Append(order.SellThings?.ToStringLabel()).AppendLine();
            sb.Append("OC_ExchangeEdit_YouBuy").Append(order.BuyThings?.ToStringLabel()).AppendLine();

            if (order.PrivatPlayers == null || order.PrivatPlayers.Count == 0)
            {
                sb.Append("OC_ExchangeEdit_ForAll");
            }
            else
            {
                sb.Append("OC_ExchangeEdit_PrivateOrder");
                for (int i = 0; i < order.PrivatPlayers.Count; i++)
                {
                    sb.Append(", ").Append(order.PrivatPlayers[i]?.Login);
                }
            }

            return sb.ToString();
        }
    }
}