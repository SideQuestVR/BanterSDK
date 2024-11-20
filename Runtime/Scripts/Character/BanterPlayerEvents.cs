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
    public UnityEvent<Vector3, HandSide> onGrab;
    public UnityEvent<HandSide> onRelease;

    [Foldout("Trigger", true)]
    public UnityEvent<HandSide> onTriggerPress;
    public UnityEvent<float, HandSide> onTrigger;

    [Foldout("Thumbstick", true)]
    public UnityEvent<Vector2, HandSide> onThumbstick;
    public UnityEvent<HandSide> onThumbstickClickDown;
    public UnityEvent<HandSide> onThumbstickClickUp;


    [Foldout("Primary/Secondary", true)]
    public UnityEvent<HandSide> onPrimaryDown;
    public UnityEvent<HandSide> onPrimaryup;
    public UnityEvent<HandSide> onSecondaryDown;
    public UnityEvent<HandSide> onSecondaryUp;
}
