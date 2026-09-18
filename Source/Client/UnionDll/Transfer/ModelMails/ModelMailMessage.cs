using Model;
using System;

namespace Transfer.ModelMails
{
    /// <summary>
    /// Лист-сповіщення загального призначення (текстові повідомлення, попередження про загрози, візити)
    /// </summary>
    [Serializable]
    public class ModelMailMessadge : ModelMail, IModelPlace
    {
        public string label;
        public string text;
        public MessadgeTypes type = MessadgeTypes.Neutral;

        public int Tile { get; set; }
        public long PlaceServerId { get; set; }

        public override string GetHash() => "NotContent";

        /// <summary>
        /// Тип сповіщення (базовий тип byte для економії мережевого трафіку)
        /// </summary>
        [Serializable]
        public enum MessadgeTypes : byte
        {
            ThreatBig = 0,
            ThreatSmall = 1,
            Negative = 2,
            Neutral = 3,
            Positive = 4,
            Death = 5,
            Visitor = 6,

            GoldenLetter = 7,
            GreyGoldenLetter = 8,
        }
    }
}
