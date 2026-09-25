using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentPsychoDrone : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            Map map = GetTarget();
            if (map == null) return false;

            int duration = Mathf.RoundToInt((day * mult) / 2);

            // ВИПРАВЛЕННЯ: у ванільному RimWorld стан зветься GameCondition_PsychicEmanation
            var drone = (GameCondition_PsychicEmanation)GameConditionMaker.MakeCondition(GameConditionDefOf.PsychicDrone, duration);

            // Визначаємо стать: з параметрів команди або випадково
            if (incidentParams != null && incidentParams.Count > 0 && incidentParams[0].Equals("female", StringComparison.OrdinalIgnoreCase))
            {
                drone.gender = Gender.Female;
            }
            else if (incidentParams != null && incidentParams.Count > 0 && incidentParams[0].Equals("male", StringComparison.OrdinalIgnoreCase))
            {
                drone.gender = Gender.Male;
            }
            else
            {
                drone.gender = Rand.Bool ? Gender.Male : Gender.Female;
            }

            // Рівень занепаду настрою залежно від сили інциденту
            drone.level = mult >= 4 ? PsychicDroneLevel.BadExtreme
                : mult >= 3 ? PsychicDroneLevel.BadHigh
                : mult >= 2 ? PsychicDroneLevel.BadMedium
                : PsychicDroneLevel.BadLow;

            string label = "OC_Incidents_PsychoDrone_Label".Translate();
            if (label == "OC_Incidents_PsychoDrone_Label") label = "Psychic Emanation";

            string text = "OC_Incidents_PsychoDrone_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent);
            map.gameConditionManager.RegisterCondition(drone);

            return true;
        }
    }

    // Залишено для зворотної сумісності з попередніми збереженнями
    public class OC_GameCondition_PsychoDrone : GameCondition_PsychicEmanation
    {
    }
}