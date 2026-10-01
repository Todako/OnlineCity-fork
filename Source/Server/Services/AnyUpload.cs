using System;
using System.Collections.Generic;
using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Services
{
    internal sealed class AnyUpload : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request45AnyLoad;

        public int ResponseTypePackage => (int)PackageType.Response46AnyLoad;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;

            var hashs = ((ModelAnyLoad)request.Packet).Hashs;
            if (hashs == null) return null;

            var uploadService = Repository.GetData.UploadService;
            var datas = new List<string>(hashs.Count);

            // Швидке наповнення списку без LINQ .Select().ToList()
            for (int i = 0; i < hashs.Count; i++)
            {
                uploadService.TryGetValue(hashs[i], out string data);
                datas.Add(data);
            }

            var result = new ModelContainer()
            {
                TypePacket = ResponseTypePackage,
                Packet = new ModelAnyLoad() { Hashs = hashs, Datas = datas }
            };

            return result;
        }
    }
}