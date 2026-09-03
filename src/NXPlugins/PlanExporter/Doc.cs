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
        public List<object> machines = new List<object>();
    }

    public sealed class ToolJson
    {
        public string tool_id = "";
        public string type = "";
        public double? diameter = null;
        public int? num_flutes = null;
        public double? flute_length = null;
        public double? lower_corner_radius = null;
    }

    public sealed class FeatureJson
    {
        public string feature_id = "";
        public string feature_type = "geometry_group";
        public GeometryRefJson geometry_ref = new GeometryRefJson();
        public Dictionary<string, double> @params = new Dictionary<string, double>();
    }

    public sealed class GeometryRefJson
    {
        public List<object> face_anchors = new List<object>();   // U-5 结案：首版恒空
        public List<string> face_ids = new List<string>();
        public List<string> edge_ids = new List<string>();
        public double[] anchor_point = null;
    }

    public sealed class OperationJson
    {
        public string operation_id = "";
        public string operation_type = "";
        public NxTemplateJson nx_template = new NxTemplateJson();
        public string tool_ref = "";
        public string method_ref = "";
        public Dictionary<string, double> strategy = new Dictionary<string, double>();
        public Dictionary<string, double> technology = new Dictionary<string, double>();
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
