using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    /// <summary>
    /// Gives the local player XP for something they did in this world, by event key. The same path as the
    /// <see cref="BSGrantXP"/> component and <c>scene.GrantXp</c> in a page: the world sends only the key,
    /// and the server rule an admin wrote for that key decides whether it pays, how much and how often.
    /// </summary>
    [UnitTitle("Grant XP")]
    [UnitShortTitle("Grant XP")]
    [UnitCategory("BS\\User")]
    [TypeIcon(typeof(BSObjectId))]
    public class GrantXP : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [PortLabel("Event Key")]
        public ValueInput eventKey;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) =>
            {
                var key = flow.GetValue<string>(eventKey);
                // Validated here as well as in the app so a mistyped key says so in the editor console
                // rather than going quiet.
                if (!BSGrantXP.IsValidKey(key))
                {
                    UnityEngine.Debug.LogWarning($"[Grant XP] '{key}' is not a valid event key " +
                                                 "(lowercase letters, digits, _ . - and 1-64 characters); nothing sent.");
                    return outputTrigger;
                }
                BSScene.Instance()?.events?.OnGrantXp?.Invoke(key);
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            eventKey = ValueInput("Event Key", "");
        }
    }
}
