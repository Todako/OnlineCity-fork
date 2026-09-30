using RimWorldOnlineCity.UI;

namespace RimWorldOnlineCity
{
    public class PanelViewInfo : DialogControlBase
    {
        private bool Inited;


        public void Init()
        {
            Inited = true;
        }

        public void Drow(Rect inRect)
        {
            if (!Inited) Init();

        }
    }
}
