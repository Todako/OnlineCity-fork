using System;
using System.Collections.Generic;

namespace Transfer
{
    [Serializable]
    public class ModelOrderLoad
    {
        public int Status { get; set; }

        public string Message { get; set; }

        public List<TradeOrder> Orders { get; set; }

    }
}
