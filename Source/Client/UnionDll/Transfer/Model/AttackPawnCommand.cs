using System;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class AttackPawnCommand
    {
        public enum PawnCommand : byte
        {
            Wait_Combat = 0,
            Goto = 1,
            Attack = 2,
            AttackMelee = 3,
            Equip = 4,
            TakeInventory = 5,
            Wear = 6,
            DropEquipment = 7,
            RemoveApparel = 8,
            Ingest = 9,
            Strip = 10,
            TendPatient = 11,
            OC_InventoryDrop = 12,

            // Нові команди для детермінованого Lockstep PvP («Гібридний міст»)
            UniversalJob = 20,
            SetDrafted = 21,
            SetHostility = 22,
            CastVerb = 23,
            CastAbility = 24
        }

        public int HostPawnID { get; set; }
        public PawnCommand Command { get; set; }
        public IntVec3S TargetPos { get; set; }
        public int TargetID { get; set; }
        public string TargetDefName { get; set; }

        // Додаткові поля для Lockstep-синхронізації та DLC (Biotech / Ideology / Royalty)
        public long ScheduledTick { get; set; }
        public string JobDefName { get; set; }
        public int TargetIDB { get; set; }
        public IntVec3S TargetPosB { get; set; }
        public int Count { get; set; } = -1;
        public bool DraftState { get; set; }
        public byte HostilityMode { get; set; }
        public string AbilityDefName { get; set; }
    }
}