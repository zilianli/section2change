namespace InterchangeMod.Generation
{
    /// <summary>
    /// 立交中的一层。Elevation 是离地高度（米）：正数=高架，0=地面，负数=下沉/隧道。
    /// 落地层按每个点的地形高度换算成世界高度，所以坡地上各层仍保持这个离地高度。
    /// </summary>
    public sealed class LevelSpec
    {
        public LevelSpec(string name, float elevation)
        {
            Name = name;
            Elevation = elevation;
        }

        public string Name { get; set; }

        public float Elevation { get; set; }

        public LevelSpec Clone() => new LevelSpec(Name, Elevation);
    }
}
