using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

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

        /// <summary>被交道路（双幅）在匝道分出点和汇入点之间用的道路（车道数 = 被交道路 − 匝道）；null 表示不变。目前只有定向式使用。</summary>
        public string? CrossReducedPrefab { get; set; }

        /// <summary>减车道后的被交道路宽度（米）。</summary>
        public float CrossReducedWidth { get; set; }

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

        /// <summary>环形匝道半径（喇叭形、苜蓿叶等类型使用）；0 = 自动，由模板取能放下的最小半径（见 RadiusSearch）。</summary>
        public float LoopRadius { get; set; } = 40f;

        /// <summary>喇叭形的环道用于驶出主线（主线左转进支路）；false 时用于驶入主线（支路左转进主线）。</summary>
        public bool TrumpetLoopForExit { get; set; } = true;

        /// <summary>喇叭形近侧直连匝道的半径（米）；0 = 自动（满足坡长约束的最小值）。</summary>
        public float DirectRampRadius { get; set; }

        /// <summary>
        /// 苜蓿叶：右转匝道贴着环道外侧走，与环道相邻的那段两者合成一条双向匝道（SharedRampPrefab，宽 SharedRampWidth），
        /// 两端再分开各自接主线 / 被交道路。false 时右转匝道是环道外侧的大弧线。
        /// </summary>
        public bool OuterHugsLoop { get; set; }

        /// <summary>
        /// 苜蓿叶：主线每一幅外侧加一条集散车道（用匝道道路，单行），四条匝道都接在集散车道上，交织移到集散车道；
        /// 主线只有一处驶出（进集散车道）和一处汇入（从集散车道回来），其间少掉集散车道的车道数。
        /// </summary>
        public bool CollectorDistributor { get; set; }

        /// <summary>苜蓿叶：被交道路两幅外侧也加集散车道（两条路都是快速路时用）。</summary>
        public bool CrossCollectorDistributor { get; set; }

        /// <summary>部分苜蓿叶：环道用于驶出主线（B2，默认），否则用于驶入主线（A2）。</summary>
        public bool ParcloLoopsForExit { get; set; } = true;

        public string? SharedRampPrefab { get; set; }

        public float SharedRampWidth { get; set; }

        /// <summary>环岛式：环岛用的单行道路（宽 RingWidth）；环岛半径用 LoopRadius（0 = 自动）。</summary>
        public string? RingPrefab { get; set; }

        public float RingWidth { get; set; }

        /// <summary>定向 / 半定向 T 型：两层（支路 → 远侧幅的左转匝道从主线下面穿过），否则三层（它在第三层跨过）。</summary>
        public bool DirectionalTwoLevel { get; set; }

        /// <summary>环岛式：环岛沿主线拉长成跑道形（两头半圆、中间直线），否则是圆形。</summary>
        public bool RoundaboutRacetrack { get; set; }

        /// <summary>定向式：左转匝道左出左进（在中央分隔带一侧驶出、汇入，立交内两条路的分隔带拉宽），否则右出右进。</summary>
        public bool StackLeftExit { get; set; }

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

        /// <summary>
        /// T 型三向臂：主线沿 0°/180°（臂 0 / 臂 2），支路沿 branchAngle（臂 1，从主线臂 0 顺时针量，0~360°）。
        /// branchAngle 小于 180° 时支路在主线臂 0 方向的右侧（从上往下看顺时针），大于 180° 时在左侧。
        /// </summary>
        public static List<LegSpec> TLegs(float branchAngle, int mainLevel, int branchLevel) => new List<LegSpec>
        {
            new LegSpec(0f, mainLevel),
            new LegSpec(branchAngle, branchLevel),
            new LegSpec(180f, mainLevel),
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

        /// <summary>
        /// 参数的文字指纹（全部可写属性、每条臂、每层高度）：参数不变时模板可以复用上次的计算结果
        /// （鼠标移动只改变摆放位置，不改变立交的局部形状）。
        /// </summary>
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            AppendProperties(sb, this);
            foreach (var leg in Legs)
                AppendProperties(sb, leg);
            foreach (var level in Levels)
                sb.Append(level.Elevation.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            return sb.ToString();
        }

        private static void AppendProperties(StringBuilder sb, object owner)
        {
            foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                    continue;
                object? value = property.GetValue(owner);
                if (value is IEnumerable && !(value is string))
                    continue;
                sb.Append(property.Name).Append('=');
                if (value != null && value.GetType().IsValueType && !value.GetType().IsPrimitive && !value.GetType().IsEnum)
                    AppendProperties(sb, value);   // 结构体（如 HeightRange）按字段展开
                else
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                sb.Append('|');
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
