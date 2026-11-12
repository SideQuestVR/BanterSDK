using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /*
    #### Banter Spawn
    A place for players to arrive. When the space loads, one of the scene's active spawns is picked at random
    and the local player is teleported there, facing the spawn's Y rotation. With a radius, they land at a
    random point within that many metres of it, so several players don't stack up. Add several spawns to
    spread arrivals across them.

    **Properties**
    - `radius` - Land within this many metres of the spawn (0 lands exactly on it).

    **Methods**
    ```js
    // Spawn - teleport the local player to this spawn (within its radius).
    spawn.Spawn();
    ```

    **Code Example**
    ```js
        const radius = 2;
        const gameObject = new BS.GameObject("MySpawn");
        const spawn = await gameObject.AddComponent(new BS.Spawn(radius));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSSpawn : BSComponentBase
    {
        [Tooltip("Players land at a random point within this many metres of the spawn. 0 lands exactly on it.")]
        [See(initial = "0")][SerializeField] internal float radius = 0;

        // Every spawn in the loaded space; one is picked once, the frame after they have all started.
        static readonly List<BSSpawn> pool = new List<BSSpawn>();
        static bool pickPending;
        static bool picked;
        static int requestFrame;
        static BSScene resetHookedScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pool.Clear();
            ClearPick();
            resetHookedScene = null;
        }

        static void ClearPick()
        {
            pickPending = false;
            picked = false;
            requestFrame = 0;
        }

        [Method]
        public void _Spawn()
        {
            Teleport(false);
        }

        internal override void StartStuff()
        {
            if (!pool.Contains(this))
            {
                pool.Add(this);
            }
            if (resetHookedScene != scene)
            {
                resetHookedScene = scene;
                scene.events.OnSceneReset.AddListener(ClearPick);
            }
            // Only spawns that are there while the space loads pick the arrival point; one added
            // afterwards (by a script, say) waits to be asked.
            if (!picked && !pickPending && !scene.loaded)
            {
                pickPending = true;
                requestFrame = Time.frameCount;
            }
            SetLoadedIfNot();
        }

        void LateUpdate()
        {
            // Any spawn can make the pick, so it doesn't matter which one registered first.
            if (!pickPending || Time.frameCount <= requestFrame)
            {
                return;
            }
            pickPending = false;
            picked = true;
            var candidates = pool.FindAll(spawn => spawn != null && spawn.isActiveAndEnabled);
            if (candidates.Count > 0)
            {
                candidates[UnityEngine.Random.Range(0, candidates.Count)].Teleport(true);
            }
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            pool.Remove(this);
            if (pool.Count == 0)
            {
                // The space was unloaded; the next one picks again.
                ClearPick();
            }
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {

        }

        void Teleport(bool isSpawn)
        {
            var point = SamplePoint(transform.position, radius, UnityEngine.Random.value, UnityEngine.Random.value * 360f);
            scene.events.OnTeleport.Invoke(point, new Vector3(0f, transform.eulerAngles.y, 0f), true, isSpawn);
        }

        /// <summary>
        /// A point on the horizontal disc of <paramref name="radius"/> around <paramref name="center"/>,
        /// uniform over its area for uniform <paramref name="u"/> in [0,1] and angle in degrees.
        /// </summary>
        public static Vector3 SamplePoint(Vector3 center, float radius, float u, float angleDegrees)
        {
            if (radius <= 0f)
            {
                return center;
            }
            var distance = Mathf.Sqrt(Mathf.Clamp01(u)) * radius;
            var angle = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            var position = transform.position;
            var forward = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.forward;
            Gizmos.color = new Color(0.3f, 1f, 0.5f);
            Gizmos.DrawLine(position, position + Vector3.up * 1.8f);
            Gizmos.DrawLine(position, position + forward * 0.6f);
            Gizmos.DrawLine(position + forward * 0.6f, position + Quaternion.Euler(0f, 150f, 0f) * forward * 0.2f + forward * 0.6f);
            Gizmos.DrawLine(position + forward * 0.6f, position + Quaternion.Euler(0f, -150f, 0f) * forward * 0.2f + forward * 0.6f);
            if (radius > 0f)
            {
                UnityEditor.Handles.color = Gizmos.color;
                UnityEditor.Handles.DrawWireDisc(position, Vector3.up, radius);
            }
        }
#endif
    }
}