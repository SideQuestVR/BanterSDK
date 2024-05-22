using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Banter.SDK
{
    public class KeyedCountingLogger
    {
        public string Name { get; private set; }
        public KeyedCountingLogger(string name)
        {
            Name = name;
        }
        private System.Collections.Generic.Dictionary<string, CountingLogger> dict = new System.Collections.Generic.Dictionary<string, CountingLogger>();

        public void Add(string key, int num = 1)
        {
            if (!dict.TryGetValue(key, out var l))
            {
                l = new CountingLogger($"{Name}[{key}]");
                dict.Add(key, l);
            }
            l.Add(num);
        }
    }

    public class CountingLogger
    {

        System.Diagnostics.Stopwatch sw;
        public CountingLogger(string name)
        {
            this.Name = name;
            sw = new System.Diagnostics.Stopwatch();
        }

        private const double LOG_INTERVAL_SEC = 1;
        public string Name { get; private set; }

        private long totalCount = 0;
        // private double firstStartStamp = 0;

        private double lastStamp = 0;
        private long lastCount = 0;

        private bool started = false;

        private void DumpLog(double nextStamp)
        {
            var delta = nextStamp - lastStamp;
            string logstr = $"Counter {Name}: {lastCount} in {delta:F2} ({(lastCount / delta):F2}/sec), {totalCount} total";
            lastCount = 0;
            lastStamp = nextStamp;
            Debug.Log(logstr);
        }


        public void Add(int num = 1)
        {
            try
            {
                totalCount += num;
                lastCount += num;

                if (!started)
                {
                    started = true;
                    sw.Start();
                    // firstStartStamp = 0;
                    lastStamp = 0;
                }

                double stamp = (double)sw.ElapsedMilliseconds / (double)1000;

                if ((stamp - lastStamp) >= LOG_INTERVAL_SEC)
                {
                    DumpLog(stamp);
                }

            }
            catch (Exception e)
            {
                Debug.LogError("Exception");
                Debug.LogError(e);
                throw e;
            }
        }


    }
}
