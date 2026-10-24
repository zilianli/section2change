using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    internal static class TestHelpers
    {
        /// <summary>
        /// 每个匝道端点要么精确落在道路节点上，要么精确等于另一段匝道的端点（匝道内部的分段点）。
        /// 游戏按坐标完全相等来合并节点，所以这里要求精确相等而不是“足够接近”。
        /// 同时要求每条匝道链两头都接到道路上：不存在悬空的匝道端点。
        /// </summary>
        public static void AssertRampsConnected(IReadOnlyList<RoadSegmentSpec> segments)
        {
            var roadNodes = segments.Where(s => s.Role != RoadRole.Ramp)
                .SelectMany(s => new[] { s.StartNode, s.EndNode }).ToList();
            var ramps = segments.Where(s => s.Role == RoadRole.Ramp).ToList();

            foreach (var ramp in ramps)
            {
                foreach (var end in new[] { ramp.Curve.P0, ramp.Curve.P3 })
                {
                    bool onRoad = roadNodes.Contains(end);
                    int sharedWithRamps = ramps.Count(r => r != ramp && (r.Curve.P0 == end || r.Curve.P3 == end));
                    Assert.True(onRoad || sharedWithRamps > 0, $"{ramp.Name} has a dangling endpoint {end}");
                }
            }
        }
    }
}
