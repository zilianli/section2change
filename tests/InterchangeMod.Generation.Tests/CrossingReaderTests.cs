using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Junctions;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CrossingReaderTests
    {
        [Fact]
        public void Intersect_FindsCrossingParameters()
        {
            var a = Bezier3.Line(new Vec3(-100, 0, 0), new Vec3(100, 0, 0));
            var b = Bezier3.Line(new Vec3(30, 0, -50), new Vec3(30, 0, 150));
            Assert.True(CrossingReader.Intersect(a, b, out float ta, out float tb));
            Assert.Equal(0.65f, ta, 3);
            Assert.Equal(0.25f, tb, 3);
            Assert.False(CrossingReader.Intersect(a, Bezier3.Line(new Vec3(-100, 0, 10), new Vec3(100, 0, 10)), out _, out _));
        }

        [Fact]
        public void DualHighwayOverStreet_MatchesAsFourWayCrossing()
        {
            // 双幅公路（东西向，两幅中心线相距 20 米，各 12 米宽 → 分隔带 8 米）高架 10 米，跨过南北向的双向街道
            var east = new CrossingRoad { Curve = Bezier3.Line(new Vec3(-200, 0, -10), new Vec3(200, 0, -10)), Road = "Highway Oneway", OneWay = true, Forward = true, Width = 12f, LanesPerDirection = 3, IsHighway = true };
            var west = new CrossingRoad { Curve = Bezier3.Line(new Vec3(-200, 0, 10), new Vec3(200, 0, 10)), Road = "Highway Oneway", OneWay = true, Forward = false, Width = 12f, LanesPerDirection = 3, IsHighway = true };
            var street = new CrossingRoad { Curve = Bezier3.Line(new Vec3(5, 0, -150), new Vec3(5, 0, 150)), Road = "Street", OneWay = false, Width = 16f, LanesPerDirection = 2 };

            var edges = new List<ExternalEdge>();
            foreach (var highway in new[] { east, west })
            {
                Assert.True(CrossingReader.Intersect(highway.Curve, street.Curve, out float th, out float ts));
                edges.AddRange(CrossingReader.Split(highway, th, 10f));
                edges.AddRange(CrossingReader.Split(street, ts, 0f));
            }

            var match = JunctionMatcher.Match(edges, preferredAngle: 90f);
            Assert.Null(match.Error);
            Assert.Equal(4, match.LegCount);
            Assert.Equal(RoadForm.Dual, match.Main!.Form);
            Assert.Equal(8f, match.Main.MedianWidth, 3);
            Assert.Equal(10f, match.Main.Elevation, 3);
            Assert.Equal(RoadForm.TwoWay, match.Cross!.Form);
            Assert.Equal(0f, match.Cross.Elevation, 3);
            Assert.Equal(90f, match.CrossingAngle, 1);
            Assert.Equal(5f, match.Center.X, 3);
            Assert.Equal(0f, match.Center.Z, 3);
        }

        [Fact]
        public void StreetEndingJustPastViaduct_MatchesAsTee()
        {
            // 双幅高架（东西向）下，街道从南边过来，只伸过高架一点就到头：伸过去的那一小半不算臂 → 丁字路口
            var east = new CrossingRoad { Curve = Bezier3.Line(new Vec3(-200, 0, -10), new Vec3(200, 0, -10)), Road = "Highway Oneway", OneWay = true, Forward = true, Width = 12f, LanesPerDirection = 3, IsHighway = true };
            var west = new CrossingRoad { Curve = Bezier3.Line(new Vec3(-200, 0, 10), new Vec3(200, 0, 10)), Road = "Highway Oneway", OneWay = true, Forward = false, Width = 12f, LanesPerDirection = 3, IsHighway = true };
            var street = new CrossingRoad { Curve = Bezier3.Line(new Vec3(0, 0, -150), new Vec3(0, 0, 15)), Road = "Street", OneWay = false, Width = 16f, LanesPerDirection = 2 };

            var edges = new List<ExternalEdge>();
            foreach (var highway in new[] { east, west })
            {
                Assert.True(CrossingReader.Intersect(highway.Curve, street.Curve, out float th, out float ts));
                edges.AddRange(CrossingReader.Split(highway, th, 10f));
                // 街道沿曲线方向（向北）只剩几米就到头：只要向南的那一半
                edges.AddRange(CrossingReader.Split(street, ts, 0f, includeForward: false));
            }

            var match = JunctionMatcher.Match(edges, preferredAngle: 90f);
            Assert.Null(match.Error);
            Assert.Equal(3, match.LegCount);
            Assert.Equal(RoadForm.Dual, match.Main!.Form);
            Assert.Equal("Street", match.Cross!.Prefab);
        }
    }
}
