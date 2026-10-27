using System.Collections.Generic;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Junctions;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class JunctionMatcherTests
    {
        /// <summary>一条双向道路穿过 center，方向角 angle：生成两条离开路口的路段。</summary>
        private static IEnumerable<ExternalEdge> TwoWayRoad(Vec3 center, float angle, string road, int lanes, float width = 24f, bool highway = false)
        {
            foreach (float a in new[] { angle, angle + 180f })
                yield return new ExternalEdge
                {
                    Anchor = center, Direction = Directions.FromAngle(a), Road = road,
                    OneWay = false, Width = width, LanesPerDirection = lanes, IsHighway = highway,
                };
        }

        /// <summary>
        /// 一条双幅道路穿过 center：两幅单行道路，中心线各偏移 (分隔带 + 宽度)/2，按右侧通行各自行驶。
        /// </summary>
        private static IEnumerable<ExternalEdge> DualRoad(Vec3 center, float angle, string road, int lanes, float width, float median, bool highway = true)
        {
            Vec3 dir = Directions.FromAngle(angle);
            Vec3 right = Directions.RightOf(dir);
            float offset = (median + width) / 2f;
            // 沿 +dir 行驶的那幅在右侧：它在 +dir 方向“离开路口”，在 −dir 方向“驶入路口”
            Vec3 forwardAnchor = center + right * offset;
            Vec3 backwardAnchor = center - right * offset;
            foreach (var (anchor, direction, outward) in new[]
            {
                (forwardAnchor, dir, true), (forwardAnchor, -dir, false),
                (backwardAnchor, -dir, true), (backwardAnchor, dir, false),
            })
                yield return new ExternalEdge
                {
                    Anchor = anchor, Direction = direction, Road = road, OneWay = true, TravelOutward = outward,
                    Width = width, LanesPerDirection = lanes, IsHighway = highway,
                };
        }

        private static List<ExternalEdge> Edges(params IEnumerable<ExternalEdge>[] roads)
        {
            var list = new List<ExternalEdge>();
            foreach (var r in roads)
                list.AddRange(r);
            return list;
        }

        [Fact]
        public void TwoWayCross_MatchesAnglesAndRoads()
        {
            var edges = Edges(TwoWayRoad(Vec3.Zero, 0f, "Large Road", 3), TwoWayRoad(Vec3.Zero, 90f, "Medium Road", 2));
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);

            Assert.Null(m.Error);
            Assert.Equal(4, m.LegCount);
            Assert.Equal(0f, m.MainAngle, 1);
            Assert.Equal(90f, m.CrossingAngle, 1);
            Assert.Equal("Large Road", m.Main!.Prefab);
            Assert.Equal(RoadForm.TwoWay, m.Main.Form);
            Assert.Equal("Medium Road", m.Cross!.Prefab);
        }

        [Fact]
        public void MainRoad_IsTheOneWithMoreLanes()
        {
            // 车道多的那条是主线，即使它离当前朝向远
            var edges = Edges(TwoWayRoad(Vec3.Zero, 0f, "Small Road", 1), TwoWayRoad(Vec3.Zero, 90f, "Large Road", 3));
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.Equal("Large Road", m.Main!.Prefab);
            Assert.True(Directions.AngleBetween(m.MainAngle, 90f) < 1f || Directions.AngleBetween(m.MainAngle, 270f) < 1f);
        }

        [Fact]
        public void SkewedCross_GivesCrossingAngleBetween0And180()
        {
            var edges = Edges(TwoWayRoad(Vec3.Zero, 10f, "Large Road", 3), TwoWayRoad(Vec3.Zero, 75f, "Medium Road", 2));
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.Null(m.Error);
            Assert.Equal(10f, m.MainAngle, 1);
            Assert.Equal(65f, m.CrossingAngle, 1);
        }

        [Fact]
        public void DualRoads_AreRecognisedWithMedian()
        {
            var edges = Edges(
                DualRoad(Vec3.Zero, 0f, "Highway Oneway - 3 lanes", 3, 16f, 14f),
                DualRoad(Vec3.Zero, 90f, "Highway Oneway - 2 lanes", 2, 12f, 6f));
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);

            Assert.Null(m.Error);
            Assert.Equal(RoadForm.Dual, m.Main!.Form);
            Assert.Equal("Highway Oneway - 3 lanes", m.Main.Prefab);
            Assert.Equal(14f, m.Main.MedianWidth, 1);
            Assert.Equal(RoadForm.Dual, m.Cross!.Form);
            Assert.Equal(6f, m.Cross.MedianWidth, 1);
            Assert.Equal(90f, m.CrossingAngle, 1);
        }

        [Fact]
        public void Center_IsWhereTheTwoRoadsCross()
        {
            var center = new Vec3(500f, 0f, -300f);
            var edges = Edges(
                DualRoad(center, 30f, "Highway Oneway - 3 lanes", 3, 16f, 14f),
                TwoWayRoad(center, 120f, "Medium Road", 2));
            var m = JunctionMatcher.Match(edges, preferredAngle: 30f);
            Assert.True(Vec3.Distance(m.Center, center) < 0.5f, $"center {m.Center}");
        }

        /// <summary>只保留沿 angle 方向离开路口的那一半（T 型路口的支路只有一个方向）。</summary>
        private static IEnumerable<ExternalEdge> OneSide(IEnumerable<ExternalEdge> road, float angle)
        {
            foreach (var e in road)
                if (Directions.AngleBetween(JunctionMatcher.AngleOf(e.Direction), angle) < 1f)
                    yield return e;
        }

        [Theory]
        [InlineData(90f)]    // 支路在右侧
        [InlineData(270f)]   // 支路在左侧
        [InlineData(60f)]
        public void TJunction_StraightRoadIsMainAndStemIsBranch(float branchAngle)
        {
            // 支路车道比主线多，也仍然是直行的那条做主线
            var edges = Edges(TwoWayRoad(Vec3.Zero, 0f, "Medium Road", 2), OneSide(TwoWayRoad(Vec3.Zero, branchAngle, "Large Road", 3), branchAngle));
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.Null(m.Error);
            Assert.Equal(3, m.LegCount);
            Assert.Equal("Medium Road", m.Main!.Prefab);
            Assert.Equal("Large Road", m.Cross!.Prefab);
            Assert.Equal(0f, m.MainAngle, 1);
            Assert.Equal(branchAngle, m.CrossingAngle, 1);
        }

        [Fact]
        public void TJunction_WithDualRoads_FindsCenterAndMedians()
        {
            var center = new Vec3(200f, 0f, 100f);
            var edges = Edges(
                DualRoad(center, 0f, "Highway Oneway - 3 lanes", 3, 16f, 10f),
                OneSide(DualRoad(center, 90f, "Highway Oneway - 2 lanes", 2, 12f, 6f), 90f));
            var m = JunctionMatcher.Match(edges, preferredAngle: 180f);
            Assert.Null(m.Error);
            Assert.Equal(3, m.LegCount);
            Assert.Equal(RoadForm.Dual, m.Main!.Form);
            Assert.Equal(10f, m.Main.MedianWidth, 1);
            Assert.Equal(RoadForm.Dual, m.Cross!.Form);
            Assert.Equal(6f, m.Cross.MedianWidth, 1);
            // 朝向 180° 时主线臂取 180°，支路在它顺时针 270° 处（即左侧）
            Assert.Equal(180f, m.MainAngle, 1);
            Assert.Equal(270f, m.CrossingAngle, 1);
            Assert.True(Vec3.Distance(m.Center, center) < 0.5f, $"center {m.Center}");
        }

        [Fact]
        public void TwoOneWayRoadsDrivingIntoTheSameNode_FailWithoutThrowing()
        {
            // 首轮核查第 7 条：两条单行道路相向驶入同一个节点时原来抛异常（游戏里每帧报错、预览不再更新）
            var edges = new List<ExternalEdge>
            {
                new ExternalEdge { Anchor = Vec3.Zero, Direction = Directions.FromAngle(0f), Road = "OW", OneWay = true, TravelOutward = false, Width = 8f, LanesPerDirection = 2 },
                new ExternalEdge { Anchor = Vec3.Zero, Direction = Directions.FromAngle(180f), Road = "OW", OneWay = true, TravelOutward = false, Width = 8f, LanesPerDirection = 2 },
            };
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.NotNull(m.Error);
        }

        [Fact]
        public void ThreeLegsWithoutStraightRoad_IsAWye()
        {
            // 三条臂互成 120°：没有直行的道路，认作 Y 型三岔路口（只有 Y 型分向能对准），给出三条臂的角度和道路
            var edges = Edges(
                OneSide(TwoWayRoad(Vec3.Zero, 0f, "A", 2), 0f),
                OneSide(TwoWayRoad(Vec3.Zero, 120f, "B", 2), 120f),
                OneSide(TwoWayRoad(Vec3.Zero, 240f, "C", 2), 240f));
            var m = JunctionMatcher.Match(edges, 130f);
            Assert.Equal(3, m.LegCount);
            Assert.Null(m.Error);
            Assert.True(m.Wye);
            Assert.Equal(120f, m.MainAngle, 1);
            Assert.Equal(new[] { 0f, 120f, 240f }, m.TripleLegs!.Select(l => (float)System.Math.Round(l.Angle)).ToArray());
            Assert.Equal(new[] { "A", "B", "C" }, m.TripleLegs!.Select(l => l.Road.Prefab).ToArray());
            Assert.True(Vec3.Distance(m.Center, Vec3.Zero) < 0.5f, $"center {m.Center}");
        }

        [Fact]
        public void LoneOneWayLeg_IsRejected()
        {
            var edges = new List<ExternalEdge>(TwoWayRoad(Vec3.Zero, 0f, "Large Road", 3));
            edges.Add(new ExternalEdge { Anchor = Vec3.Zero, Direction = Directions.FromAngle(90f), Road = "Small Road Oneway", OneWay = true, TravelOutward = true, Width = 16f, LanesPerDirection = 2 });
            edges.Add(new ExternalEdge { Anchor = Vec3.Zero, Direction = Directions.FromAngle(270f), Road = "Small Road Oneway", OneWay = true, TravelOutward = false, Width = 16f, LanesPerDirection = 2 });
            Assert.Contains("单向", JunctionMatcher.Match(edges, 0f).Error);
        }

        [Fact]
        public void PlainTwoWayRoad_AlignsMainAndCentersOnCursor()
        {
            // 一条沿 30° 的双向道路（不是路口）：主线对齐它，中心是鼠标在中心线上的投影
            var anchor = new Vec3(100f, 0f, 100f);
            var edges = new List<ExternalEdge>(TwoWayRoad(anchor, 30f, "Large Road", 3));
            var cursor = anchor + Directions.FromAngle(30f) * 50f + Directions.RightOf(Directions.FromAngle(30f)) * 7f;
            var m = JunctionMatcher.Match(edges, preferredAngle: 20f, cursor: cursor);

            Assert.Null(m.Error);
            Assert.True(m.RoadOnly);
            Assert.Null(m.Cross);
            Assert.Equal("Large Road", m.Main!.Prefab);
            Assert.Equal(30f, m.MainAngle, 1);
            var expected = anchor + Directions.FromAngle(30f) * 50f;
            Assert.True(Vec3.Distance(m.Center, new Vec3(expected.X, 0f, expected.Z)) < 0.01f, $"center {m.Center}");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SingleCarriageway_IsTreatedAsDualWithShiftedCenterline(bool rightHand)
        {
            // 指着一幅沿 +Z 行驶的单行道路：双幅中心线在它的左侧（右侧通行）或右侧（左侧通行）
            var anchor = Vec3.Zero;
            var edges = new List<ExternalEdge>
            {
                new ExternalEdge { Anchor = anchor, Direction = Directions.FromAngle(0f), Road = "Highway Oneway - 3 lanes", OneWay = true, TravelOutward = true, Width = 16f, LanesPerDirection = 3, IsHighway = true },
                new ExternalEdge { Anchor = anchor, Direction = Directions.FromAngle(180f), Road = "Highway Oneway - 3 lanes", OneWay = true, TravelOutward = false, Width = 16f, LanesPerDirection = 3, IsHighway = true },
            };
            var m = JunctionMatcher.Match(edges, 0f, cursor: anchor, dualMedian: 14f, rightHandTraffic: rightHand);

            Assert.True(m.RoadOnly);
            Assert.Equal(RoadForm.Dual, m.Main!.Form);
            Assert.Equal(14f, m.Main.MedianWidth);
            // 偏移 (14 + 16) / 2 = 15：右侧通行时中心线在 -X（这一幅的左边）
            Assert.Equal(rightHand ? -15f : 15f, m.Center.X, 2);
        }

        [Fact]
        public void LowestElevation_IsReported()
        {
            var edges = Edges(TwoWayRoad(Vec3.Zero, 0f, "Large Road", 3), TwoWayRoad(Vec3.Zero, 90f, "Medium Road", 2));
            edges[2].Elevation = -2f;
            Assert.Equal(-2f, JunctionMatcher.Match(edges, 0f).LowestElevation);
        }
    }
}
