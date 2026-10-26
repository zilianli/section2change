using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// 弯道适配：立交按直线生成后整体沿实际道路的中心线弯曲（方案见 docs/04）。局部坐标里主线沿 ê_m = FromAngle(0)、被交道路沿
    /// ê_c = FromAngle(交角)，中心在原点；斜坐标 p = a·ê_m + b·ê_c。
    ///   沿主线弯 W_M(p) = A_m(a) + Rot(φ_m(a))·A_c(b)：主线中心线和与它平行的各幅精确落在平行线上（被交道路中心线也在）；
    ///   沿被交道路弯 W_C(p) = A_c(b) + Rot(φ_c(b))·A_m(a)：对被交道路和它的平行各幅同样精确（主线中心线也在）；
    ///   混合 W = (1 − t)·W_M + t·W_C。每条路中心线两侧 kCorridor 米是它的“走廊”（各幅、拉宽的分隔带、贴着走的匝道），
    ///   距离按扣掉走廊算：d_m = max(0, 离主线距离 − kCorridor)，d_c 同理，t = smoothstep(0.1, 0.9, d_m / (d_m + d_c + kSoftness))。
    ///   走廊里横向各处 t 相同，平行的各幅和匝道一起弯、相对位置不变（分流、汇入处不会被扭出“形状无效”）；
    ///   两条走廊重叠的中心区主线优先（t = 0），被交道路的两幅在那里略有偏转，离开主线走廊约 kSoftness × 9 米后完全精确；
    ///   象限里的匝道被平滑拉伸 / 压缩（两条路都弯时这是必然的）。
    /// A(s) 是这条路中心线上离中心弧长 s 处的点，φ(s) 是那里切线相对中心处转过的角度（Centerline，不必是圆弧）；
    /// 每条路中心前后两侧各一条中心线（例如一侧直、一侧弯），两侧在中心处都与原方向相切。
    /// 高度（Y）不变。同一个局部点总是得到同一个结果，共用的节点照样精确相等。
    ///
    /// 三条臂（Y 型分向：三条臂互不相对，没有主线 / 被交道路之分）另用一种：每条臂只有从中心离开的一条中心线，
    ///   点落在相邻两条臂 i、j（顺时针）之间的扇区里，按这两条臂的斜坐标 p = a·ê_i + b·ê_j（a、b ≥ 0）照两条路的公式弯：
    ///   W_i = A_i(a) + Rot(φ_i(a))·A_j(b)，W_j = A_j(b) + Rot(φ_j(b))·A_i(a)，混合 W = (1 − t)·W_i + t·W_j，
    ///   t = smoothstep(0.1, 0.9, 0.5 + 0.5·(d_i − d_j) / (d_i + d_j + kSoftness))（d 是到两条臂的距离扣掉走廊；中心区两者平均）。
    ///   臂上 W_i、W_j 都等于 A_i(a)，所以跨过臂进入另一个扇区时连续。斜坐标的横向分量沿相邻那条臂取，它也弯时臂的走廊里横距会差
    ///   几十厘米，所以离中心 kExactFrom–kExactTo 米起走廊里逐渐改按这条臂的正交坐标弯：A_i(α) + Rot(φ_i(α))·(λ·R_i)
    ///   （α、λ 是沿臂和向右的分量；臂上两者相同，仍连续），臂端、分岔处的各幅和匝道精确平行。
    /// </summary>
    public sealed class CurveWarp
    {
        /// <summary>数值求雅可比矩阵（变换控制点的切线）的差分步长（米）。</summary>
        private const float kStep = 0.5f;
        /// <summary>每条路中心线两侧算作它的走廊的宽度（米）：各幅、拉宽的分隔带、贴着走的匝道都在里面。</summary>
        private const float kCorridor = 60f;
        /// <summary>离开走廊后两种弯法过渡的快慢（米）：被交道路走廊里离开主线走廊 9 倍这么远后完全按被交道路弯；再小时象限里的苜蓿叶环道被压得太陡，再大时立交两端对不准原有道路。</summary>
        private const float kSoftness = 20f;
        /// <summary>三条臂时：离中心这段距离（米）里，臂的走廊逐渐改按这条臂的正交坐标弯（之外精确平行）。</summary>
        private const float kExactFrom = 120f, kExactTo = 200f;

        private readonly Vec3 m_Main, m_Cross;
        private readonly Centerline m_MainAhead, m_MainBehind, m_CrossAhead, m_CrossBehind;
        private readonly float m_MainYaw, m_CrossYaw;
        private readonly float m_Det;
        /// <summary>三条臂时：每条臂的方向、方向角（弧度）和中心线；两条路时为 null。</summary>
        private readonly (Vec3 Direction, float Yaw, Centerline Line)[]? m_Legs;

        /// <summary>两条路各是一段圆弧：曲率 κ（沿 ê 前进向右弯为正，0 = 直线），中心前后是同一个圆。</summary>
        public CurveWarp(float crossingAngle, float mainCurvature, float crossCurvature)
            : this(crossingAngle, ArcFrom(mainCurvature), ArcFrom(-mainCurvature), ArcFrom(crossCurvature), ArcFrom(-crossCurvature))
        {
        }

        /// <param name="crossingAngle">被交道路方向（度，相对主线顺时针），即 Legs[1].Angle。</param>
        /// <param name="mainAhead">主线从中心沿 ê_m 往前的中心线；mainBehind 是沿 −ê_m 往后的（都按“离开中心”的方向记）。</param>
        /// <param name="crossAhead">被交道路沿 ê_c 往前的中心线；crossBehind 是沿 −ê_c 的。</param>
        public CurveWarp(float crossingAngle, Centerline mainAhead, Centerline mainBehind, Centerline crossAhead, Centerline crossBehind)
        {
            m_Main = Directions.FromAngle(0f);
            m_Cross = Directions.FromAngle(crossingAngle);
            m_MainYaw = 0f;
            m_CrossYaw = crossingAngle * Directions.DegToRad;
            m_MainAhead = mainAhead;
            m_MainBehind = mainBehind;
            m_CrossAhead = crossAhead;
            m_CrossBehind = crossBehind;
            m_Det = Cross2(m_Main, m_Cross);
            IsIdentity = mainAhead.IsStraight && mainBehind.IsStraight && crossAhead.IsStraight && crossBehind.IsStraight;
        }

        /// <summary>
        /// 三条臂（相邻夹角都小于 180°）各沿自己的中心线弯：臂 i 沿 FromAngle(legAngles[i])，中心线按“离开中心”的方向记。
        /// </summary>
        public CurveWarp(IReadOnlyList<float> legAngles, IReadOnlyList<Centerline> legLines)
            : this(0f, Centerline.Straight, Centerline.Straight, Centerline.Straight, Centerline.Straight)
        {
            // 按方向角从小到大（顺时针）排好，相邻两条之间是一个扇区
            m_Legs = legAngles.Select((angle, i) => (Directions.FromAngle(angle), Wrap(angle * Directions.DegToRad), legLines[i]))
                .OrderBy(l => l.Item2).ToArray();
            IsIdentity = legLines.All(l => l.IsStraight);
        }

        private static float Wrap(float radians)
        {
            float full = 2f * MathF.PI;
            return ((radians % full) + full) % full;
        }

        private static Centerline ArcFrom(float curvature) => curvature == 0f ? Centerline.Straight : Centerline.Arc(curvature);

        /// <summary>两条路都是直线：不用弯。</summary>
        public bool IsIdentity { get; }

        /// <summary>局部点弯曲后的位置（Y 不变）。</summary>
        public Vec3 Apply(Vec3 p)
        {
            if (IsIdentity)
                return p;
            if (m_Legs != null)
                return ApplyLegs(p);
            // 斜坐标：p = a·ê_m + b·ê_c
            float a = Cross2(p, m_Cross) / m_Det, b = Cross2(m_Main, p) / m_Det;
            var (arcMain, turnMain) = Along(m_MainAhead, m_MainBehind, m_MainYaw, a);
            var (arcCross, turnCross) = Along(m_CrossAhead, m_CrossBehind, m_CrossYaw, b);
            Vec3 alongMain = arcMain + arcCross.RotateY(turnMain);
            Vec3 alongCross = arcCross + arcMain.RotateY(turnCross);
            // 到两条中心线的垂直距离扣掉走廊：走廊里横向各处 t 相同；中心区（都在走廊里）主线优先
            float sin = Math.Abs(m_Det);
            float dm = Math.Max(0f, Math.Abs(b) * sin - kCorridor), dc = Math.Max(0f, Math.Abs(a) * sin - kCorridor);
            float t = SmoothStep(0.1f, 0.9f, dm / (dm + dc + kSoftness));
            Vec3 flat = alongMain * (1f - t) + alongCross * t;
            return new Vec3(flat.X, p.Y, flat.Z);
        }

        /// <summary>三条臂时的弯曲（见类说明）。</summary>
        private Vec3 ApplyLegs(Vec3 p)
        {
            var legs = m_Legs!;
            // 所在扇区：p 的方向角（从 +Z 顺时针）落在臂 i 和下一条臂 j 之间
            float heading = Wrap(MathF.Atan2(p.X, p.Z));
            int i = 0;
            for (int k = 0; k < legs.Length; k++)
            {
                float from = legs[k].Yaw, to = legs[(k + 1) % legs.Length].Yaw;
                if (Wrap(heading - from) <= Wrap(to - from))
                {
                    i = k;
                    break;
                }
            }
            var (ei, yawI, lineI) = legs[i];
            var (ej, yawJ, lineJ) = legs[(i + 1) % legs.Length];
            float det = Cross2(ei, ej);
            float a = Math.Max(0f, Cross2(p, ej) / det), b = Math.Max(0f, Cross2(ei, p) / det);
            Vec3 arcI = lineI.Point(a).RotateY(yawI), arcJ = lineJ.Point(b).RotateY(yawJ);
            Vec3 alongI = arcI + arcJ.RotateY(lineI.Angle(a));
            Vec3 alongJ = arcJ + arcI.RotateY(lineJ.Angle(b));
            float sin = Math.Abs(det);
            float di = Math.Max(0f, b * sin - kCorridor), dj = Math.Max(0f, a * sin - kCorridor);
            float t = SmoothStep(0.1f, 0.9f, 0.5f + 0.5f * (di - dj) / (di + dj + kSoftness));
            Vec3 flat = alongI * (1f - t) + alongJ * t;
            // 臂的走廊里（离中心够远时）按这条臂的正交坐标弯：各幅、匝道精确平行
            foreach (var (direction, yaw, line) in legs)
            {
                Vec3 right = Directions.RightOf(direction);
                float along = p.X * direction.X + p.Z * direction.Z, lateral = p.X * right.X + p.Z * right.Z;
                float exact = SmoothStep(kExactFrom, kExactTo, along) * (1f - SmoothStep(kCorridor, kCorridor + kSoftness, Math.Abs(lateral)));
                if (exact > 0f)
                {
                    Vec3 parallel = line.Point(along).RotateY(yaw) + (right * lateral).RotateY(line.Angle(along));
                    flat = flat + (parallel - flat) * exact;
                }
            }
            return new Vec3(flat.X, p.Y, flat.Z);
        }

        /// <summary>
        /// 路段弯曲：端点和节点按 Apply 变换；两头切线方向按端点处的雅可比矩阵变换（相邻路段在共用节点处仍相切），
        /// 两个控制柄的长度按最小二乘贴合逐点弯曲的原曲线（控制柄连同高度方向一起缩放，两端坡度不变）。
        /// </summary>
        public RoadSegmentSpec Apply(RoadSegmentSpec segment)
        {
            if (IsIdentity)
                return segment;
            var c = segment.Curve;
            Vec3 p0 = Apply(c.P0), p3 = Apply(c.P3);
            Vec3 d0 = Jacobian(c.P0, c.P1 - c.P0), d3 = Jacobian(c.P3, c.P2 - c.P3);
            var (k0, k3) = FitHandles(c, p0, p3, d0, d3);
            // 控制柄整体缩放（连同高度方向）：两端的坡度与弯曲前相同
            var curve = new Bezier3(p0, p0 + d0 * k0, p3 + d3 * k3, p3);
            return new RoadSegmentSpec(segment.Name, segment.Role, curve, segment.Prefab, Apply(segment.StartNode), Apply(segment.EndNode));
        }

        /// <summary>
        /// 控制柄长度的倍数 (k0, k3)：贝塞尔 B(t) = (1−t)³P0 + 3(1−t)²t(P0 + k0·d0) + 3(1−t)t²(P3 + k3·d3) + t³P3 在俯视图上
        /// 最接近逐点弯曲的原曲线（按原参数 t 对应，取中间 7 个点）。解不出来或离谱时用 1。
        /// </summary>
        private (float, float) FitHandles(Bezier3 original, Vec3 p0, Vec3 p3, Vec3 d0, Vec3 d3)
        {
            double a11 = 0, a12 = 0, a22 = 0, r1 = 0, r2 = 0;
            for (int i = 1; i < 8; i++)
            {
                float t = i / 8f, u = 1f - t;
                float b1 = 3f * u * u * t, b2 = 3f * u * t * t;
                Vec3 target = Apply(original.Evaluate(t)), basis = p0 * (u * u * u + b1) + p3 * (b2 + t * t * t);
                Vec3 r = target - basis;
                double e0 = d0.X * r.X + d0.Z * r.Z, e3 = d3.X * r.X + d3.Z * r.Z, e03 = d0.X * d3.X + d0.Z * d3.Z;
                a11 += b1 * b1 * (d0.X * d0.X + d0.Z * d0.Z);
                a22 += b2 * b2 * (d3.X * d3.X + d3.Z * d3.Z);
                a12 += b1 * b2 * e03;
                r1 += b1 * e0;
                r2 += b2 * e3;
            }
            double det = a11 * a22 - a12 * a12;
            if (Math.Abs(det) < 1e-9)
                return (1f, 1f);
            float k0 = (float)((r1 * a22 - r2 * a12) / det), k3 = (float)((a11 * r2 - a12 * r1) / det);
            return k0 > 0.3f && k0 < 3f && k3 > 0.3f && k3 < 3f ? (k0, k3) : (1f, 1f);
        }

        public List<RoadSegmentSpec> Apply(IEnumerable<RoadSegmentSpec> segments) => segments.Select(Apply).ToList();

        /// <summary>切线 v（在 p 处）弯曲后的样子：水平分量按数值雅可比矩阵变换，竖直分量不变。</summary>
        private Vec3 Jacobian(Vec3 p, Vec3 v)
        {
            float length = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            if (length < 1e-4f)
                return v;
            var dir = new Vec3(v.X / length, 0f, v.Z / length);
            Vec3 ahead = Apply(p + dir * kStep), behind = Apply(p - dir * kStep);
            Vec3 d = (ahead - behind) * (length / (2f * kStep));
            return new Vec3(d.X, v.Y, d.Z);
        }

        /// <summary>
        /// 一条路中心线上离中心弧长 s（正 = 前方、负 = 后方）处的点，以及那里沿前进方向的切线相对中心处转过的角度。
        /// 后方那条按“离开中心”记：转到 −ê 方向；反过来走时切线反向，转角不变号（向它左边弯 = 沿前进方向往右弯的另一半）。
        /// </summary>
        private static (Vec3, float) Along(Centerline ahead, Centerline behind, float yaw, float s) => s >= 0f
            ? (ahead.Point(s).RotateY(yaw), ahead.Angle(s))
            : (behind.Point(-s).RotateY(yaw + MathF.PI), behind.Angle(-s));

        private static float Cross2(Vec3 u, Vec3 v) => u.X * v.Z - u.Z * v.X;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - edge0) / (edge1 - edge0)));
            return t * t * (3f - 2f * t);
        }
    }
}
