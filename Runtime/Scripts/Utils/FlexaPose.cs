using UnityEngine;
public class FlexaPose : MonoBehaviour
{
    // [HideInInspector]
    public Pose centerEye = new Pose(Vector3.zero, Quaternion.identity);
    [HideInInspector]
    public Transform headTransform;
    // [HideInInspector]
    public Pose leftFoot = new Pose(Vector3.zero, Quaternion.identity);
    [HideInInspector]
    public Transform leftFootTransform;
    // [HideInInspector]
    public Pose rightFoot = new Pose(Vector3.zero, Quaternion.identity);
    [HideInInspector]
    public Transform rightFootTransform;

}