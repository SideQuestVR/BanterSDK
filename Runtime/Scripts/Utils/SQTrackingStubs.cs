// THIS IS A STUB FOR SQ.TRACKING OUTSIDE OF THE MAIN BANTER PROJECT

#if !BANTER_EDITOR

namespace SQ.Tracking
{
    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;
    }

    public struct Half
    {
        public ushort value;
        public static implicit operator float(Half value) { return (float)value; }
    }

    public struct HalfQuat
    {
        public Half X;
        public Half Y;
        public Half Z;
        public Half W;
    }
}
#endif
