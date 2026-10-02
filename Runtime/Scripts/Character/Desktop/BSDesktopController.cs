#if !GREENFIELD_PROJECT && !BANTER_FLEX

using System.Collections.Generic;
using SideQuest.FlexaBody;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using HardwareKeyboardInput = SideQuest.Ora.HardwareKeyboardInput;
using PanelRaycaster = UnityEngine.UIElements.PanelRaycaster;
using UIDocument = UnityEngine.UIElements.UIDocument;
using WorldDocumentRaycaster = UnityEngine.UIElements.WorldDocumentRaycaster;

namespace BS
{
    /// <summary>
    /// The SDK's play-mode player: a fly camera that handles like the Scene view, and a mouse pointer
    /// that grabs, clicks and drives UI.
    /// </summary>
    /// <remarks>
    /// <para>Hold the right mouse button to look around; WASD moves, Q/E go down/up, Shift is faster,
    /// and scrolling while flying changes the speed. With the button up, scrolling dollies the camera.</para>
    /// <para>The left button acts on whatever is nearest under the cursor: UI (uGUI canvases, UI
    /// Toolkit panels, browsers) goes to the EventSystem; a grabbable (layer 20) is grabbed by
    /// <see cref="BSDesktopMouseHand"/> with the client's grab code; a UI/Menu-layer collider gets a
    /// BSScene click on release.</para>
    /// <para>The root also carries the local <see cref="UserData"/>, so spaces see a local user.
    /// It stands at the feet; the camera is the head, at eye height.</para>
    /// <para>Seats (<see cref="BSSeat"/>, or any attached object that attaches the player to it) sit the
    /// player at the seat and carry them with it. Space stands them up like the client's jump, unless an
    /// object is held (Space is then its primary button); moving while flying stands them up like the
    /// client's move stick. Either only when the seat allows it.</para>
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("")]
    public class BSDesktopController : MonoBehaviour
    {
        const int k_GrabbableLayer = 20;
        const int k_UILayer = 5;
        const int k_MenuLayer = 22;
        const int k_ClickMask = (1 << k_UILayer) | (1 << k_MenuLayer);

        public static BSDesktopController Instance { get; private set; }

        [Header("Fly")]
        [SerializeField] float _eyeHeight = 1.6f;
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField] float _lookSensitivity = 0.15f;
        [SerializeField] float _flySpeed = 3f;
        [SerializeField] float _minFlySpeed = 0.1f;
        [SerializeField] float _maxFlySpeed = 60f;
        [SerializeField] float _boostMultiplier = 4f;
        [Tooltip("How far one scroll notch dollies the camera when not flying.")]
        [SerializeField] float _dollyStep = 0.5f;

        [Header("Pointer")]
        [Tooltip("How far the pointer reaches for grabs and clicks (the client's pointer uses 30 m).")]
        [SerializeField] float _reach = 30f;

        [Header("Held-object controls (right hand)")]
        [SerializeField] Key _triggerKey = Key.Digit3;
        [SerializeField] Key _primaryKey = Key.Space;
        [SerializeField] Key _secondaryKey = Key.Tab;
        [SerializeField] Key _thumbClickKey = Key.Digit4;

        [Header("Seat")]
        [Tooltip("Eye height above the seat while sitting.")]
        [SerializeField] float _seatedEyeHeight = 0.85f;
        [Tooltip("Stands up from a seat, like jump in the client.")]
        [SerializeField] Key _jumpKey = Key.Space;
        [Tooltip("How far in front of the seat the player stands up.")]
        [SerializeField] float _standOffset = 0.75f;

        enum PressOwner { None, UI, Grab, Click }

        BSScene _scene;
        Transform _head;
        Camera _camera;
        BSDesktopPointerCapture _capture;
        BSDesktopMouseHand _hand;
        HardwareKeyboardInput _keyboardInput;

        float _yaw;
        float _pitch;
        bool _flying;
        Vector2 _cursorBeforeFly;

        PressOwner _pressOwner;
        GameObject _clickTarget;

        bool _seated;
        Transform _seat;
        float _seatYaw;
        bool _seatUnseatOnMove;
        bool _seatUnseatOnJump;
        System.Action _onUnseat;

        readonly RaycastHit[] _hits = new RaycastHit[32];
        readonly List<RaycastResult> _uiResults = new List<RaycastResult>();
        PointerEventData _uiPointer;
        EventSystem _uiPointerSystem;

        // Multiplayer Play Mode clones report the mouse at NaN while it isn't over their Game view, and
        // Camera.ScreenPointToRay logs "Screen position out of view frustum" for every ray from such a point,
        // here and in the EventSystem's UI raycasters, every frame. The pointer is skipped, and the UI input
        // module paused, until the position is real again.
        bool _pointerUsable = true;
        BaseInputModule _pausedUIModule;

        /// <summary>Builds the desktop player: root (feet, local user) and head (camera).</summary>
        public static BSDesktopController Spawn(BSScene scene)
        {
            if (Instance != null)
                return Instance;

            var root = new GameObject("BSDesktopPlayer");
            // Built inactive so UserData.Start sees isLocal and Head already set when it
            // announces the user.
            root.SetActive(false);
            DontDestroyOnLoad(root);

            var controller = root.AddComponent<BSDesktopController>();
            controller._scene = scene;

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, controller._eyeHeight, 0f);
            head.tag = "MainCamera";
            head.layer = 23; // PhysicsPlayer

            var camera = head.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = 60f;
            head.AddComponent<AudioListener>();
            var capture = head.AddComponent<BSDesktopPointerCapture>();

            // A trigger body, so trigger zones in the space notice the player. Tagged like
            // Banter's torso: Portal and the Creator Conveniences teleporter only react to
            // __BA_LocalPlayer. A trigger, so flying never shoves props around.
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.layer = 23; // PhysicsPlayer
            try { body.tag = "__BA_LocalPlayer"; }
            catch (UnityException) { LogLine.Do("[Desktop] Tag __BA_LocalPlayer is missing (Creator SDK > Setup); portals won't see the player."); }
            // Topped out well under the eye: UI Toolkit's world picking treats any collider on the
            // camera's ray as a blocker, and only a near-vertical look down should ever meet this.
            var bodyCollider = body.AddComponent<CapsuleCollider>();
            bodyCollider.isTrigger = true;
            bodyCollider.radius = 0.15f;
            bodyCollider.height = controller._eyeHeight - 0.3f;
            bodyCollider.center = new Vector3(0f, bodyCollider.height * 0.5f, 0f);
            var bodyRigidbody = body.AddComponent<Rigidbody>();
            bodyRigidbody.isKinematic = true;
            bodyRigidbody.useGravity = false;

            var user = root.AddComponent<UserData>();
            user.isLocal = true;
            user.Head = head.transform;

            controller._head = head.transform;
            controller._camera = camera;
            controller._capture = capture;
            controller._hand = BSDesktopMouseHand.Create(head.transform, scene);

            root.SetActive(true);
            return controller;
        }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            EnsureEventSystem();
            DisableOtherMainCameras();
            SceneManager.sceneLoaded += OnSceneLoaded;
            _scene?.events.OnTeleport.AddListener(OnTeleport);
            _scene?.events.OnClippingPlaneChanged.AddListener(OnClippingPlaneChanged);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _scene?.events.OnTeleport.RemoveListener(OnTeleport);
            _scene?.events.OnClippingPlaneChanged.RemoveListener(OnClippingPlaneChanged);
            if (_flying)
                EndFly();
            ResumeUIModule();
            if (_hand != null)
                Destroy(_hand.gameObject);
        }

        void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            EnsureEventSystem();
            DisableOtherMainCameras();
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
                return;

            var pointerUsable = IsUsablePoint(mouse.position.ReadValue());
            SetPointerUsable(pointerUsable);

            UpdateFlyState(mouse);

            if (_flying)
            {
                Look(mouse.delta.ReadValue());
                Fly(keyboard);
                var scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                    _flySpeed = Mathf.Clamp(_flySpeed * (scroll > 0f ? 1.2f : 1f / 1.2f), _minFlySpeed, _maxFlySpeed);
            }
            else if (pointerUsable)
            {
                UpdatePointer(mouse, keyboard);
            }

            // Releases are handled even mid-flight, so a grab can be carried and thrown while flying.
            if (mouse.leftButton.wasReleasedThisFrame)
                EndPress(mouse);

            UpdateSeatInput(keyboard);
            UpdateHeldInputs(keyboard);
        }

        void LateUpdate()
        {
            if (_seated)
            {
                // The seat was destroyed: stand up where it was. Nothing is left to tell.
                if (_seat == null)
                    Unseat(false);
                else
                    FollowSeat();
            }

            // Cleared after the EventSystem has processed this frame, release frame included.
            if (_pressOwner == PressOwner.None || _pressOwner == PressOwner.UI)
                _capture.Capturing = false;
        }

        // ---------------------------------------------------------------- Fly

        void UpdateFlyState(Mouse mouse)
        {
            if (!_flying && mouse.rightButton.wasPressedThisFrame)
                BeginFly(mouse);
            else if (_flying && !mouse.rightButton.isPressed)
                EndFly();
        }

        void BeginFly(Mouse mouse)
        {
            _flying = true;
            _cursorBeforeFly = mouse.position.ReadValue();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            // Fly keys must not type into a focused browser page.
            SetKeyboardInputEnabled(false);
        }

        void EndFly()
        {
            _flying = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (IsUsablePoint(_cursorBeforeFly))
                Mouse.current?.WarpCursorPosition(_cursorBeforeFly);
            SetKeyboardInputEnabled(true);
        }

        void SetKeyboardInputEnabled(bool enabled)
        {
            if (_keyboardInput == null)
                _keyboardInput = FindAnyObjectByType<HardwareKeyboardInput>();
            if (_keyboardInput != null)
                _keyboardInput.enabled = enabled;
        }

        void Look(Vector2 delta)
        {
            // The client's desktop mouselook is gated the same way.
            if (!ActionsSystem.canRotate)
                return;
            _yaw += delta.x * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -89f, 89f);
            ApplyLook();
        }

        void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Fly(Keyboard keyboard)
        {
            if (!ActionsSystem.canMove || ActionsSystem.Blocker_LeftThumbstick.All)
                return;

            var direction = Vector3.zero;
            if (keyboard.wKey.isPressed) direction += Vector3.forward;
            if (keyboard.sKey.isPressed) direction += Vector3.back;
            if (keyboard.aKey.isPressed) direction += Vector3.left;
            if (keyboard.dKey.isPressed) direction += Vector3.right;
            if (keyboard.eKey.isPressed) direction += Vector3.up;
            if (keyboard.qKey.isPressed) direction += Vector3.down;
            if (direction == Vector3.zero)
                return;

            if (_seated)
            {
                // Like the client's move stick: stands up if the seat allows it, otherwise does nothing.
                if (_seatUnseatOnMove)
                    Unseat(true);
                return;
            }

            var speed = _flySpeed * (keyboard.shiftKey.isPressed ? _boostMultiplier : 1f);
            transform.position += _head.rotation * direction.normalized * (speed * Time.unscaledDeltaTime);
        }

        // ---------------------------------------------------------------- Pointer

        static bool IsUsablePoint(Vector2 point) =>
            !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.x) && !float.IsInfinity(point.y);

        void SetPointerUsable(bool usable)
        {
            if (usable == _pointerUsable)
                return;
            _pointerUsable = usable;
            if (usable)
            {
                ResumeUIModule();
                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return;
            var module = eventSystem.currentInputModule != null ? eventSystem.currentInputModule : eventSystem.GetComponent<BaseInputModule>();
            if (module != null && module.enabled)
            {
                module.enabled = false;
                _pausedUIModule = module;
            }
        }

        void ResumeUIModule()
        {
            if (_pausedUIModule != null)
                _pausedUIModule.enabled = true;
            _pausedUIModule = null;
        }

        void UpdatePointer(Mouse mouse, Keyboard keyboard)
        {
            var screenPoint = mouse.position.ReadValue();
            var ray = _camera.ScreenPointToRay(screenPoint);

            if (_hand.IsActive)
                _hand.UpdateAim(ray);

            if (mouse.leftButton.wasPressedThisFrame)
                BeginPress(screenPoint, ray);

            var scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                if (_hand.IsActive)
                    _hand.PushPull(scroll);
                else if (!_seated && !TryGetUIHit(screenPoint, ray, out _, out _))
                    transform.position += _head.forward * (Mathf.Sign(scroll) * _dollyStep * (keyboard.shiftKey.isPressed ? _boostMultiplier : 1f));
                // Over UI: the EventSystem scrolls it.
            }
        }

        void BeginPress(Vector2 screenPoint, Ray ray)
        {
            _pressOwner = PressOwner.None;
            _clickTarget = null;

            var uiDistance = TryGetUIHit(screenPoint, ray, out var d, out _) ? d : float.PositiveInfinity;
            var hasGrab = TryGetGrabTarget(ray, out var grabHit);
            var hasClick = TryGetClickTarget(ray, out var clickHit);
            var grabDistance = hasGrab ? grabHit.distance : float.PositiveInfinity;
            var clickDistance = hasClick ? clickHit.distance : float.PositiveInfinity;

            // Nearest wins; UI wins ties.
            if (float.IsPositiveInfinity(uiDistance) && !hasGrab && !hasClick)
                return;

            if (uiDistance <= grabDistance && uiDistance <= clickDistance)
            {
                _pressOwner = PressOwner.UI;
                return;
            }

            if (grabDistance <= clickDistance)
            {
                if (!ActionsSystem.canGrab || ActionsSystem.Blocker_RightGrip.Grab)
                    return;
                _pressOwner = PressOwner.Grab;
                _capture.Capturing = true;
                _hand.BeginGrab(grabHit, ray);
                return;
            }

            _pressOwner = PressOwner.Click;
            _capture.Capturing = true;
            _clickTarget = clickHit.collider.gameObject;
        }

        void EndPress(Mouse mouse)
        {
            switch (_pressOwner)
            {
                case PressOwner.Grab:
                    _hand.EndGrab();
                    break;

                case PressOwner.Click:
                    // A click is a press and release on the same object, with no UI in front of it.
                    var screenPoint = mouse.position.ReadValue();
                    if (!IsUsablePoint(screenPoint))
                        break;
                    var ray = _camera.ScreenPointToRay(screenPoint);
                    if (_clickTarget != null && TryGetClickTarget(ray, out var hit) && hit.collider.gameObject == _clickTarget)
                    {
                        var uiDistance = TryGetUIHit(screenPoint, ray, out var d, out _) ? d : float.PositiveInfinity;
                        if (hit.distance < uiDistance)
                            _scene?.Click(_clickTarget, hit.point, hit.normal);
                    }
                    break;
            }

            _pressOwner = PressOwner.None;
            _clickTarget = null;
        }

        void UpdateHeldInputs(Keyboard keyboard)
        {
            var trigger = keyboard[_triggerKey];
            var primary = keyboard[_primaryKey];
            var secondary = keyboard[_secondaryKey];
            var thumbClick = keyboard[_thumbClickKey];

            var stick = Vector2.zero;
            if (keyboard.upArrowKey.isPressed) stick.y += 1f;
            if (keyboard.downArrowKey.isPressed) stick.y -= 1f;
            if (keyboard.rightArrowKey.isPressed) stick.x += 1f;
            if (keyboard.leftArrowKey.isPressed) stick.x -= 1f;

            _hand.SetInputs(
                trigger.isPressed, trigger.wasPressedThisFrame,
                primary.isPressed, primary.wasPressedThisFrame,
                secondary.isPressed, secondary.wasPressedThisFrame,
                thumbClick.isPressed, thumbClick.wasPressedThisFrame,
                stick);
        }

        /// <summary>
        /// The nearest UI under the cursor: uGUI canvases, UI Toolkit panels and browsers, through the
        /// EventSystem's own raycasters.
        /// </summary>
        bool TryGetUIHit(Vector2 screenPoint, Ray ray, out float distance, out GameObject target)
        {
            distance = float.PositiveInfinity;
            target = null;

            var eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentInputModule == null)
                return false;

            if (_uiPointer == null || _uiPointerSystem != eventSystem)
            {
                _uiPointer = new PointerEventData(eventSystem);
                _uiPointerSystem = eventSystem;
            }
            _uiPointer.Reset();
            _uiPointer.pointerId = PointerInputModule.kMouseLeftId;
            _uiPointer.position = screenPoint;

            _uiResults.Clear();
            eventSystem.RaycastAll(_uiPointer, _uiResults);

            foreach (var result in _uiResults)
            {
                if (result.module == null || result.module == _capture || result.gameObject == null)
                    continue;

                float resultDistance;
                if (result.module is WorldDocumentRaycaster)
                {
                    // A hit with no document is a scene collider blocking the UI, not UI.
                    if (result.document == null)
                        continue;
                    resultDistance = result.distance;
                }
                else if (result.module is PanelRaycaster)
                {
                    // Flat panels report no depth. A mesh-input panel's depth is its mesh hit;
                    // anything else is a screen overlay, in front of everything.
                    resultDistance = PanelMeshInputBridge.TryResolveHit(result.gameObject, ray, out var meshHit)
                        ? meshHit.distance
                        : 0f;
                }
                else
                {
                    resultDistance = result.distance;
                }

                if (resultDistance < distance)
                {
                    distance = resultDistance;
                    target = result.gameObject;
                }
            }

            return target != null;
        }

        /// <summary>
        /// The nearest collider under the cursor, if it is grabbable. Anything nearer that isn't
        /// grabbable blocks the grab.
        /// </summary>
        bool TryGetGrabTarget(Ray ray, out RaycastHit hit)
        {
            hit = default;
            var count = Physics.RaycastNonAlloc(ray, _hits, _reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            System.Array.Sort(_hits, 0, count, RaycastHitDistanceComparer.Instance);

            for (var i = 0; i < count; i++)
            {
                var candidate = _hits[i];
                var col = candidate.collider;
                if (IsOwnCollider(col) || IsUIDocumentCollider(col))
                    continue;
                // Trigger volumes don't block; grabbable and clickable triggers do.
                if (col.isTrigger && col.gameObject.layer != k_GrabbableLayer && ((1 << col.gameObject.layer) & k_ClickMask) == 0)
                    continue;

                if (col.gameObject.layer != k_GrabbableLayer)
                    return false;

                hit = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The nearest UI/Menu-layer collider under the cursor, as the client's pointer finds scene
        /// click targets: those layers only, so other geometry doesn't block the click.
        /// </summary>
        bool TryGetClickTarget(Ray ray, out RaycastHit hit)
        {
            hit = default;
            var count = Physics.RaycastNonAlloc(ray, _hits, _reach, k_ClickMask);
            System.Array.Sort(_hits, 0, count, RaycastHitDistanceComparer.Instance);

            for (var i = 0; i < count; i++)
            {
                var col = _hits[i].collider;
                if (IsOwnCollider(col) || IsUIDocumentCollider(col))
                    continue;
                hit = _hits[i];
                return true;
            }
            return false;
        }

        bool IsOwnCollider(Collider col) =>
            col.transform.IsChildOf(transform) || (_hand != null && col.transform.IsChildOf(_hand.transform));

        // World-space UI Toolkit documents carry their own picking colliders; the UI raycast owns those.
        static bool IsUIDocumentCollider(Collider col) => col.GetComponentInParent<UIDocument>() != null;

        sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        // ---------------------------------------------------------------- Scene plumbing

        void OnTeleport(Vector3 position, Vector3 rotation, bool stopVelocity, bool isSpawn)
        {
            Unseat(true);

            var fromPosition = _head.position;
            var fromRotation = _head.rotation;

            transform.position = position;
            _yaw = rotation.y;
            ApplyLook();

            _hand.OnRigTeleported(fromPosition, fromRotation, _head.position, _head.rotation);
        }

        void OnClippingPlaneChanged(Vector2 planes)
        {
            _camera.nearClipPlane = Mathf.Max(0.001f, planes.x);
            _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 0.01f, planes.y);
        }

        // ---------------------------------------------------------------- Seat

        public bool IsSeated => _seated;

        public bool IsSeatedOn(Transform anchor) => _seated && anchor != null && _seat == anchor;

        /// <summary>The seat the player sits on, or null when standing.</summary>
        public Transform SeatAnchor => _seated ? _seat : null;

        /// <summary>The mouse hand: the desktop player's right hand, which grabs.</summary>
        public BSDesktopMouseHand MouseHand => _hand;

        /// <summary>
        /// Sits the player on <paramref name="anchor"/> (feet at its position, facing its forward) and
        /// keeps them there until they stand up. <paramref name="onUnseat"/> runs when they stand up by
        /// themselves (jump, move, a teleport), not when <see cref="Unseat"/> is asked not to notify.
        /// </summary>
        public void Seat(Transform anchor, bool unseatOnMove, bool unseatOnJump, System.Action onUnseat)
        {
            if (anchor == null)
                return;
            if (_seated && _seat != anchor)
                Unseat(true);

            _seatUnseatOnMove = unseatOnMove;
            _seatUnseatOnJump = unseatOnJump;
            _onUnseat = onUnseat;
            if (_seated)
                return;

            var fromPosition = _head.position;
            var fromRotation = _head.rotation;

            _seated = true;
            _seat = anchor;
            _seatYaw = anchor.eulerAngles.y;
            _yaw = _seatYaw;
            _pitch = 0f;
            _head.localPosition = new Vector3(0f, _seatedEyeHeight, 0f);
            FollowSeat();

            _hand.OnRigTeleported(fromPosition, fromRotation, _head.position, _head.rotation);
        }

        /// <summary>Stands the player up in front of the seat.</summary>
        public void Unseat(bool notify)
        {
            if (!_seated)
                return;

            // State first: the callback detaches the seat, which asks to unseat again.
            var anchor = _seat;
            var callback = _onUnseat;
            _seated = false;
            _seat = null;
            _onUnseat = null;

            var fromPosition = _head.position;
            var fromRotation = _head.rotation;
            _head.localPosition = new Vector3(0f, _eyeHeight, 0f);
            if (anchor != null)
                transform.position = StandUpPoint(anchor);
            ApplyLook();
            _hand.OnRigTeleported(fromPosition, fromRotation, _head.position, _head.rotation);

            if (notify)
                callback?.Invoke();
        }

        void UpdateSeatInput(Keyboard keyboard)
        {
            // While an object is held, Space is its primary button.
            if (_seated && _seatUnseatOnJump && keyboard[_jumpKey].wasPressedThisFrame && !_hand.IsHolding)
                Unseat(true);
        }

        // Carries the player with a moving or turning seat.
        void FollowSeat()
        {
            var seatYaw = _seat.eulerAngles.y;
            _yaw += Mathf.DeltaAngle(_seatYaw, seatYaw);
            _seatYaw = seatYaw;
            transform.position = _seat.position;
            ApplyLook();
        }

        // On the floor in front of the seat, ignoring the seat itself.
        Vector3 StandUpPoint(Transform anchor)
        {
            var forward = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f) * Vector3.forward;
            var point = anchor.position + forward * _standOffset;
            var seatBody = anchor.GetComponentInParent<Rigidbody>();

            var count = Physics.RaycastNonAlloc(point + Vector3.up, Vector3.down, _hits, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(_hits, 0, count, RaycastHitDistanceComparer.Instance);
            for (var i = 0; i < count; i++)
            {
                var col = _hits[i].collider;
                if (IsOwnCollider(col) || (seatBody != null && col.attachedRigidbody == seatBody))
                    continue;
                return _hits[i].point;
            }
            return point;
        }

        /// <summary>
        /// UI needs an EventSystem. Use the scene's if it has one; otherwise make one that doesn't
        /// steer uGUI selection with WASD or the arrow keys, and ignores right/middle clicks (right
        /// is for flying, and browser pages treat any button as a left click).
        /// </summary>
        void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem");
            DontDestroyOnLoad(go);
            var eventSystem = go.AddComponent<EventSystem>();
            eventSystem.sendNavigationEvents = false;
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.rightClick = null;
            module.middleClick = null;
        }

        /// <summary>One Camera.main and one AudioListener: this player's.</summary>
        void DisableOtherMainCameras()
        {
            foreach (var other in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (other == _camera || !other.enabled || !other.CompareTag("MainCamera"))
                    continue;
                other.enabled = false;
                LogLine.Do($"[Desktop] Disabled scene camera '{other.name}': the desktop controller's camera is the main camera in play mode.");
            }

            var ownListener = _head.GetComponent<AudioListener>();
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener != ownListener && listener.enabled)
                    listener.enabled = false;
            }
        }
    }
}

#endif
