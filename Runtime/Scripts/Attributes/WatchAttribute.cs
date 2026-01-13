using System;
using System.Runtime.CompilerServices;

namespace Banter.SDK
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class WatchAttribute : Attribute
    {
        public string propertyName;
        public string initial;
        public bool isAssetReference;
        public WatchAttribute([CallerMemberName] string propertyName = "", string initial = "", bool isAssetReference = false)
        {
            this.propertyName = propertyName;
            this.initial = initial;
            this.isAssetReference = isAssetReference;
        }
    }
}
