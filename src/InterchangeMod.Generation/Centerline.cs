using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// 弯道适配（docs/04）：一条路从立交中心往一侧走的中心线形状。标准朝向：从原点出发，起点切线沿 +Z；
    /// 按弧长 s 等距取点，Point(s) 是位置，Angle(s) 是切线相对起点转过的角度（弧度，RotateY 的方向，向右为正）。
    /// 超出已知长度的部分按最后的切线直线延伸。由工具层沿游戏里的道路追踪出来（不必是圆弧），测试里也可以直接造圆弧。
    /// </summary>
    public sealed class Centerline
    {
        /// <summary>重采样间距（米）。</summary>
        private const float kStep = 2f;

        private readonly Vec3[] m_Points;
        private readonly float[] m_Angles;

        private Centerline(Vec3[] points, float[] angles)
        {
            m_Points = points;
            m_Angles = angles;
        }

        /// <summary>直线（不弯）。</summary>
        public static Centerline Straight { get; } = new Centerline(new[] { Vec3.Zero, new Vec3(0f, 0f, kStep) }, new[] { 0f, 0f });

        /// <summary>整条都是直线（转角都在 0.01° 以内）。</summary>
        public bool IsStraight
        {
            get
            {
                foreach (float a in m_Angles)
                    if (Math.Abs(a) > 1.7e-4f)
                        return false;
                return true;
            }
        }

        /// <summary>已知的长度（米）。</summary>
        public float Length => (m_Points.Length - 1) * kStep;

        /// <summary>
        /// 整条（已知长度内）离起点切线最远不到 tolerance 米：立交的臂沿起点方向直着放也落在道路上，可以当直线。
        /// 不能按半径判断：半径 3000 米的弯道在 700 米外已经偏出 80 米，大型立交的臂端接不回原有道路。
        /// </summary>
        public bool NearlyStraight(float tolerance)
        {
            foreach (var p in m_Points)
                if (Math.Abs(p.X) > tolerance)
                    return false;
            return true;
        }

        /// <summary>曲率 κ（向右弯为正）、长 length 的圆弧。</summary>
        public static Centerline Arc(float curvature, float length = 3000f)
        {
            int n = Math.Max(2, (int)Math.Ceiling(length / kStep) + 1);
            var points = new Vec3[n];
            var angles = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = i * kStep, angle = curvature * s;
                angles[i] = angle;
                if (Math.Abs(curvature) < 1e-7f)
                    points[i] = new Vec3(0f, 0f, s);
                else
                {
                    float half = MathF.Sin(angle / 2f);
                    points[i] = new Vec3(2f * half * half / curvature, 0f, MathF.Sin(angle) / curvature);
                }
            }
            return new Centerline(points, angles);
        }

        /// <summary>
        /// 由一串俯视点（从立交中心出发往外，任意坐标系）建中心线：平移到原点，按 direction（这条路在中心处离开的方向，
        /// 路口匹配读出的路段切线，比从点估计准）转到沿 +Z，按弧长重采样。点太少或总长不到一个采样间距时当直线。
        /// </summary>
        public static Centerline FromPoints(IReadOnlyList<Vec3> points, Vec3 direction)
        {
            if (points == null || points.Count < 2)
                return Straight;
            var flat = new List<Vec3>(points.Count);
            foreach (var p in points)
                if (flat.Count == 0 || Distance(p, flat[flat.Count - 1]) > 1e-3f)
                    flat.Add(new Vec3(p.X, 0f, p.Z));
            if (flat.Count < 2)
                return Straight;
            Vec3 origin = flat[0];
            float yaw = MathF.Atan2(direction.X, direction.Z);
            for (int i = 0; i < flat.Count; i++)
                flat[i] = (flat[i] - origin).RotateY(-yaw);

            // 按弧长重采样
            var resampled = new List<Vec3> { Vec3.Zero };
            float carried = 0f;
            for (int i = 1; i < flat.Count; i++)
            {
                Vec3 a = flat[i - 1], b = flat[i];
                float length = Distance(a, b);
                float s = kStep - carried;
                while (s <= length)
                {
                    resampled.Add(Vec3.Lerp(a, b, s / length));
                    s += kStep;
                }
                carried = length - (s - kStep);
            }
            if (resampled.Count < 2)
                return Straight;
            var angles = new float[resampled.Count];
            for (int i = 0; i < resampled.Count; i++)
            {
                Vec3 d = i + 1 < resampled.Count ? resampled[i + 1] - resampled[Math.Max(0, i - 1)] : resampled[i] - resampled[i - 1];
                angles[i] = MathF.Atan2(d.X, d.Z);
            }
            // 角度连续（不在 ±π 处跳变）；起点处与 direction 一致，接近 0
            angles[0] = WrapPi(angles[0]);
            for (int i = 1; i < angles.Length; i++)
                angles[i] = angles[i - 1] + WrapPi(angles[i] - angles[i - 1]);
            return new Centerline(resampled.ToArray(), angles);
        }

        /// <summary>弧长 s（≥ 0）处的位置。</summary>
        public Vec3 Point(float s)
        {
            float u = Math.Max(0f, s) / kStep;
            int last = m_Points.Length - 1;
            if (u >= last)
            {
                // 超出已知长度：按最后的切线直线延伸
                float angle = m_Angles[last];
                return m_Points[last] + new Vec3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * ((u - last) * kStep);
            }
            int i = (int)u;
            return Vec3.Lerp(m_Points[i], m_Points[i + 1], u - i);
        }

        /// <summary>弧长 s 处切线相对起点转过的角度（弧度，向右为正）。</summary>
        public float Angle(float s)
        {
            float u = Math.Max(0f, s) / kStep;
            int last = m_Angles.Length - 1;
            if (u >= last)
                return m_Angles[last];
            int i = (int)u;
            return m_Angles[i] + (m_Angles[i + 1] - m_Angles[i]) * (u - i);
        }

        /// <summary>前 length 米里最急处的半径（米）：每 window 米切线转角 ÷ window 的倒数；直线时为正无穷。</summary>
        public float MinRadius(float length, float window = 20f)
        {
            float worst = 0f;
            for (float s = 0f; s + window <= length; s += kStep)
                worst = Math.Max(worst, Math.Abs(Angle(s + window) - Angle(s)) / window);
            return worst < 1e-7f ? float.PositiveInfinity : 1f / worst;
        }

        private static float Distance(Vec3 a, Vec3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        private static float WrapPi(float angle)
        {
            while (angle > MathF.PI)
                angle -= 2f * MathF.PI;
            while (angle < -MathF.PI)
                angle += 2f * MathF.PI;
            return angle;
        }
    }
}
