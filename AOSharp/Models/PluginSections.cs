using System;

namespace AOSharp.Models
{
    public static class PluginSections
    {
        public const string Library = "Library";
        public const string Utility = "Utility";
        public const string Combat = "Combat";
        public const string Bots = "Bots";
        public const string Other = "Other";

        public static readonly string[] DisplayOrder =
        {
            Library,
            Utility,
            Combat,
            Bots,
            Other
        };

        public static string Normalize(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                return Other;

            foreach (var valid in DisplayOrder)
            {
                if (string.Equals(valid, section.Trim(), StringComparison.OrdinalIgnoreCase))
                    return valid;
            }

            return Other;
        }
    }
}
