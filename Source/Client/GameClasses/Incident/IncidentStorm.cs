using RimWorld;
using RimWorld.Planet;
using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldOnlineCity
{
    class IncidentStorm : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            Map map = GetTarget();
            if (map == null) return false;

            int duration = Mathf.RoundToInt(hour * mult);

            // ВИПРАВЛЕННЯ: безпечне створення стану з ванільним Def (запобігає крашу Scribe при збереженні гри)
            var storm = new OC_GameCondition_Storm
            {
                def = GameConditionDefOf.Flashstorm,
                Duration = duration,
                startTick = Find.TickManager.TicksGame,
                cooldown = 50
            };

            string label = "OC_Incidents_Storm_Label".Translate();
            if (label == "OC_Incidents_Storm_Label") label = "Іонний шторм";

            string text = "OC_Incident_Atacker".Translate() + " " + attacker;
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent);
            map.gameConditionManager.RegisterCondition(storm);

            if (map.weatherManager.curWeather.rainRate > 0.1f)
            {
                map.weatherDecider.StartNextWeather();
            }
            return true;
        }
    }

    class OC_GameCondition_Storm : GameCondition
    {
        private static readonly IntRange AreaRadiusRange = new IntRange(45, 60);
        public IntVec2 centerLocation = IntVec2.Invalid;
        public IntRange areaRadiusOverride = IntRange.zero;
        public IntRange initialStrikeDelay = IntRange.zero;
        public int cooldown = 50;
        public bool ambientSound;

        private int areaRadius;
        private int nextLightningTicks;
        private Sustainer soundSustainer;

        public int AreaRadius => areaRadius;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref centerLocation, "centerLocation");
            Scribe_Values.Look(ref areaRadius, "areaRadius", 0);
            Scribe_Values.Look(ref areaRadiusOverride, "areaRadiusOverride");
            Scribe_Values.Look(ref nextLightningTicks, "nextLightningTicks", 0);
            Scribe_Values.Look(ref initialStrikeDelay, "initialStrikeDelay");
            Scribe_Values.Look(ref ambientSound, "ambientSound", defaultValue: false);
        }

        public override void Init()
        {
            base.Init();
            areaRadius = (areaRadiusOverride == IntRange.zero) ? AreaRadiusRange.RandomInRange : areaRadiusOverride.RandomInRange;
            nextLightningTicks = Find.TickManager.TicksGame + initialStrikeDelay.RandomInRange;
            if (centerLocation.IsInvalid)
            {
                FindGoodCenterLocation();
            }
        }

        /// <summary>
        /// ОПТИМІЗАЦІЯ: гарантоване оновлення nextLightningTicks запобігає спайкам TPS, коли клітинка під дахом.
        /// </summary>
        public override void GameConditionTick()
        {
            if (Find.TickManager.TicksGame > nextLightningTicks)
            {
                var map = base.SingleMap;
                if (map != null)
                {
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        Vector2 vector = Rand.UnitVector2 * Rand.Range(0f, areaRadius);
                        IntVec3 intVec = new IntVec3((int)Math.Round(vector.x) + centerLocation.x, 0, (int)Math.Round(vector.y) + centerLocation.z);
                        if (IsGoodLocationForStrike(intVec, map))
                        {
                            map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, intVec));
                            break;
                        }
                    }
                }
                nextLightningTicks = Find.TickManager.TicksGame + cooldown;
            }

            if (ambientSound)
            {
                if (soundSustainer == null || soundSustainer.Ended)
                {
                    soundSustainer = SoundDefOf.FlashstormAmbience.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(centerLocation.ToIntVec3, base.SingleMap), MaintenanceType.PerTick));
                }
                else
                {
                    soundSustainer.Maintain();
                }
            }
        }

        public override void End()
        {
            base.SingleMap?.weatherDecider?.DisableRainFor(30000);
            base.End();
        }

        private void FindGoodCenterLocation()
        {
            var map = base.SingleMap;
            if (map == null || map.Size.x <= 16 || map.Size.z <= 16)
            {
                centerLocation = new IntVec2(map?.Size.x / 2 ?? 100, map?.Size.z / 2 ?? 100);
                return;
            }

            for (int i = 0; i < 10; i++)
            {
                var candidate = new IntVec2(Rand.Range(8, map.Size.x - 8), Rand.Range(8, map.Size.z - 8));
                if (IsGoodLocationForStrike(new IntVec3(candidate.x, 0, candidate.z), map))
                {
                    centerLocation = candidate;
                    return;
                }
            }
            centerLocation = new IntVec2(map.Size.x / 2, map.Size.z / 2);
        }

        private static bool IsGoodLocationForStrike(IntVec3 loc, Map map)
        {
            return loc.InBounds(map) && !loc.Roofed(map) && loc.Standable(map);
        }
    }
}