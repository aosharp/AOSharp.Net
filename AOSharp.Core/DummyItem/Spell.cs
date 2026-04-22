using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.Combat;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using AOSharp.Core.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using AOSharp.Core.UI;

namespace AOSharp.Core
{
    public class Spell : NanoItem, ICombatAction
    {
        private const float CAST_TIMEOUT = 0.5f;

        public bool IsReady => GetIsReady();

        public static IEnumerable<Spell> List => GetSpellList();
        public static bool HasPendingCast => _pendingCast.Spell != null;
        private static (Spell Spell, double Timeout) _pendingCast;

        internal Spell(Identity identity) : base(identity)
        {
        }

        public static bool Find(int id, out Spell spell)
        {
            return (spell = List.FirstOrDefault(x => x.Id == id)) != null;
        }

        public static bool Find(string name, out Spell spell)
        {
            return (spell = List.FirstOrDefault(x => x.Name == name)) != null;
        }
        public void Cast(bool setTarget = false)
        {
            Cast(DynelManager.LocalPlayer, setTarget);
        }

        public void Cast(SimpleChar target, bool setTarget = false)
        {
            if (target == null)
                target = DynelManager.LocalPlayer;

            if(setTarget)
                target.Target();

            Network.Send(new CharacterActionMessage()
            {
                Action = CharacterActionType.CastNano,
                Target = target.Identity,
                Parameter1 = (int)IdentityType.NanoProgram,
                Parameter2 = Id
            });

            _pendingCast = (this, Time.NormalTime + CAST_TIMEOUT);
        }

        private unsafe bool GetIsReady()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return false;

            Identity identity = new Identity(IdentityType.NanoProgram, Id);
            return N3EngineClientAnarchy_t.IsFormulaReady(pEngine, ref identity);
        }

        internal static void Update()
        {
            try
            {
            if (_pendingCast.Spell != null && _pendingCast.Timeout <= Time.NormalTime)
                _pendingCast.Spell = null;
            }
            catch (Exception e)
            {
                Chat.WriteLine($"This shouldn't happen pls report (Spell): {e.Message}");
            }
        }

        public static Spell[] GetSpellsForNanoline(NanoLine nanoline)
        {
            return List.Where(x => x.Nanoline == nanoline).ToArray();
        }

        private static unsafe Spell[] GetSpellList()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return new Spell[0];

            return N3EngineClientAnarchy_t.GetNanoSpellList(pEngine)->ToList().Select(x => new Spell(new Identity(IdentityType.NanoProgram, (*(MemStruct*)x).Id))).ToArray();
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Spell);
        }

        public bool Equals(Spell other)
        {
            if (object.ReferenceEquals(other, null))
                return false;

            return Id == other.Id;
        }

        public override int GetHashCode()
        {
            return 91194611 + Id.GetHashCode();
        }

        public static bool operator ==(Spell a, Spell b)
        {
            if (object.ReferenceEquals(a, null))
                return object.ReferenceEquals(b, null);

            return a.Equals(b);
        }


        public static bool operator !=(Spell a, Spell b) => !(a == b);

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        private struct MemStruct
        {
            [FieldOffset(0x08)]
            public int Id;
        }
    }
}
