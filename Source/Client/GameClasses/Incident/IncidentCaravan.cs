using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentCaravan : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var target = GetTarget();
            if (target == null) return false;

            parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, target);

            string label = "OC_Incidents_Caravan_Label".Translate();
            if (label == "OC_Incidents_Caravan_Label") label = "Trade Caravan";
            parms.customLetterLabel = label;

            string text = "OC_Incidents_Caravan_Text".Translate();
            if (!string.IsNullOrEmpty(attacker))
            {
                text += ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            }
            parms.customLetterText = text;

            parms.faction = null;
            parms.forced = true;
            parms.target = target;
            parms.points = CalculatePoints();

            if (!IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms))
            {
                Messages.Message("OC_Incidents_FailedCaravan".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            return true;
        }
    }
}