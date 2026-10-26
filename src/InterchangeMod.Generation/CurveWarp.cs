using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// 弯道适配：立交按直线生成后整体沿弧线弯曲（方案见 docs/04）。局部坐标里主线沿 ê_m = FromAngle(0)、被交道路沿
    /// ê_c = FromAngle(交角)，中心在原点；斜坐标 p = a·ê_m + b·ê_c。
    ///   沿主线弯 W_M(p) = A_m(a) + Rot(φ_m(a))·(b·ê_c)：主线中心线和与它平行的各幅精确落在平行弧上；
    ///   沿被交道路弯 W_C(p) = A_c(b) + Rot(φ_c(b))·(a·ê_m)：对被交道路同样精确（被交道路中心线上 t = 1，W_M 不用管它）；
    ///   混合 W = (1 − t)·W_M + t·W_C，t = smoothstep(0.35, 0.65, |b| / (|a| + |b|))：离主线近用 W_M、离被交道路近用 W_C，
    ///   两条路和各自的平行各幅都精确，象限里的匝道被平滑拉伸 / 压缩。
    /// A(s) 是弧长 s 处的弧上点，曲率 κ 为正时向前进方向的右侧弯（右 = Directions.RightOf）；φ(s) = κ·s。
    /// 高度（Y）不变。同一个局部点总是得到同一个结果，共用的节点照样精确相等。
    /// </summary>
    public sealed class CurveWarp
    {
        /// <summary>数值求雅可比矩阵（变换控制点的切线）的差分步长（米）。</summary>
        private const float kStep = 0.5f;

        private readonly Vec3 m_Main, m_Cross;
        private readonly float m_MainCurvature, m_CrossCurvature;
        private readonly float m_Det;

        /// <param name="crossingAngle">被交道路方向（度，相对主线顺时针），即 Legs[1].Angle。</param>
        /// <param name="mainCurvature">主线曲率 1/R（沿 ê_m 前进向右弯为正；0 = 直线）。</param>
        /// <param name="crossCurvature">被交道路曲率（沿 ê_c 前进向右弯为正）。</param>
        public CurveWarp(float crossingAngle, float mainCurvature, float crossCurvature)
        {
            m_Main = Directions.FromAngle(0f);
            m_Cross = Directions.FromAngle(crossingAngle);
            m_MainCurvature = mainCurvature;
            m_CrossCurvature = crossCurvature;
            m_Det = Cross2(m_Main, m_Cross);
        }

        /// <summary>两条路都是直线：不用弯。</summary>
        public bool IsIdentity => m_MainCurvature == 0f && m_CrossCurvature == 0f;

        /// <summary>局部点弯曲后的位置（Y 不变）。</summary>
        public Vec3 Apply(Vec3 p)
        {
            if (IsIdentity)
                return p;
            // 斜坐标：p = a·ê_m + b·ê_c
            float a = Cross2(p, m_Cross) / m_Det, b = Cross2(m_Main, p) / m_Det;
            Vec3 arcMain = Arc(m_Main, m_MainCurvature, a), arcCross = Arc(m_Cross, m_CrossCurvature, b);
            Vec3 alongMain = arcMain + (m_Cross * b).RotateY(m_MainCurvature * a);
            Vec3 alongCross = arcCross + (m_Main * a).RotateY(m_CrossCurvature * b);
            float sum = Math.Abs(a) + Math.Abs(b);
            float t = sum < 1e-3f ? 0.5f : SmoothStep(0.35f, 0.65f, Math.Abs(b) / sum);
            Vec3 flat = alongMain * (1f - t) + alongCross * t;
            return new Vec3(flat.X, p.Y, flat.Z);
        }

        /// <summary>
        /// 路段弯曲：端点和节点按 Apply 变换；两头切线方向按端点处的雅可比矩阵变换（相邻路段在共用节点处仍相切），
        /// 两个控制柄的长度按最小二乘贴合逐点弯曲的原曲线。高度方向的控制柄不变。
        /// </summary>
        public RoadSegmentSpec Apply(RoadSegmentSpec segment)
        {
            if (IsIdentity)
                return segment;
            var c = segment.Curve;
            Vec3 p0 = Apply(c.P0), p3 = Apply(c.P3);
            Vec3 d0 = Jacobian(c.P0, c.P1 - c.P0), d3 = Jacobian(c.P3, c.P2 - c.P3);
            var (k0, k3) = FitHandles(c, p0, p3, d0, d3);
            var curve = new Bezier3(p0, p0 + Flat(d0) * k0 + Vertical(d0), p3 + Flat(d3) * k3 + Vertical(d3), p3);
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

        private static Vec3 Flat(Vec3 v) => new Vec3(v.X, 0f, v.Z);

        private static Vec3 Vertical(Vec3 v) => new Vec3(0f, v.Y, 0f);

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

        /// <summary>沿 dir 方向、曲率 κ 的弧上弧长 s 处的点（相对弧起点），κ 为正时向 dir 的右侧弯。</summary>
        private static Vec3 Arc(Vec3 dir, float curvature, float s)
        {
            if (Math.Abs(curvature) < 1e-7f)
                return dir * s;
            float angle = curvature * s;
            float half = MathF.Sin(angle / 2f);
            return dir * (MathF.Sin(angle) / curvature) + Directions.RightOf(dir) * (2f * half * half / curvature);
        }

        private static float Cross2(Vec3 u, Vec3 v) => u.X * v.Z - u.Z * v.X;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Max(0f, Math.Min(1f, (x - edge0) / (edge1 - edge0)));
            return t * t * (3f - 2f * t);
        }
    }
}
