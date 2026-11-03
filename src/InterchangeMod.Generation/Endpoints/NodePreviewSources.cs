using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Orders original game corridors to the solver's actual cut order.</summary>
    public static class NodePreviewSources
    {
        public static IReadOnlyList<RoadApproachTrace> Bind(IReadOnlyList<RoadApproachTrace> actualTraces,
            IReadOnlyList<RoadRetreatResult> actualCuts)
        {
            if(actualTraces==null||actualCuts==null||actualTraces.Count==0||actualTraces.Count!=actualCuts.Count)
                throw new ArgumentException("Actual source corridors and cuts must have matching nonzero counts.");
            var lookup=new Dictionary<(int,int),RoadApproachTrace>();
            foreach(var trace in actualTraces)
            {
                if(trace?.Origin==null||lookup.ContainsKey((trace.Origin.NodeIndex,trace.Origin.NodeVersion)))
                    throw new ArgumentException("Actual source corridors must have unique original endpoints.");
                lookup.Add((trace.Origin.NodeIndex,trace.Origin.NodeVersion),trace);
            }
            var ordered=new RoadApproachTrace[actualCuts.Count];
            for(int i=0;i<actualCuts.Count;i++)
            {
                var cut=actualCuts[i];
                if(cut?.Port==null||!lookup.TryGetValue((cut.Port.NodeIndex,cut.Port.NodeVersion),out var source))
                    throw new ArgumentException("A cut does not identify an actual source corridor.");
                ordered[i]=source;
            }
            // Re-cut actual curves to reject same-ID proxy geometry, forged bindings, duplicate cuts and tails.
            NodePlacementSnapshot.Capture(ordered,actualCuts);
            return Array.AsReadOnly(ordered);
        }
    }
}
