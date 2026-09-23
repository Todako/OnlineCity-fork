using Model;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses;
using System;
using System.Collections.Generic;
using System.Text;
using Verse;
using Verse.AI;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер сторони нападника в онлайн-битві PvP.
    /// Відповідає за створення копії карти, передачу команд пішакам,
    /// синхронізацію дій та отримання оновлень стану від хоста кожні 50 мс.
    /// </summary>
    public class GameAttacker
    {
        public int AttackUpdateDelay { get; } = 50;
        public int TimeStopBeforeAttack { get; } = 60;
        public int MapBorder { get; } = 10;

        public static bool CanStart => SessionClientController.Data.AttackModule == null;
        public static GameAttacker Get => SessionClientController.Data.AttackModule;

        public bool TestMode { get; set; }

        public int AttackUpdateTick { get; set; }
        private object TimerObj;
        private bool InTimer { get; set; }
        private Map GameMap { get; set; }

        private Dictionary<int, int> ThingsIDDicRev { get; set; }
        private Dictionary<int, int> ThingsIDDic { get; set; }
        private Dictionary<int, Thing> ThingsObjDic { get; set; }
        private Dictionary<Pawn, int> AttackerPawns { get; set; }
        private List<string> AttackerOriginalPawnLabels { get; set; }

        private readonly Dictionary<int, AttackPawnCommand> ToSendCommand = new Dictionary<int, AttackPawnCommand>(32);

        // Багаторазові буфери для усунення виділень пам'яті в 50-мс циклі
        private readonly List<AttackPawnCommand> _commandsBuffer = new List<AttackPawnCommand>(32);
        private readonly List<int> _needNewThingsBuffer = new List<int>(16);
        private readonly List<ThingEntry> _corpseListBuffer = new List<ThingEntry>(16);

        private Dictionary<int, Thing> CheckDestroy { get; set; }
        private Dictionary<int, Thing> CheckSpawn { get; set; }

        private bool ThingDropIgnore { get; set; }
        private bool CheckSpawnDestroyDisable { get; set; }
        private readonly object CheckSpawnDestroySync = new object();

        public bool VictoryHostToHost { get; set; }
        public bool TerribleFatalError { get; set; }

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
            Find.TickManager.Pause();
            SessionClientController.Data.DontCheckTimerFail = true;
            SessionClientController.SaveGameNowInEvent(false);

            SessionClientController.Command((connect) =>
            {
                connect.ErrorMessage = null;
                Find.TickManager.Pause();

                Loger.Log("Client GameAttack State 0");
                var res = connect.AttackOnlineInitiator(new AttackInitiatorToSrv()
                {
                    State = 0,
                    StartHostPlayer = attackedBase.Player.Public.Login,
                    HostPlaceServerId = attackedBase.OnlineWObject.PlaceServerId,
                    InitiatorPlaceServerId = UpdateWorldController.GetServerInfo(caravan).PlaceServerId,
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
                    if ((DateTime.UtcNow - s1Time).TotalSeconds > 20)
                    {
                        ErrorBreak("Timeout");
                        return;
                    }
                }

                var pawnsToSend = GetPawnsAndDeleteCaravan(caravan);
                AttackerOriginalPawnLabels = new List<string>(pawnsToSend.Count);
                for (int i = 0; i < pawnsToSend.Count; i++)
                {
                    AttackerOriginalPawnLabels.Add(pawnsToSend[i].Name);
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
                        ErrorBreak("Timeout");
                        return;
                    }
                }

                Find.TickManager.Pause();
                GameAttackTrigger_Patch.ForceSpeed = 0f;
                Loger.Log($"Client StartCreateClearMap TerrainCell={response.TerrainDefNameCell.Count} Thing={response.ThingCell.Count}");

                CreateClearMap(attackedBase.Tile, response.MapSize.Get(), (map, mapParent) =>
                {
                    GameMap = map;

                    for (int i = 0; i < response.TerrainDefNameCell.Count; i++)
                    {
                        var current = response.TerrainDefNameCell[i].Get();
                        var terrName = response.TerrainDefName[i];
                        map.terrainGrid.SetTerrain(current, DefDatabase<TerrainDef>.GetNamed(terrName));
                    }

                    ThingsIDDicRev = new Dictionary<int, int>(response.ThingCell.Count);
                    ThingsIDDic = new Dictionary<int, int>(response.ThingCell.Count);
                    ThingsObjDic = new Dictionary<int, Thing>(response.ThingCell.Count);
                    AttackerPawns = new Dictionary<Pawn, int>(16);
                    ToSendCommand.Clear();
                    CheckDestroy = new Dictionary<int, Thing>(32);
                    CheckSpawn = new Dictionary<int, Thing>(32);

                    for (int i = 0; i < response.ThingCell.Count; i++)
                    {
                        var current = response.ThingCell[i].Get();
                        var tt = response.Thing[i];
                        var th = tt.CreateThing();
                        GenSpawn.Spawn(th, current, map, new Rot4(tt.Rotation), WipeMode.Vanish);

                        if (tt.OriginalID != 0 && th.thingIDNumber != 0)
                        {
                            ThingsIDDicRev[tt.OriginalID] = th.thingIDNumber;
                            ThingsIDDic[th.thingIDNumber] = tt.OriginalID;
                            ThingsObjDic[th.thingIDNumber] = th;
                        }
                    }

                    AttackUpdateTick = 0;
                    AttackUpdate();

                    TimerObj = SessionClientController.Timers.Add(AttackUpdateDelay, AttackUpdate);
                });
            });
        }

        private Thing GetThingByHostId(int hostid)
        {
            if (!ThingsIDDicRev.TryGetValue(hostid, out int id)) return null;
            ThingsObjDic.TryGetValue(id, out Thing thing);
            return thing;
        }

        private void DestroyThing(Thing thing, int hostId = 0)
        {
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
                    thing.Destroy();
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
                sendThings.Add(ThingEntry.CreateEntry(select[i], 1));
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

        /// <summary>
        /// Застосовує оновлені стани та видаляє знищені об'єкти без створення тимчасових лямбда-делегатів.
        /// </summary>
        private void ApplyStateAndDeletions(AttackInitiatorFromSrv toClient)
        {
            if (toClient.UpdateState != null)
            {
                for (int i = 0; i < toClient.UpdateState.Count; i++)
                {
                    Thing thing = GetThingByHostId(toClient.UpdateState[i].HostThingID);
                    if (thing == null) continue;

                    try
                    {
                        ThingDropIgnore = true;
                        GameUtils.ApplyState(thing, toClient.UpdateState[i]);
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

        /// <summary>
        /// Основний високочастотний цикл онлайн-битви (кожні 50 мс).
        /// ОПТИМІЗАЦІЯ: повністю ліквідовано створення лямбда-делегатів і списків.
        /// </summary>
        private void AttackUpdate()
        {
            bool inTimerEvent = false;
            try
            {
                if (TerribleFatalError)
                {
                    Find.TickManager.Pause();
                    return;
                }

                if (!Find.TickManager.Paused)
                {
                    Find.TickManager.Pause();
                    GameUtils.ShowDialodOKCancel(
                        "OCity_GameAttacker_Dialog_Settlement_Attack".Translate(),
                        "OCity_GameAttacker_Main_Dialog".Translate() + Environment.NewLine + "OCity_GameAttacker_Withdraw".Translate(),
                        () => { },
                        null
                    );
                }

                if (InTimer) return;
                InTimer = true;
                AttackUpdateTick++;

                if (MainHelper.DebugMode || AttackUpdateTick % 20 == 0)
                {
                    Loger.Log("Client AttackUpdate #" + AttackUpdateTick);
                }

                if (AttackUpdateTick == 2)
                {
                    SessionClientController.Data.DontCheckTimerFail = false;

                    var findLabel = "LetterLabelAreaRevealed".Translate();
                    var letters = Find.LetterStack.LettersListForReading;
                    for (int i = letters.Count - 1; i >= 0; i--)
                    {
                        if (letters[i].Label == findLabel)
                        {
                            Find.LetterStack.RemoveLetter(letters[i]);
                        }
                    }

                    bool terribleFatalError = AttackerOriginalPawnLabels.Count != AttackerPawns.Count;
                    if (!terribleFatalError)
                    {
                        for (int i = 0; i < AttackerOriginalPawnLabels.Count; i++)
                        {
                            string pawnLabel = AttackerOriginalPawnLabels[i];
                            bool found = false;
                            foreach (var ap in AttackerPawns.Keys)
                            {
                                if (ap.LabelCapNoCount == pawnLabel)
                                {
                                    found = true;
                                    break;
                                }
                            }
                            if (!found)
                            {
                                terribleFatalError = true;
                                break;
                            }
                        }
                    }

                    if (terribleFatalError)
                    {
                        Loger.Log("Client AttackUpdate: TerribleFatalError detected", Loger.LogLevel.ERROR);
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
                }

                if (AttackUpdateTick == 3)
                {
                    GameUtils.ShowDialodOKCancel(
                        "OCity_GameAttacker_Dialog_Settlement_Attack".Translate(),
                        "OCity_GameAttacker_Main_Preparation_Dialog".Translate() + Environment.NewLine
                            + "OCity_GameAttacker_Main_Dialog".Translate() + Environment.NewLine
                            + "OCity_GameAttacker_Withdraw".Translate(),
                        () => { },
                        null
                    );
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
                                        checkSpawn.Value.Destroy();
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
                        if (ToSendCommand.Count > 0)
                        {
                            foreach (var cmd in ToSendCommand.Values)
                            {
                                _commandsBuffer.Add(cmd);
                            }
                            ToSendCommand.Clear();
                        }

                        var toClient = connect.AttackOnlineInitiator(new AttackInitiatorToSrv
                        {
                            State = 10,
                            UpdateCommand = _commandsBuffer,
                            SetPauseOnTimeToHost = AttackUpdateTick == 2 ? new TimeSpan(0, 0, TimeStopBeforeAttack) : TimeSpan.MinValue,
                            VictoryHostToHost = VictoryHostToHost,
                            NeedNewThingIDs = _needNewThingsBuffer,
                        });

                        if (toClient.NewPawns != null && toClient.NewCorpses != null && toClient.NewThings != null
                            && (toClient.NewPawns.Count > 0 || toClient.NewCorpses.Count > 0 || toClient.NewThings.Count > 0))
                        {
                            try
                            {
                                if (toClient.NewPawns.Count > 0)
                                {
                                    for (int i = 0; i < toClient.NewPawnsId.Count; i++)
                                    {
                                        var hostid = toClient.NewPawnsId[i];
                                        Thing thing = GetThingByHostId(hostid);
                                        if (thing != null) DestroyThing(thing, hostid);
                                    }

                                    GameUtils.SpawnList(GameMap, toClient.NewPawns, false,
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
                                                    AttackerPawns[p] = te.OriginalID;
                                                    if (p.playerSettings != null) p.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
                                                    if (p.drafter != null) p.drafter.Drafted = true;
                                                    p.jobs.StartJob(new Job(JobDefOf.Wait_Combat)
                                                    {
                                                        playerForced = true,
                                                        expiryInterval = int.MaxValue,
                                                        checkOverrideOnExpire = false,
                                                    }, JobCondition.InterruptForced);
                                                }
                                                else if (p != null && p.def.CanHaveFaction && p.Faction != null && p.Faction.IsPlayer)
                                                {
                                                    p.SetFaction(null);
                                                }
                                            }
                                        });
                                }

                                if (toClient.NewCorpses.Count > 0)
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

                                if (toClient.NewThings.Count > 0)
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

                                ApplyStateAndDeletions(toClient);

                                if (AttackUpdateTick == 1)
                                {
                                    ModBaseData.RunMainThreadSync(() =>
                                    {
                                        FloodFillerFog.DebugRefogMap(GameMap);
                                        CameraJumper.TryJump(GameMap.Center, GameMap);
                                    });
                                    GameAttackTrigger_Patch.ActiveAttacker.Add(GameMap, this);
                                }
                            }
                            catch (Exception ext)
                            {
                                Loger.Log("Client AttackUpdate SpawnListEvent Exception: " + ext.Message, Loger.LogLevel.ERROR);
                            }
                            InTimer = false;
                        }
                        else
                        {
                            ApplyStateAndDeletions(toClient);
                            InTimer = false;
                        }

                        if (toClient.Finishing) Finish(toClient.VictoryAttacker);
                    }
                    catch (Exception ext)
                    {
                        InTimer = false;
                        Loger.Log("Client AttackUpdate Command Exception: " + ext.Message, Loger.LogLevel.ERROR);
                    }
                });
            }
            catch (Exception ext)
            {
                Loger.Log("AttackUpdate Exception: " + ext.Message, Loger.LogLevel.ERROR);
            }
            if (!inTimerEvent) InTimer = false;
        }

        public void UIEventNewJob(Pawn pawn, Job job)
        {
            try
            {
                if (!AttackerPawns.TryGetValue(pawn, out int id)) return;

                var comm = new AttackPawnCommand { HostPawnID = id };

                if (job != null)
                {
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
                    else
                    {
                        comm.TargetPos = new IntVec3S(job.targetA.Cell);
                    }

                    if (job.def == JobDefOf.Goto) comm.Command = AttackPawnCommand.PawnCommand.Goto;
                    else if (job.def == JobDefOf.AttackStatic) comm.Command = AttackPawnCommand.PawnCommand.Attack;
                    else if (job.def == JobDefOf.AttackMelee) comm.Command = AttackPawnCommand.PawnCommand.AttackMelee;
                    else if (job.def == JobDefOf.Equip) comm.Command = AttackPawnCommand.PawnCommand.Equip;
                    else if (job.def == JobDefOf.TakeInventory) comm.Command = AttackPawnCommand.PawnCommand.TakeInventory;
                    else if (job.def == JobDefOf.Wear) comm.Command = AttackPawnCommand.PawnCommand.Wear;
                    else if (job.def == JobDefOf.DropEquipment) comm.Command = AttackPawnCommand.PawnCommand.DropEquipment;
                    else if (job.def == JobDefOf.RemoveApparel) comm.Command = AttackPawnCommand.PawnCommand.RemoveApparel;
                    else if (job.def == JobDefOf.Ingest) comm.Command = AttackPawnCommand.PawnCommand.Ingest;
                    else if (job.def == JobDefOf.Strip) comm.Command = AttackPawnCommand.PawnCommand.Strip;
                    else if (job.def == JobDefOf.TendPatient) comm.Command = AttackPawnCommand.PawnCommand.TendPatient;
                    else return;
                }
                else return;

                ToSendCommand[id] = comm;
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

                if (ToSendCommand.TryGetValue(id, out var comm0) && comm0.Command == AttackPawnCommand.PawnCommand.OC_InventoryDrop)
                {
                    return false;
                }

                ToSendCommand[id] = new AttackPawnCommand
                {
                    HostPawnID = id,
                    TargetDefName = thing.def.defName,
                    Command = AttackPawnCommand.PawnCommand.OC_InventoryDrop
                };
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
                            if (gs.defName == "ElevationFertility"
                                || gs.defName == "Caves"
                                || gs.defName == "Terrain"
                                || gs.defName == "CavesTerrain"
                                || gs.defName == "FindPlayerStartSpot"
                                || gs.defName == "ScenParts"
                                || gs.defName == "Fog")
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
                                Loger.Log("Client CreateClearMap Event Exception: " + exp.Message, Loger.LogLevel.ERROR);
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
                    Loger.Log("Client CreateClearMap Exception: " + exp.Message, Loger.LogLevel.ERROR);
                    ErrorBreak("Error CreateClearMap1");
                }
            }, "GeneratingMapForNewEncounter", false, null);
        }

        public void Clear()
        {
            if (TimerObj != null) SessionClientController.Timers.Remove(TimerObj);
            if (GameMap != null) GameAttackTrigger_Patch.ActiveAttacker.Remove(GameMap);
            SessionClientController.Data.AttackModule = null;
            SessionClientController.Data.BackgroundSaveGameOff = false;
            SessionClientController.Data.DontCheckTimerFail = false;
            GameAttackTrigger_Patch.ForceSpeed = -1f;
        }

        public void Finish(bool victoryAttacker)
        {
            Find.TickManager.Pause();
            Loger.Log("Client AttackerFinish");

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

            if (victoryAttacker)
            {
                ModBaseData.RunMainThreadSync(() =>
                {
                    var mapPawnsA = GameMap.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < mapPawnsA.Count; i++)
                    {
                        var pawn = mapPawnsA[i];
                        if (!pawn.def.CanHaveFaction || pawn.RaceProps.Humanlike || pawn.Faction == null || pawn.Faction.IsPlayer) continue;
                        pawn.SetFaction(null);
                    }
                });
            }
            else
            {
                var listPawn = new List<Pawn>(AttackerPawns.Count);
                foreach (var pawn in AttackerPawns.Keys)
                {
                    if (!pawn.Dead && !pawn.Downed &&
                        (pawn.Position.x < MapBorder || pawn.Position.x > GameMap.Size.x - 1 - MapBorder
                         || pawn.Position.z < MapBorder || pawn.Position.z > GameMap.Size.z - 1 - MapBorder))
                    {
                        listPawn.Add(pawn);
                    }
                }

                CaravanMaker.MakeCaravan(listPawn, Faction.OfPlayer, GameMap.Tile, false);
                Find.WorldObjects.Remove(GameMap.Parent);

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
                GameUtils.ShowDialodOKCancel(
                    "OCity_GameAttacker_Dialog_Settlement_Attack".Translate(),
                    victoryAttacker
                        ? "OCity_GameAttacker_Settlement_TakenOver".Translate()
                        : "OCity_GameAttacker_Defeated".Translate() + Environment.NewLine + "OCity_GameAttacker_Colonist_Return".Translate(),
                    () => { },
                    null
                );
            });

            Loger.Log("Client AttackerFinish end");
        }
    }
}