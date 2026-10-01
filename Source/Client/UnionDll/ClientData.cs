using Model;
using System;
using System.Collections.Generic;
using Transfer;
using RimWorld;
using Verse;

namespace OCUnion
{
    /// <summary>
    /// Базовий клас ClientData, незалежний від RimWorld та коду Verse.
    /// </summary>
    public class ClientData
    {
        public DateTime UpdateTime = DateTime.MinValue;

        /// <summary>
        /// Різниця між UtcNow клієнта і сервера + час передачі від сервера до клієнта (половина пінгу).
        /// </summary>
        public TimeSpan ServetTimeDelta = new TimeSpan(0);

        /// <summary>
        /// Час оновлення даних чату.
        /// </summary>
        public TimeSpan Ping = new TimeSpan(0);

        public List<Chat> Chats;

        public int ChatNotReadPost;

        public byte[] SaveFileData;

        public long LastSaveTick;

        private readonly string _myLogin;

        private readonly SessionClient _sessionClient;

        public ClientData(string myLogin, SessionClient sessionClient)
        {
            _myLogin = myLogin;
            _sessionClient = sessionClient;
        }

        public bool ServerConnected
        {
            get
            {
                return _sessionClient.IsLogined
                    && (LastServerConnect == DateTime.MinValue
                        || (DateTime.UtcNow - LastServerConnect).TotalSeconds < 8);
            }
        }

        public DateTime LastServerConnect = DateTime.MinValue;
        public bool LastServerConnectFail = false;
        public int ChatCountSkipUpdate = 0;

        /// <summary>
        /// Застосовує оновлення чатів без виділення зайвої пам'яті через LINQ.
        /// </summary>
        public bool ApplyChats(ModelUpdateChat updateDate, ref string newStr)
        {
            int newPost = 0;
            newStr = "";

            if (updateDate?.Chats == null || updateDate.Chats.Count == 0)
                return false;

            if (Chats != null)
            {
                for (int i = 0; i < updateDate.Chats.Count; i++)
                {
                    var chat = updateDate.Chats[i];
                    Chat cur = null;

                    // Швидкий пошук чату за індексом замість LINQ FirstOrDefault
                    for (int j = 0; j < Chats.Count; j++)
                    {
                        if (Chats[j].Id == chat.Id)
                        {
                            cur = Chats[j];
                            break;
                        }
                    }

                    if (cur != null)
                    {
                        if (chat.Posts != null && chat.Posts.Count > 0)
                        {
                            cur.Posts.AddRange(chat.Posts);

                            // Підрахунок нових повідомлень без виділення нових списків у купі
                            for (int p = 0; p < chat.Posts.Count; p++)
                            {
                                var post = chat.Posts[p];
                                if (post.OwnerLogin != _myLogin)
                                {
                                    newPost++;
                                    if (string.IsNullOrEmpty(newStr))
                                    {
                                        newStr = chat.Name + ": " + post.Message;
                                    }
                                }
                            }
                            chat.Posts = cur.Posts;
                        }

                        if (chat.PartyLogin != null)
                        {
                            cur.PartyLogin = chat.PartyLogin;
                        }
                    }
                }
            }
            else
            {
                Chats = updateDate.Chats;
            }

            ChatNotReadPost += newPost;
            return newPost > 0;
        }
    }
}