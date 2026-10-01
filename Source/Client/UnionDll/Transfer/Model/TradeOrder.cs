using Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace Transfer
{
    [Serializable]
    public class TradeOrder : TradeOrderShort
    {
        // Id на сервері.
        // Якщо 0 — додати новий.
        // Якщо більше 0 — відредагувати.
        // Якщо менше 0 — видалити з Id без мінуса.

        /// <summary>
        /// Власник угоди; його ім'я зберігається в LoginOwner.
        /// </summary>
        [XmlIgnore]
        public Player Owner { get; set; }

        /// <summary>
        /// Де знаходиться товар.
        /// </summary>
        public Place Place { get; set; }

        /// <summary>
        /// Час розміщення на сервері.
        /// </summary>
        public DateTime Created { get; set; }

        /// <summary>
        /// Речі (тип і кількість), які передасть Owner. Для всіх має бути Concrete = true.
        /// </summary>
        public List<ThingTrade> SellThings { get; set; }

        /// <summary>
        /// Речі, які отримає Owner. Concrete може мати будь-яке значення.
        /// Можна використовувати лише як фільтр: BuyThings[i].MatchesThing().
        /// </summary>
        public List<ThingTrade> BuyThings { get; set; }

        /// <summary>
        /// Кількість успішно виконаних угод.
        /// </summary>
        public int CountFnished { get; set; }

        /// <summary>
        /// Кількість доступних угод. Визначає кількість заблокованих речей (SellThings*CountReady).
        /// </summary>
        public int CountReady { get; set; }

        /// <summary>
        /// Якщо список не порожній, це приватна угода, доступна лише переліченим гравцям.
        /// </summary>
        public List<Player> PrivatPlayers { get; set; }

        /// <summary>
        /// Потрібно вручну скидати в null після зміни SellThings.
        /// </summary>
        public int SellThingsHash
        {
            get
            {
                if (_SellThingsHash == 0) _SellThingsHash = GetThingsHash(SellThings);
                return _SellThingsHash;
            }
            set => _SellThingsHash = value;
        }

        [NonSerialized]
        [XmlIgnore]
        private int _SellThingsHash;

        /// <summary>
        /// Потрібно вручну скидати в null після зміни SellThings.
        /// </summary>
        public int BuyThingsHash
        {
            get
            {
                if (_BuyThingsHash == 0) _BuyThingsHash = GetThingsHash(BuyThings);
                return _BuyThingsHash;
            }
            set => _BuyThingsHash = value;
        }

        [NonSerialized]
        [XmlIgnore]
        private int _BuyThingsHash;

        /// <summary>
        /// Хеш для попередньої перевірки простих умов.
        /// Чи відповідає один список речей іншому за типом речей (точніше, чи збігаються дані для перевірки на точну рівність).
        /// </summary>
        private int GetThingsHash(List<ThingTrade> things)
        {
            return GetThingsHashString(things)
                .GetHashCode();
        }

        private string GetThingsHashString(List<ThingTrade> things)
        {
            return things.Count == 0 ? "empty"
                : things.Select(t => t.DefName + "(" + t.PawnParam + ")" + (t.Count == 1 ? "" : "*" + t.Count) + "#")
                .OrderBy(t => t)
                .Distinct()
                .Aggregate((r, i) => r + i);
        }

        public override string ToString()
        {
            return "Order " + Id + " " + Owner.Login + " tile=" + Tile + " cnt=" + this.CountReady
                + " " + GetThingsHashString(BuyThings) + " ==buyToSell==> " + GetThingsHashString(SellThings)
                + (PrivatPlayers != null && PrivatPlayers.Count > 0 ? " privat=" + string.Join(",", PrivatPlayers.Select(p => p.Login)) : "");
        }

        public TradeOrder Clone()
        {
            var clone = (TradeOrder)this.MemberwiseClone();
            clone.SellThings = this.SellThings.Select(t => (ThingTrade)t.Clone()).ToList();
            clone.BuyThings = this.BuyThings.Select(t => (ThingTrade)t.Clone()).ToList();
            clone.PrivatPlayers = new List<Player>(this.PrivatPlayers);
            return clone;
        }

    }
}
