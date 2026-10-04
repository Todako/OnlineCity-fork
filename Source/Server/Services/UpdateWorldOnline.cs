using Model;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System.Collections.Generic;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class UpdateWorldOnline : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request43WObjectUpdate;

        public int ResponseTypePackage => (int)PackageType.Response44WObjectUpdate;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = GetWorldObjectUpdate((ModelInt)request.Packet, context);
            return result;
        }

        private ModelGameServerInfo GetWorldObjectUpdate(ModelInt packet, ServiceContext context)
        {
            var data = Repository.GetData;
            var toClientGServerInfo = new ModelGameServerInfo();

            if (packet != null && data != null)
            {
                // ОПТИМІЗАЦІЯ І ЗАХИСТ: потокобезпечний знімок списків для запобігання збоїв серіалізації "Collection was modified"
                lock (data)
                {
                    toClientGServerInfo.WObjectOnlineList = data.WorldObjectOnlineList != null
                        ? new List<WorldObjectOnline>(data.WorldObjectOnlineList)
                        : new List<WorldObjectOnline>(0);

                    toClientGServerInfo.FactionOnlineList = data.FactionOnlineList != null
                        ? new List<FactionOnline>(data.FactionOnlineList)
                        : new List<FactionOnline>(0);
                }
            }
            else
            {
                toClientGServerInfo.WObjectOnlineList = new List<WorldObjectOnline>(0);
                toClientGServerInfo.FactionOnlineList = new List<FactionOnline>(0);
            }

            return toClientGServerInfo;
        }
    }
}