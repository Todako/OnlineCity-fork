using System;

namespace Model
{
    public enum WorldObjectEntryType
    {
        Base,
        Caravan
    }

    [Serializable]
    public class WorldObjectEntry : IModelPlace
    {
        // На клієнті дані інших гравців надходять із сервера, а власні заповнюються самостійно.
        // Виняток — поля MarketValueStorage і MarketValueBalance: значення для власних об'єктів заповнюються окремо.
        public WorldObjectEntryType Type { get; set; }
        public int Tile { get; set; }
        public string Name { get; set; }
        public long PlaceServerId { get; set; }
        public float FreeWeight { get; set; }
        public float MarketValue { get; set; }
        public float MarketValuePawn { get; set; }
        public float MarketValueStorage { get; set; }
        public float MarketValueBalance { get; set; }
        public float MarketValueTotal => MarketValue + MarketValuePawn + MarketValueStorage + MarketValueBalance;

        public string LoginOwner { get; set; }

        /// <summary>
        /// Лише для сервера (не є конфіденційним).
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }
}
