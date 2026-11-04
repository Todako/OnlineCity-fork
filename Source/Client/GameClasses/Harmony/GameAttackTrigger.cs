using HarmonyLib;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimWorldOnlineCity.GameClasses
{
    /// <summary>
    /// Ядро «Гібридного мосту» (Instanced Lockstep PvP).
    /// Забезпечує детерміноване виконання тіків, ізоляцію візуального RNG
    /// та синхронне виконання черги наказів на карті активного бою.
    /// </summary>
    public static class LockstepController
    {
        public static bool IsLockstepActive => GameAttackTrigger_Patch.HasActiveAttack;
        public static int BattleSeed = 12345;
        public static long LocalBattleTick = 0;
        public static long RemoteBattleTick = 0;
        public static DateTime LastRemoteTickUpdateUtc = DateTime.MinValue;
        public static bool RemoteReady = false;
        public static bool IsInBattleTick { get; private set; }

        public const long MaxTickLead = 30;
        public const int CommandTickDelay = 2;

        private static bool _randStatePushed = false;

        public static void Reset(int seed)
        {
            if (_randStatePushed)
            {
                try { Rand.PopState(); } catch { }
                _randStatePushed = false;
            }

            BattleSeed = seed != 0 ? seed : 987654321;
            LocalBattleTick = 0;
            RemoteBattleTick = 0;
            LastRemoteTickUpdateUtc = DateTime.UtcNow;
            RemoteReady = false;
            IsInBattleTick = false;
        }

        public static void UpdateRemoteTick(long remoteTick)
        {
            if (remoteTick >= 0)
            {
                if (remoteTick != RemoteBattleTick)
                {
                    LastRemoteTickUpdateUtc = DateTime.UtcNow;
                }
                if (remoteTick > RemoteBattleTick)
                {
                    RemoteBattleTick = remoteTick;
                }
            }
        }

        public static bool CanAdvanceTick()
        {
            if (!IsLockstepActive) return true;
            if (GameAttackTrigger_Patch.ForceSpeed <= 0f) return false;

            if (!RemoteReady) return false;

            if (LocalBattleTick - RemoteBattleTick > MaxTickLead)
            {
                if (GameAttackTrigger_Patch.ActiveAttackHost.Count > 0
                    && LastRemoteTickUpdateUtc != DateTime.MinValue
                    && (DateTime.UtcNow - LastRemoteTickUpdateUtc).TotalSeconds > 2.5)
                {
                    return true;
                }
                return false;
            }

            return true;
        }

        public static void OnMapPreTick(Map map)
        {
            if (!IsLockstepActive || map == null) return;
            bool isAttackerMap = GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var attacker);
            bool isHostMap = GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var host);
            if (!isAttackerMap && !isHostMap) return;

            LocalBattleTick++;
            int deterministicTickSeed = Gen.HashCombineInt(BattleSeed, (int)(LocalBattleTick & 0x7FFFFFFF));
            Rand.PushState(deterministicTickSeed);
            _randStatePushed = true;
            IsInBattleTick = true;

            if (isAttackerMap)
            {
                attacker.ExecuteScheduledCommandsForTick(LocalBattleTick);
            }
            if (isHostMap)
            {
                host.ExecuteScheduledCommandsForTick(LocalBattleTick);
            }
        }

        public static void OnMapPostTick(Map map)
        {
            IsInBattleTick = false;
            if (_randStatePushed)
            {
                _randStatePushed = false;
                Rand.PopState();
            }
        }
    }

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

    // ====================================================================================
    // 1. Патчі детермінізму Lockstep (DoSingleTick / MapPreTick / MapPostTick)
    // ====================================================================================

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch("DoSingleTick")]
    public static class TickManager_DoSingleTick_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return true;
            return LockstepController.CanAdvanceTick();
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch("MapPreTick")]
    public static class Map_MapPreTick_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Map __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;
            LockstepController.OnMapPreTick(__instance);
        }
    }

    [HarmonyPatch(typeof(Map))]
    [HarmonyPatch("MapPostTick")]
    public static class Map_MapPostTick_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;
            LockstepController.OnMapPostTick(__instance);
        }
    }

    // ====================================================================================
    // 1.1. Блокування ванільного виходу за край карти, авто-закриття Ambush та ШІ втечі
    // ====================================================================================

    [HarmonyPatch(typeof(Pawn))]
    [HarmonyPatch("ExitMap")]
    public static class Pawn_ExitMap_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return true;
            var map = __instance?.Map;
            if (map == null) return true;

            if (GameAttackTrigger_Patch.ActiveAttacker.ContainsKey(map)
                || GameAttackTrigger_Patch.ActiveAttackHost.ContainsKey(map))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MapParent))]
    [HarmonyPatch("CheckRemoveMapNow")]
    public static class MapParent_CheckRemoveMapNow_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(MapParent __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return true;
            var map = __instance?.Map;
            if (map != null && (GameAttackTrigger_Patch.ActiveAttacker.ContainsKey(map)
                || GameAttackTrigger_Patch.ActiveAttackHost.ContainsKey(map)))
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Блокує самовільний запуск ванільного ШІ піратів (JobGiver_ExitMapBest -> Goto (199,0,0) тощо)
    /// для пішаків, які синхронізуються через мережу.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker))]
    [HarmonyPatch("DetermineNextJob")]
    public static class Pawn_JobTracker_DetermineNextJob_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_JobTracker __instance, ref ThinkResult __result, out ThinkTreeDef thinkTree)
        {
            thinkTree = null;
            if (!GameAttackTrigger_Patch.HasActiveAttack) return true;
            if (Scribe.mode != LoadSaveMode.Inactive || GameXMLUtils.FromXmlIsActive) return true;

            var pawn = __instance.GetPawn();
            var map = pawn?.Map;
            if (map == null || pawn.Dead || pawn.Downed || pawn.InMentalState) return true;

            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var host))
            {
                if (host.AttackingPawns != null && host.AttackingPawns.Contains(pawn))
                {
                    var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                    waitJob.expiryInterval = int.MaxValue;
                    waitJob.checkOverrideOnExpire = false;
                    __result = new ThinkResult(waitJob, null);
                    return false;
                }
            }
            else if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var attacker))
            {
                if (!attacker.IsMyPawn(pawn) || (pawn.drafter != null && pawn.drafter.Drafted))
                {
                    var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                    waitJob.expiryInterval = int.MaxValue;
                    waitJob.checkOverrideOnExpire = false;
                    __result = new ThinkResult(waitJob, null);
                    return false;
                }
            }

            return true;
        }
    }

    // ====================================================================================
    // 2. Ізоляція клієнтських звуків, візуальних ефектів та Scribe-десеріалізації (1.1–1.4)
    // ====================================================================================

    [HarmonyPatch]
    public static class SoundStarter_PlayOneShot_Patch
    {
        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SoundStarter), "PlayOneShot");
        }

        [HarmonyPrefix]
        public static void Prefix(out bool __state)
        {
            if (LockstepController.IsInBattleTick)
            {
                Rand.PushState();
                __state = true;
            }
            else
            {
                __state = false;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }

    [HarmonyPatch]
    public static class MoteMaker_MakeStaticMote_Patch
    {
        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            var methods = AccessTools.GetDeclaredMethods(typeof(MoteMaker));
            for (int i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.Name == "MakeStaticMote")
                {
                    var pars = m.GetParameters();
                    if (pars.Length >= 3 && pars[0].ParameterType == typeof(Vector3))
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        [HarmonyPrefix]
        public static void Prefix(out bool __state)
        {
            if (LockstepController.IsInBattleTick)
            {
                Rand.PushState();
                __state = true;
            }
            else
            {
                __state = false;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }

    [HarmonyPatch]
    public static class LifeStageWorker_Notify_LifeStageStarted_Patch
    {
        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("RimWorld.LifeStageWorker_HumanlikeAdult")
                ?? AccessTools.TypeByName("RimWorld.LifeStageWorker");
            return t != null ? AccessTools.Method(t, "Notify_LifeStageStarted") : null;
        }

        [HarmonyPrefix]
        public static bool Prefix()
        {
            return Scribe.mode == LoadSaveMode.Inactive;
        }
    }

    [HarmonyPatch]
    public static class Hediff_Addiction_Need_Patch
    {
        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Hediff_Addiction), "Need");
        }

        [HarmonyPrefix]
        public static bool Prefix(Hediff_Addiction __instance, ref Need_Chemical __result)
        {
            if (Scribe.mode != LoadSaveMode.Inactive
                || GameXMLUtils.FromXmlIsActive
                || __instance?.pawn?.needs?.AllNeeds == null)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class Pawn_NeedsTracker_AddOrRemoveNeedsAsAppropriate_Patch
    {
        private static readonly AccessTools.FieldRef<Pawn_NeedsTracker, Pawn> NeedsPawnRef =
            AccessTools.FieldRefAccess<Pawn_NeedsTracker, Pawn>("pawn");

        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Pawn_NeedsTracker), "AddOrRemoveNeedsAsAppropriate");
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn_NeedsTracker __instance)
        {
            if (__instance != null)
            {
                var pawn = NeedsPawnRef(__instance);
                if (pawn != null && pawn.needs == null)
                {
                    pawn.needs = __instance;
                }
            }
            return Scribe.mode != LoadSaveMode.LoadingVars;
        }
    }

    [HarmonyPatch]
    public static class Pawn_JobTracker_Notify_WorkTypeDisabled_Patch
    {
        public static bool Prepare() => TargetMethod() != null;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Pawn_JobTracker), "Notify_WorkTypeDisabled");
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn_JobTracker __instance)
        {
            if (Scribe.mode != LoadSaveMode.Inactive || GameXMLUtils.FromXmlIsActive)
            {
                return false;
            }
            if (__instance != null && __instance.jobQueue == null)
            {
                __instance.jobQueue = new JobQueue();
            }
            return true;
        }
    }

    // ====================================================================================
    // 3. Розмежування прав керування пішаками (Faction Authority)
    // ====================================================================================

    [HarmonyPatch(typeof(FloatMenuMakerMap))]
    [HarmonyPatch("CanTakeOrder")]
    public static class FloatMenuMakerMap_CanTakeOrder_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result || !GameAttackTrigger_Patch.HasActiveAttack || pawn?.Map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(pawn.Map, out var attacker))
            {
                if (!attacker.IsMyPawn(pawn))
                {
                    __result = false;
                }
            }
            else if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(pawn.Map, out var host))
            {
                if (host.AttackingPawns != null && host.AttackingPawns.Contains(pawn))
                {
                    __result = false;
                }
            }
        }
    }

    // ====================================================================================
    // 4. Перехоплення вхідних наказів гравця (Drafted, StartJob, InventoryDrop)
    // ====================================================================================

    [HarmonyPatch(typeof(Pawn_DraftController))]
    [HarmonyPatch("Drafted", MethodType.Setter)]
    public static class Pawn_DraftController_Drafted_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_DraftController __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;
            if (Scribe.mode != LoadSaveMode.Inactive || GameXMLUtils.FromXmlIsActive) return;

            var pawn = __instance?.pawn;
            var map = pawn?.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var client))
            {
                client.UIEventDraftChanged(pawn, __instance.Drafted);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                clientHost.UIEventDraftChanged(pawn, __instance.Drafted);
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
            if (Scribe.mode != LoadSaveMode.Inactive || GameXMLUtils.FromXmlIsActive) return;

            var pawn = __instance.GetPawn();
            var map = pawn?.Map;
            if (map == null) return;

            var curJob = __instance.curJob;
            if (curJob == null) return;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(map, out var client))
            {
                if (!client.ApplyingRemoteCommand)
                {
                    client.UIEventNewJob(pawn, curJob);
                }
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
                if (!clientHost.UIEventNewJobDisable)
                {
                    clientHost.UIEventNewJob(pawn, curJob);
                }
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
            if (!GameAttackTrigger_Patch.HasActiveAttack) return true;

            var pawn = (t.ParentHolder as Pawn_InventoryTracker)?.pawn;
            if (pawn?.Map == null) return true;
            if (!pawn.inventory.innerContainer.Contains(t)) return true;

            if (GameAttackTrigger_Patch.ActiveAttacker.TryGetValue(pawn.Map, out var client))
            {
                return client.UIEventInventoryDrop(t);
            }
            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(pawn.Map, out var clientHost))
            {
                return clientHost.UIEventInventoryDrop(t);
            }
            return true;
        }
    }

    // ====================================================================================
    // 5. Відстеження життєвого циклу об'єктів на карті та швидкості часу
    // ====================================================================================

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

    [HarmonyPatch(typeof(TickManager))]
    [HarmonyPatch("TickRateMultiplier", MethodType.Getter)]
    public static class TickManager_TickRateMultiplier
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack || GameAttackTrigger_Patch.ForceSpeed < 0f) return;
            __result = GameAttackTrigger_Patch.ForceSpeed;
        }
    }

    [HarmonyPatch(typeof(Thing))]
    [HarmonyPatch("Position", MethodType.Setter)]
    public static class Thing_Position_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            if (!GameAttackTrigger_Patch.HasActiveAttack) return;
            if (!(__instance is Pawn pawn)) return;

            var map = pawn.Map;
            if (map == null) return;

            if (GameAttackTrigger_Patch.ActiveAttackHost.TryGetValue(map, out var clientHost))
            {
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
                            pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
                        }
                        catch (Exception e)
                        {
                            Loger.Log("Client Thing_Position_Patch Exception1: " + e.ToString());
                        }
                        finally
                        {
                            clientHost.UIEventNewJobDisable = false;
                        }
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