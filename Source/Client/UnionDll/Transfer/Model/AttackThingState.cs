using RimWorld;
using System;
using Verse;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class AttackThingState
    {
        public enum PawnHealthState : byte
        {
            Dead = 0,
            Down = 1,
            Mobile = 2
        }

        public int HostThingID { get; set; }
        public IntVec3S Position { get; set; }
        public int StackCount { get; set; }
        public int HitPoints { get; set; }
        public PawnHealthState DownState { get; set; }
        public byte Rotation { get; set; }
        public bool Drafted { get; set; }

        public AttackThingState()
        {
        }

        public AttackThingState(Thing mp)
        {
            HostThingID = mp.thingIDNumber;
            StackCount = mp.stackCount;
            Position = new IntVec3S(mp.Position);
            Rotation = mp.Rotation.AsByte;

            if (mp is Fire fire)
            {
                HitPoints = (int)(fire.fireSize * 10000f);
            }
            else
            {
                HitPoints = mp.HitPoints;
            }

            if (mp is Pawn pawn)
            {
                DownState = pawn.health != null
                    ? (PawnHealthState)(int)pawn.health.State
                    : PawnHealthState.Mobile;
                Drafted = pawn.drafter?.Drafted ?? false;
            }
            else
            {
                DownState = PawnHealthState.Mobile;
                Drafted = false;
            }
        }

        public static int GetHash(Thing mp)
        {
            int pawnFlags = 0;
            if (mp is Pawn p)
            {
                pawnFlags = (p.health != null ? (int)p.health.State : 0)
                    + ((p.drafter?.Drafted ?? false) ? 8 : 0);
            }

            return ((mp.Position.x % 50) * 50 + mp.Position.z % 50)
                + (mp.stackCount + pawnFlags) * 10000
                + mp.HitPoints * 100000
                + mp.Rotation.AsByte * 2500;
        }

        public override string ToString()
        {
            return $"(hostId={HostThingID}, Pos=({Position.x},{Position.z}), HitP={HitPoints}, Cnt={StackCount} {DownState})";
        }
    }
}