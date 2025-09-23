using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

// namespace Menu.NewMenu
// {
    public class WorldSpaceUIDocument : MonoBehaviour, IPointerMoveHandler, IPointerUpHandler, IPointerDownHandler, IPointerExitHandler, ISubmitHandler, ICancelHandler, IMoveHandler, IScrollHandler, ISelectHandler, IDeselectHandler, IDragHandler, IPointerClickHandler, IPointerEnterHandler
    {
        [SerializeField] public UIDocument _uiDocument;
        [SerializeField] public BoxCollider _collider;
        [SerializeField] private float _raycastPlaneForwardOffset;

        [SerializeField] private bool _onlyShowPointerOverElement;
    
        [FormerlySerializedAs("UseDragEventFix")]
        [Tooltip("Some input modules (like the XRUIInputModule from the XR Interaction toolkit package) doesn't send PointerMove events. If you are using such an input module, just set this to true so at least you can properly drag things around.")]
        [SerializeField] private bool _useDragEventFix = false;

        [SerializeField] InputActionAsset _inputActionAsset;
        
        private InputActionMap _leftHandInputActions;
        private InputActionMap _rightHandInputActions;
        
        public bool AllowRaycastThroughBlockers;
        public bool IsPointerOver = false;
        public Vector3 LastPointerPosition;
        public Vector3 LastPointerNormal;
        public Action OnClickAway;

        private PanelEventHandler _panelEventHandler;
        
        // private PanelSettings _panelSettings;
        private RenderTexture _renderTexture;
        private Material _material;
        private Vector3[] _linePoints = new Vector3[10];
        private List<VisualElement> _pickedVes = new();

    
        private void OnEnable ()
        {
            // _panelSettings = _uiDocument.panelSettings;
            Invoke(nameof(SetupClickAway), 0.25f);  
        }

        private void OnDisable()
        {
            IsPointerOver = false;
            
            if (_inputActionAsset != null)
            {
                _leftHandInputActions = _inputActionAsset.FindActionMap("LeftHand", throwIfNotFound: true);
                _rightHandInputActions = _inputActionAsset.FindActionMap("RightHand", throwIfNotFound: true);
                _leftHandInputActions.FindAction("TriggerPress").performed -= OnClickAction;
                _rightHandInputActions.FindAction("TriggerPress").performed -= OnClickAction;
            }
        }

        private void SetupClickAway()
        {
            if (_inputActionAsset != null)
            {
                _leftHandInputActions = _inputActionAsset.FindActionMap("LeftHand", throwIfNotFound: true);
                _rightHandInputActions = _inputActionAsset.FindActionMap("RightHand", throwIfNotFound: true);
                _leftHandInputActions.FindAction("TriggerPress").performed -= OnClickAction;
                _rightHandInputActions.FindAction("TriggerPress").performed -= OnClickAction;
                _leftHandInputActions.FindAction("TriggerPress").performed += OnClickAction;
                _rightHandInputActions.FindAction("TriggerPress").performed += OnClickAction;
            }
        }

        void Start()
        {
            RebuildPanel();
            //_pointerLineRenderer.enabled = false;
            _uiDocument.rootVisualElement.RegisterCallback<PointerMoveEvent>(OnUIRootPointerMove);
        }
        
        public void RebuildPanel ()
        {
            PanelEventHandler[] handlers = FindObjectsOfType<PanelEventHandler>();

            foreach (PanelEventHandler handler in handlers)
            {
                if (handler.panel == _uiDocument.rootVisualElement.panel)
                {
                    _panelEventHandler = handler;
                    PanelRaycaster panelRaycaster = _panelEventHandler.GetComponent<PanelRaycaster>();
                    if (panelRaycaster != null)
                        panelRaycaster.enabled = false;
                
                    break;
                }
            }
        }

        void OnDestroy ()
        {
        
        }

        private void OnClickAction(InputAction.CallbackContext ctx)
        {
            if (!IsPointerOver)
            {
                OnClickAway?.Invoke();
            }
        }
        
///////////////////////// REDIRECTION OF EVENTS TO THE PANEL
        protected readonly HashSet<(BaseEventData, int)> _eventsProcessedInThisFrame = new HashSet<(BaseEventData, int)>();

        void LateUpdate ()
        {
            _eventsProcessedInThisFrame.Clear();
        }

        public void OnPointerMove (PointerEventData eventData)
        {
            //_pointerLineRenderer.enabled = false;
            IsPointerOver = true;
            LastPointerPosition = eventData.pointerCurrentRaycast.worldPosition;
            LastPointerNormal = eventData.pointerCurrentRaycast.worldNormal;
            
            //Debug.Log("OnPointerMoveBefore" + eventData.position);
            TransformPointerEventForUIToolkit(eventData);
            //LogLine.Do($"Pointer move: {eventData.position}");
            //Debug.Log("OnPointerMoveAfter" + eventData.position);
            // var existingRaycaster = _panelEventHandler?.GetComponent<PanelRaycaster>();
            if (!_panelEventHandler)
            {
                //LogLine.Do($"[Pn] PEH became null");
                RebuildPanel();
            }
            else if (_panelEventHandler.enabled == false)
                LogLine.Do($"PEH {_panelEventHandler.gameObject.name} is disabled");
            // else if (existingRaycaster?.enabled == true)
            // {
            //     // LogLine.Do($"PEH {_panelEventHandler.gameObject.name} raycaster is enabled");
            //     // existingRaycaster.enabled = false;
            // }
            _panelEventHandler?.OnPointerMove(eventData);
            
                //if (!_onlyShowPointerOverElement)
                //_pointerLineRenderer.enabled = true;
        }

        private string lastpicked;
        private void OnUIRootPointerMove(PointerMoveEvent evt)
        {
            if (_onlyShowPointerOverElement)
            {
                bool isOver = false;
                //Vector2 pickPoint = new Vector2(eventData.position.x, _uiDocument.rootVisualElement.layout.height - eventData.position.y);
                _uiDocument.rootVisualElement.panel.PickAll(evt.localPosition, _pickedVes);
                for (int i = 0; i < _pickedVes.Count; i++)
                {
                    //LogLine.Do($"{i}: {_pickedVes[i].name}");
                    if (_pickedVes[i].name.StartsWith("Blocker"))
                        continue;

                    if (_pickedVes[i].pickingMode == PickingMode.Ignore)
                    {
                        //LogLine.Do("ignored: " + _pickedVes[i].name);
                        continue;
                    }

                    isOver = true;
                    if (lastpicked != _pickedVes[i].name)
                    {
                        //LogLine.Do($"{_pickedVes[i].parent?.name}/{_pickedVes[i].name} on {_uiDocument.gameObject.name}");
                        lastpicked = _pickedVes[i].name;
                    }

                    break;
                }
            
                //_pointerLineRenderer.enabled = isOver;
            }
        }

        public void OnPointerDown (PointerEventData eventData)
        {
            TransformPointerEventForUIToolkit(eventData);
            _panelEventHandler?.OnPointerDown(eventData);
        }

        public void OnPointerUp (PointerEventData eventData)
        {
            TransformPointerEventForUIToolkit(eventData);
            _panelEventHandler?.OnPointerUp(eventData);
        }
    
        public void OnPointerExit(PointerEventData eventData)
        {
            IsPointerOver = false;
            //_pointerLineRenderer.enabled = false;
            TransformPointerEventForUIToolkit(eventData);
            //LogLine.Do($"OnPointerExit {gameObject.name} {eventData.position}");
            _panelEventHandler?.OnPointerExit(eventData);
        }
        
        public void OnPointerEnter(PointerEventData eventData)
        {
            TransformPointerEventForUIToolkit(eventData);
            //LogLine.Do($"OnPointerExit {gameObject.name} {eventData.position}");
            _panelEventHandler?.OnPointerEnter(eventData);
        }
    
        public void OnPointerClick(PointerEventData eventData)
        {
            TransformPointerEventForUIToolkit(eventData);
            _panelEventHandler?.OnPointerClick(eventData);
        }

        public void OnSubmit (BaseEventData eventData)
        {
            //LogLine.Do("OnSubmit" + eventData);
            _panelEventHandler?.OnSubmit(eventData);
        }

        public void OnCancel (BaseEventData eventData)
        {
            //Debug.Log("OnCancel" + eventData);
            _panelEventHandler?.OnCancel(eventData);
        }

        public void OnMove (AxisEventData eventData)
        {
            //LogLine.Do("OnMove" + eventData);
            _panelEventHandler?.OnMove(eventData);
        }

        public void OnScroll (PointerEventData eventData)
        {
            TransformPointerEventForUIToolkit(eventData);
            //Debug.Log("OnScroll" + eventData.position);
            _panelEventHandler?.OnScroll(eventData);
        }

        public void OnSelect (BaseEventData eventData)
        {
            //LogLine.Do($"OnSelect {gameObject.name} {eventData.selectedObject}");
            _panelEventHandler?.OnSelect(eventData);
        }

        public void OnDeselect (BaseEventData eventData)
        {
            //Debug.Log("OnDeselect" + eventData);
            _panelEventHandler?.OnDeselect(eventData);
        }

        public void OnDrag (PointerEventData eventData)
        {
            //LogLine.Do($"OnDrag {gameObject.name} {eventData.currentInputModule.gameObject.name} {eventData.position} {eventData.delta} {eventData.button} {eventData.dragging}");
            //if (_useDragEventFix)
            OnPointerMove(eventData);
        }

        private Vector2 lastPointerPos;

        protected void TransformPointerEventForUIToolkit (PointerEventData eventData)
        {
            var eventKey = (eventData, eventData.pointerId);

            if (!_eventsProcessedInThisFrame.Contains(eventKey))
            {
                _eventsProcessedInThisFrame.Add(eventKey);
                Camera eventCamera = eventData.enterEventCamera ?? eventData.pressEventCamera;

                if (eventCamera != null)
                {
                    // get current event position and create the ray from the event camera
                    Vector3 position = eventData.position;
                    position.z = eventCamera.nearClipPlane;
                    position = eventCamera.ScreenToWorldPoint(position);
                    UnityEngine.Plane panelPlane = new UnityEngine.Plane(transform.forward, transform.position + (transform.forward*_raycastPlaneForwardOffset));
                    Ray ray = new Ray(eventCamera.transform.position, position - eventCamera.transform.position);

                    if (panelPlane.Raycast(ray, out float distance))
                    {
                        // get local pointer position within the panel
                        position = ray.origin + distance * ray.direction.normalized;
                        position = transform.InverseTransformPoint(position);
                        position.x /= _collider.size.x;
                        position.y /= _collider.size.y;
                        position.y = position.y;
                        // compute a fake pointer screen position so it results in the proper panel position when projected from the camera by the PanelEventHandler
                        position.x += 0.5f; position.y -= 0.5f;
                        position = Vector3.Scale(position, new Vector3(_uiDocument.rootVisualElement.layout.width, _uiDocument.rootVisualElement.layout.height, 1.0f)*_uiDocument.panelSettings.scale);
                        position.y += Screen.height;
                        
                        // print(new Vector2(position.x, Screen.height - position.y)); // print actual computed position in panel UIToolkit coords

                        // update the event data with the new calculated position
                        eventData.position = position;
                        RaycastResult raycastResult = eventData.pointerCurrentRaycast;
                        raycastResult.screenPosition = position;
                        eventData.pointerCurrentRaycast = raycastResult;
                        raycastResult = eventData.pointerPressRaycast;
                        raycastResult.screenPosition = position;
                        eventData.pointerPressRaycast = raycastResult;

                        if (lastPointerPos == Vector2.zero)
                            lastPointerPos = position;

                        eventData.delta = new Vector2(position.x, position.y) - lastPointerPos;
                        lastPointerPos = position;
                    }
                }
            }
        }
    }
// }