using Model;
using OCUnion;
using OCUnion.Transfer.Types;
using ServerOnlineCity.Model;
using ServerOnlineCity.Services;
using System.Collections.Generic;
using Transfer;

namespace ServerOnlineCity.ChatService
{
    internal sealed class AbandonHimSettlementCmd : IChatCmd
    {
        public string CmdID => "killhimplease";

        public Grants GrantsForRun => Grants.SuperAdmin | Grants.Moderator | Grants.DiscordBot;

        public string Help => ChatManager.prefix + "killhimplease {UserLogin}: Drop user Settlement and delete him from a server";

        private readonly ChatManager _chatManager;

        public AbandonHimSettlementCmd(ChatManager chatManager)
        {
            _chatManager = chatManager;
        }

        public ModelStatus Execute(ref PlayerServer player, Chat chat, List<string> argsM, ServiceContext context)
        {
            var ownLogin = player.Public.Login;

            if (argsM.Count < 1)
            {
                return _chatManager.PostCommandPrivatPostActivChat(ChatCmdResult.PlayerNameEmpty, ownLogin, chat, "Player name is empty");
            }

            var killPlayer = Repository.GetPlayerByLogin(argsM[0]);
            if (killPlayer == null)
            {
                return _chatManager.PostCommandPrivatPostActivChat(ChatCmdResult.UserNotFound, ownLogin, chat, "User " + argsM[0] + " not found");
            }

            //var msg = "User " + killPlayer.Public.Login + " deleted settlements.";
            //_chatManager.AddSystemPostToPublicChat(msg); // Розкоментувати, щоб публікувати повідомлення в загальному чаті для всіх.

            killPlayer.AbandonSettlement();
            Loger.Log("Server killhimplease " + killPlayer.Public.Login);

            return new ModelStatus() { Status = 0 };
        }
    }
}