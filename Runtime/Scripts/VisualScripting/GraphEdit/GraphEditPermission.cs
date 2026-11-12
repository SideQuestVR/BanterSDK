using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Who may change a world's script machines from its page at run time: the SCRIPT_GRAPH requests
    /// (<c>BS.ScriptGraphBridge</c>) and <see cref="BSScriptGraph"/>'s methods. The machines the world brought with it
    /// are the host's to grant (in the app: the world's owner and staff, the people who can open the scene editor).
    /// Anyone else may only change the machines the page made itself, which is all a visitor's copy of a saved scene
    /// needs. With no host answer, nobody may change the world's own machines.
    /// </summary>
    public static class GraphEditPermission
    {
        /// <summary>
        /// The host's answer for the local player in the current space: may they change the world's own machines? Set
        /// by the app; unset means no. Main thread.
        /// </summary>
        public static Func<bool> CanEditWorldGraphs;

        static readonly HashSet<ScriptMachine> pageMade = new HashSet<ScriptMachine>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static int pageCalls;

        /// <summary>Whether the host lets the local player change the world's own machines.</summary>
        public static bool HostAllows()
        {
            var answer = CanEditWorldGraphs;
            if (answer == null) return false;
            try { return answer(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>A machine the page made (it adds nothing the world brought): the page may change it.</summary>
        public static void NotePageMade(ScriptMachine machine)
        {
            if (machine == null) return;
            if (pageMade.Count >= 256) pageMade.RemoveWhere(m => m == null);
            pageMade.Add(machine);
        }

        public static bool IsPageMade(ScriptMachine machine) => machine != null && pageMade.Contains(machine);

        /// <summary>May the page change <paramref name="machine"/>: one it made, or any if the host allows.</summary>
        public static bool MayChange(ScriptMachine machine) => machine != null && (IsPageMade(machine) || HostAllows());

        /// <summary>
        /// Inside a page's request (<see cref="ScriptGraphSessionManager"/>): references are resolved for the page.
        /// Nested scopes count; dispose to leave.
        /// </summary>
        public static IDisposable PageCall()
        {
            pageCalls++;
            return new Scope();
        }

        public static bool InPageCall => pageCalls > 0;

        /// <summary>One warning per kind of refusal, so a page in a loop can't fill the log.</summary>
        public static void WarnOnce(string what)
        {
            if (warned.Count < 64 && warned.Add(what))
                Debug.LogWarning($"[ScriptGraph] Refused {what}. A world's page can change only the graphs it made; the world's own graphs are its owner's to change.");
        }

        sealed class Scope : IDisposable
        {
            bool done;

            public void Dispose()
            {
                if (done) return;
                done = true;
                pageCalls--;
            }
        }
    }
}
