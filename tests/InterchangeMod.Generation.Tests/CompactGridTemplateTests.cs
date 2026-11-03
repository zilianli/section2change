using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>井字（紧凑定向）的替换模式：四条双幅臂换算成虚拟端点，复用连接模式的井字求解器。</summary>
    public class CompactGridTemplateTests
    {
        private readonly CompactGridTemplate _template = new CompactGridTemplate();

        private static InterchangeParams Make(float angle = 90f, float mainHeight = 0f, float crossHeight = 0f, bool rht = true)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(angle, 0, 1),
                RampWidth = 8f, RampPrefab = null, MaxGrade = 0.16f, RightHandTraffic = rht,
                RoadCatalog = CompactGridFixtures.Catalog(),
            };
            p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Road4");
            p.SetRoad(1, 3, RoadForm.Dual, 20f, 8f, "Road4");
            p.Levels[0].Elevation = mainHeight;
            p.Levels[1].Elevation = crossHeight;
            return p;
        }

        [Theory]
        [InlineData(90f, 0f, 0f, true)]
        [InlineData(90f, 0f, 0f, false)]
        [InlineData(90f, 10f, 0f, true)]
        public void Generated_ConnectsAllEightCarriagewayEnds(float angle, float mainHeight, float crossHeight, bool rht)
        {
            var p = Make(angle, mainHeight, crossHeight, rht);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.NotEmpty(segments);
            var ends = InterchangeLayout.LegEnds(segments);
            Assert.Equal(8, ends.Count);
            // 臂端在各自的道路高度上（主线、被交道路），离中心至少虚拟端点的距离
            foreach (var end in ends)
            {
                float along = new Vec3(end.X, 0f, end.Z).Length();
                Assert.True(along >= 99f, $"leg end only {along:F1} m from the center");
                bool onMain = System.Math.Abs(Directions.Dot(new Vec3(end.X, 0f, end.Z), p.Legs[0].Direction)) > along * 0.7f;
                Assert.Equal(onMain ? mainHeight : crossHeight, end.Y, 1);
            }
            Assert.Contains(segments, s => s.Role == RoadRole.Ramp);
            // 中心小转弯（右转）至少 40 米：游戏内 26 米的右转匝道看起来像折角
            Assert.All(segments.Where(s => s.Role == RoadRole.Ramp), s => Assert.True(1f / s.Curve.MaxCurvature() >= 39f, $"{s.Name}: radius {1f / s.Curve.MaxCurvature():F0} m"));
            Assert.All(segments, s => Assert.False(string.IsNullOrEmpty(s.Prefab)));
        }

        [Fact]
        public void TwoWayRoad_IsRejectedWithAHint()
        {
            var p = Make();
            p.SetRoad(1, 3, RoadForm.TwoWay, 40f, 0f, "Road8");
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("双幅"));
        }

        [Fact]
        public void SameParams_ReuseTheSolution()
        {
            var p = Make();
            Assert.Empty(_template.ValidateParams(p));
            var first = _template.Generate(p);
            Assert.Same(first, _template.Generate(p.Clone()));
        }
    }
}
