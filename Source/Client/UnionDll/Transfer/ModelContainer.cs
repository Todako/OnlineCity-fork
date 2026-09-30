using System;

namespace Transfer
{
    [Serializable]
    public class ModelContainer
    {
        public int TypePacket { get; set; }

        public object Packet { get; set; }


    }
}
