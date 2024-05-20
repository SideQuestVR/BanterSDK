using System;
using System.Runtime.CompilerServices;

namespace Banter
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class WatchAttribute : Attribute
    {
        public string propertyName;
        public string initial;
        public WatchAttribute([CallerMemberName] string propertyName = "", string initial = "")
        {
            this.propertyName = propertyName;
            this.initial = initial;
        }
    }
}
