using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointTraceDumpTests
    {
        private static RoadApproachTrace CurvedTrace(int index = 1, string prefab = "Highway")
        {
            var origin = new SelectedEndpoint(index, 3,
                new Vec3(12345.678f, -0.000123456789f, -9876.543f),
                new Vec3(0.9876543f, float.Epsilon, -0.123456789f),
                true, false, 23.456789f, 4, prefab);
            var first = new Bezier3(origin.Position,
                new Vec3(12456.789f, 12.345678f, -9854.321f),
                new Vec3(12578.901f, 23.456789f, -9789.012f),
                new Vec3(12678.901f, 21.234567f, -9678.901f));
            var second = new Bezier3(first.P3,
                new Vec3(12789.012f, 19.876543f, -9567.891f),
                new Vec3(12890.123f, 18.765432f, -9456.789f),
                new Vec3(12901.234f, 17.654321f, -9345.678f));
            return new RoadApproachTrace(origin, new[]
            {
                new RoadTracePiece(110, 7, first, false, 120, 8),
                new RoadTracePiece(121, 9, second, true, 130, 10)
            });
        }

        // Rounding, locale-sensitive numbers, omitted metadata, or unescaped names
        // prevent a captured game curve from being reproduced exactly.
        [Fact]
        public void RoundTripsEveryFieldAndControlPointUnderCommaDecimalCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var trace = CurvedTrace(prefab: "中文道路 \"quoted\" \\ 新线\n\r\t\b\f\u0001");
                var line = Assert.Single(EndpointTraceDump.Encode(new[] { trace }));
                Assert.DoesNotContain("\n", line);
                Assert.DoesNotContain("\r", line);
                using var parsed = JsonDocument.Parse(line);
                var root = parsed.RootElement;
                Assert.Equal("autochange-road-approach-trace", root.GetProperty("schema").GetString());
                Assert.Equal(1, root.GetProperty("version").GetInt32());
                var origin = root.GetProperty("origin");
                Assert.Equal(trace.Origin.NodeIndex, origin.GetProperty("nodeIndex").GetInt32());
                Assert.Equal(trace.Origin.NodeVersion, origin.GetProperty("nodeVersion").GetInt32());
                AssertVec(trace.Origin.Position, origin.GetProperty("position"));
                AssertVec(trace.Origin.OutwardTangent, origin.GetProperty("outwardTangent"));
                Assert.True(origin.GetProperty("oneWay").GetBoolean());
                Assert.False(origin.GetProperty("travelOutward").GetBoolean());
                AssertFloat(trace.Origin.Width, origin.GetProperty("width"));
                Assert.Equal(4, origin.GetProperty("lanes").GetInt32());
                Assert.Equal(trace.Origin.Prefab, origin.GetProperty("prefab").GetString());
                var pieces = root.GetProperty("pieces");
                Assert.Equal(2, pieces.GetArrayLength());
                for (int i = 0; i < trace.Pieces.Count; i++)
                {
                    var expected = trace.Pieces[i];
                    var actual = pieces[i];
                    Assert.Equal(expected.EdgeIndex, actual.GetProperty("edgeIndex").GetInt32());
                    Assert.Equal(expected.EdgeVersion, actual.GetProperty("edgeVersion").GetInt32());
                    Assert.Equal(expected.Reversed, actual.GetProperty("reversed").GetBoolean());
                    Assert.Equal(expected.OuterNodeIndex, actual.GetProperty("outerNodeIndex").GetInt32());
                    Assert.Equal(expected.OuterNodeVersion, actual.GetProperty("outerNodeVersion").GetInt32());
                    var curve = actual.GetProperty("curve");
                    AssertVec(expected.Curve.P0, curve.GetProperty("p0"));
                    AssertVec(expected.Curve.P1, curve.GetProperty("p1"));
                    AssertVec(expected.Curve.P2, curve.GetProperty("p2"));
                    AssertVec(expected.Curve.P3, curve.GetProperty("p3"));
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Fact]
        public void EmitsOneIndependentObjectPerTraceAndAcceptsEmptyCollections()
        {
            Assert.Empty(EndpointTraceDump.Encode(Array.Empty<RoadApproachTrace>()));
            var traces = Enumerable.Range(1, 8).Select(i => CurvedTrace(i)).ToArray();
            var rows = EndpointTraceDump.Encode(traces);
            Assert.Equal(8, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                using var parsed = JsonDocument.Parse(rows[i]);
                Assert.Equal(i + 1, parsed.RootElement.GetProperty("origin").GetProperty("nodeIndex").GetInt32());
            }
            var empty = new RoadApproachTrace(traces[0].Origin, Array.Empty<RoadTracePiece>());
            using var noPieces = JsonDocument.Parse(Assert.Single(EndpointTraceDump.Encode(new[] { empty })));
            Assert.Equal(0, noPieces.RootElement.GetProperty("pieces").GetArrayLength());
        }

        [Fact]
        public void EncodingIsStableWithoutChangingTheInputObjects()
        {
            var trace = CurvedTrace();
            var origin = trace.Origin;
            var pieces = trace.Pieces;
            var first = trace.Pieces[0];
            var second = trace.Pieces[1];
            var originalP1 = first.Curve.P1;
            var input = new[] { trace };
            var once = EndpointTraceDump.Encode(input);
            Assert.Equal(once, EndpointTraceDump.Encode(input));
            Assert.Same(trace, input[0]);
            Assert.Same(origin, trace.Origin);
            Assert.Same(pieces, trace.Pieces);
            Assert.Same(first, trace.Pieces[0]);
            Assert.Same(second, trace.Pieces[1]);
            Assert.Equal(originalP1, first.Curve.P1);

            // Same node identities but altered curve/metadata must not be deduplicated.
            var c = first.Curve;
            var altered = new RoadApproachTrace(origin, new[]
            {
                new RoadTracePiece(first.EdgeIndex, first.EdgeVersion,
                    new Bezier3(c.P0, new Vec3(c.P1.X + 0.125f, c.P1.Y, c.P1.Z), c.P2, c.P3),
                    first.Reversed, first.OuterNodeIndex, first.OuterNodeVersion), second
            });
            Assert.NotEqual(once[0], EndpointTraceDump.Encode(new[] { altered })[0]);
            var renamedOrigin = new SelectedEndpoint(origin.NodeIndex, origin.NodeVersion, origin.Position,
                origin.OutwardTangent, origin.OneWay, origin.TravelOutward, origin.Width, origin.Lanes, "Changed road");
            Assert.NotEqual(once[0], EndpointTraceDump.Encode(new[] { new RoadApproachTrace(renamedOrigin, pieces) })[0]);
        }

        [Fact]
        public void RejectsNullTracesPiecesAndNonFiniteNumbersInsteadOfEmittingInvalidJson()
        {
            Assert.Throws<ArgumentNullException>(() => EndpointTraceDump.Encode(null!));
            Assert.Throws<ArgumentException>(() => EndpointTraceDump.Encode(new RoadApproachTrace[] { null! }));
            var trace = CurvedTrace();
            Assert.Throws<ArgumentException>(() => EndpointTraceDump.Encode(new[]
                { new RoadApproachTrace(null!, Array.Empty<RoadTracePiece>()) }));
            Assert.Throws<ArgumentException>(() => EndpointTraceDump.Encode(new[]
                { new RoadApproachTrace(trace.Origin, new RoadTracePiece[] { null! }) }));
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var bad = new RoadTracePiece(110, 7, new Bezier3(trace.Origin.Position,
                    new Vec3(invalid, 0, 0), Vec3.UnitX, Vec3.UnitZ), false, 120, 8);
                Assert.Throws<ArgumentException>(() => EndpointTraceDump.Encode(new[]
                    { new RoadApproachTrace(trace.Origin, new[] { bad }) }));
            }
        }

        private static void AssertVec(Vec3 expected, JsonElement actual)
        {
            AssertFloat(expected.X, actual.GetProperty("x"));
            AssertFloat(expected.Y, actual.GetProperty("y"));
            AssertFloat(expected.Z, actual.GetProperty("z"));
        }

        private static void AssertFloat(float expected, JsonElement actual) =>
            Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual.GetSingle()));
    }
}
