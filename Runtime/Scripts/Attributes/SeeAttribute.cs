using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Banter
{
    [AttributeUsage(AttributeTargets.Property|AttributeTargets.Field)]
    public class SeeAttribute : Attribute
    {
        public string propertyName;
        public string initial;
        public SeeAttribute([CallerMemberName] string propertyName = "", string initial = "") {
            this.propertyName = propertyName;
            this.initial = initial;
        }
    }
}
