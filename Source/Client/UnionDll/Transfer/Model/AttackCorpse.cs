using Model;
using System;

namespace OCUnion.Transfer.Model
{
    /// <summary>
    /// Дані про труп пішака, передані під час синхронізації онлайн-битви
    /// </summary>
    [Serializable]
    public class AttackCorpse
    {
        /// <summary>
        /// Запис предмета трупа разом із даними внутрішнього пішака
        /// </summary>
        public ThingEntry CorpseWithPawn;

        /// <summary>
        /// Ідентифікатор об'єкта трупа на карті
        /// </summary>
        public int CorpseId;

        /// <summary>
        /// Ідентифікатор загиблого пішака
        /// </summary>
        public int PawnId;
    }
}
