using System;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class State
    {
        public string Name { get; set; }

        public string Color { get; set; }

        public string Description { get; set; }

        public int MarketValueRanking { get; set; }

        public int MarketValueRankingLast { get; set; }

    }
}
