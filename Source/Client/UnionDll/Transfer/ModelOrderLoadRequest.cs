using System;
using System.Collections.Generic;

namespace Transfer
{
    [Serializable]
    public class ModelOrderLoadRequest
    {
        public List<int> Tiles { get; set; }
        public string FilterBuy { get; set; }
        public string FilterSell { get; set; }
    }
}
