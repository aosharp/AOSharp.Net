using System.Collections.Generic;

namespace AOSharp
{
    public class Loadout
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public List<string> PluginKeys { get; set; } = new List<string>();
    }
}
