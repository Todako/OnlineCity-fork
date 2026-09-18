using RimWorld;
using System;
using Verse;

namespace OCUnion.Transfer.Model
{
    /// <summary>
    /// Контейнер для збереження та передачі мережею поточного фізичного стану об'єкта карти
    /// </summary>
    [Serializable]
    public class AttackThingState
    {
        /// <summary>
        /// Стан здоров'я та дієздатності пішака
        /// </summary>
        [Serializable]
        public enum PawnHealthState : byte
        {
            /// <summary>
            /// Мертвий
            /// </summary>
            Dead = 0,

            /// <summary>
            /// Знерухомлений / без тями
            /// </summary>
            Down = 1,

            /// <summary>
            /// Дієздатний / на ногах
            /// </summary>
            Mobile = 2
        }

        /// <summary>
        /// Ідентифікатор об'єкта на карті хоста
        /// </summary>
        public int HostThingID;

        /// <summary>
        /// Поточна позиція на карті
        /// </summary>
        public IntVec3S Position;

        /// <summary>
        /// Кількість предметів у стаку
        /// </summary>
        public int StackCount;

        /// <summary>
        /// Очки міцності (HitPoints) або масштаб полум'я для вогню
        /// </summary>
        public int HitPoints;

        /// <summary>
        /// Поточний стан мобільності пішака
        /// </summary>
        public PawnHealthState DownState;

        public AttackThingState()
        {
        }

        public AttackThingState(Thing mp)
        {
            if (mp == null) return;

            // Створений тут стан застосовується у GameUtils.ApplyState
            HostThingID = mp.thingIDNumber;
            StackCount = mp.stackCount;
            Position = new IntVec3S(mp.Position);

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
                DownState = (PawnHealthState)(int)pawn.health.State;
            }
            else
            {
                DownState = PawnHealthState.Mobile;
            }
        }

        /// <summary>
        /// Швидкий розрахунок просторового хешу для виявлення змін об'єкта
        /// </summary>
        public static int GetHash(Thing mp)
        {
            if (mp == null) return 0;

            unchecked
            {
                int pawnState = (mp is Pawn pawn) ? (int)pawn.health.State : 0;

                return ((mp.Position.x % 50) * 50 + mp.Position.z % 50)
                    + (mp.stackCount + pawnState) * 10000
                    + mp.HitPoints * 100000;
            }
        }

        public override string ToString()
        {
            return $"(hostId={HostThingID}, Pos=({Position.x},{Position.z}), HitP={HitPoints}, Cnt={StackCount} {DownState})";
        }
    }
}
