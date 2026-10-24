using System;
using System.Linq;
using System.Reflection;
using Game.Tools;

namespace InterchangeMod.Systems
{
    /// <summary>
    /// 与 Anarchy 模组（无视碰撞）的可选联动。不引用 Anarchy 的程序集，运行时用反射查找它公开的
    /// Anarchy.Bridge.AnarchyBridge：
    ///   TryAddToolSystem(tool) —— 把本工具加入 Anarchy 的工具列表，开启 Anarchy 时它会移除本工具预览上的大部分错误；
    ///   GetAnarchyToggleState() —— Anarchy 当前是否开启。
    /// Anarchy 的开关在 UI 端通过它自己的绑定（组 "Anarchy"：值 AnarchyEnabled、触发 AnarchyToggled）切换。
    /// 没装 Anarchy 时这里什么都不做。
    /// </summary>
    internal static class AnarchyIntegration
    {
        private static bool s_Searched;
        private static MethodInfo s_TryAddToolSystem;
        private static MethodInfo s_GetToggleState;

        /// <summary>已安装并加载了 Anarchy。</summary>
        public static bool Available
        {
            get
            {
                Find();
                return s_TryAddToolSystem != null;
            }
        }

        /// <summary>把工具注册给 Anarchy；成功或已注册时返回 true。</summary>
        public static bool TryRegister(ToolBaseSystem tool)
        {
            Find();
            if (s_TryAddToolSystem == null)
                return false;
            try
            {
                s_TryAddToolSystem.Invoke(null, new object[] { tool });
                return true;
            }
            catch (Exception e)
            {
                Mod.log.Warn($"Anarchy integration failed: {e.InnerException?.Message ?? e.Message}");
                s_TryAddToolSystem = null;
                return false;
            }
        }

        /// <summary>Anarchy 当前是否开启（没装时为 false）。</summary>
        public static bool Enabled
        {
            get
            {
                Find();
                if (s_GetToggleState == null)
                    return false;
                try
                {
                    return (bool)s_GetToggleState.Invoke(null, null);
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void Find()
        {
            if (s_Searched)
                return;
            s_Searched = true;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Anarchy");
            Type bridge = assembly?.GetType("Anarchy.Bridge.AnarchyBridge");
            if (bridge == null)
                return;
            s_TryAddToolSystem = bridge.GetMethod("TryAddToolSystem", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ToolBaseSystem) }, null);
            s_GetToggleState = bridge.GetMethod("GetAnarchyToggleState", BindingFlags.Public | BindingFlags.Static);
            Mod.log.Info(s_TryAddToolSystem != null ? "Anarchy found: interchange tool can ignore collisions" : "Anarchy found but its bridge API changed");
        }
    }
}
