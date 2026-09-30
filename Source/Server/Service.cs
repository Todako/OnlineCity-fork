using Newtonsoft.Json;
using OCUnion;
using ServerOnlineCity.Model;
using ServerOnlineCity.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Transfer;

namespace ServerOnlineCity
{
    public class Service
    {
        public static IReadOnlyDictionary<int, IGenerateResponseContainer> ServiceDictionary { get; private set; }

        public ServiceContext Context { get; private set; }
        private static ServiceAPI API { get; set; }

        static Service()
        {
            DependencyInjection();
            API = new ServiceAPI();
        }

        public Service(ServiceContext context)
        {
            Context = context;
        }

        private static void DependencyInjection()
        {
            var d = new Dictionary<int, IGenerateResponseContainer>();
            foreach (var type in Assembly.GetAssembly(typeof(Service)).GetTypes())
            {
                if (!type.IsClass)
                {
                    continue;
                }

                if (type.GetInterfaces().Any(x => x == typeof(IGenerateResponseContainer)))
                {
                    var t = (IGenerateResponseContainer)Activator.CreateInstance(type);
                    d[t.RequestTypePackage] = t;
                }
            }

            ServiceDictionary = d;
        }

        /// <summary>
        /// Перевіряє наявність нових повідомлень у чаті для користувача.
        /// </summary>
        public bool CheckChat(DateTime time)
        {
            var player = Context?.Player;
            if (player?.Chats == null)
            {
                return false;
            }

            foreach (var ct in player.Chats)
            {
                var chat = ct.Key;
                var pos = ct.Value;
                if (chat == null || pos == null) continue;

                if (pos.Value + 1 < chat.Posts.Count || chat.LastChanged > pos.Time)
                {
                    return true;
                }
            }

            return false;
        }

        internal ModelContainer GetPackage(ModelContainer inputPackage)
        {
            if (ServiceDictionary.TryGetValue(inputPackage.TypePacket, out IGenerateResponseContainer generateResponseContainer))
            {
                if (Loger.Enable)
                {
                    var login = Context.Player?.Public?.Login;
                    var formattedLogin = string.IsNullOrEmpty(login) ? "     " : (login.Length >= 5 ? login : login.PadRight(5));
                    Loger.Log("Server " + formattedLogin + " " + generateResponseContainer.GetType().Name);
                }
                return generateResponseContainer.GenerateModelContainer(inputPackage, Context);
            }

            if (Loger.Enable)
            {
                var login = Context.Player?.Public?.Login;
                var formattedLogin = string.IsNullOrEmpty(login) ? "     " : (login.Length >= 5 ? login : login.PadRight(5));
                Loger.Log("Server " + formattedLogin + $" Response for type {inputPackage.TypePacket} not found");
            }

            return new ModelContainer { TypePacket = 0 };
        }

        public static object GetPackageJson(string inputPackage, Dictionary<string, byte[]> data)
        {
            var package = JsonConvert.DeserializeObject<APIRequest>(inputPackage);
            if (data != null && data.Count > 0)
            {
                foreach (var val in data.Values)
                {
                    package.Data = val;
                    break;
                }
            }

            var ret = API.GetPackage(package);
            if (ret is APIResponseRawData rawData)
            {
                return rawData.Data;
            }
            return JsonConvert.SerializeObject(ret);
        }
    }
}