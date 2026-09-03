// ParamWhiteList.cs — 重建侧可写参数面白名单（PRE-4）
// 依据（2026-09-04 实测）：普通 Inheritable 参数 .Value 写→commit→重开持久可靠
// （camprobe-finalize E4 PartStock / camprobe-executor P2 Tl* / P4 FixtureOffset）；
// Stepover 整链 commit 写无效（U-6，拒收）。成员路径供 NX 适配器按形态分派（v1 全 .Value）。

using System.Collections.Generic;

namespace NXPlugins.PlanExecutor
{
    public static class ParamWhiteList
    {
        /// <summary>strategy 可写键 → NX 成员路径（Inheritable*.Value 形态）。</summary>
        public static readonly Dictionary<string, string> StrategyWritable =
            new Dictionary<string, string>
        {
            { "part_stock",    "CutParameters.PartStock" },
            { "floor_stock",   "CutParameters.FloorStock" },
            { "wall_stock",    "CutParameters.WallStock" },
            { "depth_per_cut", "DepthPerCut" },
            { "hole_depth",    "HoleDepth" },          // OperationBuilder 级（PTP/钻孔均可达）
        };

        /// <summary>technology 可写键 → NX 成员路径（rpm 写入持久已实测）。</summary>
        public static readonly Dictionary<string, string> TechnologyWritable =
            new Dictionary<string, string>
        {
            { "spindle_rpm", "FeedsBuilder.SpindleRpmBuilder" },
        };

        /// <summary>PRE-4 判据源：两张表非空。</summary>
        public static bool IsReady { get { return StrategyWritable.Count > 0 && TechnologyWritable.Count > 0; } }

        public static bool TryGetPath(string planKey, bool inTechnology, out string memberPath)
        {
            if (inTechnology) return TechnologyWritable.TryGetValue(planKey, out memberPath);
            return StrategyWritable.TryGetValue(planKey, out memberPath);
        }
    }
}
