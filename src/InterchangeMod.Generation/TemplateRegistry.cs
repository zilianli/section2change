using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation
{
    /// <summary>所有已实现模板的注册表。新增模板时在这里加一行。</summary>
    public static class TemplateRegistry
    {
        private static readonly IInterchangeTemplate[] Templates =
        {
            new DiamondTemplate(),
            new ExpresswayNodeTemplate(),
            new SinglePointTemplate(),
            // TODO 阶段 3: Cloverleaf, PartialCloverleaf, Trumpet
            // TODO 阶段 5: Turbine, Stack, SinglePoint, Roundabout
        };

        public static IReadOnlyList<IInterchangeTemplate> All => Templates;

        public static IInterchangeTemplate? Get(InterchangeType type) =>
            Templates.FirstOrDefault(t => t.Type == type);
    }
}
