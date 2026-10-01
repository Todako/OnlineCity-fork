using Model;
using OCUnion;
using OCUnion.Transfer.Types;
using ServerOnlineCity.Model;
using ServerOnlineCity.Services;
using System.Collections.Generic;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.ChatService
{
    class SayCmd : IChatCmd
    {
        public string CmdID => "say";

        // Лише для модераторів і адміністраторів.
        public Grants GrantsForRun => Grants.SuperAdmin | Grants.Moderator | Grants.DiscordBot;

        public string Help => ChatManager.prefix + "say {UserLogin | system} {/color} {Label} {text}";

        // say {'ім'я гравця'} {/колір}* {'заголовок'} {текст} {продовження тексту}
        // ... — надсилає гравцеві повідомлення у вигляді ігрового листа; доступно адміністратору.
        // * Необов'язковий параметр кольору визначається за початковим символом /. Можливі значення:

        // treatbig — червоний лист зі звуком
        // treatsmall — червоний лист
        // death — сірий лист зі звуком
        // negative — жовтий лист
        // positive — синій лист зі звуком
        // visitor — синій лист
        // neutral — сірий лист (за замовчуванням)

        private readonly ChatManager _chatManager;

        public SayCmd(ChatManager chatManager)
        {
            _chatManager = chatManager;
        }

        public ModelStatus Execute(ref PlayerServer player, Chat chat, List<string> argsM, ServiceContext context)
        {
            bool online = false;
            bool all = false;
            var ownLogin = player.Public.Login;
            if (argsM.Count < 3)
            {
                return _chatManager.PostCommandPrivatPostActivChat(ChatCmdResult.IncorrectSubCmd, ownLogin, chat,
                   "OC_IncdidentMessadge_ArgErr"); // Потрібно щонайменше 3 аргументи: ім'я гравця, заголовок і текст.
            }
            int argNum = 0;

            PlayerServer targetPlayer = null;
            if (argsM[argNum] == "online")
            {
                online = true;
            }
            else if (argsM[argNum] == "all")
            {
                all = true;
            }
            else
            {
                targetPlayer = Repository.GetPlayerByLogin(argsM[argNum]);
                if (targetPlayer == null)
                {
                    return _chatManager.PostCommandPrivatPostActivChat(ChatCmdResult.UserNotFound, ownLogin, chat, "User " + argsM[1] + " not found");
                }
                if (targetPlayer == player)
                {
                    return _chatManager.PostCommandPrivatPostActivChat(ChatCmdResult.IncorrectSubCmd, ownLogin, chat,
                        "OC_IncdidentMessadge_targetErr");// Не можна вказувати самого себе.
                }
            }
            argNum++;

            ModelMailMessadge.MessadgeTypes type = ModelMailMessadge.MessadgeTypes.Neutral;
            string label;
            string text = "";

            if (argsM[argNum][0] == '/')
            {
                var str = argsM[argNum].ToLower();
                switch (str)
                {
                    case "/treatbig":
                        type = ModelMailMessadge.MessadgeTypes.ThreatBig;
                        break;
                    case "/treatsmall":
                        type = ModelMailMessadge.MessadgeTypes.ThreatSmall;
                        break;
                    case "/death":
                        type = ModelMailMessadge.MessadgeTypes.Death;
                        break;
                    case "/negative":
                        type = ModelMailMessadge.MessadgeTypes.Negative;
                        break;
                    case "/positive":
                        type = ModelMailMessadge.MessadgeTypes.Positive;
                        break;
                    case "/visitor":
                        type = ModelMailMessadge.MessadgeTypes.Visitor;
                        break;
                    case "/neutral":
                        type = ModelMailMessadge.MessadgeTypes.Neutral;
                        break;
                    default:
                        return _chatManager.PostCommandPrivatPostActivChat(ChatCmdResult.IncorrectSubCmd, ownLogin, chat,
                        "OC_IncdidentMessadge_typeErr");  // Неправильний тип повідомлення.
                }
                argNum++;
            }

            label = argsM[argNum++];

            while (argNum < argsM.Count)
            {
                text += argsM[argNum++] + " ";
            }


            // Формуємо пакет.
            string reciever = "error";
            if (online)
            {
                reciever = "online";
                foreach (PlayerServer pl in Repository.GetData.GetPlayersAll)
                {
                    if (pl.Online)
                    {
                        if (pl == player) continue;
                        reciever += " " + pl.Public.Login;
                        var packet = new ModelMailMessadge()
                        {
                            From = player.Public,
                            type = type,
                            label = label,
                            text = text,
                        };
                        lock (pl)
                        {
                            packet.To = pl.Public;
                            pl.Mails.Add(packet);
                        }
                    }
                }
            }
            else if (all)
            {
                reciever = "all:";
                foreach (PlayerServer pl in Repository.GetData.GetPlayersAll)
                {
                    if (pl == player) continue;
                    reciever += " " + pl.Public.Login;
                    var packet = new ModelMailMessadge()
                    {
                        From = player.Public,
                        type = type,
                        label = label,
                        text = text,
                    };
                    lock (pl)
                    {
                        packet.To = pl.Public;
                        pl.Mails.Add(packet);
                    }
                }
            }
            else
            {
                reciever = targetPlayer.Public.Login;
                var packet = new ModelMailMessadge()
                {
                    From = player.Public,
                    type = type,
                    label = label,
                    text = text,
                };
                lock (targetPlayer)
                {
                    packet.To = targetPlayer.Public;
                    targetPlayer.Mails.Add(packet);
                }
            }

            Loger.Log("say to " + reciever);

            return new ModelStatus() { Status = 0 };
        }
    }
}
