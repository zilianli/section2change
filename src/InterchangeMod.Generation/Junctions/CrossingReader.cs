using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Junctions
{
    /// <summary>参与立体交叉的一条已有路段（工具层从游戏读出，曲线为世界坐标）。</summary>
    public sealed class CrossingRoad
    {
        public Bezier3 Curve { get; set; }
        public string Road { get; set; } = "";
        public bool OneWay { get; set; }
        /// <summary>单行道路是否沿曲线方向（P0 → P3）行驶。</summary>
        public bool Forward { get; set; } = true;
        public float Width { get; set; }
        public int LanesPerDirection { get; set; }
        public bool IsHighway { get; set; }
    }

    /// <summary>
    /// 立体交叉（两条道路相交但没有共用节点，如高架跨过街道）的读取：
    /// 把每条路段在交叉点处切成两半，当作离开“路口”的两条路段（ExternalEdge），交给 JunctionMatcher 匹配。
    /// 双幅道路的两幅各自与另一条道路相交，交叉点之间的距离就是分隔带测量的依据。
    /// </summary>
    public static class CrossingReader
    {
        /// <summary>
        /// 两条曲线在水平面上的交点参数（折线近似，每条 segments 段）。没有交点时返回 false。
        /// 有多个交点时取第一个（沿 a）。
        /// </summary>
        public static bool Intersect(Bezier3 a, Bezier3 b, out float ta, out float tb, int segments = 16)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments, a1 = (i + 1) / (float)segments;
                Vec3 p0 = a.Evaluate(a0), p1 = a.Evaluate(a1);
                for (int j = 0; j < segments; j++)
                {
                    float b0 = j / (float)segments, b1 = (j + 1) / (float)segments;
                    Vec3 q0 = b.Evaluate(b0), q1 = b.Evaluate(b1);
                    if (SegmentIntersect(p0, p1, q0, q1, out float s, out float u))
                    {
                        ta = a0 + (a1 - a0) * s;
                        tb = b0 + (b1 - b0) * u;
                        return true;
                    }
                }
            }
            ta = tb = 0f;
            return false;
        }

        /// <summary>
        /// 把一条路段在参数 t 处切开，得到两条离开交叉点的“路段”：一条沿曲线方向，一条逆曲线方向。
        /// 高度取 elevation（工具层按地形换算好的离地高度）。
        /// </summary>
        public static IEnumerable<ExternalEdge> Split(CrossingRoad road, float t, float elevation)
        {
            Vec3 point = road.Curve.Evaluate(t);
            Vec3 tangent = road.Curve.Derivative(t);
            Vec3 forward = Vec3.Normalize(new Vec3(tangent.X, 0f, tangent.Z));
            var anchor = new Vec3(point.X, 0f, point.Z);
            foreach (bool alongCurve in new[] { true, false })
            {
                yield return new ExternalEdge
                {
                    Anchor = anchor,
                    Direction = alongCurve ? forward : -forward,
                    Road = road.Road,
                    OneWay = road.OneWay,
                    // 沿曲线方向离开的那一半，行驶方向与曲线相同时就是“驶离交叉点”
                    TravelOutward = alongCurve == road.Forward,
                    Width = road.Width,
                    LanesPerDirection = road.LanesPerDirection,
                    IsHighway = road.IsHighway,
                    Elevation = elevation,
                };
            }
        }

        /// <summary>水平面上两条线段 p0p1、q0q1 的交点，s / u 为各自的比例。</summary>
        private static bool SegmentIntersect(Vec3 p0, Vec3 p1, Vec3 q0, Vec3 q1, out float s, out float u)
        {
            float dx = p1.X - p0.X, dz = p1.Z - p0.Z;
            float ex = q1.X - q0.X, ez = q1.Z - q0.Z;
            float denominator = dx * ez - dz * ex;
            s = u = 0f;
            if (Math.Abs(denominator) < 1e-6f)
                return false;
            float fx = q0.X - p0.X, fz = q0.Z - p0.Z;
            s = (fx * ez - fz * ex) / denominator;
            u = (fx * dz - fz * dx) / denominator;
            return s >= 0f && s <= 1f && u >= 0f && u <= 1f;
        }
    }
}
