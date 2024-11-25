using System;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>
    /// DataBridge exists to give the SDK access to data
    /// that can't be accessed outside the Banter client itself.
    /// </summary>
    public class DataBridge
    {
        public Func<bool> IsSpaceFavourited = () => false;
    }
}
