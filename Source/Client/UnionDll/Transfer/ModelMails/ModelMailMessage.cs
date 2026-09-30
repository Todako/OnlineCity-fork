using Model;
using System;

namespace Transfer.ModelMails
{

    [Serializable]
    public class ModelMailMessadge : ModelMail, IModelPlace
    {
        public string label;
        public string text;
        public MessadgeTypes type = MessadgeTypes.Neutral;

        public int Tile { get; set; }
        public long PlaceServerId { get; set; }

        public override string GetHash()
        {
            return "NotContent";
        }

        public enum MessadgeTypes
        {
            ThreatBig,
            ThreatSmall,
            Negative,
            Neutral,
            Positive,
            Death,
            Visitor,

            GoldenLetter,
            GreyGoldenLetter,
        }
    }

}