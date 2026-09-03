// RebuildPlan.cs — PlanExecutor 纯逻辑指令模型（无 NX 依赖）
// 性质来源：docs/nx-plan-executor-spec.md §2-3（编号见各注释）。本文件仅数据承载，
// 构建/校验在 ExecutorCore / ParamWhiteList / ToolFamilyMap。

using System.Collections.Generic;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanExecutor
{
    public enum RebuildDiagLevel { Info, Warning, Error }

    /// <summary>重建诊断（POST-2：scope 可定位到 op/ws/setup）。</summary>
    public sealed class RebuildDiag
    {
        public RebuildDiagLevel Level = RebuildDiagLevel.Info;
        public string Code = "";
        public string Message = "";
        public string Scope = "";        // 如 "OP-001" / "S-01" / "WS-02"（INV-4 聚合键）
    }

    /// <summary>可写参数指令（PRE-4 白名单产物；v1 仅 .Value 形态）。</summary>
    public sealed class ParamInstruction
    {
        public readonly string MemberPath;   // NX 成员路径，如 "CutParameters.PartStock"
        public readonly double Value;
        public ParamInstruction(string memberPath, double value) { MemberPath = memberPath; Value = value; }
    }

    /// <summary>刀具重建指令（D-2=A：模板对来自家族关键词解析 + 数值直填）。</summary>
    public sealed class ToolCommand
    {
        public readonly string ToolId;          // plan tool_id
        public readonly TemplatePair Pair;      // 重建用模板对
        public readonly bool TypeInferred;      // true=关键词推断/默认（非精确）
        public double? Diameter = null;
        public int? NumFlutes = null;
        public double? FluteLength = null;
        public double? LowerCornerRadius = null;
        public ToolCommand(string toolId, TemplatePair pair, bool typeInferred)
        { ToolId = toolId; Pair = pair; TypeInferred = typeInferred; }
    }

    /// <summary>程序组指令（全名 = 父链路径拼 "/"，根层 PROGRAM 为默认组不建）。</summary>
    public sealed class ProgramCommand
    {
        public readonly string Full;            // 如 "A01"（根层）/ "A01/子组"
        public readonly string Name;            // 本组名
        public readonly string ParentFull;      // 父全名（"" = 默认 PROGRAM 组下）
        public ProgramCommand(string name, string parentFull)
        { Name = name; ParentFull = parentFull; Full = parentFull.Length == 0 ? name : parentFull + "/" + name; }
    }

    /// <summary>setup → MCS 几何链指令（MCS 组 + 其下 WORKPIECE 子组，v1 无几何指派）。</summary>
    public sealed class GeometryChainCommand
    {
        public readonly string SetupId;
        public readonly string McsGroupName;    // setup.name 或派生名
        public readonly string WorkpieceName;   // 恒 "WORKPIECE"（组下唯一）
        public double[] McsOrigin = null;       // mcs.origin（可为 null → NX 默认）
        public double[] McsZAxis = null;
        public double[] McsXAxis = null;
        public int? FixtureOffset = null;       // PRE-4：fixture_offset 写入（实证）
        public GeometryChainCommand(string setupId, string mcsGroupName, string workpieceName)
        { SetupId = setupId; McsGroupName = mcsGroupName; WorkpieceName = workpieceName; }
    }

    /// <summary>工序重建指令（POST-1：四父锚点齐）。</summary>
    public sealed class OpCommand
    {
        public readonly string OpId;
        public readonly string DisplayName;     // ws 节点名兜底 op_id（NX 新建名）
        public readonly TemplatePair Pair;
        public readonly string ProgramFull;     // 程序锚点（"PROGRAM" 默认组）
        public readonly string MethodAnchor;    // 方法锚点（""=方法根；模板默认组名；自定义名）
        public readonly bool MethodNeedsCreate; // 自定义方法组需创建（近似，warning 见 diag）
        public readonly string ToolId;          // → ToolCommand.ToolId
        public readonly string SetupId;         // → GeometryChainCommand.SetupId
        public readonly List<ParamInstruction> Params = new List<ParamInstruction>();
        public OpCommand(string opId, string displayName, TemplatePair pair, string programFull,
            string methodAnchor, bool methodNeedsCreate, string toolId, string setupId)
        {
            OpId = opId; DisplayName = displayName; Pair = pair;
            ProgramFull = programFull; MethodAnchor = methodAnchor;
            MethodNeedsCreate = methodNeedsCreate; ToolId = toolId; SetupId = setupId;
        }
    }

    /// <summary>Build 结果（MONO-1：Ok=false 时无任何指令，适配器不落盘）。</summary>
    public sealed class RebuildPlan
    {
        public bool Ok = true;
        public readonly List<RebuildDiag> Diagnostics = new List<RebuildDiag>();
        public readonly List<ProgramCommand> Programs = new List<ProgramCommand>();
        public readonly List<ToolCommand> Tools = new List<ToolCommand>();
        public readonly List<GeometryChainCommand> Setups = new List<GeometryChainCommand>();
        public readonly List<OpCommand> Operations = new List<OpCommand>();
    }
}
