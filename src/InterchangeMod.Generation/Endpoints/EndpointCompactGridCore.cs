using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointGridArmRoads
    {
        public EndpointGridArmRoads(int arm, RoadInfo through, RoadInfo collector)
        { Arm = arm; Through = through; Collector = collector; }
        public int Arm { get; }
        public RoadInfo Through { get; }
        public RoadInfo Collector { get; }
    }

    public static class EndpointCompactGridCore
    {
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        private static Vec3 Flat(Vec3 x) => new Vec3(x.X, 0, x.Z);
        private static bool Range(HeightRange? x) => !x.HasValue || Finite(x.Value.Min) && Finite(x.Value.Max) && x.Value.Min <= x.Value.Max;
        private static bool Spacing(float? x) => !x.HasValue || Finite(x.Value) && x.Value >= 0;
        private static EndpointStackCoreResult Fail(string diagnostic) => new EndpointStackCoreResult(false,
            diagnostic, Array.Empty<SpatialSegment>(), Array.Empty<EndpointCorePort>());
        private static Vec3 Direction(EndpointArm arm) => Vec3.Normalize(arm.Endpoints.Select(e => Vec3.Normalize(Flat(e.OutwardTangent)))
            .Aggregate(Vec3.Zero, (a, b) => a + b));

        /// <summary>All possible lower tops and upper bottoms participate, including ground variants.</summary>
        public static float RequiredClearance(IReadOnlyList<EndpointGridArmRoads> roads, CloverleafRoadDesign design, RoadInfo ramp)
        {
            if (roads == null || design == null || ramp == null || roads.Any(r => r == null || r.Through == null || r.Collector == null)) return float.NaN;
            var fallback = new HeightRange(0, HeightRange.MeasuredMinimumGap);
            var ranges = new List<HeightRange>();
            foreach (var road in roads.SelectMany(r => new[] { r.Through, r.Collector }).Append(ramp))
            { ranges.Add(road.GroundHeight ?? fallback); ranges.Add(road.ElevatedHeight ?? fallback); }
            foreach (var rule in design.BridgeRules)
            {
                if (rule == null) return float.NaN;
                ranges.Add(rule.GroundHeight ?? fallback); ranges.Add(rule.ElevatedHeight ?? fallback);
            }
            if (design.BridgeRules.Count < 4) ranges.Add(fallback);
            if (ranges.Any(r => !Finite(r.Min) || !Finite(r.Max) || r.Min > r.Max)) return float.NaN;
            float gap = HeightRange.BridgeThreshold;
            foreach (var lower in ranges) foreach (var upper in ranges) gap = Math.Max(gap, HeightRange.RequiredGap(lower, upper));
            return Finite(gap) ? gap : float.NaN;
        }

        public static EndpointStackCoreResult Create(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic,
            float baseHeight, float levelGap, RoadInfo ramp, IReadOnlyList<EndpointGridArmRoads> roads,
            float maxGrade, CloverleafRoadDesign design, float terrainHeight)
        {
            var failures = new List<string>();
            foreach (var trial in Candidates(arms, rightHandTraffic, baseHeight, levelGap, ramp, roads, maxGrade, design, terrainHeight))
            {
                if (trial.Success) return trial;
                failures.Add(trial.Diagnostic);
            }
            return Fail(string.Join("; ", failures));
        }

        public static IEnumerable<EndpointStackCoreResult> Candidates(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic,
            float baseHeight, float levelGap, RoadInfo ramp, IReadOnlyList<EndpointGridArmRoads> roads,
            float maxGrade, CloverleafRoadDesign design, float terrainHeight,bool secantReference=false,float bendStrength=0)
        {
            string invalid = Validate(arms, baseHeight, levelGap, ramp, roads, maxGrade, design, terrainHeight);
            if (invalid.Length != 0) { yield return Fail(invalid); yield break; }
            if(!Finite(bendStrength)||bendStrength<0||bendStrength>1)
            {yield return Fail("Invalid compact grid curve strength.");yield break;}
            var selected = arms.OrderBy(a => a.Index).ToArray();
            var axes = selected.Select(Direction).ToArray();
            Vec3 Mid(int i)=>Flat(selected[i].Endpoints[0].Position*.5f+selected[i].Endpoints[1].Position*.5f);
            if(secantReference)
            {
                axes[0]=Vec3.Normalize(Mid(0)-Mid(2));axes[1]=Vec3.Normalize(Mid(1)-Mid(3));
                axes[2]=-axes[0];axes[3]=-axes[1];
            }
            var forward = Vec3.Normalize(axes[0] - axes[2]);
            var right = Directions.RightOf(forward) * (rightHandTraffic ? 1 : -1);
            // Check all four input gaps, including the closing 3 -> 0 gap. A
            // family-average axis can hide an individual arm beyond the contract.
            // The small dot tolerance covers float normalisation at exactly 80/100.
            float adjacentDot = (float)Math.Cos(80 * Math.PI / 180) + .000001f;
            float oppositeDot = -(float)Math.Cos(20 * Math.PI / 180) + .000001f;
            if (axes.Any(a => !Finite(a.X) || !Finite(a.Z) || a.Length() < .99f)
                || Directions.Dot(axes[0], axes[2]) > oppositeDot || Directions.Dot(axes[1], axes[3]) > oppositeDot
                || Enumerable.Range(0, 4).Any(i => Math.Abs(Directions.Dot(axes[i], axes[(i + 1) % 4])) > adjacentDot))
            { yield return Fail("Compact grid requires two near-orthogonal opposing road families (80 to 100 degrees)."); yield break; }
            int[] original = Directions.Dot(axes[1], right) > 0 ? new[] { 0, 1, 2, 3 } : new[] { 0, 3, 2, 1 };
            var frame = EndpointFrame.Estimate(selected, InterchangeType.Stack);
            if (!secantReference&&(frame.Failure != EndpointFrameFailure.None || !frame.Center.HasValue))
            { yield return Fail("Compact grid road axes have no finite common reference."); yield break; }
            var center = frame.Center.HasValue?Flat(frame.Center.Value):Vec3.Zero;
            if(secantReference)
            {
                double Cross(Vec3 a,Vec3 b)=>(double)a.X*b.Z-(double)a.Z*b.X;
                double determinant=Cross(axes[0],axes[1]);
                double along=Cross(Mid(1)-Mid(0),axes[1])/determinant;
                if(double.IsNaN(along)||double.IsInfinity(along)||Math.Abs(along)>float.MaxValue)
                {yield return Fail("Compact grid cut secants have no finite common reference.");yield break;}
                center=Mid(0)+axes[0]*(float)along;
                if(!Finite(center.X)||!Finite(center.Z))
                {yield return Fail("Compact grid cut secants have no finite common reference.");yield break;}
            }
            var median = new float[4];
            foreach (var arm in selected)
            {
                var input = arm.Endpoints.Single(e => !e.TravelOutward); var output = arm.Endpoints.Single(e => e.TravelOutward);
                float gap = Directions.Dot(Flat(output.Position - input.Position), Directions.RightOf(Direction(arm)))
                    * (rightHandTraffic ? 1 : -1) - input.Width;
                if (!Finite(gap) || gap < -.01f || gap > 1000)
                { yield return Fail($"Compact grid arm {arm.Index + 1} has overlapping or incorrectly ordered carriageways."); yield break; }
                median[arm.Index] = Math.Max(0, gap);
            }
            float reflect=rightHandTraffic?1:-1;
            var bend=bendStrength>0?EndpointCoreBend.Create(selected,original,center,right*reflect,forward,reflect,90,bendStrength):null;
            if(bendStrength>0&&(bend==null||bend.IsIdentity))
            {yield return Fail("Cut roads do not define a bounded curved grid reference.");yield break;}
            // A bounded parameter search re-solves the curves. No generated geometry is scaled.
            foreach (var alternative in new[] { (Gap: 3f, Extra: 0f, Factor: 1f, CollectorOuter:.45f), (Gap: 5f, Extra: 0f, Factor: 1f, CollectorOuter:.45f),
                (Gap: 8f, Extra: 0f, Factor: 1f, CollectorOuter:.45f), (Gap: 3f, Extra: 8f, Factor: 1f, CollectorOuter:.45f),
                (Gap: 3f, Extra: 0f, Factor: .85f, CollectorOuter:.45f), (Gap: 5f, Extra: 8f, Factor: .85f, CollectorOuter:.45f),
                // Wide street reductions can be as wide as the full source. Keep
                // the collector's first course beyond the through taper's nose,
                // so a later course cannot clip the still-diverging terminal.
                (Gap:3f,Extra:0f,Factor:1f,CollectorOuter:.48f), (Gap:5f,Extra:0f,Factor:1f,CollectorOuter:.48f),
                (Gap:8f,Extra:0f,Factor:1f,CollectorOuter:.48f) })
            {
                var trial = Solve(selected, original, roads.OrderBy(r => r.Arm).ToArray(), center, forward, right, median,
                    baseHeight, levelGap, terrainHeight, ramp, maxGrade, design, alternative.Gap, alternative.Extra, alternative.Factor,rightHandTraffic,bend,alternative.CollectorOuter);
                yield return trial.Success ? trial : Fail($"Grid gap {alternative.Gap:F0}m, outer extra {alternative.Extra:F0}m, grade factor {alternative.Factor:F2}: {trial.Diagnostic}");
            }
        }

        private static bool RoadValid(RoadInfo road) => road != null && !string.IsNullOrWhiteSpace(road.Name)
            && road.ForwardLanes > 0 && road.BackwardLanes == 0 && road.PublicTransportLanes == 0
            && Finite(road.Width) && road.Width > 0 && road.Width <= 1000
            && Finite(road.ElevatedLength) && road.ElevatedLength >= 0 && (road.ElevatedLength == 0 || road.ElevatedLength >= 10 && road.ElevatedLength <= 1000)
            && Finite(road.PillarWidth) && road.PillarWidth >= 0 && road.PillarWidth <= 1000 && Spacing(road.MiddlePillarSpacing)
            && Range(road.GroundHeight) && Range(road.ElevatedHeight);
        private static bool SameRoad(RoadInfo a, RoadInfo b) => a.Name == b.Name && a.Width == b.Width && a.ForwardLanes == b.ForwardLanes
            && a.BackwardLanes == b.BackwardLanes && a.IsHighway == b.IsHighway && a.PublicTransportLanes == b.PublicTransportLanes
            && a.ElevatedLength == b.ElevatedLength && a.MiddlePillarSpacing == b.MiddlePillarSpacing && a.PillarWidth == b.PillarWidth
            && a.GroundHeight.Equals(b.GroundHeight) && a.ElevatedHeight.Equals(b.ElevatedHeight);

        internal static string Validate(IReadOnlyList<EndpointArm> arms, float height, float gap, RoadInfo ramp,
            IReadOnlyList<EndpointGridArmRoads> roads, float grade, CloverleafRoadDesign design, float terrain)
        {
            if (arms == null || EndpointConstraints.Validate(arms, InterchangeType.Stack).Count != 0 || arms.Any(a => a.Endpoints.Count != 2))
                return "Compact grid requires four valid divided-road arms with eight directed endpoints.";
            if (!Finite(height) || !Finite(gap) || !Finite(terrain) || !Finite(height - terrain) || !Finite(height + 3 * gap)
                || height < terrain || !Finite(grade) || grade <= 0 || grade > .30f || !RoadValid(ramp) || design == null)
                return "Invalid compact grid height, terrain, strict maximum grade, ramp or design.";
            if (roads == null || roads.Count != 4 || roads.Any(r => r == null || r.Arm < 0 || r.Arm > 3
                || !RoadValid(r.Through) || !RoadValid(r.Collector)) || roads.Select(r => r.Arm).Distinct().Count() != 4)
                return "Compact grid requires four actual forward-only through and collector road selections with valid support metadata.";
            var selected = arms.OrderBy(a => a.Index).ToArray(); var chosen = roads.OrderBy(r => r.Arm).ToArray();
            for (int i = 0; i < 4; i++)
            {
                var first = selected[i].Endpoints[0]; var other = selected[(i + 2) % 4].Endpoints[0]; var choice = chosen[i];
                if (first.Width > 1000 || selected[i].Endpoints.Any(e => e.Prefab != first.Prefab || e.Width != first.Width || e.Lanes != first.Lanes)
                    || first.Prefab != other.Prefab || first.Width != other.Width || first.Lanes != other.Lanes)
                    return "Compact grid opposite arms require matching actual full-road prefabs, widths and lane counts.";
                if (choice.Through.ForwardLanes + 2 * ramp.ForwardLanes != first.Lanes || choice.Collector.ForwardLanes != 2 * ramp.ForwardLanes
                    || choice.Through.Width > first.Width || choice.Collector.Width > first.Width || ramp.Width > choice.Collector.Width
                    || !SameRoad(choice.Through, chosen[(i + 2) % 4].Through) || !SameRoad(choice.Collector, chosen[(i + 2) % 4].Collector))
                    return "Compact grid requires N=T+2R lanes, collector=2R, suitable widths and identical opposing road/support selections.";
            }
            if (design.Balances.Count != 0) return "Compact grid uses explicit through and collector roads; legacy single-ramp balances are unsupported.";
            if (design.BridgeRules.Any(r => r == null || r.Arm < 0 || r.Arm > 3 || !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000
                || !Finite(r.Width) || r.Width != selected[r.Arm].Endpoints[0].Width || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000
                || !Spacing(r.MiddleSpacing) || !Range(r.GroundHeight) || !Range(r.ElevatedHeight))
                || design.BridgeRules.Select(r => r.Arm).Distinct().Count() != design.BridgeRules.Count)
                return "Invalid compact grid full-road bridge support metadata.";
            for (int i = 0; i < 2; i++)
            {
                var a = design.BridgeRules.FirstOrDefault(r => r.Arm == i); var b = design.BridgeRules.FirstOrDefault(r => r.Arm == i + 2);
                if (a != null && b != null && (a.MaxLength != b.MaxLength || a.MiddleSpacing != b.MiddleSpacing || a.PillarWidth != b.PillarWidth
                    || !a.GroundHeight.Equals(b.GroundHeight) || !a.ElevatedHeight.Equals(b.ElevatedHeight)))
                    return "Compact grid opposing full roads have inconsistent bridge support metadata.";
            }
            var actualRoads = chosen.SelectMany(r => new[] { r.Through, r.Collector }).Append(ramp).ToArray();
            if (actualRoads.GroupBy(r => r.Name).Any(g => g.Any(r => !SameRoad(r, g.First()))))
                return "Compact grid has inconsistent metadata for the same actual prefab.";
            if (actualRoads.Any(road => selected.SelectMany(arm => arm.Endpoints).Any(endpoint => endpoint.Prefab == road.Name
                && (endpoint.Lanes != road.ForwardLanes || endpoint.Width != road.Width))))
                return "Compact grid has conflicting full-road and internal-road metadata for the same actual prefab.";
            if (design.SupportRules.Any(r => r == null || string.IsNullOrWhiteSpace(r.Prefab) || !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000
                || !Spacing(r.MiddleSpacing) || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000)
                || design.SupportRules.Select(r => r.Prefab).Distinct().Count() != design.SupportRules.Count)
                return "Invalid compact grid prefab support metadata.";
            if (design.SupportRules.Count != 0 && actualRoads.Select(r => r.Name).Concat(selected.Select(a => a.Endpoints[0].Prefab))
                .Distinct().Any(name => !design.SupportRules.Any(r => r.Prefab == name)))
                return "Compact grid is missing actual prefab support metadata.";
            foreach (var road in actualRoads)
            {
                var rule = design.SupportRules.FirstOrDefault(r => r.Prefab == road.Name);
                if (rule != null && ((road.ElevatedLength > 0 && rule.MaxLength != road.ElevatedLength)
                    || rule.MiddleSpacing != road.MiddlePillarSpacing || road.PillarWidth > 0 && rule.PillarWidth != road.PillarWidth))
                    return "Compact grid has conflicting actual prefab support metadata; real spans and piers cannot be overridden.";
            }
            foreach (var group in design.BridgeRules.GroupBy(rule => selected[rule.Arm].Endpoints[0].Prefab))
            {
                var first = group.First(); var actual = design.SupportRules.FirstOrDefault(rule => rule.Prefab == group.Key);
                if (group.Any(rule => rule.MaxLength != first.MaxLength || rule.MiddleSpacing != first.MiddleSpacing || rule.PillarWidth != first.PillarWidth
                    || !rule.GroundHeight.Equals(first.GroundHeight) || !rule.ElevatedHeight.Equals(first.ElevatedHeight))
                    || actual != null && (actual.MaxLength != first.MaxLength || actual.MiddleSpacing != first.MiddleSpacing || actual.PillarWidth != first.PillarWidth))
                    return "Compact grid has conflicting full-road prefab support metadata.";
            }
            float required = RequiredClearance(roads, design, ramp);
            if (!Finite(required)) return "Invalid compact grid road height envelope metadata.";
            if (gap < required) return $"Compact grid needs at least {required:F2}m layer clearance for all actual road envelopes.";
            if (Math.Max(terrain, height - gap / 2) + gap - terrain < HeightRange.BridgeThreshold)
                return "Compact grid second floor must reach the actual elevated bridge threshold.";
            return "";
        }

        private static EndpointStackCoreResult Solve(EndpointArm[] arms, int[] original, EndpointGridArmRoads[] roads,
            Vec3 center, Vec3 forward, Vec3 right, float[] median, float height, float gap, float terrain,
            RoadInfo ramp, float limit, CloverleafRoadDesign design, float sideGap, float outerExtra, float factor,bool rightHandTraffic,CurveWarp? bend=null,float collectorOuter=.45f)
        {
            float low = Math.Max(terrain, height - gap / 2);
            var levels = new[] { low, low + gap, low + 2 * gap, low + 3 * gap };
            float d = roads.Max(r => r.Collector.Width) / 2 + sideGap;
            // Opposite large quarter turns have a gap (2sqrt(2)-2)C-2D.
            float c = Math.Max(d + Math.Max(12, ramp.Width * 2),
                (2 * d + ramp.Width + sideGap) / (2 * (float)Math.Sqrt(2) - 2));
            float throughWidth = roads.Max(r => r.Through.Width);
            float o = c + (throughWidth + ramp.Width) / 2 + sideGap + outerExtra;
            float a = o + throughWidth / 2 + 4;
            float grade = limit * factor * .96f;
            float rise = Math.Max(Math.Abs(levels[2] - height), Math.Abs(levels[3] - height));
            // Twenty metres at each end eases into/out of the grade; most of the
            // rise has constant grade, avoiding the 1.5x whole-ramp smoothstep cost.
            float shift=arms.Max(arm=>
            {
                float m=(arm.Endpoints[0].Width+median[arm.Index])/2;
                var road=roads[arm.Index];float full=arm.Endpoints[0].Width;
                return Math.Max(Math.Abs(o-m-(full-road.Through.Width)/2),Math.Abs(m-(full-road.Collector.Width)/2-d));
            });
            // The three easing spans have |lateral acceleration| <= 11*shift/L².
            // Keep a meaningful radius for both reduced roads. The full road is
            // straight at the fork, with zero curvature on the adjoining taper.
            float spread = Math.Max(36,Math.Max(shift*1.5f,
                (float)Math.Sqrt(11*shift*Math.Max(40,2*roads.Max(road=>Math.Max(road.Through.Width,road.Collector.Width))))));
            // Climb while the through road moves sideways, rather than putting
            // the entire taper in front of the entire climb. Its first easing
            // span also eases into the grade, and the last 20m eases out of it.
            float length=Math.Max(spread+20,rise/grade+2*(.45f*spread+20)/3);
            // Attachment adds full-width segments outside the port. Their rounded
            // width envelopes must clear the collector beginning at the fork;
            // a fixed 20m stub is insufficient for wider full/collector pairs.
            float outerStub = Math.Max(20, arms.Max(arm => arm.Endpoints[0].Width) / 2
                + roads.Max(road => road.Collector.Width) / 2 + sideGap);
            float fork=a+length,s=fork-spread,boundary=fork+outerStub;
            if (!Finite(boundary) || boundary > 1400) return Fail("Compact grid exceeds its finite practical core extent.");
            var axes = new[] { Vec3.UnitZ, Vec3.UnitX, -Vec3.UnitZ, -Vec3.UnitX };
            var sides = axes.Select(Directions.RightOf).ToArray();
            Vec3 At(int slot, float lateral, float along, float y) => sides[slot] * lateral + axes[slot] * along + new Vec3(0, y, 0);
            Vec3 Rigid(Vec3 p)=>center+right*p.X+forward*p.Z+new Vec3(0,p.Y,0);
            Vec3 World(Vec3 p)=>Rigid(bend==null?p:bend.Apply(p));
            Bezier3 WorldCurve(Bezier3 curve)
            {
                if(bend!=null)curve=bend.Apply(new RoadSegmentSpec("Grid",RoadRole.MainRoad,curve,null)).Curve;
                return new Bezier3(Rigid(curve.P0),Rigid(curve.P1),Rigid(curve.P2),Rigid(curve.P3));
            }
            var segments = new List<SpatialSegment>(); var ports = new List<EndpointCorePort>();
            var supports = SupportRules(arms, roads, ramp, design);
            void Add(string path, int slot, CloverEdgeKind kind, RoadInfo road, Bezier3 curve, bool keepSpan = false,
                Vec3? logicalStart=null,Vec3? logicalEnd=null)
            {
                var rule = supports.First(r => r.Prefab == road.Name);
                float maximum = rule.MaxLength;
                // Preserve real central bridge spans if the prefab can carry them.
                // Splitting an otherwise valid span creates an avoidable centre pier.
                float emittedLength=bend==null?curve.Length(64):WorldCurve(curve).Length(64);
                int count = keepSpan && emittedLength < maximum * .98f ? 1 : Math.Max(1, (int)Math.Ceiling(emittedLength / (maximum * .90f)));
                for (int j = 0; j < count; j++)
                {
                    var piece = count == 1 ? curve : curve.Slice(j / (float)count, (j + 1) / (float)count);
                    var world = WorldCurve(piece);
                    segments.Add(new SpatialSegment(original[slot], kind, world,
                        j==0&&logicalStart.HasValue?World(logicalStart.Value):world.P0,
                        j+1==count&&logicalEnd.HasValue?World(logicalEnd.Value):world.P3,road.Name,road.Width,pathId:path));
                }
            }
            void AddChain(string path,int slot,CloverEdgeKind kind,RoadInfo road,IEnumerable<Bezier3> source,
                Vec3? logicalStart=null,Vec3? logicalEnd=null)
            {
                var chain=source.ToArray();
                for(int j=0;j<chain.Length;j++)Add(path,slot,kind,road,chain[j],true,
                    j==0?logicalStart:null,j+1==chain.Length?logicalEnd:null);
            }
            RoadInfo Full(int slot) { var p = arms[original[slot]].Endpoints[0]; return new RoadInfo(p.Prefab, true, p.Lanes, 0, p.Width); }
            float M(int slot) { var p = arms[original[slot]].Endpoints[0]; return (p.Width + median[original[slot]]) / 2; }
            var incomingFork = Enumerable.Range(0, 4).Select(i => At(i, -M(i), fork, height)).ToArray();
            var outgoingFork = Enumerable.Range(0, 4).Select(i => At(i, M(i), fork, height)).ToArray();
            var innerIn = Enumerable.Range(0, 4).Select(i => At(i, -d, c, levels[i % 2])).ToArray();
            var innerOut = Enumerable.Range(0, 4).Select(i => At(i, d, c, levels[1 - i % 2])).ToArray();
            for (int i = 0; i < 4; i++)
            {
                int opposite = (i + 2) % 4, arm = original[i]; var actual = roads[arm];
                var full = Full(i); string main = $"GridMain{arm}"; float h = levels[2 + i % 2];
                float throughDrop=(full.Width-actual.Through.Width)/2,collectorDrop=(full.Width-actual.Collector.Width)/2;
                var entry = At(i, -M(i), boundary, height); var exit = At(opposite, M(opposite), boundary, height);
                Add(main, i, CloverEdgeKind.Main, full, Bezier3.Line(entry, incomingFork[i]));
                // Keep one logical fork, but align the narrower curve surfaces to
                // opposite edges of the full road, as the established templates do.
                AddChain(main,i,CloverEdgeKind.Main,actual.Through,
                    ClimbWithShift(incomingFork[i]-sides[i]*throughDrop,At(i,-o,a,h),-axes[i],spread),logicalStart:incomingFork[i]);
                Add(main, i, CloverEdgeKind.Main, actual.Through, Bezier3.Line(At(i, -o, a, h), At(i, -o, 0, h)), true);
                Add(main, i, CloverEdgeKind.Main, actual.Through, Bezier3.Line(At(i, -o, 0, h), At(i, -o, -a, h)), true);
                AddChain(main,i,CloverEdgeKind.Main,actual.Through,
                    ClimbWithShift(outgoingFork[opposite]+sides[opposite]*throughDrop,At(opposite,o,a,h),-axes[opposite],spread)
                        .Reverse().Select(curve=>new Bezier3(curve.P3,curve.P2,curve.P1,curve.P0)),logicalEnd:outgoingFork[opposite]);
                Add(main, i, CloverEdgeKind.Main, full, Bezier3.Line(outgoingFork[opposite], exit));
                var inTangent=bend==null?World(entry-axes[i])-World(entry):WorldCurve(Bezier3.Line(entry,incomingFork[i])).Derivative(0);
                var outTangent=bend==null?World(exit-axes[i])-World(exit):WorldCurve(Bezier3.Line(outgoingFork[opposite],exit)).Derivative(1);
                ports.Add(new EndpointCorePort(arm,false,World(entry),Vec3.Normalize(inTangent),full.Name,full.Width));
                ports.Add(new EndpointCorePort(original[opposite],true,World(exit),Vec3.Normalize(outTangent),full.Name,full.Width));
                string inputPath = $"GridInCollector{arm}", outputPath = $"GridOutCollector{arm}";
                AddChain(inputPath,i,CloverEdgeKind.DirectRamp,actual.Collector,
                    Shift(incomingFork[i]+sides[i]*collectorDrop,At(i,-d,s,height),-axes[i],collectorOuter),logicalStart:incomingFork[i]);
                foreach (var curve in Profile(At(i, -d, s, height), innerIn[i], -axes[i])) Add(inputPath, i, CloverEdgeKind.DirectRamp, actual.Collector, curve);
                foreach (var curve in Profile(innerOut[i], At(i, d, s, height), axes[i])) Add(outputPath, i, CloverEdgeKind.DirectRamp, actual.Collector, curve);
                AddChain(outputPath,i,CloverEdgeKind.DirectRamp,actual.Collector,
                    Shift(At(i,d,s,height),outgoingFork[i]-sides[i]*collectorDrop,axes[i],collectorOuter),logicalEnd:outgoingFork[i]);
                foreach (bool largeTurn in new[] { true, false })
                {
                    int target = (i + (largeTurn ? 1 : 3)) % 4;
                    float radius = c + (largeTurn ? d : -d); float handle = radius * .55228475f;
                    var curve = new Bezier3(innerIn[i], innerIn[i] - axes[i] * handle,
                        innerOut[target] - axes[target] * handle, innerOut[target]);
                    var world = WorldCurve(curve); var from = world.Derivative(0); var to = world.Derivative(1);
                    // Reflection reverses physical turn handedness, while the
                    // canonical large/small-radius connectivity remains unchanged.
                    bool physicalLeft = (double)from.X * to.Z - (double)from.Z * to.X > 0;
                    Add($"Grid{(physicalLeft ? "Left" : "Right")}{arm}To{original[target]}", i,
                        physicalLeft ? CloverEdgeKind.LeftRamp : CloverEdgeKind.DirectRamp, ramp, curve, true);
                }
            }
            if(bend!=null)
            {
                var aligned=EndpointGridForkAlignment.Rebind(segments,rightHandTraffic,out string alignmentDetail);
                if(aligned==null)return Fail(alignmentDetail);
                segments=aligned;
            }
            if (segments.Any(segment => VerticalProfile.CheckGrade(segment.WorldCurve, limit) != VerticalProfileFailure.None))
                return Fail("Compact grid exceeds strict maximum grade certification.");
            if(bend!=null&&segments.Any(segment=>segment.WorldCurve.Length(128)>=supports.Single(rule=>rule.Prefab==segment.Prefab).MaxLength
                ||CurveCurvature.CheckMinimumRadius(segment.WorldCurve,2*segment.RoadWidth!.Value)!=CurveCurvatureResult.Clear))
                return Fail("Curved grid exceeds an actual bridge span or minimum road radius.");
            foreach (var path in segments.GroupBy(segment => segment.PathId))
            {
                var chain = path.ToArray();
                for (int i = 1; i < chain.Length; i++) if (chain[i - 1].EndNode != chain[i].StartNode)
                    return Fail($"Compact grid path {path.Key} has an inexact logical/curve join.");
                if (!EndpointThreeWayCore.IsSimplePath(chain.Select(segment => segment.WorldCurve).ToArray(), out string reason))
                    return Fail($"Compact grid path {path.Key} is not certified simple: {reason}");
            }
            if (ports.Count != 8 || segments.Select(segment => segment.PathId).Distinct().Count() != 20)
                return Fail("Compact grid did not produce twenty chains and eight full-road external ports.");
            foreach (var input in ports.Where(port => !port.TravelOutward))
            {
                var seen = new HashSet<Vec3> { input.Position }; var queue = new Queue<Vec3>(); queue.Enqueue(input.Position);
                while (queue.Count > 0)
                {
                    var at = queue.Dequeue(); foreach (var edge in segments.Where(segment => segment.StartNode == at))
                        if (seen.Add(edge.EndNode)) queue.Enqueue(edge.EndNode);
                }
                foreach (var output in ports.Where(port => port.TravelOutward))
                    if (seen.Contains(output.Position) != (input.Arm != output.Arm)) return Fail("Compact grid has a missing directed movement or an unintended U-turn.");
            }
            var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, segments);
            var check = EndpointNetworkClearance.Check(candidate, RequiredClearance(roads, design, ramp), out string detail);
            if (check != CurveClearanceResult.Clear) return Fail($"Compact grid deck {check}: {detail}");
            check = EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out detail, supports);
            if (check != CurveClearanceResult.Clear) return Fail($"Compact grid support {check}: {detail}");
            return new EndpointStackCoreResult(true,
                $"Compact grid four floors {levels[0]:F1}/{levels[1]:F1}/{levels[2]:F1}/{levels[3]:F1}m; full-road ports at {height:F1}m; external endpoint transitions remain required.",
                segments.AsReadOnly(), ports.OrderBy(port => port.Arm).ThenBy(port => port.TravelOutward).ToList().AsReadOnly(), center,bend!=null);
        }

        internal static IEnumerable<Bezier3> Shift(Vec3 start, Vec3 end, Vec3 direction,float outer=.45f)
        {
            float length = Directions.Dot(end - start, direction);
            var lateral=end-start-direction*length;
            Vec3 At(float t,float offset)=>start+direction*(length*t)+lateral*offset;
            // Non-uniform C2 spline. Each outer span covers 45% of the forward
            // run and 36.8% of the lateral shift: the first internal node is past
            // the fork nose, where both reduced road decks can actually separate.
            // The middle 10% connects the two linear acceleration ramps.
            float mid=1-2*outer;
            float q=outer*outer/(1-outer),v=3*outer/(1-outer);
            var a=At(outer,q);var b=At(1-outer,1-q);
            yield return new Bezier3(start,At(outer/3,0),At(2*outer/3,0),a);
            if(mid>.0001f)yield return new Bezier3(a,At(outer+mid/3,q+v*mid/3),At(outer+2*mid/3,1-q-v*mid/3),b);
            yield return new Bezier3(b,At(1-2*outer/3,1),At(1-outer/3,1),end);
        }

        internal static IEnumerable<Bezier3> ClimbWithShift(Vec3 start,Vec3 end,Vec3 direction,float spread)
        {
            float run=Directions.Dot(end-start,direction),ease=.45f*spread;
            float slope=(end.Y-start.Y)/(run-2*(ease+20)/3);
            var flatEnd=new Vec3(end.X,start.Y,end.Z);
            var shiftEnd=flatEnd-direction*(run-spread);
            var topEase=flatEnd-direction*20;
            var planar=Shift(start,shiftEnd,direction).ToList();
            if(run-spread>20.01f)planar.Add(Bezier3.Line(shiftEnd,topEase));
            else topEase=shiftEnd;
            planar.Add(Bezier3.Line(topEase,flatEnd));
            (float Height,float Grade) At(float d)
            {
                if(d<ease)return(start.Y+slope*d*d*d/(3*ease*ease),slope*d*d/(ease*ease));
                if(d<=run-20)return(start.Y+slope*(d-2*ease/3),slope);
                return(end.Y-slope*(run-d)*(run-d)*(run-d)/1200,slope*(run-d)*(run-d)/400);
            }
            var previous=start;
            for(int i=0;i<planar.Count;i++)
            {
                var c=planar[i];float from=Directions.Dot(c.P0-start,direction),to=Directions.Dot(c.P3-start,direction);
                var a=At(from);var b=At(to);float step=(to-from)/3;
                var p1=new Vec3(c.P1.X,a.Height+a.Grade*step,c.P1.Z);
                var p2=new Vec3(c.P2.X,b.Height-b.Grade*step,c.P2.Z);
                var next=i+1==planar.Count?end:new Vec3(c.P3.X,b.Height,c.P3.Z);
                yield return new Bezier3(previous,p1,p2,next);previous=next;
            }
        }

        internal static IEnumerable<Bezier3> Profile(Vec3 start, Vec3 end, Vec3 direction)
        {
            float length = Directions.Dot(end - start, direction), delta = end.Y - start.Y;
            if (Math.Abs(delta) < .00001f) { yield return Bezier3.Line(start, end); yield break; }
            float ease = Math.Min(20, length / 3), slope = delta / (length - ease);
            var first = start + direction * ease + new Vec3(0, slope * ease / 2, 0);
            var last = end - direction * ease - new Vec3(0, slope * ease / 2, 0);
            yield return new Bezier3(start, start + direction * (ease / 3),
                first - direction * (ease / 3) - new Vec3(0, slope * ease / 3, 0), first);
            yield return Bezier3.Line(first, last);
            yield return new Bezier3(last, last + direction * (ease / 3) + new Vec3(0, slope * ease / 3, 0),
                end - direction * (ease / 3), end);
        }

        internal static IReadOnlyList<EndpointPrefabSupportRule> SupportRules(EndpointArm[] arms, EndpointGridArmRoads[] roads, RoadInfo ramp, CloverleafRoadDesign design)
        {
            var result = new List<EndpointPrefabSupportRule>(design.SupportRules);
            foreach (var road in roads.SelectMany(r => new[] { r.Through, r.Collector }).Append(ramp))
                if (!result.Any(rule => rule.Prefab == road.Name)) result.Add(new EndpointPrefabSupportRule(road.Name,
                    road.ElevatedLength > 0 ? road.ElevatedLength : 80, road.MiddlePillarSpacing, road.PillarWidth > 0 ? road.PillarWidth : 3));
            foreach (var arm in arms)
            {
                string name = arm.Endpoints[0].Prefab;
                if (result.Any(rule => rule.Prefab == name)) continue;
                var rule = design.BridgeRules.FirstOrDefault(r => r.Arm == arm.Index);
                result.Add(new EndpointPrefabSupportRule(name, rule?.MaxLength ?? 80, rule?.MiddleSpacing, rule?.PillarWidth ?? 3));
            }
            return result.AsReadOnly();
        }
    }
}
