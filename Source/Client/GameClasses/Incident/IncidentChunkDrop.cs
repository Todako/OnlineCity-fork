using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldOnlineCity
{
    public class IncidentChunkDrop : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            var target = GetTarget();
            if (target == null) return false;

            parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, target);

            string label = "OC_Incidents_ChunkDrop_Label".Translate();
            if (label == "OC_Incidents_ChunkDrop_Label") label = "Chunk drop";
            parms.customLetterLabel = label;

            string text = "OC_Incidents_ChunkDrop_Text".Translate();
            if (!string.IsNullOrEmpty(attacker))
            {
                text += ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            }
            parms.customLetterText = text;

            parms.faction = null;
            parms.forced = true;
            parms.target = target;
            parms.points = CalculatePoints();

            if (!IncidentDefOf.ShipChunkDrop.Worker.TryExecute(parms))
            {
                Messages.Message("OC_Incidents_FailedChunkDrop".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }
            return true;
        }
    }
}