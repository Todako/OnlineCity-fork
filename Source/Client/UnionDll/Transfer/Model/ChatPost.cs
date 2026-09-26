using System;

namespace Model
{
    [Serializable]
    public class ChatPost
    {
        public string OwnerLogin { get; set; }

        public int IdOwner { get; set; }

        public DateTime Time { get; set; }

        public string Message { get; set; }

        /// <summary>
        /// Показувати лише даному гравцеві, якщо не задано показувати всім.
        /// Наприклад, відповідь на /help запише сюди ім'я гравця, а в OwnerLogin — "system".
        /// </summary>
        public string OnlyForPlayerLogin { get; set; }

        /// <summary>
        /// Службове поле. Якщо не дорівнює 0, повідомлення надійшло з Discord і його не слід відправляти туди назад.
        /// </summary>
        public ulong DiscordIdMessage { get; set; }
    }
}