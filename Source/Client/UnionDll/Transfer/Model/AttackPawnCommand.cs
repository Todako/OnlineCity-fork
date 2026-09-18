using System;

namespace OCUnion.Transfer.Model
{
    /// <summary>
    /// Команда та параметри цілі для конкретного пішака під час онлайн-битви
    /// </summary>
    [Serializable]
    public class AttackPawnCommand
    {
        /// <summary>
        /// Тип наказу пішаку (базовий тип byte для мінімізації трафіку)
        /// </summary>
        [Serializable]
        public enum PawnCommand : byte
        {
            /// <summary>
            /// Очікування в бойовому режимі
            /// </summary>
            Wait_Combat = 0,

            /// <summary>
            /// Йти в точку
            /// </summary>
            Goto = 1,

            /// <summary>
            /// Стріляти / дистанційна атака
            /// </summary>
            Attack = 2,

            /// <summary>
            /// Атакувати в ближньому бою
            /// </summary>
            AttackMelee = 3,

            /// <summary>
            /// Екіпірувати предмет як зброю
            /// </summary>
            Equip = 4,

            /// <summary>
            /// Взяти предмет до інвентарю
            /// </summary>
            TakeInventory = 5,

            /// <summary>
            /// Одягнути елемент одягу чи броню
            /// </summary>
            Wear = 6,

            /// <summary>
            /// Кинути поточну зброю
            /// </summary>
            DropEquipment = 7,

            /// <summary>
            /// Зняти елемент одягу
            /// </summary>
            RemoveApparel = 8,

            /// <summary>
            /// З'їсти або спожити речовину/ліки
            /// </summary>
            Ingest = 9,

            /// <summary>
            /// Роздягнути труп або знерухомленого ворога
            /// </summary>
            Strip = 10,

            /// <summary>
            /// Надання медичної допомоги / самолікування
            /// </summary>
            TendPatient = 11,

            /// <summary>
            /// Команда скидання предмета з інвентарю на землю (пряма дія без постановки Job)
            /// </summary>
            OC_InventoryDrop = 12,
        }

        /// <summary>
        /// Ідентифікатор пішака хоста (карти), якому призначено команду
        /// </summary>
        public int HostPawnID;

        /// <summary>
        /// Команда, яку необхідно виконати
        /// </summary>
        public PawnCommand Command;

        /// <summary>
        /// Координати цільової клітинки на карті бою
        /// </summary>
        public IntVec3S TargetPos;

        /// <summary>
        /// Ідентифікатор цільового об'єкта (пішака, предмета, будівлі)
        /// </summary>
        public int TargetID;

        /// <summary>
        /// Назва Def-а цільового об'єкта
        /// </summary>
        public string TargetDefName;
    }
}
