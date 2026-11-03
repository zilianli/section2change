using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Unity.Entities;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    public static class OpenEndpointReader
    {
        public static bool SameNode(SelectedEndpoint a,SelectedEndpoint b) => NodePlacementSnapshot.SameEndpoint(a,b);
        public static Entity FromHit(EntityManager em, Entity hit, float3 position)
        {
            if (hit == Entity.Null || !em.Exists(hit)) return Entity.Null;
            if (em.HasComponent<Temp>(hit)) hit=em.GetComponentData<Temp>(hit).m_Original;
            if (hit == Entity.Null || !em.Exists(hit) || em.HasComponent<Deleted>(hit)) return Entity.Null;
            if (em.HasComponent<Node>(hit))
            {
                var p=em.GetComponentData<Node>(hit).m_Position;
                return math.distance(p.xz,position.xz)<=32 ? hit:Entity.Null;
            }
            if (!em.HasComponent<Edge>(hit)) return Entity.Null;
            var edge=em.GetComponentData<Edge>(hit);
            if (!em.HasComponent<Node>(edge.m_Start)||!em.HasComponent<Node>(edge.m_End)) return Entity.Null;
            var a=em.GetComponentData<Node>(edge.m_Start).m_Position;
            var b=em.GetComponentData<Node>(edge.m_End).m_Position;
            int choice=OpenEndpointChoice.Nearest(new Vec3(a.x,a.y,a.z),new Vec3(b.x,b.y,b.z),new Vec3(position.x,position.y,position.z),32);
            return choice<0 ? Entity.Null : choice==0 ? edge.m_Start:edge.m_End;
        }
        public static bool TryRead(EntityManager em, PrefabSystem prefabs, IReadOnlyDictionary<string,RoadInfo> roads,
            Entity node, out SelectedEndpoint port, out string error)
        {
            port = null; error = "请选择道路开放端点（只有一条道路相连）";
            if (node == Entity.Null || !em.Exists(node) || em.HasComponent<Temp>(node)
                || em.HasComponent<Deleted>(node) || em.HasComponent<Owner>(node)
                || !em.HasComponent<Node>(node) || !em.HasBuffer<ConnectedEdge>(node)) return false;
            Entity roadEdge = Entity.Null;
            foreach (var ce in em.GetBuffer<ConnectedEdge>(node,true))
            {
                var e = ce.m_Edge;
                if (!em.Exists(e) || em.HasComponent<Deleted>(e)) continue;
                // Other networks at this node are not valid open-road selections either.
                if (roadEdge != Entity.Null || em.HasComponent<Temp>(e) || em.HasComponent<Owner>(e) || !em.HasComponent<Edge>(e)
                    || !em.HasComponent<Curve>(e) || !em.HasComponent<PrefabRef>(e)) return false;
                roadEdge = e;
            }
            if (roadEdge == Entity.Null) return false;
            if (!prefabs.TryGetPrefab(em.GetComponentData<PrefabRef>(roadEdge).m_Prefab,out PrefabBase prefab)
                || !roads.TryGetValue(prefab.name,out var road)) return false;
            if (!road.IsOneWay && road.ForwardLanes != road.BackwardLanes)
            { error = "暂不支持每个方向车道数不相等的双向道路"; return false; }
            var edge = em.GetComponentData<Edge>(roadEdge);
            if (edge.m_Start != node && edge.m_End != node) return false;
            bool start = edge.m_Start == node;
            var c = em.GetComponentData<Curve>(roadEdge).m_Bezier;
            float3 tangent = start ? MathUtils.StartTangent(c) : -MathUtils.EndTangent(c);
            var p = em.GetComponentData<Node>(node).m_Position;
            port = new SelectedEndpoint(node.Index,node.Version,new Vec3(p.x,p.y,p.z),
                new Vec3(tangent.x,tangent.y,tangent.z),road.IsOneWay,start == (road.ForwardLanes>0),road.Width,road.LanesPerDirection,road.Name);
            error = null; return true;
        }
    }
}
