using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class Registration : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request1Register;

        public int ResponseTypePackage => (int)PackageType.Response2Register;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = null };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player != null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = registration((ModelLogin)request.Packet, context);
            return result;
        }

        private ModelStatus registration(ModelLogin packet, ServiceContext context)
        {
            if (packet == null || string.IsNullOrWhiteSpace(packet.Login) || string.IsNullOrEmpty(packet.Pass))
            {
                return new ModelStatus
                {
                    Status = 1,
                    Message = "Invalid registration data"
                };
            }

            packet.Login = packet.Login.Trim();
            packet.Email = Repository.CheckIsIntruder(context, packet.Email, packet.Login);
            Loger.Log($"Player {packet.Login} attempt to register on the server.", Loger.LogLevel.REGISTER);

            var errorValid = Repository.GetData.NameValidator.TextValidator(packet.Login);
            if (errorValid != null)
            {
                return new ModelStatus()
                {
                    Status = 1,
                    Message = "Login " + errorValid,
                };
            }

            if (packet.Pass.Length <= 2)
            {
                return new ModelStatus()
                {
                    Status = 1,
                    Message = "Password must be longer than 3 characters"
                };
            }

            if (ServerManager.ServerSettings.PlayerNeedApproveInDiscord && (packet.DiscordUserName ?? "").Length <= 2)
            {
                return new ModelStatus()
                {
                    Status = 1,
                    Message = "OC_LoginForm_NeedApproveText1",
                };
            }

            var data = Repository.GetData;
            if (!data.NameValidator.CheckFree(packet.Login))
            {
                return new ModelStatus()
                {
                    Status = 1,
                    Message = "This login already exists"
                };
            }

            bool isAdmin;
            var newPlayer = new PlayerServer(packet.Login)
            {
                Pass = packet.Pass,
            };
            newPlayer.Public.EMail = packet.Email;
            newPlayer.Public.Version = packet.Version;
            newPlayer.Public.DiscordUserName = packet.DiscordUserName;

            var allPlayers = data.PlayersAll;
            lock (allPlayers)
            {
                isAdmin = allPlayers.Count == 2; // 1 : system, 2 : discord
                newPlayer.Public.Grants = Grants.UsualUser;
                if (isAdmin)
                {
                    newPlayer.Public.Grants |= Grants.Moderator | Grants.SuperAdmin;
                }

                // Визначаємо схвалення до індексації словників
                bool needApprove = !isAdmin && ServerManager.ServerSettings.PlayerNeedApprove;
                newPlayer.Approve = !needApprove;

                allPlayers.Add(newPlayer);
                data.UpdatePlayersAllDic();
            }

            context.Player = newPlayer;
            context.Logined();

            ChatManager.Instance.PublicChat.LastChanged = DateTime.UtcNow;
            Repository.Get.ChangeData = true;

            Loger.Log($"Player {packet.Login} version: {packet.Version}");
            Loger.Log($"Player {packet.Login} successfully register on this server.", Loger.LogLevel.REGISTER);

            if (!newPlayer.Approve)
            {
                Loger.Log($"Player {packet.Login} need approve.", Loger.LogLevel.REGISTER);
                context.Player = null;
                return new ModelStatus()
                {
                    Status = 2,
                    Message = "User not approve"
                };
            }

            Loger.Log("Server Auto Approve player " + newPlayer.Public.Login);
            return StatusOk;
        }
    }
}