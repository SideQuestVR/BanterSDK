using Unity.VisualScripting;

namespace BS.VisualScripting
{
    /// <summary>
    /// Sends the local player to another space by its URL: the graph twin of <c>scene.GoToUrl(url)</c>. Only http(s)
    /// URLs; the server picks the instance. In SDK Play Mode it only logs where it would go.
    /// </summary>
    [UnitTitle("Go To URL")]
    [UnitShortTitle("Go To URL")]
    [UnitCategory("BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
    public class GoToUrl : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput url;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                BSScene.Instance().GoToUrl(flow.GetValue<string>(url));
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            url = ValueInput("URL", string.Empty);

            Requirement(url, inputTrigger);
            Succession(inputTrigger, outputTrigger);
        }
    }
}
