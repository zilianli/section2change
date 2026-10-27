using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>
    /// 立交在地面上占的范围（俯视，忽略高度），两种：
    ///   凸包：所有生成路段中心线的凸包，再向外扩 Margin 米——被改造的那几条原有道路在范围内的部分都要拆掉（包括匝道之间的空地）；
    ///   走廊（Corridor）：每段新路中心线两侧各（半宽 + Margin）米的并集——其他原有道路只拆真正压到新路的部分
    ///   （弯道上凸包会把弯道内侧几百米外的街道也包进去）。
    /// 判断时可以再加一个宽度（extra，原有道路的半宽）：原有道路的中心线离新路不到（半宽 + Margin + extra）就算挨着；
    /// 走廊还按高度判断是否压到（Collides）：高差够净空的（如从高架匝道下面穿过的地面道路）不算。
    /// </summary>
    public sealed class Footprint
    {
        private const int kCurveSamples = 16;
        /// <summary>走廊的网格边长（米）：采样出的折线段按网格分桶，判断时只看附近几格。</summary>
        private const float kCell = 32f;
        /// <summary>上下两条路之间至少要有的高差（米）：比这小就算压到（游戏内实测的最小层间高差）。</summary>
        private const float kClearance = HeightRange.MeasuredMinimumGap;
        /// <summary>立柱沿桥面方向的厚度（米）。</summary>
        private const float kPillarThickness = 3f;

        private readonly List<(float X, float Z)> m_Hull;
        /// <summary>走廊：每段新路中心线采样成的折线段、它的半宽和两端的离地高度；凸包时为 null。</summary>
        private readonly List<(float AX, float AZ, float BX, float BZ, float Half, float AH, float BH)>? m_Pieces;
        /// <summary>
        /// 走廊：新路高架节点（离地达到桥梁高度）的立柱——垂直于桥面、和桥面一样宽的薄墙：中心、沿墙方向、半长、桥面高度。
        /// </summary>
        private readonly List<(float CX, float CZ, float DX, float DZ, float HalfLength, float Top)>? m_Pillars;
        private readonly Dictionary<(int, int), List<int>>? m_Cells;
        private readonly float m_MaxHalf;

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

        /// <summary>走廊范围：每段新路（中心线、半宽）两侧各（半宽 + margin）米的并集。</summary>
        public static Footprint Corridor(IEnumerable<(Bezier3 Curve, float HalfWidth)> roads, float margin) => new Footprint(roads, margin);

        private Footprint(IEnumerable<(Bezier3 Curve, float HalfWidth)> roads, float margin)
        {
            m_Hull = new List<(float X, float Z)>();
            m_Pieces = new List<(float AX, float AZ, float BX, float BZ, float Half, float AH, float BH)>();
            m_Pillars = new List<(float CX, float CZ, float DX, float DZ, float HalfLength, float Top)>();
            m_Cells = new Dictionary<(int, int), List<int>>();
            Margin = margin;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var (curve, half) in roads)
            {
                // 高架的两端节点下面有立柱
                AddPillar(curve.P0, curve.P1 - curve.P0, half);
                AddPillar(curve.P3, curve.P3 - curve.P2, half);
                Vec3 previous = curve.Evaluate(0f);
                for (int i = 1; i <= kCurveSamples; i++)
                {
                    Vec3 next = curve.Evaluate(i / (float)kCurveSamples);
                    int index = m_Pieces.Count;
                    m_Pieces.Add((previous.X, previous.Z, next.X, next.Z, half, previous.Y, next.Y));
                    m_MaxHalf = Math.Max(m_MaxHalf, half);
                    // 折线段经过的格子（按两端点的包围盒）都记上它
                    for (int x = Cell(Math.Min(previous.X, next.X)); x <= Cell(Math.Max(previous.X, next.X)); x++)
                        for (int z = Cell(Math.Min(previous.Z, next.Z)); z <= Cell(Math.Max(previous.Z, next.Z)); z++)
                        {
                            if (!m_Cells.TryGetValue((x, z), out var list))
                                m_Cells[(x, z)] = list = new List<int>();
                            list.Add(index);
                        }
                    float reach = half + margin;
                    minX = Math.Min(minX, Math.Min(previous.X, next.X) - reach);
                    maxX = Math.Max(maxX, Math.Max(previous.X, next.X) + reach);
                    minZ = Math.Min(minZ, Math.Min(previous.Z, next.Z) - reach);
                    maxZ = Math.Max(maxZ, Math.Max(previous.Z, next.Z) + reach);
                    previous = next;
                }
            }
            MinX = minX;
            MaxX = maxX;
            MinZ = minZ;
            MaxZ = maxZ;
        }

        /// <summary>节点 node 离地达到桥梁高度时记一根立柱：垂直于桥面方向 tangent、和桥面（半宽 half）一样宽。</summary>
        private void AddPillar(Vec3 node, Vec3 tangent, float half)
        {
            float length = MathF.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            if (node.Y < HeightRange.BridgeThreshold || length < 1e-4f)
                return;
            m_Pillars!.Add((node.X, node.Z, -tangent.Z / length, tangent.X / length, half, node.Y));
        }

        private static int Cell(float v) => (int)Math.Floor(v / kCell);

        /// <summary>true = 走廊范围；false = 凸包。</summary>
        public bool IsCorridor => m_Pieces != null;

        public float Margin { get; }

        /// <summary>包围盒（含 Margin，走廊时还含各路半宽），用于在游戏的空间索引里粗查。</summary>
        public float MinX { get; }
        public float MaxX { get; }
        public float MinZ { get; }
        public float MaxZ { get; }

        /// <summary>凸包顶点（逆时针，不含 Margin）；走廊时为空。</summary>
        public IReadOnlyList<(float X, float Z)> Hull => m_Hull;

        /// <summary>
        /// 凸包：点在凸包内，或离凸包边不超过 Margin + extra。
        /// 走廊：点离某段新路中心线不超过（它的半宽 + Margin + extra）。
        /// </summary>
        public bool Contains(float x, float z, float extra = 0f)
        {
            if (m_Pieces != null)
                return InCorridor(x, z, extra);
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
            return best <= Margin + extra;
        }

        public bool Contains(Vec3 p, float extra = 0f) => Contains(p.X, p.Z, extra);

        private bool InCorridor(float x, float z, float extra)
        {
            float reach = m_MaxHalf + Margin + extra;
            int x0 = Cell(x - reach), x1 = Cell(x + reach), z0 = Cell(z - reach), z1 = Cell(z + reach);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!m_Cells!.TryGetValue((cx, cz), out var list))
                        continue;
                    foreach (int i in list)
                    {
                        var piece = m_Pieces![i];
                        if (DistanceToSegment(x, z, (piece.AX, piece.AZ), (piece.BX, piece.BZ)) <= piece.Half + Margin + extra)
                            return true;
                    }
                }
            return false;
        }

        /// <summary>
        /// 走廊：离地高度 elevation 的原有道路上一点（extra：它的半宽）是否被新路压到——平面上挨着某段新路（离中心线不到
        /// 半宽 + Margin + extra），并且高度上挨着：那段新路在这里是路堤（实心，原有道路不比它高出净空就过不去）或路堑
        /// （开口，原有道路不比它低出净空就过不去），否则高差不到净空 kClearance；或者落在新路高架节点的立柱上（原有道路在桥面以下）。
        /// 从高架下面、净空够的地方穿过的原有道路不算压到。
        /// </summary>
        public bool Collides(Vec3 p, float elevation, float extra)
        {
            if (m_Pieces == null)
                return Contains(p, extra);
            float x = p.X, z = p.Z, reach = m_MaxHalf + Margin + extra;
            int x0 = Cell(x - reach), x1 = Cell(x + reach), z0 = Cell(z - reach), z1 = Cell(z + reach);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!m_Cells!.TryGetValue((cx, cz), out var list))
                        continue;
                    foreach (int i in list)
                    {
                        var piece = m_Pieces[i];
                        float u = Project(x, z, (piece.AX, piece.AZ), (piece.BX, piece.BZ));
                        float px = piece.AX + (piece.BX - piece.AX) * u - x, pz = piece.AZ + (piece.BZ - piece.AZ) * u - z;
                        if (MathF.Sqrt(px * px + pz * pz) > piece.Half + Margin + extra)
                            continue;
                        float height = piece.AH + (piece.BH - piece.AH) * u;
                        bool blocked = LevelPlanner.BandOf(height) switch
                        {
                            ElevationBand.Raised => elevation < height + kClearance,
                            ElevationBand.Cutting => elevation > height - kClearance,
                            _ => Math.Abs(elevation - height) < kClearance,
                        };
                        if (blocked)
                            return true;
                    }
                }
            foreach (var pillar in m_Pillars!)
            {
                if (elevation > pillar.Top - 1f)
                    continue;
                var a = (pillar.CX - pillar.DX * pillar.HalfLength, pillar.CZ - pillar.DZ * pillar.HalfLength);
                var b = (pillar.CX + pillar.DX * pillar.HalfLength, pillar.CZ + pillar.DZ * pillar.HalfLength);
                if (DistanceToSegment(x, z, a, b) <= kPillarThickness / 2f + Margin + extra)
                    return true;
            }
            return false;
        }

        /// <summary>曲线有没有任何一段在范围内。</summary>
        public bool Intersects(Bezier3 curve, int samples = 64, float extra = 0f)
        {
            var outside = OutsideIntervals(curve, samples, extra);
            return !(outside.Count == 1 && outside[0].Start == 0f && outside[0].End == 1f);
        }

        /// <summary>
        /// 曲线在范围外的参数区间（t 从 0 到 1，按顺序）。区间端点用二分法求到约 1e-4。
        /// 整条在范围内时返回空列表；整条在范围外时返回 [(0, 1)]。
        /// </summary>
        public List<(float Start, float End)> OutsideIntervals(Bezier3 curve, int samples = 64, float extra = 0f) =>
            OutsideIntervals(t => Contains(curve.Evaluate(t), extra), samples);

        /// <summary>
        /// 按参数 t（0 到 1）判断在不在范围内（inside），求在范围外的参数区间（按顺序）；区间端点用二分法求到约 1e-4。
        /// 整段都在范围内时返回空列表；整段都在范围外时返回 [(0, 1)]。
        /// </summary>
        public static List<(float Start, float End)> OutsideIntervals(Func<float, bool> inside, int samples = 64)
        {
            var result = new List<(float Start, float End)>();
            float? start = null;
            bool previousInside = false;
            float previousT = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                bool isInside = inside(t);
                if (i == 0)
                {
                    if (!isInside)
                        start = 0f;
                }
                else if (isInside != previousInside)
                {
                    float boundary = Boundary(inside, previousT, t, previousInside);
                    if (isInside)
                    {
                        result.Add((start!.Value, boundary));
                        start = null;
                    }
                    else
                    {
                        start = boundary;
                    }
                }
                previousInside = isInside;
                previousT = t;
            }
            if (start.HasValue)
                result.Add((start.Value, 1f));
            return result;
        }

        /// <summary>在 [lo, hi] 里找进出范围的位置；lo 处的内外状态为 loInside。</summary>
        private static float Boundary(Func<float, bool> inside, float lo, float hi, bool loInside)
        {
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (inside(mid) == loInside)
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

        /// <summary>点在线段 ab 上的投影参数（0 到 1）。</summary>
        private static float Project(float x, float z, (float X, float Z) a, (float X, float Z) b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z, lengthSq = dx * dx + dz * dz;
            return lengthSq <= 0f ? 0f : Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / lengthSq));
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
