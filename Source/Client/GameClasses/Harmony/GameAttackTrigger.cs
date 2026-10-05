using HarmonyLib;
using OCUnion;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RimWorldOnlineCity.GameClasses
{
    public static class GameAttackTrigger_Patch
    {
        public static float ForceSpeed = -1f;
        public static readonly Dictionary<Map, GameAttacker> ActiveAttacker = new Dictionary<Map, GameAttacker>();
        public static readonly Dictionary<Map, GameAttackHost> ActiveAttackHost = new Dictionary<Map, GameAttackHost>();

        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static Pawn GetPawn(this Pawn_JobTracker keeper)
        {
            return keeper != null ? PawnRef(keeper) : null;
        }

        public static bool HasActiveAttack => ActiveAttacker.Count > 0 || ActiveAttackHost.Count > 0;
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Destroy")]
    public static class Thing_Destroy_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;
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
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;

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
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;
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
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;

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
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;

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
            if (GameAttackTrigger_Patch.ActiveAttackHost.Count == 0) return;
            if (!(__instance is Pawn pawn)) return;

            var map = pawn.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                // Захист від повторного виклику під час програмної зміни позиції
                if (clientHost.UIEventNewJobDisable) return;
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
                        finally
                        {
                            clientHost.UIEventNewJobDisable = false;
                        }

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
                            finally
                            {
                                clientHost.UIEventNewJobDisable = false;
                            }
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