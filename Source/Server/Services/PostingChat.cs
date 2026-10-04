using Model;
using OCUnion;
using OCUnion.Common;
using OCUnion.Transfer.Model;
using OCUnion.Transfer.Types;
using ServerOnlineCity.ChatService;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using Transfer;

namespace ServerOnlineCity.Services
{
    public sealed class PostingChat : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request19PostingChat;

        public int ResponseTypePackage => (int)PackageType.Response20PostingChat;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = null };
        private static readonly ModelStatus ChatNotAvailable = new ModelStatus { Status = 1, Message = "Chat not available" };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null)
                return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = GetModelStatus((ModelPostingChat)request.Packet, context);
            return result;
        }

        public ModelStatus GetModelStatus(ModelPostingChat pc, ServiceContext context)
        {
            if (context.PossiblyIntruder)
            {
                context.Disconnect("Possibly intruder");
                return null;
            }

            if (pc == null || string.IsNullOrEmpty(pc.Message))
            {
                return StatusOk;
            }

            var timeNow = DateTime.UtcNow;

            Chat chat = null;
            var playerChats = context.Player.Chats;
            if (playerChats != null)
            {
                foreach (var ct in playerChats.Keys)
                {
                    if (ct.Id == pc.IdChat)
                    {
                        chat = ct;
                        break;
                    }
                }
            }

            if (chat == null)
            {
                return ChatNotAvailable;
            }

            Grants acceptedGrants;
            PlayerServer player;
            // Обробка команд чату та інтеграції з Discord
            if (string.Equals(context.Player.Public?.Login, "discord", StringComparison.OrdinalIgnoreCase))
            {
                player = Repository.GetPlayerByLogin(pc.Owner);
                if (player?.Public == null)
                {
                    return new ModelStatus
                    {
                        Status = (int)ChatCmdResult.UserNotFound,
                        Message = $"user {pc.Owner} not found",
                    };
                }

                acceptedGrants = context.Player.Public.Grants & player.Public.Grants;
            }
            else
            {
                player = context.Player;
                acceptedGrants = player.Public?.Grants ?? Grants.UsualUser;
            }

            if (pc.Message[0] == '/')
            {
                // Розбираємо аргументи в лапках
                ChatUtils.ParceCommand(pc.Message, out string command, out List<string> argsM);

                var result = ChatManager.TryGetCmdForUser(player.Public?.Login, acceptedGrants, command, out IChatCmd cmd);
                if (result.Status > 0)
                    return result;

                return cmd.Execute(ref player, chat, argsM, context);
            }
            else
            {
                if (Loger.Enable)
                {
                    Loger.Log("Server post " + context.Player.Public?.Login + ":" + pc.Message);
                }

                var mmsg = pc.Message;
                if (mmsg.Length > 2048) mmsg = mmsg.Substring(0, 2048);

                var post = new ChatPost
                {
                    Time = timeNow,
                    Message = mmsg,
                    OwnerLogin = player.Public?.Login,
                    DiscordIdMessage = pc.IdDiscordMsg,
                };

                // Потокобезпечне додавання повідомлення
                lock (chat.Posts)
                {
                    chat.Posts.Add(post);
                }
            }

            return StatusOk;
        }
    }
}