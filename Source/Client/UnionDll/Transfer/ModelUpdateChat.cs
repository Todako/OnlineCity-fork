using Model;
using System;
using System.Collections.Generic;

namespace Transfer
{
    [Serializable]
    public class ModelUpdateChat
    {
        public DateTime Time;

        public int LastChatPostId;

        public List<Chat> Chats;
    }
}
