using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// 规划上层道路跨越下层道路的那一段（跨越段）的两端位置，让上层的立柱都不落在下层道路上。
    /// 坐标是沿上层道路线的一维位置；下层道路在这条线上占 [center − footprint, center + footprint]。
    ///
    /// 跨越段上的立柱（游戏规则）：
    ///   1. 两端节点；
    ///   2. 段长超过 maxLength 时，游戏把它等分成 ⌈L / maxLength⌉ 份，切点是新节点（也有立柱）；
    ///   3. 有中部立柱的道路，每一份上：无间距时正中一根；有间距时 n = ⌊份长 / 间距 − 0.5⌋ 根，位于 份长·k/(n+1)。
    /// 依次尝试对称跨越段，再逐米向一侧加长，直到所有立柱都在下层道路之外或下层双幅的分隔带里。
    /// </summary>
    public static class PillarPlanner
    {
        /// <summary>节点立柱外缘离下层道路外缘的距离。</summary>
        public const float Margin = 3f;

        public readonly struct Span
        {
            public Span(float lo, float hi, string? error = null)
            {
                Lo = lo;
                Hi = hi;
                Error = error;
            }

            public float Lo { get; }
            public float Hi { get; }
            public string? Error { get; }
        }

        /// <param name="center">下层道路中心线与上层道路线的交点位置。</param>
        /// <param name="footprint">下层道路在上层道路线上占地的一半（下层半宽 / sin 夹角）。</param>
        /// <param name="medianHalf">下层双幅分隔带在上层道路线上的一半宽度；单幅为 0。</param>
        /// <param name="spacing">上层道路的中部立柱间距（见 LegSpec.MiddlePillarSpacing）。</param>
        /// <param name="pillarWidth">上层道路立柱宽度。</param>
        /// <param name="maxLength">游戏不切分的最长段长（高架 m_ElevatedLength）。</param>
        public static Span Plan(float center, float footprint, float medianHalf, float? spacing, float pillarWidth, float maxLength)
        {
            float clear = footprint + Margin + pillarWidth / 2f;   // 两端节点立柱离交点至少这么远
            float maxExtra = 2f * maxLength;

            for (float extra = 0f; extra <= maxExtra; extra += 1f)
            {
                if (PillarsClear(center - clear, center + clear + extra, center, footprint, medianHalf, spacing, pillarWidth, maxLength))
                    return new Span(center - clear, center + clear + extra);
                if (PillarsClear(center - clear - extra, center + clear, center, footprint, medianHalf, spacing, pillarWidth, maxLength))
                    return new Span(center - clear - extra, center + clear);
            }
            return new Span(center - clear, center + clear,
                $"上层道路的立柱无法避开下层道路（下层占地 {2f * footprint:F0} 米）：请把下层改为双幅并加宽分隔带，或换窄一些的道路");
        }

        /// <summary>按游戏规则列出 [lo, hi] 这一段上除两端以外的所有立柱位置（切分节点 + 中部立柱）。</summary>
        public static List<float> InnerPillars(float lo, float hi, float? spacing, float maxLength)
        {
            var result = new List<float>();
            float length = hi - lo;
            int parts = Math.Max(1, (int)Math.Ceiling((length - 0.16f) / maxLength));
            float partLength = length / parts;
            for (int part = 0; part < parts; part++)
            {
                float start = lo + partLength * part;
                if (part > 0)
                    result.Add(start);   // 切分节点
                if (spacing == null)
                    continue;
                if (spacing.Value <= 0f)
                {
                    result.Add(start + partLength / 2f);
                    continue;
                }
                int count = (int)(partLength / Math.Max(1f, spacing.Value) - 0.5f);
                for (int k = 1; k <= count; k++)
                    result.Add(start + partLength * k / (count + 1));
            }
            return result;
        }

        /// <summary>多处下层占地时的节点规划结果：沿线坐标从小到大的节点位置（至少两个）。</summary>
        public readonly struct NodePlan
        {
            public NodePlan(IReadOnlyList<float> nodes, string? error = null)
            {
                Nodes = nodes;
                Error = error;
            }

            public IReadOnlyList<float> Nodes { get; }
            public string? Error { get; }
        }

        /// <summary>
        /// 下层有好几处占地（如单点立交桥下的被交道路和几条匝道）时规划上层的节点：
        /// 第一处占地之前、最后一处之后各一个节点，中间每个空隙可以放一个节点（放得下立柱时），
        /// 所有立柱（节点、切分节点、中部立柱）都不落在占地上。每段长度由游戏规则决定切分（见 InnerPillars）。
        /// 先试节点紧贴占地，再逐步外移；用记忆化搜索，找不到时返回错误。
        /// </summary>
        /// <param name="occupied">下层占地区间（沿上层道路线的坐标，不含立柱余量）。</param>
        /// <param name="nodeClearance">节点离占地区间的距离；默认 Margin + 立柱半宽。</param>
        public static NodePlan PlanNodes(IReadOnlyList<(float Lo, float Hi)> occupied, float? spacing, float pillarWidth, float maxLength,
            float step = 2f, float? nodeClearance = null)
        {
            float nodeClear = nodeClearance ?? Margin + pillarWidth / 2f;   // 节点立柱离占地的距离（占地已含立柱横向尺寸时可传更小的值）
            float pillarClear = pillarWidth / 2f + 0.5f;   // 其他立柱离占地的距离
            var zones = Merge(occupied);
            if (zones.Count == 0)
                return new NodePlan(new float[0], "没有下层占地");

            bool Clear(float x, float clear) => zones.All(z => x <= z.Lo - clear || x >= z.Hi + clear);
            bool SegmentOk(float a, float b) => InnerPillars(a, b, spacing, maxLength).All(x => Clear(x, pillarClear));

            float reach = 2f * maxLength;
            var memo = new Dictionary<(int, int), List<float>?>();

            // 从节点 prev 出发，处理第 gap 个空隙（zones[gap] 与 zones[gap+1] 之间）；gap == zones.Count − 1 时放最后一个节点
            List<float>? Solve(float prev, int gap)
            {
                var key = ((int)Math.Round(prev / step), gap);
                if (memo.TryGetValue(key, out var cached))
                    return cached;
                List<float>? result = null;
                if (gap == zones.Count - 1)
                {
                    for (float x = zones[gap].Hi + nodeClear; x <= zones[gap].Hi + nodeClear + reach && result == null; x += step)
                        if (SegmentOk(prev, x))
                            result = new List<float> { x };
                }
                else
                {
                    // 空隙里放一个节点（放得下时），否则跨过这个空隙
                    for (float x = zones[gap].Hi + nodeClear; x <= zones[gap + 1].Lo - nodeClear && result == null; x += step)
                    {
                        if (!Clear(x, nodeClear) || !SegmentOk(prev, x))
                            continue;
                        var rest = Solve(x, gap + 1);
                        if (rest != null)
                            result = new List<float> { x }.Concat(rest).ToList();
                    }
                    result ??= Solve(prev, gap + 1);
                }
                memo[key] = result;
                return result;
            }

            for (float first = zones[0].Lo - nodeClear; first >= zones[0].Lo - nodeClear - reach; first -= step)
            {
                var rest = Solve(first, 0);
                if (rest != null)
                    return new NodePlan(new List<float> { first }.Concat(rest).ToList());
            }
            return new NodePlan(new[] { zones[0].Lo - nodeClear, zones[zones.Count - 1].Hi + nodeClear },
                "上层道路的立柱无法避开桥下的道路：请减小相交角度的倾斜、加长匝道，或换中部立柱更少的道路");
        }

        /// <summary>按起点排序并合并重叠的区间。</summary>
        private static List<(float Lo, float Hi)> Merge(IReadOnlyList<(float Lo, float Hi)> intervals)
        {
            var result = new List<(float Lo, float Hi)>();
            foreach (var interval in intervals.OrderBy(i => i.Lo))
            {
                if (result.Count > 0 && interval.Lo <= result[result.Count - 1].Hi)
                    result[result.Count - 1] = (result[result.Count - 1].Lo, Math.Max(result[result.Count - 1].Hi, interval.Hi));
                else
                    result.Add(interval);
            }
            return result;
        }

        private static bool PillarsClear(float lo, float hi, float center, float footprint, float medianHalf, float? spacing, float pillarWidth, float maxLength)
        {
            float halfPillar = pillarWidth / 2f;
            foreach (float pillar in InnerPillars(lo, hi, spacing, maxLength))
            {
                float offset = Math.Abs(pillar - center);
                bool outside = offset >= footprint + halfPillar + 0.5f;
                bool inMedian = offset + halfPillar + 0.5f <= medianHalf;
                if (!outside && !inMedian)
                    return false;
            }
            return true;
        }
    }
}
