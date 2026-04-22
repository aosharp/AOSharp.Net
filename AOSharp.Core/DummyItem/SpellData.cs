using AOSharp.Common.GameData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core
{
    public class SpellData
    {
        public readonly SpellFunction Function;
        public readonly Dictionary<SpellPropertyOperator, int> Properties;

        private SpellData(SpellFunction func, Dictionary<SpellPropertyOperator, int> props)
        {
            Function = func;
            Properties = props;
        }

        public static SpellData New(SpellFunction func, Dictionary<SpellPropertyOperator, int> props)
        {
            switch(func)
            {
                case SpellFunction.Hit:
                    if ((Stat)props[SpellPropertyOperator.Stat] == Stat.Health)
                    {
                        if (props[SpellPropertyOperator.Min] < 0 && props[SpellPropertyOperator.Max] < 0)
                        {
                            return props[SpellPropertyOperator.Duration] > 1 ? (SpellData)new DamageOverTime(func, props) : new Damage(func, props);
                        }
                        else
                        {
                            return props[SpellPropertyOperator.Duration] > 1 ? (SpellData)new HealingOverTime(func, props) : new Healing(func, props);
                        }
                    }
                    break;
            }

            return new SpellData(func, props);
        }

        public class SingleHitSpellData : SpellData
        {
            public int Min => Properties[SpellPropertyOperator.Min];
            public int Max => Properties[SpellPropertyOperator.Max];
            public int Average => (Min + Max) / 2;

            public Stat DamageType => (Stat)Properties[SpellPropertyOperator.DamageType];

            public SpellModifierTarget ApplyOn => (SpellModifierTarget)Properties[SpellPropertyOperator.ApplyOn];

            internal SingleHitSpellData(SpellFunction func, Dictionary<SpellPropertyOperator, int> props) : base(func, props)
            {
            }
        }

        public class HitOverTimeSpellData : SingleHitSpellData
        {
            public int ElaspedMin => Min * Ticks;
            public int ElaspedMax => Max * Ticks;
            public int ElaspedAverage => Average * Ticks;
            public int Ticks => Properties[SpellPropertyOperator.Duration];
            public float Interval => Properties[SpellPropertyOperator.Interval] / 100;
            public float Duration => Interval * Ticks;

            internal HitOverTimeSpellData(SpellFunction func, Dictionary<SpellPropertyOperator, int> props) : base(func, props)
            {
            }
        }

        public class Healing : SingleHitSpellData
        {
            internal Healing(SpellFunction func, Dictionary<SpellPropertyOperator, int> props) : base(func, props)
            {
            }
        }

        public class Damage : SingleHitSpellData
        {
            internal Damage(SpellFunction func, Dictionary<SpellPropertyOperator, int> props) : base(func, props)
            {
            }
        }

        public class DamageOverTime : HitOverTimeSpellData
        {
            internal DamageOverTime(SpellFunction func, Dictionary<SpellPropertyOperator, int> props) : base(func, props)
            {
            }
        }

        public class HealingOverTime : HitOverTimeSpellData
        {
            internal HealingOverTime(SpellFunction func, Dictionary<SpellPropertyOperator, int> props) : base(func, props)
            {
            }
        }
    }

    public struct SpellProperty
    {
        public SpellPropertyOperator Operator;
        public int Value;
    }
}
