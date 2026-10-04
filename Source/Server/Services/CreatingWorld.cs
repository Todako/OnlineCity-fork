using Model;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System.Collections.Generic;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class CreatingWorld : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request7CreateWorld;

        public int ResponseTypePackage => (int)PackageType.Response8WorldCreated;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = null };
        private static readonly ModelStatus StatusAccessDenied = new ModelStatus { Status = 1, Message = "Access is denied" };
        private static readonly ModelStatus StatusAlreadyCreated = new ModelStatus { Status = 2, Message = "The world is already created" };
        private static readonly ModelStatus StatusInvalidRequest = new ModelStatus { Status = 3, Message = "Invalid request data" };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = creatingWorld(request.Packet as ModelCreateWorld, context);
            return result;
        }

        public ModelStatus creatingWorld(ModelCreateWorld packet, ServiceContext context)
        {
            if (packet == null) return StatusInvalidRequest;

            lock (context.Player)
            {
                if (!context.Player.IsAdmin)
                {
                    return StatusAccessDenied;
                }

                var data = Repository.GetData;
                if (data == null) return StatusInvalidRequest;

                // ОПТИМІЗАЦІЯ І ЗАХИСТ: блокування сховища даних на час повної ініціалізації світу
                lock (data)
                {
                    if (!string.IsNullOrEmpty(data.WorldSeed))
                    {
                        return StatusAlreadyCreated;
                    }

                    data.WorldSeed = packet.Seed;
                    data.WorldScenarioName = packet.ScenarioName;
                    data.WorldDifficulty = packet.Difficulty;
                    data.WorldStoryteller = packet.Storyteller;
                    data.WorldMapSize = packet.MapSize;
                    data.WorldPlanetCoverage = packet.PlanetCoverage;
                    data.WorldObjects = packet.WObjects != null ? new List<WorldObjectEntry>(packet.WObjects) : new List<WorldObjectEntry>(0);
                    data.WorldObjectsDeleted = new List<WorldObjectEntry>(0);
                    data.Orders = new List<TradeOrder>(0);
                    data.OrdersPlaceServerIdByTile = new Dictionary<int, long>(0);
                    Repository.Get.ChangeData = true;

                    ServerManager.SaveSettings(data.WorldStoryteller, data.WorldDifficulty);
                }
            }

            return StatusOk;
        }
    }
}