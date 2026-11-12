namespace BS
{
    // The named parametric surfaces have no size parameters of their own - the surface function
    // fixes their extent, which ranges from roughly 4m to 15m across. FitToUnitCube is what gives
    // them a usable default size.
    public class Spring : ParametricGeometry
    {
        public Spring(int stacks = 32, int slices = 32) : base(stacks, slices, ParametricGeometry.Spring)
        {
            FitToUnitCube();
        }
    }
}
