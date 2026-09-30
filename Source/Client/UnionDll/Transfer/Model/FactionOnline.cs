using System;

namespace Model
{
    [Serializable]
    public class FactionOnline
    {
        public string Name { get; set; }
        public string LabelCap { get; set; }
        public string DefName { get; set; }
        public int loadID { get; set; }
    }
}
