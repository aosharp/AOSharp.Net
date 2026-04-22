using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Interfaces;

namespace AOSharp.Core.Inventory
{
    public class Backpack : Container
    {
        public string Name => InventoryGUIModule.GetBackpackName(Identity);
        public readonly Identity Slot;

        internal Backpack(Identity identity, Identity slot) : base(identity)
        {
            Slot = slot;
        }

        public void SetName(string name)
        {
            InventoryGUIModule.SetBackpackName(Identity, name);
        }
    }
}
