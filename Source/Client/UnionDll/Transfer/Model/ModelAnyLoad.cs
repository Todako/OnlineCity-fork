using System;
using System.Collections.Generic;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class ModelAnyLoad
    {
        public List<long> Hashs { get; set; }
        public List<string> Datas { get; set; }

    }
}
