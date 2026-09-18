using System;

namespace OCUnion.Transfer
{
    /// <summary>
    /// Тип запитуваної інформації про сервер або стан світу
    /// </summary>
    [Serializable]
    public enum ServerInfoType : byte
    {
        /// <summary>
        /// Повна базова інформація про сервер
        /// </summary>
        Full = 1,

        /// <summary>
        /// Коротка інформація (основний статус)
        /// </summary>
        Short = 2,

        /// <summary>
        /// Запит на відправку файлу збереження світу
        /// </summary>
        SendSave = 3,

        /// <summary>
        /// Повна інформація з докладним текстовим описом
        /// </summary>
        FullWithDescription = 4
    }
}
