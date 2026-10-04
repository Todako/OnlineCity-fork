using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class SetPlayerInfo : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request41SetPlayerInfo;

        public int ResponseTypePackage => (int)PackageType.Response42SetPlayerInfo;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = null };
        private static readonly ModelStatus StatusPvpCooldown = new ModelStatus { Status = 1, Message = "You can’t change the status of PVP yet" };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = setPlayerInfo((ModelPlayerInfo)request.Packet, context);
            return result;
        }

        private ModelStatus setPlayerInfo(ModelPlayerInfo packet, ServiceContext context)
        {
            if (packet == null) return StatusOk;

            lock (context.Player)
            {
                if (context.Player.Public == null) return StatusOk;

                var timeNow = DateTime.UtcNow;

                if (context.Player.Public.EnablePVP != packet.EnablePVP)
                {
                    // Якщо на сервері глобально вимкнено PVP, увімкнення блокується
                    if (packet.EnablePVP && !ServerManager.ServerSettings.GeneralSettings.EnablePVP)
                    {
                        return new ModelStatus
                        {
                            Status = 1,
                            Message = "PVP is disabled on this server"
                        };
                    }

                    if (context.Player.TimeChangeEnablePVP >= timeNow)
                    {
                        return StatusPvpCooldown;
                    }

                    context.Player.TimeChangeEnablePVP = timeNow.AddHours(24);
                    context.Player.Public.EnablePVP = packet.EnablePVP;
                }

                // Обмеження розміру тексту для запобігання переповненню пам'яті
                var aboutText = packet.AboutMyTextBox;
                if (aboutText != null && aboutText.Length > 2048) aboutText = aboutText.Substring(0, 2048);

                var email = packet.EMail;
                if (email != null && email.Length > 256) email = email.Substring(0, 256);

                var discord = packet.DiscordUserName;
                if (discord != null && discord.Length > 256) discord = discord.Substring(0, 256);

                context.Player.Public.AboutMyText = aboutText;
                context.Player.Public.EMail = email;
                context.Player.Public.DiscordUserName = discord;
                context.Player.SettingDelaySaveGame = Math.Max(0, packet.DelaySaveGame);

                // Обов'язкова позначка для збереження зміненого стану гравця на диск
                Repository.Get.ChangeData = true;
            }

            return StatusOk;
        }
    }
}