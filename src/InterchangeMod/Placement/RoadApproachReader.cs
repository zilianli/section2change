using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Unity.Entities;
namespace InterchangeMod.Placement
{
    public static class RoadApproachReader
    {
        public static RoadApproachTrace Read(EntityManager em,PrefabSystem prefabs,IReadOnlyDictionary<string,RoadInfo> roads,
            SelectedEndpoint origin,float limit,out string stopReason)
        {
            var pieces=new List<RoadTracePiece>();
            var node=new Entity{Index=origin.NodeIndex,Version=origin.NodeVersion};
            Entity previous=Entity.Null;var seen=new HashSet<Entity>();double length=0;
            stopReason="达到退接上限";
            for(int count=0;count<128 && length<limit+10;count++)
            {
                if(!em.Exists(node)||!em.HasBuffer<ConnectedEdge>(node)) {stopReason="道路链结束";break;}
                var adjacent=new List<Entity>();
                foreach(var ce in em.GetBuffer<ConnectedEdge>(node,true))
                    if(em.Exists(ce.m_Edge)&&!em.HasComponent<Deleted>(ce.m_Edge)) adjacent.Add(ce.m_Edge);
                if(adjacent.Count!=(previous==Entity.Null?1:2)) {stopReason="遇到路口或道路尽头";break;}
                var edgeEntity=adjacent.Find(e=>e!=previous);
                if(edgeEntity==Entity.Null||!seen.Add(edgeEntity)||em.HasComponent<Owner>(edgeEntity)||em.HasComponent<Temp>(edgeEntity)
                    ||!em.HasComponent<Edge>(edgeEntity)||!em.HasComponent<Curve>(edgeEntity)||!em.HasComponent<PrefabRef>(edgeEntity))
                {stopReason="遇到不可改造道路或循环";break;}
                if(!prefabs.TryGetPrefab(em.GetComponentData<PrefabRef>(edgeEntity).m_Prefab,out PrefabBase prefab)
                    ||!roads.TryGetValue(prefab.name,out var info)||prefab.name!=origin.Prefab||info.Width!=origin.Width)
                {stopReason="路型或宽度变化";break;}
                var edge=em.GetComponentData<Edge>(edgeEntity);bool start=edge.m_Start==node;
                if(!start&&edge.m_End!=node) {stopReason="道路链无效";break;}
                if(info.IsOneWay!=origin.OneWay || (info.IsOneWay && (start==(info.ForwardLanes>0))!=origin.TravelOutward))
                {stopReason="行驶方向变化";break;}
                var w=em.GetComponentData<Curve>(edgeEntity).m_Bezier;
                var c=new Bezier3(new Vec3(w.a.x,w.a.y,w.a.z),new Vec3(w.b.x,w.b.y,w.b.z),new Vec3(w.c.x,w.c.y,w.c.z),new Vec3(w.d.x,w.d.y,w.d.z));
                if(!start)c=c.Reverse();
                var outer=start?edge.m_End:edge.m_Start;
                pieces.Add(new RoadTracePiece(edgeEntity.Index,edgeEntity.Version,c,!start,outer.Index,outer.Version));
                length+=RoadRetreat.HorizontalLength(c);previous=edgeEntity;node=outer;
                if(count==127)stopReason="道路链预算上限";
            }
            return new RoadApproachTrace(origin,pieces);
        }
    }
}
