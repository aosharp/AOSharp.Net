using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core
{
    public static class Research
    {
        public static List<ResearchGoal> Goals => N3EngineClientAnarchy.GetPersonalResearchGoals();
        public static List<uint> Completed => N3EngineClientAnarchy.GetCompletedPersonalResearchGoals();

        public static void Train(int researchId) => DynelManager.LocalPlayer.SetStat(Stat.PersonalResearchGoal, researchId);
    }
}
