using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses;
using System;
using System.Collections.Generic;
using System.Threading;
using Verse;
using Verse.AI;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер сторони нападника в режимі Instanced Lockstep PvP («Гібридний міст»).
    /// Підтримує атаки як на поселення (BaseOnline), так і на каравани (CaravanOnline).
    /// </summary>
    public class GameAttacker
    {
        public int AttackUpdateDelay { get; } = 50;
        public int TimeStopBeforeAttack { get; } = 15;
        public int MapBorder { get; } = 10;

        private static readonly TimeSpan DefaultPauseTimeToHost = new TimeSpan(0, 0, 15);

        private static readonly HashSet<string> AllowedGenSteps = new HashSet<string>(StringComparer.Ordinal)
        {
            "ElevationFertility",
            "Caves",
            "Terrain",
            "CavesTerrain",
            "FindPlayerStartSpot",
            "ScenParts",
            "Fog"
        };

        public static bool CanStart => SessionClientController.Data.AttackModule == null;
        public static GameAttacker Get => SessionClientController.Data.AttackModule;

        public bool TestMode { get; set; }
        public bool IsCaravanTarget { get; private set; }
        public DateTime PreparationEndTimeUtc { get; private set; }
        public bool AttackerReady { get; private set; }
        private bool _prepDialogShown;
        private bool _pauseTimeSentToHost;

        public int AttackUpdateTick { get; set; }
        private object TimerObj;
        private bool InTimer { get; set; }
        private Map GameMap { get; set; }

        private Dictionary<int, int> ThingsIDDicRev { get; set; }
        private Dictionary<int, int> ThingsIDDic { get; set; }
        private Dictionary<int, Thing> ThingsObjDic { get; set; }
        private Dictionary<Pawn, int> AttackerPawns { get; set; }
        private List<string> AttackerOriginalPawnLabels { get; set; }
        private HashSet<int> AttackerOriginalPawnIds { get; set; }

        // Вікно інерції після руху для захисту від відкидання назад запізнілими пакетами UpdateState
        private readonly Dictionary<int, long> _ignorePosSyncUntilTick = new Dictionary<int, long>(32);

        private readonly List<AttackPawnCommand> OutgoingCommands = new List<AttackPawnCommand>(32);
        private readonly List<AttackPawnCommand> ScheduledHostCommands = new List<AttackPawnCommand>(32);
        private readonly object CommandSync = new object();

        // Багаторазові буфери для 50-мс циклу
        private readonly List<AttackPawnCommand> _commandsBuffer = new List<AttackPawnCommand>(32);
        private readonly List<int> _needNewThingsBuffer = new List<int>(16);
        private readonly List<ThingEntry> _corpseListBuffer = new List<ThingEntry>(16);

        private Dictionary<int, Thing> CheckDestroy { get; set; }
        private Dictionary<int, Thing> CheckSpawn { get; set; }

        private bool ThingDropIgnore { get; set; }
        public bool ApplyingRemoteCommand { get; private set; }
        private bool CheckSpawnDestroyDisable { get; set; }
        private readonly object CheckSpawnDestroySync = new object();

        public bool VictoryHostToHost { get; set; }
        private DateTime _victoryHostSentTimeUtc = DateTime.MinValue;
        public bool TerribleFatalError { get; set; }

        public bool IsMyPawn(Pawn pawn)
        {
            return pawn != null && AttackerPawns != null && AttackerPawns.ContainsKey(pawn);
        }

        public static bool Create()
        {
            if (!CanStart) return false;
            SessionClientController.Data.AttackModule = new GameAttacker();
            SessionClientController.Data.BackgroundSaveGameOff = true;
            return true;
        }

        private GameAttacker()
        {
        }

        public void Start(Caravan caravan, BaseOnline attackedBase, bool testMode)
        {
            Start(caravan, (CaravanOnline)attackedBase, testMode);
        }

        public void Start(Caravan caravan, CaravanOnline attackedTarget, bool testMode)
        {
            ModBaseData.RunMainThreadSync(() => Find.TickManager?.Pause());
            SessionClientController.Data.DontCheckTimerFail = true;
            SessionClientController.SaveGameNowInEvent(false);

            IsCaravanTarget = !(attackedTarget is BaseOnline);
            string hostLogin = attackedTarget.Player.Public.Login;
            long hostPlaceId = attackedTarget.OnlineWObject.PlaceServerId;
            long initiatorPlaceId = UpdateWorldController.GetServerInfo(caravan).PlaceServerId;
            int targetTile = attackedTarget.Tile;

            LongEventHandler.QueueLongEvent(delegate
            {
                SessionClientController.Command((connect) =>
                {
                    try
                    {
                        connect.ErrorMessage = null;
                        AttackerReady = false;
                        _prepDialogShown = false;
                        _pauseTimeSentToHost = false;
                        _victoryHostSentTimeUtc = DateTime.MinValue;

                        Loger.Log("Client GameAttack State 0 (IsCaravanTarget=" + IsCaravanTarget + ")");
                        var res = connect.AttackOnlineInitiator(new AttackInitiatorToSrv
                        {
                            State = 0,
                            StartHostPlayer = hostLogin,
                            HostPlaceServerId = hostPlaceId,
                            InitiatorPlaceServerId = initiatorPlaceId,
                            TestMode = testMode,
                        });

                        if (!string.IsNullOrEmpty(res.ErrorText))
                        {
                            ErrorBreak(res.ErrorText?.ServerTranslate());
                            return;
                        }
                        if (!string.IsNullOrEmpty(connect.ErrorMessage))
                        {
                            ErrorBreak(connect.ErrorMessage?.ServerTranslate());
                            return;
                        }

                        Loger.Log("Client GameAttack State 1");
                        var s1Time = DateTime.UtcNow;
                        while (true)
                        {
                            var res1 = connect.AttackOnlineInitiator(new AttackInitiatorToSrv { State = 1 });
                            if (!string.IsNullOrEmpty(connect.ErrorMessage))
                            {
                                ErrorBreak(connect.ErrorMessage?.ServerTranslate());
                                return;
                            }
                            if (res1.State == 2) break;
                            if ((DateTime.UtcNow - s1Time).TotalSeconds > 30)
                            {
                                ErrorBreak("Timeout State 1");
                                return;
                            }
                            Thread.Sleep(100);
                        }

                        List<ThingEntry> pawnsToSend = null;
                        ModBaseData.RunMainThreadSync(() =>
                        {
                            Find.TickManager.Pause();
                            pawnsToSend = GetPawnsAndDeleteCaravan(caravan);
                        });

                        AttackerOriginalPawnLabels = new List<string>(pawnsToSend.Count);
                        AttackerOriginalPawnIds = new HashSet<int>();
                        for (int i = 0; i < pawnsToSend.Count; i++)
                        {
                            AttackerOriginalPawnLabels.Add(pawnsToSend[i].Name);
                            if (pawnsToSend[i].OriginalID != 0)
                            {
                                AttackerOriginalPawnIds.Add(pawnsToSend[i].OriginalID);
                            }
                        }

                        var response = connect.AttackOnlineInitiator(new AttackInitiatorToSrv
                        {
                            State = 2,
                            Pawns = pawnsToSend,
                        });

                        if (!string.IsNullOrEmpty(connect.ErrorMessage))
                        {
                            ErrorBreak(connect.ErrorMessage?.ServerTranslate());
                            return;
                        }
                        TestMode = response.TestMode;

                        Loger.Log("Client GameAttack WaitTo3");
                        s1Time = DateTime.UtcNow;
                        while (true)
                        {
                            response = connect.AttackOnlineInitiator(new AttackInitiatorToSrv { State = 3 });
                            if (!string.IsNullOrEmpty(connect.ErrorMessage))
                            {
                                ErrorBreak(connect.ErrorMessage?.ServerTranslate());
                                return;
                            }
                            if (response.State >= 4) break;
                            if ((DateTime.UtcNow - s1Time).TotalSeconds > 60)
                            {
                                ErrorBreak("Timeout State 3");
                                return;
                            }
                            Thread.Sleep(100);
                        }

                        GameAttackTrigger_Patch.ForceSpeed = 0f;
                        LockstepController.Reset(response.BattleSeed);

                        Loger.Log($"Client StartCreateClearMap Seed={response.BattleSeed} TerrainCell={response.TerrainDefNameCell?.Count ?? 0} Thing={response.ThingCell?.Count ?? 0}");

                        CreateClearMap(targetTile, response.MapSize.Get(), (map, mapParent) =>
                        {
                            GameMap = map;

                            var terrainDefCache = new Dictionary<string, TerrainDef>(32, StringComparer.Ordinal);
                            if (response.TerrainDefNameCell != null && response.TerrainDefName != null)
                            {
                                int terrCount = Math.Min(response.TerrainDefNameCell.Count, response.TerrainDefName.Count);
                                for (int i = 0; i < terrCount; i++)
                                {
                                    try
                                    {
                                        var current = response.TerrainDefNameCell[i].Get();
                                        if (!current.InBounds(map)) continue;

                                        var terrName = response.TerrainDefName[i];
                                        if (string.IsNullOrEmpty(terrName)) continue;

                                        if (!terrainDefCache.TryGetValue(terrName, out var terrDef))
                                        {
                                            terrDef = DefDatabase<TerrainDef>.GetNamedSilentFail(terrName);
                                            terrainDefCache[terrName] = terrDef;
                                        }

                                        if (terrDef != null)
                                        {
                                            map.terrainGrid.SetTerrain(current, terrDef);
                                        }
                                    }
                                    catch { }
                                }
                            }

                            int thingCount = (response.ThingCell != null && response.Thing != null)
                                ? Math.Min(response.ThingCell.Count, response.Thing.Count)
                                : 0;

                            ThingsIDDicRev = new Dictionary<int, int>(thingCount);
                            ThingsIDDic = new Dictionary<int, int>(thingCount);
                            ThingsObjDic = new Dictionary<int, Thing>(thingCount);
                            AttackerPawns = new Dictionary<Pawn, int>(16);
                            _ignorePosSyncUntilTick.Clear();
                            lock (CommandSync)
                            {
                                OutgoingCommands.Clear();
                                ScheduledHostCommands.Clear();
                            }
                            CheckDestroy = new Dictionary<int, Thing>(32);
                            CheckSpawn = new Dictionary<int, Thing>(32);

                            using (GameUtils.NormalGameError())
                            {
                                for (int i = 0; i < thingCount; i++)
                                {
                                    try
                                    {
                                        var current = response.ThingCell[i]?.Get() ?? IntVec3.Invalid;
                                        if (!current.InBounds(map)) continue;

                                        var tt = response.Thing[i];
                                        if (tt == null) continue;

                                        if (tt.Def != null && typeof(Corpse).IsAssignableFrom(tt.Def.thingClass))
                                            continue;

                                        var th = tt.CreateThing();
                                        if (th == null || th is Corpse || th is Blueprint || th is Frame || th is UnfinishedThing || th is Filth || th is Mote)
                                            continue;

                                        if (th is MinifiedThing minified && minified.InnerThing == null)
                                            continue;

                                        GenSpawn.Spawn(th, current, map, new Rot4(tt.Rotation), WipeMode.Vanish);

                                        if (tt.OriginalID != 0 && th.thingIDNumber != 0)
                                        {
                                            ThingsIDDicRev[tt.OriginalID] = th.thingIDNumber;
                                            ThingsIDDic[th.thingIDNumber] = tt.OriginalID;
                                            ThingsObjDic[th.thingIDNumber] = th;
                                        }
                                    }
                                    catch (Exception exThing)
                                    {
                                        Loger.Log("Client CreateClearMap skip thing warning: " + exThing.Message, Loger.LogLevel.WARNING);
                                    }
                                }
                            }

                            try
                            {
                                FloodFillerFog.DebugRefogMap(GameMap);
                            }
                            catch { }

                            CameraJumper.TryJump(new GlobalTargetInfo(GameMap.Center, GameMap));
                            GameAttackTrigger_Patch.ActiveAttacker[GameMap] = this;

                            PreparationEndTimeUtc = DateTime.UtcNow + DefaultPauseTimeToHost;
                            AttackUpdateTick = 0;

                            TimerObj = SessionClientController.Timers.Add(AttackUpdateDelay, AttackUpdate);
                        });
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("Client GameAttacker Start Exception: " + ex, Loger.LogLevel.ERROR);
                        ErrorBreak(ex.Message);
                    }
                });
            }, "Connecting", true, null);
        }

        private Thing GetThingByHostId(int hostid)
        {
            if (hostid == 0 || !ThingsIDDicRev.TryGetValue(hostid, out int id)) return null;
            ThingsObjDic.TryGetValue(id, out Thing thing);
            return thing;
        }

        private void DestroyThing(Thing thing, int hostId = 0)
        {
            if (thing == null) return;
            var id = thing.thingIDNumber;
            if (hostId != 0 || ThingsIDDic.TryGetValue(id, out hostId))
                ThingsIDDicRev.Remove(hostId);
            ThingsIDDic.Remove(id);
            ThingsObjDic.Remove(id);
            CheckDestroy.Remove(id);

            ModBaseData.RunMainThreadSync(() =>
            {
                try
                {
                    ThingDropIgnore = true;
                    if (!thing.Destroyed)
                    {
                        thing.Destroy();
                    }
                }
                finally
                {
                    ThingDropIgnore = false;
                }
            });
        }

        private void ErrorBreak(string msg)
        {
            Loger.Log("Client GameAttack error: " + msg, Loger.LogLevel.ERROR);
            Clear();
            SessionClientController.Disconnected("OCity_GameAttacker_Dialog_ErrorMessage".Translate());
        }

        private List<ThingEntry> GetPawnsAndDeleteCaravan(Caravan caravan)
        {
            var select = caravan.PawnsListForReading;
            var sendThings = new List<ThingEntry>(select.Count);
            for (int i = 0; i < select.Count; i++)
            {
                var entry = ThingEntry.CreateEntry(select[i], 1);
                if (entry.OriginalID == 0) entry.OriginalID = select[i].thingIDNumber;
                entry.TransportID = entry.OriginalID;
                sendThings.Add(entry);
            }

            try
            {
                ThingDropIgnore = true;
                caravan.RemoveAllPawns();
                if (caravan.Spawned)
                {
                    Find.WorldObjects.Remove(caravan);
                }
                for (int i = 0; i < select.Count; i++)
                {
                    GameUtils.PawnDestroy(select[i]);
                }
            }
            finally
            {
                ThingDropIgnore = false;
            }
            return sendThings;
        }

        public void ExecuteScheduledCommandsForTick(long currentBattleTick)
        {
            List<AttackPawnCommand> due = null;
            lock (CommandSync)
            {
                for (int i = 0; i < ScheduledHostCommands.Count; i++)
                {
                    var cmd = ScheduledHostCommands[i];
                    if (cmd != null && cmd.ScheduledTick <= currentBattleTick)
                    {
                        if (due == null) due = new List<AttackPawnCommand>(8);
                        due.Add(cmd);
                        ScheduledHostCommands.RemoveAt(i--);
                    }
                }
            }

            if (due == null) return;

            ApplyingRemoteCommand = true;
            try
            {
                for (int i = 0; i < due.Count; i++)
                {
                    var comm = due[i];
                    var pawn = GetThingByHostId(comm.HostPawnID) as Pawn;
                    if (pawn == null || pawn.Dead) continue;

                    ExecuteRemoteCommandOnPawn(pawn, comm);
                }
            }
            finally
            {
                ApplyingRemoteCommand = false;
            }
        }

        private void ApplyStateAndDeletions(AttackInitiatorFromSrv toClient)
        {
            if (toClient.HostCommands != null && toClient.HostCommands.Count > 0)
            {
                if (GameAttackTrigger_Patch.ForceSpeed <= 0f)
                {
                    ApplyingRemoteCommand = true;
                    try
                    {
                        for (int i = 0; i < toClient.HostCommands.Count; i++)
                        {
                            var comm = toClient.HostCommands[i];
                            if (comm == null) continue;
                            var pawn = GetThingByHostId(comm.HostPawnID) as Pawn;
                            if (pawn == null || pawn.Dead) continue;

                            ExecuteRemoteCommandOnPawn(pawn, comm);
                        }
                    }
                    finally
                    {
                        ApplyingRemoteCommand = false;
                    }
                }
                else
                {
                    lock (CommandSync)
                    {
                        ScheduledHostCommands.AddRange(toClient.HostCommands);
                    }
                }
            }

            if (toClient.UpdateState != null)
            {
                long curTick = LockstepController.LocalBattleTick;
                for (int i = 0; i < toClient.UpdateState.Count; i++)
                {
                    var st = toClient.UpdateState[i];
                    Thing thing = GetThingByHostId(st.HostThingID);
                    if (thing == null || thing.Destroyed) continue;

                    try
                    {
                        ThingDropIgnore = true;
                        if (thing is Pawn p && !p.Dead && p.Spawned)
                        {
                            var targetCell = st.Position.Get();
                            float distSq = p.Position.DistanceToSquared(targetCell);
                            bool sameHealthState = (int)p.health.State == (int)st.DownState;

                            if (sameHealthState)
                            {
                                bool isMoving = p.pather != null && p.pather.Moving;
                                if (isMoving)
                                {
                                    _ignorePosSyncUntilTick[p.thingIDNumber] = curTick + 90;
                                    if (distSq <= 36f)
                                    {
                                        if (p.def.useHitPoints && st.HitPoints > 0) p.HitPoints = st.HitPoints;
                                        continue;
                                    }
                                }
                                else
                                {
                                    if (_ignorePosSyncUntilTick.TryGetValue(p.thingIDNumber, out long ignoreUntil)
                                        && curTick <= ignoreUntil
                                        && distSq <= 36f)
                                    {
                                        if (p.def.useHitPoints && st.HitPoints > 0) p.HitPoints = st.HitPoints;
                                        continue;
                                    }

                                    if (AttackerPawns.ContainsKey(p) && distSq <= 4f)
                                    {
                                        if (p.def.useHitPoints && st.HitPoints > 0) p.HitPoints = st.HitPoints;
                                        continue;
                                    }
                                }
                            }
                        }

                        bool prevApplying = ApplyingRemoteCommand;
                        ApplyingRemoteCommand = true;
                        try
                        {
                            GameUtils.ApplyState(thing, st);
                        }
                        finally
                        {
                            ApplyingRemoteCommand = prevApplying;
                        }
                    }
                    finally
                    {
                        ThingDropIgnore = false;
                    }
                }
            }

            if (toClient.Delete != null)
            {
                for (int i = 0; i < toClient.Delete.Count; i++)
                {
                    Thing thing = GetThingByHostId(toClient.Delete[i]);
                    if (thing == null) continue;

                    DestroyThing(thing, toClient.Delete[i]);
                }
            }
        }

        private void ExecuteRemoteCommandOnPawn(Pawn pawn, AttackPawnCommand comm)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned) return;

            try
            {
                _ignorePosSyncUntilTick[pawn.thingIDNumber] = LockstepController.LocalBattleTick + 90;

                if (comm.Command == AttackPawnCommand.PawnCommand.SetDrafted)
                {
                    if (pawn.drafter != null && pawn.drafter.Drafted != comm.DraftState)
                    {
                        pawn.drafter.Drafted = comm.DraftState;
                    }
                    return;
                }

                if (comm.Command == AttackPawnCommand.PawnCommand.SetHostility)
                {
                    if (pawn.playerSettings != null)
                    {
                        pawn.playerSettings.hostilityResponse = (HostilityResponseMode)comm.HostilityMode;
                    }
                    return;
                }

                if (comm.Command == AttackPawnCommand.PawnCommand.OC_InventoryDrop)
                {
                    if (!string.IsNullOrEmpty(comm.TargetDefName) && pawn.inventory?.innerContainer != null)
                    {
                        for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                        {
                            var item = pawn.inventory.innerContainer[i];
                            if (item.def.defName == comm.TargetDefName)
                            {
                                GenDrop.TryDropSpawn(item, pawn.Position, GameMap, ThingPlaceMode.Near, out _);
                                break;
                            }
                        }
                    }
                    return;
                }

                JobDef jobDef = null;
                if (!string.IsNullOrEmpty(comm.JobDefName))
                {
                    jobDef = DefDatabase<JobDef>.GetNamedSilentFail(comm.JobDefName);
                }

                if (jobDef == null)
                {
                    if (comm.Command == AttackPawnCommand.PawnCommand.Goto) jobDef = JobDefOf.Goto;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Attack) jobDef = JobDefOf.AttackStatic;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.AttackMelee) jobDef = JobDefOf.AttackMelee;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Equip) jobDef = JobDefOf.Equip;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.TakeInventory) jobDef = JobDefOf.TakeInventory;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Wear) jobDef = JobDefOf.Wear;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.DropEquipment) jobDef = JobDefOf.DropEquipment;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.RemoveApparel) jobDef = JobDefOf.RemoveApparel;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Ingest) jobDef = JobDefOf.Ingest;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Strip) jobDef = JobDefOf.Strip;
                    else if (comm.Command == AttackPawnCommand.PawnCommand.TendPatient) jobDef = JobDefOf.TendPatient;
                    else jobDef = JobDefOf.Wait_Combat;
                }

                LocalTargetInfo targetA = LocalTargetInfo.Invalid;
                if (comm.TargetID != 0)
                {
                    var t = GetThingByHostId(comm.TargetID);
                    if (t != null) targetA = new LocalTargetInfo(t);
                }
                else if (comm.TargetPos != null && (comm.TargetPos.x != 0 || comm.TargetPos.z != 0))
                {
                    targetA = new LocalTargetInfo(comm.TargetPos.Get());
                }

                LocalTargetInfo targetB = LocalTargetInfo.Invalid;
                if (comm.TargetIDB != 0)
                {
                    var tB = GetThingByHostId(comm.TargetIDB);
                    if (tB != null) targetB = new LocalTargetInfo(tB);
                }
                else if (comm.TargetPosB != null && (comm.TargetPosB.x != 0 || comm.TargetPosB.z != 0))
                {
                    targetB = new LocalTargetInfo(comm.TargetPosB.Get());
                }

                var job = targetB.IsValid
                    ? new Job(jobDef, targetA, targetB)
                    : (targetA.IsValid ? new Job(jobDef, targetA) : new Job(jobDef));

                job.playerForced = true;
                job.expiryInterval = int.MaxValue;
                job.checkOverrideOnExpire = false;
                if (comm.Count > 0) job.count = comm.Count;

                if (!string.IsNullOrEmpty(comm.AbilityDefName) && pawn.abilities != null)
                {
                    var abDef = DefDatabase<AbilityDef>.GetNamedSilentFail(comm.AbilityDefName);
                    if (abDef != null)
                    {
                        var ability = pawn.abilities.GetAbility(abDef);
                        if (ability != null)
                        {
                            job.ability = ability;
                            job.verbToUse = ability.verb;
                        }
                    }
                }

                pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
            }
            catch (Exception ex)
            {
                Loger.Log("Client ExecuteRemoteCommandOnPawn Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        private void AttackUpdate()
        {
            bool inTimerEvent = false;
            try
            {
                if (SessionClientController.Data?.AttackModule != this)
                {
                    return;
                }

                if (TerribleFatalError)
                {
                    ModBaseData.RunMainThread(() => Find.TickManager?.Pause());
                    return;
                }

                if (LongEventHandler.AnyEventNowOrWaiting)
                {
                    return;
                }

                bool isPrepPhase = (DateTime.UtcNow < PreparationEndTimeUtc && LockstepController.RemoteBattleTick <= 0)
                    || !AttackerReady
                    || !LockstepController.RemoteReady;

                if (isPrepPhase)
                {
                    GameAttackTrigger_Patch.ForceSpeed = 0f;
                    if (Find.TickManager != null && !Find.TickManager.Paused)
                    {
                        ModBaseData.RunMainThread(() =>
                        {
                            if (SessionClientController.Data?.AttackModule != this) return;
                            if (Find.TickManager != null && !Find.TickManager.Paused)
                            {
                                Find.TickManager.Pause();
                                GameAttackTrigger_Patch.ForceSpeed = 0f;
                            }
                        });
                    }
                }
                else
                {
                    GameAttackTrigger_Patch.ForceSpeed = 1f;
                    if (Find.TickManager != null && (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed != TimeSpeed.Normal))
                    {
                        ModBaseData.RunMainThread(() =>
                        {
                            if (SessionClientController.Data?.AttackModule != this) return;
                            if (Find.TickManager != null)
                            {
                                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                                GameAttackTrigger_Patch.ForceSpeed = 1f;
                            }
                        });
                    }
                }

                if (InTimer) return;
                InTimer = true;
                AttackUpdateTick++;

                if (MainHelper.DebugMode || AttackUpdateTick % 20 == 0)
                {
                    Loger.Log($"Client Lockstep AttackUpdate #{AttackUpdateTick} Pawns={AttackerPawns.Count}/{AttackerOriginalPawnLabels.Count} LocalTick={LockstepController.LocalBattleTick} RemoteTick={LockstepController.RemoteBattleTick}");
                }

                if (!AttackerReady && AttackerPawns.Count >= AttackerOriginalPawnLabels.Count && AttackerOriginalPawnLabels.Count > 0)
                {
                    AttackerReady = true;
                    PreparationEndTimeUtc = DateTime.UtcNow + DefaultPauseTimeToHost;
                    SessionClientController.Data.DontCheckTimerFail = false;

                    ModBaseData.RunMainThreadSync(() =>
                    {
                        var findLabel = "LetterLabelAreaRevealed".Translate();
                        var letters = Find.LetterStack.LettersListForReading;
                        for (int i = letters.Count - 1; i >= 0; i--)
                        {
                            if (letters[i].Label == findLabel)
                            {
                                Find.LetterStack.RemoveLetter(letters[i]);
                            }
                        }
                    });
                }
                else if (!AttackerReady && AttackUpdateTick > 300)
                {
                    Loger.Log($"Client AttackUpdate: TerribleFatalError timeout waiting for pawns ({AttackerPawns.Count}/{AttackerOriginalPawnLabels.Count})", Loger.LogLevel.ERROR);
                    SessionClientController.Command((connect) =>
                    {
                        connect.AttackOnlineInitiator(new AttackInitiatorToSrv
                        {
                            State = 10,
                            TerribleFatalError = true,
                        });
                        TerribleFatalError = true;
                    });
                    return;
                }

                if (AttackerReady && !_prepDialogShown)
                {
                    _prepDialogShown = true;
                    ModBaseData.RunMainThread(() =>
                    {
                        GameUtils.ShowDialodOKCancel(
                            "OCity_GameAttacker_Dialog_Settlement_Attack".Translate(),
                            "OCity_GameAttacker_Main_Preparation_Dialog".Translate() + Environment.NewLine
                                + "OCity_GameAttacker_Main_Dialog".Translate() + Environment.NewLine
                                + "OCity_GameAttacker_Withdraw".Translate(),
                            () => { },
                            null
                        );
                    });
                }

                inTimerEvent = true;
                SessionClientController.Command((connect) =>
                {
                    try
                    {
                        _needNewThingsBuffer.Clear();

                        CheckSpawnDestroyDisable = true;
                        lock (CheckSpawnDestroySync)
                        {
                            foreach (var checkSpawn in CheckSpawn)
                            {
                                if (!ThingsIDDic.ContainsKey(checkSpawn.Key))
                                {
                                    try
                                    {
                                        var thingToDestroy = checkSpawn.Value;
                                        ModBaseData.RunMainThreadSync(() =>
                                        {
                                            if (thingToDestroy != null && !thingToDestroy.Destroyed)
                                            {
                                                thingToDestroy.Destroy();
                                            }
                                        });
                                    }
                                    catch (Exception ext2)
                                    {
                                        Loger.Log("Client AttackUpdate Destroy Exception: " + ext2.Message, Loger.LogLevel.ERROR);
                                    }
                                }
                            }
                            CheckSpawn.Clear();

                            foreach (var checkDestroy in CheckDestroy)
                            {
                                if (ThingsIDDic.TryGetValue(checkDestroy.Key, out int cdOutID))
                                {
                                    _needNewThingsBuffer.Add(cdOutID);
                                }
                            }
                            CheckDestroy.Clear();
                        }
                        CheckSpawnDestroyDisable = false;

                        _commandsBuffer.Clear();
                        lock (CommandSync)
                        {
                            if (OutgoingCommands.Count > 0)
                            {
                                _commandsBuffer.AddRange(OutgoingCommands);
                                OutgoingCommands.Clear();
                            }
                        }

                        TimeSpan pauseToSend = TimeSpan.MinValue;
                        if (AttackerReady && !_pauseTimeSentToHost)
                        {
                            _pauseTimeSentToHost = true;
                            pauseToSend = DefaultPauseTimeToHost;
                        }
                        else if (AttackUpdateTick == 2)
                        {
                            pauseToSend = DefaultPauseTimeToHost;
                        }

                        if (VictoryHostToHost && _victoryHostSentTimeUtc == DateTime.MinValue)
                        {
                            _victoryHostSentTimeUtc = DateTime.UtcNow;
                        }

                        var toClient = connect.AttackOnlineInitiator(new AttackInitiatorToSrv
                        {
                            State = 10,
                            CurrentBattleTick = LockstepController.LocalBattleTick,
                            AttackerReady = AttackerReady,
                            UpdateCommand = _commandsBuffer,
                            SetPauseOnTimeToHost = pauseToSend,
                            VictoryHostToHost = VictoryHostToHost,
                            NeedNewThingIDs = _needNewThingsBuffer,
                        });

                        if (toClient == null)
                        {
                            InTimer = false;
                            return;
                        }

                        if (toClient.HostReady)
                        {
                            LockstepController.RemoteReady = true;
                        }
                        if (toClient.HostBattleTick >= 0)
                        {
                            LockstepController.UpdateRemoteTick(toClient.HostBattleTick);
                        }

                        bool hasNewPawns = toClient.NewPawns != null && toClient.NewPawns.Count > 0;
                        bool hasNewCorpses = toClient.NewCorpses != null && toClient.NewCorpses.Count > 0;
                        bool hasNewThings = toClient.NewThings != null && toClient.NewThings.Count > 0;

                        if (hasNewPawns || hasNewCorpses || hasNewThings)
                        {
                            try
                            {
                                ModBaseData.RunMainThreadSync(() =>
                                {
                                    bool prevApplying = ApplyingRemoteCommand;
                                    ApplyingRemoteCommand = true;
                                    try
                                    {
                                        if (hasNewPawns && toClient.NewPawnsId != null)
                                        {
                                            var filteredPawns = new List<ThingEntry>(toClient.NewPawns.Count);
                                            int pairCount = Math.Min(toClient.NewPawns.Count, toClient.NewPawnsId.Count);
                                            for (int i = 0; i < pairCount; i++)
                                            {
                                                var hostid = toClient.NewPawnsId[i];
                                                Thing existingThing = GetThingByHostId(hostid);

                                                if (existingThing is Pawn existingPawn && existingPawn.Spawned && !existingPawn.Destroyed && !existingPawn.Dead)
                                                {
                                                    continue;
                                                }

                                                if (existingThing != null) DestroyThing(existingThing, hostid);

                                                var te = toClient.NewPawns[i];
                                                if (te != null)
                                                {
                                                    bool isAttackerPawn = te.TransportID != 0
                                                        || (te.OriginalID != 0 && te.OriginalID != hostid)
                                                        || (te.OriginalID != 0 && AttackerOriginalPawnIds != null && AttackerOriginalPawnIds.Contains(te.OriginalID));

                                                    if (isAttackerPawn)
                                                    {
                                                        te.TransportID = te.OriginalID != 0 ? te.OriginalID : hostid;
                                                    }
                                                    else
                                                    {
                                                        te.TransportID = 0;
                                                    }
                                                    te.OriginalID = hostid;
                                                    filteredPawns.Add(te);
                                                }
                                            }

                                            if (filteredPawns.Count > 0)
                                            {
                                                GameUtils.SpawnList(GameMap, filteredPawns, false,
                                                    p => p.TransportID == 0,
                                                    (th, te) =>
                                                    {
                                                        CheckDestroy.Remove(th.thingIDNumber);
                                                        var p = th as Pawn;
                                                        if (te.OriginalID != 0 && th.thingIDNumber != 0)
                                                        {
                                                            ThingsIDDicRev[te.OriginalID] = th.thingIDNumber;
                                                            ThingsIDDic[th.thingIDNumber] = te.OriginalID;
                                                            ThingsObjDic[th.thingIDNumber] = th;

                                                            if (te.TransportID != 0 && p != null)
                                                            {
                                                                if (p.Faction != Faction.OfPlayer)
                                                                {
                                                                    p.SetFaction(Faction.OfPlayer);
                                                                }
                                                                if (p.jobs != null && p.jobs.jobQueue == null)
                                                                {
                                                                    p.jobs.jobQueue = new JobQueue();
                                                                }

                                                                AttackerPawns[p] = te.OriginalID;
                                                                if (p.playerSettings != null) p.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                                                                if (p.drafter != null) p.drafter.Drafted = true;

                                                                try
                                                                {
                                                                    if (GameMap.fogGrid != null)
                                                                    {
                                                                        FloodFillerFog.FloodUnfog(p.Position, GameMap);
                                                                        foreach (var adj in GenAdj.CellsAdjacent8Way(p))
                                                                        {
                                                                            if (adj.InBounds(GameMap) && GameMap.fogGrid.IsFogged(adj))
                                                                            {
                                                                                FloodFillerFog.FloodUnfog(adj, GameMap);
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                catch { }

                                                                p.jobs?.StartJob(new Job(JobDefOf.Wait_Combat)
                                                                {
                                                                    playerForced = true,
                                                                    expiryInterval = int.MaxValue,
                                                                    checkOverrideOnExpire = false,
                                                                }, JobCondition.InterruptForced);

                                                                if (AttackerPawns.Count == 1)
                                                                {
                                                                    CameraJumper.TryJump(new GlobalTargetInfo(p.Position, GameMap));
                                                                    Find.ColonistBar?.MarkColonistsDirty();
                                                                }
                                                            }
                                                            else if (p != null)
                                                            {
                                                                if (p.def.CanHaveFaction && p.Faction != null && p.Faction.IsPlayer)
                                                                {
                                                                    var hostileFac = SessionClientController.Data?.FactionPirate
                                                                        ?? Find.FactionManager?.FirstFactionOfDef(FactionDefOf.Pirate);
                                                                    p.SetFaction(hostileFac);
                                                                }
                                                                if (!p.Dead && !p.Downed && p.jobs != null)
                                                                {
                                                                    p.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                                                                    {
                                                                        playerForced = true,
                                                                        expiryInterval = int.MaxValue,
                                                                        checkOverrideOnExpire = false,
                                                                    }, JobCondition.InterruptForced);
                                                                }
                                                            }
                                                        }
                                                    });

                                                Find.ColonistBar?.MarkColonistsDirty();
                                            }
                                        }

                                        if (hasNewCorpses)
                                        {
                                            for (int i = 0; i < toClient.NewCorpses.Count; i++)
                                            {
                                                var hostid = toClient.NewCorpses[i].PawnId;
                                                Thing thing = GetThingByHostId(hostid);
                                                if (thing != null) DestroyThing(thing, hostid);

                                                var corpseHostId = toClient.NewCorpses[i].CorpseId;
                                                Thing cThing = GetThingByHostId(corpseHostId);
                                                if (cThing != null) DestroyThing(cThing, corpseHostId);
                                            }

                                            _corpseListBuffer.Clear();
                                            for (int i = 0; i < toClient.NewCorpses.Count; i++)
                                            {
                                                _corpseListBuffer.Add(toClient.NewCorpses[i].CorpseWithPawn);
                                            }

                                            GameUtils.SpawnList(GameMap, _corpseListBuffer, false,
                                                p => p.TransportID == 0,
                                                (th, te) =>
                                                {
                                                    int corpsId = 0;
                                                    lock (CheckSpawnDestroySync)
                                                    {
                                                        foreach (var cs in CheckSpawn)
                                                        {
                                                            if (cs.Value is Corpse corpse && corpse.InnerPawn != null && corpse.InnerPawn.thingIDNumber == th.thingIDNumber)
                                                            {
                                                                corpsId = cs.Key;
                                                                break;
                                                            }
                                                        }
                                                        CheckDestroy.Remove(th.thingIDNumber);
                                                        if (corpsId != 0) CheckDestroy.Remove(corpsId);
                                                    }

                                                    if (te.OriginalID != 0 && corpsId != 0)
                                                    {
                                                        ThingsIDDicRev[te.OriginalID] = corpsId;
                                                        ThingsIDDic[corpsId] = te.OriginalID;
                                                        ThingsObjDic[corpsId] = th;
                                                    }
                                                });
                                        }

                                        if (hasNewThings && toClient.NewThingsId != null)
                                        {
                                            for (int i = 0; i < toClient.NewThingsId.Count; i++)
                                            {
                                                var hostid = toClient.NewThingsId[i];
                                                Thing thing = GetThingByHostId(hostid);
                                                if (thing != null) DestroyThing(thing, hostid);
                                            }

                                            GameUtils.SpawnList(GameMap, toClient.NewThings, false,
                                                p => false,
                                                (th, te) =>
                                                {
                                                    CheckDestroy.Remove(th.thingIDNumber);
                                                    if (te.OriginalID != 0 && th.thingIDNumber != 0)
                                                    {
                                                        ThingsIDDicRev[te.OriginalID] = th.thingIDNumber;
                                                        ThingsIDDic[th.thingIDNumber] = te.OriginalID;
                                                        ThingsObjDic[th.thingIDNumber] = th;
                                                    }
                                                });
                                        }
                                    }
                                    finally
                                    {
                                        ApplyingRemoteCommand = prevApplying;
                                    }

                                    ApplyStateAndDeletions(toClient);
                                });
                            }
                            catch (Exception ext)
                            {
                                Loger.Log("Client AttackUpdate SpawnListEvent Exception: " + ext, Loger.LogLevel.ERROR);
                            }
                            InTimer = false;
                        }
                        else
                        {
                            ModBaseData.RunMainThreadSync(() => ApplyStateAndDeletions(toClient));
                            InTimer = false;
                        }

                        if (toClient.Finishing || (VictoryHostToHost && _victoryHostSentTimeUtc != DateTime.MinValue && (DateTime.UtcNow - _victoryHostSentTimeUtc).TotalSeconds > 2.0))
                        {
                            Finish(toClient.Finishing ? toClient.VictoryAttacker : false);
                        }
                    }
                    catch (Exception ext)
                    {
                        InTimer = false;
                        Loger.Log("Client AttackUpdate Command Exception: " + ext, Loger.LogLevel.ERROR);
                    }
                });
            }
            catch (Exception ext)
            {
                Loger.Log("AttackUpdate Exception: " + ext, Loger.LogLevel.ERROR);
            }
            if (!inTimerEvent) InTimer = false;
        }

        public void UIEventDraftChanged(Pawn pawn, bool drafted)
        {
            if (ApplyingRemoteCommand || pawn == null) return;
            if (!AttackerPawns.TryGetValue(pawn, out int id)) return;

            lock (CommandSync)
            {
                OutgoingCommands.Add(new AttackPawnCommand
                {
                    HostPawnID = id,
                    Command = AttackPawnCommand.PawnCommand.SetDrafted,
                    DraftState = drafted,
                    ScheduledTick = LockstepController.LocalBattleTick + LockstepController.CommandTickDelay
                });
            }
        }

        public void UIEventNewJob(Pawn pawn, Job job)
        {
            if (ApplyingRemoteCommand || job == null || pawn == null) return;

            if (job.def == JobDefOf.Wait_Combat || job.def == JobDefOf.Wait || job.def == JobDefOf.Wait_MaintainPosture || job.def == JobDefOf.GotoWander)
            {
                if (!job.playerForced) return;
            }
            else if (!job.playerForced && job.def != JobDefOf.AttackStatic && job.def != JobDefOf.AttackMelee)
            {
                return;
            }

            try
            {
                if (!AttackerPawns.TryGetValue(pawn, out int id)) return;

                _ignorePosSyncUntilTick[pawn.thingIDNumber] = LockstepController.LocalBattleTick + 120;

                var comm = new AttackPawnCommand
                {
                    HostPawnID = id,
                    Command = job.ability != null ? AttackPawnCommand.PawnCommand.CastAbility : AttackPawnCommand.PawnCommand.UniversalJob,
                    JobDefName = job.def?.defName ?? "Wait_Combat",
                    AbilityDefName = job.ability?.def?.defName,
                    Count = job.count,
                    ScheduledTick = LockstepController.LocalBattleTick + LockstepController.CommandTickDelay
                };

                if (job.targetA.HasThing)
                {
                    if (!ThingsIDDic.TryGetValue(job.targetA.Thing.thingIDNumber, out int tid))
                    {
                        comm.TargetDefName = job.targetA.Thing.def.defName;
                    }
                    else
                    {
                        comm.TargetID = tid;
                    }
                }
                else if (job.targetA.IsValid)
                {
                    comm.TargetPos = new IntVec3S(job.targetA.Cell);
                }

                if (job.targetB.HasThing)
                {
                    if (ThingsIDDic.TryGetValue(job.targetB.Thing.thingIDNumber, out int tidB))
                    {
                        comm.TargetIDB = tidB;
                    }
                }
                else if (job.targetB.IsValid)
                {
                    comm.TargetPosB = new IntVec3S(job.targetB.Cell);
                }

                lock (CommandSync)
                {
                    if (OutgoingCommands.Count > 0)
                    {
                        var last = OutgoingCommands[OutgoingCommands.Count - 1];
                        if (last.HostPawnID == comm.HostPawnID
                            && last.JobDefName == comm.JobDefName
                            && last.TargetID == comm.TargetID
                            && last.ScheduledTick == comm.ScheduledTick)
                        {
                            return;
                        }
                    }
                    OutgoingCommands.Add(comm);
                }
            }
            catch (Exception exp)
            {
                Loger.Log("AttackUpdate UIEventNewJob Exception: " + exp.Message, Loger.LogLevel.ERROR);
            }
        }

        public bool UIEventInventoryDrop(Thing thing)
        {
            try
            {
                var pawn = (thing.ParentHolder as Pawn_InventoryTracker)?.pawn;
                if (pawn == null) return true;

                if (!AttackerPawns.TryGetValue(pawn, out int id)) return true;

                lock (CommandSync)
                {
                    OutgoingCommands.Add(new AttackPawnCommand
                    {
                        HostPawnID = id,
                        TargetDefName = thing.def.defName,
                        Command = AttackPawnCommand.PawnCommand.OC_InventoryDrop,
                        ScheduledTick = LockstepController.LocalBattleTick + LockstepController.CommandTickDelay
                    });
                }
            }
            catch (Exception exp)
            {
                Loger.Log("AttackUpdate UIEventInventoryDrop Exception: " + exp.Message, Loger.LogLevel.ERROR);
            }
            return true;
        }

        public void UIEventChange(Thing thing, bool distroy = false, bool newSpawn = false)
        {
            if (thing is Plant && !thing.def.plant.IsTree) return;
            if (CheckSpawnDestroyDisable) return;

            lock (CheckSpawnDestroySync)
            {
                if (distroy)
                {
                    CheckDestroy[thing.thingIDNumber] = thing;
                    CheckSpawn.Remove(thing.thingIDNumber);
                }
                else if (newSpawn)
                {
                    CheckSpawn[thing.thingIDNumber] = thing;
                    CheckDestroy.Remove(thing.thingIDNumber);
                }
            }
        }

        private void CreateClearMap(int tile, IntVec3 mapSize, Action<Map, MapParent> ready)
        {
            LongEventHandler.QueueLongEvent(delegate
            {
                try
                {
                    Rand.PushState();
                    try
                    {
                        TileFinder_IsValidTileForNewSettlement_Patch.Off = true;
                        Rand.Seed = Gen.HashCombineInt(Find.World.info.Seed, tile);

                        var mapParent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Ambush);
                        mapParent.Tile = tile;
                        Find.WorldObjects.Add(mapParent);
                        mapParent.SetFaction(Find.FactionManager.OfPlayer);

                        var genSteps = new List<GenStepDef>(8);
                        for (int i = 0; i < mapParent.MapGeneratorDef.genSteps.Count; i++)
                        {
                            var gs = mapParent.MapGeneratorDef.genSteps[i];
                            if (AllowedGenSteps.Contains(gs.defName))
                            {
                                genSteps.Add(gs);
                            }
                        }

                        var mapSet = new MapGeneratorDef { genSteps = genSteps };
                        var map = MapGenerator.GenerateMap(mapSize, mapParent, mapSet, null, null);

                        LongEventHandler.QueueLongEvent(delegate
                        {
                            try
                            {
                                CellRect cellRect = CellRect.WholeMap(map);
                                cellRect.ClipInsideMap(map);
                                GenDebug.ClearArea(cellRect, map);

                                ready(map, mapParent);
                                TileFinder_IsValidTileForNewSettlement_Patch.Off = false;
                            }
                            catch (Exception exp)
                            {
                                Loger.Log("Client CreateClearMap Event Exception: " + exp, Loger.LogLevel.ERROR);
                                ErrorBreak("Error CreateClearMap2");
                            }
                        }, "GeneratingMapForNewEncounter", false, null);
                    }
                    finally
                    {
                        Rand.PopState();
                    }
                }
                catch (Exception exp)
                {
                    Loger.Log("Client CreateClearMap Exception: " + exp, Loger.LogLevel.ERROR);
                    ErrorBreak("Error CreateClearMap1");
                }
            }, "GeneratingMapForNewEncounter", false, null);
        }

        public void Clear()
        {
            if (TimerObj != null) SessionClientController.Timers.Remove(TimerObj);
            TimerObj = null;
            if (GameMap != null) GameAttackTrigger_Patch.ActiveAttacker.Remove(GameMap);
            SessionClientController.Data.AttackModule = null;
            SessionClientController.Data.BackgroundSaveGameOff = false;
            SessionClientController.Data.DontCheckTimerFail = false;
            GameAttackTrigger_Patch.ForceSpeed = -1f;
            LockstepController.Reset(0);
        }

        public void Finish(bool victoryAttacker)
        {
            ModBaseData.RunMainThreadSync(() =>
            {
                if (SessionClientController.Data?.AttackModule != this) return;

                Find.TickManager.Pause();
                Loger.Log("Client AttackerFinish (victoryAttacker=" + victoryAttacker + ", IsCaravanTarget=" + IsCaravanTarget + ")");

                Clear();

                if (TestMode)
                {
                    GameUtils.ShowDialodOKCancel(
                        "OCity_GameAttacker_Dialog_Settlement_Attack".Translate(),
                        victoryAttacker
                            ? "Ocity_GameAttacker_TrainingFight_Won".Translate() + Environment.NewLine + "Ocity_GameAttacker_TrainingFight_Caravan_Restore".Translate()
                            : "Ocity_GameAttacker_TrainingFight_Lost".Translate() + Environment.NewLine + "Ocity_GameAttacker_TrainingFight_Caravan_Restore".Translate(),
                        () => SessionClientController.Disconnected("OCity_GameAttacker_Done".Translate()),
                        null
                    );
                    return;
                }

                if (victoryAttacker && !IsCaravanTarget)
                {
                    // Перемога над поселенням: карта переходить у власність нападника
                    var mapPawnsA = GameMap.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < mapPawnsA.Count; i++)
                    {
                        var pawn = mapPawnsA[i];
                        if (!pawn.def.CanHaveFaction || pawn.RaceProps.Humanlike || pawn.Faction == null || pawn.Faction.IsPlayer) continue;
                        pawn.SetFaction(null);
                    }
                }
                else if (victoryAttacker && IsCaravanTarget)
                {
                    // Перемога над караваном: збираємо всіх живих колоністів/тварин нападника + трофеї з карти і формуємо караван
                    var listPawn = new List<Pawn>(AttackerPawns.Count);
                    foreach (var pawn in AttackerPawns.Keys)
                    {
                        if (pawn != null && !pawn.Dead && !pawn.Destroyed)
                        {
                            if (pawn.carryTracker?.CarriedThing != null && pawn.inventory?.innerContainer != null)
                            {
                                pawn.carryTracker.innerContainer.TryTransferAllToContainer(pawn.inventory.innerContainer);
                            }
                            listPawn.Add(pawn);
                        }
                    }

                    // Збираємо трофеї з повалених/мертвих ворогів та речі на землі
                    var lootThings = new List<Thing>();
                    if (GameMap != null)
                    {
                        var allThings = GameMap.listerThings.AllThings;
                        for (int i = allThings.Count - 1; i >= 0; i--)
                        {
                            var th = allThings[i];
                            if (th == null || th.Destroyed) continue;

                            if (th is Corpse corpse && corpse.InnerPawn != null && !AttackerPawns.ContainsKey(corpse.InnerPawn))
                            {
                                var innerP = corpse.InnerPawn;
                                if (innerP.equipment?.AllEquipmentListForReading != null)
                                {
                                    for (int e = innerP.equipment.AllEquipmentListForReading.Count - 1; e >= 0; e--)
                                    {
                                        var eq = innerP.equipment.AllEquipmentListForReading[e];
                                        innerP.equipment.Remove(eq);
                                        lootThings.Add(eq);
                                    }
                                }
                                if (innerP.apparel?.WornApparel != null)
                                {
                                    for (int a = innerP.apparel.WornApparel.Count - 1; a >= 0; a--)
                                    {
                                        var ap = innerP.apparel.WornApparel[a];
                                        innerP.apparel.Remove(ap);
                                        lootThings.Add(ap);
                                    }
                                }
                                if (innerP.inventory?.innerContainer != null)
                                {
                                    for (int inv = innerP.inventory.innerContainer.Count - 1; inv >= 0; inv--)
                                    {
                                        var item = innerP.inventory.innerContainer[inv];
                                        innerP.inventory.innerContainer.Remove(item);
                                        lootThings.Add(item);
                                    }
                                }
                            }
                            else if (th.def.category == ThingCategory.Item && !(th is Corpse))
                            {
                                if (th.Spawned) th.DeSpawn();
                                lootThings.Add(th);
                            }
                        }

                        var allPawns = GameMap.mapPawns.AllPawnsSpawned;
                        for (int i = allPawns.Count - 1; i >= 0; i--)
                        {
                            var p = allPawns[i];
                            if (p != null && !AttackerPawns.ContainsKey(p))
                            {
                                if (p.equipment?.AllEquipmentListForReading != null)
                                {
                                    for (int e = p.equipment.AllEquipmentListForReading.Count - 1; e >= 0; e--)
                                    {
                                        var eq = p.equipment.AllEquipmentListForReading[e];
                                        p.equipment.Remove(eq);
                                        lootThings.Add(eq);
                                    }
                                }
                                if (p.inventory?.innerContainer != null)
                                {
                                    for (int inv = p.inventory.innerContainer.Count - 1; inv >= 0; inv--)
                                    {
                                        var item = p.inventory.innerContainer[inv];
                                        p.inventory.innerContainer.Remove(item);
                                        lootThings.Add(item);
                                    }
                                }
                            }
                        }
                    }

                    int mapTile = GameMap.Tile;
                    Caravan newCaravan = null;
                    if (listPawn.Count > 0)
                    {
                        newCaravan = CaravanMaker.MakeCaravan(listPawn, Faction.OfPlayer, mapTile, false);
                        for (int i = 0; i < lootThings.Count; i++)
                        {
                            var item = lootThings[i];
                            if (item == null || item.Destroyed) continue;
                            var recipient = CaravanInventoryUtility.FindPawnToMoveInventoryTo(item, newCaravan.PawnsListForReading, null);
                            if (recipient?.inventory?.innerContainer != null)
                            {
                                recipient.inventory.innerContainer.TryAdd(item, true);
                            }
                        }
                    }

                    GameUtils.DeinitAndRemoveMapSafe(GameMap);

                    for (int i = 0; i < listPawn.Count; i++)
                    {
                        var pawn = listPawn[i];
                        if (!pawn.IsWorldPawn())
                        {
                            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
                        }
                    }
                }
                else
                {
                    // Поразка або відступ: повертаємо в караван усіх не збитих з ніг колоністів із прикордонної зони
                    var listPawn = new List<Pawn>(AttackerPawns.Count);
                    foreach (var pawn in AttackerPawns.Keys)
                    {
                        if (pawn != null && !pawn.Dead && !pawn.Downed &&
                            (pawn.Position.x < MapBorder || pawn.Position.x > GameMap.Size.x - 1 - MapBorder
                             || pawn.Position.z < MapBorder || pawn.Position.z > GameMap.Size.z - 1 - MapBorder))
                        {
                            if (pawn.carryTracker?.CarriedThing != null && pawn.inventory?.innerContainer != null)
                            {
                                pawn.carryTracker.innerContainer.TryTransferAllToContainer(pawn.inventory.innerContainer);
                            }
                            listPawn.Add(pawn);
                        }
                    }

                    int mapTile = GameMap.Tile;
                    if (listPawn.Count > 0)
                    {
                        CaravanMaker.MakeCaravan(listPawn, Faction.OfPlayer, mapTile, false);
                    }

                    GameUtils.DeinitAndRemoveMapSafe(GameMap);

                    for (int i = 0; i < listPawn.Count; i++)
                    {
                        var pawn = listPawn[i];
                        if (!pawn.IsWorldPawn())
                        {
                            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
                        }
                    }
                }

                SessionClientController.SaveGameNow(true, () =>
                {
                    ModBaseData.RunMainThread(() =>
                    {
                        GameUtils.ShowDialodOKCancel(
                            "OCity_GameAttacker_Dialog_Settlement_Attack".Translate(),
                            victoryAttacker
                                ? "OCity_GameAttacker_Settlement_TakenOver".Translate()
                                : "OCity_GameAttacker_Defeated".Translate() + Environment.NewLine + "OCity_GameAttacker_Colonist_Return".Translate(),
                            () =>
                            {
                                GameAttackTrigger_Patch.ForceSpeed = -1f;
                                if (Find.TickManager != null && Find.TickManager.Paused)
                                {
                                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                                }
                            },
                            null
                        );
                    });
                });

                Loger.Log("Client AttackerFinish end");
            });
        }
    }
}