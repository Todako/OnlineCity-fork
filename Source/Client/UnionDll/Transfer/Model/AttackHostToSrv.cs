using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;

namespace Model
{
    /// <summary>
    /// Пакет стану карти та дій оборони, що надсилається хостом (захисником) до сервера під час онлайн-битви
    /// </summary>
    [Serializable]
    public class AttackHostToSrv
    {
        /// <summary>
        /// Поточний числовий стан сесії бою
        /// </summary>
        public int State;

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
        /// Координати розташування нових або змінених предметів/будівель
        /// </summary>
        public List<IntVec3S> ThingCell;

        /// <summary>
        /// Список предметів/будівель на відповідних клітинках
        /// </summary>
        public List<ThingTrade> Thing;

        /// <summary>
        /// Список нових пішаків, залучених до карти бою
        /// </summary>
        public List<ThingEntry> NewPawns;

        /// <summary>
        /// Ідентифікатори нових пішаків
        /// </summary>
        public List<int> NewPawnsId;

        /// <summary>
        /// Список нових предметів (наприклад, скинутий лут чи зброя)
        /// </summary>
        public List<ThingTrade> NewThings;

        /// <summary>
        /// Ідентифікатори нових предметів
        /// </summary>
        public List<int> NewThingsId;

        /// <summary>
        /// Ідентифікатори об'єктів або речей, знищених на карті під час бою
        /// </summary>
        public List<int> Delete;

        /// <summary>
        /// Оновлені стани об'єктів (здоров'я, пошкодження тощо)
        /// </summary>
        public List<AttackThingState> UpdateState;

        /// <summary>
        /// Прапорець перемоги атакуючого гравця (null, якщо бій триває)
        /// </summary>
        public bool? VictoryAttacker;

        /// <summary>
        /// Список трупів, які з'явилися на полі бою
        /// </summary>
        public List<AttackCorpse> NewCorpses;
    }
}
