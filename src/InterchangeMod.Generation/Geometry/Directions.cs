namespace InterchangeMod.Generation.Geometry
{
    /// <summary>方向角（度，从上往下看顺时针，0 = +Z）与向量之间的换算。</summary>
    public static class Directions
    {
        public const float DegToRad = MathF.PI / 180f;

        public static Vec3 FromAngle(float degrees) => Vec3.UnitZ.RotateY(degrees * DegToRad);

        /// <summary>水平面上行驶方向的右侧。</summary>
        public static Vec3 RightOf(Vec3 direction) => direction.RotateY(MathF.PI / 2f);

        /// <summary>两个方向角之间的夹角，范围 [0, 180]。</summary>
        public static float AngleBetween(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d < 0f)
                d += 360f;
            return d > 180f ? 360f - d : d;
        }

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
}
