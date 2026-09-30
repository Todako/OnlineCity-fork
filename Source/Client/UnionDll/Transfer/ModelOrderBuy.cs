using System;

namespace Transfer
{
    [Serializable]
    public class ModelOrderBuy
    {
        public long OrderId { get; set; }

        public int Count { get; set; }
    }
}
