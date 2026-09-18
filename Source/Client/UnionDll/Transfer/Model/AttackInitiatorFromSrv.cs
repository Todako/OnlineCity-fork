using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// Пакет стану бою та оновлень карти, що надсилається сервером атакуючому гравцю (ініціатору)
    /// </summary>
    [Serializable]
    public class AttackInitiatorFromSrv
    {
        /// <summary>
        /// Текст помилки, якщо сесія або дія завершилися збоєм
        /// </summary>
        public string ErrorText;

        /// <summary>
        /// Поточний числовий стан сесії бою
        /// </summary>
        public int State;

        /// <summary>
        /// Прапорець тестового режиму
        /// </summary>
        public bool TestMode;

        /// <summary>
        /// Розмір карти бою
        /// </summary>
        public IntVec3S MapSize;

        /// <summary>
        /// Координати клітинок покриття території, що змінилися
        /// </summary>
        public List<IntVec3S> TerrainDefNameCell;

        /// <summary>
        /// Назви Def-ів покриття території
        /// </summary>
        public List<string> TerrainDefName;

        /// <summary>
        /// Координати розташування об'єктів або предметів
        /// </summary>
        public List<IntVec3S> ThingCell;

        /// <summary>
        /// Список об'єктів та предметів на відповідних клітинках
        /// </summary>
        public List<ThingTrade> Thing;

        /// <summary>
        /// Список нових пішаків, що з'явилися на карті бою
        /// </summary>
        public List<ThingEntry> NewPawns;

        /// <summary>
        /// Ідентифікатори нових пішаків
        /// </summary>
        public List<int> NewPawnsId;

        /// <summary>
        /// Список нових предметів (скинута зброя, спорядження тощо)
        /// </summary>
        public List<ThingTrade> NewThings;

        /// <summary>
        /// Ідентифікатори нових предметів
        /// </summary>
        public List<int> NewThingsId;

        /// <summary>
        /// Список нових трупів на полі бою
        /// </summary>
        public List<AttackCorpse> NewCorpses;

        /// <summary>
        /// Ідентифікатори знищених або видалених об'єктів карти
        /// </summary>
        public List<int> Delete;

        /// <summary>
        /// Оновлені стани об'єктів (здоров'я, пошкодження тощо)
        /// </summary>
        public List<AttackThingState> UpdateState;

        /// <summary>
        /// Прапорець переходу бою до завершальної фази
        /// </summary>
        public bool Finishing;

        /// <summary>
        /// Прапорець перемоги атакуючого гравця
        /// </summary>
        public bool VictoryAttacker;
    }
}
