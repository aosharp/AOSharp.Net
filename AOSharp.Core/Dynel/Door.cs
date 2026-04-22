using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Imports.Gamecode;
using AOSharp.Common.Unmanaged.Interfaces;

namespace AOSharp.Core
{
    public class Door : LockableItem
    {
        //public bool IsOpen => Playfield.IsDoorOpenBetweenRooms(Link1, Link2);

        public Room RoomLink1 => Playfield.IsDungeon && Link1 >= 0 ? Playfield.Rooms[Link1] : null;
        public Room RoomLink2 => Playfield.IsDungeon && Link2 >= 0 ? Playfield.Rooms[Link2] : null;

        private unsafe short Link1 => (*(MemStruct*)Pointer).RoomLink1;
        private unsafe short Link2 => (*(MemStruct*)Pointer).RoomLink2;

        public Door(IntPtr pointer) : base(pointer)
        {
        }
        
        public Door(Dynel dynel) : base(dynel.Pointer)
        {
        }

        /*
        private bool GetIsLocked()
        {
            var isLocked = Game.GetVtbl<GamecodeVtblDelegates.IsLockedDelegate>(BasePointer, 63);

            return isLocked(BasePointer);
        }
        */

        public Room GetDestinationRoom(Room originRoom)
        {
            if(originRoom == RoomLink1)
                return RoomLink2;
            else
                return RoomLink1;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        protected new unsafe struct MemStruct
        {
            [FieldOffset(0x120)]
            public short RoomLink1;

            [FieldOffset(0x122)]
            public short RoomLink2;
        }
    }
}
