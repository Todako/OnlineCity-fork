using Model;
using System;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class AttackCorpse
    {
        public ThingEntry CorpseWithPawn { get; set; }
        public int CorpseId { get; set; }
        public int PawnId { get; set; }
    }
}
