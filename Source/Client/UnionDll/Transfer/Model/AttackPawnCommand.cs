using System;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class AttackPawnCommand
    {
        public enum PawnCommand : byte
        {
            Wait_Combat = 0,
            Goto, // йти
            Attack, // стріляти
            AttackMelee, // бити впритул
            Equip, // взяти як зброю
            TakeInventory, // взяти в інвентар
            Wear, // одягти
            DropEquipment, // скинути зброю
            RemoveApparel, // зняти одяг
            Ingest, // з'їсти
            Strip, // роздягти труп
            TendPatient, // надати медичну допомогу
            OC_InventoryDrop // спеціальна команда на скидання речі з інвентаря
        }

        public int HostPawnID { get; set; }
        public PawnCommand Command { get; set; }
        public IntVec3S TargetPos { get; set; }
        public int TargetID { get; set; }
        public string TargetDefName { get; set; }

    }
}
