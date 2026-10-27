using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>
    /// 原路口改造（跨模板）：工具把每条臂的 ExternalElevation 设成原路口的高度，不在这个高度的臂（如高架的上层）要在臂端加坡道降过去，
    /// 否则臂端悬在原有道路上方、接不回去。每种类型按典型参数、原路口在地面（0 米）时，所有臂端都要落在 0 米，坡度不超限。
    /// </summary>
    public class ApproachSlopeTests
    {
        public static IEnumerable<object[]> Types() => TemplateRegistry.All.Select(t => new object[] { t.Type });

        [Theory]
        [MemberData(nameof(Types))]
        public void Retrofit_AllLegEndsReachTheExistingRoad(InterchangeType type)
        {
            var template = TemplateRegistry.Get(type)!;
            var p = ThumbnailTests.Params(type);
            foreach (var leg in p.Legs)
                leg.ExternalElevation = 0f;
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            var ends = InterchangeLayout.LegEnds(segments);
            Assert.NotEmpty(ends);
            Assert.All(ends, end => Assert.True(System.Math.Abs(end.Y) <= InterchangeLayout.ApproachTolerance,
                $"leg end ({end.X:F0}, {end.Z:F0}) is at {end.Y:F1} m"));
        }
    }
}
