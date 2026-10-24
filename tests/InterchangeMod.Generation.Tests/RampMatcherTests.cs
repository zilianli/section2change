using System.Collections.Generic;
using InterchangeMod.Generation.Roads;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class RampMatcherTests
    {
        // 名字取自游戏日志里的真实道路；车道数和宽度是假设值，只用于检验匹配规则
        private static readonly List<RoadInfo> Catalog = new List<RoadInfo>
        {
            new RoadInfo("Small Road", false, 1, 1, 16f),
            new RoadInfo("Small Road Oneway - 1 lane", false, 1, 0, 12f),
            new RoadInfo("Small Road Oneway", false, 2, 0, 16f),
            new RoadInfo("Small Road Oneway - Single Sided Parking", false, 2, 0, 20f),
            new RoadInfo("Small Road Oneway - 3 lanes", false, 3, 0, 20f),
            new RoadInfo("Medium Road", false, 2, 2, 24f),
            new RoadInfo("Medium Road Oneway", false, 4, 0, 24f),
            new RoadInfo("Large Road Oneway", false, 6, 0, 32f),
            new RoadInfo("Alley Oneway", false, 1, 0, 8f),
            new RoadInfo("Oneway Public Transport Lane", false, 0, 0, 12f, publicTransportLanes: 1),
            new RoadInfo("Highway Oneway - 1 lane", true, 1, 0, 12f),
            new RoadInfo("Highway Oneway - 2 lanes", true, 2, 0, 16f),
            new RoadInfo("Highway Oneway - 3 lanes", true, 3, 0, 20f),
            new RoadInfo("Highway Oneway - 4 lanes", true, 4, 0, 24f),
            new RoadInfo("Suspension Bridge - Highway Oneway - 2 Lanes", true, 2, 0, 16f),
            new RoadInfo("bbf49461f-8cd8-44df-9359-81dc361b37ee", false, 2, 0, 16f),
        };

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 2)]
        [InlineData(6, 3)]
        public void DefaultExitLanes_IsHalfOfMainLanes(int mainLanes, int expected)
        {
            Assert.Equal(expected, RampMatcher.DefaultExitLanes(mainLanes));
        }

        [Fact]
        public void HighwayMain_GetsHighwayRampWithMatchingLanes()
        {
            Assert.Equal("Highway Oneway - 2 lanes", RampMatcher.PickRamp(Catalog, highway: true, targetLanes: 2)!.Name);
            Assert.Equal("Highway Oneway - 1 lane", RampMatcher.PickRamp(Catalog, highway: true, targetLanes: 1)!.Name);
        }

        [Fact]
        public void StreetMain_GetsPlainStreetOneway()
        {
            // 2 车道有两个候选，选更窄的、不带停车的那条
            Assert.Equal("Small Road Oneway", RampMatcher.PickRamp(Catalog, highway: false, targetLanes: 2)!.Name);
        }

        [Fact]
        public void NoExactMatch_PrefersMoreLanes()
        {
            // 街道里没有 5 车道单行：4 和 6 一样接近，选 6（匝道不做瓶颈）
            Assert.Equal("Large Road Oneway", RampMatcher.PickRamp(Catalog, highway: false, targetLanes: 5)!.Name);
        }

        [Fact]
        public void SpecialRoads_AreNeverPicked()
        {
            var bridges = new List<RoadInfo>
            {
                new RoadInfo("Suspension Bridge - Highway Oneway - 2 Lanes", true, 2, 0, 16f),
                new RoadInfo("Alley Oneway", false, 1, 0, 8f),
                new RoadInfo("Oneway Public Transport Lane", false, 1, 0, 12f, publicTransportLanes: 1),
                new RoadInfo("bbf49461f-8cd8-44df-9359-81dc361b37ee", false, 2, 0, 16f),
            };
            Assert.Null(RampMatcher.PickRamp(bridges, highway: true, targetLanes: 2));
        }

        [Fact]
        public void TwoWayRoads_AreNeverPicked()
        {
            var twoWay = new List<RoadInfo> { new RoadInfo("Medium Road", false, 2, 2, 24f) };
            Assert.Null(RampMatcher.PickRamp(twoWay, highway: false, targetLanes: 2));
        }

        [Fact]
        public void MissingCategory_FallsBackToOtherType()
        {
            var streetsOnly = new List<RoadInfo> { new RoadInfo("Small Road Oneway", false, 2, 0, 16f) };
            Assert.Equal("Small Road Oneway", RampMatcher.PickRamp(streetsOnly, highway: true, targetLanes: 2)!.Name);
        }

        [Fact]
        public void PickClosest_FindsOneWayCounterpartOfTwoWayRoad()
        {
            // Medium Road 每方向 2 车道 → 建双幅时每幅用 2 车道的街道单行道路
            Assert.Equal("Small Road Oneway", RampMatcher.PickClosest(Catalog, highway: false, lanesPerDirection: 2, oneWay: true)!.Name);
        }

        [Fact]
        public void PickClosest_FindsSymmetricTwoWayCounterpartOfOneWayRoad()
        {
            var roads = new List<RoadInfo>(Catalog)
            {
                new RoadInfo("Large Road", false, 3, 3, 32f),
                new RoadInfo("Large Road Asymmetric - 2+4 lanes", false, 4, 2, 32f),
            };
            // Medium Road Oneway 4 车道 → 双向道路里最接近的是对称的 Large Road（3+3），不选不对称道路
            Assert.Equal("Large Road", RampMatcher.PickClosest(roads, highway: false, lanesPerDirection: 4, oneWay: false)!.Name);
            Assert.Equal("Medium Road", RampMatcher.PickClosest(roads, highway: false, lanesPerDirection: 2, oneWay: false)!.Name);
        }

        [Fact]
        public void LanesPerDirection_HandlesOneWayAndAsymmetricRoads()
        {
            Assert.Equal(4, new RoadInfo("Medium Road Oneway", false, 4, 0, 24f).LanesPerDirection);
            Assert.Equal(3, new RoadInfo("Reversed Oneway", false, 0, 3, 20f).LanesPerDirection);
            Assert.Equal(2, new RoadInfo("Large Road Asymmetric - 2+4 lanes", false, 4, 2, 32f).LanesPerDirection);
            Assert.False(new RoadInfo("Medium Road", false, 2, 2, 24f).IsOneWay);
        }
    }

    public class RecentListTests
    {
        [Fact]
        public void Add_MovesItemToFrontAndKeepsCapacity()
        {
            var list = new RecentList(capacity: 3);
            list.Add("A");
            list.Add("B");
            list.Add("C");
            list.Add("A");
            list.Add("D");
            Assert.Equal(new[] { "D", "A", "C" }, list.Items);
        }

        [Fact]
        public void Add_ReportsWhetherListChanged()
        {
            var list = new RecentList();
            Assert.True(list.Add("A"));
            Assert.False(list.Add("A"));
            Assert.False(list.Add(""));
        }

        [Fact]
        public void Serialize_RoundTripsOrder()
        {
            var list = new RecentList();
            list.Add("Small Road Oneway");
            list.Add("Highway Oneway - 2 lanes");
            var copy = RecentList.Deserialize(list.Serialize());
            Assert.Equal(list.Items, copy.Items);
            Assert.Empty(RecentList.Deserialize(null).Items);
        }
    }
}
