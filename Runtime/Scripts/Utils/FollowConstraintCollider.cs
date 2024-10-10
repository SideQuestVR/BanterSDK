using System;
using UnityEngine;

public class FollowConstraintCollider : MonoBehaviour
{
    public Transform followTransform; 
    public Vector3 offset; 
    [SerializeField] Collider _collider;

    public bool isHeightToo;
    public Transform heightTopTransform; 
    public Transform heightBottomTransform; 

    bool _hasFollowTransform;
    bool _hasFollowCollider; 
    bool _hasHeightTop; 
    bool _hasHeightBottom; 
    void OnEnable() {
        _hasFollowTransform = followTransform != null;
        _hasFollowCollider = _collider != null;
        _hasHeightTop = heightTopTransform != null;
        _hasHeightBottom = heightBottomTransform != null;
    }

    private void FixedUpdate()  {
        
        if(!_hasFollowTransform || !_hasFollowCollider || (isHeightToo && (!_hasHeightTop || !_hasHeightBottom))) {
            enabled = false;
            return;
        }

        if(_collider is SphereCollider) {
            ((SphereCollider)_collider).center = _collider.transform.InverseTransformPoint(followTransform.position) + offset;
        }else if(_collider is CapsuleCollider) {
            var col = (CapsuleCollider)_collider;
            if(isHeightToo){
                col.height = Vector3.Distance(heightTopTransform.position, heightBottomTransform.position);
                var center = _collider.transform.InverseTransformPoint(followTransform.position);
                center.y -= col.height / 2;
                col.center = center + offset;
            }else{
                col.center = _collider.transform.InverseTransformPoint(followTransform.position) + offset;
            }
        }
    }
}
