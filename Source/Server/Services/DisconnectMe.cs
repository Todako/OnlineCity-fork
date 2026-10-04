using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using Transfer;

namespace ServerOnlineCity.Services
{
    /// <summary>
    /// Коректна ініціація розриву з'єднання (дисконекту) з повідомленням причини іншій стороні.
    /// </summary>
    internal sealed class DisconnectMe : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request39Disconnect;

        public int ResponseTypePackage => (int)PackageType.Response40Disconnect;

        // ОПТИМІЗАЦІЯ: статичний кешований результат (нуль алокацій у GC)
        private static readonly ModelInt ResponseCloseConnection = new ModelInt { Value = (int)DisconnectReason.CloseConnection };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = GetInfo((ModelInt)request.Packet, context);
            return result;
        }

        public ModelInt GetInfo(ModelInt packet, ServiceContext context)
        {
            if (packet == null || context?.Player == null) return ResponseCloseConnection;

            var reason = (DisconnectReason)packet.Value;

            lock (context.Player)
            {
                context.Player.ExitReason = reason;
                return ResponseCloseConnection;
            }
        }
    }
}