#nullable enable
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
#if NET3
using Path = Net3_Proxy.Path;
#endif

namespace Banter.Utilities.Async
{
    /// <summary>
    /// Provides some basic utility methods and properties of Beat Saber
    /// </summary>
    public static class UnityGame
    {
        private static Thread? mainThread;
        /// <summary>
        /// Checks if the currently running code is running on the Unity main thread.
        /// </summary>
        /// <value><see langword="true"/> if the curent thread is the Unity main thread, <see langword="false"/> otherwise</value>
        public static bool OnMainThread => Environment.CurrentManagedThreadId == mainThread?.ManagedThreadId;

        internal static void SetMainThread()
            => mainThread = Thread.CurrentThread;
        }
    }
