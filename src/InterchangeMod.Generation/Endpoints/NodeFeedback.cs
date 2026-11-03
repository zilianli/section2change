using System.Collections.Generic;
using System.Linq;
namespace InterchangeMod.Generation.Endpoints
{
    public enum NodeMarkerKind { Available, Hovered, Selected }
    public readonly struct NodeMarkerStyle
    {
        public NodeMarkerStyle(NodeMarkerKind kind,int groupNumber) { Kind=kind;GroupNumber=groupNumber; }
        public NodeMarkerKind Kind { get; }
        public int GroupNumber { get; }
    }
    public static class NodeFeedback
    {
        public static bool SelectionChanged(EndpointSelectionChange change) =>
            change==EndpointSelectionChange.Added || change==EndpointSelectionChange.Removed;
        public static bool ReadyForAutoPreview(InterchangeType type,IReadOnlyList<EndpointArm> arms) =>
            EndpointTypeRequest.Create(type,arms,true,.16f,new float[arms.Count]).InputReady;
        public static NodeMarkerStyle Style(int index,int version,bool hovered,IReadOnlyList<EndpointArm> arms)
        {
            foreach(var arm in arms)
                if(arm.Endpoints.Any(p=>p.NodeIndex==index && p.NodeVersion==version))
                    return new NodeMarkerStyle(NodeMarkerKind.Selected,arm.Index+1);
            return new NodeMarkerStyle(hovered?NodeMarkerKind.Hovered:NodeMarkerKind.Available,0);
        }
    }
}
