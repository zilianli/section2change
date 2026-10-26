using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 部分苜蓿叶 ParClo（快速路 × 普通道路，被交道路上平交路口）。主线（臂 0/2，双幅）与被交道路（臂 1/3，双向）一上一下，
    /// 主线每一幅在同一侧各有一条环道和一条直连匝道，两条在路口前合成一条双向匝道（主干）接到被交道路上的平交路口：
    ///
    ///      A2（环道驶入主线，匝道都在被交道路之前接主线）       B2（环道驶出主线，匝道都在被交道路之后接主线）
    ///               ║ ║                                              ║ ║   ╭──╮
    ///   ════════╤═╤═╬═╬══════  被交道路                              ║ ║   │  │ 环道
    ///           │ │ ║ ║╭──╮                                ════════╬═╬═╤═╤══════
    ///           │ ╰─╫─╫╯  │ 环道（内侧）                              ║ ║ │ ╰─ 直连匝道（外侧）
    ///    直连匝道 ╰───╫─╫───╯                                          ...
    ///
    /// A2 是本模板直接生成的布置（以主线某一幅为例，右侧通行）：双向主干从被交道路上的路口背离被交道路直行到分岔点（环道顶部），
    /// 内侧车道是环道，右转 180° 在被交道路之前外切汇入主线；外侧车道是直连匝道，它更早从主线驶出，S 形横移到环道顶部外侧直行到分岔点。
    /// 主线上先驶出、后汇入，没有交织。另一幅的布置是它绕立交中心转 180°。
    /// B2 = A2 沿被交道路镜像并把所有路段反向（与喇叭形“环道用于驶入”同样的做法）。
    /// 坐标单位米，立交中心为原点。
    /// </summary>
    public sealed class PartialCloverleafTemplate : IInterchangeTemplate
    {
        /// <summary>两条道路的夹角范围（相对正交 ±30°，即 60°–120°）。</summary>
        private const float kMaxSkew = 30f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        private const float kPillarMargin = 3f;
        /// <summary>匝道接高架道路那头的圆弧半径上限（米）：并行重叠段要短于游戏切分高架路段的长度。</summary>
        private const float kDivergeRadius = 80f;
        /// <summary>直连匝道 S 形横移的圆弧半径（米，地面时）。</summary>
        private const float kSRadius = 150f;
        /// <summary>匝道与所接道路并行重叠的那段再多保持同高这么远（米）才开始升降。</summary>
        private const float kFlatMargin = 15f;
        /// <summary>平交路口前匝道保持路口高度的长度（米）。</summary>
        private const float kTerminalFlat = 15f;
        private const float kMinLoopRadius = 30f;
        private const float kMaxLoopRadius = 250f;
        /// <summary>直线段用超大半径的圆弧表示（RampPath 只有圆弧）。</summary>
        private const float kStraight = 100000f;

        public InterchangeType Type => InterchangeType.PartialCloverleaf;

        public string DisplayName => "部分苜蓿叶";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance)
                errors.Add("部分苜蓿叶要求两条道路都是直线（臂 0/2、臂 1/3 各自相对）");
            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (Math.Abs(crossing - 90f) > kMaxSkew)
                errors.Add($"两条道路夹角 {crossing:F0}°：部分苜蓿叶需要 {90f - kMaxSkew:F0}°–{90f + kMaxSkew:F0}°");
            if (legs[0].Form != RoadForm.Dual)
                errors.Add("部分苜蓿叶的主线需要是双幅（两条对向单行道路）：请选择单行的公路或把主线形式改为双幅");
            if (legs[1].Form != RoadForm.TwoWay)
                errors.Add("部分苜蓿叶的被交道路需要是双向道路（匝道在它上面接平交路口）");

            float hm = p.ElevationOf(legs[0]), hc = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            float upper = Math.Max(hm, hc);
            if (Math.Abs(hm - hc) < gap)
                errors.Add($"两条道路高差 {Math.Abs(hm - hc):F1} 米，至少需要 {gap:F1} 米净空");
            else if (LevelPlanner.BandOf(upper) != ElevationBand.Elevated)
                errors.Add($"上层道路离地 {upper:F1} 米会建成路堤（挡土墙），下层道路无法从下面穿过：需要至少 {HeightRange.BridgeThreshold:F1} 米");
            if (string.IsNullOrEmpty(p.SharedRampPrefab) || p.SharedRampWidth <= 0f)
                errors.Add("部分苜蓿叶需要一条与匝道车道数相同的双向道路作匝道主干");
            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;
            if (p.LoopRadius > 0f && p.LoopRadius < kMinLoopRadius)
                return new[] { $"环道半径至少 {kMinLoopRadius:F0} 米" };

            bool auto = p.LoopRadius <= 0f;
            p = WithLoopRadius(p);
            var plan = PlanFor(p);
            errors.AddRange(plan.Errors);
            if (!plan.Fits)
            {
                float? fits = auto ? null : RadiusSearch.Smallest(r =>
                {
                    var trial = p.Clone();
                    trial.LoopRadius = r;
                    return new Plan(trial).Fits;
                }, p.LoopRadius + 5f, kMaxLoopRadius);
                errors.Add(fits is float radius
                    ? $"环道半径至少 {radius:F0} 米（环道要在主线旁边留出匝道的位置）"
                    : "这个交角下放不下：请把交角调得更接近 90°");
            }
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            p = WithLoopRadius(p);
            string key = p.Fingerprint();
            if (key != s_segmentsKey)
            {
                var plan = PlanFor(p);
                var built = new List<RoadSegmentSpec>();
                plan.Build(built);
                if (p.ParcloLoopsForExit)
                {
                    // B2：沿被交道路镜像，再把每条路段反向（行驶方向随之反过来，右侧通行不变）
                    Vec3 c = plan.CrossAxis;
                    Vec3 Mirror(Vec3 v)
                    {
                        float along = v.X * c.X + v.Z * c.Z;
                        return new Vec3(2f * along * c.X - v.X, v.Y, 2f * along * c.Z - v.Z);
                    }
                    for (int i = 0; i < built.Count; i++)
                        built[i] = built[i].Transform(Mirror).Reversed();
                }
                // 外部被交道路是双幅时，两端从双向分回双幅
                InterchangeLayout.AddDualTails(built, p);
                InterchangeLayout.AddApproachSlopes(built, p);
                s_segments = built;
                s_segmentsKey = key;
            }
            return new List<RoadSegmentSpec>(s_segments!);
        }

        /// <summary>环道半径：手动（LoopRadius &gt; 0）时就是它；自动（0）时取能放下的最小半径（5 米一档）。</summary>
        public static float ResolveLoopRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p.LoopRadius;
            string key = p.Fingerprint();
            if (key == s_radiusKey)
                return s_radius;
            var trial = p.Clone();
            s_radius = RadiusSearch.Smallest(r =>
            {
                trial.LoopRadius = r;
                return new Plan(trial).Fits;
            }, kMinLoopRadius, kMaxLoopRadius) ?? kMaxLoopRadius;
            s_radiusKey = key;
            return s_radius;
        }

        private static InterchangeParams WithLoopRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p;
            var resolved = p.Clone();
            resolved.LoopRadius = ResolveLoopRadius(p);
            return resolved;
        }

        // —— 缓存（参数不变时立交的局部形状不变，鼠标移动不重复计算）——
        private static string? s_planKey, s_radiusKey, s_segmentsKey;
        private static Plan? s_plan;
        private static float s_radius;
        private static List<RoadSegmentSpec>? s_segments;

        private static Plan PlanFor(InterchangeParams p)
        {
            string key = p.Fingerprint();
            if (key != s_planKey || s_plan == null)
            {
                s_plan = new Plan(p);
                s_planKey = key;
            }
            return s_plan;
        }

        /// <summary>直线段（RampPath 只有圆弧，用超大半径的圆弧代替）。</summary>
        private static RampArc Straight(Vec3 from, Vec3 heading, float length) => new RampArc(Flat0(from), heading, kStraight, length / kStraight);

        /// <summary>
        /// 按参数算出的一侧布置（主线沿 V 行驶的那一幅、被交道路上 L 那一侧的两个路口），另一侧是它绕中心转 180°。
        /// 坐标：主线那一幅上的点 = V·t + L·lateral；被交道路中心线上的点 = C·s。
        /// </summary>
        private sealed class Plan
        {
            private readonly InterchangeParams P;
            public Vec3 CrossAxis { get; }
            private readonly Vec3 V, L, C;
            private readonly float hm, hc, om, grade, sepMain;
            private readonly PillarPlanner.Span mainSpan, crossSpan;
            public readonly List<string> Errors = new List<string>();
            public readonly bool Fits = true;

            private readonly bool mainElevated;
            // 主干（双向匝道）：被交道路上 sStem 处的平交路口 → 分岔点 splitPoint（高度 splitHeight）
            private readonly float sStem, splitHeight, stemFlat;
            private readonly Vec3 splitPoint;
            private readonly RampPath stem;
            // 环道：从分岔点右转 180°，在 tMerge 汇入主线
            private readonly float tMerge, loopFlatEnd;
            private readonly RampPath loop;
            // 直连匝道：在 tDiverge 驶出主线，S 形横移到环道顶部外侧，直行到分岔点
            private readonly float tDiverge, rampFlatStart;
            private readonly RampPath ramp;

            public Plan(InterchangeParams p)
            {
                P = p;
                var legs = p.Legs;
                float hand = p.RightHandTraffic ? 1f : -1f;
                grade = p.MaxGrade * kGradeMargin;
                Vec3 m = Flat(legs[0].Direction), c = Flat(legs[1].Direction);
                CrossAxis = c;
                // B2 时整个布置最后沿被交道路镜像：这里先用镜像后的主线方向，镜像回去正好是真实方向
                if (p.ParcloLoopsForExit)
                {
                    float along = Directions.Dot(m, c);
                    m = Vec3.Normalize(new Vec3(2f * along * c.X - m.X, 0f, 2f * along * c.Z - m.Z));
                }
                V = m;
                L = Directions.RightOf(V) * hand;
                C = Directions.Dot(c, L) > 0f ? c : -c;
                hm = p.ElevationOf(legs[0]);
                hc = p.ElevationOf(legs[1]);
                om = legs[0].CarriagewayOffset;
                float rw = p.RampWidth, gap = p.RampSideGap;
                sepMain = legs[0].RoadWidth / 2f + rw / 2f + gap;
                float clearance = rw + gap + 2f * kPillarMargin;
                float cosCL = Directions.Dot(C, L), cosCV = Directions.Dot(C, V);
                float sin = Math.Abs(DiamondTemplate.Cross2(V, C));

                // 主线这一幅跨越被交道路的范围（沿 t）、被交道路跨越主线的范围（沿 s）：在上层且是高架时按立柱规划
                {
                    Vec3 origin = L * om;
                    float center = -DiamondTemplate.Cross2(origin, C) / DiamondTemplate.Cross2(V, C);
                    float half = legs[1].RoadWidth / 2f / sin;
                    mainSpan = hm > hc && LevelPlanner.BandOf(hm) == ElevationBand.Elevated
                        ? PillarPlanner.Plan(center, half, 0f, legs[0].MiddlePillarSpacing, legs[0].PillarWidth, legs[0].MaxElevatedEdgeLength)
                        : new PillarPlanner.Span(center - half - kPillarMargin, center + half + kPillarMargin);
                    float crossHalf = legs[0].HalfExtent / sin;
                    crossSpan = hc > hm && LevelPlanner.BandOf(hc) == ElevationBand.Elevated
                        ? PillarPlanner.Plan(0f, crossHalf, legs[0].MedianWidth / 2f / sin, legs[1].MiddlePillarSpacing, legs[1].PillarWidth, legs[1].MaxElevatedEdgeLength)
                        : new PillarPlanner.Span(-crossHalf - kPillarMargin, crossHalf + kPillarMargin);
                    foreach (var span in new[] { mainSpan, crossSpan })
                        if (span.Error != null && !Errors.Contains(span.Error))
                            Errors.Add(span.Error);
                }

                float R = p.LoopRadius;
                float rise = Math.Abs(hm - hc);
                mainElevated = hm > hc && LevelPlanner.BandOf(hm) == ElevationBand.Elevated;

                // 同一侧只有一条双向匝道（主干）接被交道路上的平交路口：主干从路口背离被交道路（沿 −V）直行到分岔点，
                // 分成环道（内侧车道，右转 180° 外切汇入主线）和直连匝道（外侧车道，沿主线方向直行，S 形横移接主线）。
                // 分岔点在环道顶部：环道中心线横向 om + 2R，主干中心线外移 w/4，直连匝道中心线外移 w/2（与环道外侧相切分开）
                float w = p.SharedRampWidth;
                float topLateral = om + 2f * R;
                sStem = (topLateral + w / 4f) / cosCL;
                float sMin = crossSpan.Hi + RampPath.MinSegment;
                if (w <= 0f || sStem < sMin)
                    Fits = false;
                loopFlatEnd = R * (float)Math.Acos(Math.Max(-1f, 1f - sepMain / R)) + kFlatMargin;
                float loopLength = MathF.PI * R;
                if (loopFlatEnd > loopLength * 0.8f || (mainElevated && loopFlatEnd > legs[0].MaxElevatedEdgeLength - 5f))
                    Fits = false;

                // 高度：环道分支在自己的长度内尽量升降，剩下的在主干上完成（主干直线段随之加长）
                float climb = Math.Min(rise, grade * (loopLength - loopFlatEnd));
                splitHeight = rise <= 0f ? hm : hm + (hc - hm) * climb / rise;
                // 主干从被交道路中心线出发：先穿过被交道路半幅（斜交时更长），出了路面再保持路口高度 kTerminalFlat；
                // 分岔点的节点区（约主干宽一半）与路口节点区之间至少再隔开一段
                stemFlat = legs[1].RoadWidth / 2f / sin + kTerminalFlat;
                float straight = Math.Max(stemFlat + w / 2f + RampPath.MinSegment, stemFlat + (rise - climb) / grade);
                // 分岔点（环道起点）与汇入点沿主线位置相同：汇入点要在主线跨越被交道路的范围之前
                float tStem = sStem * cosCV;
                straight = Math.Max(straight, tStem - (mainSpan.Lo - RampPath.MinSegment));
                float tSplit = tStem - straight;
                tMerge = tSplit;
                Vec3 loopStart = V * tSplit + L * topLateral;
                loop = RampPath.Arcs1(loopStart, -V, -L, R, MathF.PI);
                Vec3 stemStart = V * tStem + L * (topLateral + w / 4f);
                stem = RampPath.FromArcs(Straight(stemStart, -V, straight));
                splitPoint = V * tSplit + L * (topLateral + w / 4f);

                // —— 直连匝道：在 tDiverge 驶出主线，S 形横移到环道顶部外侧，沿主线方向直行到分岔点 ——
                float r1 = mainElevated ? kDivergeRadius : kSRadius, r2 = kSRadius;
                float shift = topLateral + w / 2f - om;
                float theta = (float)Math.Acos(Math.Max(-1f, 1f - shift / (r1 + r2)));
                float sAlong = (r1 + r2) * MathF.Sin(theta), sLength = (r1 + r2) * theta;
                rampFlatStart = r1 * (float)Math.Acos(Math.Max(-1f, 1f - sepMain / r1)) + kFlatMargin;
                if (rampFlatStart > r1 * theta || (mainElevated && rampFlatStart > legs[0].MaxElevatedEdgeLength - 5f))
                    Fits = false;
                // S 形结束点在环道最低处（离被交道路最远处）之外，坡长要够
                float run = Math.Max(R + clearance, climb / grade + rampFlatStart - sLength);
                float tEnd = tSplit - run;
                tDiverge = tEnd - sAlong;
                // 主线上先驶出、后汇入，中间至少隔开一段
                if (tDiverge + rampFlatStart + RampPath.MinSegment > tMerge - loopFlatEnd)
                    Fits = false;
                Vec3 divergePoint = V * tDiverge + L * om;
                var a1 = RampPath.Arcs1(divergePoint, V, L, r1, theta).Arcs[0];
                var a2 = new RampArc(a1.End, a1.EndHeading, r2, -Math.Sign(a1.Angle) * theta);
                ramp = RampPath.FromArcs(a1, a2, Straight(a2.End, V, run));
            }

            private static Vec3 At(Vec3 v, float y) => new Vec3(v.X, y, v.Z);

            private Vec3 MainPoint(float t, float h) => V * t + L * om + new Vec3(0f, h, 0f);

            private Vec3 CrossPoint(float s) => C * s + new Vec3(0f, hc, 0f);

            public void Build(List<RoadSegmentSpec> segments)
            {
                if (!Fits)
                    return;
                var p = P;
                var legs = p.Legs;

                // —— 主线：这一幅（沿 V），另一幅是它绕中心转 180° ——
                float mainEnd = Math.Max(Math.Abs(tDiverge), Math.Abs(tMerge)) + legs[0].StubLength;
                var mainNodes = new List<float> { -mainEnd, tDiverge, tMerge, mainSpan.Lo, mainSpan.Hi, mainEnd };
                if (mainElevated)
                {
                    // 高架主线的路段超过游戏切分长度会被从中间切开，切点的立柱可能顶到旁边匝道的桥面：
                    // 自己按长度加节点，并且不放在匝道与主线并行重叠的那段里
                    float allowed = legs[0].MaxElevatedEdgeLength - 10f;
                    var sorted = mainNodes.OrderBy(t => t).ToList();
                    for (int i = 0; i + 1 < sorted.Count; i++)
                    {
                        float lo = sorted[i], hi = sorted[i + 1];
                        if (hi - lo <= allowed)
                            continue;
                        float freeLo = lo == tDiverge ? lo + rampFlatStart : lo, freeHi = hi == tMerge ? hi - loopFlatEnd : hi;
                        int n = (int)Math.Ceiling((hi - lo) / allowed);
                        for (int k = 1; k < n; k++)
                            mainNodes.Add(Math.Max(freeLo, Math.Min(freeHi, lo + (hi - lo) * k / n)));
                    }
                }
                var mainLine = new List<RoadSegmentSpec>();
                AddLine(mainLine, "Main", RoadRole.MainRoad, legs[0].RoadPrefab, mainNodes, t => MainPoint(t, hm), new[] { tDiverge, tMerge, -mainEnd, mainEnd });

                // —— 两侧匝道（这一侧，另一侧转 180°）——
                var side = new List<RoadSegmentSpec>();
                side.AddRange(mainLine);
                Vec3 split = At(splitPoint, splitHeight);
                stem.Emit(side, "Stem", CrossPoint(sStem), split, stemFlat, 0f, p.SharedRampPrefab);
                loop.Emit(side, "Loop", At(loop.Start, splitHeight), MainPoint(tMerge, hm), 0f, loopFlatEnd, p.RampPrefab, startNode: split);
                ramp.Emit(side, "Ramp", MainPoint(tDiverge, hm), At(ramp.End, splitHeight), rampFlatStart, 0f, p.RampPrefab, endNode: split);
                segments.AddRange(side);
                foreach (var s in side)
                {
                    var rotated = s.Transform(v => new Vec3(-v.X, v.Y, -v.Z));
                    segments.Add(new RoadSegmentSpec(s.Name.Replace("Main", "MainB").Replace("Ramp", "RampB").Replace("Loop", "LoopB").Replace("Stem", "StemB"),
                        rotated.Role, rotated.Curve, rotated.Prefab, rotated.StartNode, rotated.EndNode));
                }

                // —— 被交道路（双向）：两侧各一个平交路口 ——
                float crossEnd = sStem + legs[1].StubLength;
                var crossNodes = new List<float> { -crossEnd, -sStem, crossSpan.Lo, crossSpan.Hi, sStem, crossEnd };
                AddLine(segments, "Cross", RoadRole.CrossRoad, legs[1].RoadPrefab, crossNodes, CrossPoint,
                    new[] { -crossEnd, -sStem, sStem, crossEnd });

                // 外部是双向道路的主线：两端过渡
                float hand = p.RightHandTraffic ? 1f : -1f;
                foreach (float end in new[] { 1f, -1f })
                {
                    LegSpec leg = end > 0f ? legs[0] : legs[2];
                    if (!leg.NeedsDualToTwoWayTransition)
                        continue;
                    Vec3 axis = V * end;
                    Vec3 lateral = Directions.RightOf(axis) * hand;
                    Vec3 Point(float along, float l) => axis * along + lateral * l + new Vec3(0f, hm, 0f);
                    DiamondTemplate.AddTransition(segments, end > 0f ? "Main-endA" : "Main-endB", RoadRole.MainRoad, Point, +1, mainEnd, om,
                        mainEnd + leg.StubLength, p.LegTransitionAngle, leg.RoadPrefab, leg.ExternalPrefab);
                }
            }

            /// <summary>沿一条直线按位置把节点连成路段（相距不到 10 米的合并，优先保留 keep 里的节点）；点 at(位置) 给出坐标。</summary>
            private static void AddLine(List<RoadSegmentSpec> segments, string name, RoadRole role, string? prefab, List<float> nodes,
                Func<float, Vec3> at, IReadOnlyList<float> keep)
            {
                var sorted = new List<float>();
                foreach (float t in nodes.OrderBy(t => t))
                {
                    int near = sorted.FindIndex(k => Math.Abs(k - t) < RampPath.MinSegment);
                    if (near < 0)
                        sorted.Add(t);
                    else if (keep.Contains(t))
                        sorted[near] = t;
                }
                sorted.Sort();
                for (int i = 0; i + 1 < sorted.Count; i++)
                    segments.Add(new RoadSegmentSpec($"{name}-{i + 1}", role, Bezier3.Line(at(sorted[i]), at(sorted[i + 1])), prefab));
            }
        }
    }
}
