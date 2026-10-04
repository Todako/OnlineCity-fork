using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class GetPlayerByToken : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.RequestPlayerByToken;

        public int ResponseTypePackage => (int)PackageType.ResponsePlayerByToken;

        public ModelContainer GenerateModelContainer(ModelContainer modelContainer, ServiceContext context)
        {
            if (context?.Player == null || !(modelContainer?.Packet is Guid packet)) return null;

            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            var data = Repository.GetData;
            var players = data?.PlayersAll;

            PlayerServer targetPlayer = null;

            if (players != null)
            {
                // ОПТИМІЗАЦІЯ І ЗАХИСТ: потокобезпечний обхід без LINQ і алокацій лямбд
                lock (players)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        var p = players[i];
                        if (p != null && p.DiscordToken == packet)
                        {
                            targetPlayer = p;
                            break;
                        }
                    }
                }
            }

            result.Packet = targetPlayer?.Public;
            return result;
        }
    }
}