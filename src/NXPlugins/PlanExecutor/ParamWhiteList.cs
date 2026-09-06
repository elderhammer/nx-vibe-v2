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
            { "depth_per_cut", new ParamTarget("CutLevel.GlobalDepthPerCut.DistanceBuilder", ParamKind.Number) },
            // v2.5（2026-09-05 camprobe-v2depth-211011 实证）：腔写面目标改 CutLevel 子树（引擎消费成员）；
            // op 级 DepthPerCut 写持久但惰性（写对照刀路零变化）——旧键行原指该惰性成员。
            { "reference_tool", new ParamTarget("ReferenceTool", ParamKind.Number) },
            // v2.5 参考刀具批（2026-09-06 camprobe-v2reftool 定案，注册表 #17）：参考刀具（残余加工清根）。值 = 参考
            // 刀具直径 mm（跨件表达：gt 名 "17.0" vs 重建 T-001 名差 → 按直径 0.001 匹配库刀，P3 实证）；
            // 写侧 NX 落点 = b.ReferenceTool（MillOperationBuilder，NX7.5，Tool 对象直赋）。OP-002 只切
            // 一小截现象（gt 36 区 vs 重建 118 区 43×）系此键缺失：写回 Ø17 → regen 36 区 = gt 同数。
            { "transfer_within_levels", new ParamTarget("NonCuttingBuilder.TransferWithinLevelsType", ParamKind.Enum) },
            // v2.5 转移族批（2026-09-06 camprobe-v2ncm 实证，注册表 #18）：层内转移方式。OP-002 36 区
            // 同构下残余长度差（重建 3257 vs gt 929）主因 = 模板默认 Clearance（抬刀模式）vs gt Direct
            // （直接平移，gt 四腔 op 全 Direct/UseEngret，v2surf-gt 195205 矩阵）→ 写回 Direct → 长度
            // 收敛 71%（[I] 142827/143155：重建件 3257→1299）。#19 height 键负结案撤采：Value 写 commit
            // 还原（camprobe-v2ncmh 四写序变体 Intent/ValueIntent 持久、Value 恒回模板 3 = UI 可设 API
            // 不可写，stepover #9 同族新实例）。
            { "hole_depth",    new ParamTarget("HoleDepth", ParamKind.Number) },   // OperationBuilder 级（PTP/钻孔均可达）
            // v1.5-③ S1：注册表 4 持久键（E1/E7 锚定；E3 cut_order/cut_direction v1 单跑——I-2 [I] 复跑点亮）
            { "cut_pattern",   new ParamTarget("CutPattern.CutPattern", ParamKind.Enum) },
            { "cut_order",     new ParamTarget("CutParameters.CutOrder", ParamKind.Enum) },
            { "cut_direction", new ParamTarget("CutParameters.CutDirection.Type", ParamKind.Enum) },
            { "finish_passes", new ParamTarget("CutParameters.FinishPasses.NumberOfFinishPasses", ParamKind.Number) },
        };

        /// <summary>technology 可写键 → 写面目标（rpm 持久 [I] 已证；feed_cut 三跑持久实证
        /// 2026-09-05 camprobe-feedcut，注册表 #15 补行——v1.5-⑤）。</summary>
        public static readonly Dictionary<string, ParamTarget> TechnologyWritable =
            new Dictionary<string, ParamTarget>
        {
            { "spindle_rpm", new ParamTarget("FeedsBuilder.SpindleRpmBuilder", ParamKind.Number) },
            { "feed_cut",    new ParamTarget("FeedsBuilder.FeedCutBuilder", ParamKind.Number) },
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
