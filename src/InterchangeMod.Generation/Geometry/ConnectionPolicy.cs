using System;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>臂端与旧路保留侧的方向。flow: 0 双向，1 向外，-1 向内。</summary>
    public static class ConnectionPolicy
    {
        public static bool CanConnect(bool matchedRoad, Vec3 outward, int flow, Vec3 oldOutward, int oldFlow)
        {
            if (!matchedRoad || flow != oldFlow) return false;
            float a = MathF.Sqrt(outward.X * outward.X + outward.Z * outward.Z);
            float b = MathF.Sqrt(oldOutward.X * oldOutward.X + oldOutward.Z * oldOutward.Z);
            // At most 30 degrees between the outward tangents, including curved roads.
            return a > 1e-5f && b > 1e-5f
                && (outward.X * oldOutward.X + outward.Z * oldOutward.Z) / (a * b) >= 0.8660254f;
        }
    }
}
