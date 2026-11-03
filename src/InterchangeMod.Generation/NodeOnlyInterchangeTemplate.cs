using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Endpoints;

namespace InterchangeMod.Generation
{
    /// <summary>Fail-closed legacy adapter for a node-only interchange.</summary>
    public sealed class NodeOnlyInterchangeTemplate : IInterchangeTemplate
    {
        public NodeOnlyInterchangeTemplate(InterchangeType type=InterchangeType.CompactGrid)
        {
            if(EndpointTypeCatalog.Find(type)?.NodeOnly!=true)throw new ArgumentOutOfRangeException(nameof(type));
            Type=type;
        }
        public InterchangeType Type {get;}
        public string DisplayName=>"井字立交";
        public int LegCount=>4;
        public IReadOnlyList<string> ValidateParams(InterchangeParams p)=>Array.AsReadOnly(new[]{DisplayName+"当前仅支持点选节点预览；四个方向各选一个双向端点，或一进一出的两个单向端点。"});
        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)=>Array.Empty<RoadSegmentSpec>();
    }
}
