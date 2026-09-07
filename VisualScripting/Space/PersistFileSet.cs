using Unity.VisualScripting;
using BS;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    /// <summary>
    /// Writes a string to this world's <c>__persisted_&lt;name&gt;.json</c>.
    /// </summary>
    /// <remarks>
    /// Fires and returns immediately, like Base64ToCDN — the write is an authenticated HTTP round
    /// trip and a graph must not block on it. The outcome arrives on the
    /// <see cref="OnPersistFileSaved"/> event unit, matched by the same name.
    ///
    /// Only the world's OWNER can write; for anyone else this reports a failure on that event
    /// rather than silently doing nothing.
    /// </remarks>
    [UnitTitle("Persist File Set")]
    [UnitShortTitle("PersistFileSet")]
    [UnitCategory("BS\\World")]
    [TypeIcon(typeof(BSObjectId))]
    public class PersistFileSet : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput fileName;

        [DoNotSerialize]
        public ValueInput data;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _fileName = flow.GetValue<string>(fileName);
                var _data = flow.GetValue<string>(data);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(
                    () => PersistFileUnitSupport.Run("set", _fileName, _data, "OnPersistFileSaved"),
                    $"{nameof(PersistFileSet)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            fileName = ValueInput("Name", "");
            data = ValueInput("Data", "");
        }
    }
}
