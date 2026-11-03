using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Lossless, local diagnostic records of the road curves used by endpoint search.</summary>
    public static class EndpointTraceDump
    {
        public static IReadOnlyList<string> Encode(IReadOnlyList<RoadApproachTrace> traces)
        {
            if (traces == null) throw new ArgumentNullException(nameof(traces));
            var rows = new string[traces.Count];
            for (int i = 0; i < traces.Count; i++)
            {
                var trace = traces[i];
                if (trace == null || trace.Origin == null)
                    throw new ArgumentException("A trace and its origin must not be null.", nameof(traces));
                var origin = trace.Origin;
                if (origin.NodeIndex <= 0 || origin.NodeVersion <= 0 || origin.Width <= 0 || origin.Lanes <= 0
                    || string.IsNullOrWhiteSpace(origin.Prefab))
                    throw new ArgumentException("A trace origin has invalid road metadata.", nameof(traces));

                var b = new StringBuilder();
                b.Append("{\"schema\":\"autochange-road-approach-trace\",\"version\":1,\"origin\":{\"nodeIndex\":");
                Integer(b, origin.NodeIndex);
                b.Append(",\"nodeVersion\":"); Integer(b, origin.NodeVersion);
                b.Append(",\"position\":"); Vector(b, origin.Position);
                b.Append(",\"outwardTangent\":"); Vector(b, origin.OutwardTangent);
                b.Append(",\"oneWay\":").Append(origin.OneWay ? "true" : "false");
                b.Append(",\"travelOutward\":").Append(origin.TravelOutward ? "true" : "false");
                b.Append(",\"width\":"); Number(b, origin.Width);
                b.Append(",\"lanes\":"); Integer(b, origin.Lanes);
                b.Append(",\"prefab\":"); Quoted(b, origin.Prefab);
                b.Append("},\"pieces\":[");
                for (int pieceIndex = 0; pieceIndex < trace.Pieces.Count; pieceIndex++)
                {
                    var piece = trace.Pieces[pieceIndex];
                    if (piece == null)
                        throw new ArgumentException("A trace piece must not be null.", nameof(traces));
                    if (piece.EdgeIndex <= 0 || piece.EdgeVersion <= 0
                        || piece.OuterNodeIndex <= 0 || piece.OuterNodeVersion <= 0)
                        throw new ArgumentException("A trace piece has invalid entity metadata.", nameof(traces));
                    if (pieceIndex != 0) b.Append(',');
                    b.Append("{\"edgeIndex\":"); Integer(b, piece.EdgeIndex);
                    b.Append(",\"edgeVersion\":"); Integer(b, piece.EdgeVersion);
                    b.Append(",\"reversed\":").Append(piece.Reversed ? "true" : "false");
                    b.Append(",\"outerNodeIndex\":"); Integer(b, piece.OuterNodeIndex);
                    b.Append(",\"outerNodeVersion\":"); Integer(b, piece.OuterNodeVersion);
                    b.Append(",\"curve\":{\"p0\":"); Vector(b, piece.Curve.P0);
                    b.Append(",\"p1\":"); Vector(b, piece.Curve.P1);
                    b.Append(",\"p2\":"); Vector(b, piece.Curve.P2);
                    b.Append(",\"p3\":"); Vector(b, piece.Curve.P3);
                    b.Append("}}");
                }
                b.Append("]}");
                rows[i] = b.ToString();
            }
            return Array.AsReadOnly(rows);
        }

        private static void Integer(StringBuilder b, int value) =>
            b.Append(value.ToString(CultureInfo.InvariantCulture));

        private static void Number(StringBuilder b, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Trace coordinates and widths must be finite.", "traces");
            b.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Vector(StringBuilder b, Vec3 value)
        {
            b.Append("{\"x\":"); Number(b, value.X);
            b.Append(",\"y\":"); Number(b, value.Y);
            b.Append(",\"z\":"); Number(b, value.Z);
            b.Append('}');
        }

        private static void Quoted(StringBuilder b, string value)
        {
            b.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ' || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029')
                            b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
    }
}
