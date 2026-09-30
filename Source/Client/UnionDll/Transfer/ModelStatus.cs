using System;

namespace Transfer
{
    [Serializable]
    public class ModelStatus
    {
        public int Status { get; set; }
        public string Message { get; set; }
    }
}
