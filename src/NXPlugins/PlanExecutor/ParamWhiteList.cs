// ParamWhiteList.cs — 重建侧可写参数面白名单（PRE-4 / V15-PRE-2）
// 依据（实测）：普通 Inheritable 参数 .Value 写→commit→重开持久可靠（camprobe-finalize E4 PartStock /
// camprobe-executor P2 Tl* / P4 FixtureOffset）；Stepover 整链 commit 写无效（U-6，拒收）；
// v1.5-④ 收口三跑（docs/nx-param-registry-spec.md §2 #1-8）：cut_pattern/cut_order/cut_direction/
// finish_passes 四持久键（E1-E7 锚定），MultiDepthCut 整对象 + Boundary 容差族负结案（拒收）。
// 成员路径供 NX 适配器按形态分派（kind = Number → Inheritable/直接赋值 .Value；Enum → 词 Parse 直赋）。

using System.Collections.Generic;

namespace NXPlugins.PlanExecutor
{
    /// <summary>写入形态：Number = 数值（Inheritable .Value 或 int/double 直赋）；Enum = NX 枚举原文串（词集在 NxParamWords）。</summary>
    public enum ParamKind { Number, Enum }

    /// <summary>写面目标（成员路径 + 取值形态 kind）。</summary>
    public sealed class ParamTarget
    {
        public readonly string MemberPath;
        public readonly ParamKind Kind;
        public ParamTarget(string memberPath, ParamKind kind) { MemberPath = memberPath; Kind = kind; }
        public override string ToString() { return MemberPath + ":" + Kind; }
    }

    public static class ParamWhiteList
    {
        /// <summary>strategy 可写键 → 写面目标（数值键 Inheritable .Value 形态；枚举键词 Parse 直赋）。</summary>
        public static readonly Dictionary<string, ParamTarget> StrategyWritable =
            new Dictionary<string, ParamTarget>
        {
            { "part_stock",    new ParamTarget("CutParameters.PartStock", ParamKind.Number) },
            { "floor_stock",   new ParamTarget("CutParameters.FloorStock", ParamKind.Number) },
            { "wall_stock",    new ParamTarget("CutParameters.WallStock", ParamKind.Number) },
            { "depth_per_cut", new ParamTarget("DepthPerCut", ParamKind.Number) },
            { "hole_depth",    new ParamTarget("HoleDepth", ParamKind.Number) },   // OperationBuilder 级（PTP/钻孔均可达）
            // v1.5-③ S1：注册表 4 持久键（E1/E7 锚定；E3 cut_order/cut_direction v1 单跑——I-2 [I] 复跑点亮）
            { "cut_pattern",   new ParamTarget("CutPattern.CutPattern", ParamKind.Enum) },
            { "cut_order",     new ParamTarget("CutParameters.CutOrder", ParamKind.Enum) },
            { "cut_direction", new ParamTarget("CutParameters.CutDirection.Type", ParamKind.Enum) },
            { "finish_passes", new ParamTarget("CutParameters.FinishPasses.NumberOfFinishPasses", ParamKind.Number) },
        };

        /// <summary>technology 可写键 → 写面目标（rpm 写入持久已实测）。</summary>
        public static readonly Dictionary<string, ParamTarget> TechnologyWritable =
            new Dictionary<string, ParamTarget>
        {
            { "spindle_rpm", new ParamTarget("FeedsBuilder.SpindleRpmBuilder", ParamKind.Number) },
        };

        /// <summary>PRE-4 判据源：两张表非空。</summary>
        public static bool IsReady { get { return StrategyWritable.Count > 0 && TechnologyWritable.Count > 0; } }

        public static bool TryGetTarget(string planKey, bool inTechnology, out ParamTarget target)
        {
            if (inTechnology) return TechnologyWritable.TryGetValue(planKey, out target);
            return StrategyWritable.TryGetValue(planKey, out target);
        }
    }
}
