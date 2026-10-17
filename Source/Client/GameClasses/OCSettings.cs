using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Нативні налаштування OnlineCity, що зберігаються через системний механізм RimWorld (Scribe).
    /// Замінює конфігураційне сховище HugsLib.
    /// </summary>
    public class OCSettings : ModSettings
    {
        public string LastIP = "";
        public string LastLoginName = "";
        public string LastPassword = "";
        public string LastCash = "";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref LastIP, "ocLastIP", "");
            Scribe_Values.Look(ref LastLoginName, "ocLastLoginName", "");
            Scribe_Values.Look(ref LastPassword, "ocLastPassword", "");
            Scribe_Values.Look(ref LastCash, "ocLastCash", "");
        }
    }
}