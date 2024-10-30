using System.Collections;
using System.Collections.Generic;
using Banter.SDK;
using Pixeye.Unity;
using UnityEngine;
using UnityEngine.Events;

public class BanterPlayerEvents : MonoBehaviour
{
    [Foldout("Click")]
    public UnityEvent<Vector3, Vector3> onClick;

    [Foldout("Grab", true)]
    public UnityEvent<Vector3, HandSide> onGrab;
    public UnityEvent<HandSide> onRelease;

    [Foldout("Trigger", true)]
    public float triggerThreshold = 0.5f;
    public UnityEvent<HandSide> onTrigger;

    // [Foldout("Keyboard Keys", true)]
    // public UnityEvent<KeyCode> onKeyPress;
    // public UnityEvent<KeyCode> onKeyDown;
    // public UnityEvent<KeyCode> onKeyUp;

    // [Foldout("Controller Buttons", true)]
    // public UnityEvent<ButtonType> onButtonPress;
    // public UnityEvent<ButtonType> onButtonRelease;
}
