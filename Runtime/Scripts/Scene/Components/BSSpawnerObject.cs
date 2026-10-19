using System;
using System.Collections.Generic;
using SideQuest.FlexaBody;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS
{
    /*
    #### Banter Spawner Object
    A dispenser. Grabbing it puts a copy of the whole object in your hand: same mesh, children and components, but free
    to move, thrown with physics and synced for everyone in the space, including players who join later. The spawner
    itself stays where it is (its Rigidbody is made kinematic) unless `forceKinematic` is off. Copies get their own
    network ids, never the spawner's. A copy is removed when the player who owns it leaves; past `maxSpawned`, spawning
    removes the oldest copy nobody is holding. Spawning needs a multiplayer connection.

    **Properties**
    - `maxSpawned` - How many copies of this spawner can exist at once.
    - `forceKinematic` - Make the spawner's Rigidbody kinematic so it stays put. Off: the Rigidbody is left as you set it
      (the spawner itself is never synced, so a spawner that can move does so separately on each player).

    **Code Example**
    ```js
        const maxSpawned = 10;
        const gameObject = new BS.GameObject("MySpawner");
        await gameObject.SetNetworkId("my-spawner"); // the same on every player, before the spawner is added
        await gameObject.SetLayer(20);              // Grabbable
        await gameObject.AddComponent(new BS.BoxCollider());
        const spawner = await gameObject.AddComponent(new BS.SpawnerObject(maxSpawned));
    ```

    */
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [RequireComponent(typeof(Rigidbody))]
    [WatchComponent]
    public class BSSpawnerObject : BSComponentBase
    {
        [Tooltip("How many copies of this spawner can exist at once. Spawning past it removes the oldest copy nobody is holding.")]
        [See(initial = "10")][SerializeField] internal int maxSpawned = 10;

        [Tooltip("Make the spawner's Rigidbody kinematic so it stays put. Off: the Rigidbody is left as you set it. The " +
                 "spawner itself is never synced, so one that can move does so separately on each player; copies are always " +
                 "dynamic and synced.")]
        [See(initial = "true")][SerializeField] internal bool forceKinematic = true;

        // Every started spawner, so a copy announced by the network can find its template by id.
        static readonly List<BSSpawnerObject> spawners = new List<BSSpawnerObject>();

        // SDK Play without multiplayer: this spawner's local copies, oldest first.
        readonly List<GameObject> localCopies = new List<GameObject>();

        /// <summary>A spawner has started (and so can be found with <see cref="FindById"/>).</summary>
        public static event Action<BSSpawnerObject> Registered;

        /// <summary>
        /// Set while a multiplayer layer makes the copies (the SDK's local multiplayer, while it runs): the SDK's desktop
        /// hand then leaves spawner grabs to it. Unset, that hand makes a local copy nobody else sees
        /// (<see cref="SpawnLocalCopy"/>). Greenfield's grab bridge goes to its own network service either way.
        /// </summary>
        public static bool NetworkMakesCopies { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            spawners.Clear();
            Registered = null;
            NetworkMakesCopies = false;
        }

        /// <summary>This spawner's id, which the copies' records carry: its <see cref="BSObjectId"/> as it is now.</summary>
        public string SpawnerId
        {
            get
            {
                var objectId = GetComponent<BSObjectId>();
                return objectId != null ? objectId.Id : null;
            }
        }

        /// <summary>The started spawner whose id is <paramref name="id"/> right now, or null.</summary>
        public static BSSpawnerObject FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (var spawner in spawners)
                if (spawner != null && spawner.SpawnerId == id)
                    return spawner;
            return null;
        }

        /// <summary>
        /// A copy of this whole object, made under <paramref name="inactiveHolder"/> so none of its components has
        /// woken up yet, at this spawner's pose. It has no BSSpawnerObject, a dynamic Rigidbody and a BSSyncedObject
        /// that takes ownership on grab (the spawner's own settings when it has one); a BSSyncedObject on a child is
        /// removed, since only the copy as a whole is synced. It carries <paramref name="copyId"/> and the matching
        /// child ids (<see cref="SpawnerCopyRules.AssignIds"/>), never the spawner's. The caller adds its networking,
        /// moves it out of the holder and activates it.
        /// </summary>
        public GameObject CreateCopy(Transform inactiveHolder, string copyId)
        {
            if (inactiveHolder == null || inactiveHolder.gameObject.activeInHierarchy)
                throw new ArgumentException("The copy has to be made under an inactive holder.", nameof(inactiveHolder));
            if (string.IsNullOrEmpty(copyId))
                throw new ArgumentException("A copy needs its own id.", nameof(copyId));

            var copy = Instantiate(gameObject, transform.position, transform.rotation, inactiveHolder);
            copy.name = gameObject.name;
            foreach (var spawner in copy.GetComponentsInChildren<BSSpawnerObject>(true))
            {
                if (spawner.gameObject != copy)
                    Debug.LogWarning($"[SpawnerObject] '{spawner.name}' under spawner '{name}' is a spawner too; its copies don't spawn.", this);
                DestroyImmediate(spawner);
            }

            var body = copy.GetComponent<Rigidbody>();
            if (body != null)
                body.isKinematic = false;

            foreach (var nested in copy.GetComponentsInChildren<BSSyncedObject>(true))
            {
                if (nested.gameObject == copy)
                    continue;
                Debug.LogWarning($"[SpawnerObject] '{nested.name}' under spawner '{name}' has a Synced Object; only the copy as a whole is synced, so it was removed from the copy.", this);
                DestroyImmediate(nested);
            }
            var synced = copy.GetComponent<BSSyncedObject>();
            if (synced == null)
                synced = copy.AddComponent<BSSyncedObject>();
            synced.takeOwnershipOnGrab = true;

            SpawnerCopyRules.AssignIds(copy, copyId);
            SpawnerCopyRules.ClearJsIds(copy);
            return copy;
        }

        /// <summary>
        /// SDK Play on its own (no multiplayer): <paramref name="hand"/> has just grabbed this spawner, so put a copy in
        /// it that only this player sees. Past <see cref="MaxSpawned"/>, the oldest copy the hand isn't holding goes.
        /// </summary>
        public void SpawnLocalCopy(GrabHand hand)
        {
            if (hand == null)
                return;
            var heldCollider = hand.HeldCollider;
            localCopies.RemoveAll(copy => copy == null);
            if (localCopies.Count >= Math.Max(1, maxSpawned))
            {
                var oldest = localCopies.Find(copy => heldCollider == null || !heldCollider.transform.IsChildOf(copy.transform));
                if (oldest != null)
                {
                    localCopies.Remove(oldest);
                    Destroy(oldest);
                }
            }

            var staging = new GameObject("Spawner copy (staging)");
            staging.SetActive(false);
            var copy = CreateCopy(staging.transform, "local-copy:" + Guid.NewGuid().ToString("N"));
            DestroyImmediate(copy.GetComponent<BSSyncedObject>()); // nothing to sync with
            copy.SetActive(false);
            copy.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(copy, gameObject.scene);
            Destroy(staging);
            copy.SetActive(true);
            SpawnerCopyRules.IgnoreSpawnerCollisions(copy, this);
            localCopies.Add(copy);

            var target = SpawnerCopyRules.Counterpart(transform, heldCollider, copy.transform)
                         ?? copy.GetComponentInChildren<Collider>();
            if (target == null || !hand.TransferGrab(target))
                hand.ReleaseGrab();
        }

        internal override void StartStuff()
        {
            KeepStill();
            if (!spawners.Contains(this))
                spawners.Add(this);
            SetLoadedIfNot();
            try { Registered?.Invoke(this); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal override void UpdateStuff()
        {

        }

        internal override void DestroyStuff()
        {
            spawners.Remove(this);
        }

        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            // Turning it on (a script, or the start of the space) pins the spawner; turning it off leaves the Rigidbody as
            // it is, for the author or the page to set.
            if (changedProperties.Contains(PropertyName.forceKinematic))
                KeepStill();
        }

        void KeepStill()
        {
            if (!forceKinematic)
                return;
            var body = GetComponent<Rigidbody>();
            if (body != null)
                body.isKinematic = true;
        }
        // BS COMPILED CODE
        public System.Int32 MaxSpawned { get { return maxSpawned; } set { maxSpawned = value; UpdateCallback(new List<PropertyName> { PropertyName.maxSpawned }); } }
        public System.Boolean ForceKinematic { get { return forceKinematic; } set { forceKinematic = value; UpdateCallback(new List<PropertyName> { PropertyName.forceKinematic }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.maxSpawned, PropertyName.forceKinematic, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "SpawnerObject" + PropertyName.maxSpawned + maxSpawned + PropertyName.forceKinematic + forceKinematic;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(BSScene.UnityId(gameObject), BSScene.UnityId(this), ComponentType.SpawnerObject);


            oid = BSScene.UnityId(gameObject);
            cid = BSScene.UnityId(this);

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {
            return null;
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSInt)
                {
                    var valmaxSpawned = (BSInt)values[i];
                    if (valmaxSpawned.n == PropertyName.maxSpawned)
                    {
                        maxSpawned = valmaxSpawned.x;
                        changedProperties.Add(PropertyName.maxSpawned);
                    }
                }
                if (values[i] is BSBool)
                {
                    var valforceKinematic = (BSBool)values[i];
                    if (valforceKinematic.n == PropertyName.forceKinematic)
                    {
                        forceKinematic = valforceKinematic.x;
                        changedProperties.Add(PropertyName.forceKinematic);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.maxSpawned,
                    type = PropertyType.Int,
                    value = maxSpawned,
                    componentType = ComponentType.SpawnerObject,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.forceKinematic,
                    type = PropertyType.Bool,
                    value = forceKinematic,
                    componentType = ComponentType.SpawnerObject,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BS COMPILED CODE
    }
}
