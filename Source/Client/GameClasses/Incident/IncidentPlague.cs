using RimWorld;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentPlague : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail("Disease_Plague")
                   ?? DefDatabase<IncidentDef>.GetNamedSilentFail("Plague");

            if (def?.Worker == null) return false;

            var parms = GetParms();
            if (parms == null) return false;

            if (!def.Worker.TryExecute(parms))
            {
                Messages.Message("OC_Incidents_FailedPlague".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            return true;
        }

        private IncidentParms GetParms()
        {
            var target = GetTarget();
            if (target == null) return null;

            var incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatSmall, target);

            string label = "OC_Incidents_Plague_Label".Translate();
            if (label == "OC_Incidents_Plague_Label") label = "Epidemic!";
            incidentParms.customLetterLabel = label;

            incidentParms.customLetterText = "OC_Incidents_Plague_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            incidentParms.forced = true;
            incidentParms.target = target;
            incidentParms.points = CalculatePoints();

            this.parms = incidentParms;
            return incidentParms;
        }
    }
}