using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Host-side (defender) controller for Instanced Lockstep PvP.
    /// Supports defending both player settlements (MapParent) and player caravans (Caravan).
    /// </summary>
    public class GameAttackHost
    {
        private class RetreatingPawnInfo
        {
            public string AttackerLogin;
            public int Tile;
        }

        // Tracks stranded attacker pawns fleeing off the Host's colony map after surrender
        private static readonly Dictionary<int, RetreatingPawnInfo> RetreatingPawnsToOwner =
            new Dictionary<int, RetreatingPawnInfo>(16);

        public static bool TryHandleRetreatingPawnExit(Pawn pawn)
        {
            if (pawn == null) return false;

            RetreatingPawnInfo info;
            lock (RetreatingPawnsToOwner)
            {
                if (!RetreatingPawnsToOwner.TryGetValue(pawn.thingIDNumber, out info))
                {
                    return false;
                }
                RetreatingPawnsToOwner.Remove(pawn.thingIDNumber);
            }

            try
            {
                if (pawn.Dead || pawn.Destroyed) return true;

                if (pawn.carryTracker?.CarriedThing != null && pawn.inventory?.innerContainer != null)
                {
                    pawn.carryTracker.innerContainer.TryTransferAllToContainer(pawn.inventory.innerContainer);
                }

                var entry = ThingEntry.CreateEntry(pawn, 1);
                entry.Affiliation = PawnAffiliation.Colonist;

                GameUtils.PawnDestroy(pawn);

                string targetLogin = info.AttackerLogin;
                int targetTile = info.Tile;

                if (!string.IsNullOrEmpty(targetLogin) && SessionClientController.My != null)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            SessionClientController.CommandSafely((connect) =>
                                connect.SendThings(
                                    new List<ThingEntry>(1) { entry },
                                    SessionClientController.My.Login,
                                    targetLogin,
                                    ExchengeUtils.RetreatingPawnMarkerId,
                                    targetTile));
                        }
                        catch (Exception ex)
                        {
                            Loger.Log("TryHandleRetreatingPawnExit network error: " + ex.Message, Loger.LogLevel.WARNING);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Loger.Log("TryHandleRetreatingPawnExit Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }

            return true;
        }

        public int AttackUpdateDelay { get; } = 50;
        public bool PlantNotSend { get; } = false;
        public int SendDelayedFillPawnsSeconds { get; } = 30;
        public int CheckVictoryDelay { get; } = 3;
        public int MapBorder { get; } = 15;
        public float TickTimeSpeed { get; } = 1f;
        public float HostPawnMoveSpeed { get; } = 1f;

        public bool TestMode { get; set; }
        public string AttackerLogin { get; set; }
        public long HostPlaceServerId { get; set; }
        public long InitiatorPlaceServerId { get; set; }

        public HashSet<int> SendedPawnsId { get; set; }
        public Dictionary<int, int> SendedState { get; set; }
        public Dictionary<int, int> SendedXP { get; set; }

        public HashSet<int> ToUpdateStateId { get; set; }
        public List<Thing> ToUpdateState { get; set; }

        public HashSet<int> ToSendDeleteId { get; set; }
        public HashSet<int> ToSendAddId { get; set; }
        public HashSet<Thing> ToSendThingAdd { get; set; }
        public Dictionary<int, Thing> SendedActual { get; set; }
        public HashSet<Thing> FireList { get; set; }
        public HashSet<int> ToSendDelayedFillPawnsId { get; set; }

        public DateTime SendDelayedFillPawnsLastTime;

        public HashSet<Pawn> AttackingPawns { get; set; }
        public Dictionary<int, int> AttackingPawnDic { get; set; }
        public Dictionary<Pawn, IntVec3> AttackingPawnsLastPos { get; set; }
        public Dictionary<int, AttackPawnCommand> AttackingPawnJobDic { get; set; }

        private readonly object ToSendListsSync = new object();

        public long AttackUpdateTick { get; set; }
        private Map GameMap { get; set; }
        private bool _isTemporaryCaravanMap = false;
        private readonly HashSet<int> _hostCaravanInitialItemIds = new HashSet<int>();

        private readonly Dictionary<int, Thing> ThingPrepareChange2 = new Dictionary<int, Thing>(16);

        private HashSet<Thing> ThingPrepareChange1 = new HashSet<Thing>();
        private HashSet<Thing> ThingPrepareChange0 = new HashSet<Thing>();

        public DateTime CurrentPauseToTime;
        public bool IsPause => CurrentPauseToTime > (DateTime.UtcNow + SessionClientController.ServerTimeDelta);

        public bool HostPawnMoveSpeedActive => false;

        public DateTime TimeStartGameAttack = DateTime.MinValue;
        private DateTime CheckVictoryTime = DateTime.MaxValue;
        private DateTime EdgeRetreatStartTime = DateTime.MaxValue;

        public bool? ConfirmedVictoryAttacker { get; set; }
        public bool TerribleFatalError { get; set; }

        public bool WaitOrErrorExit { get; set; }
        private DateTime WaitOrErrorExitStart { get; set; }

        private readonly Dictionary<int, Pawn> AllPawns = new Dictionary<int, Pawn>(64);

        // Reusable buffers to avoid GC allocations every 50ms
        private readonly List<ThingEntry> _newPawnsBuffer = new List<ThingEntry>(16);
        private readonly List<int> _newPawnsIdBuffer = new List<int>(16);
        private readonly List<ThingTrade> _newThingsBuffer = new List<ThingTrade>(32);
        private readonly List<int> _newThingsIdBuffer = new List<int>(32);
        private readonly List<AttackCorpse> _newCorpsesBuffer = new List<AttackCorpse>(16);
        private readonly List<AttackThingState> _toSendStateBuffer = new List<AttackThingState>(64);
        private readonly List<int> _deleteBuffer = new List<int>(16);
        private readonly List<int> _addedIdBuffer = new List<int>(16);

        // Lockstep command queues
        private readonly List<AttackPawnCommand> _outgoingHostCommands = new List<AttackPawnCommand>(32);
        private readonly List<AttackPawnCommand> _scheduledCommands = new List<AttackPawnCommand>(64);
        private readonly object _commandsSync = new object();

        private bool _localSceneReady = false;
        private int _battleSeed = 987654321;

        public static GameAttackHost Get => SessionClientController.Data.AttackUsModule;

        private object TimerObj;
        private bool InTimer;

        public static bool AttackMessage()
        {
            if (SessionClientController.Data.AttackModule == null
                && SessionClientController.Data.AttackUsModule == null)
            {
                SessionClientController.Data.AttackUsModule = new GameAttackHost();
                SessionClientController.Data.BackgroundSaveGameOff = true;
                return true;
            }
            return false;
        }

        private string GetHostDialogTitle()
        {
            if (TestMode)
            {
                return "OCity_GameAttack_Host_Test_Attack".Translate(AttackerLogin);
            }
            if (_isTemporaryCaravanMap && "OCity_GameAttack_Host_Caravan_Attacking".CanTranslate())
            {
                return "OCity_GameAttack_Host_Caravan_Attacking".Translate(AttackerLogin);
            }
            return "OCity_GameAttack_Host_Settlement_Attacking".Translate(AttackerLogin);
        }

        private void PauseMessage()
        {
            ModBaseData.RunMainThread(() =>
            {
                string surrenderText = (_isTemporaryCaravanMap && "OCity_GameAttack_Host_Surrender_Caravan".CanTranslate())
                    ? "OCity_GameAttack_Host_Surrender_Caravan".Translate().ToString()
                    : "OCity_GameAttack_Host_Surrender".Translate().ToString();

                GameUtils.ShowDialodOKCancel(
                    GetHostDialogTitle(),
                    TestMode
                        ? "OCity_GameAttack_Host_GameSpeed_Lock_Dialog".Translate() + Environment.NewLine
                            + "OCity_GameAttack_Host_Cancel_Action".Translate() + Environment.NewLine
                            + surrenderText
                        : "OCity_GameAttack_Host_GameSpeed_Lock_Dialog".Translate() + Environment.NewLine
                            + "OCity_GameAttack_Host_GameSpeed_Lock_Dialog2".Translate() + Environment.NewLine
                            + surrenderText,
                    () => { },
                    null
                );
            });
        }

        public void Start(SessionClient connect)
        {
            Find.TickManager.Pause();
            SessionClientController.Data.DontCheckTimerFail = true;

            Loger.Log("Client GameAttackHost Start 1");
            var toClient = connect.AttackOnlineHost(new AttackHostToSrv { State = 2 });
            if (!string.IsNullOrEmpty(connect.ErrorMessage))
            {
                ErrorBreak(connect.ErrorMessage?.ServerTranslate());
                return;
            }

            HostPlaceServerId = toClient.HostPlaceServerId;
            InitiatorPlaceServerId = toClient.InitiatorPlaceServerId;
            AttackerLogin = toClient.StartInitiatorPlayer;
            TestMode = toClient.TestMode;
            _battleSeed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            if (_battleSeed == 0) _battleSeed = 1936507579;
            LockstepController.Reset(_battleSeed);
            _localSceneReady = false;
            _isTemporaryCaravanMap = false;
            _hostCaravanInitialItemIds.Clear();

            Loger.Log($"Client GameAttackHost Start 2 {HostPlaceServerId} TestMode={TestMode} Seed={_battleSeed}");

            LongEventHandler.QueueLongEvent(() =>
            {
                SessionClientController.SaveGameNowInEvent(false);

                Find.TickManager.Pause();
                GameAttackTrigger_Patch.ForceSpeed = 0f;

                CurrentPauseToTime = DateTime.UtcNow + SessionClientController.ServerTimeDelta + TimeSpan.FromMinutes(5);
                Loger.Log("HostAttackUpdate Set 1 CurrentPauseToTime=" + CurrentPauseToTime.ToGoodUtcString());

                var hostWorldObj = UpdateWorldController.GetWOByServerId(HostPlaceServerId);
                if (hostWorldObj is MapParent hostPlace && hostPlace.Map != null)
                {
                    _isTemporaryCaravanMap = false;
                    GameMap = hostPlace.Map;
                }
                else if (hostWorldObj is Caravan hostCaravan)
                {
                    _isTemporaryCaravanMap = true;
                    int caravanTile = hostCaravan.Tile;

                    var caravanPawns = hostCaravan.PawnsListForReading;
                    for (int i = 0; i < caravanPawns.Count; i++)
                    {
                        var cp = caravanPawns[i];
                        if (cp == null) continue;
                        foreach (var item in cp.EquippedWornOrInventoryThings)
                        {
                            if (item != null) _hostCaravanInitialItemIds.Add(item.thingIDNumber);
                        }
                    }

                    Rand.PushState();
                    try
                    {
                        TileFinder_IsValidTileForNewSettlement_Patch.Off = true;
                        Rand.Seed = Gen.HashCombineInt(Find.World.info.Seed, caravanTile);

                        var ambushParent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Ambush);
                        ambushParent.Tile = caravanTile;
                        Find.WorldObjects.Add(ambushParent);
                        ambushParent.SetFaction(Faction.OfPlayer);

                        GameMap = MapGenerator.GenerateMap(new IntVec3(150, 1, 150), ambushParent, ambushParent.MapGeneratorDef, ambushParent.ExtraGenStepDefs, null);
                        CaravanEnterMapUtility.Enter(hostCaravan, GameMap, CaravanEnterMode.Center, CaravanDropInventoryMode.DoNotDrop, draftColonists: true);
                    }
                    finally
                    {
                        TileFinder_IsValidTileForNewSettlement_Patch.Off = false;
                        Rand.PopState();
                    }
                }
                else
                {
                    ErrorBreak("Target WorldObject not found for PlaceServerId=" + HostPlaceServerId);
                    return;
                }

                PauseMessage();

                var toSrvMap = new AttackHostToSrv
                {
                    State = 4,
                    BattleSeed = _battleSeed,
                    TerrainDefNameCell = new List<IntVec3S>(GameMap.cellIndices.NumGridCells),
                    TerrainDefName = new List<string>(GameMap.cellIndices.NumGridCells),
                    Thing = new List<ThingTrade>(2048),
                    ThingCell = new List<IntVec3S>(2048)
                };

                toSrvMap.MapSize = new IntVec3S(GameMap.Size);

                CellRect cellRect = CellRect.WholeMap(GameMap);
                cellRect.ClipInsideMap(GameMap);

                SendedActual = new Dictionary<int, Thing>(2048);
                FireList = new HashSet<Thing>();

                foreach (IntVec3 current in cellRect)
                {
                    var terr = GameMap.terrainGrid.TerrainAt(current);
                    toSrvMap.TerrainDefNameCell.Add(new IntVec3S(current));
                    toSrvMap.TerrainDefName.Add(terr.defName);

                    var thingsAtCell = GameMap.thingGrid.ThingsListAt(current);
                    for (int t = 0; t < thingsAtCell.Count; t++)
                    {
                        var thc = thingsAtCell[t];
                        if (thc is Pawn || thc is Corpse || thc is Blueprint || thc is Frame || thc is UnfinishedThing || thc is Filth || thc is Mote)
                            continue;
                        if (thc is Fire fire) FireList.Add(fire);

                        var isPlant = thc.def.category == ThingCategory.Plant && !thc.def.plant.IsTree;
                        if (PlantNotSend && isPlant) continue;

                        if (thc.Position != current) continue;

                        var tt = ThingTrade.CreateTrade(thc, thc.stackCount, false);
                        toSrvMap.ThingCell.Add(new IntVec3S(current));
                        toSrvMap.Thing.Add(tt);

                        if (!isPlant) SendedActual[thc.thingIDNumber] = thc;
                    }
                }

                SessionClientController.Command((connect0) =>
                {
                    connect0.AttackOnlineHost(toSrvMap);
                    if (!string.IsNullOrEmpty(connect0.ErrorMessage))
                    {
                        ErrorBreak(connect0.ErrorMessage?.ServerTranslate());
                        return;
                    }

                    AttackHostFromSrv fromClient2 = null;
                    int waitCount = 0;
                    while (waitCount++ < 60 * 2)
                    {
                        Thread.Sleep(500);
                        fromClient2 = connect0.AttackOnlineHost(new AttackHostToSrv { State = 5 });
                        if (!string.IsNullOrEmpty(connect0.ErrorMessage))
                        {
                            ErrorBreak(connect0.ErrorMessage?.ServerTranslate());
                            return;
                        }
                        if (fromClient2.Pawns != null && fromClient2.Pawns.Count > 0) break;
                    }

                    if (fromClient2?.Pawns == null || fromClient2.Pawns.Count == 0)
                    {
                        ErrorBreak("Wait timeout (code 5)");
                        return;
                    }

                    SendedPawnsId = new HashSet<int>();
                    SendedState = new Dictionary<int, int>();
                    SendedXP = new Dictionary<int, int>();
                    ToUpdateStateId = new HashSet<int>();
                    ToUpdateState = new List<Thing>();
                    ToSendDeleteId = new HashSet<int>();
                    ToSendAddId = new HashSet<int>();
                    ToSendThingAdd = new HashSet<Thing>();
                    ToSendDelayedFillPawnsId = new HashSet<int>();
                    SendDelayedFillPawnsLastTime = DateTime.UtcNow;

                    AttackingPawns = new HashSet<Pawn>();
                    AttackingPawnDic = new Dictionary<int, int>();
                    AttackingPawnsLastPos = new Dictionary<Pawn, IntVec3>();
                    AttackingPawnJobDic = new Dictionary<int, AttackPawnCommand>();

                    AllPawns.Clear();

                    var mainCann = GameUtils.SpawnCaravanPirate(GameMap, fromClient2.Pawns, (thin, thingEntry) =>
                    {
                        if (thin is Pawn pawn)
                        {
                            int origId = thingEntry.OriginalID != 0 ? thingEntry.OriginalID : pawn.thingIDNumber;
                            AllPawns[pawn.thingIDNumber] = pawn;
                            ToSendAddId.Add(pawn.thingIDNumber);
                            AttackingPawns.Add(pawn);
                            AttackingPawnDic[pawn.thingIDNumber] = origId;
                            AttackingPawnsLastPos[pawn] = pawn.Position;

                            if (!pawn.Dead && !pawn.Downed && pawn.jobs != null)
                            {
                                if (pawn.jobs.jobQueue == null) pawn.jobs.jobQueue = new JobQueue();
                                pawn.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                                {
                                    playerForced = true,
                                    expiryInterval = int.MaxValue,
                                    checkOverrideOnExpire = false,
                                }, JobCondition.InterruptForced);
                            }
                        }
                    });

                    var mapPawns = GameMap.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < mapPawns.Count; i++)
                    {
                        var p = mapPawns[i];
                        if (!AllPawns.ContainsKey(p.thingIDNumber))
                        {
                            AllPawns[p.thingIDNumber] = p;
                            ToSendAddId.Add(p.thingIDNumber);
                        }
                    }

                    ModBaseData.RunMainThreadSync(() =>
                    {
                        CameraJumper.TryJump(new GlobalTargetInfo(mainCann, GameMap));
                    });

                    GameAttackTrigger_Patch.ActiveAttackHost[GameMap] = this;
                    _localSceneReady = true;

                    TimerObj = SessionClientController.Timers.Add(AttackUpdateDelay, AttackUpdate);
                });
            }, "OCity_GameAttack_Host_Loading", false, null);
        }

        private void ErrorBreak(string msg)
        {
            Loger.Log("Client GameAttackHost ErrorBreak: " + msg, Loger.LogLevel.ERROR);
            Clear();
            ModBaseData.RunMainThread(() =>
            {
                Find.WindowStack.Add(new Dialog_MessageBox(msg));
            });
        }

        public void Clear()
        {
            if (TimerObj != null) SessionClientController.Timers.Remove(TimerObj);
            TimerObj = null;

            if (GameMap != null) GameAttackTrigger_Patch.ActiveAttackHost.Remove(GameMap);
            LockstepController.Reset(0);

            SessionClientController.Data.AttackUsModule = null;
            SessionClientController.Data.BackgroundSaveGameOff = false;
            SessionClientController.Data.DontCheckTimerFail = false;
            GameAttackTrigger_Patch.ForceSpeed = -1f;
        }

        private void StartStrandedAttackersFlee(List<Pawn> strandedPawns)
        {
            if (strandedPawns == null || strandedPawns.Count == 0 || GameMap == null) return;

            try
            {
                Faction pirateFaction = strandedPawns[0].Faction
                    ?? SessionClientController.Data?.FactionPirate
                    ?? Find.FactionManager?.FirstFactionOfDef(FactionDefOf.Pirate);

                Lord fleeLord = null;
                if (pirateFaction != null)
                {
                    fleeLord = LordMaker.MakeNewLord(
                        pirateFaction,
                        new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true),
                        GameMap);
                }

                for (int i = 0; i < strandedPawns.Count; i++)
                {
                    var p = strandedPawns[i];
                    if (p == null || p.Dead || p.Destroyed || !p.Spawned) continue;

                    lock (RetreatingPawnsToOwner)
                    {
                        RetreatingPawnsToOwner[p.thingIDNumber] = new RetreatingPawnInfo
                        {
                            AttackerLogin = AttackerLogin,
                            Tile = GameMap.Tile
                        };
                    }

                    if (p.drafter != null && p.drafter.Drafted)
                    {
                        p.drafter.Drafted = false;
                    }

                    var oldLord = p.GetLord();
                    if (oldLord != null && oldLord != fleeLord)
                    {
                        oldLord.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
                    }
                    fleeLord?.AddPawn(p);

                    p.jobs?.StopAll();

                    if (!p.Downed && RCellFinder.TryFindBestExitSpot(p, out IntVec3 exitSpot))
                    {
                        var exitJob = JobMaker.MakeJob(JobDefOf.Goto, exitSpot);
                        exitJob.exitMapOnArrival = true;
                        exitJob.locomotionUrgency = LocomotionUrgency.Jog;
                        exitJob.expiryInterval = 2000;
                        exitJob.canBashDoors = true;
                        p.jobs?.StartJob(exitJob, JobCondition.InterruptForced);
                    }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("StartStrandedAttackersFlee warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        /// <summary>
        /// Forces all visiting guests, traders, and NPC caravans on the host map to flee off the map immediately when host surrenders.
        /// </summary>
        private void StartVisitorsFlee()
        {
            if (GameMap == null) return;
            try
            {
                var allPawns = GameMap.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    var p = allPawns[i];
                    if (p == null || p.Dead || p.Downed || !p.Spawned) continue;
                    if (p.IsColonist || (AttackingPawns != null && AttackingPawns.Contains(p))) continue;

                    // If NPC guest, visitor, or guest caravan member
                    if (p.RaceProps != null && p.RaceProps.Humanlike && p.Faction != Faction.OfPlayer)
                    {
                        if (p.drafter != null && p.drafter.Drafted) p.drafter.Drafted = false;
                        p.jobs?.StopAll();
                        if (!p.Downed && RCellFinder.TryFindBestExitSpot(p, out IntVec3 exitSpot))
                        {
                            var exitJob = JobMaker.MakeJob(JobDefOf.Goto, exitSpot);
                            exitJob.exitMapOnArrival = true;
                            exitJob.locomotionUrgency = LocomotionUrgency.Jog;
                            exitJob.expiryInterval = 2000;
                            p.jobs?.StartJob(exitJob, JobCondition.InterruptForced);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("StartVisitorsFlee warning: " + ex.Message, Loger.LogLevel.WARNING);
            }
        }

        public void Finish(bool victoryAttacker)
        {
            if (ConfirmedVictoryAttacker == null)
            {
                ConfirmedVictoryAttacker = victoryAttacker;
                try
                {
                    SessionClientController.Command((connect) =>
                    {
                        connect.AttackOnlineHost(new AttackHostToSrv
                        {
                            State = 10,
                            BattleSeed = _battleSeed,
                            CurrentBattleTick = LockstepController.LocalBattleTick,
                            HostReady = _localSceneReady,
                            VictoryAttacker = victoryAttacker
                        });
                    });
                }
                catch { }
            }

            ModBaseData.RunMainThreadSync(() =>
            {
                if (SessionClientController.Data?.AttackUsModule != this) return;

                Find.TickManager.Pause();
                Clear();

                if (TestMode)
                {
                    GameUtils.ShowDialodOKCancel(
                        GetHostDialogTitle(),
                        victoryAttacker
                            ? "Ocity_GameAttacker_TrainingFight_Lost".Translate() + Environment.NewLine + "OCity_GameAttack_Host_Card_Restored".Translate()
                            : "OCity_GameAttack_Host_Training_Attack_Repulsed".Translate() + Environment.NewLine + "OCity_GameAttack_Host_Card_Restored".Translate(),
                        () => SessionClientController.Disconnected("OCity_GameAttacker_Done".Translate()),
                        null
                    );
                    return;
                }

                string finishBodyText;
                if (victoryAttacker)
                {
                    finishBodyText = _isTemporaryCaravanMap && "OCity_GameAttack_Host_Caravan_Defeated".CanTranslate()
                        ? "OCity_GameAttack_Host_Caravan_Defeated".Translate().ToString()
                        : "OCity_GameAttack_Host_Caravan_TransferToNewOwner".Translate().ToString();
                }
                else
                {
                    finishBodyText = _isTemporaryCaravanMap && "OCity_GameAttack_Host_Caravan_Attack_Repulsed".CanTranslate()
                        ? "OCity_GameAttack_Host_Caravan_Attack_Repulsed".Translate().ToString()
                        : ("OCity_GameAttack_Host_Atack_Repulsed".Translate() + Environment.NewLine + "OCity_GameAttack_Host_Stranded_EnemiesLostCommander_Touch".Translate()).ToString();
                }

                Action saveAndShowFinishDialog = () =>
                {
                    SessionClientController.SaveGameNow(true, () =>
                    {
                        ModBaseData.RunMainThread(() =>
                        {
                            GameUtils.ShowDialodOKCancel(
                                GetHostDialogTitle(),
                                finishBodyText,
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
                };

                if (_isTemporaryCaravanMap && GameMap != null)
                {
                    if (!victoryAttacker)
                    {
                        if (AttackingPawns != null)
                        {
                            foreach (var enemy in AttackingPawns)
                            {
                                if (enemy != null && !enemy.Dead && !enemy.Downed &&
                                    (enemy.Position.x < MapBorder || enemy.Position.x > GameMap.Size.x - 1 - MapBorder
                                     || enemy.Position.z < MapBorder || enemy.Position.z > GameMap.Size.z - 1 - MapBorder))
                                {
                                    GameUtils.PawnDestroy(enemy);
                                }
                            }
                        }

                        GameUtils.OpenCaravanLootSelectionDialog(GameMap, _hostCaravanInitialItemIds, saveAndShowFinishDialog, AttackerLogin);
                    }
                    else
                    {
                        var remainingPawns = new List<Pawn>(GameMap.mapPawns.AllPawns);
                        for (int i = 0; i < remainingPawns.Count; i++)
                        {
                            GameUtils.PawnDestroy(remainingPawns[i]);
                        }

                        try { CameraJumper.TryShowWorld(); } catch { }
                        GameUtils.DeinitAndRemoveMapSafe(GameMap);
                        saveAndShowFinishDialog();
                    }
                }
                else if (victoryAttacker)
                {
                    // Host surrendered settlement: make all visitors/caravans flee immediately
                    StartVisitorsFlee();

                    try { CameraJumper.TryShowWorld(); } catch { }
                    GameUtils.DeinitAndRemoveMapSafe(GameMap);
                    saveAndShowFinishDialog();
                }
                else if (AttackingPawns != null && GameMap != null)
                {
                    var strandedPawnsToFlee = new List<Pawn>();

                    foreach (var pawn in AttackingPawns)
                    {
                        if (pawn == null || pawn.Dead || pawn.Destroyed) continue;
                        strandedPawnsToFlee.Add(pawn);
                    }

                    StartStrandedAttackersFlee(strandedPawnsToFlee);
                    saveAndShowFinishDialog();
                }
                else
                {
                    saveAndShowFinishDialog();
                }
            });
        }

        public void ControlPawnMoveSpeed(Pawn pawn, ref int speed)
        {
        }

        private bool? CheckAttackerVictory()
        {
            bool existHostPawn = false;
            bool existAttackerPawn = false;
            bool allAliveAttackersInRetreatZone = true;
            bool anyAttackerAtMapEdge = false;

            foreach (var kvp in AllPawns)
            {
                var pawn = kvp.Value;
                if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned) continue;

                if (pawn.RaceProps != null && pawn.RaceProps.Humanlike
                    && AttackingPawnDic.ContainsKey(pawn.thingIDNumber))
                {
                    existAttackerPawn = true;

                    bool inBorder = pawn.Position.x < MapBorder || pawn.Position.x > GameMap.Size.x - 1 - MapBorder
                        || pawn.Position.z < MapBorder || pawn.Position.z > GameMap.Size.z - 1 - MapBorder;

                    if (!inBorder)
                    {
                        allAliveAttackersInRetreatZone = false;
                    }
                    else if (pawn.Position.x <= 3 || pawn.Position.x >= GameMap.Size.x - 4
                        || pawn.Position.z <= 3 || pawn.Position.z >= GameMap.Size.z - 4)
                    {
                        anyAttackerAtMapEdge = true;
                    }
                }

                if (!existHostPawn && pawn.IsColonist && !AttackingPawnDic.ContainsKey(pawn.thingIDNumber))
                {
                    existHostPawn = true;
                }
            }

            if (existHostPawn && existAttackerPawn)
            {
                if (LockstepController.LocalBattleTick > 600 && allAliveAttackersInRetreatZone && anyAttackerAtMapEdge)
                {
                    if (EdgeRetreatStartTime == DateTime.MaxValue)
                    {
                        EdgeRetreatStartTime = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - EdgeRetreatStartTime).TotalSeconds >= 3.0)
                    {
                        return false;
                    }
                }
                else
                {
                    EdgeRetreatStartTime = DateTime.MaxValue;
                }

                return null;
            }

            EdgeRetreatStartTime = DateTime.MaxValue;
            return existHostPawn && existAttackerPawn ? (bool?)null : existAttackerPawn;
        }

        private void CheckVictory()
        {
            if (IsPause || ConfirmedVictoryAttacker != null) return;
            if (TimeStartGameAttack == DateTime.MinValue) return;
            if ((DateTime.UtcNow - TimeStartGameAttack).TotalSeconds < 5) return;

            var vic = CheckAttackerVictory();
            if (vic == null)
            {
                if (CheckVictoryTime != DateTime.MaxValue) CheckVictoryTime = DateTime.MaxValue;
            }
            else
            {
                if (CheckVictoryTime == DateTime.MaxValue)
                {
                    CheckVictoryTime = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - CheckVictoryTime).TotalSeconds > CheckVictoryDelay)
                {
                    ConfirmedVictoryAttacker = vic;
                    Loger.Log("Client Host CheckVictory ConfirmedVictoryAttacker=" + ConfirmedVictoryAttacker.Value);
                }
            }
        }

        private void AttackUpdate()
        {
            if (InTimer) return;
            InTimer = true;
            bool inTimerEvent = false;
            try
            {
                if (SessionClientController.Data?.AttackUsModule != this)
                {
                    InTimer = false;
                    return;
                }

                if (WaitOrErrorExit)
                {
                    if ((DateTime.UtcNow - WaitOrErrorExitStart).TotalSeconds > 20)
                    {
                        Clear();
                    }
                    InTimer = false;
                    return;
                }

                if (LongEventHandler.AnyEventNowOrWaiting)
                {
                    InTimer = false;
                    return;
                }

                AttackUpdateTick++;

                if (IsPause || !_localSceneReady || !LockstepController.RemoteReady)
                {
                    GameAttackTrigger_Patch.ForceSpeed = 0f;
                    if (Find.TickManager != null && !Find.TickManager.Paused)
                    {
                        ModBaseData.RunMainThread(() =>
                        {
                            if (SessionClientController.Data?.AttackUsModule != this) return;
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
                    if (TimeStartGameAttack == DateTime.MinValue)
                    {
                        TimeStartGameAttack = DateTime.UtcNow;
                        SessionClientController.Data.DontCheckTimerFail = false;
                    }
                    GameAttackTrigger_Patch.ForceSpeed = TickTimeSpeed;
                    if (Find.TickManager != null && (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed != TimeSpeed.Normal))
                    {
                        ModBaseData.RunMainThread(() =>
                        {
                            if (SessionClientController.Data?.AttackUsModule != this) return;
                            if (Find.TickManager != null)
                            {
                                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                                GameAttackTrigger_Patch.ForceSpeed = TickTimeSpeed;
                            }
                        });
                    }
                }

                _newPawnsBuffer.Clear();
                _newPawnsIdBuffer.Clear();
                _newThingsBuffer.Clear();
                _newThingsIdBuffer.Clear();
                _newCorpsesBuffer.Clear();
                _toSendStateBuffer.Clear();
                _deleteBuffer.Clear();
                _addedIdBuffer.Clear();

                List<AttackPawnCommand> hostCmdsToSend = null;
                lock (_commandsSync)
                {
                    if (_outgoingHostCommands.Count > 0)
                    {
                        hostCmdsToSend = new List<AttackPawnCommand>(_outgoingHostCommands);
                        _outgoingHostCommands.Clear();
                    }
                }

                ModBaseData.RunMainThreadSync(() =>
                {
                    CheckVictory();

                    var currentPawns = GameMap.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < currentPawns.Count; i++)
                    {
                        var p = currentPawns[i];
                        if (!AllPawns.ContainsKey(p.thingIDNumber))
                        {
                            AllPawns[p.thingIDNumber] = p;
                            lock (ToSendListsSync)
                            {
                                ToSendAddId.Add(p.thingIDNumber);
                            }
                        }
                    }

                    lock (ToSendListsSync)
                    {
                        var tempSet = ThingPrepareChange0;
                        ThingPrepareChange0 = ThingPrepareChange1;
                        ThingPrepareChange1 = tempSet;
                        ThingPrepareChange1.Clear();

                        foreach (var th in ThingPrepareChange0)
                        {
                            if (th == null) continue;
                            if (th.Destroyed || !th.Spawned)
                            {
                                if (SendedActual.ContainsKey(th.thingIDNumber))
                                {
                                    ToSendDeleteId.Add(th.thingIDNumber);
                                }
                            }
                            else
                            {
                                if (th is Pawn)
                                {
                                    if (!SendedPawnsId.Contains(th.thingIDNumber))
                                    {
                                        ToSendAddId.Add(th.thingIDNumber);
                                    }
                                }
                                else
                                {
                                    ToSendThingAdd.Add(th);
                                }
                            }
                        }
                        ThingPrepareChange0.Clear();

                        if (ToSendAddId.Count > 0)
                        {
                            int cnt = !LockstepController.RemoteReady || ToSendAddId.Count < 3 || ToSendAddId.Count > 6 || AttackUpdateTick <= 2
                                ? ToSendAddId.Count
                                : 3;
                            foreach (var id in ToSendAddId)
                            {
                                if (cnt-- <= 0) break;
                                _addedIdBuffer.Add(id);
                                if (!AllPawns.TryGetValue(id, out var pawn) || pawn == null) continue;

                                if (pawn.Dead || pawn.Destroyed || !pawn.Spawned)
                                {
                                    if (SendedPawnsId.Contains(id))
                                    {
                                        SendedPawnsId.Remove(id);
                                        if (pawn.Corpse != null && pawn.Corpse.Spawned && !SendedActual.ContainsKey(pawn.Corpse.thingIDNumber))
                                        {
                                            _newCorpsesBuffer.Add(new AttackCorpse
                                            {
                                                CorpseWithPawn = ThingEntry.CreateEntry(pawn.Corpse, 1),
                                                CorpseId = pawn.Corpse.thingIDNumber,
                                                PawnId = id
                                            });
                                            SendedActual[pawn.Corpse.thingIDNumber] = pawn.Corpse;
                                        }
                                        else if (pawn.Corpse == null || !pawn.Corpse.Spawned)
                                        {
                                            _deleteBuffer.Add(id);
                                        }
                                    }
                                    continue;
                                }

                                SendedPawnsId.Add(id);
                                var sendPawn = ThingEntry.CreateEntry(pawn, 1);
                                if (AttackingPawnDic.TryGetValue(id, out int origId))
                                {
                                    sendPawn.OriginalID = origId;
                                    sendPawn.TransportID = origId;
                                }
                                else
                                {
                                    sendPawn.OriginalID = id;
                                    sendPawn.TransportID = 0;
                                }
                                _newPawnsBuffer.Add(sendPawn);
                                _newPawnsIdBuffer.Add(id);
                            }

                            for (int i = 0; i < _addedIdBuffer.Count; i++)
                            {
                                ToSendAddId.Remove(_addedIdBuffer[i]);
                            }
                        }

                        if (ToSendThingAdd.Count > 0)
                        {
                            foreach (var th in ToSendThingAdd)
                            {
                                if (th == null) continue;
                                if (th.Destroyed || !th.Spawned)
                                {
                                    if (SendedActual.ContainsKey(th.thingIDNumber))
                                    {
                                        ToSendDeleteId.Add(th.thingIDNumber);
                                    }
                                    continue;
                                }

                                if (th is Corpse corpse && corpse.InnerPawn != null)
                                {
                                    if (SendedActual.ContainsKey(corpse.thingIDNumber)) continue;
                                    SendedPawnsId.Remove(corpse.InnerPawn.thingIDNumber);
                                    _newCorpsesBuffer.Add(new AttackCorpse
                                    {
                                        CorpseWithPawn = ThingEntry.CreateEntry(corpse, 1),
                                        CorpseId = corpse.thingIDNumber,
                                        PawnId = corpse.InnerPawn.thingIDNumber
                                    });
                                    SendedActual[corpse.thingIDNumber] = corpse;
                                    continue;
                                }

                                SendedActual[th.thingIDNumber] = th;
                                _newThingsBuffer.Add(ThingTrade.CreateTrade(th, th.stackCount, false));
                                _newThingsIdBuffer.Add(th.thingIDNumber);
                            }
                            ToSendThingAdd.Clear();
                        }

                        if (ToSendDeleteId.Count > 0)
                        {
                            foreach (var id in ToSendDeleteId)
                            {
                                SendedPawnsId.Remove(id);
                                SendedActual.Remove(id);
                                SendedState.Remove(id);
                                _deleteBuffer.Add(id);
                            }
                            ToSendDeleteId.Clear();
                        }
                    }

                    foreach (var p in AllPawns.Values)
                    {
                        if (p == null) continue;
                        int id = p.thingIDNumber;
                        if (!SendedPawnsId.Contains(id)) continue;

                        if (p.Dead || p.Destroyed || !p.Spawned)
                        {
                            lock (ToSendListsSync)
                            {
                                ToSendAddId.Add(id);
                            }
                            continue;
                        }

                        var hash = AttackThingState.GetHash(p);
                        if (!SendedState.TryGetValue(id, out int oldHash) || oldHash != hash)
                        {
                            SendedState[id] = hash;
                            _toSendStateBuffer.Add(new AttackThingState(p));
                        }
                    }

                    lock (ToSendListsSync)
                    {
                        if (FireList.Count > 0)
                        {
                            ToUpdateState.AddRange(FireList);
                        }

                        for (int i = 0; i < ToUpdateState.Count; i++)
                        {
                            var th = ToUpdateState[i];
                            if (th == null) continue;
                            int id = th.thingIDNumber;
                            if (th.Destroyed || !th.Spawned)
                            {
                                FireList.Remove(th);
                                if (SendedActual.ContainsKey(id))
                                {
                                    SendedActual.Remove(id);
                                    SendedState.Remove(id);
                                    _deleteBuffer.Add(id);
                                }
                                continue;
                            }

                            var hash = AttackThingState.GetHash(th);
                            if (!SendedState.TryGetValue(id, out int oldHash) || oldHash != hash)
                            {
                                SendedState[id] = hash;
                                _toSendStateBuffer.Add(new AttackThingState(th));
                            }
                        }
                        ToUpdateState.Clear();
                        ToUpdateStateId.Clear();
                    }
                });

                inTimerEvent = true;
                SessionClientController.Command((connect) =>
                {
                    try
                    {
                        var toSrv = new AttackHostToSrv
                        {
                            State = 10,
                            BattleSeed = _battleSeed,
                            CurrentBattleTick = LockstepController.LocalBattleTick,
                            HostReady = _localSceneReady,
                            NewPawns = _newPawnsBuffer.Count > 0 ? new List<ThingEntry>(_newPawnsBuffer) : null,
                            NewPawnsId = _newPawnsIdBuffer.Count > 0 ? new List<int>(_newPawnsIdBuffer) : null,
                            NewThings = _newThingsBuffer.Count > 0 ? new List<ThingTrade>(_newThingsBuffer) : null,
                            NewThingsId = _newThingsIdBuffer.Count > 0 ? new List<int>(_newThingsIdBuffer) : null,
                            NewCorpses = _newCorpsesBuffer.Count > 0 ? new List<AttackCorpse>(_newCorpsesBuffer) : null,
                            Delete = _deleteBuffer.Count > 0 ? new List<int>(_deleteBuffer) : null,
                            UpdateState = _toSendStateBuffer.Count > 0 ? new List<AttackThingState>(_toSendStateBuffer) : null,
                            HostCommands = hostCmdsToSend,
                            VictoryAttacker = ConfirmedVictoryAttacker
                        };

                        var toClient = connect.AttackOnlineHost(toSrv);
                        if (!string.IsNullOrEmpty(connect.ErrorMessage))
                        {
                            Loger.Log("HostAttackUpdate Network Error: " + connect.ErrorMessage, Loger.LogLevel.ERROR);
                        }

                        if (toClient != null)
                        {
                            if (toClient.AttackerBattleTick >= 0)
                            {
                                LockstepController.UpdateRemoteTick(toClient.AttackerBattleTick);
                            }
                            if (toClient.AttackerReady)
                            {
                                LockstepController.RemoteReady = true;
                            }

                            if (toClient.SetPauseOnTime > DateTime.MinValue)
                            {
                                CurrentPauseToTime = toClient.SetPauseOnTime;
                                Loger.Log("HostAttackUpdate SetPauseOnTime=" + CurrentPauseToTime.ToGoodUtcString());
                            }

                            if (toClient.TerribleFatalError)
                            {
                                TerribleFatalError = true;
                                Clear();
                                return;
                            }

                            if (toClient.VictoryHost)
                            {
                                ConfirmedVictoryAttacker = false;
                                connect.AttackOnlineHost(new AttackHostToSrv
                                {
                                    State = 10,
                                    BattleSeed = _battleSeed,
                                    CurrentBattleTick = LockstepController.LocalBattleTick,
                                    HostReady = _localSceneReady,
                                    VictoryAttacker = false
                                });
                            }

                            if (ConfirmedVictoryAttacker != null)
                            {
                                Finish(ConfirmedVictoryAttacker.Value);
                                return;
                            }

                            if (toClient.UpdateCommand != null && toClient.UpdateCommand.Count > 0)
                            {
                                ModBaseData.RunMainThread(() =>
                                {
                                    bool prevDisable = UIEventNewJobDisable;
                                    UIEventNewJobDisable = true;
                                    try
                                    {
                                        for (int i = 0; i < toClient.UpdateCommand.Count; i++)
                                        {
                                            var comm = toClient.UpdateCommand[i];
                                            if (comm == null) continue;
                                            if (!AllPawns.TryGetValue(comm.HostPawnID, out Pawn pawn) || pawn == null) continue;

                                            AttackingPawnJobDic[comm.HostPawnID] = comm;
                                            if (comm.ScheduledTick > LockstepController.LocalBattleTick)
                                            {
                                                lock (_commandsSync)
                                                {
                                                    _scheduledCommands.Add(comm);
                                                }
                                            }
                                            else
                                            {
                                                ApplyAttackingPawnJob(pawn, comm);
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        UIEventNewJobDisable = prevDisable;
                                    }
                                });
                            }

                            if (toClient.NeedNewThingIDs != null && toClient.NeedNewThingIDs.Count > 0)
                            {
                                lock (ToSendListsSync)
                                {
                                    for (int idx = 0; idx < toClient.NeedNewThingIDs.Count; idx++)
                                    {
                                        var id = toClient.NeedNewThingIDs[idx];
                                        if (AllPawns.ContainsKey(id))
                                        {
                                            ToSendAddId.Add(id);
                                            continue;
                                        }
                                        if (SendedActual.TryGetValue(id, out Thing thing) && !(thing is Pawn))
                                        {
                                            ToSendThingAdd.Add(thing);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ext)
                    {
                        Loger.Log("HostAttackUpdate Event Exception: " + ext.Message, Loger.LogLevel.ERROR);
                    }
                    finally
                    {
                        InTimer = false;
                    }
                });

                if (MainHelper.DebugMode || AttackUpdateTick % 20 == 0)
                {
                    Loger.Log($"HostAttackUpdate Tick #{AttackUpdateTick} LocalTick={LockstepController.LocalBattleTick} RemoteTick={LockstepController.RemoteBattleTick} Ready={_localSceneReady}/{LockstepController.RemoteReady}");
                }
            }
            catch (Exception ext)
            {
                Loger.Log("HostAttackUpdate Exception: " + ext.Message, Loger.LogLevel.ERROR);
            }
            if (!inTimerEvent) InTimer = false;
        }

        public void ExecuteScheduledCommandsForTick(long currentTick)
        {
            List<AttackPawnCommand> toRun = null;
            lock (_commandsSync)
            {
                for (int i = _scheduledCommands.Count - 1; i >= 0; i--)
                {
                    var cmd = _scheduledCommands[i];
                    if (cmd == null || cmd.ScheduledTick <= currentTick)
                    {
                        if (cmd != null)
                        {
                            if (toRun == null) toRun = new List<AttackPawnCommand>(8);
                            toRun.Add(cmd);
                        }
                        _scheduledCommands.RemoveAt(i);
                    }
                }
            }

            if (toRun == null || toRun.Count == 0) return;

            bool prevDisable = UIEventNewJobDisable;
            UIEventNewJobDisable = true;
            try
            {
                for (int i = 0; i < toRun.Count; i++)
                {
                    var comm = toRun[i];
                    if (AllPawns.TryGetValue(comm.HostPawnID, out var pawn) && pawn != null)
                    {
                        ApplyAttackingPawnJob(pawn, comm);
                    }
                }
            }
            finally
            {
                UIEventNewJobDisable = prevDisable;
            }
        }

        private class APJBT
        {
            public long Tick;
            public AttackPawnCommand Comm;
        }
        private readonly Dictionary<int, APJBT> ApplyPawnJobByTick = new Dictionary<int, APJBT>(16);

        private void SetPawnJob(
            Pawn pawn,
            JobDef def,
            LocalTargetInfo targetA,
            LocalTargetInfo targetB = default,
            int count = -1,
            string abilityDefName = null,
            LocomotionUrgency urgency = LocomotionUrgency.Jog)
        {
            if (pawn?.jobs == null || def == null || pawn.Dead || pawn.Downed) return;

            if ((def == JobDefOf.Wait_Combat || def == JobDefOf.Wait) && pawn.CurJobDef == def)
            {
                return;
            }

            var job = targetB.IsValid ? new Job(def, targetA, targetB) : new Job(def, targetA);
            job.playerForced = true;
            job.locomotionUrgency = urgency != LocomotionUrgency.None ? urgency : LocomotionUrgency.Jog;
            job.expiryInterval = int.MaxValue;
            job.checkOverrideOnExpire = false;
            if (count > 0) job.count = count;

            if (!string.IsNullOrEmpty(abilityDefName) && pawn.abilities != null)
            {
                var abDef = DefDatabase<AbilityDef>.GetNamedSilentFail(abilityDefName);
                if (abDef != null)
                {
                    var ab = pawn.abilities.GetAbility(abDef);
                    if (ab != null)
                    {
                        job.ability = ab;
                        job.verbToUse = ab.verb;
                    }
                }
            }

            bool prevDisable = UIEventNewJobDisable;
            UIEventNewJobDisable = true;
            try
            {
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            finally
            {
                UIEventNewJobDisable = prevDisable;
            }
        }

        private void ApplyAttackingPawnJob(Pawn pawn, AttackPawnCommand explicitComm = null)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Destroyed || !pawn.Spawned) return;

            bool prevDisable = UIEventNewJobDisable;
            UIEventNewJobDisable = true;

            var pId = pawn.thingIDNumber;
            AttackPawnCommand comm = explicitComm;
            bool stopJob = comm == null && (!AttackingPawnDic.ContainsKey(pId) || !AttackingPawnJobDic.TryGetValue(pId, out comm));

            try
            {
                if (comm != null && comm.Command == AttackPawnCommand.PawnCommand.SetDrafted)
                {
                    if (pawn.drafter != null && pawn.drafter.Drafted != comm.DraftState)
                    {
                        pawn.drafter.Drafted = comm.DraftState;
                    }
                    return;
                }

                if (comm != null && comm.Command == AttackPawnCommand.PawnCommand.SetHostility)
                {
                    if (pawn.playerSettings != null)
                    {
                        pawn.playerSettings.hostilityResponse = (HostilityResponseMode)comm.HostilityMode;
                    }
                    return;
                }

                var tick = (long)Find.TickManager.TicksGame;
                if (!stopJob && explicitComm == null && ApplyPawnJobByTick.TryGetValue(pId, out APJBT check))
                {
                    if (check.Comm == comm && check.Tick == tick)
                    {
                        return;
                    }
                }

                Thing target = null;
                if (!stopJob && comm.TargetID != 0)
                {
                    if (AllPawns.TryGetValue(comm.TargetID, out Pawn pTarget))
                    {
                        target = pTarget;
                    }
                    else if (!SendedActual.TryGetValue(comm.TargetID, out target) || target == null)
                    {
                        stopJob = true;
                    }
                }

                if (!stopJob && !string.IsNullOrEmpty(comm.TargetDefName))
                {
                    if (pawn.inventory?.innerContainer != null)
                    {
                        for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                        {
                            var item = pawn.inventory.innerContainer[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null && pawn.carryTracker?.innerContainer != null)
                    {
                        for (int i = 0; i < pawn.carryTracker.innerContainer.Count; i++)
                        {
                            var item = pawn.carryTracker.innerContainer[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null && pawn.equipment?.AllEquipmentListForReading != null)
                    {
                        for (int i = 0; i < pawn.equipment.AllEquipmentListForReading.Count; i++)
                        {
                            var item = pawn.equipment.AllEquipmentListForReading[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null && pawn.apparel?.WornApparel != null)
                    {
                        for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
                        {
                            var item = pawn.apparel.WornApparel[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null) stopJob = true;
                }

                LocalTargetInfo targetB = LocalTargetInfo.Invalid;
                if (!stopJob)
                {
                    if (comm.TargetIDB != 0)
                    {
                        if (AllPawns.TryGetValue(comm.TargetIDB, out Pawn pTargetB))
                            targetB = pTargetB;
                        else if (SendedActual.TryGetValue(comm.TargetIDB, out Thing thTargetB))
                            targetB = thTargetB;
                    }
                    else if (comm.TargetPosB != null && (comm.TargetPosB.x != 0 || comm.TargetPosB.z != 0))
                    {
                        targetB = comm.TargetPosB.Get();
                    }
                }

                if (!stopJob)
                {
                    LocomotionUrgency syncedUrgency = (comm.HostilityMode > 0 && comm.HostilityMode <= (int)LocomotionUrgency.Sprint)
                        ? (LocomotionUrgency)comm.HostilityMode
                        : LocomotionUrgency.Jog;

                    if (comm.Command == AttackPawnCommand.PawnCommand.Wait_Combat)
                    {
                        stopJob = true;
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.UniversalJob || comm.Command == AttackPawnCommand.PawnCommand.CastAbility)
                    {
                        var jDef = !string.IsNullOrEmpty(comm.JobDefName)
                            ? DefDatabase<JobDef>.GetNamedSilentFail(comm.JobDefName)
                            : null;
                        if (jDef == null || jDef == JobDefOf.Wait_Combat || jDef == JobDefOf.Wait)
                        {
                            stopJob = true;
                        }
                        else
                        {
                            LocalTargetInfo targetA = target != null
                                ? new LocalTargetInfo(target)
                                : (comm.TargetPos != null ? new LocalTargetInfo(comm.TargetPos.Get()) : LocalTargetInfo.Invalid);
                            SetPawnJob(pawn, jDef, targetA, targetB, comm.Count, comm.AbilityDefName, syncedUrgency);
                        }
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Goto)
                    {
                        SetPawnJob(pawn, JobDefOf.Goto, comm.TargetPos.Get(), default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Attack)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.AttackStatic, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.AttackMelee)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.AttackMelee, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Equip)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Equip, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.TakeInventory)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.TakeInventory, target, default, target.stackCount, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Wear)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Wear, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.DropEquipment)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.DropEquipment, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.RemoveApparel)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.RemoveApparel, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Ingest)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Ingest, target, default, Math.Min(target.stackCount, target.def.ingestible.maxNumToIngestAtOnce), null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Strip)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Strip, target, default, -1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.TendPatient)
                    {
                        if (target == null) target = pawn;
                        SetPawnJob(pawn, JobDefOf.TendPatient, target, default, 1, null, syncedUrgency);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.OC_InventoryDrop)
                    {
                        stopJob = true;
                        if (target != null)
                        {
                            GenDrop.TryDropSpawn(target, pawn.Position, GameMap, ThingPlaceMode.Near, out _);
                        }
                    }
                    else
                    {
                        stopJob = true;
                    }
                }

                if (stopJob)
                {
                    if (AttackingPawnJobDic.ContainsKey(pId)) AttackingPawnJobDic.Remove(pId);

                    if (pawn.CurJobDef != JobDefOf.Wait_Combat)
                    {
                        pawn.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                        {
                            playerForced = true,
                            expiryInterval = int.MaxValue,
                            checkOverrideOnExpire = false,
                        }, JobCondition.InterruptForced);
                    }
                }
                else
                {
                    if (ApplyPawnJobByTick.TryGetValue(pId, out APJBT existingCheck))
                    {
                        existingCheck.Comm = comm;
                        existingCheck.Tick = tick;
                    }
                    else
                    {
                        ApplyPawnJobByTick[pId] = new APJBT { Comm = comm, Tick = tick };
                    }
                }
            }
            catch (Exception exp)
            {
                if (AttackingPawnJobDic.ContainsKey(pId)) AttackingPawnJobDic.Remove(pId);
                Loger.Log("HostAttackUpdate ApplyAttackingPawnJob Exception: " + exp.Message, Loger.LogLevel.ERROR);
            }
            finally
            {
                UIEventNewJobDisable = prevDisable;
            }
        }

        public bool UIEventNewJobDisable = false;

        public void NotifyHostPawnStartedPath(Pawn pawn, IntVec3 destCell, LocomotionUrgency urgency)
        {
            if (UIEventNewJobDisable || pawn == null || !destCell.IsValid) return;
            int pawnId = pawn.thingIDNumber;
            if (AttackingPawnDic != null && AttackingPawnDic.ContainsKey(pawnId)) return;

            var hostCmd = new AttackPawnCommand
            {
                HostPawnID = pawnId,
                Command = AttackPawnCommand.PawnCommand.UniversalJob,
                JobDefName = (urgency == LocomotionUrgency.Walk || urgency == LocomotionUrgency.Amble) ? "GotoWander" : "Goto",
                TargetPos = new IntVec3S(destCell),
                HostilityMode = (byte)urgency,
                ScheduledTick = LockstepController.LocalBattleTick + LockstepController.CommandTickDelay
            };

            lock (_commandsSync)
            {
                if (_outgoingHostCommands.Count > 0)
                {
                    var last = _outgoingHostCommands[_outgoingHostCommands.Count - 1];
                    if (last.HostPawnID == hostCmd.HostPawnID
                        && last.TargetPos != null
                        && last.TargetPos.x == destCell.x
                        && last.TargetPos.z == destCell.z
                        && Math.Abs(last.ScheduledTick - hostCmd.ScheduledTick) <= 5)
                    {
                        return;
                    }
                }
                _outgoingHostCommands.Add(hostCmd);
            }
        }

        public void UIEventDraftChanged(Pawn pawn, bool drafted)
        {
            if (UIEventNewJobDisable || pawn == null) return;
            if (AttackingPawns != null && AttackingPawns.Contains(pawn)) return;

            var cmd = new AttackPawnCommand
            {
                HostPawnID = pawn.thingIDNumber,
                Command = AttackPawnCommand.PawnCommand.SetDrafted,
                DraftState = drafted,
                ScheduledTick = LockstepController.LocalBattleTick + LockstepController.CommandTickDelay
            };

            lock (_commandsSync)
            {
                _outgoingHostCommands.Add(cmd);
            }
        }

        public void UIEventNewJob(Pawn pawn, Job job)
        {
            if (UIEventNewJobDisable || pawn == null || job == null) return;

            try
            {
                var pawnId = pawn.thingIDNumber;

                lock (ToSendListsSync)
                {
                    if (ThingPrepareChange2.TryGetValue(pawnId, out Thing jobThing) && !ThingPrepareChange1.Contains(jobThing))
                    {
                        ThingPrepareChange1.Add(jobThing);
                        ThingPrepareChange2.Remove(pawnId);
                    }
                    if (job.targetA.HasThing && !(job.targetA.Thing is Pawn))
                    {
                        ThingPrepareChange2[pawnId] = job.targetA.Thing;
                    }
                    if (pawn.RaceProps != null && pawn.RaceProps.Humanlike
                        && (job.def == JobDefOf.Equip
                        || job.def == JobDefOf.TakeInventory
                        || job.def == JobDefOf.Wear
                        || job.def == JobDefOf.DropEquipment
                        || job.def == JobDefOf.RemoveApparel
                        || job.def == JobDefOf.Ingest
                        || job.def == JobDefOf.TendPatient))
                    {
                        ThingPrepareChange2[pawnId] = pawn;
                    }
                }

                if (!AttackingPawnDic.ContainsKey(pawnId) && job.def != null)
                {
                    bool isIdleWait = job.def == JobDefOf.Wait_Combat
                        || job.def == JobDefOf.Wait
                        || job.def == JobDefOf.Wait_MaintainPosture
                        || job.def == JobDefOf.Wait_Wander;

                    bool shouldSync = (job.playerForced && !isIdleWait)
                        || job.def == JobDefOf.Goto
                        || job.def == JobDefOf.GotoWander
                        || job.def == JobDefOf.AttackMelee
                        || job.def == JobDefOf.AttackStatic
                        || job.def == JobDefOf.Flee
                        || job.def == JobDefOf.FleeAndCower
                        || job.def == JobDefOf.Follow
                        || job.def == JobDefOf.FollowClose
                        || job.def == JobDefOf.Rescue
                        || job.def == JobDefOf.TendPatient;

                    if (shouldSync)
                    {
                        LocomotionUrgency urgency = job.locomotionUrgency;
                        if (urgency == LocomotionUrgency.None)
                        {
                            urgency = (pawn.Drafted || job.playerForced) ? LocomotionUrgency.Jog : LocomotionUrgency.Walk;
                        }

                        var hostCmd = new AttackPawnCommand
                        {
                            HostPawnID = pawnId,
                            Command = job.ability != null ? AttackPawnCommand.PawnCommand.CastAbility : AttackPawnCommand.PawnCommand.UniversalJob,
                            JobDefName = job.def.defName,
                            Count = job.count,
                            AbilityDefName = job.ability?.def?.defName,
                            HostilityMode = (byte)urgency,
                            ScheduledTick = LockstepController.LocalBattleTick + LockstepController.CommandTickDelay
                        };

                        if (job.targetA.HasThing)
                            hostCmd.TargetID = job.targetA.Thing.thingIDNumber;
                        else if (job.targetA.IsValid)
                            hostCmd.TargetPos = new IntVec3S(job.targetA.Cell);

                        if (job.targetB.HasThing)
                            hostCmd.TargetIDB = job.targetB.Thing.thingIDNumber;
                        else if (job.targetB.IsValid)
                            hostCmd.TargetPosB = new IntVec3S(job.targetB.Cell);

                        lock (_commandsSync)
                        {
                            if (_outgoingHostCommands.Count > 0)
                            {
                                var last = _outgoingHostCommands[_outgoingHostCommands.Count - 1];
                                if (last.HostPawnID == hostCmd.HostPawnID
                                    && last.JobDefName == hostCmd.JobDefName
                                    && last.TargetID == hostCmd.TargetID
                                    && (last.TargetPos == null && hostCmd.TargetPos == null
                                        || last.TargetPos != null && hostCmd.TargetPos != null && last.TargetPos.x == hostCmd.TargetPos.x && last.TargetPos.z == hostCmd.TargetPos.z)
                                    && Math.Abs(last.ScheduledTick - hostCmd.ScheduledTick) <= 2)
                                {
                                    return;
                                }
                            }
                            _outgoingHostCommands.Add(hostCmd);
                        }
                    }
                }
            }
            catch { }
        }

        public void UIEventChange(Thing thing, bool distroy = false, bool newSpawn = false)
        {
            if (PlantNotSend && thing is Plant && !thing.def.plant.IsTree) return;
            if (thing == null) return;

            var tId = thing.thingIDNumber;
            lock (ToSendListsSync)
            {
                if (distroy)
                {
                    ToSendDeleteId.Add(tId);
                    ToSendThingAdd.Remove(thing);
                    if (thing is Fire fi) FireList.Remove(fi);
                }
                else if (newSpawn)
                {
                    ToSendThingAdd.Add(thing);
                    ToSendDeleteId.Remove(tId);
                    if (thing is Fire fi) FireList.Add(fi);
                }
                else
                {
                    if (!(thing is Pawn))
                    {
                        if (ToUpdateStateId.Add(tId))
                        {
                            ToUpdateState.Add(thing);
                        }
                    }
                }
            }
        }

        public bool UIEventInventoryDrop(Thing thing)
        {
            return true;
        }
    }
}