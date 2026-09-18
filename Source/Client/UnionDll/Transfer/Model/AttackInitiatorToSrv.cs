using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// Пакет команд та стану бою, що надсилається атакуючим гравцем (ініціатором) до сервера
    /// </summary>
    [Serializable]
    public class AttackInitiatorToSrv
    {
        /// <summary>
        /// Поточний числовий стан сесії бою
        /// </summary>
        public int State;

        /// <summary>
        /// Прапорець тестового режиму
        /// </summary>
        public bool TestMode;

        /// <summary>
        /// Логін гравця-захисника (хоста карти)
        /// </summary>
        public string StartHostPlayer;

        /// <summary>
        /// Серверний ідентифікатор об'єкта карти захисника
        /// </summary>
        public long HostPlaceServerId;

        /// <summary>
        /// Серверний ідентифікатор об'єкта каравану або бази атакуючого
        /// </summary>
        public long InitiatorPlaceServerId;

        /// <summary>
        /// Список підконтрольних пішаків атакуючого
        /// </summary>
        public List<ThingEntry> Pawns;

        /// <summary>
        /// Список оновлених наказів і команд для пішаків
        /// </summary>
        public List<AttackPawnCommand> UpdateCommand;

        /// <summary>
        /// Список ідентифікаторів об'єктів, дані про які потрібно отримати від сервера
        /// </summary>
        public List<int> NeedNewThingIDs;

        /// <summary>
        /// Запит на встановлення паузи в бою для хоста
        /// </summary>
        public TimeSpan SetPauseOnTimeToHost;

        /// <summary>
        /// Прапорець визнання перемоги захисника (здача атакуючого)
        /// </summary>
        public bool VictoryHostToHost;

        /// <summary>
        /// Прапорець критичної фатальної помилки на боці атакуючого
        /// </summary>
        public bool TerribleFatalError;
    }
}
