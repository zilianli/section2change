using System.Collections.Generic;
using System.Linq;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Input;
using Game.Notifications;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Junctions;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Templates;
using InterchangeMod.Placement;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace InterchangeMod.Systems
{
    /// <summary>
    /// 立交工具：整座立交跟随鼠标预览，, / . 旋转，右键退出。两步放置：左键只把预览固定在原处（固定后改设置、旋转都在原处重建，
    /// 左键点别处换位置，右键取消固定），在面板点“确认放置”才真正放置——求解慢时不会因为鼠标一动就重算。
    ///
    /// 每帧流程（与原版 NetToolSystem 相同）：
    ///   预览变化：applyMode = Clear（清掉旧临时道路）→ 销毁旧定义实体 → 重新创建
    ///   预览不变：applyMode = None，保留现有临时道路
    ///   确认：applyMode = Apply，游戏把当前临时道路变成正式道路
    /// </summary>
    public partial class InterchangeToolSystem : ToolBaseSystem
    {
        public const string kToolID = "Interchange Tool";

        /// <summary>玩家没在道路菜单里选过道路时使用的预制体。</summary>
        private const string kFallbackRoadPrefab = "Medium Road";

        /// <summary>面板里没选被交道路时用的道路。</summary>
        private const string kDefaultCrossRoad = "Medium Road";

        /// <summary>找不到合适匝道时用的单行道路。</summary>
        private const string kOneWayRoadPrefab = "Medium Road Oneway";

        /// <summary>旋转一步：, / . 和滚轮每格 15°，Shift + 滚轮每格 1°。</summary>
        private const float kRotationStep = 15f * Directions.DegToRad;
        private const float kFineRotationStep = 1f * Directions.DegToRad;

        private ToolOutputBarrier m_ToolOutputBarrier;
        private NetToolSystem m_NetToolSystem;
        private TerrainSystem m_TerrainSystem;
        private CityConfigurationSystem m_CityConfigurationSystem;
        private Game.Net.SearchSystem m_NetSearchSystem;
        private EntityQuery m_DefinitionQuery;
        private PrefabResolver m_PrefabResolver;
        private readonly IInterchangeTemplate m_Diamond = new DiamondTemplate();
        private readonly IInterchangeTemplate m_Expressway = new ExpresswayNodeTemplate();
        private readonly IInterchangeTemplate m_ExpresswayT = new ExpresswayNodeTemplate(tee: true);
        private readonly IInterchangeTemplate m_SinglePoint = new SinglePointTemplate();
        private readonly IInterchangeTemplate m_Trumpet = new TrumpetTemplate();
        private readonly IInterchangeTemplate m_Cloverleaf = new CloverleafTemplate();
        private readonly IInterchangeTemplate m_Parclo = new PartialCloverleafTemplate();
        private readonly IInterchangeTemplate m_Roundabout = new RoundaboutTemplate();
        private readonly IInterchangeTemplate m_DirectionalT = new DirectionalTTemplate(directional: true);
        private readonly IInterchangeTemplate m_Stack = new StackTemplate();
        private readonly IInterchangeTemplate m_Turbine = new StackTemplate(turbine: true);
        private readonly IInterchangeTemplate m_SemiDirectionalT = new DirectionalTTemplate(directional: false);

        /// <summary>面板选的立交类型。</summary>
        /// <summary>中式快速路节点（十字或 T 型）：有地面辅路。</summary>
        private bool IsExpressway => setting?.InterchangeType == "ExpresswayNode" || setting?.InterchangeType == "ExpresswayT";
        private bool IsExpresswayT => setting?.InterchangeType == "ExpresswayT";
        private bool IsSinglePoint => setting?.InterchangeType == "SinglePoint";
        private bool IsTrumpet => setting?.InterchangeType == "Trumpet";
        private bool IsCloverleaf => setting?.InterchangeType == "Cloverleaf";
        private bool IsParclo => setting?.InterchangeType == "PartialCloverleaf";
        private bool IsRoundabout => setting?.InterchangeType == "Roundabout";
        /// <summary>定向 T 型或半定向 T 型：支路与主线同层，左转匝道以跨线桥跨过主线。</summary>
        private bool IsDirectionalT => setting?.InterchangeType == "DirectionalT" || setting?.InterchangeType == "SemiDirectionalT";

        /// <summary>定向式和涡轮（同一个模板的两种造型：两条路都是双幅、匝道都在两条路之间升降）。</summary>
        private bool IsStack => setting?.InterchangeType == "Stack" || IsTurbine;

        private bool IsTurbine => setting?.InterchangeType == "Turbine";
        /// <summary>有环道的类型（喇叭形、苜蓿叶）：环道半径可自动；双向被交道路在立交内拆成双幅。</summary>
        private bool HasLoops => IsTrumpet || IsCloverleaf || IsParclo || IsRoundabout;
        private IInterchangeTemplate CurrentTemplate => IsTurbine ? m_Turbine : IsStack ? m_Stack : setting?.InterchangeType == "DirectionalT" ? m_DirectionalT
            : setting?.InterchangeType == "SemiDirectionalT" ? m_SemiDirectionalT : IsRoundabout ? m_Roundabout : IsParclo ? m_Parclo : IsCloverleaf ? m_Cloverleaf : IsTrumpet ? m_Trumpet : IsExpresswayT ? m_ExpresswayT : IsExpressway ? m_Expressway : IsSinglePoint ? m_SinglePoint : m_Diamond;

        /// <summary>主线是否在上层：中式快速路节点和单点立交的主线总是高架。</summary>
        private bool MainOnTop => IsCloverleaf || IsParclo || IsRoundabout ? !setting.CloverleafCrossOnTop : !IsTrumpet && !IsDirectionalT && !IsStack && (IsExpressway || IsSinglePoint || setting.MainOnTop);

        private RoadPrefab m_SelectedRoad;
        private ProxyAction m_RotateLeftAction;
        private ProxyAction m_RotateRightAction;
        /// <summary>
        /// 滚轮旋转时挡住镜头缩放（游戏的内置输入动作不能由模组直接启用，所以滚轮和 Shift 直接读设备，
        /// 再用输入屏障挡住镜头的缩放动作）。找不到缩放动作时为 null（滚轮照样旋转，镜头也会缩放）。
        /// </summary>
        private InputBarrier m_ZoomBarrier;
        private bool m_ZoomBarrierChecked;
        private float m_Yaw;
        private bool m_HasPreview;
        /// <summary>预览已固定（左键）：不再跟随鼠标，等面板确认放置。</summary>
        private bool m_Pinned;
        /// <summary>固定时对准了已有路口：朝向跟随匹配结果，旋转键不起作用。</summary>
        private bool m_PinnedOnMatch;
        /// <summary>面板点了“确认放置”，下一次更新时放置。</summary>
        private bool m_ConfirmRequested;
        private PreviewKey m_LastKey;
        private string m_LastReportedErrors;
        private string m_LastDebugConfig;
        private float m_LastFittedRampLength;
        private int m_SettingsVersion;
        private EntityQuery m_IconQuery;
        private float3 m_LastOrigin;
        private bool m_HasOrigin;
        private int m_ErrorRefreshCounter;
        private const int kErrorRefreshInterval = 15;
        private List<RoadInfo> m_RoadCatalog;
        private Dictionary<string, RoadInfo> m_RoadsByName;
        private RecentList m_RecentRoads;
        private int m_RandomSeed;
        private Random m_Random;
        private float m_LastYaw;
        /// <summary>当前预览的生成结果（局部坐标），调试时用来把错误位置对应到路段名。</summary>
        private IReadOnlyList<RoadSegmentSpec> m_LastSegments;
        /// <summary>当前路口匹配：鼠标指着已有道路时的路口及匹配结果。</summary>
        private Entity m_MatchSeed;
        private List<ExternalEdge> m_MatchEdges;
        private JunctionMatch m_Match;
        /// <summary>当前立体交叉匹配的那对路段（鼠标指着的、与之相交的）；不是立体交叉时为 default。</summary>
        private (Entity, Entity) m_CrossingKey;
        /// <summary>参与当前立体交叉的所有已有路段：改造时在立交范围内都要拆掉（不论高度）。</summary>
        private List<Entity> m_CrossingEdges = new List<Entity>();
        /// <summary>指着双幅道路的一幅时量出的实际分隔带；没有对向一幅时为 null。</summary>
        private float? m_PartnerMedian;
        /// <summary>最近一次鼠标位置（只对齐一条道路时，T 型支路放在鼠标所在的一侧）。</summary>
        private Vec3 m_Cursor;
        /// <summary>十字路口当 T 型用时支路在主线哪一侧（true = 右侧）；不是这种情况时为 null。</summary>
        private bool? m_TeeSide;
        /// <summary>原路口改造时当前预览的立交范围（局部坐标，相对 m_LastOrigin / m_LastYaw）；不拆除时为 null。</summary>
        private Footprint m_RemovalFootprint;
        /// <summary>立交范围比生成路段中心线的凸包向外多出的距离：原有道路在这之外的部分保留。</summary>
        private const float kRemovalMargin = 2f;

        public override string toolID => kToolID;

        public bool isActive => m_ToolSystem.activeTool == this;

        /// <summary>由 Mod 在加载时注入，用于读取参数和快捷键。</summary>
        public Setting setting { get; set; }

        /// <summary>最近一次预览实际用到的道路和结果；工具还没预览过时为 null。</summary>
        public PanelSnapshot snapshot { get; private set; }

        /// <summary>道路目录（工具第一次运行后才有），供道路选择表使用。</summary>
        public IReadOnlyList<RoadInfo> roadCatalog => m_RoadCatalog;

        /// <summary>道路菜单里最近选的道路，即主线的默认道路。</summary>
        public string selectedRoadName => m_SelectedRoad?.name ?? "";

        /// <summary>面板改了参数：下一帧重建预览。</summary>
        public void NotifySettingsChanged() => m_SettingsVersion++;

        /// <summary>预览已固定，等待确认放置。</summary>
        public bool pinned => m_Pinned;

        /// <summary>空地放置时的朝向（度，0–360）。</summary>
        public float headingDegrees => ((m_Yaw / Directions.DegToRad) % 360f + 360f) % 360f;

        /// <summary>朝向由旋转键决定（没有对准已有道路）：面板显示朝向。</summary>
        public bool headingFree => m_Pinned ? !m_PinnedOnMatch : m_Match == null || m_Match.Error != null;

        /// <summary>面板“确认放置”：下一次更新时放置固定的预览（游戏报错阻止放置时不放）。</summary>
        public void RequestConfirm() => m_ConfirmRequested = true;

        /// <summary>面板“取消固定”：预览重新跟随鼠标。</summary>
        public void Unpin()
        {
            m_Pinned = false;
            m_ConfirmRequested = false;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolOutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_NetToolSystem = World.GetOrCreateSystemManaged<NetToolSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_CityConfigurationSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_NetSearchSystem = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            m_DefinitionQuery = GetDefinitionQuery();
            m_IconQuery = GetEntityQuery(ComponentType.ReadOnly<Icon>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>());
            m_PrefabResolver = new PrefabResolver(m_PrefabSystem);
            m_Random = new Random(0x1A2B3C4Du);
            m_RandomSeed = m_Random.NextInt();
            m_ToolSystem.EventPrefabChanged += OnPrefabChanged;
        }

        protected override void OnDestroy()
        {
            m_ZoomBarrier?.Dispose();
            m_ToolSystem.EventPrefabChanged -= OnPrefabChanged;
            base.OnDestroy();
        }

        /// <summary>按名称排序的最近使用道路（最新在前），供道路选择表使用。</summary>
        public IReadOnlyList<string> recentRoads => GetRecentRoads().Items;

        private RecentList GetRecentRoads() =>
            m_RecentRoads ??= RecentList.Deserialize(setting?.RecentRoads);

        /// <summary>玩家在原版道路菜单里选了道路：记进最近使用，并保存到设置。</summary>
        private void OnPrefabChanged(PrefabBase prefab)
        {
            if (setting == null || !(prefab is RoadPrefab road) || !GetRecentRoads().Add(road.name))
                return;
            setting.RecentRoads = m_RecentRoads.Serialize();
            setting.ApplyAndSave();
        }

        /// <summary>
        /// 道路目录在工具第一次更新时建立（此时预制体都已加载）。
        /// 计算车道要用查找表，只能在系统自己的 OnUpdate 里创建，所以不能放在快捷键回调里。
        /// </summary>
        private void EnsureRoadCatalog()
        {
            if (m_RoadCatalog != null)
                return;
            CompleteDependency();
            var lookups = new RoadCatalogBuilder.Lookups
            {
                SubSections = GetBufferLookup<NetSubSection>(true),
                SectionPieces = GetBufferLookup<NetSectionPiece>(true),
                Pieces = GetComponentLookup<NetPieceData>(true),
                VertexMatches = GetComponentLookup<NetVertexMatchData>(true),
                Lanes = GetComponentLookup<NetLaneData>(true),
                PieceLanes = GetBufferLookup<NetPieceLane>(true),
            };
            m_RoadCatalog = RoadCatalogBuilder.Build(EntityManager, m_PrefabSystem, lookups);
            m_RoadsByName = new Dictionary<string, RoadInfo>();
            foreach (var road in m_RoadCatalog)
                m_RoadsByName[road.Name] = road;
            if (setting.DebugLogging)
            {
                RoadCatalogBuilder.Log(m_RoadCatalog);
                Mod.log.Info($"[debug] recent roads: {string.Join(", ", GetRecentRoads().Items)}");
            }
        }

        /// <summary>快捷键调用：未激活时激活工具，已激活时切回默认工具。</summary>
        public void Toggle()
        {
            if (isActive)
            {
                m_ToolSystem.activeTool = m_DefaultToolSystem;
                return;
            }

            if (!ResolveSelectedRoad())
                return;
            m_ToolSystem.activeTool = this;
            Mod.log.Info($"Interchange tool activated, main road '{m_SelectedRoad.name}'");
        }

        /// <summary>优先使用玩家最近在原版道路工具里选的道路，否则用默认道路。</summary>
        private bool ResolveSelectedRoad()
        {
            if (m_NetToolSystem.GetPrefab() is RoadPrefab selected)
            {
                m_SelectedRoad = selected;
                return true;
            }

            if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(RoadPrefab), kFallbackRoadPrefab), out PrefabBase fallback)
                && fallback is RoadPrefab road)
            {
                m_SelectedRoad = road;
                return true;
            }

            Mod.log.Error($"No road prefab available: select a road in the road menu first (fallback '{kFallbackRoadPrefab}' not found)");
            return false;
        }

        public override PrefabBase GetPrefab() => m_SelectedRoad;

        // 返回 false，避免玩家在原版菜单选道路时游戏把道路交给本工具而不是原版道路工具
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
            // 路口匹配：同时检测道路，鼠标指着已有道路时对准它所在的路口
            if (setting != null && setting.MatchJunctions)
            {
                m_ToolRaycastSystem.typeMask |= TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = Layer.Road;
            }
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_HasPreview = false;
            m_Pinned = false;
            m_ConfirmRequested = false;
            m_LastReportedErrors = null;
            m_RotateLeftAction = setting.GetAction(Mod.kRotateLeftActionName);
            m_RotateRightAction = setting.GetAction(Mod.kRotateRightActionName);
            // 旋转键只在工具激活时占用，避免和其他功能抢按键
            m_RotateLeftAction.shouldBeEnabled = true;
            m_RotateRightAction.shouldBeEnabled = true;
            CreateZoomBarrier();
            // 装了 Anarchy 时把本工具注册给它：开启 Anarchy 后它会移除本工具预览上的大部分错误
            if (!m_AnarchyRegistered)
                m_AnarchyRegistered = AnarchyIntegration.TryRegister(this);
        }

        private bool m_AnarchyRegistered;

        /// <summary>Anarchy 已安装并已注册本工具（面板据此显示无视碰撞开关）。</summary>
        public bool anarchyAvailable => m_AnarchyRegistered;

        protected override void OnStopRunning()
        {
            m_RotateLeftAction.shouldBeEnabled = false;
            m_RotateRightAction.shouldBeEnabled = false;
            if (m_ZoomBarrier != null)
                m_ZoomBarrier.blocked = false;
            base.OnStopRunning();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;

            if (m_FocusChanged || m_SelectedRoad == null)
                return inputDeps;
            EnsureRoadCatalog();

            // 右键：固定时取消固定（预览重新跟随鼠标），否则退出工具
            if (cancelAction.WasPressedThisFrame())
            {
                if (m_Pinned)
                    Unpin();
                else
                {
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                    m_HasPreview = false;
                    return Clear(inputDeps);
                }
            }

            float turn = 0f;
            if (m_RotateLeftAction.WasPressedThisFrame())
                turn -= kRotationStep;
            if (m_RotateRightAction.WasPressedThisFrame())
                turn += kRotationStep;
            // 滚轮：朝向由旋转决定（没对准已有道路）、鼠标不在面板上时旋转，并挡住镜头缩放；否则滚轮照常缩放镜头、滚动面板
            bool wheelRotates = headingFree && !InputManager.instance.mouseOverUI;
            if (m_ZoomBarrier != null)
                m_ZoomBarrier.blocked = wheelRotates;
            if (turn == 0f && wheelRotates)
                turn = WheelRotation();
            m_Yaw += turn;

            // 面板点了“确认放置”：放置固定的预览
            if (m_ConfirmRequested)
            {
                m_ConfirmRequested = false;
                if (m_Pinned && m_HasPreview && TryApply(inputDeps) is JobHandle applied)
                {
                    m_Pinned = false;
                    return applied;
                }
            }

            // 鼠标在面板等界面上、或预览已固定（左键点别处才换位置）：不跟随鼠标，预览留在原处；
            // 面板改了参数、旋转了要在原处重建预览
            float3 origin;
            float yaw;
            bool repin = m_Pinned && !InputManager.instance.mouseOverUI && applyAction.WasPressedThisFrame();
            if (InputManager.instance.mouseOverUI || (m_Pinned && !repin))
            {
                if (!m_HasOrigin)
                    return inputDeps;
                origin = m_LastOrigin;
                yaw = m_Pinned && !m_PinnedOnMatch ? m_Yaw : m_LastYaw;
            }
            else
            {
                if (!GetRaycastResult(out Entity hitEntity, out RaycastHit hit))
                {
                    m_HasPreview = false;
                    return Clear(inputDeps);
                }
                origin = hit.m_HitPosition;
                yaw = m_Yaw;

                // 指着已有道路：对准它所在路口的中心和主线方向
                UpdateJunctionMatch(hitEntity, hit.m_HitPosition);
                if (m_Match != null && m_Match.Error == null)
                {
                    origin = new float3(m_Match.Center.X, hit.m_HitPosition.y, m_Match.Center.Z);
                    yaw = m_Match.MainAngle * Directions.DegToRad;
                }

                // 左键：把预览固定在这里（已固定时换到这里）
                if (applyAction.WasPressedThisFrame())
                {
                    m_Pinned = true;
                    m_PinnedOnMatch = m_Match != null && m_Match.Error == null;
                }
            }

            var key = new PreviewKey(origin.xz, yaw, m_SettingsVersion);
            if (m_HasPreview && !m_ForceUpdate && key.Equals(m_LastKey))
            {
                applyMode = ApplyMode.None;
                // 游戏对预览的检查结果要过几帧才出来，定期读一次给面板显示
                if (++m_ErrorRefreshCounter % kErrorRefreshInterval == 0)
                    RefreshGameErrors();
                return inputDeps;
            }

            m_LastKey = key;
            m_LastOrigin = origin;
            m_LastYaw = yaw;
            m_HasOrigin = true;
            return UpdatePreview(inputDeps, origin, yaw);
        }

        /// <summary>
        /// 滚轮这一帧转了多少（直接读鼠标、键盘）：每格 15°，按住 Shift 每格 1°；向上滚逆时针。
        /// Windows 上一格滚动量是 120，按格数算，快速滚动时一帧可能有好几格。
        /// </summary>
        private static float WheelRotation()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
                return 0f;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll == 0f)
                return 0f;
            float notches = System.Math.Abs(scroll) >= 60f ? (float)System.Math.Round(scroll / 120f) : System.Math.Sign(scroll);
            if (notches == 0f)
                notches = System.Math.Sign(scroll);
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool fine = keyboard != null && keyboard.shiftKey.isPressed;
            return -notches * (fine ? kFineRotationStep : kRotationStep);
        }

        /// <summary>
        /// 找镜头的缩放动作（镜头动作表里名字含 Zoom 的）建一个输入屏障，工具第一次激活时建一次。
        /// 只挡这一个动作，不碰游戏的内置动作的启用状态。找不到或出错时不挡（调试日志记下镜头动作表）。
        /// </summary>
        private void CreateZoomBarrier()
        {
            if (m_ZoomBarrierChecked)
                return;
            m_ZoomBarrierChecked = true;
            try
            {
                if (!InputManager.instance.TryFindActionMap(InputManager.kCameraMap, out ProxyActionMap camera))
                    return;
                var names = camera.actions.Keys.ToList();
                string zoom = names.FirstOrDefault(n => n.IndexOf("Zoom", System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (zoom != null)
                    m_ZoomBarrier = InputManager.instance.CreateActionBarrier(InputManager.kCameraMap, zoom, nameof(InterchangeToolSystem));
                if (setting.DebugLogging)
                    Mod.log.Info($"[debug] camera actions: {string.Join(", ", names)}; zoom barrier on '{zoom ?? "none"}'");
            }
            catch (System.Exception e)
            {
                Mod.log.Warn($"Could not block camera zoom for wheel rotation: {e.Message}");
                m_ZoomBarrier = null;
            }
        }

        /// <summary>放置当前预览；游戏报错阻止放置时记下原因并返回 null。</summary>
        private JobHandle? TryApply(JobHandle inputDeps)
        {
            if (GetAllowApply())
            {
                applyMode = ApplyMode.Apply;
                m_HasPreview = false;
                m_RandomSeed = m_Random.NextInt();
                return DestroyDefinitions(m_DefinitionQuery, m_ToolOutputBarrier, inputDeps);
            }
            RefreshGameErrors();
            string details = snapshot == null ? "" : string.Join("; ", snapshot.GameErrors);
            Mod.log.Info($"Placement blocked by the game: {details}");
            if (setting.DebugLogging && snapshot != null)
                LogErrorLocations(snapshot.GameErrors);
            return null;
        }

        /// <summary>
        /// 鼠标指着的实体换了路口时重新读路口、重新匹配。不是道路（或关了匹配）时清除匹配。
        /// </summary>
        private void UpdateJunctionMatch(Entity hitEntity, float3 hitPosition)
        {
            m_Cursor = new Vec3(hitPosition.x, 0f, hitPosition.z);
            UpdateJunctionMatchCore(hitEntity, hitPosition);
            OrientCrossing();
            // 十字路口当 T 型用时，支路取鼠标所在一侧：鼠标移到主线另一侧就重建预览
            bool? side = TeeFromCross ? CursorRightOfMain() : (bool?)null;
            if (side != m_TeeSide)
            {
                m_TeeSide = side;
                m_SettingsVersion++;
            }
        }

        /// <summary>
        /// 四向立体交叉（两条路没有共用节点、一上一下）：哪条已有道路当主线由“上层道路”决定——主线在上层时取上面那条，
        /// 被交道路在上层时取下面那条（中式快速路、单点立交的主线固定在上层）。匹配本身按车道数和朝向选主线，
        /// 可能选到下面那条，这里按需要对调主线和被交道路；改了“上层道路”也会重新对调。
        /// </summary>
        private void OrientCrossing()
        {
            var match = m_Match;
            if (match == null || match.Error != null || match.RoadOnly || match.LegCount != 4
                || match.Main == null || match.Cross == null)
                return;
            bool mainUpper = match.Main.Elevation > match.Cross.Elevation;
            bool stacked = System.Math.Abs(match.Main.Elevation - match.Cross.Elevation) >= 0.5f;
            bool swap;
            if (!match.Crossing)
            {
                // 同层的十字路口当 T 型用：没有上下层可分，面板“换另一条”时主线换成匹配结果的另一条路（记在匹配结果上，每帧不来回换）
                if (CurrentTemplate.LegCount != 3)
                    return;
                bool wantSwapped = setting.TeeMainRoad == "swap";
                if (wantSwapped == match.TeeSwapped)
                    return;
                match.TeeSwapped = wantSwapped;
                swap = true;
            }
            else if (CurrentTemplate.LegCount == 3)
            {
                // 三向立交（十字当 T 型用）：主线按面板选——自动时一条是公路、另一条不是就取公路，否则取上层
                string choice = setting.TeeMainRoad ?? "auto";
                if (choice == "auto" && match.Main.IsHighway != match.Cross.IsHighway)
                    swap = !match.Main.IsHighway;
                else if (!stacked)
                    swap = false;
                else
                    swap = choice == "lower" ? mainUpper : !mainUpper;
            }
            else
                swap = stacked && mainUpper != MainOnTop;
            if (!swap)
                return;
            // 新主线 = 原被交道路（在原主线顺时针 CrossingAngle 处）；原主线的另一条臂在新主线顺时针 180° − CrossingAngle 处
            float crossing = match.CrossingAngle;
            (match.Main, match.Cross) = (match.Cross, match.Main);
            match.MainAngle = (match.MainAngle + crossing) % 360f;
            match.CrossingAngle = 180f - crossing;
            m_SettingsVersion++;
        }

        /// <summary>
        /// 三向立交对准十字路口：当作 T 型，支路取鼠标所在一侧的那条臂，另一侧那条臂在立交范围内拆掉
        /// （拆除照常按立交范围进行；范围外保留的部分没有臂端可接，自由结束）。
        /// </summary>
        private bool TeeFromCross => m_Match != null && m_Match.Error == null && !m_Match.RoadOnly
            && m_Match.LegCount == 4 && CurrentTemplate.LegCount == 3;

        private bool CursorRightOfMain() =>
            Directions.Dot(m_Cursor - m_Match.Center, Directions.RightOf(Directions.FromAngle(m_Match.MainAngle))) > 0f;

        private void UpdateJunctionMatchCore(Entity hitEntity, float3 hitPosition)
        {
            // 指着的是本工具自己的预览（新建的临时道路，没有对应的原有道路）：保持当前匹配，否则预览会来回跳
            if (hitEntity != Entity.Null && EntityManager.Exists(hitEntity) && EntityManager.HasComponent<Temp>(hitEntity)
                && EntityManager.GetComponentData<Temp>(hitEntity).m_Original == Entity.Null)
                return;

            // 立体交叉（如高架跨过街道，没有共用节点）：以两条道路在俯视图上的交点作为虚拟路口来匹配
            if (setting.MatchJunctions && TryMatchCrossing(hitEntity, hitPosition))
                return;
            if (setting.DebugLogging && setting.MatchJunctions && JunctionReader.LastCrossingReason != m_LastCrossingReason)
            {
                m_LastCrossingReason = JunctionReader.LastCrossingReason;
                Mod.log.Info($"[debug] crossing check: {m_LastCrossingReason}");
            }

            Entity seed = setting.MatchJunctions ? JunctionReader.FindSeedNode(EntityManager, hitEntity, hitPosition) : Entity.Null;
            var cursor = new Vec3(hitPosition.x, 0f, hitPosition.z);
            m_Cursor = cursor;
            // 预览里原有道路已显示为拆除，鼠标可能指不到它们：仍在立交范围内就保持当前匹配，否则预览会来回跳
            if (seed == Entity.Null && m_RemovalFootprint != null && m_Match != null && m_Match.Error == null
                && m_RemovalFootprint.Contains(new Vec3(hitPosition.x - m_LastOrigin.x, 0f, hitPosition.z - m_LastOrigin.z).RotateY(-m_LastYaw)))
            {
                if (m_Match.RoadOnly)
                    m_Match = MatchEdges(cursor);
                return;
            }
            if (m_CrossingKey != default)
            {
                // 离开立体交叉：清掉交叉匹配，下面按普通路口/道路重新匹配
                m_CrossingKey = default;
                m_CrossingEdges.Clear();
                m_Match = null;
                m_MatchEdges = null;
                m_MatchSeed = Entity.Null;
                m_SettingsVersion++;
            }
            if (seed == m_MatchSeed)
            {
                // 普通道路：中心跟着鼠标沿道路移动，每帧重新投影（路段已缓存，开销很小）
                if (m_Match != null && m_Match.RoadOnly)
                    m_Match = MatchEdges(cursor);
                return;
            }
            m_MatchSeed = seed;
            m_Match = null;
            m_MatchEdges = null;
            if (seed != Entity.Null)
            {
                TerrainHeightData terrain = m_TerrainSystem.GetHeightData();
                m_MatchEdges = JunctionReader.Read(EntityManager, m_PrefabSystem, seed, m_RoadsByName, ref terrain);
                // 指着双幅的一幅时，量出对向那一幅的实际分隔带（没有时用面板的分隔带估算）
                var netTree = m_NetSearchSystem.GetNetSearchTree(readOnly: true, out JobHandle treeDeps);
                treeDeps.Complete();
                m_PartnerMedian = JunctionReader.MeasureDualMedian(EntityManager, m_PrefabSystem, netTree, hitEntity, m_RoadsByName,
                    !m_CityConfigurationSystem.leftHandTraffic);
                m_Match = MatchEdges(cursor);
                if (setting.DebugLogging)
                    Mod.log.Info($"[debug] junction: {m_MatchEdges.Count} edges, {m_Match.LegCount} legs, " +
                        (m_Match.Error ?? (m_Match.RoadOnly
                            ? $"plain road '{m_Match.Main.Prefab}' {m_Match.Main.Form} at {m_Match.MainAngle:F0}°"
                            : $"main '{m_Match.Main.Prefab}' {m_Match.Main.Form} at {m_Match.MainAngle:F0}°, cross '{m_Match.Cross.Prefab}' {m_Match.Cross.Form}, angle {m_Match.CrossingAngle:F0}°")));
            }
            m_SettingsVersion++;
        }

        /// <summary>
        /// 鼠标指着的路段附近有立体交叉时按交叉点匹配（见 JunctionReader.ReadCrossing），返回 true。
        /// 同一处交叉（同一对路段）不重复匹配。
        /// </summary>
        private string m_LastCrossingReason = "";

        private bool TryMatchCrossing(Entity hitEntity, float3 hitPosition)
        {
            var matched = new List<Entity>();
            TerrainHeightData terrain = m_TerrainSystem.GetHeightData();
            var netTree = m_NetSearchSystem.GetNetSearchTree(readOnly: true, out JobHandle treeDeps);
            treeDeps.Complete();
            var edges = JunctionReader.ReadCrossing(EntityManager, m_PrefabSystem, netTree, hitEntity, hitPosition,
                m_RoadsByName, ref terrain, matched, out var key);
            if (edges == null)
                return false;
            if (key.Equals(m_CrossingKey) && m_Match != null)
                return true;

            m_CrossingKey = key;
            m_CrossingEdges = matched;
            m_MatchSeed = Entity.Null;
            m_MatchEdges = edges;
            m_Match = MatchEdges(new Vec3(hitPosition.x, 0f, hitPosition.z));
            m_Match.Crossing = true;
            if (setting.DebugLogging)
                Mod.log.Info($"[debug] crossing: {matched.Count} edges, {m_Match.LegCount} legs, " +
                    (m_Match.Error ?? $"main '{m_Match.Main.Prefab}' {m_Match.Main.Form} at {m_Match.Main.Elevation:F1} m, " +
                        $"cross '{m_Match.Cross.Prefab}' {m_Match.Cross.Form} at {m_Match.Cross.Elevation:F1} m, angle {m_Match.CrossingAngle:F0}°"));
            m_SettingsVersion++;
            return true;
        }

        /// <summary>
        /// 基层跟随已有道路：对准已有路口/道路时（面板默认“跟随”），基层取已有道路的实际离地高度
        /// （路堤、下沉、高架都可能，不一定是地面）；立体交叉时总是跟随。空地放置或面板选“手动”时用面板的基层。
        /// </summary>
        private bool BaseFollowsMatch => m_Match != null && m_Match.Error == null && (m_Match.Crossing || setting.BaseFollowsMatch);

        /// <summary>基层高度：跟随已有道路时取较低那条已有道路的高度，否则是面板设定。</summary>
        private float BaseElevation => BaseFollowsMatch ? m_Match.LowestElevation : setting.BaseElevation;

        /// <summary>
        /// 叠建：手动把基层放在高架或隧道那一段时，在原有道路上方/下方另建一座，不拆原有道路。
        /// 跟随已有道路时总是原路口改造（新立交替换原有道路）。
        /// </summary>
        private bool StackedOverMatch => !BaseFollowsMatch && LevelPlanner.BandOf(setting.BaseElevation) is ElevationBand.Elevated or ElevationBand.Tunnel;

        private JunctionMatch MatchEdges(Vec3 cursor) =>
            JunctionMatcher.Match(m_MatchEdges, m_Yaw / Directions.DegToRad, cursor, m_PartnerMedian ?? setting.MedianWidth, !m_CityConfigurationSystem.leftHandTraffic);

        /// <summary>把当前路口匹配结果写进面板快照。</summary>
        private void FillMatchState(PanelSnapshot snapshot)
        {
            if (m_Match == null)
            {
                snapshot.MatchState = "none";
                return;
            }
            snapshot.MatchLegs = m_Match.LegCount;
            if (m_Match.Error != null)
            {
                snapshot.MatchState = "error";
                snapshot.MatchError = m_Match.Error;
                return;
            }
            snapshot.MatchState = m_Match.RoadOnly ? "road" : "matched";
            snapshot.MatchAngle = TeeFromCross ? BranchAngle(true) : m_Match.CrossingAngle;
            snapshot.MatchDropsLeg = TeeFromCross;
            snapshot.MatchCrossing = m_Match.Crossing;
            snapshot.MatchMain = Describe(m_Match.Main);
            snapshot.MatchCross = m_Match.RoadOnly ? "" : Describe(m_Match.Cross);
        }

        private static string Describe(MatchedRoad road) =>
            road.Form == RoadForm.Dual ? $"{road.Prefab}|Dual|{road.MedianWidth:F0}" : $"{road.Prefab}|TwoWay|0";

        /// <summary>
        /// 调试：每个错误图标的位置（立交局部坐标）和离它最近的生成路段名，方便对照模板查是哪段路出的问题。
        /// </summary>
        private void LogErrorLocations(List<GameError> errors)
        {
            if (m_LastSegments == null)
                return;
            foreach (var error in errors)
            {
                foreach (float3 world in error.Positions)
                {
                    var local = new Vec3(world.x - m_LastOrigin.x, world.y, world.z - m_LastOrigin.z).RotateY(-m_LastYaw);
                    string nearest = "?";
                    float best = float.MaxValue;
                    foreach (var segment in m_LastSegments)
                    {
                        for (int i = 0; i <= 16; i++)
                        {
                            Vec3 q = segment.Curve.Evaluate(i / 16f);
                            float d = (q.X - local.X) * (q.X - local.X) + (q.Z - local.Z) * (q.Z - local.Z);
                            if (d < best)
                            {
                                best = d;
                                nearest = segment.Name;
                            }
                        }
                    }
                    Mod.log.Info($"[debug]   {error.Type} {error.Road} at local ({local.X:F1}, {local.Z:F1}), height {world.y:F1}: nearest segment {nearest} ({System.Math.Sqrt(best):F1} m)");
                }
            }
        }

        /// <summary>读出游戏对当前预览报的错误（重叠、高度不足、净空不足等），存进面板快照。</summary>
        private void RefreshGameErrors()
        {
            if (snapshot == null)
                return;
            if (m_ErrorQuery.IsEmptyIgnoreFilter)
            {
                if (snapshot.GameErrors.Count > 0)
                    snapshot.GameErrors = new List<GameError>();
                return;
            }
            snapshot.GameErrors = PlacementErrorReader.Read(EntityManager, m_PrefabSystem, m_IconQuery);
            // 调试：错误变化时记下每个错误的位置和最近的路段，不用点击放置也能查
            string summary = string.Join("; ", snapshot.GameErrors);
            if (setting.DebugLogging && summary != m_LastLoggedGameErrors)
            {
                m_LastLoggedGameErrors = summary;
                if (snapshot.GameErrors.Count > 0)
                {
                    Mod.log.Info($"[debug] game errors: {summary}");
                    LogErrorLocations(snapshot.GameErrors);
                }
            }
        }

        private string m_LastLoggedGameErrors;

        private JobHandle UpdatePreview(JobHandle inputDeps, float3 origin, float yaw)
        {
            inputDeps = Clear(inputDeps);
            m_HasPreview = false;

            var errors = new List<string>();
            var snapshot = new PanelSnapshot();
            FillMatchState(snapshot);
            if (m_Match != null && m_Match.Error != null)
                errors.Add($"无法匹配路口：{m_Match.Error}（把鼠标移到空地上可自由放置）");
            else if (m_Match != null && !m_Match.RoadOnly && m_Match.LegCount != CurrentTemplate.LegCount && !TeeFromCross)
                errors.Add(m_Match.LegCount == 3
                    ? "这是三向（T 型）路口：当前立交类型需要四向路口，请选择快速路 T 型、喇叭形、定向 T 型或半定向 T 型"
                    : $"这是 {m_Match.LegCount} 向路口，当前立交类型需要 {CurrentTemplate.LegCount} 向");
            InterchangeParams p = BuildParams(errors, snapshot);
            // 调试日志：定向式每次求解（参数变化时）的耗时，用来查卡顿
            StackTemplate.Trace = IsStack && setting.DebugLogging ? (System.Action<string>)(m => Mod.log.Info($"[debug] stack: {m}")) : null;
            if (errors.Count == 0)
            {
                // 匝道长度设置太短时自动加长到满足坡度，而不是让预览直接失败
                p = InterchangeLayout.FitRampLength(CurrentTemplate, p);
                if (p.RampLength != m_LastFittedRampLength && p.RampLength > setting.RampLength)
                    Mod.log.Info($"Ramp length extended from {setting.RampLength:F0} m to {p.RampLength:F0} m to meet the grade limit");
                m_LastFittedRampLength = p.RampLength;
                errors.AddRange(CurrentTemplate.ValidateParams(p));
                // 面板显示实际用的环道半径（自动时是求出来的最小半径）
                if (IsTrumpet && errors.Count == 0)
                    snapshot.LoopRadius = TrumpetTemplate.ResolveLoopRadius(p);
                else if (IsCloverleaf && errors.Count == 0)
                    snapshot.LoopRadius = CloverleafTemplate.ResolveLoopRadius(p);
                else if (IsParclo && errors.Count == 0)
                    snapshot.LoopRadius = PartialCloverleafTemplate.ResolveLoopRadius(p);
                else if (IsRoundabout && errors.Count == 0)
                    snapshot.LoopRadius = RoundaboutTemplate.ResolveRadius(p);
            }
            snapshot.RampLength = p.RampLength;

            IReadOnlyList<RoadSegmentSpec> segments = null;
            m_LastSegments = null;
            if (errors.Count == 0)
            {
                segments = CurrentTemplate.Generate(p);
                m_LastSegments = segments;
                errors.AddRange(InterchangeLayout.ValidateGeometry(segments, p));
            }

            // 匹配到路口时：基层是高架桥或隧道 → 在原有道路上方/下方叠建；否则是原路口改造，拆掉范围内的原有道路
            bool retrofit = false;
            if (m_Match != null && m_Match.Error == null)
            {
                snapshot.MatchStacked = StackedOverMatch;
                retrofit = !snapshot.MatchStacked;
            }

            m_RemovalFootprint = null;
            if (errors.Count == 0)
            {
                TerrainHeightData terrain = m_TerrainSystem.GetHeightData();
                EntityCommandBuffer commandBuffer = m_ToolOutputBarrier.CreateCommandBuffer();
                errors = InterchangePlacer.CreateDefinitions(commandBuffer, segments,
                    origin, yaw, ref terrain, m_PrefabResolver, m_SelectedRoad, m_RandomSeed);
                if (errors.Count == 0 && retrofit)
                {
                    m_RemovalFootprint = new Footprint(segments.Select(s => s.Curve), kRemovalMargin);
                    var netTree = m_NetSearchSystem.GetNetSearchTree(readOnly: true, out JobHandle treeDeps);
                    treeDeps.Complete();
                    // 新立交两层所在的高度都清理：被替换的高架（或下穿道路）和地面道路。
                    // 立体交叉时两条原有道路原来的高度也清理：新立交的层不一定和它们同高（如定向 T 型的支路改到主线那一层），
                    // 否则范围内离交叉点稍远的原有路段会留在原地、和新立交叠在一起
                    var levels = new List<float> { p.Levels[0].Elevation, p.Levels[1].Elevation };
                    if (m_Match.Crossing)
                        levels.AddRange(new[] { m_Match.Main.Elevation, m_Match.Cross.Elevation });
                    List<Entity> removed = RoadRemover.FindEdges(EntityManager, netTree, m_RemovalFootprint, origin, yaw,
                        levels, ref terrain, m_Match.Crossing ? m_CrossingEdges : null);
                    RoadRemover.Debug = setting.DebugLogging;
                    snapshot.ConnectedEnds = RoadRemover.CreateDefinitions(commandBuffer, EntityManager, removed, m_RemovalFootprint,
                        InterchangeLayout.LegEnds(segments), origin, yaw, ref terrain, m_RandomSeed + segments.Count);
                    snapshot.RemovedRoads = removed.Count;
                }
            }

            m_HasPreview = errors.Count == 0;
            snapshot.Errors.AddRange(errors);
            this.snapshot = snapshot;
            ReportErrors(errors);
            return inputDeps;
        }

        /// <summary>
        /// 分层：基层（下层）高度由面板设定，上层 = 基层 + 层间高差（自动时按 LevelPlanner 规则）。
        /// 要在道路设好之后算，因为最小净空取决于两种道路的竖向范围。
        /// </summary>
        private void PlanLevels(InterchangeParams p, PanelSnapshot snapshot)
        {
            if (IsDirectionalT)
            {
                // 定向 / 半定向 T 型：主线、支路在基层（立体交叉时取主线原来的高度），层 1 是第一层跨线桥 = 基层 + 层间高差；
                // 支路原来的高度只用于臂端坡道接回
                float baseHeight = m_Match != null && m_Match.Error == null && m_Match.Crossing ? m_Match.Main.Elevation : BaseElevation;
                float flyGap = LevelPlanner.ResolveGap(p.Legs[0], p.Legs[0], baseHeight, setting.LevelGap);
                p.Levels[0].Elevation = baseHeight;
                p.Levels[1].Elevation = baseHeight + flyGap;
                snapshot.BaseFollows = BaseFollowsMatch;
                snapshot.BaseElevation = BaseElevation;
                snapshot.MainElevation = baseHeight;
                snapshot.CrossElevation = baseHeight;
                snapshot.LevelGap = flyGap;
                snapshot.MinimumGap = LevelPlanner.RequiredGap(p.Legs[0], p.Legs[0], baseHeight);
                return;
            }
            LegSpec lower = MainOnTop ? p.Legs[1] : p.Legs[0];
            LegSpec upper = MainOnTop ? p.Legs[0] : p.Legs[1];
            float baseElevation = BaseElevation;
            float gap = LevelPlanner.ResolveGap(lower, upper, baseElevation, setting.LevelGap);
            float mainElevation = MainOnTop ? baseElevation + gap : baseElevation;
            float crossElevation = MainOnTop ? baseElevation : baseElevation + gap;
            snapshot.BaseFollows = BaseFollowsMatch;
            snapshot.BaseElevation = baseElevation;
            if (m_Match != null && m_Match.Error == null && m_Match.Crossing)
            {
                // 立体交叉：两条道路保持原来的高度，好在两端接回原有道路
                mainElevation = m_Match.Main.Elevation;
                crossElevation = m_Match.Cross.Elevation;
                gap = System.Math.Abs(mainElevation - crossElevation);
            }
            else if (BaseFollowsMatch && m_Match.RoadOnly && MainOnTop
                && LevelPlanner.BandOf(m_Match.LowestElevation) == ElevationBand.Elevated)
            {
                // 对齐的是一条已有高架（主线在上层的立交）：主线保持这条高架的高度，下层（被交道路、辅路）在地面
                // （游戏内实测：把高架高度当成基层时主线又抬高一层，原高架留在下面，重叠且接不回）
                mainElevation = m_Match.LowestElevation;
                crossElevation = 0f;
                gap = mainElevation;
                baseElevation = 0f;
                snapshot.BaseElevation = baseElevation;
            }
            p.Levels[0].Elevation = mainElevation;
            p.Levels[1].Elevation = crossElevation;
            snapshot.MainElevation = mainElevation;
            snapshot.CrossElevation = crossElevation;
            snapshot.LevelGap = gap;
            snapshot.MinimumGap = LevelPlanner.RequiredGap(lower, upper, baseElevation);
        }

        /// <summary>
        /// T 型的支路角度（相对主线臂 0 顺时针，0~360°）：对准已有 T 型路口时取路口的；
        /// 否则按面板的相交角度，支路在右侧时就是它，在左侧时取 360° − 它。
        /// </summary>
        private float BranchAngle(bool matched)
        {
            if (matched && !m_Match.RoadOnly && m_Match.LegCount == 3)
                return m_Match.CrossingAngle;
            // 十字路口当 T 型：被交道路右侧那条臂在 CrossingAngle（0~180°），左侧那条在对面
            if (matched && TeeFromCross)
                return CursorRightOfMain() ? m_Match.CrossingAngle : m_Match.CrossingAngle + 180f;
            bool left = setting.BranchOnLeft;
            if (matched && m_Match.RoadOnly)
            {
                // 只对齐了一条道路：支路放在鼠标所在的一侧（相对对齐后的主线方向），不用面板的左右
                Vec3 mainDirection = Directions.FromAngle(m_Match.MainAngle);
                left = Directions.Dot(m_Cursor - m_Match.Center, Directions.RightOf(mainDirection)) < 0f;
            }
            return left ? 360f - setting.CrossingAngle : setting.CrossingAngle;
        }

        private InterchangeParams BuildParams(List<string> errors, PanelSnapshot snapshot)
        {
            bool matched = m_Match != null && m_Match.Error == null;
            var p = new InterchangeParams
            {
                RampLength = setting.RampLength,
                RampDivergeAngle = setting.RampDivergeAngle,
                RightHandTraffic = !m_CityConfigurationSystem.leftHandTraffic,
                Legs = CurrentTemplate.LegCount == 3
                    // 定向 / 半定向 T 型的支路与主线同层（基层），层 1 是第一层跨线桥
                    ? InterchangeParams.TLegs(BranchAngle(matched), mainLevel: 0, branchLevel: IsDirectionalT ? 0 : 1)
                    : InterchangeParams.CrossLegs(matched && !m_Match.RoadOnly ? m_Match.CrossingAngle : setting.CrossingAngle, mainLevel: 0, crossLevel: 1),
            };
            // 被交道路（支路）的另一端：十字型是臂 3，T 型只有臂 1
            int crossOther = p.Legs.Count == 4 ? 3 : 1;

            string mainName, crossName;
            RoadForm mainForm, crossForm;
            float mainMedian = setting.MedianWidth, crossMedian = setting.MedianWidth;
            if (matched)
            {
                // 对准已有路口：道路、形式、分隔带都跟随路口外部的道路；
                // 只指着一条普通道路时，只有主线跟随它，被交道路仍按面板
                mainName = m_Match.Main.Prefab;
                mainForm = m_Match.Main.Form;
                if (mainForm == RoadForm.Dual)
                    mainMedian = m_Match.Main.MedianWidth;
                if (m_Match.RoadOnly)
                {
                    (crossName, crossForm) = ResolveRoad(setting.CrossRoadPrefab, kDefaultCrossRoad, setting.CrossRoadForm);
                }
                else
                {
                    crossName = m_Match.Cross.Prefab;
                    crossForm = m_Match.Cross.Form;
                    if (crossForm == RoadForm.Dual)
                        crossMedian = m_Match.Cross.MedianWidth;
                }
            }
            else
            {
                // 主线默认用道路菜单里选的道路，被交道路默认 Medium Road；面板里选了就用面板的
                (mainName, mainForm) = ResolveRoad(setting.MainRoadPrefab, m_SelectedRoad.name, setting.MainRoadForm);
                (crossName, crossForm) = ResolveRoad(setting.CrossRoadPrefab, kDefaultCrossRoad, setting.CrossRoadForm);
            }
            SetRoad(p, 0, 2, mainForm, mainName, mainMedian, errors);
            SetRoad(p, 1, crossOther, crossForm, crossName, crossMedian, errors);
            // 原有道路的类型（公路 / 街道）：后面双向道路可能被拆成别的单行道路，苜蓿叶集散车道的“自动”按原来的类型判断
            string mainOriginal = mainName, crossOriginal = crossName;
            PlanLevels(p, snapshot);

            // 外部主线是双向道路、但立交主线不在地面（或是苜蓿叶，匝道都接在两幅上）：匝道无法从双向道路中心分出，
            // 立交内部改用双幅（每方向车道相同的单行道路），臂端过渡回外部的双向道路；苜蓿叶时两幅外缘对齐双向道路的边缘
            if (mainForm == RoadForm.TwoWay && (IsCloverleaf || IsParclo || IsRoundabout || IsDirectionalT || IsStack || (matched && LevelPlanner.BandOf(p.Levels[0].Elevation) != ElevationBand.Ground))
                && m_RoadsByName.TryGetValue(mainName, out RoadInfo external))
            {
                RoadInfo oneWay = (IsCloverleaf || IsDirectionalT || IsStack ? RampMatcher.PickSplitOneWay(m_RoadCatalog, external) : null)
                    ?? RampMatcher.PickClosest(m_RoadCatalog, external.IsHighway, external.LanesPerDirection, oneWay: true);
                if (oneWay != null)
                {
                    float median = setting.MedianWidth, externalWidth = 0f;
                    if ((IsCloverleaf || IsDirectionalT || IsStack) && m_PrefabResolver.TryResolve(mainName, m_SelectedRoad, out NetPrefab mainPrefab)
                        && m_PrefabResolver.TryResolve(oneWay.Name, m_SelectedRoad, out NetPrefab mainOneWayPrefab))
                    {
                        externalWidth = m_PrefabResolver.GetWidth(mainPrefab);
                        float aligned = externalWidth - 2f * m_PrefabResolver.GetWidth(mainOneWayPrefab);
                        if (aligned >= 0f)
                            median = System.Math.Max(aligned, TrumpetTemplate.kMinSplitMedian);
                    }
                    SetRoad(p, 0, 2, RoadForm.Dual, oneWay.Name, median, errors);
                    foreach (int index in new[] { 0, 2 })
                    {
                        p.Legs[index].ExternalForm = RoadForm.TwoWay;
                        p.Legs[index].ExternalPrefab = mainName;
                        p.Legs[index].ExternalWidth = externalWidth;
                    }
                    snapshot.MainTransition = mainName;
                    mainName = oneWay.Name;
                    mainForm = RoadForm.Dual;
                    PlanLevels(p, snapshot);
                }
            }
            if (IsExpressway)
                SetFrontage(p, errors, snapshot);

            if (HasLoops)
            {
                p.LoopRadius = System.Math.Max(0f, setting.LoopRadius);   // 0 = 自动，模板取能放下的最小半径
                p.TrumpetLoopForExit = setting.TrumpetLoopForExit;
                p.ParcloLoopsForExit = setting.ParcloLoopsForExit;
                p.DirectRampRadius = setting.TrumpetNearRampRadius;
            }
            {
                // 喇叭形、定向 / 半定向 T 型的支路、苜蓿叶和定向式的被交道路两幅分开接匝道，是双向道路时立交内部改用双幅，外端过渡回双向
                // 部分苜蓿叶的被交道路保持双向（匝道在它上面接平交路口）
                // 环岛式的被交道路也保持双向（直接接环岛）
                if ((HasLoops || IsDirectionalT || IsStack) && !IsParclo && !IsRoundabout && crossForm == RoadForm.TwoWay && m_RoadsByName.TryGetValue(crossName, out RoadInfo branch))
                {
                    // 优先选两幅并排正好放得进双向道路宽度的单行道路（街道单行道带人行道较宽，放不下时用同车道数的公路单行道）
                    RoadInfo oneWay = RampMatcher.PickSplitOneWay(m_RoadCatalog, branch)
                        ?? RampMatcher.PickClosest(m_RoadCatalog, branch.IsHighway, branch.LanesPerDirection, oneWay: true);
                    if (oneWay != null && m_PrefabResolver.TryResolve(crossName, m_SelectedRoad, out NetPrefab twoWayPrefab)
                        && m_PrefabResolver.TryResolve(oneWay.Name, m_SelectedRoad, out NetPrefab oneWayPrefab))
                    {
                        // 两幅外缘对齐双向道路的两侧边缘（分隔带 = 双向宽 − 2 × 单幅宽），外端两幅平行接入同一个节点；
                        // 单行道路比双向道路的一半还宽时放不下，改用斜向汇合的过渡段
                        float twoWayWidth = m_PrefabResolver.GetWidth(twoWayPrefab);
                        float aligned = twoWayWidth - 2f * m_PrefabResolver.GetWidth(oneWayPrefab);
                        SetRoad(p, 1, crossOther, RoadForm.Dual, oneWay.Name, aligned >= 0f ? System.Math.Max(aligned, TrumpetTemplate.kMinSplitMedian) : setting.MedianWidth, errors);
                        foreach (int index in new[] { 1, crossOther })
                        {
                            p.Legs[index].ExternalForm = RoadForm.TwoWay;
                            p.Legs[index].ExternalPrefab = crossName;
                            p.Legs[index].ExternalWidth = twoWayWidth;
                        }
                        crossName = oneWay.Name;
                        crossForm = RoadForm.Dual;
                        PlanLevels(p, snapshot);
                    }
                }

                // 单点立交、部分苜蓿叶的被交道路要求双向：是双幅时立交内部改用同车道数的双向道路，两端分回双幅
                if ((IsSinglePoint || IsParclo) && crossForm == RoadForm.Dual && m_RoadsByName.TryGetValue(crossName, out RoadInfo dualCross))
                {
                    RoadInfo twoWay = RampMatcher.PickClosest(m_RoadCatalog, dualCross.IsHighway, dualCross.LanesPerDirection, oneWay: false);
                    if (twoWay != null && m_PrefabResolver.TryResolve(crossName, m_SelectedRoad, out NetPrefab crossOneWayPrefab))
                    {
                        float oneWayWidth = m_PrefabResolver.GetWidth(crossOneWayPrefab);
                        SetRoad(p, 1, crossOther, RoadForm.TwoWay, twoWay.Name, 0f, errors);
                        foreach (int index in new[] { 1, crossOther })
                        {
                            p.Legs[index].ExternalForm = RoadForm.Dual;
                            p.Legs[index].ExternalPrefab = crossName;
                            p.Legs[index].ExternalWidth = oneWayWidth;
                            p.Legs[index].ExternalMedian = crossMedian;
                        }
                        crossName = twoWay.Name;
                        crossForm = RoadForm.TwoWay;
                        PlanLevels(p, snapshot);
                    }
                }
            }

            // 原路口改造：已匹配的臂接回原有道路，不在原有道路高度的那层（如高架的上层）在臂端加坡道升降过去
            if (matched && !StackedOverMatch)
            {
                foreach (int index in m_Match.RoadOnly ? new[] { 0, 2 } : Enumerable.Range(0, p.Legs.Count).ToArray())
                {
                    // 立体交叉时两条道路各有自己的高度
                    bool mainLeg = index % 2 == 0;
                    p.Legs[index].ExternalElevation = !m_Match.Crossing ? m_Match.LowestElevation
                        : mainLeg ? m_Match.Main.Elevation : m_Match.Cross.Elevation;
                }
                if (m_Match.Crossing && MainOnTop && m_Match.Main.Elevation < m_Match.Cross.Elevation)
                    errors.Add("这处立体交叉里被交道路在上层：该立交类型的主线必须在上层，请换成菱形立交");
            }
            ChooseRamp(p, mainName, errors, snapshot);
            if (IsCloverleaf && setting.CloverleafHugLoop)
                ChooseSharedRamp(p, errors);
            if (IsParclo)
                ChooseSharedRamp(p, errors);
            if (IsRoundabout)
            {
                p.RoundaboutRacetrack = setting.RoundaboutRacetrack;
                ChooseRingRoad(p, crossName, errors);
            }
            snapshot.ExtraRoad = IsParclo ? p.SharedRampPrefab ?? "" : IsRoundabout ? p.RingPrefab ?? "" : "";
            // 苜蓿叶集散车道：用匝道道路，主线在集散车道驶出与汇入之间少掉匝道的车道数（车道平衡沿用匝道车道数）
            // 模式：none 主线交织；auto 按道路类型（是公路的路加，两条都是公路就都加）；main 仅主线；both 两条路都加
            if (IsCloverleaf)
            {
                string mode = setting.CloverleafCollectorMode ?? "none";
                bool mainHighway = m_RoadsByName.TryGetValue(mainOriginal, out RoadInfo mainInfo) && mainInfo.IsHighway;
                bool crossHighway = m_RoadsByName.TryGetValue(crossOriginal, out RoadInfo crossInfo) && crossInfo.IsHighway;
                p.CollectorDistributor = mode == "main" || mode == "both" || (mode == "auto" && mainHighway);
                p.CrossCollectorDistributor = mode == "both" || (mode == "auto" && crossHighway);
            }
            if (setting.LaneBalance)
                ApplyLaneBalance(p, mainName, crossName, snapshot);
            if (IsDirectionalT)
                p.DirectionalTwoLevel = setting.DirectionalTwoLevel;
            if (IsStack)
                p.StackLeftExit = setting.StackLeftExit;
            if (IsExpressway || IsSinglePoint || HasLoops || IsDirectionalT || IsStack)
                p.RampSideGap = setting.RampGap > 0f ? setting.RampGap : 1f;

            snapshot.MainRoad = mainName;
            snapshot.MainForm = mainForm.ToString();
            snapshot.MainLanes = LanesOf(mainName);
            snapshot.CrossRoad = crossName;
            snapshot.CrossForm = crossForm.ToString();
            snapshot.CrossLanes = LanesOf(crossName);

            if (setting.DebugLogging)
            {
                // 每次移动鼠标都会重建预览，只在道路配置变化时记录
                string config = $"main {Describe(p.Legs[0])}; cross {Describe(p.Legs[1])}; " +
                    $"ramp '{snapshot.RampRoad}' {snapshot.RampLanes} lanes (target {snapshot.RampTarget}{(snapshot.RampAuto ? ", auto" : "")})";
                if (config != m_LastDebugConfig)
                    Mod.log.Info($"[debug] {config}");
                m_LastDebugConfig = config;
            }
            return p;
        }

        /// <summary>
        /// 决定一条道路实际用的预制体和形式：
        ///   choice 为空时用 defaultName；形式为 Auto 时单行道路 → 双幅，双向道路 → 双向；
        ///   形式和道路不符（如双向道路要建双幅）时，换成每方向车道数相同的单行/双向同类道路。
        /// </summary>
        private (string name, RoadForm form) ResolveRoad(string choice, string defaultName, RoadFormOption option)
        {
            string name = string.IsNullOrEmpty(choice) ? defaultName : choice;
            m_RoadsByName.TryGetValue(name, out RoadInfo info);
            bool oneWay = info?.IsOneWay ?? false;
            RoadForm form = option switch
            {
                RoadFormOption.TwoWay => RoadForm.TwoWay,
                RoadFormOption.Dual => RoadForm.Dual,
                _ => oneWay ? RoadForm.Dual : RoadForm.TwoWay,
            };

            if (info != null && (form == RoadForm.Dual) != oneWay)
            {
                RoadInfo counterpart = RampMatcher.PickClosest(m_RoadCatalog, info.IsHighway, info.LanesPerDirection, oneWay: form == RoadForm.Dual);
                if (counterpart != null)
                    name = counterpart.Name;
            }
            return (name, form);
        }

        /// <summary>
        /// 匝道道路：面板里指定了就用指定的；否则目标车道数 = 面板值，或自动取主线每方向车道数的一半，
        /// 再从与主线同类（公路/街道）的单行道路里选最接近的。
        /// </summary>
        private void ChooseRamp(InterchangeParams p, string mainRoadName, List<string> errors, PanelSnapshot snapshot)
        {
            m_RoadsByName.TryGetValue(mainRoadName, out RoadInfo main);
            int mainLanes = main?.LanesPerDirection ?? 2;
            int target = setting.RampLanes > 0 ? setting.RampLanes : RampMatcher.DefaultExitLanes(mainLanes);
            // 定向 / 半定向 T 型：两条左转匝道沿支路两幅并排升降，自动选的匝道不比支路宽（车道数不超过支路每方向车道数）
            if (IsDirectionalT && setting.RampLanes <= 0 && m_RoadsByName.TryGetValue(p.Legs[1].RoadPrefab ?? "", out RoadInfo branch))
                target = System.Math.Max(1, System.Math.Min(target, branch.LanesPerDirection));
            bool auto = string.IsNullOrEmpty(setting.RampPrefab);
            p.RampPrefab = auto
                ? RampMatcher.PickRamp(m_RoadCatalog, main?.IsHighway ?? false, target)?.Name ?? kOneWayRoadPrefab
                : setting.RampPrefab;

            if (m_PrefabResolver.TryResolve(p.RampPrefab, m_SelectedRoad, out NetPrefab rampPrefab))
            {
                p.RampWidth = m_PrefabResolver.GetWidth(rampPrefab);
                // 坡度上限：面板值（默认 8%），且不超过匝道和要升降的主线 / 被交道路自身允许的坡度
                p.MaxGrade = (setting.MaxGradePercent > 0f ? setting.MaxGradePercent : 8f) / 100f;
                foreach (string road in new[] { p.RampPrefab, p.Legs[0].RoadPrefab, p.Legs[1].RoadPrefab })
                    if (road != null && m_RoadsByName.TryGetValue(road, out RoadInfo info) && info.MaxSlope > 0f)
                        p.MaxGrade = System.Math.Min(p.MaxGrade, info.MaxSlope);
            }
            else
                errors.Add($"ramp prefab '{p.RampPrefab}' not found");

            snapshot.RampRoad = p.RampPrefab;
            snapshot.RampLanes = LanesOf(p.RampPrefab);
            snapshot.RampTarget = target;
            snapshot.RampAuto = auto;
        }

        /// <summary>
        /// 环岛式立交的环岛：与被交道路同类（公路/街道）的单行道路，车道数取被交道路每方向车道数（至少 2）。
        /// </summary>
        private void ChooseRingRoad(InterchangeParams p, string crossName, List<string> errors)
        {
            m_RoadsByName.TryGetValue(crossName ?? "", out RoadInfo cross);
            int lanes = System.Math.Max(2, cross?.LanesPerDirection ?? 2);
            RoadInfo ring = RampMatcher.PickClosest(m_RoadCatalog, cross?.IsHighway ?? false, lanes, oneWay: true);
            if (ring == null || !m_PrefabResolver.TryResolve(ring.Name, m_SelectedRoad, out NetPrefab prefab))
            {
                errors.Add("找不到可以作环岛的单行道路");
                return;
            }
            p.RingPrefab = ring.Name;
            p.RingWidth = m_PrefabResolver.GetWidth(prefab);
        }

        /// <summary>
        /// 苜蓿叶右转匝道贴环道时与环道合成的那段双向匝道、部分苜蓿叶的匝道主干：每方向车道数与匝道相同、与匝道同类（公路/街道）的双向道路。
        /// </summary>
        private void ChooseSharedRamp(InterchangeParams p, List<string> errors)
        {
            m_RoadsByName.TryGetValue(p.RampPrefab ?? "", out RoadInfo ramp);
            RoadInfo shared = RampMatcher.PickClosest(m_RoadCatalog, ramp?.IsHighway ?? true, ramp?.LanesPerDirection ?? 1, oneWay: false);
            if (shared == null || !m_PrefabResolver.TryResolve(shared.Name, m_SelectedRoad, out NetPrefab prefab))
            {
                errors.Add(IsParclo ? "找不到与匝道车道数相同的双向道路作部分苜蓿叶的匝道主干"
                    : "找不到与匝道车道数相同的双向道路，无法让右转匝道贴环道：请改回外侧大弧线");
                return;
            }
            p.OuterHugsLoop = IsCloverleaf;
            p.SharedRampPrefab = shared.Name;
            p.SharedRampWidth = m_PrefabResolver.GetWidth(prefab);
        }

        /// <summary>
        /// 车道平衡：下匝道分出后主线少掉匝道的车道数，直到上匝道汇入再恢复；
        /// 中式快速路节点的辅路接纳下匝道后多出匝道的车道数，直到上匝道分出。只在找到车道数正好的单行道路时生效。
        /// </summary>
        private void ApplyLaneBalance(InterchangeParams p, string mainName, string crossName, PanelSnapshot snapshot)
        {
            int rampLanes = LanesOf(p.RampPrefab);
            if (rampLanes <= 0)
                return;

            if (p.Legs[0].Form == RoadForm.Dual && m_RoadsByName.TryGetValue(mainName, out RoadInfo main)
                && FindOneWay(main.IsHighway, main.LanesPerDirection - rampLanes) is RoadInfo reduced)
            {
                p.MainReducedPrefab = reduced.Name;
                if (m_PrefabResolver.TryResolve(reduced.Name, m_SelectedRoad, out NetPrefab reducedPrefab))
                    p.MainReducedWidth = m_PrefabResolver.GetWidth(reducedPrefab);
                snapshot.MainReducedRoad = reduced.Name;
                snapshot.MainReducedLanes = reduced.LanesPerDirection;
            }

            // 定向式、涡轮的被交道路两幅也接匝道：同样减车道
            if (IsStack && p.Legs[1].Form == RoadForm.Dual && m_RoadsByName.TryGetValue(crossName, out RoadInfo cross)
                && FindOneWay(cross.IsHighway, cross.LanesPerDirection - rampLanes) is RoadInfo crossReduced
                && m_PrefabResolver.TryResolve(crossReduced.Name, m_SelectedRoad, out NetPrefab crossReducedPrefab))
            {
                p.CrossReducedPrefab = crossReduced.Name;
                p.CrossReducedWidth = m_PrefabResolver.GetWidth(crossReducedPrefab);
            }

            if (IsExpressway && m_RoadsByName.TryGetValue(p.FrontagePrefab ?? "", out RoadInfo frontage)
                && FindOneWay(frontage.IsHighway, frontage.LanesPerDirection + rampLanes) is RoadInfo widened
                && m_PrefabResolver.TryResolve(widened.Name, m_SelectedRoad, out NetPrefab widenedPrefab))
            {
                p.FrontageWidenedPrefab = widened.Name;
                p.FrontageWidenedWidth = m_PrefabResolver.GetWidth(widenedPrefab);
                snapshot.FrontageWidenedRoad = widened.Name;
                snapshot.FrontageWidenedLanes = widened.LanesPerDirection;
            }
        }

        /// <summary>车道数正好为 lanes 的普通单行道路（同为公路或街道优先）；没有时返回 null。</summary>
        private RoadInfo FindOneWay(bool highway, int lanes)
        {
            if (lanes < 1)
                return null;
            RoadInfo road = RampMatcher.PickClosest(m_RoadCatalog, highway, lanes, oneWay: true);
            return road != null && road.LanesPerDirection == lanes ? road : null;
        }

        private int LanesOf(string roadName) => m_RoadsByName.TryGetValue(roadName, out RoadInfo info) ? info.LanesPerDirection : 0;

        private string Describe(LegSpec leg) =>
            $"{leg.Form} '{leg.RoadPrefab}' {leg.RoadWidth:F1} m" + (leg.Form == RoadForm.Dual ? $" median {leg.MedianWidth:F0} m" : "");

        /// <summary>中式快速路节点的地面辅路：面板选的单行道路，默认 Medium Road Oneway。</summary>
        private void SetFrontage(InterchangeParams p, List<string> errors, PanelSnapshot snapshot)
        {
            string name = string.IsNullOrEmpty(setting.FrontageRoadPrefab) ? kOneWayRoadPrefab : setting.FrontageRoadPrefab;
            if (!m_PrefabResolver.TryResolve(name, m_SelectedRoad, out NetPrefab prefab))
            {
                errors.Add($"road prefab '{name}' not found");
                return;
            }
            if (m_RoadsByName.TryGetValue(name, out RoadInfo info) && !info.IsOneWay)
                errors.Add($"辅路 {name} 不是单行道路：辅路两侧各一幅，请选单行道路");
            p.FrontagePrefab = name;
            p.FrontageWidth = m_PrefabResolver.GetWidth(prefab);
            snapshot.FrontageRoad = name;
            snapshot.FrontageLanes = LanesOf(name);
        }

        /// <summary>设置一条道路两端的形式、预制体，并从预制体读出真实宽度。</summary>
        private void SetRoad(InterchangeParams p, int legA, int legB, RoadForm form, string prefabName, float medianWidth, List<string> errors)
        {
            if (!m_PrefabResolver.TryResolve(prefabName, m_SelectedRoad, out NetPrefab prefab))
            {
                errors.Add($"road prefab '{prefabName}' not found");
                return;
            }
            p.SetRoad(legA, legB, form, m_PrefabResolver.GetWidth(prefab), medianWidth, prefabName);

            // 竖向范围决定上下层道路之间需要多少净空
            if (m_RoadsByName.TryGetValue(prefabName, out RoadInfo info) && info.GroundHeight.HasValue && info.ElevatedHeight.HasValue)
            {
                foreach (int index in new[] { legA, legB })
                {
                    p.Legs[index].GroundHeight = info.GroundHeight.Value;
                    p.Legs[index].ElevatedHeight = info.ElevatedHeight.Value;
                    // 立柱信息：决定上层跨越段怎么放才能让立柱避开下层道路
                    p.Legs[index].MiddlePillarSpacing = info.MiddlePillarSpacing;
                    if (info.PillarWidth > 0f)
                        p.Legs[index].PillarWidth = info.PillarWidth;
                    if (info.ElevatedLength > 0f)
                        p.Legs[index].MaxElevatedEdgeLength = info.ElevatedLength;
                }
            }
        }

        /// <summary>同样的错误只写一次日志；面板上一直显示。</summary>
        private void ReportErrors(List<string> errors)
        {
            string text = errors.Count == 0 ? null : string.Join("; ", errors);
            if (text != null && text != m_LastReportedErrors)
                Mod.log.Warn($"Cannot preview interchange: {text}");
            m_LastReportedErrors = text;
        }

        private JobHandle Clear(JobHandle inputDeps)
        {
            applyMode = ApplyMode.Clear;
            return DestroyDefinitions(m_DefinitionQuery, m_ToolOutputBarrier, inputDeps);
        }

        /// <summary>决定是否需要重建预览：位置、朝向或设置版本（面板每次改参数加 1）变化时重建。</summary>
        private readonly struct PreviewKey
        {
            private readonly float2 m_Position;
            private readonly float m_Yaw;
            private readonly int m_SettingsVersion;

            public PreviewKey(float2 position, float yaw, int settingsVersion)
            {
                m_Position = position;
                m_Yaw = yaw;
                m_SettingsVersion = settingsVersion;
            }

            public bool Equals(PreviewKey other) =>
                m_Position.Equals(other.m_Position) && m_Yaw == other.m_Yaw && m_SettingsVersion == other.m_SettingsVersion;
        }
    }

    /// <summary>最近一次预览实际用到的道路和结果，供工具面板显示。</summary>
    public sealed class PanelSnapshot
    {
        /// <summary>路口匹配：none 没指着道路 / matched 已对准 / error 指着道路但无法匹配。</summary>
        public string MatchState = "none";
        public string MatchError = "";
        public int MatchLegs;
        public float MatchAngle;
        /// <summary>“预制体|形式|分隔带”，面板拆开显示。</summary>
        public string MatchMain = "";
        public string MatchCross = "";
        /// <summary>true = 在原有道路上方/下方叠建；false = 原路口改造（放置时拆除范围内的原有道路）。</summary>
        public bool MatchStacked;
        /// <summary>匹配的是立体交叉（虚拟交汇点），不是共用节点的路口。</summary>
        public bool MatchCrossing;
        /// <summary>原路口改造时预览中要拆除的原有路段数。</summary>
        public int RemovedRoads;
        /// <summary>保留的原有道路接回立交的臂端数。</summary>
        public int ConnectedEnds;
        /// <summary>基层跟随已有道路（而不是面板设定）。</summary>
        public bool BaseFollows;
        /// <summary>实际用的基层高度。</summary>
        public float BaseElevation;
        /// <summary>中式快速路节点实际用的辅路；菱形时为空。</summary>
        public string FrontageRoad = "";
        public int FrontageLanes;
        /// <summary>部分苜蓿叶的匝道主干、环岛式的环岛实际用的道路；其他类型为空。</summary>
        public string ExtraRoad = "";
        /// <summary>车道平衡：主线中段和辅路加宽段实际用的道路；未启用或找不到合适道路时为空。</summary>
        public string MainReducedRoad = "";
        public int MainReducedLanes;
        public string FrontageWidenedRoad = "";
        public int FrontageWidenedLanes;
        /// <summary>主线外部双向道路名：立交内部改用双幅并在臂端过渡时非空。</summary>
        public string MainTransition = "";
        public float MainElevation;
        public float CrossElevation;
        /// <summary>实际用的层间高差。</summary>
        public float LevelGap;
        /// <summary>这两种道路之间需要的最小高差（自动高差就取它）。</summary>
        public float MinimumGap;
        public string MainRoad = "";
        public string MainForm = "";
        public int MainLanes;
        public string CrossRoad = "";
        public string CrossForm = "";
        public int CrossLanes;
        public string RampRoad = "";
        public int RampLanes;
        public int RampTarget;
        public bool RampAuto = true;
        /// <summary>实际匝道长度（可能被自动加长）。</summary>
        public float RampLength;
        /// <summary>生成前我们自己检查出的问题（参数、坡度、预制体）。</summary>
        public List<string> Errors = new List<string>();
        /// <summary>游戏对预览报的问题（重叠、高度不足等），由工具定期读取。</summary>
        public List<GameError> GameErrors = new List<GameError>();
        /// <summary>三向立交对准了十字路口：去掉鼠标对面那条臂。</summary>
        public bool MatchDropsLeg;
        /// <summary>实际用的环道半径（米）；不用环道时为 0。</summary>
        public float LoopRadius;
    }
}
