using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class ExchengeBuy : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request23;

        public int ResponseTypePackage => (int)PackageType.Response24;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = exchengeBuy((ModelOrderBuy)request.Packet, context);
            return result;
        }

        private ModelStatus exchengeBuy(ModelOrderBuy buy, ServiceContext context)
        {
            if (buy == null || buy.Count <= 0 || buy.OrderId == 0)
            {
                return new ModelStatus
                {
                    Status = 1,
                    Message = "Invalid buy request"
                };
            }

            lock (context.Player)
            {
                var data = Repository.GetData;
                if (data?.OrderOperator == null || context.Player.Public == null)
                {
                    return new ModelStatus
                    {
                        Status = 2,
                        Message = "Service unavailable"
                    };
                }

                lock (data)
                {
                    if (!data.OrderOperator.OrdersById.TryGetValue(buy.OrderId, out var order) || order == null)
                    {
                        return new ModelStatus()
                        {
                            Status = 1,
                            Message = "Order not found"
                        };
                    }

                    if (!data.OrderOperator.ImplementTradeByStorage(order, context.Player, buy.Count))
                    {
                        var storage = data.OrderOperator.GetStorage(order.Tile, context.Player.Public, false);
                        var storageStr = storage?.Things?.ToStringLabel() ?? "null";
                        Loger.Log($"Server exchengeBuy Operation not possible! order={order}\n\n storage={storageStr}", Loger.LogLevel.EXCHANGE);

                        return new ModelStatus()
                        {
                            Status = 2,
                            Message = "Operation not possible"
                        };
                    }

                    Repository.Get.ChangeData = true;
                }

                return new ModelStatus()
                {
                    Status = 0,
                    Message = null
                };
            }
        }
    }
}