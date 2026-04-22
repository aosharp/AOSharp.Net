using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AOSharp.Common.GameData;
using AOSharp.Core.Combat;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;

namespace AOSharp.Core
{
    public class PerkAction : DummyItem, ICombatAction, IEquatable<PerkAction>
    {
        private const float PERK_TIMEOUT = 1;

        public unsafe bool IsAvailable => GetIsAvailable();

        public bool IsPending => _pendingQueue.FirstOrDefault(x => x.Id == Id) != null;
        public bool IsExecuting => _executingQueue.FirstOrDefault(x => x.Id == Id) != null;
        public PerkLine PerkLine => Perk.GetByInstance(Id).PerkLine;
        public readonly PerkHash Hash;

        public static List<PerkAction> List => GetPerkActions();
        private static Queue<QueueItem> _pendingQueue = new Queue<QueueItem>();
        private static Queue<QueueItem> _executingQueue = new Queue<QueueItem>();

        private PerkAction(Identity identity, int hashInt) : base(GetPtr(identity))
        {
            Hash = (PerkHash)hashInt;
        }

        public bool Use(bool packetOnly = false)
        {
            return Use(DynelManager.LocalPlayer, packetOnly);
        }

        public unsafe bool Use(SimpleChar target, bool setTarget = false, bool packetOnly = false)
        {
            if (target == null)
                target = DynelManager.LocalPlayer;

            if (setTarget)
                target.Target();

            if (packetOnly)
            {
                Network.Send(new CharacterActionMessage()
                {
                    Action = CharacterActionType.UsePerk,
                    Target = target.Identity,
                    Parameter1 = Id,
                    Parameter2 = (int)Hash
                });

                EnqueuePendingPerk(this);

                return true;
            } else
            {
                IntPtr pEngine = N3Engine_t.GetInstance();

                if (pEngine == IntPtr.Zero)
                    return false;

                Identity identity = new Identity(IdentityType.SpecialAction, Id);
                return N3EngineClientAnarchy_t.PerformSpecialAction(pEngine, ref identity);
            }
        }

        private bool GetIsAvailable()
        {
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return false;

            Identity identity = new Identity(IdentityType.SpecialAction, Id);
            return N3EngineClientAnarchy_t.GetSpecialActionState(pEngine, ref identity) == SpecialActionState.Ready;
        }

        public static bool Find(int id, out PerkAction perkAction)
        {
            return (perkAction = List.FirstOrDefault(x => x.Id == id)) != null;
        }

        public static bool Find(string name, out PerkAction perkAction)
        {
            return (perkAction = List.FirstOrDefault(x => x.Name == name)) != null;
        }

        public static bool Find(PerkHash hash, out PerkAction perkAction)
        {
            return (perkAction = List.FirstOrDefault(x => x.Hash == hash)) != null;
        }

        private static void EnqueuePendingPerk(PerkAction perkAction)
        {
            _pendingQueue.Enqueue(new QueueItem
            {
                Id = perkAction.Id,
                AttackTime = perkAction.AttackDelay,
                Timeout = Time.NormalTime + PERK_TIMEOUT
            });
        }

        private static unsafe List<PerkAction> GetPerkActions()
        {
            List<PerkAction> perks = new List<PerkAction>();
            IntPtr pEngine = N3Engine_t.GetInstance();

            if (pEngine == IntPtr.Zero)
                return perks;

            foreach (IntPtr pAction in N3EngineClientAnarchy_t.GetSpecialActionList(pEngine)->ToList())
            {
                SpecialActionMemStruct specialAction = *(SpecialActionMemStruct*)pAction;

                if (specialAction.Identity.Type != IdentityType.PerkHash)
                    continue;

                perks.Add(new PerkAction(specialAction.TemplateIdentity, specialAction.Identity.Instance));
            }

            return perks.OrderBy(perk => perk.Id).ToList();
        }

        internal static void Update()
        {
            try
            {
                while(_pendingQueue.Count > 0 && _pendingQueue.Peek().Timeout <= Time.NormalTime)
                    _pendingQueue.Dequeue();

                while (_executingQueue.Count > 0 && _executingQueue.Peek().Timeout <= Time.NormalTime)
                    _executingQueue.Dequeue();
            }
            catch (Exception e)
            {
                Chat.WriteLine($"This shouldn't happen pls report (Perk): {e.Message}");
            }
        }

        internal static void OnPerkFinished(int lowId, int highId, int ql, Identity owner)
        {
            //Will have to implement this some other way
            /*
            PerkExecuted?.Invoke(null, new PerkExecutedEventArgs
            {
                OwnerIdentity = owner,
                Owner = DynelManager.GetDynel(owner)?.Cast<SimpleChar>(),
                Perk = perk
            });
            */

            if (owner != DynelManager.LocalPlayer.Identity)
                return;

            _executingQueue.Dequeue();

            if (TryGet(lowId, highId, ql, out ACGItem perkDummyItem))
                CombatHandler.Instance?.OnPerkExecuted(perkDummyItem);
        }

        internal static void OnPerkQueued()
        {
            PerkAction perkAction;
            if (_pendingQueue.Count == 0 || !Find(_pendingQueue.Dequeue().Id, out perkAction))
                return;

            //Calc time offset of perks before this one in queue.
            float queueOffset = _executingQueue.Sum(x => x.AttackTime);
            double nextTimeout = Time.NormalTime + perkAction.AttackDelay + PERK_TIMEOUT + queueOffset;

            _executingQueue.Enqueue(new QueueItem
            {
                Id = perkAction.Id,
                AttackTime = perkAction.AttackDelay,
                Timeout = nextTimeout
            });

            CombatHandler.Instance?.OnPerkLanded(perkAction, nextTimeout);
        }

        private static void OnClientPerformedSpecialAction(Identity identity)
        {
            PerkAction perkAction;
            if (!Find(identity.Instance, out perkAction))
                return;

            EnqueuePendingPerk(perkAction);
        }

        public bool Equals(PerkAction other)
        {
            if (object.ReferenceEquals(other, null))
                return false;

            return Id == other.Id;
        }

        public static bool operator ==(PerkAction a, PerkAction b)
        {
            if (object.ReferenceEquals(a, null))
                return object.ReferenceEquals(b, null);

            return a.Equals(b);
        }

        public static bool operator !=(PerkAction a, PerkAction b) => !(a == b);

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        private struct SpecialActionMemStruct
        {
            [FieldOffset(0x8)]
            public Identity TemplateIdentity;

            [FieldOffset(0x10)]
            public Identity Identity;

            [FieldOffset(0x24)]
            public bool IsOnCooldown;
        }

        private class QueueItem
        {
            public int Id;
            public float AttackTime;
            public double Timeout;
        }
    }

    public class PerkExecutedEventArgs : EventArgs
    {
        public SimpleChar Owner { get; set; }
        public Identity OwnerIdentity { get; set; }
        public PerkAction PerkAction { get; set; }
    }
}
