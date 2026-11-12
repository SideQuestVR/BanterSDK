namespace BS
{
    /// <summary>
    /// Serialized as an int on the JS-Unity wire, so this list is APPEND ONLY. Inserting or
    /// reordering a member silently repoints every published world that stored a later ordinal.
    /// </summary>
    public enum GeometryType
    {
        BoxGeometry,
        CircleGeometry,
        ConeGeometry,
        CylinderGeometry,
        PlaneGeometry,
        RingGeometry,
        SphereGeometry,
        TorusGeometry,
        TorusKnotGeometry,
        ParametricGeometry,

        // Appended for three.js parity. The block is reserved in one go so that the two
        // workstreams behind it - the polyhedra and the curve-based shapes - cannot each append
        // and end up with divergent ordinals.
        CapsuleGeometry,
        DodecahedronGeometry,
        IcosahedronGeometry,
        OctahedronGeometry,
        TetrahedronGeometry,
        LatheGeometry,
        TubeGeometry,
        ExtrudeGeometry,
        ShapeGeometry
    }
}
