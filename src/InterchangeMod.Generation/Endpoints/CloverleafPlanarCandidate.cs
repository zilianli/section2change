using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    public enum PlanarCandidateFailure
    {
        None, InvalidSelection, UnsupportedRoadForm, NonStraightApproach, InvalidRadius,
        DegenerateCrossing, AttachmentsOutsideApproach, ConflictingAttachmentOrder, InvalidRetreat,
        InvalidRoadWidth, MergeCannotClear, MergeOverlapConflict,
    }

    public sealed class PlanarRamp
    {
        internal PlanarRamp(int sourceArm, int targetArm, CloverEdgeKind kind, float startAlong, float endAlong,
            float sweepRadians, IEnumerable<Bezier3> curves, float? requiredFlatStart = null, float? requiredFlatEnd = null,
            Vec3? startNode=null,Vec3? endNode=null,string pathId="",string? prefab=null,float? roadWidth=null,
            float? startLevelFactor=null,float? endLevelFactor=null,float? flatStartLength=null,float? flatEndLength=null,
            float startGradeFactor=0,float endGradeFactor=0)
        {
            SourceArm = sourceArm; TargetArm = targetArm; Kind = kind;
            StartAlong = startAlong; EndAlong = endAlong; SweepRadians = sweepRadians;
            Curves = Array.AsReadOnly(curves.ToArray());
            RequiredFlatStart = requiredFlatStart; RequiredFlatEnd = requiredFlatEnd;
            StartNode=startNode??Curves[0].P0;EndNode=endNode??Curves[Curves.Count-1].P3;
            PathId=pathId;Prefab=prefab;RoadWidth=roadWidth;StartLevelFactor=startLevelFactor;EndLevelFactor=endLevelFactor;
            FlatStartLength=flatStartLength;FlatEndLength=flatEndLength;StartGradeFactor=startGradeFactor;EndGradeFactor=endGradeFactor;
        }
        public int SourceArm { get; }
        public int TargetArm { get; }
        public CloverEdgeKind Kind { get; }
        public float StartAlong { get; }
        /// <summary>Distance along the target's incoming-to-outgoing carriageway.</summary>
        public float EndAlong { get; }
        public float SweepRadians { get; }
        public IReadOnlyList<Bezier3> Curves { get; }
        public Vec3 StartNode { get; }
        public Vec3 EndNode { get; }
        public string PathId { get; }
        public string? Prefab { get; }
        public float? RoadWidth { get; }
        public float? StartLevelFactor { get; }
        public float? EndLevelFactor { get; }
        /// <summary>Actual level length; a shared-split protection piece may carry grade.</summary>
        public float? FlatStartLength { get; }
        public float? FlatEndLength { get; }
        /// <summary>Signed grade per metre of layer separation, along this physical path.</summary>
        public float StartGradeFactor { get; }
        public float EndGradeFactor { get; }
        /// <summary>Sampled length of the first/last segment after width-aware planning, null when width was not supplied.</summary>
        public float? RequiredFlatStart { get; }
        public float? RequiredFlatEnd { get; }
    }

    public sealed class PlanarJunction
    {
        internal PlanarJunction(CloverNode node, Vec3 position, float along) { Node = node; Position = position; Along = along; }
        public CloverNode Node { get; }
        public Vec3 Position { get; }
        public float Along { get; }
    }

    public sealed class PlanarThroughLine
    {
        internal PlanarThroughLine(int sourceArm, Vec3 start, Vec3 end, float length, IEnumerable<PlanarJunction> junctions,
            IEnumerable<PlanarOverlap> protectedOverlaps)
        {
            SourceArm = sourceArm; Start = start; End = end; Length = length;
            Junctions = Array.AsReadOnly(junctions.ToArray());
            ProtectedOverlaps = Array.AsReadOnly(protectedOverlaps.ToArray());
        }
        public int SourceArm { get; }
        public Vec3 Start { get; }
        public Vec3 End { get; }
        public float Length { get; }
        public IReadOnlyList<PlanarJunction> Junctions { get; }
        /// <summary>Along-line intervals where no unrelated mainline node should be inserted.</summary>
        public IReadOnlyList<PlanarOverlap> ProtectedOverlaps { get; }
    }

    public sealed class PlanarOverlap
    {
        internal PlanarOverlap(CloverNode attachment, float first, float second)
        {
            Attachment = attachment; StartAlong = Math.Min(first, second); EndAlong = Math.Max(first, second);
        }
        public CloverNode Attachment { get; }
        public float StartAlong { get; }
        public float EndAlong { get; }
    }

    public sealed class PlanarCandidateResult
    {
        internal PlanarCandidateResult(PlanarCandidateFailure failure, IEnumerable<PlanarRamp> ramps, IEnumerable<PlanarThroughLine> lines,
            IEnumerable<float> retreatByArm, IEnumerable<EndpointArm> inputArms,CloverleafRoadDesign? design=null,bool rightHandTraffic=true,float? rampWidth=null)
        {
            Failure = failure; Ramps = Array.AsReadOnly(ramps.ToArray()); Lines = Array.AsReadOnly(lines.ToArray());
            RetreatByArm = Array.AsReadOnly(retreatByArm.ToArray());
            InputArms = Array.AsReadOnly(inputArms.ToArray());
            Design=design;RightHandTraffic=rightHandTraffic;RampWidth=rampWidth;
        }
        public PlanarCandidateFailure Failure { get; }
        public IReadOnlyList<PlanarRamp> Ramps { get; }
        public IReadOnlyList<PlanarThroughLine> Lines { get; }
        /// <summary>Requested straight-line retreat, not a verified old-road demolition plan.</summary>
        public IReadOnlyList<float> RetreatByArm { get; }
        /// <summary>Immutable source snapshot, retained to detect stale selections.</summary>
        public IReadOnlyList<EndpointArm> InputArms { get; }
        public CloverleafRoadDesign? Design { get; }
        public bool RightHandTraffic { get; }
        public float? RampWidth { get; }
        /// <summary>Planar geometry has not passed vertical, terrain, collision or game checks.</summary>
        public bool IsPlacementReady => false;
    }

    /// <summary>
    /// Creates one candidate directly from four selected dual-road approaches.
    /// This is neither the existing cloverleaf template nor a placement solver.
    /// </summary>
    public static partial class PlanarCloverleafCandidate
    {
        private const float MinNodeSpacing = 10f;
        private const float MaxPieceLength = 70f;
        private const float MaxAlignmentError = 0.05f;
        private const float MinCross = 0.02f;
        private const float MergeNodeMargin = 5f;

        private sealed class Line
        {
            public Vec3 Start, End, Direction;
            public float Length;
            public readonly PlanarJunction?[] Junctions = new PlanarJunction?[4];
            public readonly List<PlanarOverlap> ProtectedOverlaps = new List<PlanarOverlap>();
        }

        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static Vec3 Flat(Vec3 p) => new Vec3(p.X, 0f, p.Z);
        private static float Cross(Vec3 a, Vec3 b) => a.X * b.Z - a.Z * b.X;
        private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Z * b.Z;
        private static Vec3 Normalized(Vec3 d)
        {
            double length = Math.Sqrt((double)d.X * d.X + (double)d.Z * d.Z);
            return new Vec3((float)(d.X / length), 0, (float)(d.Z / length));
        }

        public static PlanarCandidateResult Create(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic, float loopRadius, float directRadius,
            IReadOnlyList<float>? retreatByArm = null, float? rampWidth = null, float sideGap = 4f, float minimumFlatLength = 0f,
            float directMiddleLength = 0f,CloverleafRoadDesign? design=null)
        {
            if (arms == null) throw new ArgumentNullException(nameof(arms));
            PlanarCandidateResult Fail(PlanarCandidateFailure code) => new PlanarCandidateResult(code, Array.Empty<PlanarRamp>(), Array.Empty<PlanarThroughLine>(), Array.Empty<float>(), Array.Empty<EndpointArm>());
            var snapshot = arms.ToArray();
            if (EndpointConstraints.Validate(snapshot).Count != 0) return Fail(PlanarCandidateFailure.InvalidSelection);
            if (!Finite(loopRadius) || !Finite(directRadius) || loopRadius <= 0 || directRadius <= 0)
                return Fail(PlanarCandidateFailure.InvalidRadius);
            if(!Finite(directMiddleLength)||directMiddleLength<0||directMiddleLength>1000|| (directMiddleLength>0&&!rampWidth.HasValue))
                return Fail(PlanarCandidateFailure.InvalidRadius);
            if ((rampWidth.HasValue && (!Finite(rampWidth.Value) || rampWidth.Value <= 0)) || !Finite(sideGap) || sideGap < 0
                || !Finite(minimumFlatLength) || minimumFlatLength < 0 || (!rampWidth.HasValue && minimumFlatLength > 0))
                return Fail(PlanarCandidateFailure.InvalidRoadWidth);
            if (retreatByArm != null && retreatByArm.Count != 4)
                return Fail(PlanarCandidateFailure.InvalidRetreat);
            float[] retreat = retreatByArm?.ToArray() ?? new float[4];
            if (retreat.Any(v => !Finite(v) || v < 0)) return Fail(PlanarCandidateFailure.InvalidRetreat);
            if (snapshot.Any(a => a.Endpoints.Count != 2)) return Fail(PlanarCandidateFailure.UnsupportedRoadForm);
            if(design!=null && (!rampWidth.HasValue||design.Balances.Any(b=>b==null||b.Arm<0||b.Arm>=4
                ||string.IsNullOrWhiteSpace(b.Prefab)||!Finite(b.Width)||b.Width<=0||b.RampLanes<1||b.Lanes<1)
                ||design.Balances.Select(b=>b.Arm).Distinct().Count()!=design.Balances.Count))return Fail(PlanarCandidateFailure.InvalidRoadWidth);
            if(design!=null&&design.Balances.Any(b=>{
                var port=snapshot.First(a=>a.Index==b.Arm).Endpoints.First(p=>!p.TravelOutward);
                return b.Width>port.Width||b.Lanes+b.RampLanes!=port.Lanes;
            }))return Fail(PlanarCandidateFailure.InvalidRoadWidth);
            if(design?.HugLoops==true&&(!design.EdgeAligned||!rampWidth.HasValue
                ||string.IsNullOrWhiteSpace(design.SharedRampPrefab)||!Finite(design.SharedRampWidth)
                ||design.SharedRampWidth<rampWidth.Value))return Fail(PlanarCandidateFailure.InvalidRoadWidth);

            var selected = snapshot.OrderBy(a => a.Index).ToArray();
            var lines = new Line[4];
            for (int i = 0; i < 4; i++)
            {
                var incoming = selected[i].Endpoints.First(p => !p.TravelOutward);
                var outgoing = selected[(i + 2) % 4].Endpoints.First(p => p.TravelOutward);
                Vec3 start = Flat(incoming.Position) + Normalized(incoming.OutwardTangent) * retreat[i];
                Vec3 end = Flat(outgoing.Position) + Normalized(outgoing.OutwardTangent) * retreat[(i + 2) % 4];
                Vec3 delta = end - start;
                double len = Math.Sqrt((double)delta.X * delta.X + (double)delta.Z * delta.Z);
                if (double.IsInfinity(len) || len < 1e-3 || len > float.MaxValue)
                    return Fail(PlanarCandidateFailure.NonStraightApproach);
                Vec3 direction = Normalized(delta), inDir = -Normalized(incoming.OutwardTangent), outDir = Normalized(outgoing.OutwardTangent);
                bool strict=design?.ApproachTransitions!=true;
                float minimumDot=strict ? .99999f : (float)Math.Cos(CloverleafRoadDesign.MaxApproachDeflectionDegrees*Math.PI/180);
                if (Dot(direction, inDir) < minimumDot || Dot(direction, outDir) < minimumDot
                    || (strict&&(Math.Abs(Cross(delta, inDir)) > MaxAlignmentError || Math.Abs(Cross(delta, outDir)) > MaxAlignmentError)))
                    return Fail(PlanarCandidateFailure.NonStraightApproach);
                lines[i] = new Line { Start = start, End = end, Direction = direction, Length = (float)len };
            }

            var ramps = new List<PlanarRamp>();
            for (int i = 0; i < 4; i++)
                foreach (var kind in new[] { CloverEdgeKind.DirectRamp, CloverEdgeKind.LoopRamp })
                {
                    bool direct = kind == CloverEdgeKind.DirectRamp;
                    if(direct&&design?.HugLoops==true)continue;
                    int offset = direct ? (rightHandTraffic ? 1 : 3) : (rightHandTraffic ? 3 : 1);
                    int destinationLine = (i + offset) % 4;
                    var from = lines[i]; var to = lines[destinationLine];
                    Vec3 Side(Line line)=>new Vec3(line.Direction.Z,0,-line.Direction.X)*(rightHandTraffic?1:-1);
                    float Drop(int arm)=>design?.EdgeAligned==true ?Math.Max(0,(selected[arm].Endpoints.First(p=>!p.TravelOutward).Width-rampWidth!.Value)/2):0;
                    Vec3 originFrom=from.Start+Side(from)*Drop(i),originTo=to.Start+Side(to)*Drop(destinationLine);
                    if (Math.Abs(Cross(from.Direction, to.Direction)) < MinCross)
                        return Fail(PlanarCandidateFailure.DegenerateCrossing);
                    float sign = rightHandTraffic ? -1f : 1f;
                    var path = RampPath.Turn(originFrom, from.Direction, originTo, to.Direction, sign,
                        direct ? directRadius : loopRadius, out float atFrom, out float atTo);
                    float sweep = Math.Abs(path.Arcs[0].Angle);
                    bool smooth=direct&&directMiddleLength>0&&design?.SmoothDirect==true;
                    if(smooth)
                    {
                        float Reach(int arm)=>selected[arm].Endpoints.First(p=>!p.TravelOutward).Width/2+rampWidth!.Value/2+sideGap-Drop(arm);
                        path=SmoothDirectPath.Create(originFrom,from.Direction,originTo,to.Direction,sign,directRadius,
                            directRadius+2*directMiddleLength,Reach(i),Reach(destinationLine),out atFrom,out atTo);
                        if(path.Arcs[0].Radius*(1-(float)Math.Cos(path.Arcs[0].Angle))+1e-3f<Reach(i)
                            ||path.Arcs[2].Radius*(1-(float)Math.Cos(path.Arcs[2].Angle))+1e-3f<Reach(destinationLine))
                            return Fail(PlanarCandidateFailure.MergeCannotClear);
                    }
                    CompoundDirectPath? compound=direct&&directMiddleLength>0&&!smooth
                        ?new CompoundDirectPath(originFrom,from.Direction,originTo,to.Direction,sign,directRadius,directMiddleLength,out atFrom,out atTo):null;
                    float pathLength=compound?.Length??path.Length;
                    Vec3 pathStart=compound?.Start??path.Start,pathEnd=compound?.End??path.End;
                    if (!Finite(pathLength) || !Finite(atFrom) || !Finite(atTo))
                        return Fail(PlanarCandidateFailure.InvalidRadius);
                    if ((direct && sweep >= (float)Math.PI) || (!direct && sweep <= (float)Math.PI))
                        return Fail(PlanarCandidateFailure.DegenerateCrossing);
                    if (atFrom < 0 || atFrom > from.Length || atTo < 0 || atTo > to.Length)
                        return Fail(PlanarCandidateFailure.AttachmentsOutsideApproach);

                    if (pathLength > MaxPieceLength * 10000f) return Fail(PlanarCandidateFailure.InvalidRadius);
                    float radius = direct ? directRadius : loopRadius;
                    var cuts = new List<float> { 0f };
                    if (rampWidth is float rampRoadWidth)
                    {
                        float sourceWidth = selected[i].Endpoints.First(p => !p.TravelOutward).Width;
                        int targetArm = (destinationLine + 2) % 4;
                        float targetWidth = selected[targetArm].Endpoints.First(p => p.TravelOutward).Width;
                        float Flat(float roadWidth)
                        {
                            double drop=design?.EdgeAligned==true?Math.Max(0,(roadWidth-rampRoadWidth)/2):0;
                            double reach = ((double)roadWidth + rampRoadWidth) / 2 + sideGap-drop;
                            if (reach >= 2.0 * radius) return float.PositiveInfinity;
                            double separation = radius * Math.Acos(1 - reach / radius);
                            return (float)Math.Max(minimumFlatLength, Math.Max(MinNodeSpacing, separation + MergeNodeMargin));
                        }
                        float startFlat = Flat(sourceWidth), endFlat = Flat(targetWidth);
                        if(smooth) {startFlat=path.Arcs[0].Length;endFlat=path.Arcs[2].Length;}
                        double maxArcPiece = Math.Min(MaxPieceLength, radius * Math.PI / 2);
                        if (!Finite(startFlat) || !Finite(endFlat) || startFlat > maxArcPiece || endFlat > maxArcPiece)
                            return Fail(PlanarCandidateFailure.MergeCannotClear);
                        float middle = pathLength - startFlat - endFlat;
                        if (middle < MinNodeSpacing) return Fail(PlanarCandidateFailure.MergeCannotClear);
                        int pieces = (int)Math.Ceiling(middle / maxArcPiece);
                        if (pieces > 10000 || middle / pieces < MinNodeSpacing) return Fail(PlanarCandidateFailure.MergeCannotClear);
                        cuts.Add(startFlat);
                        if(compound!=null)
                        {
                            if(compound.FirstBendEnd-startFlat<MinNodeSpacing || pathLength-endFlat-compound.LastBendStart<MinNodeSpacing)
                                return Fail(PlanarCandidateFailure.MergeCannotClear);
                            var anchors=new[]{startFlat,compound.FirstBendEnd,compound.LastBendStart,pathLength-endFlat};
                            for(int j=0;j+1<anchors.Length;j++)
                            {
                                float length=anchors[j+1]-anchors[j];int count=(int)Math.Ceiling(length/maxArcPiece);
                                if(count<=0||length/count<MinNodeSpacing) return Fail(PlanarCandidateFailure.MergeCannotClear);
                                for(int n=1;n<=count;n++)cuts.Add(anchors[j]+length*n/count);
                            }
                        }
                        else if(smooth)
                        {
                            // First/last bends are precisely the protected flat merge pieces.
                            // The middle bend is subdivided without adding tiny arc-boundary pieces.
                            for(int n=1;n<pieces;n++)cuts.Add(startFlat+middle*n/pieces);
                            cuts.Add(pathLength-endFlat);
                        }
                        else
                        {
                            for (int n = 1; n < pieces; n++) cuts.Add(startFlat + middle * n / pieces);
                            cuts.Add(pathLength - endFlat);
                        }
                    }
                    else
                    {
                        int pieces = Math.Max((int)Math.Ceiling(pathLength / MaxPieceLength), (int)Math.Ceiling(sweep / (Math.PI / 2)));
                        for (int n = 1; n < pieces; n++) cuts.Add(pathLength * n / pieces);
                    }
                    cuts.Add(pathLength);
                    var heights = new float[cuts.Count];
                    var segments = new List<RoadSegmentSpec>();
                    if(compound==null) path.EmitAt(segments, $"{kind}{i}-", RoadRole.Ramp, cuts, heights, null, pathStart, pathEnd);
                    else segments.AddRange(compound.Emit(cuts).Select((c,n)=>new RoadSegmentSpec($"{kind}{i}-{n}",RoadRole.Ramp,c,null)));
                    var sourceKind = direct ? CloverNodeKind.DirectExit : CloverNodeKind.LoopExit;
                    var destKind = direct ? CloverNodeKind.DirectMerge : CloverNodeKind.LoopMerge;
                    int sourceSlot = direct ? 0 : 2, destSlot = direct ? 3 : 1;
                    Vec3 nodeStart=design?.EdgeAligned==true?from.Start+from.Direction*atFrom:pathStart;
                    Vec3 nodeEnd=design?.EdgeAligned==true?to.Start+to.Direction*atTo:pathEnd;
                    from.Junctions[sourceSlot] = new PlanarJunction(new CloverNode(i, sourceKind), nodeStart, atFrom);
                    to.Junctions[destSlot] = new PlanarJunction(new CloverNode(destinationLine, destKind), nodeEnd, atTo);
                    if (rampWidth.HasValue)
                    {
                        float sourceClear = Dot(segments[0].Curve.P3 - from.Start, from.Direction);
                        float targetClear = Dot(segments[segments.Count - 1].Curve.P0 - to.Start, to.Direction);
                        from.ProtectedOverlaps.Add(new PlanarOverlap(new CloverNode(i, sourceKind), atFrom, sourceClear));
                        to.ProtectedOverlaps.Add(new PlanarOverlap(new CloverNode(destinationLine, destKind), targetClear, atTo));
                    }
                    float? plannedStart = rampWidth.HasValue ? segments[0].Curve.Length(64) : (float?)null;
                    float? plannedEnd = rampWidth.HasValue ? segments[segments.Count - 1].Curve.Length(64) : (float?)null;
                    ramps.Add(new PlanarRamp(i, (destinationLine + 2) % 4, kind, atFrom, atTo, sweep, segments.Select(s => s.Curve), plannedStart, plannedEnd,nodeStart,nodeEnd));
                }

            if(design?.HugLoops==true)
            {
                var hugFailure=CreateHugRamps(lines,selected,ramps,rightHandTraffic,loopRadius,directRadius,rampWidth!.Value,sideGap,minimumFlatLength,design);
                if(hugFailure!=PlanarCandidateFailure.None)return Fail(hugFailure);
            }
            var resultLines = new List<PlanarThroughLine>();
            for (int i = 0; i < 4; i++)
            {
                var line = lines[i];
                if (line.Junctions.Any(j => j == null)) return Fail(PlanarCandidateFailure.ConflictingAttachmentOrder);
                var joints = line.Junctions.Select(j => j!).ToArray();
                float previous = 0f;
                foreach (var joint in joints)
                {
                    if (joint.Along - previous < MinNodeSpacing) return Fail(PlanarCandidateFailure.ConflictingAttachmentOrder);
                    previous = joint.Along;
                }
                if (line.Length - previous < MinNodeSpacing) return Fail(PlanarCandidateFailure.ConflictingAttachmentOrder);
                if (joints.Any(j => line.ProtectedOverlaps.Any(zone => j.Along > zone.StartAlong + 1e-3f && j.Along < zone.EndAlong - 1e-3f)))
                    return Fail(PlanarCandidateFailure.MergeOverlapConflict);
                resultLines.Add(new PlanarThroughLine(i, line.Start, line.End, line.Length, joints, line.ProtectedOverlaps));
            }
            return new PlanarCandidateResult(PlanarCandidateFailure.None, ramps, resultLines, retreat, selected,design,rightHandTraffic,rampWidth);
        }
    }
}
