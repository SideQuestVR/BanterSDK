using System;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// The half of the persisted-file units that both of them share: sanitise, call the bridge,
    /// and raise the result on the event bus.
    /// </summary>
    /// <remarks>
    /// It is a static helper rather than a base Unit because the two units differ in their ports
    /// and a shared base class in this codebase would show up in the node catalogue as an abstract
    /// entry the palette then has to filter out.
    ///
    /// Nothing here throws into the flow: these run detached from the graph that started them, so
    /// a fault has nowhere to surface except the event and the log.
    /// </remarks>
    static class PersistFileUnitSupport
    {
        internal static async Task Run(string op, string name, string data, string eventHook)
        {
            string fileName = null;
            PersistFileResult result;
            try
            {
                fileName = BSScene.SanitisePersistFileName(name);
                if (fileName == null)
                {
                    result = PersistFileResult.Unavailable("'" + name + "' has no usable characters for a file name");
                }
                else
                {
                    result = await BSScene.Instance().data.PersistFile(new PersistFileRequest
                    {
                        Op = op,
                        Name = fileName,
                        Data = data,
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BS] PersistFile " + op + " '" + name + "' failed: " + e.Message);
                result = new PersistFileResult { ok = false, error = e.Message };
            }

            result ??= PersistFileResult.Unavailable("no result");

            // Arguments are positional and must stay in this order — the event units read them by
            // index: the bare name the graph asked for, success, the contents (get) or "" (set),
            // and the failure reason or "".
            EventBus.Trigger(eventHook, new CustomEventArgs(eventHook, new object[]
            {
                name ?? "",
                result.ok,
                result.data ?? "",
                result.error ?? result.reason ?? "",
            }));
        }
    }
}
