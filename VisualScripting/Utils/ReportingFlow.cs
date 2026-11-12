using System;
using Unity.VisualScripting;
using UnityEngine;

namespace BS.VisualScripting
{
    /// <summary>
    /// A flow for a unit that reads its ports away from a running graph, as an event does when its graph starts listening.
    /// A port that throws stops here and is logged, so the rest of the graph still starts.
    /// </summary>
    internal static class ReportingFlow
    {
        public static void Run(GraphReference reference, IGraphElement source, Action<Flow> body)
        {
#if GREENFIELD_PROJECT
            GraphErrorThrottle.RunFlow(reference, source, body);
#else
            // Stock Visual Scripting. This runs once each time a graph starts listening, so a plain log is enough.
            var flow = Flow.New(reference);

            try
            {
                body(flow);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, reference.component);
            }
            finally
            {
                flow.Dispose();
            }
#endif
        }
    }
}
