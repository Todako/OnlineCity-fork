using Model;
using System;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Базовий клас для листів. Яку саме дію буде виконано на клієнті при отриманні листа — див. клас RimWorldOnlineCity.MailController
    /// </summary>
    [Serializable]
    public abstract class ModelMail
    {
        /// <summary>
        /// Час, коли лист було вперше додано на сервер (наразі використовується для забезпечення унікальності листа)
        /// </summary>
        public DateTime Created { get; set; }

        public Player From { get; set; }
        public Player To { get; set; }

        public bool NeedSaveGame { get; set; }

        public ModelMail()
        {
            Created = DateTime.UtcNow;
        }

        public virtual string ContentString() => GetType().Name;

        public abstract string GetHash();

        /// <summary>
        /// Швидкий розрахунок базового хешу без створення тимчасових рядків у кучі пам'яті
        /// </summary>
        public int GetHashBase()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (GetType().Name?.GetHashCode() ?? 0);
                hash = hash * 31 + (From?.Login?.GetHashCode() ?? 0);
                hash = hash * 31 + (To?.Login?.GetHashCode() ?? 0);
                hash = hash * 31 + Created.Ticks.GetHashCode();

                var customHash = GetHash();
                if (customHash != null)
                {
                    hash = hash * 31 + customHash.GetHashCode();
                }

                return hash;
            }
        }
    }
}
