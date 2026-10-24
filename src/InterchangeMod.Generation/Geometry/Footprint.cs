using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>
    /// 立交在地面上占的范围（俯视，忽略高度）：所有生成路段中心线的凸包，再向外扩 Margin 米。
    /// 原路口改造时，原有道路在范围内的部分要拆掉，范围外的部分保留。
    /// </summary>
    public sealed class Footprint
    {
        private const int kCurveSamples = 16;

        private readonly List<(float X, float Z)> m_Hull;

        public Footprint(IEnumerable<Bezier3> curves, float margin)
        {
            var points = new List<(float X, float Z)>();
            foreach (Bezier3 curve in curves)
            {
                for (int i = 0; i <= kCurveSamples; i++)
                {
                    Vec3 p = curve.Evaluate(i / (float)kCurveSamples);
                    points.Add((p.X, p.Z));
                }
            }
            m_Hull = ConvexHull(points);
            Margin = margin;

            MinX = MinZ = float.MaxValue;
            MaxX = MaxZ = float.MinValue;
            foreach (var (x, z) in m_Hull)
            {
                MinX = Math.Min(MinX, x - margin);
                MaxX = Math.Max(MaxX, x + margin);
                MinZ = Math.Min(MinZ, z - margin);
                MaxZ = Math.Max(MaxZ, z + margin);
            }
        }

        public float Margin { get; }

        /// <summary>包围盒（含 Margin），用于在游戏的空间索引里粗查。</summary>
        public float MinX { get; }
        public float MaxX { get; }
        public float MinZ { get; }
        public float MaxZ { get; }

        /// <summary>凸包顶点（逆时针，不含 Margin）。</summary>
        public IReadOnlyList<(float X, float Z)> Hull => m_Hull;

        /// <summary>点在凸包内，或离凸包边不超过 Margin。</summary>
        public bool Contains(float x, float z)
        {
            if (m_Hull.Count == 0)
                return false;
            if (m_Hull.Count >= 3 && InsideHull(x, z))
                return true;
            float best = float.MaxValue;
            for (int i = 0; i < m_Hull.Count; i++)
            {
                var a = m_Hull[i];
                var b = m_Hull[(i + 1) % m_Hull.Count];
                best = Math.Min(best, DistanceToSegment(x, z, a, b));
            }
            return best <= Margin;
        }

        public bool Contains(Vec3 p) => Contains(p.X, p.Z);

        /// <summary>曲线有没有任何一段在范围内。</summary>
        public bool Intersects(Bezier3 curve, int samples = 64)
        {
            var outside = OutsideIntervals(curve, samples);
            return !(outside.Count == 1 && outside[0].Start == 0f && outside[0].End == 1f);
        }

        /// <summary>
        /// 曲线在范围外的参数区间（t 从 0 到 1，按顺序）。区间端点用二分法求到约 1e-4。
        /// 整条在范围内时返回空列表；整条在范围外时返回 [(0, 1)]。
        /// </summary>
        public List<(float Start, float End)> OutsideIntervals(Bezier3 curve, int samples = 64)
        {
            var result = new List<(float Start, float End)>();
            float? start = null;
            bool previousInside = false;
            float previousT = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                bool inside = Contains(curve.Evaluate(t));
                if (i == 0)
                {
                    if (!inside)
                        start = 0f;
                }
                else if (inside != previousInside)
                {
                    float boundary = Boundary(curve, previousT, t, previousInside);
                    if (inside)
                    {
                        result.Add((start!.Value, boundary));
                        start = null;
                    }
                    else
                    {
                        start = boundary;
                    }
                }
                previousInside = inside;
                previousT = t;
            }
            if (start.HasValue)
                result.Add((start.Value, 1f));
            return result;
        }

        /// <summary>在 [lo, hi] 里找进出范围的位置；lo 处的内外状态为 loInside。</summary>
        private float Boundary(Bezier3 curve, float lo, float hi, bool loInside)
        {
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Contains(curve.Evaluate(mid)) == loInside)
                    lo = mid;
                else
                    hi = mid;
            }
            // 取范围外的那一侧，保证保留的路段端点确实在范围外
            return loInside ? hi : lo;
        }

        private bool InsideHull(float x, float z)
        {
            for (int i = 0; i < m_Hull.Count; i++)
            {
                var a = m_Hull[i];
                var b = m_Hull[(i + 1) % m_Hull.Count];
                if (Cross(a, b, (x, z)) < 0f)
                    return false;
            }
            return true;
        }

        private static float DistanceToSegment(float x, float z, (float X, float Z) a, (float X, float Z) b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lengthSq = dx * dx + dz * dz;
            float t = lengthSq <= 0f ? 0f : Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / lengthSq));
            float px = a.X + t * dx - x, pz = a.Z + t * dz - z;
            return MathF.Sqrt(px * px + pz * pz);
        }

        private static float Cross((float X, float Z) o, (float X, float Z) a, (float X, float Z) b) =>
            (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);

        /// <summary>Andrew 单调链凸包，返回逆时针顶点。</summary>
        private static List<(float X, float Z)> ConvexHull(List<(float X, float Z)> points)
        {
            points.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Z.CompareTo(q.Z));
            if (points.Count < 3)
                return new List<(float X, float Z)>(points);
            var hull = new List<(float X, float Z)>();
            foreach (var p in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count + 1;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                var p = points[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }
    }
}
