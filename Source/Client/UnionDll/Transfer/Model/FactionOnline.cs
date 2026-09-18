using System;

namespace Model
{
    /// <summary>
    /// Модель даних онлайн-фракції для синхронізації між клієнтом та сервером
    /// </summary>
    [Serializable]
    public class FactionOnline
    {
        /// <summary>
        /// Власна назва фракції
        /// </summary>
        public string Name;

        /// <summary>
        /// Назва фракції з великої літери (для інтерфейсу)
        /// </summary>
        public string LabelCap;

        /// <summary>
        /// Назва Def-а типу фракції
        /// </summary>
        public string DefName;

        /// <summary>
        /// Унікальний числовий ідентифікатор фракції у збереженні світу
        /// </summary>
        public int loadID;
    }
}
