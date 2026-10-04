using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class AttackOnlineHost : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request29;

        public int ResponseTypePackage => (int)PackageType.Response30;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = attackOnlineHost((AttackHostToSrv)request.Packet, context);
            return result;
        }

        private AttackHostFromSrv attackOnlineHost(AttackHostToSrv fromClient, ServiceContext context)
        {
            if (fromClient == null)
            {
                return new AttackHostFromSrv { ErrorText = "No request data" };
            }

            lock (context.Player)
            {
                if (context.Player.AttackData == null)
                {
                    Loger.Log("Server AttackOnlineHost Unexpected error, no data", Loger.LogLevel.ERROR);
                    return new AttackHostFromSrv { ErrorText = "Unexpected error, no data" };
                }

                // ОПТИМІЗАЦІЯ: пряме передавання керування без виділення тимчасового об'єкта кожні 50 мс
                return context.Player.AttackData.RequestHost(fromClient);
            }
        }
    }
}