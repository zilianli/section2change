using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Tools;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Simulation;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    /// <summary>Prepares one native temporary batch, retaining world Y and logical/physical fork offsets.</summary>
    public static class NodePreviewPlacer
    {
        internal readonly struct PreparedRemoval
        {
            internal PreparedRemoval(Entity entity, Edge edge, Bezier4x3 curve) { Entity=entity;Edge=edge;Curve=curve; }
            internal readonly Entity Entity; internal readonly Edge Edge; internal readonly Bezier4x3 Curve;
        }
        internal readonly struct PreparedSegment
        {
            internal PreparedSegment(Entity prefab,Bezier4x3 curve,float startElevation,float endElevation,int seed,
                Entity start,Entity end,float3 startPosition,float3 endPosition)
            { Prefab=prefab;Curve=curve;StartElevation=startElevation;EndElevation=endElevation;Seed=seed;
              Start=start;End=end;StartPosition=startPosition;EndPosition=endPosition; }
            internal readonly Entity Prefab,Start,End;
            internal readonly Bezier4x3 Curve;
            internal readonly float StartElevation,EndElevation;
            internal readonly int Seed;
            internal readonly float3 StartPosition,EndPosition;
        }
        public sealed class PreparedBatch
        {
            internal PreparedBatch(IEnumerable<PreparedRemoval> removals,IEnumerable<PreparedSegment> segments,string fingerprint)
            { Removals=removals.ToArray();Segments=segments.ToArray();Fingerprint=fingerprint; }
            internal readonly PreparedRemoval[] Removals;
            internal readonly PreparedSegment[] Segments;
            public string Fingerprint { get; }
        }
        private static float3 V(Vec3 p) => new float3(p.X,p.Y,p.Z);
        private static bool Finite(float3 p) => math.all(math.isfinite(p));
        private static bool Same(Bezier4x3 w,Bezier3 c) => math.all(w.a==V(c.P0))&&math.all(w.b==V(c.P1))
            &&math.all(w.c==V(c.P2))&&math.all(w.d==V(c.P3));
        private static void Key(StringBuilder b,Entity e) => b.Append(e.Index).Append(':').Append(e.Version).Append('|');
        private static void Number(StringBuilder b,float f) => b.Append(f.ToString("R",CultureInfo.InvariantCulture)).Append('|');
        private static void Point(StringBuilder b,float3 p) { Number(b,p.x);Number(b,p.y);Number(b,p.z); }
        private static void CurveKey(StringBuilder b,Bezier4x3 c) { Point(b,c.a);Point(b,c.b);Point(b,c.c);Point(b,c.d); }
        private static bool ValidPrefab(EntityManager em,Entity e) => e!=Entity.Null&&em.Exists(e)
            &&!em.HasComponent<Deleted>(e)&&!em.HasComponent<Owner>(e)&&em.HasComponent<PrefabData>(e);
        private static bool ReadNode(EntityManager em,Entity e,StringBuilder fingerprint,out float3 position)
        {
            position=default;
            if(e==Entity.Null||!em.Exists(e)||em.HasComponent<Deleted>(e)||em.HasComponent<Temp>(e)
                ||em.HasComponent<Owner>(e)||!em.HasComponent<Node>(e))return false;
            position=em.GetComponentData<Node>(e).m_Position;
            if(!Finite(position))return false;
            Key(fingerprint,e);Point(fingerprint,position);
            if(em.HasComponent<PrefabRef>(e))
            {
                var prefab=em.GetComponentData<PrefabRef>(e).m_Prefab;
                if(!ValidPrefab(em,prefab))return false;
                Key(fingerprint,prefab);
            }
            else fingerprint.Append("no-node-prefab|");
            // Native temporary siblings must not alter original-world connectivity identity.
            var adjacent=new List<Entity>();
            if(em.HasBuffer<ConnectedEdge>(e))
                foreach(var ce in em.GetBuffer<ConnectedEdge>(e,true))
                    if(em.Exists(ce.m_Edge)&&!em.HasComponent<Deleted>(ce.m_Edge)&&!em.HasComponent<Temp>(ce.m_Edge))adjacent.Add(ce.m_Edge);
            fingerprint.Append(adjacent.Count).Append('|');
            foreach(var edge in adjacent.OrderBy(x=>x.Index).ThenBy(x=>x.Version))Key(fingerprint,edge);
            return true;
        }

        /// <summary>Read only: no ECB is accepted or written, including on validation failure.</summary>
        public static List<string> Prepare(SpatialCandidateResult candidate,IReadOnlyList<EndpointArm> arms,string rampName,
            ref TerrainHeightData terrain,PrefabResolver prefabs,NetPrefab fallback,int seed,EntityManager em,
            IReadOnlyList<RoadRetreatResult> retreats,IReadOnlyList<RoadApproachTrace> sources,out PreparedBatch prepared)
        {
            prepared=null;
            var errors=new List<string>();
            List<string> Fail(string text) { errors.Add(text);return errors; }
            if(candidate==null||candidate.Failure!=SpatialCandidateFailure.None||candidate.Segments.Count==0)
                return Fail("候选生成未通过");
            NodePlacementSnapshot snapshot;
            try { snapshot=NodePlacementSnapshot.Capture(sources,retreats); }
            catch(ArgumentException) { return Fail("原路退接快照无效，请重新生成预览"); }
            var fingerprint=new StringBuilder(snapshot.Fingerprint);
            var nodes=new Dictionary<Vec3,Entity>();
            var positions=new Dictionary<Entity,float3>();
            var originals=new Dictionary<Entity,(Edge Edge,Bezier4x3 Curve)>();
            foreach(var source in sources)
            {
                var inner=new Entity{Index=source.Origin.NodeIndex,Version=source.Origin.NodeVersion};
                if(!ReadNode(em,inner,fingerprint,out var originPosition))return Fail("原路端点已变化，请重新生成预览");
                if(!math.all(originPosition==V(source.Origin.Position)))return Fail("原路端点位置已变化，请重新生成预览");
                positions[inner]=originPosition;
                foreach(var part in source.Pieces)
                {
                    var e=new Entity{Index=part.EdgeIndex,Version=part.EdgeVersion};
                    var outer=new Entity{Index=part.OuterNodeIndex,Version=part.OuterNodeVersion};
                    if(!em.Exists(e)||em.HasComponent<Deleted>(e)||em.HasComponent<Owner>(e)||em.HasComponent<Temp>(e)
                        ||!em.HasComponent<Edge>(e)||!em.HasComponent<Curve>(e)||!em.HasComponent<PrefabRef>(e))
                        return Fail("原路路廊已变化，请重新生成预览");
                    var edge=em.GetComponentData<Edge>(e);
                    if(edge.m_Start!=(part.Reversed?outer:inner)||edge.m_End!=(part.Reversed?inner:outer)||inner==outer)
                        return Fail("原路连接节点已变化，请重新生成预览");
                    var prefab=em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if(!ValidPrefab(em,prefab))return Fail("原路预制体已变化，请重新生成预览");
                    var curve=em.GetComponentData<Curve>(e).m_Bezier;
                    if(!Same(curve,part.Reversed?part.Curve.Reverse():part.Curve))return Fail("原路曲线已变化，请重新生成预览");
                    if(!ReadNode(em,outer,fingerprint,out var outerPosition))return Fail("原路接回节点已变化，请重新生成预览");
                    positions[outer]=outerPosition;
                    Key(fingerprint,e);Key(fingerprint,edge.m_Start);Key(fingerprint,edge.m_End);Key(fingerprint,prefab);CurveKey(fingerprint,curve);
                    originals[e]=(edge,curve);inner=outer;
                }
            }
            var removals=new List<PreparedRemoval>();
            var removed=new HashSet<Entity>();
            foreach(var cut in retreats)
            {
                var p=cut.Port;
                if(nodes.ContainsKey(p.Position))return Fail("多个端点坐标重合，请重新选择");
                var binding=new Entity{Index=cut.BindingNodeIndex,Version=cut.BindingNodeVersion};
                if(binding!=Entity.Null&&!positions.ContainsKey(binding))return Fail("接回节点已变化，请重新生成预览");
                nodes.Add(p.Position,binding);
                foreach(var part in cut.Consumed)
                {
                    var e=new Entity{Index=part.EdgeIndex,Version=part.EdgeVersion};
                    if(!removed.Add(e))return Fail("两个方向的退接改造范围重叠");
                    if(!originals.TryGetValue(e,out var original))return Fail("退接范围原路缺失，请重新生成预览");
                    removals.Add(new PreparedRemoval(e,original.Edge,original.Curve));
                }
            }
            var segments=new List<PreparedSegment>();
            var raw=new List<(Bezier3 Curve,string Road,Entity Start,Entity End,float3 StartPosition,float3 EndPosition)>();
            foreach(var s in candidate.Segments)
            {
                var incoming=arms.FirstOrDefault(a=>a.Index==s.SourceArm)?.Endpoints.FirstOrDefault(p=>!p.TravelOutward);
                string road=s.Prefab??(s.Kind==CloverEdgeKind.Main?incoming?.Prefab:rampName);
                if(string.IsNullOrEmpty(road))return Fail("候选道路预制体缺失");
                nodes.TryGetValue(s.StartNode,out var start);nodes.TryGetValue(s.EndNode,out var end);
                raw.Add((s.WorldCurve,road,start,end,start==Entity.Null?V(s.StartNode):positions[start],end==Entity.Null?V(s.EndNode):positions[end]));
            }
            foreach(var cut in retreats)
                if(cut.Remainder is Bezier3 tail)
                {
                    var last=cut.Consumed[cut.Consumed.Count-1];
                    var outer=new Entity{Index=last.OuterNodeIndex,Version=last.OuterNodeVersion};
                    if(!positions.TryGetValue(outer,out var outerPosition))return Fail("保留尾段外端节点已变化");
                    if(last.Reversed)tail=tail.Reverse();
                    raw.Add((tail,cut.Port.Prefab,last.Reversed?outer:Entity.Null,last.Reversed?Entity.Null:outer,
                        last.Reversed?outerPosition:V(tail.P0),last.Reversed?V(tail.P3):outerPosition));
                }
            foreach(var s in raw)
            {
                var c=s.Curve;var w=new Bezier4x3(V(c.P0),V(c.P1),V(c.P2),V(c.P3));
                if(!Finite(w.a)||!Finite(w.b)||!Finite(w.c)||!Finite(w.d)||!Finite(s.StartPosition)||!Finite(s.EndPosition))
                    return Fail("候选道路坐标无效");
                if(!prefabs.TryResolve(s.Road,fallback,out var prefab))return Fail($"找不到道路：{s.Road}");
                float ea=InterchangePlacer.TerrainElevation(w.a,ref terrain),eb=InterchangePlacer.TerrainElevation(w.b,ref terrain),
                    ec=InterchangePlacer.TerrainElevation(w.c,ref terrain),ed=InterchangePlacer.TerrainElevation(w.d,ref terrain);
                if(!math.isfinite(ea)||!math.isfinite(eb)||!math.isfinite(ec)||!math.isfinite(ed))return Fail("地形高度无效");
                if(!prefabs.TryGetEntityForElevation(prefab,math.min(math.min(ea,eb),math.min(ec,ed)),math.max(math.max(ea,eb),math.max(ec,ed)),out var entity,out var error))
                    return Fail($"{s.Road}：{error}");
                if(!ValidPrefab(em,entity))return Fail($"道路预制体已失效：{s.Road}");
                var segment=new PreparedSegment(entity,w,ea,ed,seed+segments.Count,s.Start,s.End,s.StartPosition,s.EndPosition);
                segments.Add(segment);
                Key(fingerprint,entity);CurveKey(fingerprint,w);Key(fingerprint,s.Start);Key(fingerprint,s.End);
                Point(fingerprint,s.StartPosition);Point(fingerprint,s.EndPosition);
                Number(fingerprint,ea);Number(fingerprint,eb);Number(fingerprint,ec);Number(fingerprint,ed);
            }
            prepared=new PreparedBatch(removals,segments,fingerprint.ToString());
            return errors;
        }

        /// <summary>Writes only a fully prepared batch. Confirm never calls this method.</summary>
        private static void Emit(EntityCommandBuffer commands,PreparedBatch prepared)
        {
            foreach(var r in prepared.Removals)RoadRemover.CreateDelete(commands,r.Entity,r.Edge,r.Curve);
            foreach(var s in prepared.Segments)
                NetCourseBuilder.CreateSegment(commands,s.Prefab,s.Curve,s.StartElevation,s.EndElevation,s.Seed,s.Start,s.End,
                    startNodePosition:s.StartPosition,endNodePosition:s.EndPosition);
        }
        public static List<string> Create(EntityCommandBuffer commands,SpatialCandidateResult candidate,IReadOnlyList<EndpointArm> arms,
            string rampName,ref TerrainHeightData terrain,PrefabResolver prefabs,NetPrefab fallback,int seed,EntityManager em,
            IReadOnlyList<RoadRetreatResult> retreats,IReadOnlyList<RoadApproachTrace> sources,out PreparedBatch prepared)
        {
            var errors=Prepare(candidate,arms,rampName,ref terrain,prefabs,fallback,seed,em,retreats,sources,out prepared);
            if(errors.Count==0)Emit(commands,prepared);
            return errors;
        }
    }
}
