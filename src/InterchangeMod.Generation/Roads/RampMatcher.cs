using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Roads
{
    /// <summary>
    /// 道路自动匹配：
    ///   匝道车道数——主线每方向 N 条车道，分流时 R 条驶出，默认 R = max(1, ⌊N/2⌋)；
    ///   匝道道路——从和主线同类（公路/街道）的单行道路里选车道数最接近 R 的；
    ///   形式转换——双向道路要建成双幅时，找每方向车道数相同的单行道路；反之亦然。
    /// </summary>
    public static class RampMatcher
    {
        /// <summary>
        /// 不适合自动选用的特殊道路（桥梁、港口、公交道路等）。按名称排除，因为游戏里没有对应的分类字段。
        /// </summary>
        private static readonly string[] ExcludedNameParts =
        {
            "Bridge", "Harbor", "Quay", "Dam", "Gravel", "Public Transport", "Golden Gate", "Alley", "Pedestrian",
        };

        public static int DefaultExitLanes(int mainLanesPerDirection) => Math.Max(1, mainLanesPerDirection / 2);

        /// <summary>可以被自动选用的普通道路：不是特殊道路、没有公交专用道、名字不是 GUID。</summary>
        public static bool IsOrdinaryRoad(RoadInfo road) =>
            road.ForwardLanes + road.BackwardLanes > 0
            && road.PublicTransportLanes == 0
            && !ExcludedNameParts.Any(part => road.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
            && !LooksGenerated(road.Name);

        public static bool IsRampCandidate(RoadInfo road) => road.IsOneWay && IsOrdinaryRoad(road);

        /// <summary>挑选匝道道路：同类单行道路中车道数最接近 targetLanes 的。</summary>
        public static RoadInfo? PickRamp(IEnumerable<RoadInfo> roads, bool highway, int targetLanes) =>
            PickClosest(roads, highway, targetLanes, oneWay: true);

        /// <summary>
        /// 在单行（oneWay = true）或双向道路中，挑每方向车道数最接近 lanesPerDirection 的那条：
        /// 一样接近时优先车道多的（不做瓶颈），再优先更窄的，再优先名字短的（不带停车等变体）。
        /// 同类（公路/街道）里没有合适的道路时退回到另一类。
        /// </summary>
        public static RoadInfo? PickClosest(IEnumerable<RoadInfo> roads, bool highway, int lanesPerDirection, bool oneWay)
        {
            var candidates = roads.Where(r => r.IsOneWay == oneWay && IsOrdinaryRoad(r) && (oneWay || IsSymmetric(r))).ToList();
            return Best(candidates.Where(r => r.IsHighway == highway), lanesPerDirection)
                ?? Best(candidates, lanesPerDirection);
        }

        /// <summary>
        /// 把双向道路拆成双幅时用的单行道路：每方向车道数相同、两幅并排不超过双向道路的宽度（好让两幅外缘对齐它的两侧边缘），
        /// 同类（公路/街道）优先，再取最宽的（分隔带最窄）。没有放得下的返回 null。
        /// </summary>
        public static RoadInfo? PickSplitOneWay(IEnumerable<RoadInfo> roads, RoadInfo twoWay)
        {
            var fits = roads.Where(r => r.IsOneWay && IsOrdinaryRoad(r) && r.LanesPerDirection == twoWay.LanesPerDirection
                && 2f * r.Width <= twoWay.Width + 0.01f).ToList();
            return fits.OrderBy(r => r.IsHighway == twoWay.IsHighway ? 0 : 1)
                .ThenByDescending(r => r.Width)
                .ThenBy(r => r.Name.Length)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static bool IsSymmetric(RoadInfo road) => road.ForwardLanes == road.BackwardLanes;

        private static RoadInfo? Best(IEnumerable<RoadInfo> roads, int targetLanes) =>
            roads.OrderBy(r => Math.Abs(r.LanesPerDirection - targetLanes))
                .ThenByDescending(r => r.LanesPerDirection)
                .ThenBy(r => r.Width)
                .ThenBy(r => r.Name.Length)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .FirstOrDefault();

        /// <summary>资产包里的道路有时用 GUID 当名字（如 bbf49461f-…），不适合自动选用。</summary>
        private static bool LooksGenerated(string name) =>
            name.Length >= 36 && name.Count(c => c == '-') >= 4 && name.Take(8).All(Uri.IsHexDigit);
    }
}
