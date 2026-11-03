using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>
    /// 原生输入与外部接回合同分别测试。过渡合同显式提供内部/外部形式，
    /// 不调用也不复制游戏工具 BuildParams，不能证明目录选路或游戏摆放成功。
    /// </summary>
    public class RoadFormSupportMatrixTests
    {
        public static IEnumerable<object[]> NativeCases() =>
            from template in TemplateRegistry.All.Where(t => t.Type != InterchangeType.Wye)
            from main in new[] { RoadForm.TwoWay, RoadForm.Dual }
            from cross in new[] { RoadForm.TwoWay, RoadForm.Dual }
            select new object[] { template.Type, main, cross };

        [Theory]
        [MemberData(nameof(NativeCases))]
        public void NativeInput_IsExplicitlyRejectedOrProducesConnectedExternalEnds(
            InterchangeType type, RoadForm main, RoadForm cross)
        {
            var template = TemplateRegistry.Get(type)!;
            var p = Make(template, main, cross);
            var errors = template.ValidateParams(p);
            bool mainRejected = type != InterchangeType.Diamond && main != RoadForm.Dual;
            bool crossMustBeTwoWay = type is InterchangeType.SinglePoint or InterchangeType.PartialCloverleaf;
            bool crossMustBeDual = type is InterchangeType.Trumpet or InterchangeType.Cloverleaf
                or InterchangeType.SemiDirectionalT or InterchangeType.DirectionalT
                or InterchangeType.Stack or InterchangeType.Turbine;
            if (mainRejected || (crossMustBeTwoWay && cross != RoadForm.TwoWay)
                || (crossMustBeDual && cross != RoadForm.Dual))
            {
                Assert.NotEmpty(errors);
                Assert.Contains(errors, e => e.Contains("双幅") || e.Contains("双向"));
                return;
            }
            AssertLayout(template, p, main, cross);
        }

        // 每一项是待核查的外部接回合同；不是对工具自动转换的运行测试。
        public static IEnumerable<object[]> ExternalCases() => NativeCases().Where(c =>
            !((InterchangeType)c[0] is InterchangeType.SinglePoint or InterchangeType.Trumpet
                && (RoadForm)c[1] == RoadForm.TwoWay));

        [Theory]
        [InlineData(InterchangeType.SinglePoint, RoadForm.TwoWay)]
        [InlineData(InterchangeType.SinglePoint, RoadForm.Dual)]
        [InlineData(InterchangeType.Trumpet, RoadForm.TwoWay)]
        [InlineData(InterchangeType.Trumpet, RoadForm.Dual)]
        public void MissingMainTransition_IsExplicitlyRejected(InterchangeType type, RoadForm crossOutside)
        {
            var template = TemplateRegistry.Get(type)!;
            var crossInside = type == InterchangeType.SinglePoint ? RoadForm.TwoWay : RoadForm.Dual;
            // 使用已通过的内部组合；只为真实缺失的主线外部双向过渡要求拒绝。
            var p = Make(template, RoadForm.Dual, crossInside);
            SetExternal(p, template.LegCount == 3 ? new[] { 1 } : new[] { 1, 3 }, crossOutside, "CrossOutside");
            foreach (var indices in new[] { new[] { 0 }, new[] { 2 }, new[] { 0, 2 } })
            {
                var unsupported = p.Clone();
                SetExternal(unsupported, indices, RoadForm.TwoWay, "MainOutside");
                Assert.Contains(template.ValidateParams(unsupported), e => e.Contains("主线") && e.Contains("双向")
                    && (e.Contains("不支持") || e.Contains("未实现")));
            }
        }

        [Theory]
        [MemberData(nameof(ExternalCases))]
        public void ExternalContract_ConvertedInternalFormsRetainRequestedEnds(
            InterchangeType type, RoadForm mainOutside, RoadForm crossOutside)
        {
            var template = TemplateRegistry.Get(type)!;
            RoadForm crossInside = type is InterchangeType.SinglePoint or InterchangeType.PartialCloverleaf
                ? RoadForm.TwoWay
                : type is InterchangeType.Diamond or InterchangeType.ExpresswayNode
                    or InterchangeType.ExpresswayT or InterchangeType.Roundabout ? crossOutside : RoadForm.Dual;
            var p = Make(template, RoadForm.Dual, crossInside);
            SetExternal(p, new[] { 0, 2 }, mainOutside, "MainOutside");
            SetExternal(p, template.LegCount == 3 ? new[] { 1 } : new[] { 1, 3 }, crossOutside, "CrossOutside");
            AssertLayout(template, p, mainOutside, crossOutside);
        }

        public static IEnumerable<object[]> WyeCases() => Enumerable.Range(0, 8).Select(mask => new object[] { mask });

        public static IEnumerable<object[]> LeftTrafficCases() => TemplateRegistry.All.Select(t => new object[] { t.Type });

        [Theory]
        [MemberData(nameof(LeftTrafficCases))]
        public void LeftTraffic_IsExplicitlyRejectedOrKeepsExternalTravelSides(InterchangeType type)
        {
            var template = TemplateRegistry.Get(type)!;
            var cross = type is InterchangeType.SinglePoint or InterchangeType.PartialCloverleaf
                ? RoadForm.TwoWay : RoadForm.Dual;
            var p = type == InterchangeType.Wye ? MakeWye() : Make(template, RoadForm.Dual, cross);
            p.RightHandTraffic = false;
            if (type is InterchangeType.Trumpet or InterchangeType.SemiDirectionalT or InterchangeType.DirectionalT
                or InterchangeType.Stack or InterchangeType.Turbine or InterchangeType.Wye)
            {
                Assert.Contains(template.ValidateParams(p), e => e.Contains("目前只支持右侧通行"));
                return;
            }
            AssertLayout(template, p, RoadForm.Dual, cross);
        }

        [Theory]
        [MemberData(nameof(WyeCases))]
        public void Wye_RequiresAllThreeInternalLegsDual(int dualMask)
        {
            var template = TemplateRegistry.Get(InterchangeType.Wye)!;
            var p = MakeWye();
            for (int i = 0; i < 3; i++)
                if ((dualMask & (1 << i)) == 0)
                    p.Legs[i].Form = RoadForm.TwoWay;
            if (dualMask != 7)
            {
                Assert.Contains(template.ValidateParams(p), e => e.Contains("双幅"));
                return;
            }
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
            AssertLegEnds(segments, p, p.Legs.Select(l => l.Form).ToArray());
        }

        [Theory]
        [MemberData(nameof(WyeCases))]
        public void Wye_ExternalFormsAreIndependentPerLeg(int dualMask)
        {
            var template = TemplateRegistry.Get(InterchangeType.Wye)!;
            var p = MakeWye();
            var outside = Enumerable.Range(0, 3)
                .Select(i => (dualMask & (1 << i)) == 0 ? RoadForm.TwoWay : RoadForm.Dual).ToArray();
            for (int i = 0; i < 3; i++)
                SetExternal(p, new[] { i }, outside[i], "WyeOutside");
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(outside.Sum(f => f == RoadForm.Dual ? 2 : 1), InterchangeLayout.LegEnds(segments).Count);
            AssertLegEnds(segments, p, outside);
        }

        private static InterchangeParams Make(IInterchangeTemplate template, RoadForm main, RoadForm cross)
        {
            bool directional = template.Type is InterchangeType.SemiDirectionalT or InterchangeType.DirectionalT;
            var p = new InterchangeParams
            {
                Legs = template.LegCount == 3 ? InterchangeParams.TLegs(90f, 0, directional ? 0 : 1)
                    : InterchangeParams.CrossLegs(90f, 0, 1),
                RampWidth = 8f,
                LoopRadius = 0f,
                SharedRampPrefab = "SharedTwoWay", SharedRampWidth = 16f,
                RingPrefab = "RingOneWay", RingWidth = 8f,
            };
            p.SetRoad(0, 2, main, main == RoadForm.Dual ? 16f : 36f, 4f, "MainInside");
            p.SetRoad(1, template.LegCount == 3 ? 1 : 3, cross, cross == RoadForm.Dual ? 12f : 28f, 4f, "CrossInside");
            if (template.Type is InterchangeType.SinglePoint or InterchangeType.ExpresswayNode or InterchangeType.ExpresswayT
                or InterchangeType.Cloverleaf or InterchangeType.PartialCloverleaf or InterchangeType.Roundabout)
            {
                p.Levels[0].Elevation = 10f;
                p.Levels[1].Elevation = 0f;
            }
            // 原生菱形双向主线只在地面合法；斜坡长度采用真实生产求解器。
            if (template.Type is InterchangeType.Diamond or InterchangeType.Trumpet)
                p = InterchangeLayout.FitRampLength(template, p);
            return p;
        }

        private static InterchangeParams MakeWye()
        {
            var p = new InterchangeParams
            {
                Legs = new List<LegSpec> { new LegSpec(0f, 0), new LegSpec(120f, 0), new LegSpec(240f, 0) },
                RampWidth = 8f, RampSideGap = 2f,
            };
            for (int i = 0; i < 3; i++)
                p.SetRoad(i, i, RoadForm.Dual, 16f, 8f, "WyeInside");
            return p;
        }

        private static void SetExternal(InterchangeParams p, int[] indices, RoadForm outside, string prefab)
        {
            foreach (int i in indices)
            {
                var leg = p.Legs[i];
                leg.ExternalElevation = 0f;
                if (leg.Form == outside)
                    continue;
                leg.ExternalForm = outside;
                leg.ExternalPrefab = prefab;
                leg.ExternalWidth = outside == RoadForm.TwoWay ? 2f * leg.RoadWidth + leg.MedianWidth : 12f;
                leg.ExternalMedian = 4f;
            }
        }

        private static void AssertLayout(IInterchangeTemplate template, InterchangeParams p, RoadForm main, RoadForm cross)
        {
            p = InterchangeLayout.FitRampLength(template, p);
            Assert.True(template.ValidateParams(p).Count == 0, string.Join("; ", template.ValidateParams(p)));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            var outside = p.Legs.Select((_, i) => i % 2 == 0 ? main : cross).ToArray();
            int frontageEnds = template.Type is InterchangeType.ExpresswayNode or InterchangeType.ExpresswayT ? 4 : 0;
            Assert.Equal(outside.Sum(f => f == RoadForm.Dual ? 2 : 1) + frontageEnds,
                InterchangeLayout.LegEnds(segments).Count);
            AssertLegEnds(segments, p, outside);
        }

        private static void AssertLegEnds(IReadOnlyList<RoadSegmentSpec> segments, InterchangeParams p, RoadForm[] outside)
        {
            var roads = segments.Where(s => s.Role is RoadRole.MainRoad or RoadRole.CrossRoad).ToList();
            var ends = InterchangeLayout.LegEnds(segments).Where(e => roads.Any(s => s.StartNode == e || s.EndNode == e)).ToList();
            for (int i = 0; i < p.Legs.Count; i++)
            {
                int nearest(Vec3 e) => Enumerable.Range(0, p.Legs.Count)
                    .OrderByDescending(k => Directions.Dot(e, p.Legs[k].Direction)).First();
                var legEnds = ends.Where(e => nearest(e) == i).ToList();
                Assert.Equal(outside[i] == RoadForm.Dual ? 2 : 1, legEnds.Count);
                var owners = legEnds.Select(e => roads.Single(s => s.StartNode == e || s.EndNode == e)).ToList();
                string? expectedPrefab = p.Legs[i].Form == outside[i] ? p.Legs[i].RoadPrefab : p.Legs[i].ExternalPrefab;
                Assert.All(owners, s => Assert.Equal(expectedPrefab, s.Prefab));
                if (outside[i] == RoadForm.Dual)
                {
                    Assert.Single(owners.Where(s => legEnds.Contains(s.EndNode)));
                    Assert.Single(owners.Where(s => legEnds.Contains(s.StartNode)));
                    foreach (var owner in owners)
                    {
                        bool outward = legEnds.Contains(owner.EndNode);
                        Vec3 tangent = owner.Curve.Derivative(outward ? 1f : 0f);
                        float dot = Directions.Dot(tangent, p.Legs[i].Direction);
                        Assert.True(outward ? dot > 0f : dot < 0f, $"leg {i}: {owner.Name} direction");
                        Vec3 end = outward ? owner.Curve.P3 : owner.Curve.P0;
                        float side = Directions.Dot(end, Directions.RightOf(p.Legs[i].Direction))
                            * (p.RightHandTraffic ? 1f : -1f);
                        Assert.True(outward ? side > 0f : side < 0f, $"leg {i}: {owner.Name} traffic side");
                    }
                }
            }
        }
    }
}
