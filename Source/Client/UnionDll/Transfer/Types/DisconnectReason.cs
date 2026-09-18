using System;

namespace OCUnion.Transfer
{
    /// <summary>
    /// Причина відключення або розірвання з'єднання
    /// </summary>
    [Serializable]
    public enum DisconnectReason : byte
    {
        /// <summary>
        /// Усе добре, з'єднання стабільне, продовжуємо роботу
        /// </summary>
        AllGood = 0,

        /// <summary>
        /// З'єднання закрито за ініціативою клієнта (вихід із гри або відключення)
        /// </summary>
        CloseConnection = 1,

        /// <summary>
        /// Вичерпано час очікування відповіді від сервера (тайм-аут з'єднання)
        /// </summary>
        ConnectionTimeOut = 2,

        /// <summary>
        /// Невідповідність або помилка синхронізації файлів конфігурацій чи модифікацій
        /// </summary>
        FilesMods = 3,
    }
}
