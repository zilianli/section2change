using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Bounded alternate support-node search; every edge includes automatic/middle supports.</summary>
    public static class BridgeSupportSearch
    {
        public static IReadOnlyList<float>? Find(float length,float minimumSpacing,float maxLength,float? middleSpacing,
            Func<float,bool> supportClear,int maxNodes=256)
        {
            bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
            if(!Finite(length)||length<=0||!Finite(minimumSpacing)||minimumSpacing<=0||!Finite(maxLength)||maxLength<=0
                ||(middleSpacing.HasValue&&(!Finite(middleSpacing.Value)||middleSpacing.Value<0))
                ||supportClear==null||maxNodes<2||maxNodes>1024||Math.Ceiling(length/2)+1>maxNodes)return null;
            var cache=new Dictionary<float,bool>();
            bool Clear(float at)
            {if(!cache.TryGetValue(at,out bool clear)){clear=supportClear(at);cache.Add(at,clear);}return clear;}
            if(!Clear(0)||!Clear(length))return null;
            var nodes=new List<float>{0};
            for(float x=2;x<length;x+=2)
                if(x>=minimumSpacing&&length-x>=minimumSpacing&&Clear(x))nodes.Add(x);
            nodes.Add(length);
            var cost=new int[nodes.Count];var previous=new int[nodes.Count];
            for(int i=1;i<nodes.Count;i++)
            {
                cost[i]=int.MaxValue;previous[i]=-1;
                for(int j=0;j<i;j++)
                {
                    if(cost[j]==int.MaxValue||cost[j]+1>=cost[i]||nodes[i]-nodes[j]<minimumSpacing)continue;
                    bool clear=true;
                    foreach(float at in PillarPlanner.InnerPillars(nodes[j],nodes[i],middleSpacing,maxLength))
                        if(!Clear(at)){clear=false;break;}
                    if(clear){cost[i]=cost[j]+1;previous[i]=j;}
                }
            }
            if(previous[nodes.Count-1]<0)return null;
            var result=new List<float>();
            for(int at=nodes.Count-1;;at=previous[at])
            {result.Add(nodes[at]);if(at==0)break;}
            result.Reverse();return result.AsReadOnly();
        }
    }
}
