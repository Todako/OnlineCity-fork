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

            // Зчитуємо посилання атомарно без утримання lock (context.Player) під час 50-мс Lockstep-циклу
            var attackData = context.Player.AttackData;
            if (attackData == null)
            {
                lock (context.Player)
                {
                    attackData = context.Player.AttackData;
                }
            }

            if (attackData == null)
            {
                Loger.Log("Server AttackOnlineHost Unexpected error, no data", Loger.LogLevel.ERROR);
                return new AttackHostFromSrv { ErrorText = "Unexpected error, no data" };
            }

            return attackData.RequestHost(fromClient);
        }
    }
}