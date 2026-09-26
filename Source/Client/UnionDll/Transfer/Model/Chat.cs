using System;
using System.Collections.Generic;

namespace Model
{
    [Serializable]
    public class Chat
    {
        // 0 — приватний системний чат, повідомлення не зберігаються і створюються щоразу заново для кожного користувача
        // 1 — публічний загальний чат
        public int Id;

        public string OwnerLogin;

        public string Name;

        /// <summary>
        /// Створений гравцем вручну (можна додавати інших гравців); 
        /// інакше — автоматичний з усіх, хто доступний власнику (його загальний чат).
        /// </summary>
        public bool OwnerMaker;

        public List<string> PartyLogin;

        public List<ChatPost> Posts = new List<ChatPost>();

        public DateTime LastChanged;

        public Chat()
        {
        }
    }
}