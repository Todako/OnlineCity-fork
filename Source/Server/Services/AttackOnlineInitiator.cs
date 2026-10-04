using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class AttackOnlineInitiator : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request27;

        public int ResponseTypePackage => (int)PackageType.Response28;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = attackOnlineInitiator((AttackInitiatorToSrv)request.Packet, context);
            return result;
        }

        public AttackInitiatorFromSrv attackOnlineInitiator(AttackInitiatorToSrv fromClient, ServiceContext context)
        {
            if (fromClient == null)
            {
                return new AttackInitiatorFromSrv { ErrorText = "No request data" };
            }

            lock (context.Player)
            {
                // Ініціалізація спільного об'єкта битви
                if (!string.IsNullOrEmpty(fromClient.StartHostPlayer))
                {
                    if (context.Player.AttackData != null)
                    {
                        return new AttackInitiatorFromSrv { ErrorText = "There is an active attack" };
                    }

                    // ОПТИМІЗАЦІЯ: швидкий O(1) пошук гравця замість LINQ FirstOrDefault
                    var hostPlayer = Repository.GetPlayerByLogin(fromClient.StartHostPlayer);
                    if (hostPlayer == null)
                    {
                        return new AttackInitiatorFromSrv { ErrorText = "Player not found" };
                    }

                    // Потокобезпечна перевірка участі захисника в іншому бою
                    lock (hostPlayer)
                    {
                        if (hostPlayer.AttackData != null)
                        {
                            return new AttackInitiatorFromSrv { ErrorText = "The player participates in the attack" };
                        }

                        var att = new AttackServer();
                        var err = att.New(context.Player, hostPlayer, fromClient, fromClient.TestMode);
                        if (err != null)
                        {
                            return new AttackInitiatorFromSrv { ErrorText = err };
                        }
                    }
                }

                if (context.Player.AttackData == null)
                {
                    Loger.Log("Server AttackOnlineInitiator Unexpected error, no data", Loger.LogLevel.ERROR);
                    return new AttackInitiatorFromSrv { ErrorText = "Unexpected error, no data" };
                }

                // ОПТИМІЗАЦІЯ: пряме передавання керування спільному об'єкту без попереднього new AttackInitiatorFromSrv()
                return context.Player.AttackData.RequestInitiator(fromClient);
            }
        }
    }
}