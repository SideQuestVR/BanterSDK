using System.Collections;
using System.Collections.Generic;
using Banter.SDK;
using Pixeye.Unity;
using UnityEngine;
using UnityEngine.Events;

public class BanterPlayerEvents : MonoBehaviour
{
    [Foldout("Click", true)]
    public UnityEvent<Vector3, Vector3> onClick;


    [Foldout("Grabbing", true)]
    public UnityEvent<HandSide> onGrab;
    public UnityEvent<HandSide> onHeld;
    public UnityEvent<HandSide> onRelease;

    [Foldout("Trigger", true)]
    public UnityEvent onGunTrigger;
    public UnityEvent<float, HandSide> onPrimaryTrigger;
    public UnityEvent<float, HandSide> onSecondaryTrigger;

    [Foldout("Thumbstick", true)]
    public UnityEvent<Vector2, HandSide> onPrimaryThumbstick;
    public UnityEvent<HandSide> onPrimaryThumbClickDown;
    public UnityEvent<HandSide> onPrimaryThumbClickUp;

    public UnityEvent<Vector2, HandSide> onSecondaryThumbstick;
    public UnityEvent<HandSide> onSecondaryThumbClickDown;
    public UnityEvent<HandSide> onSecondaryThumbClickUp;

    [Foldout("Buttons", true)]
    public UnityEvent<HandSide> onADown;
    public UnityEvent<HandSide> onAUp;
    public UnityEvent<HandSide> onBDown;
    public UnityEvent<HandSide> onBUp;
    public UnityEvent<HandSide> onXDown;
    public UnityEvent<HandSide> onXUp;
    public UnityEvent<HandSide> onYDown;
    public UnityEvent<HandSide> onYUp;
}
