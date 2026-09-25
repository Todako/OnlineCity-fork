using RimWorld;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentPack : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var parms = GetParms();
            if (parms == null) return false;

            if (!IncidentDefOf.ManhunterPack.Worker.TryExecute(parms))
            {
                Messages.Message("OC_Incidents_FailedPack".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            return true;
        }

        private IncidentParms GetParms()
        {
            var target = GetTarget();
            if (target == null) return null;

            var incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, target);
            incidentParms.customLetterLabel = "OC_Incidents_Pack_Label".Translate();

            string text = "OC_Incidents_Pack_Text".Translate();
            if (!string.IsNullOrEmpty(attacker))
            {
                text += ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            }
            incidentParms.customLetterText = text;

            incidentParms.forced = true;
            incidentParms.target = target;
            incidentParms.points = CalculatePoints();

            this.parms = incidentParms;
            return incidentParms;
        }
    }
}