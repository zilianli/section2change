using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>圆弧 → 贝塞尔曲线的近似，用于苜蓿叶环形匝道等。</summary>
    public static class Arc
    {
        /// <summary>每段贝塞尔覆盖的最大角度，超过则拆分以保证精度。</summary>
        private const float MaxSweepPerSegment = MathF.PI / 2f;

        /// <summary>
        /// 生成水平面上的圆弧（Y 从 startY 线性变化到 endY）。
        /// 角度以 +X 轴为 0，逆时针（从上往下看，X→Z）为正，单位弧度。
        /// </summary>
        public static List<Bezier3> Create(Vec3 center, float radius, float startAngle, float sweep, float startY, float endY)
        {
            int count = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / MaxSweepPerSegment - 1e-4f));
            float step = sweep / count;
            // 标准圆弧近似系数 k = 4/3 * tan(θ/4)
            float k = 4f / 3f * MathF.Tan(step / 4f) * radius;

            var result = new List<Bezier3>(count);
            for (int i = 0; i < count; i++)
            {
                float a0 = startAngle + step * i;
                float a1 = a0 + step;
                float y0 = startY + (endY - startY) * i / count;
                float y1 = startY + (endY - startY) * (i + 1) / count;

                var p0 = Point(center, radius, a0, y0);
                var p3 = Point(center, radius, a1, y1);
                var p1 = p0 + Tangent(a0) * k;
                var p2 = p3 - Tangent(a1) * k;
                p1.Y = y0 + (y1 - y0) / 3f;
                p2.Y = y0 + (y1 - y0) * 2f / 3f;
                result.Add(new Bezier3(p0, p1, p2, p3));
            }
            return result;
        }

        private static Vec3 Point(Vec3 c, float r, float a, float y) =>
            new Vec3(c.X + r * MathF.Cos(a), y, c.Z + r * MathF.Sin(a));

        private static Vec3 Tangent(float a) => new Vec3(-MathF.Sin(a), 0f, MathF.Cos(a));
    }
}
