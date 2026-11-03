using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Net;
using Game.Rendering;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Placement;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InterchangeMod.Systems
{
    public partial class InterchangeToolSystem
    {
        private readonly List<SelectedEndpoint> m_NodeMarkers=new List<SelectedEndpoint>();
        private float3 m_NodeMarkerCenter;
        private int m_NodeMarkerAge=999;
        private static Color NodeGroupColor(int group) => group==1 ? new Color(1f,.73f,.18f,1)
            : group==2 ? new Color(.35f,.75f,1f,1) : group==3 ? new Color(.85f,.42f,1f,1) : new Color(.3f,1f,.57f,1);

        private void DrawNodeFeedback(float3? cursor,Entity hover)
        {
            if(cursor.HasValue && (++m_NodeMarkerAge>=30 || math.distancesq(cursor.Value.xz,m_NodeMarkerCenter.xz)>10000))
            {
                m_NodeMarkerAge=0; m_NodeMarkerCenter=cursor.Value;
                var tree=m_NetSearchSystem.GetNetSearchTree(true,out var treeDeps); treeDeps.Complete();
                using(var candidates=new NativeList<Entity>(Allocator.Temp))
                {
                    var iterator=new RoadRemover.BoundsIterator { m_Bounds=new Bounds2(cursor.Value.xz-600,cursor.Value.xz+600),m_Result=candidates };
                    tree.Iterate(ref iterator);
                    var seen=new HashSet<Entity>(); var found=new List<SelectedEndpoint>();
                    foreach(var hit in candidates)
                    {
                        if(!EntityManager.Exists(hit)) continue;
                        if(EntityManager.HasComponent<Edge>(hit))
                        {
                            var e=EntityManager.GetComponentData<Edge>(hit); Add(e.m_Start); Add(e.m_End);
                        }
                        else Add(hit);
                    }
                    void Add(Entity node)
                    {
                        if(!seen.Add(node)) return;
                        if(OpenEndpointReader.TryRead(EntityManager,m_PrefabSystem,m_RoadsByName,node,out var p,out _)
                            && math.distancesq(new float2(p.Position.X,p.Position.Z),m_NodeMarkerCenter.xz)<=360000) found.Add(p);
                    }
                    m_NodeMarkers.Clear();
                    m_NodeMarkers.AddRange(found.OrderBy(p=>math.distancesq(new float2(p.Position.X,p.Position.Z),m_NodeMarkerCenter.xz)).Take(256));
                }
                m_NetSearchSystem.AddNetSearchTreeReader(default);
            }
            var overlay=World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            var buffer=overlay.GetBuffer(out var overlayDeps); overlayDeps.Complete();
            var arms=nodeArms;
            var all=m_NodeMarkers.Concat(arms.SelectMany(a=>a.Endpoints)).GroupBy(p=>(p.NodeIndex,p.NodeVersion)).Select(g=>g.First());
            foreach(var p in all)
            {
                var entity=NodeEntity(p);
                if(!EntityManager.Exists(entity)||!EntityManager.HasComponent<Node>(entity)) continue;
                var style=NodeFeedback.Style(p.NodeIndex,p.NodeVersion,entity==hover,arms);
                var pos=EntityManager.GetComponentData<Node>(entity).m_Position+new float3(0,.35f,0);
                Color color=style.Kind==NodeMarkerKind.Selected ? NodeGroupColor(style.GroupNumber)
                    : style.Kind==NodeMarkerKind.Hovered ? new Color(.2f,.95f,1f,.85f):new Color(.65f,.65f,.65f,.48f);
                float diameter=style.Kind==NodeMarkerKind.Selected?12:style.Kind==NodeMarkerKind.Hovered?11:8;
                buffer.DrawCircle(color,pos,diameter);
                if(style.Kind==NodeMarkerKind.Selected) DrawGroupNumber(buffer,pos+new float3(9,.2f,0),style.GroupNumber,color);
            }
            if(m_HasPreview&&!m_NodeDirty&&m_NodeRetreatPreview!=null)
                foreach(var cut in m_NodeRetreatPreview)
                {
                    foreach(var c in cut.ModifiedCurves)
                    {
                        float3 V(InterchangeMod.Generation.Geometry.Vec3 p)=>new float3(p.X,p.Y+.6f,p.Z);
                        buffer.DrawDashedCurve(new Color(1,.65f,.2f,.9f),new Bezier4x3(V(c.P0),V(c.P1),V(c.P2),V(c.P3)),1.5f,8f,4f);
                    }
                    if(cut.Port!=null && cut.Distance>0)
                        buffer.DrawCircle(new Color(1,1,1,.9f),new float3(cut.Port.Position.X,cut.Port.Position.Y+.7f,cut.Port.Position.Z),10);
                }
            overlay.AddBufferWriter(default);
        }

        // World-space line glyphs avoid adding persistent text or label entities.
        private static void DrawGroupNumber(OverlayRenderSystem.Buffer buffer,float3 center,int digit,Color color)
        {
            int mask=digit==1?0x06:digit==2?0x5b:digit==3?0x4f:0x66;
            var a=new[] {new float2(-2,3),new float2(2,3),new float2(2,0),new float2(2,-3),new float2(-2,-3),new float2(-2,0),new float2(-2,0)};
            var b=new[] {new float2(2,3),new float2(2,0),new float2(2,-3),new float2(-2,-3),new float2(-2,0),new float2(-2,3),new float2(2,0)};
            for(int i=0;i<7;i++) if((mask&(1<<i))!=0)
                buffer.DrawLine(color,new Line3.Segment(center+new float3(a[i].x,0,a[i].y),center+new float3(b[i].x,0,b[i].y)),1f,false);
        }
    }
}
