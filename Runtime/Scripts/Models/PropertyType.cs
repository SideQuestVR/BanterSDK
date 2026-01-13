public enum PropertyType
{
    Bool,
    Int,
    Float,
    String,
    Vector2,
    Vector3,
    Vector4,
    Vector5,
    Quaternion,
    SoftJointLimit,
    JointLimits,
    JointDrive,
    NotSupported,
    // Reference types
    AssetReference = 100,
    ComponentReference = 101,
    GameObjectReference = 102,
    TransformReference = 103
}