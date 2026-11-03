using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Extends only real outer ports; a branch path may have just one outer end.</summary>
    internal static class EndpointCoreAttachment
    {
        internal static SpatialCandidateResult Attach(EndpointStackCoreResult core,IReadOnlyList<EndpointArm> arms,float maxGrade,float handle)
        {
            SpatialCandidateResult Fail(string detail)=>new SpatialCandidateResult(SpatialCandidateFailure.InvalidGeometry,
                Array.Empty<SpatialSegment>(),detail);
            if(core==null||!core.Success||arms==null||core.Segments.Count==0||core.Ports.Count!=arms.Sum(a=>a.Endpoints.Count)
                ||core.Ports.Select(p=>(p.Arm,p.TravelOutward)).Distinct().Count()!=core.Ports.Count)
                return Fail("A complete core and distinct original road ports are required.");
            var output=new List<SpatialSegment>();var used=new HashSet<(int,bool)>();
            foreach(var group in core.Segments.GroupBy(s=>s.PathId))
            {
                var path=group.ToArray();
                if(string.IsNullOrWhiteSpace(group.Key)||path.Zip(path.Skip(1),(a,b)=>a.EndNode==b.StartNode).Any(ok=>!ok))
                    return Fail("Core paths must be directed, ordered and identified.");
                var starts=core.Ports.Where(p=>!p.TravelOutward&&p.Position==path[0].WorldCurve.P0).ToArray();
                var ends=core.Ports.Where(p=>p.TravelOutward&&p.Position==path[path.Length-1].WorldCurve.P3).ToArray();
                if(starts.Length>1||ends.Length>1)return Fail("An outer curve matches more than one port.");
                SpatialCandidateResult? Extend(EndpointCorePort port)
                {
                    var arm=arms.SingleOrDefault(a=>a.Index==port.Arm);
                    var road=arm?.Endpoints.SingleOrDefault(p=>p.TravelOutward==port.TravelOutward);
                    var segment=port.TravelOutward?path[path.Length-1]:path[0];
                    if(road==null||road.Width!=port.Width||road.Prefab!=port.Prefab||segment.Prefab!=port.Prefab
                        ||segment.RoadWidth!=port.Width||!used.Add((port.Arm,port.TravelOutward)))
                        return Fail("Core outer port and original road metadata do not match.");
                    return EndpointStackSearch.BuildApproach(road,port,path[0].SourceArm,group.Key,maxGrade,handle);
                }
                if(starts.Length==1)
                {
                    var before=Extend(starts[0])!;
                    if(before.Failure!=SpatialCandidateFailure.None)return before;
                    output.AddRange(before.Segments);
                }
                output.AddRange(path);
                if(ends.Length==1)
                {
                    var after=Extend(ends[0])!;
                    if(after.Failure!=SpatialCandidateFailure.None)return after;
                    output.AddRange(after.Segments);
                }
            }
            if(used.Count!=core.Ports.Count)return Fail("Not every outer port was attached exactly once.");
            return new SpatialCandidateResult(SpatialCandidateFailure.None,output);
        }
    }
}
