using System;
using System.Collections.Generic;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class AnyUpload : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request45AnyLoad;

        public int ResponseTypePackage => (int)PackageType.Response46AnyLoad;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null) return null;

            var packet = request?.Packet as ModelAnyLoad;
            var hashs = packet?.Hashs;
            if (hashs == null) return null;

            var dataContainer = Repository.GetData;
            var uploadService = dataContainer?.UploadService;

            var datas = new List<string>(hashs.Count);

            if (uploadService != null && hashs.Count > 0)
            {
                lock (uploadService)
                {
                    for (int i = 0; i < hashs.Count; i++)
                    {
                        uploadService.TryGetValue(hashs[i], out string data);
                        datas.Add(data);
                    }
                }
            }

            var result = new ModelContainer
            {
                TypePacket = ResponseTypePackage,
                Packet = new ModelAnyLoad
                {
                    Hashs = hashs,
                    Datas = datas
                }
            };

            return result;
        }
    }
}