using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>水平面上的向量小工具（匝道圆弧路径用）。</summary>
    internal static class Planar
    {
        /// <summary>从 v0 转到 v1 的角度（0 ~ 2π），sign &gt; 0 时朝 Perp 一侧转，否则反向。</summary>
        public static float RotAngle(Vec3 v0, Vec3 v1, float sign)
        {
            float angle = (float)Math.Atan2(DiamondTemplate.Cross2(v0, v1), Directions.Dot(Flat0(v0), Flat0(v1))) * sign;
            return angle < 0f ? angle + 2f * MathF.PI : angle;
        }

        public static Vec3 Flat(Vec3 v) => Vec3.Normalize(new Vec3(v.X, 0f, v.Z));

        /// <summary>水平面内把 v 转 90°，转向 Cross2 为正的一侧。</summary>
        public static Vec3 Perp(Vec3 v) => new Vec3(-v.Z, 0f, v.X);

        /// <summary>水平面内旋转：sign·angle &gt; 0 时转向 Perp 一侧。</summary>
        public static Vec3 Rot(Vec3 v, float angle) => v * MathF.Cos(angle) + Perp(v) * MathF.Sin(angle);

        public static Vec3 Flat0(Vec3 v) => new Vec3(v.X, 0f, v.Z);
    }

    // —— 匝道圆弧路径（喇叭形、苜蓿叶等共用）：与道路相切的单段 / 复合圆弧，按长度分段、两头并行重叠段保持所接道路高度 ——

    /// <summary>一段圆弧：起点、起点方向、半径、转角（带符号：正 = 转向 Perp 一侧）。</summary>
    internal readonly struct RampArc
    {
        public RampArc(Vec3 from, Vec3 heading, float radius, float angle)
        {
            From = from;
            Heading = heading;
            Radius = radius;
            Angle = angle;
        }

        public Vec3 From { get; }
        public Vec3 Heading { get; }
        public float Radius { get; }
        public float Angle { get; }
        public float Length => Radius * Math.Abs(Angle);
        private float Sign => Math.Sign(Angle);
        public Vec3 Center => From + Perp(Heading) * (Sign * Radius);

        public Vec3 PointAt(float s) => Center + Rot(From - Center, Sign * s / Radius);
        public Vec3 HeadingAt(float s) => Rot(Heading, Sign * s / Radius);
        public Vec3 End => PointAt(Length);
        public Vec3 EndHeading => HeadingAt(Length);
    }

    /// <summary>首尾相接、切线连续的一串圆弧（俯视，高度另给）。</summary>
    internal readonly struct RampPath
    {
        /// <summary>分段时相邻断点的最小间距（米），避免很短的路段。</summary>
        public const float MinSegment = 10f;

        private RampPath(List<RampArc> arcs) { Arcs = arcs; }

        public List<RampArc> Arcs { get; }
        public Vec3 Start => Arcs[0].From;
        public Vec3 End => Arcs[Arcs.Count - 1].End;
        public float Length => Arcs.Sum(a => a.Length);

        /// <summary>从 from 沿 heading 出发，朝 turnToward 一侧转 angle（弧度，正值）。</summary>
        public static RampPath Arcs1(Vec3 from, Vec3 heading, Vec3 turnToward, float radius, float angle)
        {
            float sign = DiamondTemplate.Cross2(heading, turnToward) >= 0f ? 1f : -1f;
            return new RampPath(new List<RampArc> { new RampArc(Flat0(from), Flat(heading), radius, sign * angle) });
        }

        public static RampPath FromArcs(params RampArc[] arcs) => new RampPath(arcs.ToList());

        /// <summary>
        /// 与两条直线都相切的单段圆弧：从直线 a（沿 aDir 行驶）按 sign 方向转到 bDir（转角 0 ~ 360°，可以超过 180°，如 270° 的环道）。
        /// 返回起点在 a 上的位置 aAt、终点在 b 上的位置 bAt。
        /// </summary>
        public static RampPath Turn(Vec3 aOrigin, Vec3 aDir, Vec3 bOrigin, Vec3 bDir, float sign, float radius, out float aAt, out float bAt)
        {
            aDir = Flat(aDir);
            bDir = Flat(bDir);
            var arc = new RampArc(Vec3.Zero, aDir, radius, sign * RotAngle(aDir, bDir, sign));
            Vec3 w = Flat0(bOrigin) - Flat0(aOrigin) - arc.End;
            float cross = DiamondTemplate.Cross2(aDir, bDir);
            aAt = DiamondTemplate.Cross2(w, bDir) / cross;
            bAt = DiamondTemplate.Cross2(w, aDir) / cross;
            Vec3 start = Flat0(aOrigin) + aDir * aAt;
            return new RampPath(new List<RampArc> { new RampArc(start, aDir, radius, arc.Angle) });
        }

        /// <summary>沿路径弧长 s 处的点（俯视）。</summary>
        public Vec3 PointAt(float s)
        {
            foreach (var arc in Arcs)
            {
                if (s <= arc.Length)
                    return arc.PointAt(Math.Max(0f, s));
                s -= arc.Length;
            }
            return End;
        }

        /// <summary>弧长 s0 到 s1 之间的一段。</summary>
        public RampPath Slice(float s0, float s1)
        {
            var arcs = new List<RampArc>();
            float offset = 0f;
            foreach (var arc in Arcs)
            {
                float a = Math.Max(s0, offset), b = Math.Min(s1, offset + arc.Length);
                if (b - a > 1e-4f)
                    arcs.Add(new RampArc(arc.PointAt(a - offset), arc.HeadingAt(a - offset), arc.Radius, Math.Sign(arc.Angle) * (b - a) / arc.Radius));
                offset += arc.Length;
            }
            return new RampPath(arcs);
        }

        /// <summary>反向行驶的同一条路径。</summary>
        public RampPath Reversed() =>
            new RampPath(Enumerable.Reverse(Arcs).Select(a => new RampArc(a.End, -a.EndHeading, a.Radius, -a.Angle)).ToList());

        /// <summary>接着朝 turnToward 一侧再转 angle。</summary>
        public RampPath Then(Vec3 turnToward, float radius, float angle)
        {
            var last = Arcs[Arcs.Count - 1];
            float sign = DiamondTemplate.Cross2(last.EndHeading, turnToward) >= 0f ? 1f : -1f;
            var arcs = new List<RampArc>(Arcs) { new RampArc(last.End, last.EndHeading, radius, sign * angle) };
            return new RampPath(arcs);
        }

        /// <summary>
        /// 与两条直线都相切的复合圆弧：从直线 a（沿 aDir 行驶）出发，转到 b 的方向 bDir 并落在直线 b 上。
        /// 两头可以各有一段较小半径的圆弧（rStart / rEnd 小于 r 时），转到与所接直线横向相距 sepStart / sepEnd（两路不再重叠）为止，
        /// 中间用半径 r：这样与两头道路并行重叠的长度短，大半径部分负责升降。两头小圆弧合计不超过总转角的 90%。
        /// overlapStart / overlapEnd：两头与所接道路并行重叠（横向不足 sep）的长度（沿路径）。
        /// 返回起点在 a 上的位置 aAt、终点在 b 上的位置 bAt。
        /// </summary>
        public static RampPath Fillet(Vec3 aOrigin, Vec3 aDir, Vec3 bOrigin, Vec3 bDir, float rStart, float r, float rEnd,
            float sepStart, float sepEnd, out float aAt, out float bAt, out float overlapStart, out float overlapEnd)
        {
            aDir = Flat(aDir);
            bDir = Flat(bDir);
            float cross = DiamondTemplate.Cross2(aDir, bDir);
            float sign = cross >= 0f ? 1f : -1f;
            float theta = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(aDir, bDir))));
            float Clear(float radius, float sep) => (float)Math.Acos(Math.Max(-1f, 1f - sep / radius));
            float a0 = rStart < r - 0.5f ? Clear(rStart, sepStart) : 0f;
            float a1 = rEnd < r - 0.5f ? Clear(rEnd, sepEnd) : 0f;
            if (a0 + a1 > theta * 0.9f)
            {
                float scale = theta * 0.9f / (a0 + a1);
                a0 *= scale;
                a1 *= scale;
            }
            var arcs = new List<RampArc>();
            Vec3 at = Vec3.Zero, heading = aDir;
            foreach (var (radius, angle) in new[] { (rStart, a0), (r, theta - a0 - a1), (rEnd, a1) })
            {
                if (angle <= 1e-4f)
                    continue;
                var arc = new RampArc(at, heading, radius, sign * angle);
                arcs.Add(arc);
                at = arc.End;
                heading = arc.EndHeading;
            }
            // aOrigin + aDir·aAt + d = bOrigin + bDir·bAt
            Vec3 w = Flat0(bOrigin) - Flat0(aOrigin) - at;
            aAt = DiamondTemplate.Cross2(w, bDir) / cross;
            bAt = DiamondTemplate.Cross2(w, aDir) / cross;
            Vec3 start = Flat0(aOrigin) + aDir * aAt;

            // 并行重叠长度：小圆弧内横向偏移到 sep 为止；没到（被截短）时剩下的按小圆弧末端的方向角估算
            float Overlap(float radius, float angle, float sep, bool tight)
            {
                if (!tight)
                    return MathF.Sqrt(2f * r * sep);
                float offset = radius * (1f - MathF.Cos(angle));
                float rest = sep - offset;
                return radius * angle + (rest > 0.01f ? rest / Math.Max(MathF.Sin(angle), 0.05f) : 0f);
            }
            overlapStart = Overlap(rStart, a0, sepStart, a0 > 0f);
            overlapEnd = Overlap(rEnd, a1, sepEnd, a1 > 0f);
            return new RampPath(arcs.Select(a => new RampArc(a.From + start, a.Heading, a.Radius, a.Angle)).ToList());
        }

        /// <summary>
        /// 输出成路段：每段不超过约 90°，在圆弧交接处和水平段边界处分段。高度：起点 flatStart 米内与 start 同高，
        /// 终点 flatEnd 米内与 end 同高，中间线性升降。start / end 原样用作两端点（游戏按坐标完全相等合并节点）。
        /// </summary>
        /// <param name="startNode">起点所在的节点（与曲线端点不同时 = 边缘对齐接入）；null = 曲线端点本身。</param>
        public void Emit(List<RoadSegmentSpec> segments, string name, Vec3 start, Vec3 end, float flatStart, float flatEnd, string? prefab,
            Vec3? startNode = null, Vec3? endNode = null)
        {
            float total = Length;
            float y0 = start.Y, y1 = end.Y;
            float rampFrom = Math.Min(flatStart, total), rampTo = Math.Max(rampFrom, total - flatEnd);
            float Height(float s) => s <= rampFrom ? y0 : s >= rampTo ? y1 : y0 + (y1 - y0) * (s - rampFrom) / (rampTo - rampFrom);

            // 断点（沿全长的距离）
            // 必须的断点：两端和升降段的起止；圆弧分段（每段不超过约 90°）的断点离它们或彼此不到 MinSegment 时省掉，
            // 避免很短的路段。与所接道路并行重叠的两头之内也不分段：那里的节点会让一段独立的路和所接道路重叠
            var required = new List<float> { 0f, total };
            // 与所接道路并行重叠的两头总是单独成段（高架时这段要短于游戏切分长度，节点区也不会吞掉整段）
            if (Math.Abs(y1 - y0) > 0.05f || flatStart > 0f || flatEnd > 0f)
            {
                if (flatStart > 0f || Math.Abs(y1 - y0) > 0.05f)
                    required.Add(rampFrom);
                if (flatEnd > 0f || Math.Abs(y1 - y0) > 0.05f)
                    required.Add(rampTo);
            }
            var optional = new List<float>();
            float offset = 0f;
            foreach (var arc in Arcs)
            {
                int pieces = (int)Math.Ceiling(Math.Abs(arc.Angle) / (MathF.PI / 2f + 0.05f));
                for (int k = 0; k <= pieces; k++)
                    optional.Add(offset + arc.Length * k / pieces);
                offset += arc.Length;
            }
            var sorted = required.Where(c => c >= 0f && c <= total).Distinct().OrderBy(c => c).ToList();
            for (int i = sorted.Count - 1; i > 0; i--)
                if (sorted[i] - sorted[i - 1] < 1f)
                    sorted.RemoveAt(i == sorted.Count - 1 ? i - 1 : i);
            foreach (float c in optional)
            {
                bool insideOverlap = (c > 0.5f && c < flatStart - 0.5f) || (c < total - 0.5f && c > total - flatEnd + 0.5f);
                if (!insideOverlap && sorted.All(k => Math.Abs(k - c) >= MinSegment))
                {
                    sorted.Add(c);
                    sorted.Sort();
                }
            }
            sorted[sorted.Count - 1] = total;

            // 每个断点的位置和方向只算一次，相邻两段共用（游戏按坐标完全相等合并节点）
            var points = new List<Vec3>();
            var headings = new List<Vec3>();
            var arcOf = new List<int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                float arcStart = 0f;
                int which = Arcs.Count - 1;
                for (int j = 0; j < Arcs.Count; j++)
                {
                    if (sorted[i] < arcStart + Arcs[j].Length - 0.5f)
                    {
                        which = j;
                        break;
                    }
                    arcStart += Arcs[j].Length;
                }
                if (which == Arcs.Count - 1)
                    arcStart = Length - Arcs[which].Length;
                float local = Math.Min(Math.Max(sorted[i] - arcStart, 0f), Arcs[which].Length);
                Vec3 point = Arcs[which].PointAt(local);
                point.Y = Height(sorted[i]);
                points.Add(i == 0 ? start : i == sorted.Count - 1 ? end : point);
                headings.Add(Arcs[which].HeadingAt(local));
                arcOf.Add(which);
            }

            int index = 0;
            for (int i = 0; i + 1 < sorted.Count; i++)
            {
                // 这一段所在的圆弧：起点所在的那段（断点落在圆弧交接处时取后一段）
                // 一段可能跨两段圆弧（重叠区内不分段），手柄长按弦长和两端方向夹角算：圆弧时等于 4/3·tan(Δ/4)·r
                Vec3 a = points[i], b = points[i + 1];
                float delta = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(headings[i], headings[i + 1]))));
                float chord = Vec3.Distance(Flat0(a), Flat0(b));
                float cosQuarter = MathF.Cos(delta / 4f);
                float k = chord / (3f * cosQuarter * cosQuarter);
                Vec3 p1 = a + headings[i] * k, p2 = b - headings[i + 1] * k;
                p1.Y = a.Y + (b.Y - a.Y) / 3f;
                p2.Y = a.Y + (b.Y - a.Y) * 2f / 3f;
                segments.Add(new RoadSegmentSpec($"{name}{++index}", RoadRole.Ramp, new Bezier3(a, p1, p2, b), prefab,
                    i == 0 ? startNode : null, i + 2 == sorted.Count ? endNode : null));
            }
        }
    }
}
