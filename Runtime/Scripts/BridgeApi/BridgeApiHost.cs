using BS.Bridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.BridgeApi
{
    /// <summary>
    /// Plugs the typed API into the bridge. This assembly is only ever reached from here, so this method is also what keeps
    /// it in a stripped player: the linker treats an assembly with a RuntimeInitializeOnLoadMethod as a root, and the thunks
    /// reference every member they call directly.
    /// </summary>
    internal static class BridgeApiHost
    {
        static BridgeWorldScope scope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            scope = BridgeWorldScope.ForRunningWorld();
            BridgeCallHost.Current = Create(scope);
        }

        /// <summary>The host for a scope. Tests give their own.</summary>
        internal static BridgeCallHost Create(BridgeWorldScope scope)
        {
            var handles = new UnityHandles(scope, id => BSScene.Current?.GetGameObject(id), BridgeApiThunks.IsReadOnlyAsset, BridgeApiThunks.BuildTypes());
            return new BridgeCallHost(BridgeApiThunks.Build(), handles, () => Time.frameCount)
            {
                Log = Debug.LogWarning,
                CapsLimit = ClientCaps,
                MemberOff = ClientMemberOff,
            };
        }

        const uint AllCaps = BridgeCap.Read | BridgeCap.Write | BridgeCap.Global;

        /// <summary>A limit that can't be read counts as off.</summary>
        static uint ClientCaps()
        {
            var caps = BSTypedApi.Caps;
            if (caps == null) return AllCaps;
            try { return (uint)caps() & AllCaps; }
            catch (System.Exception) { return 0; }
        }

        static bool ClientMemberOff(int member)
        {
            var off = BSTypedApi.MemberOff;
            if (off == null) return false;
            try { return off(member); }
            catch (System.Exception) { return true; }
        }

        static void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) scope?.SceneLoaded(loaded);
        }
    }
}
