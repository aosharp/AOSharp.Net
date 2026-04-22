using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AOSharp.Core
{
    public class Coroutine
    {
        public double ExecutionTime;
        public Action Action;

        public Coroutine(double executionTime, Action action)
        {
            ExecutionTime = executionTime;
            Action = action;
        }

        private static List<Coroutine> _coroutines = new List<Coroutine>();

        public static void ExecuteAfter(int delay, Action action)
        {
            Coroutine coroutine = new Coroutine(Time.NormalTime + delay / 1000f, action);
            _coroutines.Add(coroutine);
        }

        internal static void Update()
        {
            foreach(Coroutine coroutine in _coroutines.Where(x => Time.NormalTime >= x.ExecutionTime).ToList())
            {
                coroutine.Action?.Invoke();

                _coroutines.Remove(coroutine);
            }
        }
    }
}
