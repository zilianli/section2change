using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public enum SpatialCandidateFailure
    {
        None, InvalidSelection, StalePlanar, PlanarFailure, InvalidParameter, GradeExceeded, InvalidGeometry, Uncertain, MergeBoundaryConflict, PillarConflict,
    }

    public sealed class SpatialSegment
    {
        internal SpatialSegment(int sourceArm, CloverEdgeKind kind, Bezier3 worldCurve,
            Vec3? startNode=null,Vec3? endNode=null,string? prefab=null,float? roadWidth=null,bool isWeaving=false,string pathId="")
        {
            SourceArm = sourceArm; Kind = kind; WorldCurve = worldCurve;
            StartNode=startNode??worldCurve.P0;EndNode=endNode??worldCurve.P3;Prefab=prefab;RoadWidth=roadWidth;
            IsWeaving=isWeaving;PathId=pathId;
        }
        public int SourceArm { get; }
        public CloverEdgeKind Kind { get; }
        /// <summary>World-space Y, not the original generation layer's height above terrain.</summary>
        public Bezier3 WorldCurve { get; }
        public Vec3 StartNode { get; }
        public Vec3 EndNode { get; }
        public string? Prefab { get; }
        public float? RoadWidth { get; }
        public bool IsWeaving { get; }
        public string PathId { get; }
    }

    public sealed class SpatialCandidateResult
    {
        internal SpatialCandidateResult(SpatialCandidateFailure failure, IEnumerable<SpatialSegment> segments,string diagnostic="")
        {
            Failure = failure; Segments = Array.AsReadOnly(segments.ToArray());
            Diagnostic=diagnostic;
        }
        public SpatialCandidateFailure Failure { get; }
        public IReadOnlyList<SpatialSegment> Segments { get; }
        public string Diagnostic { get; }
        /// <summary>No road width, clearance, terrain, pillar or game validation has been performed.</summary>
        public bool IsPlacementReady => false;
    }

    /// <summary>Combines planar paths and grade-limited world-height profiles into an unvalidated skeleton.</summary>
    public static class SpatialCloverleafCandidate
    {
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static float OutwardGrade(SelectedEndpoint p)
        {
            double horizontal = Math.Sqrt((double)p.OutwardTangent.X * p.OutwardTangent.X + (double)p.OutwardTangent.Z * p.OutwardTangent.Z);
            return (float)(p.OutwardTangent.Y / horizontal);
        }
        private static Vec3 AtHeight(Vec3 position, float height) => new Vec3(position.X, height, position.Z);

        private static bool SamePort(SelectedEndpoint a, SelectedEndpoint b) =>
            a.NodeIndex == b.NodeIndex && a.NodeVersion == b.NodeVersion && a.Position.Equals(b.Position)
            && a.OutwardTangent.Equals(b.OutwardTangent) && a.OneWay == b.OneWay && a.TravelOutward == b.TravelOutward
            && a.Width.Equals(b.Width) && a.Lanes == b.Lanes && string.Equals(a.Prefab, b.Prefab, StringComparison.Ordinal);

        private static bool SameSnapshot(IReadOnlyList<EndpointArm> current, IReadOnlyList<EndpointArm> original)
        {
            if (current.Count != original.Count) return false;
            foreach (var arm in current)
            {
                var prior = original.FirstOrDefault(a => a.Index == arm.Index);
                if (prior == null || arm.Endpoints.Count != prior.Endpoints.Count) return false;
                foreach (var port in arm.Endpoints)
                {
                    var matching = prior.Endpoints.FirstOrDefault(p => p.NodeIndex == port.NodeIndex && p.NodeVersion == port.NodeVersion);
                    if (matching == null || !SamePort(port, matching)) return false;
                }
            }
            return true;
        }

        public static SpatialCandidateResult Create(IReadOnlyList<EndpointArm> arms, PlanarCandidateResult planar,
            float mainHeight, float crossHeight, float rampFlatLength, float maxGrade)
        {
            if (arms == null) throw new ArgumentNullException(nameof(arms));
            if (planar == null) throw new ArgumentNullException(nameof(planar));
            SpatialCandidateResult Fail(SpatialCandidateFailure code) => new SpatialCandidateResult(code, Array.Empty<SpatialSegment>());
            var selected = arms.ToArray();
            if (EndpointConstraints.Validate(selected).Count != 0) return Fail(SpatialCandidateFailure.InvalidSelection);
            if (planar.Failure != PlanarCandidateFailure.None) return Fail(SpatialCandidateFailure.PlanarFailure);
            if (!SameSnapshot(selected, planar.InputArms)) return Fail(SpatialCandidateFailure.StalePlanar);
            if (!Finite(mainHeight) || !Finite(crossHeight) || !Finite(rampFlatLength) || rampFlatLength < 0
                || !Finite(maxGrade) || maxGrade <= 0 || maxGrade > 0.30f)
                return Fail(SpatialCandidateFailure.InvalidParameter);

            SpatialCandidateFailure ConvertFailure(VerticalProfileFailure failure) => failure switch
            {
                VerticalProfileFailure.None => SpatialCandidateFailure.None,
                VerticalProfileFailure.InvalidInput => SpatialCandidateFailure.InvalidParameter,
                VerticalProfileFailure.InvalidGeometry => SpatialCandidateFailure.InvalidGeometry,
                VerticalProfileFailure.GradeExceeded => SpatialCandidateFailure.GradeExceeded,
                VerticalProfileFailure.InsufficientLength => SpatialCandidateFailure.GradeExceeded,
                _ => SpatialCandidateFailure.Uncertain,
            };
            float Height(int arm) => arm % 2 == 0 ? mainHeight : crossHeight;
            float RampHeight(int arm, float? factor) => factor.HasValue
                ? mainHeight + (crossHeight - mainHeight) * factor.Value : Height(arm);
            var sorted = selected.OrderBy(a => a.Index).ToArray();
            var segments = new List<SpatialSegment>();
            foreach (var line in planar.Lines)
            {
                int i = line.SourceArm, opposite = (i + 2) % 4;
                var incoming = sorted[i].Endpoints.First(p => !p.TravelOutward);
                var outgoing = sorted[opposite].Endpoints.First(p => p.TravelOutward);
                var balance=planar.Design?.Balances.FirstOrDefault(b=>b.Arm==i);
                SpatialSegment Main(Bezier3 curve,bool reduced=false,bool weaving=false)
                {
                    var direction=Vec3.Normalize(new Vec3(line.End.X-line.Start.X,0,line.End.Z-line.Start.Z));
                    var outer=new Vec3(direction.Z,0,-direction.X)*(planar.RightHandTraffic?1:-1);
                    var offset=reduced&&balance!=null?-outer*((incoming.Width-balance.Width)/2):Vec3.Zero;
                    var actual=new Bezier3(curve.P0+offset,curve.P1+offset,curve.P2+offset,curve.P3+offset);
                    return new SpatialSegment(i,CloverEdgeKind.Main,actual,curve.P0,curve.P3,
                        reduced&&balance!=null?balance.Prefab:incoming.Prefab,reduced&&balance!=null?balance.Width:incoming.Width,weaving);
                }
                float gradeIn = OutwardGrade(incoming), gradeOut = OutwardGrade(outgoing);
                float startHeight = incoming.Position.Y + planar.RetreatByArm[i] * gradeIn;
                float endHeight = outgoing.Position.Y + planar.RetreatByArm[opposite] * gradeOut;
                if (!Finite(startHeight) || !Finite(endHeight) || !Finite(gradeIn) || !Finite(gradeOut))
                    return Fail(SpatialCandidateFailure.InvalidGeometry);

                var junctions = line.Junctions.Select(j => j.Position).ToArray();
                var lineDirection=new Vec3(line.End.X-line.Start.X,0,line.End.Z-line.Start.Z);
                var approachIn=planar.Design?.ApproachTransitions==true
                    ?Bezier3.FromTangents(line.Start,-incoming.OutwardTangent,junctions[0],lineDirection,Vec3.Distance(line.Start,junctions[0])/3)
                    :Bezier3.Line(line.Start,junctions[0]);
                var first = VerticalProfile.Apply(new[] { approachIn }, startHeight, Height(i), -gradeIn, 0, maxGrade);
                var status = ConvertFailure(first.Failure);
                if (status != SpatialCandidateFailure.None) return Fail(status);
                segments.AddRange(first.Curves.Select(c => Main(c)));
                for (int j = 0; j + 1 < junctions.Length; j++)
                    segments.Add(Main(Bezier3.Line(AtHeight(junctions[j],Height(i)),AtHeight(junctions[j+1],Height(i))),j==0||j==2,j==1));
                var approachOut=planar.Design?.ApproachTransitions==true
                    ?Bezier3.FromTangents(junctions[junctions.Length-1],lineDirection,line.End,outgoing.OutwardTangent,Vec3.Distance(junctions[junctions.Length-1],line.End)/3)
                    :Bezier3.Line(junctions[junctions.Length-1],line.End);
                var last = VerticalProfile.Apply(new[] { approachOut }, Height(i), endHeight, 0, gradeOut, maxGrade);
                status = ConvertFailure(last.Failure);
                if (status != SpatialCandidateFailure.None) return Fail(status);
                segments.AddRange(last.Curves.Select(c => Main(c)));
            }
            foreach (var ramp in planar.Ramps)
            {
                if ((ramp.RequiredFlatStart.HasValue && rampFlatLength > ramp.RequiredFlatStart.Value + 1e-3f)
                    || (ramp.RequiredFlatEnd.HasValue && rampFlatLength > ramp.RequiredFlatEnd.Value + 1e-3f))
                    return Fail(SpatialCandidateFailure.MergeBoundaryConflict);
                float flatStart = Math.Max(rampFlatLength, ramp.FlatStartLength ?? ramp.RequiredFlatStart ?? 0);
                float flatEnd = Math.Max(rampFlatLength, ramp.FlatEndLength ?? ramp.RequiredFlatEnd ?? 0);
                float startHeight = RampHeight(ramp.SourceArm, ramp.StartLevelFactor);
                float endHeight = RampHeight(ramp.TargetArm, ramp.EndLevelFactor);
                var profile = VerticalProfile.ApplyWithBoundaryGrades(ramp.Curves, startHeight, endHeight,
                    flatStart, flatEnd,(crossHeight-mainHeight)*ramp.StartGradeFactor,
                    (crossHeight-mainHeight)*ramp.EndGradeFactor,maxGrade);
                var status = ConvertFailure(profile.Failure);
                if (status != SpatialCandidateFailure.None) return Fail(status);
                for(int n=0;n<profile.Curves.Count;n++)
                {
                    var c=profile.Curves[n];
                    segments.Add(new SpatialSegment(ramp.SourceArm,ramp.Kind,c,
                        n==0?AtHeight(ramp.StartNode,startHeight):c.P0,
                        n+1==profile.Curves.Count?AtHeight(ramp.EndNode,endHeight):c.P3,
                        prefab:ramp.Prefab,roadWidth:ramp.RoadWidth??planar.RampWidth,pathId:ramp.PathId));
                }
            }
            var result=new SpatialCandidateResult(SpatialCandidateFailure.None,segments);
            return planar.Design==null?result:CloverleafBridgePlanner.Apply(result,planar.Design.BridgeRules);
        }
    }
}
