using System;

namespace Model
{
    /// <summary>
    /// Окреме повідомлення в каналі чату
    /// </summary>
    [Serializable]
    public class ChatPost
    {
        /// <summary>
        /// Логін автора повідомлення
        /// </summary>
        public string OwnerLogin;

        /// <summary>
        /// Числовий ідентифікатор автора повідомлення
        /// </summary>
        public int IdOwner;

        /// <summary>
        /// Час відправлення повідомлення
        /// </summary>
        public DateTime Time;

        /// <summary>
        /// Текст повідомлення
        /// </summary>
        public string Message;

        /// <summary>
        /// Показувати лише зазначеному гравцеві; якщо значення не вказано — повідомлення бачать усі учасники чату.
        /// Наприклад, для відповіді на команду /help тут записується нікнейм гравця, а в OwnerLogin — значення "system".
        /// </summary>
        public string OnlyForPlayerLogin;

        /// <summary>
        /// Службове поле: якщо значення != 0, повідомлення надійшло з Discord і його не потрібно відправляти туди назад
        /// </summary>
        public ulong DiscordIdMessage;
    }
}
