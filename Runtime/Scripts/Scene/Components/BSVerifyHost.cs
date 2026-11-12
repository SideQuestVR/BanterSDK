using UnityEngine;

namespace BS
{
    /// <summary>
    /// Checks that the world is running from its own address (see <see cref="VerifyHostCheck"/>). A plain component on
    /// purpose, with nothing on the bridge: it runs on the values saved in the build. The runtime route to it is a prefab,
    /// or the Verify Host node.
    /// </summary>
    [AddComponentMenu("BS/Verify Host")]
    public class BSVerifyHost : MonoBehaviour
    {
        [Tooltip("How Url is read. Domain compares only the host (and any port). Regular Expression runs it against the page's entire URL.")]
        [SerializeField] VerifyHostMatch match = VerifyHostMatch.Domain;
        [Tooltip("Domain: the address this world belongs at, such as https://my-world.worldspace.host. Regular Expression: an expression for the entire URL, such as ^https://my-world\\.worldspace\\.host/")]
        [SerializeField] string url = "";
        [Tooltip("What happens to a player who finds this world at any other address.")]
        [SerializeField] VerifyHostOutcome outcome = VerifyHostOutcome.TeleportToPosition;
        [Tooltip("Teleport To Position: where the player goes, in world space.")]
        [SerializeField] Vector3 teleportPosition = Vector3.zero;
        [Tooltip("Send To World: the world the player is sent to (http or https).")]
        [SerializeField] string worldUrl = "";
        [Tooltip("Send To World: the error shown as the player leaves. Leave empty to show nothing.")]
        [SerializeField] string errorMessage = VerifyHostCheck.DefaultErrorMessage;

        void Start()
        {
            StartCoroutine(VerifyHostCheck.Run(url, match, outcome, teleportPosition, worldUrl, errorMessage));
        }
    }
}
