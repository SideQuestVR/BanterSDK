[APICommands]
public class APICommands
{
    public const string KEYBOARD_FOCUS = "!kf!";
    public const string KEY_EVENT = "!ke!";
    public const string TOUCH_EVENT = "!te!";
    public const string CREATE_WINDOW = "!cw!";
    public const string RESIZE_WINDOW = "!rw!";
    public const string KILL_WINDOW = "!kw!";
    public const string SET_NETWORK_ID = "!sni!";
    public const string SET_NAME = "!sn!";
    public const string SET_TAG = "!sta!";
    public const string SET_TRANSFORM = "!st!";
    public const string MONO_BEHAVIOUR = "!mb!";
    public const string WATCH_TRANSFORM = "!wt!";
    public const string REQUEST_ID = "!i!";
    public const string RESPONSE_ID = "!o!";
    public const string UPDATE = "!u!";
    public const string DOM_READY = "!dr!";
    public const string LOAD_FAILED = "!lf!";
    public const string ONLOAD = "!ol!";
    public const string RELOAD = "!r!";
    public const string LOG = "!l!";
    public const string SCENE_READY = "!sr!";
    public const string SCENE_START = "!sst!";
    public const string SCENE_SETTINGS = "!ss!";
    public const string NOTHING_20S = "!no20s!";
    public const string NOTHING = "!no!";
    public const string OBJECT_ADDED = "!oa!";
    public const string COMPONENT_ADDED = "!ca!";
    public const string OBJECT_UPDATE_REQUEST = "!our!";
    public const string QUERY_COMPONENTS = "!qc!";
    public const string WATCH_PROPERTIES = "!wp!";
    public const string SET_PARENT = "!sp!";
    public const string COMPONENT_REMOVED = "!cr!";
    public const string SET_ACTIVE = "!sa!";
    public const string OBJECT_REMOVED = "!or!";
    public const string COMPONENT_UPDATED = "!cu!";
    public const string INSTANTIATE = "!inst!";
    public const string WAIT_FOR_END_OF_FRAME = "!weof!";
    public const string CALL_METHOD = "!cm!";
    public const string METHOD_RETURN = "!mr!";
    public const string RAYCAST = "!rc!";
    public const string SET_PUBLIC_SPACE_PROPS = "!spsp!";
    public const string SET_PROTECTED_SPACE_PROPS = "!sprsp!";
    public const string SET_USER_PROPS = "!sup!";
    public const string TELEPORT = "!t!";
    public const string ATTACH = "!at!";
    public const string DETACH = "!dt!";
    public const string LOAD_URL = "!lu!";
    public const string TOGGLE_DEV_TOOLS = "!tdt!";
    public const string HIDE_DEV_TOOLS = "!hdt!";
    public const string ENABLE_LEGACY = "!el!";
    public const string SET_LAYER = "!sl!";
    public const string OPEN_PAGE = "!op!";
    public const string START_TTS = "!stts!";
    public const string STOP_TTS = "!otts!";
    public const string AI_IMAGE = "!aiimg!";
    public const string AI_MODEL = "!aiglb!";
    public const string ADD_PLAYER_FORCE = "!adpf!";
    public const string BASE_64_TO_CDN = "!b64cdn!";
    public const string OBJECT_TEX_TO_BASE_64 = "!objtob64!";
    public const string SELECT_FILE = "!sltglb!";
    public const string GRAVITY = "!gv!";
    public const string TIME_SCALE = "!ts!";
    public const string PLAYER_SPEED = "!ps!";
    public const string DEEP_LINK = "!dl!";
    public const string ONE_SHOT = "!ons!";
    public const string SEND_MENU_BROWSER_MESSAGE = "!smbm!";
    public const string INJECT_JS = "!ijs!";
    public const string INJECT_JS_CALLBACK = "!ijc!";
    public const string YT_INFO = "!yti!";
    public const string LOCK_TELEPORT = "!lt!";
    public const string LOCK_SPIDERMAN = "!ls!";
    public const string TELEMETRY = "!tel!";



    #region Events
    public const string EVENT = "!e!";
    public const string POSE_UPDATE = "pu!";
    public const string LOADED = "l!";
    public const string UNITY_LOADED = "ulo!";
    public const string CLICKED = "c!";
    public const string GRABBED = "g!";
    public const string RELEASED = "r!";
    public const string BUTTON_PRESSED = "bp!";
    public const string BUTTON_RELEASED = "br!";
    public const string TRANSFORM_UPDATED = "tu!";
    public const string KEY = "k!";
    public const string ONE_SHOT_RECIEVED = "on!";
    public const string COLLISION_ENTER = "ce!";
    public const string COLLISION_EXIT = "cx!";
    public const string COLLISION_STAY = "cs!";
    public const string TRIGGER_ENTER = "te!";
    public const string TRIGGER_EXIT = "tx!";
    public const string TRIGGER_STAY = "ts!";
    public const string USER_JOINED = "uj!";
    public const string USER_LEFT = "ul!";
    public const string USER_STATE_CHANGED = "upc!";
    public const string SPACE_STATE_CHANGED = "spc!";
    public const string AFRAME_TRIGGER = "at!";
    public const string MENU_BROWSER_MESSAGE = "mbm!";
    public const string BROWSER_MESSAGE = "bm!";
    public const string SEND_TRANSCRIPTION = "st!";
    public const string AI_IMAGE_RECV = "aiimg!";
    public const string AI_MODEL_RECV = "aiglb!";
    public const string BASE_64_TO_CDN_RECV = "b64cdn!";
    public const string SELECT_FILE_RECV = "sltglb!";
    public const string BANTER_VERSION = "bv!";
    public const string SEND_USER = "su!";
    public const string FULL_SPACE_STATE = "fss!";
    public const string VOICE_STARTED = "vs!";
    public const string PLAY_AVATAR = "pa!";
    #endregion
    #region Legacy stuff
    public const string LEGACY = "!le";
    public const string LEGACY_SEND_TO_AFRAME = "sta!";
    public const string LEGACY_SET_CHILD_COLOR = "scc!";
    public const string LEGACY_LOCK_PLAYER = "lp!";
    public const string LEGACY_UNLOCK_PLAYER = "up!";
    public const string LEGACY_SIT_PLAYER = "sp!";
    public const string LEGACY_UNSIT_PLAYER = "usp!";
    public const string LEGACY_GORILLA_PLAYER = "gp!";
    public const string LEGACY_UNGORILLA_PLAYER = "ugp!";
    public const string LEGACY_ENABLE_CONTROLLER_EXTRAS = "ece!";
    public const string LEGACY_ENABLE_QUATERNION_POSE = "eqp!";
    public const string LEGACY_SET_VIDEO_URL = "svu!";
    public const string LEGACY_SET_REFRESH_RATE = "srr!";
    public const string LEGACY_SEND_AFRAME_EVENT = "sae!";
    public const string LEGACY_PLAY_AVATAR = "lpa!";
    public const string LEGACY_REQUEST_OWNERSHIP = "ro!";
    public const string LEGACY_RESET_NETWORK_OBJECT = "rno!";
    public const string LEGACY_DO_I_OWN = "dio!";
    public const string LEGACY_ATTACH_OBJECT = "ao!";
    #endregion


    public const string SET_CAN_MOVE = "!scm!";
    public const string SET_CAN_ROTATE = "!scr!";
    public const string SET_CAN_CROUCH = "!scc!";
    public const string SET_CAN_TELEPORT = "!sct!";
    public const string SET_CAN_GRAPPLE = "!scg!";
    public const string SET_CAN_JUMP = "!scj!";
    public const string SET_CAN_GRAB = "!scgr!";
    public const string SET_BLOCK_LEFT_THUMBSTICK = "!sblt!";
    public const string SET_BLOCK_RIGHT_THUMBSTICK = "!sbrt!";
    public const string SET_BLOCK_LEFT_PRIMARY = "!sblp!";
    public const string SET_BLOCK_RIGHT_PRIMARY = "!sbrp!";
    public const string SET_BLOCK_LEFT_SECONDARY = "!sbls!";
    public const string SET_BLOCK_RIGHT_SECONDARY = "!sbrs!";
    public const string SET_BLOCK_LEFT_THUMBSTICK_CLICK = "!sbltc!";
    public const string SET_BLOCK_RIGHT_THUMBSTICK_CLICK = "!sbrtc!";
    public const string SET_BLOCK_LEFT_TRIGGER = "!sbltr!";
    public const string SET_BLOCK_RIGHT_TRIGGER = "!sbrtr!";

    // Platform detection command
    public const string GET_PLATFORM = "!gp!";

    public const string GET_BOUNDS = "!gb!";

    // Haptic feedback command
    public const string SEND_HAPTIC_IMPULSE = "!shi!";

    // Controller input events
    public const string CONTROLLER_AXIS_UPDATE = "ca!";
    public const string TRIGGER_AXIS_UPDATE = "ta!";
}
