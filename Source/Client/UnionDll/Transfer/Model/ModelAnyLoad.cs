using System;
using System.Collections.Generic;

namespace OCUnion.Transfer.Model
{
    /// <summary>
    /// Універсальний контейнер для завантаження або пакетної передачі даних за хешами
    /// </summary>
    [Serializable]
    public class ModelAnyLoad
    {
        /// <summary>
        /// Список числових хешів об'єктів чи фрагментів даних
        /// </summary>
        public List<long> Hashs;

        /// <summary>
        /// Список серіалізованих рядкових даних, що відповідають зазначеним хешам
        /// </summary>
        public List<string> Datas;
    }
}
