using System;
using System.Collections.Generic;
using Model;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Common;
using ServerOnlineCity.Model;
using Transfer;

namespace ServerOnlineCity.Services
{
    public sealed class UpdateChat : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request17;

        public int ResponseTypePackage => (int)PackageType.Response18;

        private readonly ChatManager _chatManager = ChatManager.Instance;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = updateChat((ModelUpdateTime)request.Packet, context);
            return result;
        }

        private ModelUpdateChat updateChat(ModelUpdateTime time, ServiceContext context)
        {
            lock (context.Player)
            {
                var res = new ModelUpdateChat()
                {
                    Time = DateTime.UtcNow,
                    Chats = new List<Chat>(),
                };
                bool fullRequest = time.Time == DateTime.MinValue;

                var myLogin = context.Player.Public.Login;

                // Список гравців, яких бачить гравець (до побудови консолі зв'язку — радіус 10 клітинок; модератори та discord бачать усіх)
                var ps = StaticHelper.PartyLoginSee(context.Player);

                foreach (var chatPair in context.Player.Chats)
                {
                    var ct = chatPair.Key;
                    var resChat = new Chat()
                    {
                        Id = ct.Id,
                        OwnerLogin = ct.OwnerLogin,
                        Name = ct.Name,
                        OwnerMaker = ct.OwnerMaker,
                        Posts = new List<ChatPost>(),
                        LastChanged = ct.LastChanged,
                    };

                    // Копіюємо чат без зайвих даних та відфільтровуємо повідомлення
                    var ix = chatPair.Value;
                    var countOfPosts = ct.Posts.Count;
                    const int fullRequestMinCountPosts = 20;

                    if (fullRequest && countOfPosts - ((int)ix.Value + 1) < fullRequestMinCountPosts)
                    {
                        ix.Value = countOfPosts - fullRequestMinCountPosts - 1;
                        if (ix.Value < 0) ix.Value = 0;
                    }

                    int startIdx = (int)ix.Value + 1;
                    for (int i = startIdx; i < countOfPosts; i++)
                    {
                        var post = ct.Posts[i];
                        if ((post.OnlyForPlayerLogin == null && ps.Contains(post.OwnerLogin)) || post.OnlyForPlayerLogin == myLogin)
                        {
                            resChat.Posts.Add(post);
                        }
                    }

                    ix.Value = countOfPosts - 1;

                    // Якщо від моменту останньої зміни змінився список учасників — надсилаємо оновлений перелік
                    if (fullRequest || ct.LastChanged > ix.Time)
                    {
                        resChat.PartyLogin = ct.PartyLogin;
                        ix.Time = ct.LastChanged;
                    }

                    res.Chats.Add(resChat);
                }

                return res;
            }
        }
    }
}