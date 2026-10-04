using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Services
{
    internal sealed class SendThings : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request15;

        public int ResponseTypePackage => (int)PackageType.Response16;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = "Load shipped" };
        private static readonly ModelStatus StatusDestNotFound = new ModelStatus { Status = 1, Message = "Destination not found" };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = sendThings((ModelMailTrade)request.Packet, context);
            return result;
        }

        public ModelStatus sendThings(ModelMailTrade packet, ServiceContext context)
        {
            if (packet?.To == null || string.IsNullOrEmpty(packet.To.Login))
            {
                return StatusDestNotFound;
            }

            var toPlayer = Repository.GetPlayerByLogin(packet.To.Login);
            if (toPlayer?.Public == null)
            {
                return StatusDestNotFound;
            }

            // Гарантуємо, що відправником є автентифікований гравець сесії
            packet.From = context.Player.Public;
            packet.To = toPlayer.Public;
            packet.Created = DateTime.UtcNow;
            packet.NeedSaveGame = true;

            lock (toPlayer)
            {
                if (toPlayer.Mails == null)
                {
                    toPlayer.Mails = new List<ModelMail>();
                }
                toPlayer.Mails.Add(packet);
            }

            // Позначаємо необхідність збереження стану бази даних на диск
            Repository.Get.ChangeData = true;

            if (Loger.Enable)
            {
                Loger.Log($"Mail SendThings {packet.From?.Login ?? "-"}->{packet.To.Login} {packet.ContentString()}");
            }

            return StatusOk;
        }
    }
}