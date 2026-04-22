using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;

namespace AOSharp.Core.UI.Options
{
    public class Menu : MenuComponent
    {
        internal List<Menu> SubMenus = new List<Menu>();
        private Dictionary<string, MenuItem> _menuItems = new Dictionary<string, MenuItem>();

        public Menu(string name, string displayName) : base(name, displayName)
        {
        }

        public void AddItem(MenuItem item)
        {
            _menuItems.Add(item.Name, item);
        }

        public void AddSubMenu(Menu menu)
        {
            SubMenus.Add(menu);
        }

        public bool GetBool(string itemName)
        {
            return ((MenuBool)_menuItems[itemName]).Value;
        }

        internal override View CreateView()
        {
            View view = View.Create(Rect.Default, $"{Name}View", 5, 0);
            view.SetLayoutNode(VLayoutNode.Create());

            foreach (MenuItem item in _menuItems.Values)
            {
                view.AddChild(item.CreateView(), false);
            }

            return view;
        }
    }
}
