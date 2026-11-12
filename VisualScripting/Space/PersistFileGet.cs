using Unity.VisualScripting;
using BS;
using BS.Utilities.Async;

namespace BS.VisualScripting
{
    /// <summary>
    /// Reads this world's <c>__persisted_&lt;name&gt;.json</c>. The contents arrive on the
    /// <see cref="OnPersistFileLoaded"/> event unit; a file that does not exist yet reports empty
    /// contents rather than a failure.
    /// </summary>
    [UnitTitle("Persist File Get")]
    [UnitShortTitle("PersistFileGet")]
    [UnitCategory("BS\\World")]
    [TypeIcon(typeof(BSObjectId))]
    public class PersistFileGet : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput fileName;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var _fileName = flow.GetValue<string>(fileName);
                UnityMainThreadTaskScheduler.Default.Enqueue(TaskRunner.Track(
                    () => PersistFileUnitSupport.Run("get", _fileName, null, "OnPersistFileLoaded"),
                    $"{nameof(PersistFileGet)}.{nameof(Definition)}"));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            fileName = ValueInput("Name", "");
        }
    }
}
