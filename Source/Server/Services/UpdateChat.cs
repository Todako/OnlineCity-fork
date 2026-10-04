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

        private const int FullRequestMinCountPosts = 20;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null) return null;
            var result = new ModelContainer { TypePacket = ResponseTypePackage };
            result.Packet = updateChat((ModelUpdateTime)request.Packet, context);
            return result;
        }

        private ModelUpdateChat updateChat(ModelUpdateTime time, ServiceContext context)
        {
            lock (context.Player)
            {
                var playerChats = context.Player.Chats;
                var res = new ModelUpdateChat
                {
                    Time = DateTime.UtcNow,
                    Chats = playerChats != null ? new List<Chat>(playerChats.Count) : new List<Chat>(0)
                };

                if (playerChats == null || playerChats.Count == 0)
                {
                    return res;
                }

                bool fullRequest = time == null || time.Time == DateTime.MinValue;
                var myLogin = context.Player.Public?.Login;

                // Список гравців, яких бачить гравець (до побудови консолі зв'язку — радіус 10 клітинок; модератори та discord бачать усіх)
                var ps = StaticHelper.PartyLoginSee(context.Player);

                foreach (var chatPair in playerChats)
                {
                    var ct = chatPair.Key;
                    var ix = chatPair.Value;
                    if (ct == null || ix == null) continue;

                    var resChat = new Chat
                    {
                        Id = ct.Id,
                        OwnerLogin = ct.OwnerLogin,
                        Name = ct.Name,
                        OwnerMaker = ct.OwnerMaker,
                        LastChanged = ct.LastChanged
                    };

                    int countOfPosts = ct.Posts?.Count ?? 0;

                    // Якщо це первинний запит — підтягуємо останні FullRequestMinCountPosts повідомлень без втрати першого
                    if (fullRequest && countOfPosts - ((int)ix.Value + 1) < FullRequestMinCountPosts)
                    {
                        ix.Value = countOfPosts <= FullRequestMinCountPosts ? -1 : countOfPosts - FullRequestMinCountPosts - 1;
                    }

                    int startIdx = Math.Max(0, (int)ix.Value + 1);
                    int toRead = countOfPosts - startIdx;

                    if (toRead > 0 && ct.Posts != null)
                    {
                        resChat.Posts = new List<ChatPost>(toRead);
                        lock (ct.Posts)
                        {
                            int actualCount = ct.Posts.Count;
                            for (int i = startIdx; i < actualCount; i++)
                            {
                                var post = ct.Posts[i];
                                if (post == null) continue;

                                if ((post.OnlyForPlayerLogin == null && post.OwnerLogin != null && ps.Contains(post.OwnerLogin))
                                    || (myLogin != null && post.OnlyForPlayerLogin == myLogin))
                                {
                                    resChat.Posts.Add(post);
                                }
                            }
                            ix.Value = actualCount - 1;
                        }
                    }
                    else
                    {
                        resChat.Posts = new List<ChatPost>(0);
                        ix.Value = countOfPosts - 1;
                    }

                    // Якщо змінився склад учасників — відправляємо ізольовану копію списку для уникнення збоїв серіалізації
                    if (fullRequest || ct.LastChanged > ix.Time)
                    {
                        lock (ct)
                        {
                            resChat.PartyLogin = ct.PartyLogin != null ? new List<string>(ct.PartyLogin) : null;
                        }
                        ix.Time = ct.LastChanged;
                    }

                    res.Chats.Add(resChat);
                }

                return res;
            }
        }
    }
}