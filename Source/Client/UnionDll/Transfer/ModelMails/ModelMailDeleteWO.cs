using Model;
using System;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Лист-сповіщення про видалення об'єкта світу (поселення, каравану тощо)
    /// </summary>
    [Serializable]
    public class ModelMailDeleteWO : ModelMail, IModelPlace
    {
        /// <summary>
        /// Номер тайла карти, де знаходився об'єкт
        /// </summary>
        public int Tile { get; set; }

        /// <summary>
        /// Серверний ідентифікатор об'єкта
        /// </summary>
        public long PlaceServerId { get; set; }

        /// <summary>
        /// Формування унікального рядкового ключа без пакування типів (boxing)
        /// </summary>
        public override string GetHash() => string.Concat("T", Tile.ToString(), "P", PlaceServerId.ToString());
    }
}
