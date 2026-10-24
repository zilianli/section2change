using System;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>
    /// 三次贝塞尔曲线，与游戏中 Colossal.Mathematics.Bezier4x3 一一对应。
    /// 生成层用自己的 Vec3 以保持与游戏无关，落地层负责转换。
    /// </summary>
    public readonly struct Bezier3
    {
        public readonly Vec3 P0, P1, P2, P3;

        public Bezier3(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3)
        {
            P0 = p0; P1 = p1; P2 = p2; P3 = p3;
        }

        /// <summary>直线段（控制点取三等分点）。</summary>
        public static Bezier3 Line(Vec3 a, Vec3 b) =>
            new Bezier3(a, Vec3.Lerp(a, b, 1f / 3f), Vec3.Lerp(a, b, 2f / 3f), b);

        /// <summary>
        /// 俯视为直线、竖向两端水平的升降段：起点和终点处坡度为 0，中间最陡（约为平均坡度的 1.5 倍），
        /// 和前后的水平路段衔接时没有竖向折角。
        /// </summary>
        public static Bezier3 SmoothSlope(Vec3 a, Vec3 b)
        {
            var p1 = Vec3.Lerp(a, b, 1f / 3f);
            var p2 = Vec3.Lerp(a, b, 2f / 3f);
            p1.Y = a.Y;
            p2.Y = b.Y;
            return new Bezier3(a, p1, p2, b);
        }

        /// <summary>
        /// 由起止点和起止切线方向构造曲线。
        /// 高度按 t 线性插值，使坡度尽量均匀。
        /// </summary>
        public static Bezier3 FromTangents(Vec3 start, Vec3 startDir, Vec3 end, Vec3 endDir, float handleLength)
        {
            var d0 = Flatten(startDir) * handleLength;
            var d1 = Flatten(endDir) * handleLength;
            var p1 = start + d0;
            var p2 = end - d1;
            p1.Y = start.Y + (end.Y - start.Y) / 3f;
            p2.Y = start.Y + (end.Y - start.Y) * 2f / 3f;
            return new Bezier3(start, p1, p2, end);
        }

        public Vec3 Evaluate(float t)
        {
            float u = 1f - t;
            return u * u * u * P0 + 3f * u * u * t * P1 + 3f * u * t * t * P2 + t * t * t * P3;
        }

        /// <summary>一阶导数（未归一化的切线）。</summary>
        public Vec3 Derivative(float t)
        {
            float u = 1f - t;
            return 3f * u * u * (P1 - P0) + 6f * u * t * (P2 - P1) + 3f * t * t * (P3 - P2);
        }

        /// <summary>采样近似弧长。</summary>
        public float Length(int samples = 32)
        {
            float len = 0f;
            var prev = P0;
            for (int i = 1; i <= samples; i++)
            {
                var cur = Evaluate(i / (float)samples);
                len += Vec3.Distance(prev, cur);
                prev = cur;
            }
            return len;
        }

        /// <summary>采样得到的最大纵坡（|dy| / 水平距离）。</summary>
        public float MaxGrade(int samples = 32)
        {
            float max = 0f;
            for (int i = 0; i <= samples; i++)
            {
                var d = Derivative(i / (float)samples);
                float horizontal = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
                if (horizontal < 1e-4f)
                    return float.PositiveInfinity;
                max = MathF.Max(max, MathF.Abs(d.Y) / horizontal);
            }
            return max;
        }

        /// <summary>先绕 Y 轴旋转 yaw 弧度，再平移到 position。</summary>
        public Bezier3 Place(Vec3 position, float yaw) => new Bezier3(
            P0.RotateY(yaw) + position, P1.RotateY(yaw) + position,
            P2.RotateY(yaw) + position, P3.RotateY(yaw) + position);

        public Bezier3 Reverse() => new Bezier3(P3, P2, P1, P0);

        public override string ToString() => $"{P0} -> {P3}";

        private static Vec3 Flatten(Vec3 v)
        {
            v.Y = 0f;
            return Vec3.Normalize(v);
        }
    }
}
