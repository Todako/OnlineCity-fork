using Model;
using System;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Базовий клас листів. Дію, яку виконає клієнт після отримання листа, див. у класі RimWorldOnlineCity.MailController.
    /// </summary>
    [Serializable]
    public abstract class ModelMail
    {
        /// <summary>
        /// Час першого додавання на сервер; наразі використовується лише для унікальності листа.
        /// </summary>
        public DateTime Created { get; set; }
        public Player From { get; set; }
        public Player To { get; set; }

        public bool NeedSaveGame { get; set; }

        public ModelMail()
        {
            Created = DateTime.UtcNow;
        }

        public virtual string ContentString()
        {
            return this.GetType().Name;
        }

        public abstract string GetHash();

        public int GetHashBase()
        {
            return (GetType().Name + $":pf{From?.Login ?? "-"}pt{To?.Login ?? "-"}с{Created.Ticks}:" + GetHash()).GetHashCode();
        }
    }


}
