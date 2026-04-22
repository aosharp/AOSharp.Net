using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;

namespace AOSharp.Core.UI.Options
{
    public class MenuBool : MenuItem
    {
        public bool Value { get; set; }

        public MenuBool(string name, string displayName, bool defaultValue = false) : base(name, displayName)
        {
            Value = defaultValue;
        }

        internal override View CreateView()
        {
            Checkbox checkbox = Checkbox.Create(Name, DisplayName, Value, true);

            checkbox.Toggled += (sender, enabled) =>
            {
                Value = enabled;
            };

            return checkbox;
        }
    }
}
