using Banter;
using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerEmulator : MonoBehaviour
{
    [SerializeField] float navigationSpeed = 0.2f;
    [SerializeField] float shiftMultiplier = 5f;
    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float panSensitivity = 0.01f;
    [SerializeField] float mouseWheelZoomSpeed = 1.0f;
    [SerializeField] bool leftHandActive = true;
    [SerializeField] KeyCode triggerKey = KeyCode.Alpha1;
    [SerializeField] KeyCode gripKey = KeyCode.Alpha2;
    [SerializeField] KeyCode primaryKey = KeyCode.Alpha3;
    [SerializeField] KeyCode secondaryKey = KeyCode.Alpha4;
    [SerializeField] Camera head;

    // private InputActionAsset inputActionAsset;
    // private Vector3 anchorPoint;
    // private Quaternion anchorRot;
    private bool isPanning;
    public bool isGrabbing;
    private Transform grabbleObject;
    private Transform grabbleObjectParent;
    private Vector3 screenPoint;
    private Vector3 offset;
    private int grabbableLayer = 1 << 20;
    private int clickableLayer = 1 << 5;
    private BanterScene scene;
    private bool ctrlPressed = false;
    public float SmoothTurnSpeed = 90f;
    public static float TurnSpeed = 0f;
    void Awake()
    {
        scene = BanterScene.Instance();
    }

    void Start()
    {
        FlyControls();
        VRInputControls();
    }

    public void FlyControls()
    {
        var leftHandJoystick = scene.LeftHandActions.FindAction("Primary2DAxis");
        var rightHandJoystick = scene.RightHandActions.FindAction("Primary2DAxis");

        leftHandJoystick.performed += ctx =>
        {
            var value = ctx.ReadValue<Vector2>();
            var direction = head.transform.forward * value.y + head.transform.right * value.x;
            // transform.position = Vector3.MoveTowards(transform.position, transform.position + direction, Time.deltaTime * 5);
            transform.Translate(direction * Time.deltaTime * 5, Space.World);
        };

        rightHandJoystick.performed += ctx =>
        {
            var value = ctx.ReadValue<Vector2>();
            TurnSpeed = SmoothTurnSpeed * value.x * Time.deltaTime;
            transform.RotateAround(head.transform.position, Vector3.up, TurnSpeed);
        };
    }

    public void VRInputControls()
    {
        scene.LeftHandActions.FindAction("TriggerPress").started += ctx => scene.link.OnButtonPressed(ButtonType.TRIGGER, HandSide.LEFT);
        scene.LeftHandActions.FindAction("TriggerPress").canceled += ctx => scene.link.OnButtonReleased(ButtonType.TRIGGER, HandSide.LEFT);
        scene.LeftHandActions.FindAction("GripPress").started += ctx => scene.link.OnButtonPressed(ButtonType.GRIP, HandSide.LEFT);
        scene.LeftHandActions.FindAction("GripPress").canceled += ctx => scene.link.OnButtonReleased(ButtonType.GRIP, HandSide.LEFT);
        scene.LeftHandActions.FindAction("PrimaryButton").started += ctx => scene.link.OnButtonPressed(ButtonType.PRIMARY, HandSide.LEFT);
        scene.LeftHandActions.FindAction("PrimaryButton").canceled += ctx => scene.link.OnButtonReleased(ButtonType.PRIMARY, HandSide.LEFT);
        scene.LeftHandActions.FindAction("SecondaryButton").started += ctx => scene.link.OnButtonPressed(ButtonType.SECONDARY, HandSide.LEFT);
        scene.LeftHandActions.FindAction("SecondaryButton").canceled += ctx => scene.link.OnButtonReleased(ButtonType.SECONDARY, HandSide.LEFT);
        scene.RightHandActions.FindAction("TriggerPress").started += ctx => scene.link.OnButtonPressed(ButtonType.TRIGGER, HandSide.RIGHT);
        scene.RightHandActions.FindAction("TriggerPress").canceled += ctx => scene.link.OnButtonReleased(ButtonType.TRIGGER, HandSide.RIGHT);
        scene.RightHandActions.FindAction("GripPress").started += ctx => scene.link.OnButtonPressed(ButtonType.GRIP, HandSide.RIGHT);
        scene.RightHandActions.FindAction("GripPress").canceled += ctx => scene.link.OnButtonReleased(ButtonType.GRIP, HandSide.RIGHT);
        scene.RightHandActions.FindAction("PrimaryButton").started += ctx => scene.link.OnButtonPressed(ButtonType.PRIMARY, HandSide.RIGHT);
        scene.RightHandActions.FindAction("PrimaryButton").canceled += ctx => scene.link.OnButtonReleased(ButtonType.PRIMARY, HandSide.RIGHT);
        scene.RightHandActions.FindAction("SecondaryButton").started += ctx => scene.link.OnButtonPressed(ButtonType.SECONDARY, HandSide.RIGHT);
        scene.RightHandActions.FindAction("SecondaryButton").canceled += ctx => scene.link.OnButtonReleased(ButtonType.SECONDARY, HandSide.RIGHT);
    }
    bool Raycast(out RaycastHit hit, bool isClickable = false)
    {
        return Physics.Raycast(head.ScreenPointToRay(Input.mousePosition), out hit, 1000f, isClickable ? clickableLayer : grabbableLayer);
    }
    void Update()
    {
        var side = leftHandActive ? HandSide.LEFT : HandSide.RIGHT;
        MousePanning();
        if (isPanning) { return; }

        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            ctrlPressed = true;
        }
        if (Input.GetKeyDown(triggerKey))
        {
            scene.link.OnButtonPressed(ButtonType.TRIGGER, side);
        }
        else if (Input.GetKeyDown(gripKey))
        {
            scene.link.OnButtonPressed(ButtonType.GRIP, side);
        }
        else if (Input.GetKeyDown(primaryKey))
        {
            scene.link.OnButtonPressed(ButtonType.PRIMARY, side);
        }
        else if (Input.GetKeyDown(secondaryKey))
        {
            scene.link.OnButtonPressed(ButtonType.SECONDARY, side);
        }
        if (Input.GetKeyUp(triggerKey))
        {
            scene.link.OnButtonReleased(ButtonType.TRIGGER, side);
        }
        else if (Input.GetKeyUp(gripKey))
        {
            scene.link.OnButtonReleased(ButtonType.GRIP, side);
        }
        else if (Input.GetKeyUp(primaryKey))
        {
            scene.link.OnButtonReleased(ButtonType.PRIMARY, side);
        }
        else if (Input.GetKeyUp(secondaryKey))
        {
            scene.link.OnButtonReleased(ButtonType.SECONDARY, side);
        }
        if (Input.GetMouseButtonDown(0))
        {
            if (!isGrabbing && Raycast(out RaycastHit hit, true))
            {
                scene.Click(hit.transform.gameObject, hit.point, hit.normal);
            }
        }
        if (Input.GetMouseButton(0) && ctrlPressed)
        {
            if (!isGrabbing && Raycast(out RaycastHit hit))
            {
                grabbleObject = hit.transform;
                grabbleObjectParent = grabbleObject.parent;
                grabbleObject.SetParent(transform);
                screenPoint = head.WorldToScreenPoint(hit.transform.position);
                offset = hit.transform.gameObject.transform.position - head.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z));
                isGrabbing = true;
                scene.Grab(grabbleObject.gameObject, hit.point, side);

            }
            else if (isGrabbing)
            {
                Vector3 curScreenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z);
                Vector3 curPosition = head.ScreenToWorldPoint(curScreenPoint) + offset;
                grabbleObject.position = curPosition;
            }
        }
        else if (isGrabbing && grabbleObject != null)
        {
            scene.Release(grabbleObject.gameObject, side);
            grabbleObject.SetParent(grabbleObjectParent);
            grabbleObject = null;
            isGrabbing = false;
            ctrlPressed = false;
        }

        if (Input.GetMouseButton(1))
        {
            Vector3 move = Vector3.zero;
            float speed = navigationSpeed * (Input.GetKey(KeyCode.LeftShift) ? shiftMultiplier : 1f) * Time.deltaTime * 9.1f;
            if (Input.GetKey(KeyCode.W))
                move += head.transform.forward * speed;
            if (Input.GetKey(KeyCode.S))
                move -= head.transform.forward * speed;
            if (Input.GetKey(KeyCode.D))
                move += head.transform.right * speed;
            if (Input.GetKey(KeyCode.A))
                move -= head.transform.right * speed;
            if (Input.GetKey(KeyCode.E))
                move += head.transform.up * speed;
            if (Input.GetKey(KeyCode.Q))
                move -= head.transform.up * speed;
            transform.Translate(move, Space.World);
        }
        if (Input.GetMouseButton(1))
        {
            head.transform.Rotate(new Vector3(-Input.GetAxis("Mouse Y") * sensitivity, 0, 0));
            transform.Rotate(new Vector3(0, Input.GetAxis("Mouse X") * sensitivity, 0));
        }

        MouseWheeling();

    }

    void MouseWheeling()
    {
        float speed = 10 * (mouseWheelZoomSpeed * (Input.GetKey(KeyCode.LeftShift) ? shiftMultiplier : 1f) * Time.deltaTime * 9.1f);

        Vector3 pos = transform.position;
        if (Input.GetAxis("Mouse ScrollWheel") < 0)
        {
            pos = pos - (transform.forward * speed);
            transform.position = pos;
        }
        if (Input.GetAxis("Mouse ScrollWheel") > 0)
        {
            pos = pos + (transform.forward * speed);
            transform.position = pos;
        }
    }


    private float pan_x;
    private float pan_y;
    private Vector3 panComplete;

    void MousePanning()
    {
        pan_x = -Input.GetAxis("Mouse X") * panSensitivity;
        pan_y = -Input.GetAxis("Mouse Y") * panSensitivity;
        panComplete = new Vector3(pan_x, pan_y, 0);

        if (Input.GetMouseButtonDown(2))
        {
            isPanning = true;
        }

        if (Input.GetMouseButtonUp(2))
        {
            isPanning = false;
        }

        if (isPanning)
        {
            transform.Translate(panComplete);
        }
    }

}
