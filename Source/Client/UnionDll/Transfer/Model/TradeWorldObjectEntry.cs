using System;

namespace Model
{
    public enum TradeWorldObjectEntryType
    {
        TradeOrder,
        ThingsPlayer
    }

    /// <summary>
    /// Клас використовується як батьківський, а також як спрощена копія TradeOrder для завантаження всіх ордерів на карту планети.
    /// </summary>
    [Serializable]
    public class TradeWorldObjectEntry : IModelPlace
    {
        public long Id { get; set; }
        public TradeWorldObjectEntryType Type { get; set; }
        public int Tile { get; set; }
        public long PlaceServerId { get; set; }
        public string Name { get; set; }



        public string LoginOwner { get; set; }

        /// <summary>
        /// Лише для сервера (не є конфіденційним).
        /// </summary>
        public DateTime UpdateTime { get; set; }

        public TradeWorldObjectEntry()
        { }

    }
}
