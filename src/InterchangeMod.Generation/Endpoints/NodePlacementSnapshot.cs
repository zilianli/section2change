using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>
    /// Frozen exact source-corridor and cut identity. The original open endpoint is
    /// recorded separately from the displaced solver port. This is not game validation.
    /// </summary>
    public sealed class NodePlacementSnapshot
    {
        private NodePlacementSnapshot(string fingerprint) { Fingerprint = fingerprint; }
        public string Fingerprint { get; }
        public static bool SameEndpoint(SelectedEndpoint a, SelectedEndpoint b) => a.NodeIndex == b.NodeIndex
            && a.NodeVersion == b.NodeVersion && a.Position == b.Position && a.OutwardTangent == b.OutwardTangent
            && a.Prefab == b.Prefab && a.Lanes == b.Lanes && a.Width == b.Width
            && a.OneWay == b.OneWay && a.TravelOutward == b.TravelOutward;

        public static NodePlacementSnapshot Capture(IReadOnlyList<RoadApproachTrace> traces, IReadOnlyList<RoadRetreatResult> cuts)
        {
            if (traces == null || cuts == null || traces.Count == 0 || traces.Count != cuts.Count)
                throw new ArgumentException("Source corridors and cuts must have matching nonzero counts.");
            var data = new StringBuilder();
            data.Append(traces.Count).Append('|');
            var sources = new HashSet<(int, int)>();
            for (int i = 0; i < traces.Count; i++)
            {
                var trace = traces[i]; var cut = cuts[i];
                if (trace == null || trace.Origin == null || cut == null || cut.Port == null
                    || cut.Failure != RoadRetreatFailure.None || trace.Pieces.Count == 0
                    || !sources.Add((trace.Origin.NodeIndex, trace.Origin.NodeVersion)))
                    throw new ArgumentException("Invalid source or cut.");
                var recut = RoadRetreat.Cut(trace, cut.Distance);
                if (recut.Failure != RoadRetreatFailure.None || CutKey(recut) != CutKey(cut))
                    throw new ArgumentException("Cut does not match the original source corridor.");
                Endpoint(data, trace.Origin);
                Pieces(data, trace.Pieces);
                data.Append(CutKey(cut));
            }
            return new NodePlacementSnapshot(data.ToString());
        }
        public bool Matches(IReadOnlyList<RoadApproachTrace> traces, IReadOnlyList<RoadRetreatResult> cuts)
        {
            try { return Fingerprint == Capture(traces, cuts).Fingerprint; }
            catch (ArgumentException) { return false; }
        }
        private static void Number(StringBuilder b, float f) => b.Append(f.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        private static void Point(StringBuilder b, Vec3 p) { Number(b, p.X); Number(b, p.Y); Number(b, p.Z); }
        private static void Curve(StringBuilder b, Bezier3 c) { Point(b, c.P0); Point(b, c.P1); Point(b, c.P2); Point(b, c.P3); }
        private static void Endpoint(StringBuilder b, SelectedEndpoint p)
        {
            b.Append(p.NodeIndex).Append('|').Append(p.NodeVersion).Append('|');
            Point(b, p.Position); Point(b, p.OutwardTangent); Number(b, p.Width);
            b.Append(p.OneWay).Append('|').Append(p.TravelOutward).Append('|').Append(p.Lanes).Append('|')
                .Append(p.Prefab.Length).Append(':').Append(p.Prefab).Append('|');
        }
        private static void Pieces(StringBuilder b, IReadOnlyList<RoadTracePiece> pieces)
        {
            b.Append(pieces.Count).Append('|');
            foreach (var p in pieces)
            {
                b.Append(p.EdgeIndex).Append('|').Append(p.EdgeVersion).Append('|').Append(p.Reversed).Append('|')
                    .Append(p.OuterNodeIndex).Append('|').Append(p.OuterNodeVersion).Append('|');
                Curve(b, p.Curve);
            }
        }
        private static string CutKey(RoadRetreatResult cut)
        {
            var b = new StringBuilder();
            b.Append((int)cut.Failure).Append('|'); Number(b, cut.Distance);
            if (cut.Port != null) Endpoint(b, cut.Port);
            Pieces(b, cut.Consumed);
            b.Append(cut.ModifiedCurves.Count).Append('|');
            foreach (var c in cut.ModifiedCurves) Curve(b, c);
            b.Append(cut.Remainder.HasValue).Append('|');
            if (cut.Remainder is Bezier3 tail) Curve(b, tail);
            b.Append(cut.BindingNodeIndex).Append('|').Append(cut.BindingNodeVersion).Append('|');
            return b.ToString();
        }
    }
}
