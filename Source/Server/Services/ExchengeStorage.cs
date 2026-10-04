using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class ExchengeStorage : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request47Storage;

        public int ResponseTypePackage => (int)PackageType.Response48Storage;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = null };
        private static readonly ModelStatus StatusInvalidRequest = new ModelStatus { Status = 1, Message = "Invalid request data" };
        private static readonly ModelStatus StatusTooMuch = new ModelStatus { Status = 1, Message = "Operation not possible (too much)" };
        private static readonly ModelStatus StatusNotPossible = new ModelStatus { Status = 1, Message = "Operation not possible" };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = exchengeStorage(request.Packet as ModelExchengeStorage, context);
            return result;
        }

        private ModelStatus exchengeStorage(ModelExchengeStorage diff, ServiceContext context)
        {
            if (diff == null)
            {
                return StatusInvalidRequest;
            }

            lock (context.Player)
            {
                var data = Repository.GetData;
                if (data?.OrderOperator == null)
                {
                    return StatusNotPossible;
                }

                lock (data)
                {
                    var addThings = diff.AddThings;
                    if (addThings != null && addThings.Count > 0)
                    {
                        data.OrderOperator.SendToStorage(diff.Tile, context.Player, addThings);
                        Repository.Get.ChangeData = true;
                    }

                    var deleteThings = diff.DeleteThings;
                    if (deleteThings != null && deleteThings.Count > 0)
                    {
                        if (diff.TileTo != 0 && diff.Cost > 0 && diff.Dist > 0
                            && diff.Cost > context.Player.CashlessBalance)
                        {
                            if (Loger.Enable)
                            {
                                long costThings = 0;
                                for (int i = 0; i < deleteThings.Count; i++)
                                {
                                    var t = deleteThings[i];
                                    if (t != null)
                                    {
                                        costThings += (long)t.GameCost * t.Count;
                                    }
                                }

                                Loger.Log($"Server exchengeStorage CargoDelivery tile {diff.Tile} to {diff.TileTo} cost={diff.Cost} dist={diff.Dist} costThings={costThings}", Loger.LogLevel.EXCHANGE);
                            }
                            return StatusTooMuch;
                        }

                        if (data.OrderOperator.GetFromStorage(diff.Tile, context.Player, deleteThings) == null)
                        {
                            if (Loger.Enable)
                            {
                                var storage = data.OrderOperator.GetStorage(diff.Tile, context.Player.Public, false);
                                var storageStr = storage?.Things?.ToStringThing() ?? "null";
                                Loger.Log($"Server exchengeStorage Operation not possible! storage={storageStr}", Loger.LogLevel.EXCHANGE);
                            }

                            return StatusNotPossible;
                        }
                        else
                        {
                            if (diff.TileTo != 0 && diff.Cost > 0 && diff.Dist > 0)
                            {
                                data.OrderOperator.SendToStorage(diff.TileTo, context.Player, deleteThings);
                                context.Player.CashlessBalance -= diff.Cost;
                            }
                        }
                        Repository.Get.ChangeData = true;
                    }
                }

                return StatusOk;
            }
        }
    }
}