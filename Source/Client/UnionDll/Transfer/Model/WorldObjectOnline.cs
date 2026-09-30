using System;

namespace Model
{
    [Serializable]
    public class WorldObjectOnline
    {
        public string Name { get; set; }
        public int Tile { get; set; }
        public string FactionGroup { get; set; }
        public string FactionDef { get; set; }
        public int loadID { get; set; }

    }
}
