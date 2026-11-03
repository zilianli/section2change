using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests
{
    internal static class EndpointFixtures
    {
        /// <summary>
        /// Positions reproduce supplied 丁香花 asset endpoints for distance=252;
        /// traffic directions, widths, lanes and prefab names are synthetic.
        /// </summary>
        public static EndpointArm[] DualCross(float distance = 252, bool rightHandTraffic = true)
        {
            EndpointArm Dual(int i, Vec3 inPosition, Vec3 outPosition, Vec3 outward)
            {
                if (!rightHandTraffic) (inPosition, outPosition) = (outPosition, inPosition);
                return new EndpointArm(i, new[]
                {
                    new SelectedEndpoint(i * 10 + 1, 1, inPosition, outward, true, false, 12, 2, "Synthetic"),
                    new SelectedEndpoint(i * 10 + 2, 1, outPosition, outward, true, true, 12, 2, "Synthetic"),
                });
            }
            return new[]
            {
                Dual(0, new Vec3(-12,10,distance), new Vec3(12,10,distance), Vec3.UnitZ),
                Dual(1, new Vec3(distance,10,12), new Vec3(distance,10,-12), Vec3.UnitX),
                Dual(2, new Vec3(12,10,-distance), new Vec3(-12,10,-distance), -Vec3.UnitZ),
                Dual(3, new Vec3(-distance,10,-12), new Vec3(-distance,10,12), -Vec3.UnitX),
            };
        }
    }
}
