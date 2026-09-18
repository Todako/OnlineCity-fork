using System;
using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// Модель каналу чату (системного, публічного або приватного/групового)
    /// </summary>
    [Serializable]
    public class Chat
    {
        /// <summary>
        /// Ідентифікатор системного приватного чату (повідомлення не зберігаються)
        /// </summary>
        public const int SystemChatId = 0;

        /// <summary>
        /// Ідентифікатор загального публічного чату
        /// </summary>
        public const int PublicChatId = 1;

        /// <summary>
        /// Ідентифікатор чату:
        /// 0 — приватний системний чат (повідомлення не зберігаються, створюється щоразу новий для кожного користувача);
        /// 1 — загальний публічний чат;
        /// > 1 — призначені для користувача або групові канали.
        /// </summary>
        public int Id;

        /// <summary>
        /// Логін власника (творця) каналу чату
        /// </summary>
        public string OwnerLogin;

        /// <summary>
        /// Назва каналу чату
        /// </summary>
        public string Name;

        /// <summary>
        /// Ознака створення користувачем вручну (true — створений гравцем, можна додавати учасників; 
        /// false — автоматичний спільний чат для всіх доступних власнику контактів)
        /// </summary>
        public bool OwnerMaker;

        /// <summary>
        /// Список логінів учасників, які входять до цього чату
        /// </summary>
        public List<string> PartyLogin;

        /// <summary>
        /// Список повідомлень каналу
        /// </summary>
        public List<ChatPost> Posts = new List<ChatPost>();

        /// <summary>
        /// Час останньої зміни або надходження нового повідомлення в чат
        /// </summary>
        public DateTime LastChanged;

        public Chat()
        {
        }
    }
}
