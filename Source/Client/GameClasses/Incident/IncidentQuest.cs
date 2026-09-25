using RimWorld;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentQuest : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var target = GetTarget();
            if (target == null) return false;

            parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, target);
            parms.customLetterLabel = "OC_Incidents_Quest_Label".Translate();
            parms.customLetterText = "OC_Incidents_Quest_Text".Translate();
            parms.faction = null;
            parms.forced = true;
            parms.target = target;
            parms.points = CalculatePoints();

            if (!IncidentDefOf.GiveQuest_Random.Worker.TryExecute(parms))
            {
                Messages.Message("OC_Incidents_FailedQuest".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            return true;
        }
    }
}