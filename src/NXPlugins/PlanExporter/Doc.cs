// Doc.cs — schema v3 子集的序列化 DTO（与 autocam-plan.schema.json 对齐；仅含导出 MVP 用域）。
// 无 [DataContract] 特性：DataContractJsonSerializer 对纯 POCO 走默认契约（公开读写属性）。

using System.Collections.Generic;

namespace NXPlugins.PlanExporter
{
    public sealed class PlanDocument
    {
        public string contract_version = "3.0";
        public string plan_id = "";
        public string name = "";
        public string input_ref = "";
        public PlanMeta meta = new PlanMeta();
        public List<SetupJson> setups = new List<SetupJson>();
        public ResourcesJson resources = new ResourcesJson();
        public List<FeatureJson> features = new List<FeatureJson>();
        public List<OperationJson> operations = new List<OperationJson>();
        public List<WorkingstepJson> workingsteps = new List<WorkingstepJson>();
        public WorkplanJson workplan = new WorkplanJson();
        public List<DiagnosticJson> diagnostics = new List<DiagnosticJson>();
    }

    public sealed class PlanMeta
    {
        public string generator = "PlanExporter";
        public string generator_version = "";
        public string created_at = "";
        public string notes = "";
    }

    public sealed class SetupJson
    {
        public string setup_id = "";
        public string name = "";
        public McsJson mcs = new McsJson();
        public double? safe_plane_z = null;
        public int? fixture_offset = null;
    }

    public sealed class McsJson
    {
        public double[] origin = null;
        public double[] z_axis = null;
        public double[] x_axis = null;
    }

    public sealed class ResourcesJson
    {
        public List<ToolJson> tools = new List<ToolJson>();
    }

    public sealed class ToolJson
    {
        public string tool_id = "";
        public string type = "";          // U-7：NX Tool.Types 原文（schema enum）
        public string subtype = null;     // U-7：NX Tool.Subtypes 原文（可选；null=不填，DCJS 输出 "subtype":null 同 fixture_offset 先例）
        public double? diameter = null;
        public int? num_flutes = null;
        public double? flute_length = null;
        public double? lower_corner_radius = null;
    }

    public sealed class FeatureJson
    {
        public string feature_id = "";
        public string feature_type = "geometry_group";   // 自由串两档（D-4/X）：导出恒组级口径
        public Dictionary<string, double> @params = new Dictionary<string, double>();
    }

    public sealed class OperationJson
    {
        public string operation_id = "";
        public string operation_type = "";
        public NxTemplateJson nx_template = new NxTemplateJson();
        public string tool_ref = "";
        public string method_ref = "";
        // v1.5-③：参数值 = ParamValue 联合（数值 N / 枚举串 S），KV 数组 Value = {"N":..}|{"S":..} 包装（P0 实证）
        public Dictionary<string, ParamValue> strategy = new Dictionary<string, ParamValue>();
        public Dictionary<string, ParamValue> technology = new Dictionary<string, ParamValue>();
        // v2：op 级 cut-area 面签名（可选，additive；导出侧采集、重建侧签名匹配指派。值取整见 FaceSignature）
        public List<FaceSignatureJson> cut_area_signatures = null;   // null=不落盘（DCJS 对 null 字段落 null？→ 序列化前由 ExporterCore 置空表转 null？沿 fixture_offset 先例：null 落盘）
    }

    public sealed class FaceSignatureJson
    {
        public int face_type = 0;
        public string normal_axis = "";
        public double rx = 0, ry = 0, rz = 0;    // 0.01mm 取整值（采集侧已取整）
        public double radius = 0;                 // 0.001 取整
    }

    public sealed class NxTemplateJson
    {
        public string type = "";
        public string subtype = "";
    }

    public sealed class WorkingstepJson
    {
        public string workingstep_id = "";
        public string feature_ref = "";
        public string operation_ref = "";
        public string setup_ref = "";
    }

    public sealed class WorkplanJson
    {
        public WorkplanNodeJson root = new WorkplanNodeJson();
    }

    public sealed class WorkplanNodeJson
    {
        public string kind = "program";
        public string name = "";
        public string @ref = "";
        public List<WorkplanNodeJson> children = new List<WorkplanNodeJson>();
    }

    public sealed class DiagnosticJson
    {
        public string level = "info";
        public string code = "";
        public string message = "";
        public string operation_id = "";
    }
}
