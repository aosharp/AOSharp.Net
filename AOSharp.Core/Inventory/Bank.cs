using AOSharp.Common.GameData;
using AOSharp.Core.GameData;
using AOSharp.Common.Unmanaged.Imports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core.Inventory
{
    public class Bank : ItemHolder
    {
        public override List<Item> Items => Inventory.GetBankItems();
        public List<Backpack> Backpacks => Items.Where(x => x.UniqueIdentity.Type == IdentityType.Container).Select(x => new Backpack(x.UniqueIdentity, x.Slot)).ToList();
        public bool IsOpen => Inventory.GetBankInventoryEntry() != IntPtr.Zero;
        public Identity Identity = new Identity(IdentityType.Bank, Game.ClientInst);

        public bool FindContainer(string name, out Backpack backpack)
        {
            return (backpack = Backpacks.FirstOrDefault(x => x.Name == name)) != null;
        }
    }
}
