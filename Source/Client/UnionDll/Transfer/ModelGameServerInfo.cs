using Model;
using System;
using System.Collections.Generic;

namespace Transfer
{
    [Serializable]
    public class ModelGameServerInfo
    {
        public List<WorldObjectOnline> WObjectOnlineList { get; set; }
        public List<FactionOnline> FactionOnlineList { get; set; }
    }
}
