using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses;
using System;
using System.Collections.Generic;
using System.Text;
using Transfer;
using Verse;
using Verse.AI;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер сторони захисника (хоста) в онлайн-битві PvP.
    /// Передає початковий стан карти, синхронізує переміщення, поранення,
    /// створення нових об'єктів/трупів та виконує команди атакуючого кожні 50 мс.
    /// </summary>
    public class GameAttackHost
    {
        public int AttackUpdateDelay { get; } = 50;
        public bool PlantNotSend { get; } = false;
        public int SendDelayedFillPawnsSeconds { get; } = 30;
        public int CheckVictoryDelay { get; } = 20;
        public int MapBorder { get; } = 10;
        public float TickTimeSpeed { get; } = 0.5f;
        public float HostPawnMoveSpeed { get; } = 0.5f;

        public bool TestMode { get; set; }
        public string AttackerLogin { get; set; }
        public long HostPlaceServerId { get; set; }
        public long InitiatorPlaceServerId { get; set; }

        public HashSet<int> SendedPawnsId { get; set; }
        public Dictionary<int, int> SendedState { get; set; }
        public Dictionary<int, int> SendedXP { get; set; }

        public List<int> ToUpdateStateId { get; set; }
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

        private readonly Dictionary<int, Thing> ThingPrepareChange2 = new Dictionary<int, Thing>(16);

        // ОПТИМІЗАЦІЯ: постійні змінні для усунення 20 алокацій new HashSet<Thing>() на секунду
        private HashSet<Thing> ThingPrepareChange1 = new HashSet<Thing>();
        private HashSet<Thing> ThingPrepareChange0 = new HashSet<Thing>();

        public DateTime CurrentPauseToTime;
        public bool IsPause => CurrentPauseToTime > DateTime.UtcNow;

        public bool HostPawnMoveSpeedActive =>
            TimeStartGameAttack != DateTime.MinValue
            && TimeStartGameAttack.AddMinutes(1) < DateTime.UtcNow;

        public DateTime TimeStartGameAttack = DateTime.MinValue;
        private DateTime CheckVictoryTime = DateTime.MaxValue;

        public bool? ConfirmedVictoryAttacker { get; set; }
        public bool TerribleFatalError { get; set; }

        public bool WaitOrErrorExit { get; set; }
        private DateTime WaitOrErrorExitStart { get; set; }

        private readonly Dictionary<int, Pawn> AllPawns = new Dictionary<int, Pawn>(64);

        // Буфери багаторазового використання для усунення виділень пам'яті кожні 50 мс
        private readonly List<ThingEntry> _newPawnsBuffer = new List<ThingEntry>(16);
        private readonly List<int> _newPawnsIdBuffer = new List<int>(16);
        private readonly List<ThingTrade> _newThingsBuffer = new List<ThingTrade>(32);
        private readonly List<int> _newThingsIdBuffer = new List<int>(32);
        private readonly List<AttackCorpse> _newCorpsesBuffer = new List<AttackCorpse>(16);
        private readonly List<AttackThingState> _toSendStateBuffer = new List<AttackThingState>(64);
        private readonly List<int> _deleteBuffer = new List<int>(16);
        private readonly List<int> _addedIdBuffer = new List<int>(16);

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

        private void PauseMessage()
        {
            GameUtils.ShowDialodOKCancel(
                TestMode
                    ? "OCity_GameAttack_Host_Test_Attack".Translate(AttackerLogin)
                    : "OCity_GameAttack_Host_Settlement_Attacking".Translate(AttackerLogin),
                TestMode
                    ? "OCity_GameAttack_Host_GameSpeed_Lock_Dialog".Translate() + Environment.NewLine
                        + "OCity_GameAttack_Host_Cancel_Action".Translate() + Environment.NewLine
                        + "OCity_GameAttack_Host_Surrender".Translate()
                    : "OCity_GameAttack_Host_GameSpeed_Lock_Dialog".Translate() + Environment.NewLine
                        + "OCity_GameAttack_Host_GameSpeed_Lock_Dialog2".Translate() + Environment.NewLine
                        + "OCity_GameAttack_Host_Surrender".Translate(),
                () => { },
                null
            );
        }

        public void Start(SessionClient connect)
        {
            Find.TickManager.Pause();
            SessionClientController.Data.DontCheckTimerFail = true;

            Loger.Log("Client GameAttackHost Start 1");
            var tolient = connect.AttackOnlineHost(new AttackHostToSrv { State = 2 });
            if (!string.IsNullOrEmpty(connect.ErrorMessage))
            {
                ErrorBreak(connect.ErrorMessage?.ServerTranslate());
                return;
            }

            AttackerLogin = tolient.StartInitiatorPlayer;
            InitiatorPlaceServerId = tolient.InitiatorPlaceServerId;
            HostPlaceServerId = tolient.HostPlaceServerId;
            TestMode = tolient.TestMode;

            Loger.Log("Client GameAttackHost Start 2 " + tolient.HostPlaceServerId + " TestMode=" + TestMode);

            Find.TickManager.Pause();

            LongEventHandler.QueueLongEvent(delegate
            {
                try
                {
                    SessionClientController.SaveGameNowInEvent(false);

                    Find.TickManager.Pause();
                    GameAttackTrigger_Patch.ForceSpeed = 0f;
                    PauseMessage();

                    CurrentPauseToTime = DateTime.UtcNow.AddMinutes(5);
                    Loger.Log("HostAttackUpdate Set 1 CurrentPauseToTime=" + CurrentPauseToTime.ToGoodUtcString());

                    var hostPlace = UpdateWorldController.GetWOByServerId(HostPlaceServerId) as MapParent;
                    GameMap = hostPlace.Map;

                    var toSrvMap = new AttackHostToSrv
                    {
                        State = 4,
                        TerrainDefNameCell = new List<IntVec3S>(GameMap.cellIndices.NumGridCells),
                        TerrainDefName = new List<string>(GameMap.cellIndices.NumGridCells),
                        Thing = new List<ThingTrade>(2048),
                        ThingCell = new List<IntVec3S>(2048)
                    };

                    toSrvMap.MapSize = new IntVec3S(GameMap.Size);

                    CellRect cellRect = CellRect.WholeMap(GameMap);
                    cellRect.ClipInsideMap(GameMap);

                    foreach (IntVec3 current in cellRect)
                    {
                        var terr = GameMap.terrainGrid.TerrainAt(current);
                        toSrvMap.TerrainDefNameCell.Add(new IntVec3S(current));
                        toSrvMap.TerrainDefName.Add(terr.defName);
                    }

                    SendedActual = new Dictionary<int, Thing>(2048);
                    FireList = new HashSet<Thing>();

                    foreach (IntVec3 current in cellRect)
                    {
                        var thingsAtCell = GameMap.thingGrid.ThingsListAt(current);
                        for (int t = 0; t < thingsAtCell.Count; t++)
                        {
                            var thc = thingsAtCell[t];
                            if (thc is Pawn) continue;
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

                        List<ThingEntry> pawnsA = null;
                        var s1Time = DateTime.UtcNow;
                        while (true)
                        {
                            var toClient5 = connect0.AttackOnlineHost(new AttackHostToSrv { State = 5 });
                            if (!string.IsNullOrEmpty(connect0.ErrorMessage))
                            {
                                ErrorBreak(connect0.ErrorMessage);
                                return;
                            }
                            pawnsA = toClient5.Pawns;
                            if (pawnsA != null && pawnsA.Count > 0) break;
                            if ((DateTime.UtcNow - s1Time).TotalSeconds > 20)
                            {
                                ErrorBreak("Timeout");
                                return;
                            }
                        }

                        SendedPawnsId = new HashSet<int>();
                        SendedState = new Dictionary<int, int>(64);
                        SendedXP = new Dictionary<int, int>(64);
                        ToUpdateStateId = new List<int>(32);
                        ToUpdateState = new List<Thing>(32);
                        ToSendDeleteId = new HashSet<int>();
                        ToSendAddId = new HashSet<int>();
                        ToSendThingAdd = new HashSet<Thing>();
                        ToSendDelayedFillPawnsId = new HashSet<int>();
                        AttackingPawns = new HashSet<Pawn>();
                        AttackingPawnDic = new Dictionary<int, int>(16);
                        AttackingPawnsLastPos = new Dictionary<Pawn, IntVec3>(16);
                        AttackingPawnJobDic = new Dictionary<int, AttackPawnCommand>(16);
                        AttackUpdateTick = 0;

                        try
                        {
                            var mapPawnsA = GameMap.mapPawns.AllPawnsSpawned;
                            for (int i = 0; i < pawnsA.Count; i++)
                            {
                                var label = pawnsA[i].Name;
                                for (int j = 0; j < mapPawnsA.Count; j++)
                                {
                                    var exist = mapPawnsA[j];
                                    if (exist != null && exist.LabelCapNoCount == label)
                                    {
                                        if (exist.IsColonist)
                                        {
                                            pawnsA.RemoveAt(i--);
                                            break;
                                        }
                                        else
                                        {
                                            if (exist.Spawned) exist.Destroy();
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }

                        UIEventNewJobDisable = true;
                        var cellPawns = GameUtils.SpawnCaravanPirate(GameMap, pawnsA, (th, te) =>
                        {
                            if (th is Pawn p)
                            {
                                AttackingPawns.Add(p);
                                AttackingPawnDic.Add(p.thingIDNumber, te.OriginalID);

                                p.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                                p.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                                {
                                    playerForced = true,
                                    expiryInterval = int.MaxValue,
                                    checkOverrideOnExpire = false,
                                }, JobCondition.InterruptForced);
                            }
                        });
                        UIEventNewJobDisable = false;

                        CameraJumper.TryJump(cellPawns, GameMap);
                        TimerObj = SessionClientController.Timers.Add(AttackUpdateDelay, AttackUpdate);
                        SessionClientController.Data.DontCheckTimerFail = false;

                        GameAttackTrigger_Patch.ActiveAttackHost.Add(GameMap, this);
                    });
                }
                catch (Exception ext)
                {
                    Loger.Log("GameAttackHost Start() Exception: " + ext.Message, Loger.LogLevel.ERROR);
                }
            }, "...", false, null);
        }

        private void ErrorBreak(string msg)
        {
            Loger.Log("Client GameAttackHost error: " + msg, Loger.LogLevel.ERROR);
            Clear();
            SessionClientController.Disconnected("OCity_GameAttacker_Dialog_ErrorMessage".Translate());
        }

        /// <summary>
        /// Основний 50-мс цикл синхронізації дій на стороні хоста.
        /// ОПТИМІЗАЦІЯ: пул списків і масивів, double-buffering для запобігання алокаціям.
        /// </summary>
        private void AttackUpdate()
        {
            bool inTimerEvent = false;
            try
            {
                if (WaitOrErrorExit)
                {
                    Find.TickManager.Pause();
                    if (WaitOrErrorExitStart == DateTime.MinValue)
                    {
                        WaitOrErrorExitStart = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - WaitOrErrorExitStart).TotalSeconds > 60)
                    {
                        ErrorBreak("Unknown error");
                    }
                    return;
                }
                if (TerribleFatalError)
                {
                    Find.TickManager.Pause();
                    return;
                }

                if (InTimer) return;
                InTimer = true;

                AttackUpdateTick++;

                if (IsPause)
                {
                    if (!Find.TickManager.Paused)
                    {
                        Find.TickManager.Pause();
                        GameAttackTrigger_Patch.ForceSpeed = 0f;
                        PauseMessage();
                    }
                }
                else
                {
                    if (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed != TimeSpeed.Normal)
                    {
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                        GameAttackTrigger_Patch.ForceSpeed = TickTimeSpeed;
                        if (TimeStartGameAttack == DateTime.MinValue) TimeStartGameAttack = DateTime.UtcNow;
                    }
                }

                SessionClientController.Command((connect) =>
                {
                    try
                    {
                        AttackHostFromSrv toClient;
                        lock (ToSendListsSync)
                        {
                            AllPawns.Clear();
                            var spawnedPawns = GameMap.mapPawns.AllPawnsSpawned;
                            for (int pIdx = 0; pIdx < spawnedPawns.Count; pIdx++)
                            {
                                var p = spawnedPawns[pIdx];
                                AllPawns[p.thingIDNumber] = p;
                            }

                            if (!IsPause && ConfirmedVictoryAttacker == null)
                            {
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
                                    }
                                }
                            }
                            if (ConfirmedVictoryAttacker != null) Finish(ConfirmedVictoryAttacker.Value);

                            foreach (var pawnId in AllPawns.Keys)
                            {
                                if (!SendedPawnsId.Contains(pawnId))
                                {
                                    ToSendAddId.Add(pawnId);
                                }
                            }
                            foreach (var sendedId in SendedPawnsId)
                            {
                                if (!AllPawns.ContainsKey(sendedId))
                                {
                                    ToSendDeleteId.Add(sendedId);
                                }
                            }

                            if ((DateTime.UtcNow - SendDelayedFillPawnsLastTime).TotalSeconds >= SendDelayedFillPawnsSeconds)
                            {
                                foreach (var mpp in AllPawns)
                                {
                                    var mp = mpp.Value;
                                    var mpID = mpp.Key;
                                    if (ToSendDelayedFillPawnsId.Contains(mpID)) continue;

                                    var mpXPnew = (int)(mp.health.summaryHealth.SummaryHealthPercent * 10000f);
                                    if (!SendedXP.TryGetValue(mpID, out int mpXP))
                                    {
                                        SendedXP[mpID] = mpXPnew;
                                    }
                                    else if (mpXP != mpXPnew)
                                    {
                                        SendedXP[mpID] = mpXPnew;
                                        ToSendDelayedFillPawnsId.Add(mpID);
                                    }
                                }

                                foreach (var fire in FireList)
                                {
                                    if (!ToUpdateState.Contains(fire))
                                    {
                                        ToUpdateState.Add(fire);
                                    }
                                }

                                SendDelayedFillPawnsLastTime = DateTime.UtcNow;
                                ToSendDelayedFillPawnsId.IntersectWith(AllPawns.Keys);
                                ToSendDelayedFillPawnsId.ExceptWith(ToSendAddId);
                                ToSendAddId.AddRange(ToSendDelayedFillPawnsId);
                                ToSendDelayedFillPawnsId.Clear();
                            }

                            foreach (var mp in ThingPrepareChange0)
                            {
                                if (!(mp is Pawn)) continue;
                                var mpID = mp.thingIDNumber;
                                if (!ToSendAddId.Contains(mpID)) ToSendAddId.Add(mpID);
                            }

                            _newPawnsBuffer.Clear();
                            _newPawnsIdBuffer.Clear();

                            int cnt = ToSendAddId.Count < 3 || ToSendAddId.Count > 6 || AttackUpdateTick == 0
                                ? ToSendAddId.Count
                                : 3;

                            // ОПТИМІЗАЦІЯ: використання _addedIdBuffer замість new int[cnt] що-50 мс
                            _addedIdBuffer.Clear();
                            foreach (int id in ToSendAddId)
                            {
                                if (_addedIdBuffer.Count >= cnt) break;
                                _addedIdBuffer.Add(id);

                                if (!AllPawns.TryGetValue(id, out Pawn thing)) continue;
                                var tt = ThingEntry.CreateEntry(thing, 1);
                                if (AttackingPawnDic.TryGetValue(tt.OriginalID, out int transId)) tt.TransportID = transId;

                                _newPawnsBuffer.Add(tt);
                                _newPawnsIdBuffer.Add(id);
                                SendedActual[thing.thingIDNumber] = thing;
                            }

                            for (int aIdx = 0; aIdx < _addedIdBuffer.Count; aIdx++)
                            {
                                int id = _addedIdBuffer[aIdx];
                                ToSendAddId.Remove(id);
                                SendedPawnsId.Add(id);
                            }

                            foreach (int id in ToSendDeleteId)
                            {
                                SendedPawnsId.Remove(id);
                            }

                            _newThingsBuffer.Clear();
                            _newThingsIdBuffer.Clear();
                            _newCorpsesBuffer.Clear();

                            if (ToSendThingAdd.Count > 0)
                            {
                                foreach (var item in ToSendThingAdd)
                                {
                                    if (ToSendDeleteId.Contains(item.thingIDNumber)) continue;

                                    if (item is Corpse corpse && corpse.InnerPawn != null)
                                    {
                                        _newCorpsesBuffer.Add(new AttackCorpse
                                        {
                                            CorpseId = item.thingIDNumber,
                                            PawnId = corpse.InnerPawn.thingIDNumber,
                                            CorpseWithPawn = ThingEntry.CreateEntry(corpse.InnerPawn, 1)
                                        });
                                    }
                                    else if (!(item is Corpse))
                                    {
                                        var tt = ThingTrade.CreateTrade(item, item.stackCount, false);
                                        _newThingsBuffer.Add(tt);
                                        _newThingsIdBuffer.Add(tt.OriginalID);
                                    }

                                    SendedActual[item.thingIDNumber] = item;
                                }
                            }

                            _toSendStateBuffer.Clear();
                            foreach (var mpp in AllPawns)
                            {
                                var mp = mpp.Value;
                                var mpID = mpp.Key;
                                if (ToUpdateStateId.Contains(mpID)) continue;

                                var mpHash = AttackThingState.GetHash(mp);
                                if (!SendedState.TryGetValue(mpID, out int mpHS) || mpHS != mpHash)
                                {
                                    SendedState[mpID] = mpHash;
                                    _toSendStateBuffer.Add(new AttackThingState(mp));
                                }
                            }

                            for (int imp = 0; imp < ToUpdateState.Count; imp++)
                            {
                                var mp = ToUpdateState[imp];
                                var mpID = mp.thingIDNumber;
                                if (ToSendDeleteId.Contains(mpID)) continue;

                                if (mp is Pawn)
                                {
                                    var mpHash = AttackThingState.GetHash(mp);
                                    SendedState[mpID] = mpHash;
                                }
                                _toSendStateBuffer.Add(new AttackThingState(mp));
                            }

                            foreach (var mp in ThingPrepareChange0)
                            {
                                if (mp is Pawn) continue;
                                var mpID = mp.thingIDNumber;
                                if (ToSendDeleteId.Contains(mpID)) continue;

                                _toSendStateBuffer.Add(new AttackThingState(mp));
                            }

                            // ОПТИМІЗАЦІЯ: Double-buffering замість new HashSet<Thing>() кожні 50 мс
                            ThingPrepareChange0.Clear();
                            var tempSet = ThingPrepareChange0;
                            ThingPrepareChange0 = ThingPrepareChange1;
                            ThingPrepareChange1 = tempSet;

                            _deleteBuffer.Clear();
                            foreach (var item in ToSendDeleteId)
                            {
                                SendedActual.Remove(item);
                                _deleteBuffer.Add(item);
                            }

                            toClient = connect.AttackOnlineHost(new AttackHostToSrv
                            {
                                State = 10,
                                NewPawns = _newPawnsBuffer,
                                NewPawnsId = _newPawnsIdBuffer,
                                NewThings = _newThingsBuffer,
                                NewThingsId = _newThingsIdBuffer,
                                NewCorpses = _newCorpsesBuffer,
                                Delete = _deleteBuffer,
                                UpdateState = _toSendStateBuffer,
                                VictoryAttacker = ConfirmedVictoryAttacker,
                            });

                            ToSendThingAdd.Clear();
                            ToSendDeleteId.Clear();
                            ToUpdateStateId.Clear();
                            ToUpdateState.Clear();
                        }

                        if (toClient == null || toClient.State == 0)
                        {
                            WaitOrErrorExit = true;
                            return;
                        }

                        if (toClient.SetPauseOnTime != DateTime.MinValue)
                        {
                            CurrentPauseToTime = toClient.SetPauseOnTime - SessionClientController.Data.ServetTimeDelta;
                        }

                        if (toClient.VictoryHost)
                        {
                            ConfirmedVictoryAttacker = false;
                        }

                        if (toClient.TerribleFatalError)
                        {
                            TerribleFatalError = true;
                        }

                        if (toClient.UpdateCommand.Count > 0)
                        {
                            UIEventNewJobDisable = true;
                            for (int ii = 0; ii < toClient.UpdateCommand.Count; ii++)
                            {
                                var comm = toClient.UpdateCommand[ii];
                                if (!AllPawns.TryGetValue(comm.HostPawnID, out Pawn pawn)) continue;

                                AttackingPawnJobDic[comm.HostPawnID] = comm;
                                ApplyAttackingPawnJob(pawn);
                            }
                            UIEventNewJobDisable = false;
                        }

                        if (toClient.NeedNewThingIDs.Count > 0)
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
                    catch (Exception ext)
                    {
                        InTimer = false;
                        Loger.Log("HostAttackUpdate Event Exception: " + ext.Message, Loger.LogLevel.ERROR);
                    }
                    finally
                    {
                        InTimer = false;
                    }
                });

                if (MainHelper.DebugMode || AttackUpdateTick % 20 == 0)
                {
                    Loger.Log("HostAttackUpdate Tick #" + AttackUpdateTick);
                }
            }
            catch (Exception ext)
            {
                Loger.Log("HostAttackUpdate Exception: " + ext.Message, Loger.LogLevel.ERROR);
            }
            if (!inTimerEvent) InTimer = false;
        }

        private class APJBT
        {
            public long Tick;
            public AttackPawnCommand Comm;
        }
        private readonly Dictionary<int, APJBT> ApplyPawnJobByTick = new Dictionary<int, APJBT>(16);

        private void SetPawnJob(Pawn pawn, JobDef def, LocalTargetInfo target, int count = -1)
        {
            pawn.jobs.StartJob(new Job(def, target)
            {
                playerForced = true,
                expiryInterval = int.MaxValue,
                checkOverrideOnExpire = false,
                count = count,
            }, JobCondition.InterruptForced);
        }

        private void ApplyAttackingPawnJob(Pawn pawn)
        {
            var pId = pawn.thingIDNumber;
            AttackPawnCommand comm = null;
            bool stopJob = !AttackingPawnDic.ContainsKey(pId) || !AttackingPawnJobDic.TryGetValue(pId, out comm);
            try
            {
                var tick = (long)Find.TickManager.TicksGame;
                if (ApplyPawnJobByTick.TryGetValue(pId, out APJBT check))
                {
                    if (check.Comm == comm && check.Tick == tick)
                    {
                        stopJob = true;
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
                    for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                    {
                        var item = pawn.inventory.innerContainer[i];
                        if (item.def.defName == comm.TargetDefName) { target = item; break; }
                    }
                    if (target == null)
                    {
                        for (int i = 0; i < pawn.carryTracker.innerContainer.Count; i++)
                        {
                            var item = pawn.carryTracker.innerContainer[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null)
                    {
                        for (int i = 0; i < pawn.equipment.AllEquipmentListForReading.Count; i++)
                        {
                            var item = pawn.equipment.AllEquipmentListForReading[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null)
                    {
                        for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
                        {
                            var item = pawn.apparel.WornApparel[i];
                            if (item.def.defName == comm.TargetDefName) { target = item; break; }
                        }
                    }
                    if (target == null) stopJob = true;
                }

                if (!stopJob)
                {
                    if (comm.Command == AttackPawnCommand.PawnCommand.Goto)
                    {
                        SetPawnJob(pawn, JobDefOf.Goto, comm.TargetPos.Get());
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Attack)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.AttackStatic, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.AttackMelee)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.AttackMelee, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Equip)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Equip, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.TakeInventory)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.TakeInventory, target, target.stackCount);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Wear)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Wear, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.DropEquipment)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.DropEquipment, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.RemoveApparel)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.RemoveApparel, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Ingest)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Ingest, target, Math.Min(target.stackCount, target.def.ingestible.maxNumToIngestAtOnce));
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.Strip)
                    {
                        if (target == null) stopJob = true;
                        else SetPawnJob(pawn, JobDefOf.Strip, target);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.TendPatient)
                    {
                        if (target == null) target = pawn;
                        SetPawnJob(pawn, JobDefOf.TendPatient, target, 1);
                    }
                    else if (comm.Command == AttackPawnCommand.PawnCommand.OC_InventoryDrop)
                    {
                        stopJob = true;
                        GenDrop.TryDropSpawn(target, pawn.Position, GameMap, ThingPlaceMode.Near, out _);
                    }
                    else stopJob = true;
                }

                if (stopJob)
                {
                    if (AttackingPawnJobDic.ContainsKey(pId)) AttackingPawnJobDic.Remove(pId);

                    pawn.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                    {
                        playerForced = true,
                        expiryInterval = int.MaxValue,
                        checkOverrideOnExpire = false,
                    }, JobCondition.InterruptForced);
                }
                else
                {
                    // ОПТИМІЗАЦІЯ: оновлення наявного об'єкта APJBT без створення нового екземпляра
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
        }

        public bool UIEventNewJobDisable = false;
        public void UIEventNewJob(Pawn pawn, Job job)
        {
            try
            {
                var pawnId = pawn.thingIDNumber;

                if (ThingPrepareChange2.TryGetValue(pawnId, out Thing jobThing) && !ThingPrepareChange1.Contains(jobThing))
                {
                    ThingPrepareChange1.Add(jobThing);
                    ThingPrepareChange2.Remove(pawnId);
                }
                if (job != null && job.targetA.HasThing && !(job.targetA.Thing is Pawn))
                {
                    ThingPrepareChange2[pawnId] = job.targetA.Thing;
                }
                if (job != null && pawn.RaceProps.Humanlike
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

                if (UIEventNewJobDisable) return;

                if (!AttackingPawnDic.ContainsKey(pawnId)) return;

                UIEventNewJobDisable = true;
                ApplyAttackingPawnJob(pawn);
            }
            catch { }
            UIEventNewJobDisable = false;
        }

        public void UIEventChange(Thing thing, bool distroy = false, bool newSpawn = false)
        {
            if (PlantNotSend && thing is Plant && !thing.def.plant.IsTree) return;

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
                    if (thing is Pawn)
                    {
                        if (!ToSendDelayedFillPawnsId.Contains(tId)) ToSendDelayedFillPawnsId.Add(tId);
                    }
                    else
                    {
                        if (!ToUpdateStateId.Contains(tId))
                        {
                            ToUpdateStateId.Add(tId);
                            ToUpdateState.Add(thing);
                        }
                    }
                }
            }
        }

        public void ControlPawnMoveSpeed(Pawn pawn, ref int speed)
        {
            if (!HostPawnMoveSpeedActive || pawn == null) return;

            if (!AttackingPawnDic.ContainsKey(pawn.thingIDNumber))
            {
                speed = (int)(speed / HostPawnMoveSpeed);
                if (speed > 450) speed = 450;
            }
        }

        private bool? CheckAttackerVictory()
        {
            bool existHostPawn = false;
            bool existAttackerPawn = false;

            foreach (var pawn in AllPawns.Values)
            {
                if (pawn.Dead || pawn.Downed) continue;

                if (!existAttackerPawn
                    && pawn.RaceProps.Humanlike
                    && AttackingPawnDic.ContainsKey(pawn.thingIDNumber))
                {
                    existAttackerPawn = true;
                }

                if (!existHostPawn && pawn.IsColonist)
                {
                    existHostPawn = true;
                }

                if (existHostPawn && existAttackerPawn)
                {
                    return null;
                }
            }

            return existHostPawn && existAttackerPawn ? (bool?)null : existAttackerPawn;
        }

        public void Clear()
        {
            if (TimerObj != null) SessionClientController.Timers.Remove(TimerObj);
            if (GameMap != null) GameAttackTrigger_Patch.ActiveAttackHost.Remove(GameMap);
            SessionClientController.Data.AttackUsModule = null;
            SessionClientController.Data.BackgroundSaveGameOff = false;
            SessionClientController.Data.DontCheckTimerFail = false;
            GameAttackTrigger_Patch.ForceSpeed = -1f;
        }

        public void Finish(bool victoryAttacker)
        {
            Find.TickManager.Pause();
            Clear();

            if (TestMode)
            {
                GameUtils.ShowDialodOKCancel(
                    TestMode
                        ? "OCity_GameAttack_Host_Test_Attack".Translate(AttackerLogin)
                        : "OCity_GameAttack_Host_Settlement_Attacking".Translate(AttackerLogin),
                    victoryAttacker
                        ? "Ocity_GameAttacker_TrainingFight_Lost".Translate() + Environment.NewLine + "OCity_GameAttack_Host_Card_Restored".Translate()
                        : "OCity_GameAttack_Host_Training_Attack_Repulsed".Translate() + Environment.NewLine + "OCity_GameAttack_Host_Card_Restored".Translate(),
                    () => SessionClientController.Disconnected("OCity_GameAttacker_Done".Translate()),
                    null
                );
                return;
            }

            if (victoryAttacker)
            {
                Find.WorldObjects.Remove(GameMap.Parent);
            }
            else
            {
                var pawnsToDestroy = new List<Pawn>();
                foreach (var pawn in AttackingPawns)
                {
                    if (!pawn.Dead && !pawn.Downed &&
                        (pawn.Position.x < MapBorder || pawn.Position.x > GameMap.Size.x - 1 - MapBorder
                         || pawn.Position.z < MapBorder || pawn.Position.z > GameMap.Size.z - 1 - MapBorder))
                    {
                        pawnsToDestroy.Add(pawn);
                    }
                }
                for (int pIdx = 0; pIdx < pawnsToDestroy.Count; pIdx++)
                {
                    GameUtils.PawnDestroy(pawnsToDestroy[pIdx]);
                }
            }

            SessionClientController.SaveGameNow(true, () =>
            {
                GameUtils.ShowDialodOKCancel(
                    TestMode
                        ? "OCity_GameAttack_Host_Test_Attack".Translate(AttackerLogin)
                        : "OCity_GameAttack_Host_Settlement_Attacking".Translate(AttackerLogin),
                    victoryAttacker
                        ? "OCity_GameAttack_Host_Caravan_TransferToNewOwner".Translate()
                        : "OCity_GameAttack_Host_Atack_Repulsed".Translate() + Environment.NewLine + "OCity_GameAttack_Host_Stranded_EnemiesLostCommander_Touch".Translate(),
                    () => { },
                    null
                );
            });
        }
    }
}