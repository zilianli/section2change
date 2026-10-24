using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation
{
    /// <summary>
    /// 立交生成参数。长度单位为米，角度单位为度。
    /// 坐标约定与 Unity 一致：Y 轴向上；局部坐标原点是立交中心，Y = 离地高度。
    /// </summary>
    public sealed class InterchangeParams
    {
        /// <summary>各层及其离地高度。汇入臂通过下标引用。</summary>
        public List<LevelSpec> Levels { get; set; } = new List<LevelSpec>
        {
            new LevelSpec("Ground", 0f),
            new LevelSpec("Upper", 10f),
        };

        /// <summary>汇入臂。四向立交约定：0、2 为主线两端，1、3 为被交道路两端。</summary>
        public List<LegSpec> Legs { get; set; } = CrossLegs(90f, mainLevel: 0, crossLevel: 1);

        /// <summary>匝道预制体名（必须是单行道路）。null 表示使用玩家在道路菜单里选的道路。</summary>
        public string? RampPrefab { get; set; } = "Medium Road Oneway";

        /// <summary>匝道沿主线方向的水平总长度（分流段 + 爬坡段）。</summary>
        public float RampLength { get; set; } = 250f;

        /// <summary>匝道道路宽度（米），由工具层从预制体读出。</summary>
        public float RampWidth { get; set; } = 12f;

        /// <summary>
        /// 分流段结束时匝道外缘与主线外缘之间留出的间距。
        /// 分流段是一段直线：以 RampDivergeAngle 离开主线，直到横向分开这么远，长度由此算出（见 DiamondTemplate.TaperLength）。
        /// 游戏的分流节点会一直延伸到两条路边缘不再重叠的位置，分流段必须比它长，否则报“形状无效/重叠”。
        /// </summary>
        public float RampSideGap { get; set; } = 3f;

        /// <summary>匝道与被交道路交接点离立交中心的距离。</summary>
        public float RampTerminalOffset { get; set; } = 40f;

        /// <summary>地面辅路（单行道路，两侧各一幅）的预制体名，仅中式快速路节点使用。</summary>
        public string? FrontagePrefab { get; set; } = "Medium Road Oneway";

        /// <summary>辅路宽度（米）。</summary>
        public float FrontageWidth { get; set; } = 12f;

        // —— 车道平衡：匝道分出后主线减少车道、匝道并入后辅路增加车道 ——

        /// <summary>主线在下匝道分出点和上匝道汇入点之间用的道路（车道数 = 主线 − 匝道）；null 表示不变。</summary>
        public string? MainReducedPrefab { get; set; }

        /// <summary>减车道后的主线宽度（米）。中段按不变车道一侧（内侧）对齐，中心线向内平移半个宽度差；0 表示不知道宽度（仍居中）。</summary>
        public float MainReducedWidth { get; set; }

        /// <summary>辅路在匝道落地点和上匝道分出点之间用的道路（车道数 = 辅路 + 匝道）；null 表示不变。</summary>
        public string? FrontageWidenedPrefab { get; set; }

        /// <summary>加宽后的辅路宽度（米）。加宽段按不变车道一侧（外侧）对齐，中心线向内平移半个宽度差；0 表示不加宽。</summary>
        public float FrontageWidenedWidth { get; set; }

        /// <summary>主线中段（减车道）中心线向中央分隔带平移的距离：保持内侧（直行车道）边缘不变。</summary>
        public float MainDropShift => MainReducedPrefab != null && MainReducedWidth > 0f ? (Legs[0].RoadWidth - MainReducedWidth) / 2f : 0f;

        /// <summary>辅路加宽段中心线向主线一侧平移的距离：保持外侧（直行车道）边缘不变，新增车道在匝道并入的内侧。</summary>
        public float FrontageAddShift => FrontageWidenedPrefab != null && FrontageWidenedWidth > 0f ? (FrontageWidenedWidth - FrontageWidth) / 2f : 0f;

        /// <summary>臂端过渡段（双幅 ↔ 双向）中每幅与中心线的夹角（度）。</summary>
        public float LegTransitionAngle { get; set; } = 12f;

        /// <summary>匝道在分流/合流节点处与主线的夹角（度）。</summary>
        public float RampDivergeAngle { get; set; } = 15f;

        /// <summary>环形匝道半径（苜蓿叶等类型使用）。</summary>
        public float LoopRadius { get; set; } = 60f;

        /// <summary>允许的最大纵坡，0.08 = 8%。</summary>
        public float MaxGrade { get; set; } = 0.08f;

        /// <summary>true 为右侧通行（中国大陆、美国等）。</summary>
        public bool RightHandTraffic { get; set; } = true;

        public float ElevationOf(LegSpec leg) => Levels[leg.Level].Elevation;

        /// <summary>
        /// 十字型四向臂：主线沿 0°/180°，被交道路沿 crossingAngle / crossingAngle + 180°。
        /// </summary>
        public static List<LegSpec> CrossLegs(float crossingAngle, int mainLevel, int crossLevel) => new List<LegSpec>
        {
            new LegSpec(0f, mainLevel),
            new LegSpec(crossingAngle, crossLevel),
            new LegSpec(180f, mainLevel),
            new LegSpec(crossingAngle + 180f, crossLevel),
        };

        /// <summary>把同一条道路的两端（如主线臂 0/2）设成相同的形式、宽度和预制体。</summary>
        public void SetRoad(int legA, int legB, RoadForm form, float roadWidth, float medianWidth, string? prefab)
        {
            foreach (var leg in new[] { Legs[legA], Legs[legB] })
            {
                leg.Form = form;
                leg.RoadWidth = roadWidth;
                leg.MedianWidth = medianWidth;
                leg.RoadPrefab = prefab;
            }
        }

        public InterchangeParams Clone()
        {
            var copy = (InterchangeParams)MemberwiseClone();
            copy.Levels = Levels.Select(l => l.Clone()).ToList();
            copy.Legs = Legs.Select(l => l.Clone()).ToList();
            return copy;
        }

        /// <summary>所有模板共用的基础检查：臂数量、层下标。</summary>
        public List<string> ValidateLegsAndLevels(int expectedLegCount)
        {
            var errors = new List<string>();
            if (Legs.Count != expectedLegCount)
                errors.Add($"需要 {expectedLegCount} 条汇入臂，当前 {Legs.Count} 条");
            for (int i = 0; i < Legs.Count; i++)
            {
                if (Legs[i].Level < 0 || Legs[i].Level >= Levels.Count)
                    errors.Add($"汇入臂 {i} 引用了不存在的层 {Legs[i].Level}");
            }
            if (MaxGrade <= 0f)
                errors.Add("最大坡度必须大于 0");
            return errors;
        }
    }
}
