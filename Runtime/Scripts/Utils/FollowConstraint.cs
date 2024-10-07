using UnityEngine;

public class FollowConstraint : MonoBehaviour
{
    public Transform followTransform; // Assign your Ghost object's Transform in the Inspector
    private Rigidbody _rb;
    public float positionLerpSpeed = 0.0f; // Speed of movement
    public float rotationLerpSpeed = 0.0f; // Speed of rotation
    private bool _hasRigidBody;
    void OnEnable() {
        _rb = GetComponent<Rigidbody>();
        _hasRigidBody = _rb != null;
    }

    private void Update()  {
        if(!followTransform) {
            enabled = false;
            return;
        }

        if(_hasRigidBody)
            return;
        
        if(positionLerpSpeed == 0) {
            transform.position = followTransform.position;
        }else{
            transform.position = Vector3.Lerp(transform.position, followTransform.position, Time.deltaTime * positionLerpSpeed);
        }
        if(rotationLerpSpeed == 0) {
            transform.rotation = followTransform.rotation;
        }else{
            transform.rotation = Quaternion.Lerp(transform.rotation, followTransform.rotation, Time.deltaTime * rotationLerpSpeed);
        }
    }

    private void FixedUpdate()
    {
        if(!_hasRigidBody)
            return;
        

        Vector3 newPosition;
        if(positionLerpSpeed == 0) {
            newPosition = followTransform.position;
        }else{
            newPosition = Vector3.Lerp(_rb.position, followTransform.position, Time.deltaTime * positionLerpSpeed);
        }

        Quaternion newRotation;

        if(rotationLerpSpeed == 0) {
            newRotation = followTransform.rotation;
        }else{
            newRotation = Quaternion.Lerp(_rb.rotation, followTransform.rotation, Time.deltaTime * rotationLerpSpeed);
        }

        _rb.MovePosition(newPosition);
        _rb.MoveRotation(newRotation);
    }
}
