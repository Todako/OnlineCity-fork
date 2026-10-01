using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    class IncidentAcid : OCIncident
    {
        public override bool TryExecuteEvent()
        {
            Map map = GetTarget();
            if (map == null) return false;

            int duration = Mathf.RoundToInt(2 * hour * mult);

            // ВИПРАВЛЕННЯ: створюємо стан безпосередньо без небезпечної мутації глобального GameConditionDefOf.ToxicFallout
            var acid = new OC_GameCondition_Acid
            {
                def = GameConditionDefOf.ToxicFallout,
                Duration = duration,
                startTick = Find.TickManager.TicksGame,
                PlantKillChance = 0.5f,
                ToxicPerDay = 10f,
                CorpseRotProgressAdd = 5000f,
                CheckInterval = 1250
            };

            string label = "OC_Incidents_Acid_Label".Translate();
            if (label == "OC_Incidents_Acid_Label") label = "Acid";

            string text = "OC_Incidents_Acid_Text".Translate() + ". " + "OC_Incident_Atacker".Translate() + " " + attacker;
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NegativeEvent);
            map.gameConditionManager.RegisterCondition(acid);

            if (map.weatherManager.curWeather.rainRate > 0.1f)
            {
                map.weatherDecider.StartNextWeather();
            }
            return true;
        }
    }

    public class OC_GameCondition_Acid : GameCondition
    {
        private const float MaxSkyLerpFactor = 0.5f;
        private const float SkyGlow = 0.85f;

        private readonly SkyColorSet ToxicFalloutColors = new SkyColorSet(
            new ColorInt(216, 255, 0).ToColor,
            new ColorInt(234, 200, 255).ToColor,
            new Color(0.6f, 0.8f, 0.5f),
            SkyGlow);

        private readonly List<SkyOverlay> overlays = new List<SkyOverlay>
        {
            new WeatherOverlay_Fallout()
        };

        public int CheckInterval = 3451;
        public float ToxicPerDay = 0.5f;
        public float PlantKillChance = 0.0065f;
        public float CorpseRotProgressAdd = 3000f;

        public override int TransitionTicks => 5000;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref CheckInterval, "CheckInterval", 3451);
            Scribe_Values.Look(ref ToxicPerDay, "ToxicPerDay", 0.5f);
            Scribe_Values.Look(ref PlantKillChance, "PlantKillChance", 0.0065f);
            Scribe_Values.Look(ref CorpseRotProgressAdd, "CorpseRotProgressAdd", 3000f);
        }

        public override void Init()
        {
            LessonAutoActivator.TeachOpportunity(ConceptDefOf.ForbiddingDoors, OpportunityType.Critical);
            LessonAutoActivator.TeachOpportunity(ConceptDefOf.AllowedAreas, OpportunityType.Critical);
        }

        public override void GameConditionTick()
        {
            List<Map> affectedMaps = base.AffectedMaps;
            if (Find.TickManager.TicksGame % CheckInterval == 0)
            {
                for (int i = 0; i < affectedMaps.Count; i++)
                {
                    DoPawnsToxicDamage(affectedMaps[i]);
                }
            }
            for (int j = 0; j < overlays.Count; j++)
            {
                for (int k = 0; k < affectedMaps.Count; k++)
                {
                    overlays[j].TickOverlay(affectedMaps[k]);
                }
            }
        }

        private void DoPawnsToxicDamage(Map map)
        {
            List<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawnsSpawned.Count; i++)
            {
                DoPawnToxicDamage(allPawnsSpawned[i]);
            }
        }

        public static void DoPawnToxicDamage(Pawn p)
        {
            // ВИПРАВЛЕННЯ: пішак повинен бути саме Spawned на карті
            if (p != null && p.Spawned && p.Position.UsesOutdoorTemperature(p.Map))
            {
                if (!p.RaceProps.IsFlesh)
                {
                    p.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 1f));
                }
                else
                {
                    float num = 0.0230066683f;
                    num *= Mathf.Max(1f - p.GetStatValue(StatDefOf.ToxicResistance), 0f);
                    if (ModsConfig.BiotechActive)
                    {
                        num *= Mathf.Max(1f - p.GetStatValue(StatDefOf.ToxicEnvironmentResistance), 0f);
                    }

                    float num2 = Mathf.Lerp(0.85f, 1.15f, Rand.ValueSeeded(p.thingIDNumber ^ 0x46EDC5D));
                    num *= num2;
                    if (num != 0f)
                    {
                        HealthUtility.AdjustSeverity(p, HediffDefOf.ToxicBuildup, num);
                    }
                }
            }
        }

        public override void DoCellSteadyEffects(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return;
            if (c.Roofed(map) && !isEdgeWall(c, map))
            {
                return;
            }

            List<Thing> thingList = c.GetThingList(map);
            // ВИПРАВЛЕННЯ: зворотна ітерація для безпечного видалення
            for (int i = thingList.Count - 1; i >= 0; i--)
            {
                if (i >= thingList.Count) continue;
                Thing thing = thingList[i];
                if (thing is Plant)
                {
                    if (thing.def.plant.dieFromToxicFallout && Rand.Value < PlantKillChance)
                    {
                        thing.Kill();
                    }
                }
                else if (thing.def.category == ThingCategory.Item)
                {
                    try
                    {
                        CompRottable compRottable = thing.TryGetComp<CompRottable>();
                        if (compRottable != null && (int)compRottable.Stage < 2)
                        {
                            compRottable.RotProgress += CorpseRotProgressAdd;
                        }
                    }
                    catch
                    {
                        thing.TakeDamage(new DamageInfo(DamageDefOf.Burn, 1f));
                    }
                }
                else if (GameUtils.isBuilding(thing) && isEdgeWall(c, map))
                {
                    thing.TakeDamage(new DamageInfo(DamageDefOf.Burn, 1f));
                }
            }
        }

        // ВИПРАВЛЕННЯ: виправлено критичну помилку tmp.y замість tmp.z (перевірка 2D-сітки на південь і північ)
        public static bool isEdgeWall(IntVec3 c, Map map)
        {
            if (!c.Roofed(map)) return true;

            IntVec3 tmp = c;
            tmp.x++;
            if (tmp.InBounds(map) && tmp.UsesOutdoorTemperature(map)) return true;
            tmp.x -= 2;
            if (tmp.InBounds(map) && tmp.UsesOutdoorTemperature(map)) return true;
            tmp.x = c.x;
            tmp.z++;
            if (tmp.InBounds(map) && tmp.UsesOutdoorTemperature(map)) return true;
            tmp.z -= 2;
            if (tmp.InBounds(map) && tmp.UsesOutdoorTemperature(map)) return true;
            return false;
        }

        public override void GameConditionDraw(Map map)
        {
            for (int i = 0; i < overlays.Count; i++)
            {
                overlays[i].DrawOverlay(map);
            }
        }

        public override float SkyTargetLerpFactor(Map map)
        {
            return GameConditionUtility.LerpInOutValue(this, TransitionTicks, ToxicPerDay);
        }

        public override SkyTarget? SkyTarget(Map map)
        {
            return new SkyTarget(0.85f, ToxicFalloutColors, 1f, 1f);
        }

        public override float AnimalDensityFactor(Map map) => 0f;

        public override float PlantDensityFactor(Map map) => 0f;

        public override bool AllowEnjoyableOutsideNow(Map map) => false;

        public override List<SkyOverlay> SkyOverlays(Map map) => overlays;
    }
}