// ComparerModel.cs — PlanComparer 纯逻辑结果模型（无 NX 依赖）
// 性质来源：docs/nx-plan-comparer-spec.md §2-3（编号见各注释）。CompareCore 只读不写输入快照
// （INV-C2/MONO-C1）。Issue 携带可溯 key（INV-C3）。

using System.Collections.Generic;

namespace NXPlugins.PlanComparer
{
    /// <summary>对比容差（决策④直觉默认；首批样例校准后固化为评分规格文档）。</summary>
    public sealed class ComparerOptions
    {
        public double EpsLen = 0.01;    // 数值绝对容差 mm（沿设计 §2.2 几何口径）
        public double RelTol = 0.05;    // 数值相对偏差容差（5%，待校准）
        public double EpsAxis = 1e-6;   // MCS 单位向量轴元素差容差
    }

    /// <summary>非 PASS 项（结构差异/数值偏差/类型失配），Code 稳定可聚合（INV-C4）。</summary>
    public sealed class ComparerIssue
    {
        public string Key = "";         // op 名 / "setup:名" / "tool#序"（INV-C3 可溯）
        public string Code = "";        // OP_PARAM_DIFF / OP_TEMPLATE_DIFF / OP_STRUCT / TOOL_PARAM_DIFF /
                                        // TOOL_TYPE_DIFF / MCS_DIFF / FIXTURE_DIFF / SETUP_STRUCT /
                                        // TOOL_STRUCT / PROGRAM_ORDER_DIFF / ORDER_SHIFT / DUP_NAME /
                                        // READ_MISSING
        public string Detail = "";      // 人类可读，含双侧值与键（不静默）
        public double? AbsDiff = null;  // 数值差（有则填；MCS=欧氏距离）
    }

    /// <summary>Compare 结果（POST-C5：汇总由条目派生——计数与明细一致）。</summary>
    public sealed class ComparerResult
    {
        public readonly List<ComparerIssue> Issues = new List<ComparerIssue>();
        public readonly List<string> Notes = new List<string>();   // 非致命注记（刀名差等）

        // 结构统计（POST-C6）
        public int OpsMatched = 0;      // 成功配对的 op 数
        public int OpsMissing = 0;      // 仅 A 有（重建缺失）
        public int OpsExtra = 0;        // 仅 B 有（重建多余）
        public int ToolsA = 0, ToolsB = 0;
        public int SetupsA = 0, SetupsB = 0;

        // 维度计数（POST-C1/C3/C4 派生；POST-C5：手算 == 汇总）
        public int ParamChecks = 0, ParamPass = 0;
        public int ToolChecks = 0, ToolPass = 0;   // 每把刀一个 check（类型+数值整体 PASS 才计 Pass）
        public int McsChecks = 0, McsPass = 0;     // 每 setup 一个 check
        public int FixtureChecks = 0, FixturePass = 0;
        public int TemplateChecks = 0, TemplatePass = 0;
    }
}
