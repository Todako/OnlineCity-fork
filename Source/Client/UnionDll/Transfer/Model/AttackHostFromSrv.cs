using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// Пакет стану бою, що надсилається сервером гравцю-захиснику (хосту карти)
    /// </summary>
    [Serializable]
    public class AttackHostFromSrv
    {
        /// <summary>
        /// Текст помилки, якщо операція завершилася невдачею
        /// </summary>
        public string ErrorText;

        /// <summary>
        /// Поточний числовий стан сесії атаки
        /// </summary>
        public int State;

        /// <summary>
        /// Логін гравця-ініціатора нападу
        /// </summary>
        public string StartInitiatorPlayer;

        /// <summary>
        /// Серверний ID об'єкта карти захисника
        /// </summary>
        public long HostPlaceServerId;

        /// <summary>
        /// Серверний ID об'єкта каравану/бази атакуючого
        /// </summary>
        public long InitiatorPlaceServerId;

        /// <summary>
        /// Список пішаків, залучених до бою
        /// </summary>
        public List<ThingEntry> Pawns;

        /// <summary>
        /// Список оновлених команд для пішаків від атакуючого
        /// </summary>
        public List<AttackPawnCommand> UpdateCommand;

        /// <summary>
        /// Список ID об'єктів, які потребують синхронізації з клієнтом
        /// </summary>
        public List<int> NeedNewThingIDs;

        /// <summary>
        /// Прапорець тестового режиму
        /// </summary>
        public bool TestMode;

        /// <summary>
        /// Час встановлення паузи в бою
        /// </summary>
        public DateTime SetPauseOnTime;

        /// <summary>
        /// Прапорець перемоги захисника
        /// </summary>
        public bool VictoryHost;

        /// <summary>
        /// Прапорець критичної фатальної помилки сесії
        /// </summary>
        public bool TerribleFatalError;
    }
}
