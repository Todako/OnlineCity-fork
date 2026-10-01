using HarmonyLib;
using OCUnion;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Verse;
using Verse.AI;

namespace RimWorldOnlineCity.GameClasses
{
    public static class GameAttackTrigger_Patch
    {
        public static float ForceSpeed = -1f;
        public static readonly Dictionary<Map, GameAttacker> ActiveAttacker = new Dictionary<Map, GameAttacker>();
        public static readonly Dictionary<Map, GameAttackHost> ActiveAttackHost = new Dictionary<Map, GameAttackHost>();

        private static readonly Func<Pawn_JobTracker, Pawn> GetPawnFromPawn_JobTracker;

        static GameAttackTrigger_Patch()
        {
            var field = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
            if (field != null)
            {
                var keeperArg = Expression.Parameter(typeof(Pawn_JobTracker), "keeper");
                var secretAccessor = Expression.Field(keeperArg, field);
                GetPawnFromPawn_JobTracker = Expression.Lambda<Func<Pawn_JobTracker, Pawn>>(secretAccessor, keeperArg).Compile();
            }
        }

        public static Pawn GetPawn(this Pawn_JobTracker keeper)
        {
            return GetPawnFromPawn_JobTracker != null ? GetPawnFromPawn_JobTracker(keeper) : null;
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Destroy")]
    public static class Thing_Destroy_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            if (GameAttackTrigger_Patch.ActiveAttacker.Count == 0
                && GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;

            if (__instance is Projectile || __instance is Mote || __instance is Filth || __instance is Explosion) return;

            var map = __instance.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var client))
            {
                client.UIEventChange(__instance, true);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                clientHost.UIEventChange(__instance, true);
            }
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("DeSpawn")]
    public static class Thing_DeSpawn_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            if (GameAttackTrigger_Patch.ActiveAttacker.Count == 0
                && GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;

            if (__instance is Pawn || __instance is Corpse || __instance is Projectile
                || __instance is Mote || __instance is Filth || __instance is Explosion) return;

            var map = __instance.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var client))
            {
                client.UIEventChange(__instance, true);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                clientHost.UIEventChange(__instance, true);
            }
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("PostApplyDamage")]
    public static class Thing_PostApplyDamage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;
            // Усунення дублювання: об'єкти з компонентами обробляються окремим патчем ThingWithComps
            if (__instance is ThingWithComps) return;
            if (__instance is Projectile || __instance is Mote || __instance is Filth || __instance is Plant || __instance is Explosion) return;

            var map = __instance.Map;
            if (map != null && GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var client))
            {
                client.UIEventChange(__instance, false);
            }
        }
    }

    [HarmonyPatch(typeof(ThingWithComps))]
    [HarmonyPatch("PostApplyDamage")]
    public static class ThingWithComps_PostApplyDamage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;
            if (__instance is Projectile || __instance is Mote || __instance is Filth || __instance is Plant || __instance is Explosion) return;

            var map = __instance.Map;
            if (map != null && GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var client))
            {
                client.UIEventChange(__instance, false);
            }
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("SpawnSetup")]
    public static class Thing_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance, Map map, bool respawningAfterLoad)
        {
            if (GameAttackTrigger_Patch.ActiveAttacker.Count == 0
                && GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;

            if (__instance is Pawn || __instance is Projectile || __instance is Mote || __instance is Filth || __instance is Explosion) return;

            var targetMap = __instance.Map ?? map;
            if (targetMap == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(targetMap, out var client))
            {
                client.UIEventChange(__instance, false, true);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(targetMap, out var clientHost))
            {
                clientHost.UIEventChange(__instance, false, true);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch("StartJob")]
    public static class Pawn_JobTracker_StartJob_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_JobTracker __instance)
        {
            if (GameAttackTrigger_Patch.ActiveAttacker.Count == 0
                && GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;

            var pawn = __instance.GetPawn();
            var map = pawn?.Map;
            if (map == null) return;

            var curJob = __instance.curJob;
            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var client))
            {
                client.UIEventNewJob(pawn, curJob);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                clientHost.UIEventNewJob(pawn, curJob);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch("CleanupCurrentJob")]
    public static class Pawn_JobTracker_CleanupCurrentJob_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_JobTracker __instance)
        {
            if (GameAttackTrigger_Patch.ActiveAttacker.Count == 0
                && GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;

            var pawn = __instance.GetPawn();
            var map = pawn?.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var client))
            {
                client.UIEventNewJob(pawn, null);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                clientHost.UIEventNewJob(pawn, null);
            }
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Gear))]
    [HarmonyPatch("InterfaceDrop")]
    public static class ITab_Pawn_Gear_InterfaceDrop_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ITab_Pawn_Gear __instance, Thing t)
        {
            if (GameAttackTrigger_Patch.ActiveAttacker.Count == 0) return true;

            var pawn = (t.ParentHolder as Pawn_InventoryTracker)?.pawn;
            if (pawn?.Map == null) return true;

            // Швидка перевірка наявності без алокації LINQ
            if (!pawn.inventory.innerContainer.Contains(t)) return true;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(pawn.Map, out var client))
            {
                return client.UIEventInventoryDrop(t);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch("TickRateMultiplier", MethodType.Getter)]
    public static class TickManager_TickRateMultiplier
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            if (GameAttackTrigger_Patch.ForceSpeed < 0f) return;
            __result = GameAttackTrigger_Patch.ForceSpeed;
        }
    }

    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch("TicksPerMove")]
    public static class Pawn_TicksPerMove
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref int __result)
        {
            if (GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;

            var map = __instance.Map;
            if (map != null && GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                clientHost.ControlPawnMoveSpeed(__instance, ref __result);
            }
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Position", MethodType.Setter)]
    public static class Thing_Position_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            // ОПТИМІЗАЦІЯ: ранній вихід за відсутності атаки знімає навантаження з усіх рухів на карті
            if (GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;
            if (!(__instance is Pawn pawn)) return;

            var map = pawn.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                if (clientHost.AttackingPawns == null || !clientHost.AttackingPawns.Contains(pawn)) return;

                const int mapBorder = 1;
                if (pawn.Position.x < mapBorder || pawn.Position.x > map.Size.x - 1 - mapBorder
                    || pawn.Position.z < mapBorder || pawn.Position.z > map.Size.z - 1 - mapBorder)
                {
                    if (clientHost.AttackingPawnsLastPos.TryGetValue(pawn, out var resPos))
                    {
                        try
                        {
                            clientHost.UIEventNewJobDisable = true;
                            pawn.Position = resPos;
                            pawn.Notify_Teleported(true, true);
                        }
                        catch (Exception e)
                        {
                            Loger.Log("Client Thing_Position_Patch Exception1: " + e.ToString());
                        }
                        clientHost.UIEventNewJobDisable = false;

                        LongEventHandler.QueueLongEvent(delegate
                        {
                            try
                            {
                                clientHost.AttackingPawnJobDic.Remove(pawn.thingIDNumber);
                                clientHost.UIEventNewJobDisable = true;
                                pawn.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                                {
                                    playerForced = true,
                                    expiryInterval = int.MaxValue,
                                    checkOverrideOnExpire = false,
                                }, JobCondition.InterruptForced);
                            }
                            catch (Exception e)
                            {
                                Loger.Log("Client Thing_Position_Patch Exception2: " + e.ToString());
                            }
                            clientHost.UIEventNewJobDisable = false;
                        }, "", false, null);
                    }
                }
                else
                {
                    clientHost.AttackingPawnsLastPos[pawn] = pawn.Position;
                }
            }
        }
    }
}