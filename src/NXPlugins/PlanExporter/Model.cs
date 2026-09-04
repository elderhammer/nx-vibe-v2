// Model.cs — PlanExporter 纯逻辑层数据模型（NX 适配器与核心的隔离边界；无 NXOpen 依赖）
//
// 性质来源：docs/nx-plan-exporter-spec.md §2-3（编号性质见各注释）。本文件仅数据承载，
// 校验/映射在 PlanValidator / ExporterCore。

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace NXPlugins.PlanExporter
{
    /// <summary>参数值联合类型（v1.5-③，V15-*）：kind 按键固定于参数键集注册表（docs/nx-param-registry-spec.md）——
    /// 数值键用 N、枚举键用 S（NX 枚举原文串，schema 词集直写先例）。本批无布尔键 → B 不入生产（S2 时加）。
    /// P0 DCJS 探针实证（2026-09-04）：EmitDefaultValue=false 下仅设字段落盘、N=0 与缺值可区分、round-trip 无损。
    /// implicit 转换供夹具/兼容赋值（double/string → 对应 kind 字段）。</summary>
    [DataContract]
    public sealed class ParamValue
    {
        [DataMember(EmitDefaultValue = false)] public double? N;
        [DataMember(EmitDefaultValue = false)] public string S;

        public ParamValue() { }
        public ParamValue(double n) { N = n; }
        public ParamValue(string s) { S = s; }

        public static implicit operator ParamValue(double d) { return new ParamValue(d); }
        public static implicit operator ParamValue(string s) { return new ParamValue(s); }

        public override string ToString()
        {
            if (N.HasValue) return N.Value.ToString("0.####");
            if (S != null) return S;
            return "(null)";
        }
    }

    /// <summary>NX Tag 的纯逻辑镜像（四视图去重键，INV-5）。NX 侧把整型 Tag 包装进来。</summary>
    public sealed class TagKey : IEquatable<TagKey>
    {
        public readonly ulong Raw;
        public TagKey(ulong raw) { Raw = raw; }
        public bool Equals(TagKey other) { return other != null && Raw == other.Raw; }
        public override bool Equals(object obj) { return Equals(obj as TagKey); }
        public override int GetHashCode() { return Raw.GetHashCode(); }
        public override string ToString() { return "Tag(" + Raw + ")"; }
    }

    /// <summary>一次导出的输入快照：NX 适配器（会话内只读遍历，[I] 层）填此模型，核心不再碰 NX。</summary>
    public sealed class ExportSnapshot
    {
        public string PlanId = "PLAN-0001";          // POST-1 必填域（缺省由调用方覆盖）
        public string Name = "";                     // plan.name
        public string InputRef = "";                 // 源 prt 相对引用
        public string GeneratorVersion = "0.1.0";    // meta.generator_version
        public string CreatedAt = "";                // RFC3339，由适配器填
        public readonly List<OperationItem> Operations = new List<OperationItem>();
        public readonly List<ToolItem> Tools = new List<ToolItem>();
        public readonly List<SetupItem> Setups = new List<SetupItem>();
        /// <summary>顶层程序组名序列（comparer 顶层组序比对用；导出侧不再消费——v1.5-① 起树形取 ProgramTree）。</summary>
        public readonly List<string> ProgramOrder = new List<string>();
        /// <summary>顶层程序组树（v1.5-①：嵌套组真实展开，组内成员保 NX GetMembers 序；空 = 无程序组）。</summary>
        public readonly List<ProgramNode> ProgramTree = new List<ProgramNode>();
    }

    /// <summary>程序组树节点（ProgramOrder 视图自包含结构；子组经 Members 引用嵌套）。</summary>
    public sealed class ProgramNode
    {
        public string Name = "";
        /// <summary>组内有序成员（NX GetMembers 序：工序与子组混合——保刀路输出序）。</summary>
        public readonly List<ProgramMember> Members = new List<ProgramMember>();
    }

    /// <summary>程序组内一个有序成员。</summary>
    public sealed class ProgramMember
    {
        public bool IsOperation = true;   // false = 子组
        public ProgramNode Group = null;  // !IsOperation：子组节点（递归）
        public string OpName = "";        // IsOperation：工序名（日志/退化关联）
        public TagKey OpKey = null;       // IsOperation：工序去重键（生产采集必填，同名/跨组精确定位）
    }

    /// <summary>一个工序在 NX 中的结构镜像（INV-4：四父链；INV-5：TagKey 唯一）。</summary>
    public sealed class OperationItem
    {
        public string Name = "";                     // NX 对象名（含 _COPY 链后缀）
        public string UserName = "";                 // 显示名（可空）
        public TagKey Key = null;                    // 四视图去重键（必填，PRE-1 检查）
        public string TypeFamily = "";               // GetNameOfType 大类描述串（如 Cavity Milling / Point to Point）
        public string ProgramParent = "";            // 程序父组名（A01 等；顶层空串）
        public string MethodParent = "";             // 方法父组名
        public string ToolParent = "";               // 刀具父组名
        public string GeometryParent = "";           // 几何父组名（WORKPIECE 等；缺失→warning）
        public bool HasGeometryParent = true;        // 几何父链是否存在（INV-4 判据）
        public readonly List<string> ReadbackErrors = new List<string>(); // 字段级回读失败（POST-3）
        /// <summary>导出侧解析出的参数值（注册表按键定 kind，v1.5-③）；缺失值不入此表。
        /// tech: 前缀键 → technology 段（ExporterCore 分流）。数值原样（POST-4）。</summary>
        public readonly Dictionary<string, ParamValue> Params = new Dictionary<string, ParamValue>();
        /// <summary>同大类歧义时由 WhiteList.Resolve 给出（POST-6），否则空串。</summary>
        public string TemplateType = "";
        public string TemplateSubtype = "";
        public bool TemplateAmbiguous = false;
    }

    public sealed class ToolItem
    {
        public string Name = "";
        public string TypeFamily = "";               // 组 GetNameOfType（铣刀-5 参数 等，语言敏感——容器判定/回退用）
        public string NxType = "";                   // U-7：Tool.GetTypeAndSubtype 的 Types 原文（schema type 词）
        public string NxSubtype = "";                // U-7：Subtypes 原文（可空；空 → 落盘不填）
        public string TypeReadbackError = "";        // U-7：as Tool/读回失败原因（非空 → 该刀不入 plan，INV-U7-4）
        public double? Diameter = null;
        public int? NumFlutes = null;
        public double? FluteLength = null;
        public double? LowerCornerRadius = null;
    }

    public sealed class SetupItem
    {
        public string Name = "";
        public double[] McsOrigin = null;            // U-4：适配器实测前允许 null
        public double[] McsZAxis = null;
        public double[] McsXAxis = null;
        public double? SafePlaneZ = null;
        public int? FixtureOffset = null;
        public bool MissingMcs = false;              // 缺 MCS → diagnostic error
    }

    public enum DiagLevel { Info, Warning, Error }

    public sealed class Diagnostic
    {
        public DiagLevel Level = DiagLevel.Info;
        public string Code = "";
        public string Message = "";
        public string OperationName = "";            // 归属工序（INV-6 聚合键之一）
    }

    /// <summary>Create 用模板对（type=模板部件名 / subtype=对象模板类型，实证见索引 §2.1）。</summary>
    public sealed class TemplatePair
    {
        public readonly string Type;
        public readonly string Subtype;
        public TemplatePair(string type, string subtype) { Type = type; Subtype = subtype; }
        public override string ToString() { return "(" + Type + ", " + Subtype + ")"; }
    }
}
