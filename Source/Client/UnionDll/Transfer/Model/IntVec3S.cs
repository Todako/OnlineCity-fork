using System;
using Verse;

namespace OCUnion.Transfer.Model
{
    /// <summary>
    /// Легковажна серіалізована тривимірна координата (на базі short) для ефективної синхронізації карти
    /// </summary>
    [Serializable]
    public struct IntVec3S : IEquatable<IntVec3S>
    {
        public short x;
        public short y;
        public short z;

        public IntVec3S(short x, short y, short z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public IntVec3S(int x, int y, int z)
        {
            this.x = (short)x;
            this.y = (short)y;
            this.z = (short)z;
        }

        public IntVec3S(IntVec3 vec)
        {
            x = (short)vec.x;
            y = (short)vec.y;
            z = (short)vec.z;
        }

        /// <summary>
        /// Перетворення на внутрішній тип IntVec3 рушія гри
        /// </summary>
        public IntVec3 Get() => new IntVec3(x, y, z);

        public static implicit operator IntVec3(IntVec3S v) => new IntVec3(v.x, v.y, v.z);

        public static explicit operator IntVec3S(IntVec3 v) => new IntVec3S(v);

        public bool Equals(IntVec3S other) => x == other.x && y == other.y && z == other.z;

        public override bool Equals(object obj) => obj is IntVec3S other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = x;
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ z;
                return hash;
            }
        }

        public static bool operator ==(IntVec3S left, IntVec3S right) => left.Equals(right);

        public static bool operator !=(IntVec3S left, IntVec3S right) => !left.Equals(right);

        public override string ToString() => $"({x},{y},{z})";
    }
}
