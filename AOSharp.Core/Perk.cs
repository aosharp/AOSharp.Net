using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Common.Helpers;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core.UI;

namespace AOSharp.Core
{
    public class Perk
    {
        public int Instance;
        public int TemplateInstance;
        public int PrerequisitePerkInstance;
        public PerkType PerkType;
        public ProfessionFlag AllowedProfessions;
        public int ActionInstance;
        public int RequiredExperience;

        public int Level { get; private set; }
        public string Name { get; private set; }
        public PerkLine PerkLine { get; private set; }

        private static Dictionary<int, string> _perkNameCache = new Dictionary<int, string>();
        private static Dictionary<int, PerkLine> _perkLineCache = new Dictionary<int, PerkLine>();

        private Perk(int instance, int templateInstance, int prerequisitePerkInstance, int perkType,
            int allowedProfessions, int actionInstance, int requiredExperience)
        {
            Instance = instance;
            TemplateInstance = templateInstance;
            PrerequisitePerkInstance = prerequisitePerkInstance;
            PerkType = (PerkType)perkType;
            AllowedProfessions = (ProfessionFlag)allowedProfessions;
            ActionInstance = actionInstance;
            RequiredExperience = requiredExperience;

            Name = GetName();
            PerkLine = GetPerkLine();
        }

        public static Perk GetByInstance(int instance)
        {
            return GetFullPerkMap().FirstOrDefault(p => p.Instance == instance);
        }

        public static List<Perk>GetByInstance(IEnumerable<int> instances)
        {
            return GetFullPerkMap().Where(perk => instances.Contains(perk.Instance)).ToList();
        }
        
        public static Dictionary<PerkLine, int> GetPerkLineLevels(bool includeAllPerkLines = false)
        {
            return GetFullPerkMap()
                .Where(p => includeAllPerkLines || p.IsTrained())
                .GroupBy(p => p.PerkLine)
                .ToDictionary(g => g.Key, g => g
                    .Where(p => p.IsTrained())
                    .Max(p => (int?) p.Level) ?? 0);
        }
        
        public static int GetPerkLineLevel(PerkLine perkLine)
        {
            List<Perk> trainedPerksInLine = GetFullPerkMap()
                .Where(p => p.PerkLine == perkLine)
                .Where(p => p.IsTrained())
                .OrderBy(p => p.Instance)
                .ToList();
                
            if (trainedPerksInLine.Count == 0)
                return 0;

            return trainedPerksInLine.Max(p => p.Level);
        }
        
        public static bool IsTrained(PerkLine perkLine, int level)
        {
            return GetFullPerkMap()
                .Any(p => p.PerkLine == perkLine && p.Level == level && p.IsTrained());
        }

        public bool IsTrained()
        {
            return N3EngineClientAnarchy.HasPerk(Instance);
        }

        private string GetName()
        {
            if (!_perkNameCache.TryGetValue(Instance, out string name))
            {
                name = N3EngineClientAnarchy.GetName(new Identity(IdentityType.None, TemplateInstance));
                _perkNameCache[Instance] = name;
            }

            return name;
        }

        private PerkLine GetPerkLine()
        {
            if (!_perkLineCache.TryGetValue(Instance, out PerkLine line))
            {
                string sanitizedName = Name.Replace(" ", "").Replace("-", "").Replace("'", "");
                line = (PerkLine)Enum.Parse(typeof(PerkLine), sanitizedName);
                _perkLineCache[Instance] = line;
            }

            return line;
        }

        private static unsafe List<Perk> GetFullPerkMap()
        {
            List<Perk> perks = new List<Perk>();

            IntPtr pEngine = N3Engine_t.GetInstance();
            
            if (pEngine == IntPtr.Zero)
                return perks;
            
            foreach (PerkMemStruct perkMemStruct in N3EngineClientAnarchy_t.GetFullPerkMap(pEngine)->ToList<PerkMemStruct>())
            {
                Perk perk = new Perk(perkMemStruct.Instance, perkMemStruct.TemplateInstance,
                    perkMemStruct.PrerequisitePerkInstance, perkMemStruct.PerkType, perkMemStruct.AllowedProfessions,
                    perkMemStruct.ActionInstance, perkMemStruct.RequiredExperience);

                perks.Add(perk);
            }

            // I'm not doing the following in the Perk constructor because we need the full perk 
            // map to do it and querying the entire perk map again in the constructor is too expensive

            string currentName = "NoName";
            int currentLevel = 1;

            foreach (Perk perk in perks.OrderBy(perk => perk.Instance))
            {
                if (perk.Name != currentName)
                {
                    currentName = perk.Name;
                    currentLevel = 1;
                }

                perk.Level = currentLevel;

                currentLevel++;
            }

            return perks;
        }
        
        private struct PerkMemStruct
        {
            public int Instance;
            public int TemplateInstance;
            public int PrerequisitePerkInstance;
            public int PerkType;
            public int AllowedProfessions;
            public int ActionInstance;
            public int RequiredExperience;
        }
    }

    [Flags]
    public enum PerkType : uint
    {
        Shadowlands = BitFlag.None,
        ShadowBreed = BitFlag.Bit0,
        AlienInvasion = BitFlag.Bit1,
        BreedSolitus = BitFlag.Bit2,
        BreedOpifex = BitFlag.Bit3,
        BreedNanomage = BitFlag.Bit4,
        BreedAtrox = BitFlag.Bit5,
        PersonalResearch = BitFlag.Bit6,
        GlobalResearch1 = BitFlag.Bit7,
        GlobalResearch2 = BitFlag.Bit8,
    }
}
