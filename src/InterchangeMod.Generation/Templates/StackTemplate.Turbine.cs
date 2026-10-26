using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 涡轮立交（Turbine，三层，右侧通行）：主线在第 1 层、被交道路在第 2 层。四条左转匝道各走一个半圆，四个圆半径相同：
    /// 左转匝道从来向那幅右侧驶出，向右转一个切入角切进自己的圆，沿圆左转半圈，再向右转出、切到去向那幅，从右侧汇入
    /// （两个切入角之和 = 180° − 转角，正交时各 45°）。每个圆心从立交中心往切入方向的左侧挪开同样的距离 d：进圆处离中心 R − d、
    /// 出圆处离中心 R + d，四个半圆像风车叶片一样一个压着一个（正交时互为 90° 旋转，每条在下一条外侧、相距 d 到 1.4 d），
    /// 同一对的两条在一条进圆、另一条出圆处并排、相距 2d。右转匝道是四角的大弧，比左转匝道早驶出、晚汇入，整条绕在外面。左转匝道不固定在某一层：沿途每处与道路、别的匝道交叉时从上面或下面错开
    /// （被交道路是高架时可以从它下面穿过），按最大坡度连成一条纵断面，最高不超过第 3 层。
    /// </summary>
    public sealed partial class StackTemplate
    {
        /// <summary>左转匝道进圆处离立交中心的距离（R − d）的搜索范围（米，从两条路外缘再往外算起）和步长。</summary>
        private const float kRingMinClearance = 20f, kRingMaxClearance = 300f, kRingStep = 10f;
        /// <summary>左转匝道接高架道路那头先用的小弧半径（米）：并行重叠段短于游戏切分长度。</summary>
        private const float kRingElevatedShift = 80f;
        /// <summary>圆心挪开的距离在最小间距之外再加的候选（米）。</summary>
        private static readonly float[] kRingSpacings = { 0f, 15f, 30f, 45f, 60f, 80f };
        /// <summary>纵断面求解的采样间距（米），和化简时允许的高度误差（米）。</summary>
        private const float kProfileStep = 2f, kProfileTolerance = 0.2f;
        /// <summary>纵断面在 0.9 个层高净空之外再留的余量（米）。</summary>
        private const float kProfileMargin = 0.1f;
        /// <summary>转折点落在挡立柱的位置上时，沿匝道最多挪开的距离（米）。</summary>
        private const float kMaxKnotShift = 30f;
        /// <summary>一条匝道最多考虑的交叉处数（每处上 / 下两种选法，组合数随它指数增长）。</summary>
        private const int kMaxCrossings = 8;
        /// <summary>涡轮搜索的时间上限（毫秒）：超过就按放不下处理，免得预览卡住。</summary>
        private const long kTurbineBudgetMs = 1500;
        /// <summary>涡轮右转匝道接高架道路时并行重叠段的最大长度（米）。</summary>
        private const float kMaxRightOverlap = 55f;

        private sealed partial class Plan
        {
            /// <summary>
            /// 涡轮：先定左转匝道（四个等半径的半圆），再定右转匝道（绕在外面的最小半径大弧）。按半圆最远处离中心的距离（R + d）
            /// 从小到大、一样大时圆心挪得少的先试（坡度越大纵断面越容易放下，圆就越小）；每个候选先求转角小的一对、再换另一对先求，
            /// 取第一个各处都放得下的。
            /// </summary>
            private void SolveTurbine()
            {
                float TurnOf(bool main)
                {
                    var x = lines.First(l => l.Main == main);
                    return (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(x.Dir, LeftTarget(x).Dir))));
                }
                bool preferMain = TurnOf(true) <= TurnOf(false) + 1e-3f;
                float outside = lines.Max(l => Math.Abs(Directions.Dot(l.Origin, l.Right)) + l.Width / 2f);
                // 圆心挪开的距离：相邻两个半圆并排处相距至少这么多——两条匝道各半宽、间距、立柱余量；
                // 再按候选拉开（交叉处角度更陡，立柱放得下）
                float spacing = rw + gap + kPillarMargin;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string failure = "左转匝道放不下：请加大最大坡度、减小层间高差或把交角调得更接近 90°";
                // 进圆处（离中心 R − d）至少在两条路外缘再往外 kRingMinClearance
                float innerMin = outside + kRingMinClearance + rw / 2f;
                var candidates = new List<(float Radius, float Shift)>();
                foreach (float extraSpacing in kRingSpacings)
                    for (float clearance = 0f; clearance <= kRingMaxClearance - kRingMinClearance; clearance += kRingStep)
                        candidates.Add((innerMin + clearance + spacing + extraSpacing, spacing + extraSpacing));
                foreach (var (radius, shift) in candidates.OrderBy(c => c.Radius + c.Shift).ThenBy(c => c.Shift))
                    foreach (bool mainInner in new[] { preferMain, !preferMain })
                    {
                        if (watch.ElapsedMilliseconds > kTurbineBudgetMs)
                        {
                            Fail("time budget");
                            goto failed;
                        }
                        lefts.Clear();
                        rights.Clear();
                        if (TryRingPair(mainInner, radius, shift) is not List<Ramp> first)
                            continue;
                        lefts.AddRange(first);
                        if (TryRingPair(!mainInner, radius, shift) is not List<Ramp> second)
                            continue;
                        lefts.AddRange(second);
                        // 左转匝道的断点先单独排一次：排不开时加上右转匝道也排不开，省得白求右转匝道
                        if (!lefts.All(PlanCuts))
                        {
                            Fail("pillars");
                            failure = "跨线桥的立柱无法避开下面的道路：请把交角调得更接近 90° 或改变层间高差";
                            continue;
                        }
                        if (!SolveOuterRights())
                        {
                            Fail("rights");
                            failure = "右转匝道放不下：请加大最大坡度或把交角调得更接近 90°";
                            continue;
                        }
                        bool cut = lefts.Concat(rights).All(PlanCuts);
                        if (!cut || !lines.All(l => LineCuts(l, allowInOverlap: false) != null))
                        {
                            Fail(cut ? "line pillars" : "pillars");
                            failure = "跨线桥的立柱无法避开下面的道路：请把交角调得更接近 90° 或改变层间高差";
                            continue;
                        }
                        return;
                    }
            failed:
                Trace?.Invoke("turbine: no ring radius / turn fits: " + string.Join(", ", m_Fails.Select(kv => $"{kv.Key} {kv.Value}")));
                lefts.Clear();
                rights.Clear();
                Errors.Add(failure);
            }

            /// <summary>调试：涡轮求解时各候选卡在哪一步的计数（失败时写进调试日志）。</summary>
            private readonly Dictionary<string, int> m_Fails = new Dictionary<string, int>();

            private void Fail(string reason) => m_Fails[reason] = m_Fails.TryGetValue(reason, out int n) ? n + 1 : 1;

            private T? Fail<T>(string reason) where T : class
            {
                Fail(reason);
                return null;
            }

            /// <summary>
            /// 一对左转匝道（从同一条路出发的两条，在对角象限）各走半径 radius 的半圆，圆心往切入方向的左侧挪 shift：
            /// 纵断面放得下、与已有的匝道间距够、与同一幅上别的匝道接点不冲突、跨越段下面放得下立柱；不行时返回 null。
            /// </summary>
            private List<Ramp>? TryRingPair(bool fromMain, float radius, float shift)
            {
                var pair = new List<Ramp>();
                foreach (var x in lines.Where(l => l.Main == fromMain))
                {
                    // 要跨过（或从下面穿过）的：两条路、已经求出的左转匝道（含同一对里先求出的那条）
                    var obstacles = lines.Select(l => (Obstacle)new LineObstacle(l.Origin, l.Dir, l.Width, l.Height))
                        .Concat(lefts.Concat(pair).Select(q => (Obstacle)q.AsObstacle(rw))).ToList();
                    var ramp = BuildRingLeft(x, LeftTarget(x), radius, shift, obstacles);
                    string stage = lefts.Count == 0 ? "first" : "second";
                    if (ramp == null)
                        return null;
                    if (!lefts.Concat(pair).All(q => Clear(ramp, q)))
                        return Fail<List<Ramp>>(stage + " clearance");
                    if (MaxBlocked(ramp, obstacles) > kMaxBlocked)
                        return Fail<List<Ramp>>(stage + " blocked");
                    pair.Add(ramp);
                    var all = lefts.Concat(pair).ToList();
                    if (!AttachmentsApart(all) || !AttachNodesClear(all))
                        return Fail<List<Ramp>>(stage + " attach");
                }
                return pair;
            }

            /// <summary>
            /// 涡轮左转匝道 X → T：从 X 上的接入位置向右转切入角 beta = (180° − 转角) / 2（大半径），在 P_in 与圆相切；沿圆左转半圈到
            /// P_out；再向右转 beta 落在 T 上的接入位置。圆心从立交中心往切入方向的左侧挪 shift：P_in 离中心 radius − shift，
            /// P_out 离中心 radius + shift（都在过中心、垂直于切入方向的那条线上）。
            /// 两段切入 / 切出弧的半径由“起点 / 终点横向落在接入位置上”算出；接高架道路那头先用小弧离开道路。高度按途中各处交叉求纵断面。
            /// </summary>
            private Ramp? BuildRingLeft(Carriageway x, Carriageway t, float radius, float shift, IReadOnlyList<Obstacle> obstacles)
            {
                float turn = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(x.Dir, t.Dir))));
                float rightSign = DiamondTemplate.Cross2(x.Dir, x.Right) >= 0f ? 1f : -1f;
                float beta = ((float)Math.PI - turn) / 2f;
                // 切进圆：方向是 X 右转 beta，圆上那一点在这个方向的右侧（圆心在左侧 shift 处）
                Vec3 hIn = Rotate(x.Dir, rightSign * beta);
                Vec3 pIn = Directions.RightOf(hIn) * (radius - shift);
                var entry = Bend(x.Dir, rightSign, beta, Directions.Dot(pIn - x.Origin, x.Right) - LeftAttach(x), Elevated(x.Height) > 0f, Sep(x));
                if (entry == null)
                    return Fail<Ramp>("entry bend");
                Vec3 start = Flat0(pIn) - entry[entry.Count - 1].End;
                var arcs = entry.Select(a => new RampArc(a.From + start, a.Heading, a.Radius, a.Angle)).ToList();
                var ring = new RampArc(arcs[arcs.Count - 1].End, arcs[arcs.Count - 1].EndHeading, radius, -rightSign * (turn + 2f * beta));
                arcs.Add(ring);
                // 切出环：倒过来看是从 T 上的接入位置“逆行”右转 beta 切到环上
                var exit = Bend(t.Dir, rightSign, beta, Directions.Dot(Flat0(ring.End) - t.Origin, t.Right) - LeftAttach(t), Elevated(t.Height) > 0f, Sep(t), reverse: true);
                if (exit == null)
                    return Fail<Ramp>("exit bend");
                foreach (var a in exit)
                {
                    var prev = arcs[arcs.Count - 1];
                    arcs.Add(new RampArc(prev.End, prev.EndHeading, a.Radius, a.Angle));
                }
                var path = RampPath.FromArcs(arcs.ToArray());

                float flatS = Overlap(path, x, Sep(x), true) + kFlatMargin, flatE = Overlap(path, t, Sep(t), false) + kFlatMargin;
                if ((Elevated(x.Height) > 0f && flatS > 75f) || (Elevated(t.Height) > 0f && flatE > 75f))
                    return Fail<Ramp>("overlap");
                var knots = Profile(path, x.Height, t.Height, flatS, flatE, x, t, obstacles);
                if (knots == null)
                    return Fail<Ramp>("profile");
                return new Ramp
                {
                    Path = path, FlatStart = flatS, FlatEnd = flatE, H0 = x.Height, H1 = t.Height, Knots = knots,
                    Level = knots.Max(k => k.H), Radius = radius, Tight = beta * 180f / (float)Math.PI, Extra = shift,
                    From = x, To = t, FromAt = x.At(path.Start), ToAt = t.At(path.End), Name = $"Left{x.Name}",
                };
            }

            private static Vec3 Rotate(Vec3 v, float angle) =>
                new Vec3(v.X * MathF.Cos(angle) - v.Z * MathF.Sin(angle), 0f, v.X * MathF.Sin(angle) + v.Z * MathF.Cos(angle));

            /// <summary>
            /// 从道路（沿 heading）向右转 beta、横向离开 lateral 米的弯：一段圆弧，或接高架时先一段 kRingElevatedShift 小弧离开到
            /// 与道路相距 sep 再接大弧。返回从原点出发的圆弧（reverse 时是从弯的另一头接回道路的顺序：先大弧后小弧，左转方向）。
            /// 横向距离太小、算出的大弧半径比小弧还小时返回 null。
            /// </summary>
            private static List<RampArc>? Bend(Vec3 heading, float rightSign, float beta, float lateral, bool elevated, float sep, bool reverse = false)
            {
                if (lateral < kMinSegment)
                    return null;
                float rs = kRingElevatedShift;
                float a0 = elevated ? Math.Min(beta * 0.9f, (float)Math.Acos(Math.Max(-1f, 1f - sep / rs))) : 0f;
                // 横向：小弧 rs(1 − cos a0) + 大弧 ρ(cos a0 − cos beta)
                float rho = (lateral - rs * (1f - MathF.Cos(a0))) / (MathF.Cos(a0) - MathF.Cos(beta));
                if (rho < (a0 > 0f ? rs : kRightRadius))
                    return null;
                var parts = a0 > 0f ? new[] { (rs, a0), (rho, beta - a0) } : new[] { (rho, beta) };
                var arcs = new List<RampArc>();
                Vec3 at = Vec3.Zero, h = heading;
                if (!reverse)
                {
                    foreach (var (r, a) in parts)
                    {
                        var arc = new RampArc(at, h, r, rightSign * a);
                        arcs.Add(arc);
                        at = arc.End;
                        h = arc.EndHeading;
                    }
                    return arcs;
                }
                // 汇入那头：从环上（方向是 heading 左转 beta）先大弧后小弧右转回 heading；这里只给出半径和转角
                foreach (var (r, a) in parts.Reverse())
                    arcs.Add(new RampArc(Vec3.Zero, heading, r, rightSign * a));
                return arcs;
            }

            /// <summary>
            /// 涡轮的四条右转匝道：四角的大弧（与两幅都相切的圆弧，接高架那头的半径有上限），半径从小到大，取第一个绕在左转匝道外面
            /// （离开道路后与它们在平面上拉开）、坡度够、接点不冲突的。比左转匝道早驶出、晚汇入。
            /// </summary>
            private bool SolveOuterRights()
            {
                foreach (var x in lines)
                {
                    var t = RightTarget(x);
                    // 在左转匝道外面：比从同一幅驶出的左转匝道早驶出、比汇入同一幅的左转匝道晚汇入
                    float latestExit = lefts.Where(l => l.From == x).Min(l => l.FromAt) - kMinSegment;
                    float earliestEntry = lefts.Where(l => l.To == t).Max(l => l.ToAt) + kMinSegment;
                    Ramp? TryRadius(float r)
                    {
                        foreach (float part in new[] { 1f, 0.6f, 0.35f })
                        {
                            // 接高架那头的半径有上限：并行重叠段（约 √(2·间距·半径)）不超过 kMaxRightOverlap，短于游戏切分高架路段的长度；
                            // 转角小、半径大时小弧只转出一部分横距（part），剩下的交给中间的大弧
                            float rS = Elevated(x.Height) > 0f ? Math.Min(r, kMaxRightOverlap * kMaxRightOverlap / (2f * Sep(x))) : r;
                            float rE = Elevated(t.Height) > 0f ? Math.Min(r, kMaxRightOverlap * kMaxRightOverlap / (2f * Sep(t))) : r;
                            if (part < 1f && rS >= r && rE >= r)
                                continue;
                            var path = RampPath.Fillet(x.Origin, x.Dir, t.Origin, t.Dir, rS, r, rE, Sep(x) * part, Sep(t) * part, out _, out _, out _, out _);
                            float flatS = Overlap(path, x, Sep(x), true) + kRightFlatMargin, flatE = Overlap(path, t, Sep(t), false) + kRightFlatMargin;
                            if ((Elevated(x.Height) > 0f && flatS > 75f) || (Elevated(t.Height) > 0f && flatE > 75f))
                                continue;
                            if (path.Length - flatS - flatE < Math.Abs(t.Height - x.Height) / grade)
                                continue;
                            var ramp = new Ramp
                            {
                                Path = path, FlatStart = flatS, FlatEnd = flatE, H0 = x.Height, H1 = t.Height,
                                From = x, To = t, FromAt = x.At(path.Start), ToAt = t.At(path.End), Name = $"Right{x.Name}",
                            };
                            if (ramp.FromAt + ramp.FlatStart > latestExit || ramp.ToAt - ramp.FlatEnd < earliestEntry)
                                continue;
                            if (lefts.All(q => PlanApart(ramp, q)) && rights.All(q => Clear(ramp, q))
                                && AttachmentsApart(lefts.Concat(rights).Append(ramp).ToList()))
                                return ramp;
                        }
                        return null;
                    }
                    // 半径越大大体越容易放下（驶出越早、离左转匝道越远）：按 8% 一档，跳着加倍找到第一个放得下的，再二分往小找
                    var radii = new List<float>();
                    for (float r = 60f; r <= 3000f; r *= 1.08f)
                        radii.Add(r);
                    Ramp? found = null;
                    int lo = -1, hi = -1;
                    for (int k = 0, step = 1; found == null && lo < radii.Count - 1; k = Math.Min(radii.Count - 1, k + step), step *= 2)
                    {
                        if ((found = TryRadius(radii[k])) != null)
                            hi = k;
                        else
                            lo = k;
                    }
                    while (found != null && hi - lo > 1)
                    {
                        int mid = (lo + hi) / 2;
                        if (TryRadius(radii[mid]) is Ramp smaller)
                        {
                            found = smaller;
                            hi = mid;
                        }
                        else
                            lo = mid;
                    }
                    if (found == null)
                    {
                        Trace?.Invoke($"turbine right turn from {x.Name}: no radius fits");
                        return false;
                    }
                    rights.Add(found);
                }
                return true;
            }

            /// <summary>
            /// 纵断面：两头水平段分别与 h0、h1 同高；途中每处与障碍物（道路、别的匝道）平面上挨着的范围，要么在它上方至少 0.9 个层高，
            /// 要么在它下方至少 0.9 个层高（只有它够高时）；全程不低于第 1 层、不高于第 3 层，坡度不超过最大坡度。逐一试每处的上 / 下
            /// 选法，取最高点最低（其次总升降最少）的那个；按“能不升降就不升降”从头走出高度，化简成几个转折点。放不下时返回 null。
            /// 来向那幅在开头的水平段里、去向那幅在结尾的水平段里不算交叉（那是分流、合流）。
            /// </summary>
            private List<(float S, float H)>? Profile(RampPath path, float h0, float h1, float flatS, float flatE,
                Carriageway from, Carriageway to, IReadOnlyList<Obstacle> obstacles)
            {
                float total = path.Length;
                int n = Math.Max(2, (int)Math.Ceiling(total / kProfileStep)) + 1;
                float ds = total / (n - 1);
                // 净空多留出化简误差和一点余量：化简后的高度仍满足 Clear 的 0.9 个层高，不卡在临界上（浮点舍入不同就会时过时不过）
                float minH = Math.Min(hm, hc), maxH = Math.Max(hm, hc) + g, clearance = 0.9f * g + kProfileTolerance + kProfileMargin;
                // 从地面以上的道路 / 匝道上方经过要够高架（低于 8.5 米是路堤，挡土墙压在下面的路上）
                float bridge = HeightRange.BridgeThreshold + kProfileTolerance + kProfileMargin;

                // 每处交叉：障碍物、采样范围、障碍物在这段里的最低 / 最高处
                var crossings = new List<(int Lo, int Hi, float Min, float Max)>();
                var open = new Dictionary<int, (int Lo, int Hi, float Min, float Max)>();
                for (int i = 0; i < n; i++)
                {
                    float s = i * ds;
                    Vec3 v = path.PointAt(s);
                    var hit = new HashSet<int>();
                    for (int k = 0; k < obstacles.Count; k++)
                    {
                        var o = obstacles[k];
                        if (o is LineObstacle && ((s < flatS && IsLine(o, from)) || (s > total - flatE && IsLine(o, to))))
                            continue;
                        if (o.Distance(v, out float h) >= (rw + o.Width) / 2f + gap * 0.5f)
                            continue;
                        hit.Add(k);
                        open[k] = open.TryGetValue(k, out var c) ? (c.Lo, i, Math.Min(c.Min, h), Math.Max(c.Max, h)) : (i, i, h, h);
                    }
                    foreach (int k in open.Keys.Where(k => !hit.Contains(k)).ToList())
                    {
                        crossings.Add(open[k]);
                        open.Remove(k);
                    }
                }
                crossings.AddRange(open.Values);
                if (crossings.Count > kMaxCrossings)
                    return null;

                float[] best = null!;
                float bestTop = float.MaxValue, bestClimb = float.MaxValue;
                var lo = new float[n];
                var hi = new float[n];
                for (int combo = 0; combo < 1 << crossings.Count; combo++)
                {
                    bool possible = true;
                    for (int i = 0; i < n; i++)
                    {
                        float s = i * ds;
                        if (s <= flatS)
                            lo[i] = hi[i] = h0;
                        else if (s >= total - flatE)
                            lo[i] = hi[i] = h1;
                        else
                        {
                            lo[i] = minH;
                            hi[i] = maxH;
                        }
                    }
                    for (int c = 0; c < crossings.Count && possible; c++)
                    {
                        var (a, b, min, max) = crossings[c];
                        bool over = (combo & (1 << c)) == 0;
                        float bound = over ? (max > -HeightRange.RaisedThreshold ? Math.Max(max + clearance, bridge) : max + clearance) : min - clearance;
                        // 从下面穿过时，上面那条在这里也要是高架
                        if (over ? bound > maxH + 1e-3f : bound < minH - 1e-3f || min < HeightRange.BridgeThreshold - 0.01f)
                            possible = false;
                        for (int i = a; i <= b && possible; i++)
                        {
                            if (over)
                                lo[i] = Math.Max(lo[i], bound);
                            else
                                hi[i] = Math.Min(hi[i], bound);
                        }
                    }
                    if (!possible)
                        continue;
                    // 坡度：从两头各推一遍，得到每处能达到的最低 / 最高
                    float step = grade * ds;
                    for (int i = 1; i < n; i++)
                    {
                        hi[i] = Math.Min(hi[i], hi[i - 1] + step);
                        lo[i] = Math.Max(lo[i], lo[i - 1] - step);
                    }
                    for (int i = n - 2; i >= 0; i--)
                    {
                        hi[i] = Math.Min(hi[i], hi[i + 1] + step);
                        lo[i] = Math.Max(lo[i], lo[i + 1] - step);
                    }
                    if (Enumerable.Range(0, n).Any(i => lo[i] > hi[i] + 1e-3f))
                        continue;
                    // 能不升降就不升降
                    var h = new float[n];
                    h[0] = h0;
                    for (int i = 1; i < n; i++)
                    {
                        float low = Math.Max(lo[i], h[i - 1] - step), high = Math.Min(hi[i], h[i - 1] + step);
                        h[i] = Math.Max(low, Math.Min(high, h[i - 1]));
                    }
                    float top = h.Max(), climb = 0f;
                    for (int i = 1; i < n; i++)
                        climb += Math.Abs(h[i] - h[i - 1]);
                    if (top < bestTop - 0.01f || (top < bestTop + 0.01f && climb < bestClimb))
                    {
                        best = h;
                        bestTop = top;
                        bestClimb = climb;
                    }
                }
                if (best == null)
                    return null;
                return Simplify(best, ds);
            }

            /// <summary>
            /// 纵断面的转折点是匝道的节点（下面有立柱）：落在挡立柱的位置（下面有道路、匝道）上时沿匝道挪到最近的空处，
            /// 高度取原纵断面上那一点（只把一个折角削成弦，坡度不会变陡）；挪完要仍与途中的道路、匝道上下错开。挪不开时返回 false。
            /// </summary>
            private bool ShiftKnots(Ramp ramp, IReadOnlyList<Obstacle> lower)
            {
                var knots = ramp.Knots!;
                float total = ramp.Length;
                bool Inside(float s) => s > ramp.FlatStart + kMinSegment && s < total - ramp.FlatEnd - kMinSegment;
                for (int i = 1; i + 1 < knots.Count; i++)
                {
                    if (!Inside(knots[i].S) || Blocker(ramp, knots[i].S, lower) == -1)
                        continue;
                    var old = knots[i];
                    bool moved = false;
                    for (float d = 1f; d <= kMaxKnotShift && !moved; d += 1f)
                        foreach (float t in new[] { old.S - d, old.S + d })
                        {
                            if (!Inside(t) || t <= knots[i - 1].S + kMinSegment || t >= knots[i + 1].S - kMinSegment)
                                continue;
                            knots[i] = old;
                            knots[i] = (t, ramp.HeightAt(t));
                            if (Blocker(ramp, t, lower) == -1 && ProfileClear(ramp, lower))
                            {
                                moved = true;
                                break;
                            }
                        }
                    ramp.HeightsChanged();
                    if (!moved)
                    {
                        knots[i] = old;
                        return false;
                    }
                }
                return true;
            }

            /// <summary>匝道与途中的道路、匝道上下错开至少 0.9 个层高，从地面以上的上方经过、从下面穿过时上面那条都是高架（同 Profile 的判断）。</summary>
            private bool ProfileClear(Ramp ramp, IReadOnlyList<Obstacle> obstacles)
            {
                float total = ramp.Length;
                for (float s = 0f; s <= total; s += kProfileStep)
                {
                    Vec3 v = ramp.Path.PointAt(s);
                    float h = ramp.HeightAt(s);
                    foreach (var o in obstacles)
                    {
                        if (o is LineObstacle && ((s < ramp.FlatStart && IsLine(o, ramp.From)) || (s > total - ramp.FlatEnd && IsLine(o, ramp.To))))
                            continue;
                        if (o.Distance(v, out float hh) >= (rw + o.Width) / 2f + gap * 0.5f)
                            continue;
                        bool bad = h > hh
                            ? h - hh < 0.9f * g || (hh > -HeightRange.RaisedThreshold && h < HeightRange.BridgeThreshold - 0.01f)
                            : hh - h < 0.9f * g || hh < HeightRange.BridgeThreshold - 0.01f;
                        if (bad)
                            return false;
                    }
                }
                return true;
            }

            private static bool IsLine(Obstacle o, Carriageway line) =>
                o is LineObstacle && Directions.Dot(o.Direction, line.Dir) > 0.9999f && o.Distance(line.Origin, out _) < 0.01f;

            /// <summary>采样高度化简成转折点：去掉与相邻两点连线偏差不超过 kProfileTolerance 的点（Douglas–Peucker）。</summary>
            private static List<(float S, float H)> Simplify(float[] h, float ds)
            {
                var keep = new bool[h.Length];
                keep[0] = keep[h.Length - 1] = true;
                void Split(int a, int b)
                {
                    int worst = -1;
                    float worstError = kProfileTolerance;
                    for (int i = a + 1; i < b; i++)
                    {
                        float expected = h[a] + (h[b] - h[a]) * (i - a) / (b - a);
                        float error = Math.Abs(h[i] - expected);
                        if (error > worstError)
                        {
                            worstError = error;
                            worst = i;
                        }
                    }
                    if (worst < 0)
                        return;
                    keep[worst] = true;
                    Split(a, worst);
                    Split(worst, b);
                }
                Split(0, h.Length - 1);
                return Enumerable.Range(0, h.Length).Where(i => keep[i]).Select(i => (i * ds, h[i])).ToList();
            }

            /// <summary>
            /// 高架道路上匝道接点（节点下有立柱，位置固定，分段时挪不动）的下面没有匝道穿过。
            /// </summary>
            private bool AttachNodesClear(IReadOnlyList<Ramp> ramps)
            {
                foreach (var line in lines.Where(l => LevelPlanner.BandOf(l.Height) == ElevationBand.Elevated))
                {
                    var nodes = ramps.Where(r => r.From == line).Select(r => r.FromAt).Concat(ramps.Where(r => r.To == line).Select(r => r.ToAt)).ToList();
                    float reach = line.Width / 2f + rw / 2f + kPillarThickness;
                    foreach (var ramp in ramps)
                        foreach (var v in ramp.Samples())
                            if (v.Y < line.Height - 1f && Math.Abs(DiamondTemplate.Cross2(Flat0(v) - line.Origin, line.Dir)) < reach
                                && nodes.Any(at => Math.Abs(line.At(v) - at) < rw / 2f + kPillarThickness + kPillarMargin))
                                return false;
                }
                return true;
            }

            /// <summary>两条匝道离开道路的部分在平面上拉开（不靠高度错开）：右转匝道整条绕在左转匝道外面。</summary>
            private bool PlanApart(Ramp one, Ramp other)
            {
                var o = other.AsObstacle(rw);
                float reach = rw + gap;
                foreach (var u in one.Samples())
                {
                    float onRoad = Math.Min(Math.Abs(DiamondTemplate.Cross2(Flat0(u) - one.From.Origin, one.From.Dir)),
                        Math.Abs(DiamondTemplate.Cross2(Flat0(u) - one.To.Origin, one.To.Dir)));
                    if (onRoad >= Sep(one.From) && o.Distance(u, out _) < reach)
                        return false;
                }
                return true;
            }

            /// <summary>每一幅上各匝道的接点互不冲突：接点不落在别的匝道与这一幅的并行重叠范围里，相邻接点至少隔 kMinSegment。</summary>
            private bool AttachmentsApart(IReadOnlyList<Ramp> ramps)
            {
                foreach (var line in lines)
                {
                    var attaches = ramps.Where(r => r.From == line).Select(r => (Node: r.FromAt, Lo: r.FromAt, Hi: r.FromAt + r.FlatStart))
                        .Concat(ramps.Where(r => r.To == line).Select(r => (Node: r.ToAt, Lo: r.ToAt - r.FlatEnd, Hi: r.ToAt))).ToList();
                    for (int i = 0; i < attaches.Count; i++)
                        for (int j = 0; j < attaches.Count; j++)
                        {
                            if (i == j)
                                continue;
                            var a = attaches[i];
                            var b = attaches[j];
                            if (a.Node > b.Lo - kMinSegment && a.Node < b.Hi + kMinSegment)
                                return false;
                        }
                }
                return true;
            }
        }
    }
}
