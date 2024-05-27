using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Banter.SDK
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MethodAttribute : Attribute
    {
        public string methodName;
        public string overload;
        public MethodAttribute([CallerMemberName] string methodName = "", string overload = "")
        {
            this.methodName = methodName;
            this.overload = overload;
        }
    }
}
