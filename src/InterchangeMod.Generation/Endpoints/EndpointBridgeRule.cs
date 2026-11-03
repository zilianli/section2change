namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Support metadata for the actual prefab of a generated segment, including reduced roads.</summary>
    public sealed class EndpointPrefabSupportRule
    {
        public EndpointPrefabSupportRule(string prefab,float maxLength,float? middleSpacing,float pillarWidth)
        {Prefab=prefab;MaxLength=maxLength;MiddleSpacing=middleSpacing;PillarWidth=pillarWidth;}
        public string Prefab { get; }
        public float MaxLength { get; }
        public float? MiddleSpacing { get; }
        public float PillarWidth { get; }
    }
    /// <summary>Prefab support rules for a full-width through carriageway.</summary>
    public sealed class EndpointBridgeRule
    {
        public EndpointBridgeRule(int arm,float maxLength,float? middleSpacing,float width,float? pillarWidth=null,
            HeightRange? groundHeight=null,HeightRange? elevatedHeight=null)
        {Arm=arm;MaxLength=maxLength;MiddleSpacing=middleSpacing;Width=width;PillarWidth=pillarWidth??4;
         GroundHeight=groundHeight;ElevatedHeight=elevatedHeight;}
        public int Arm { get; }
        public float MaxLength { get; }
        public float? MiddleSpacing { get; }
        public float Width { get; }
        public float PillarWidth { get; }
        public HeightRange? GroundHeight { get; }
        public HeightRange? ElevatedHeight { get; }
    }
}
