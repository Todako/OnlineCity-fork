using System;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class StatePosition
    {
        public string StateName { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool RightHead { get; set; }

        public bool RightEdit { get; set; }

        public bool RightAddPlayer { get; set; }

        public bool RightExcludePlayer { get; set; }
    }
}
