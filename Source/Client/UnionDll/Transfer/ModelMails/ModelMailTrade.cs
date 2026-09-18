using Model;
using System;
using System.Collections.Generic;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Посилка (передача предметів) від каравану або поселення іншого гравця
    /// </summary>
    [Serializable]
    public class ModelMailTrade : ModelMail, IModelPlace
    {
        /// <summary>
        /// Номер тайла карти, куди надходить посилка
        /// </summary>
        public int Tile { get; set; }

        /// <summary>
        /// Серверний ідентифікатор цільового об'єкта (каравану чи бази)
        /// </summary>
        public long PlaceServerId { get; set; }

        /// <summary>
        /// Список предметів, що передаються в посилці
        /// </summary>
        public List<ThingEntry> Things { get; set; }

        /// <summary>
        /// Формування унікального рядкового хешу без пакування типів у кучі (boxing)
        /// </summary>
        public override string GetHash()
        {
            return string.Concat("T", Tile.ToString(), "P", PlaceServerId.ToString(), " ", ContentString());
        }

        /// <summary>
        /// Рядкове представлення вмісту посилки
        /// </summary>
        public override string ContentString()
        {
            if (Things == null || Things.Count == 0)
            {
                return string.Empty;
            }

            return Things.ToStringLabel();
        }
    }
}
