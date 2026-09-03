// ExporterCore.cs — 快照 → PlanDocument 的核心映射（纯逻辑，无 NX 依赖）
// 性质落点：INV-3（1 op ↔ 1 ws）、INV-4（四父链缺失 warning）、INV-5（Tag 去重守卫）、
// INV-6（diagnostics 聚合：同 op 同 code 一次）、POST-3（ReadbackErrors → diag，不静默）、
// POST-6（歧义 → 默认对 + warning）。workplan 树序 = 快照顶层程序组序列 + 每工序节点。

using System;
using System.Collections.Generic;

namespace NXPlugins.PlanExporter
{
    public static class ExporterCore
    {
        public static PlanDocument Build(ExportSnapshot snap, Func<string, TemplateResolution> resolve)
        {
            if (snap == null) throw new ArgumentNullException("snap");
            var doc = new PlanDocument();
            doc.plan_id = string.IsNullOrEmpty(snap.PlanId) ? "PLAN-0001" : snap.PlanId;
            doc.name = snap.Name;
            doc.input_ref = snap.InputRef;
            doc.meta.generator_version = snap.GeneratorVersion;
            doc.meta.created_at = snap.CreatedAt;

            var diags = new List<Diagnostic>();
            var seen = new Dictionary<TagKey, string>();          // INV-5 守卫

            // setups（快照直通；U-4 前适配器可给部分值）
            for (int i = 0; i < snap.Setups.Count; i++)
            {
                SetupItem s = snap.Setups[i];
                var sj = new SetupJson { setup_id = "S-" + (i + 1).ToString("D2"), name = s.Name };
                if (s.MissingMcs)
                {
                    sj.mcs = null;
                    AddDiag(diags, DiagLevel.Error, "MCS_MISSING", "setup " + s.Name + " 无 MCS 回读", "");
                }
                else
                {
                    sj.mcs = new McsJson { origin = s.McsOrigin, z_axis = s.McsZAxis, x_axis = s.McsXAxis };
                    sj.safe_plane_z = s.SafePlaneZ;
                    sj.fixture_offset = s.FixtureOffset;
                }
                doc.setups.Add(sj);
            }

            // tools（INV-2 的 tool_ref 目标）
            for (int i = 0; i < snap.Tools.Count; i++)
            {
                ToolItem t = snap.Tools[i];
                doc.resources.tools.Add(new ToolJson
                {
                    tool_id = "T-" + (i + 1).ToString("D3"),
                    type = t.TypeFamily,
                    diameter = t.Diameter,
                    num_flutes = t.NumFlutes,
                    flute_length = t.FluteLength,
                    lower_corner_radius = t.LowerCornerRadius,
                });
            }

            // operations / workingsteps / features（1:1）与 workplan 节点
            var wpByParent = new Dictionary<string, WorkplanNodeJson>();
            var root = doc.workplan.root;
            root.name = "PROGRAM";
            foreach (string prog in snap.ProgramOrder)
            {
                var g = new WorkplanNodeJson { kind = "program", name = prog };
                root.children.Add(g);
                wpByParent[prog] = g;
            }

            int opIdx = 0, wsIdx = 0, featIdx = 0;
            foreach (OperationItem o in snap.Operations)
            {
                if (o.Key == null) { AddDiag(diags, DiagLevel.Error, "OP_NO_TAG", "工序缺 TagKey: " + o.Name, o.Name); continue; }
                if (seen.ContainsKey(o.Key))
                {
                    AddDiag(diags, DiagLevel.Error, "DUP_TAG", "同一工序重复出现（四视图去重失败）: " + o.Name, o.Name);
                    continue;   // INV-5：重复源不入 plan
                }
                seen[o.Key] = o.Name;

                if (!o.HasGeometryParent)
                    AddDiag(diags, DiagLevel.Warning, "GEOM_PARENT_MISSING", "工序无几何父链: " + o.Name, o.Name);  // INV-4

                // nx_template（POST-6 / 缺家族 → error）
                TemplateResolution res = resolve == null ? null : resolve(o.TypeFamily);
                string tType = "", tSub = "";
                bool noTemplate = res == null || res.Pair == null;
                if (!noTemplate) { tType = res.Pair.Type; tSub = res.Pair.Subtype; }
                else AddDiag(diags, DiagLevel.Error, "TPL_UNKNOWN",
                             "无模板对可解析（家族=" + o.TypeFamily + "）: " + o.Name, o.Name);
                if (noTemplate || res.Ambiguous)
                    AddDiag(diags, DiagLevel.Warning, "TPL_AMBIGUOUS",
                            "模板对为默认项（歧义/未知），需人工/识别侧复核: " + o.Name, o.Name);

                // POST-3：ReadbackErrors → diag，不静默
                foreach (string err in o.ReadbackErrors)
                    AddDiag(diags, DiagLevel.Error, "READBACK_FAIL", err, o.Name);

                // 参数回填：MVP 字段进 strategy/technology 占位（形态注册表后续按字段细化）
                var strategy = new Dictionary<string, double>();
                var technology = new Dictionary<string, double>();
                foreach (KeyValuePair<string, double> kv in o.Params)
                {
                    if (kv.Key.StartsWith("tech:", StringComparison.Ordinal)) technology[kv.Key.Substring(5)] = kv.Value;
                    else strategy[kv.Key] = kv.Value;
                }

                opIdx++;
                string opId = "OP-" + opIdx.ToString("D3");
                var oj = new OperationJson
                {
                    operation_id = opId,
                    operation_type = FamilyToOperationType(o.TypeFamily),
                    tool_ref = "T-" + (IndexByName(snap.Tools, o.ToolParent) + 1).ToString("D3"),
                    method_ref = o.MethodParent,
                    strategy = strategy,
                    technology = technology,
                };
                oj.nx_template.type = tType;
                oj.nx_template.subtype = tSub;
                doc.operations.Add(oj);

                wsIdx++;
                string wsId = "WS-" + wsIdx.ToString("D2");
                doc.workingsteps.Add(new WorkingstepJson
                {
                    workingstep_id = wsId,
                    feature_ref = "F-" + (++featIdx).ToString("D2"),
                    operation_ref = opId,
                    setup_ref = snap.Setups.Count > 0 ? "S-01" : "",
                });
                doc.features.Add(new FeatureJson { feature_id = "F-" + featIdx.ToString("D2") });
                // geometry_ref.anchor 兜底（U-5 结案：face_anchors 留空）
                doc.features[doc.features.Count - 1].geometry_ref.anchor_point = null;

                // workplan：工序节点挂到其程序父组（缺父 → 挂 root）
                WorkplanNodeJson parent;
                if (!wpByParent.TryGetValue(o.ProgramParent, out parent)) parent = root;
                parent.children.Add(new WorkplanNodeJson { kind = "workingstep", name = o.Name, @ref = wsId });
            }

            // INV-6：聚合 + 落文档
            foreach (Diagnostic d in diags)
                doc.diagnostics.Add(new DiagnosticJson
                {
                    level = d.Level.ToString().ToLowerInvariant(),
                    code = d.Code,
                    message = d.Message,
                    operation_id = d.OperationName,
                });
            return doc;
        }

        /// <summary>GetNameOfType 大类 → operation_type 粗类（U-1 决议：细类交给识别/CAPP 层）。</summary>
        public static string FamilyToOperationType(string family)
        {
            if (family == null) return "other";
            string f = family.ToLowerInvariant();
            if (f.Contains("cavity") || f.Contains("milling")) return "milling";
            if (f.Contains("drill") || f.Contains("point to point") || f.Contains("hole")) return "drilling";
            return "other";
        }

        private static int IndexByName(List<ToolItem> tools, string name)
        {
            for (int i = 0; i < tools.Count; i++)
                if (tools[i].Name == name) return i;
            return -1;
        }

        private static void AddDiag(List<Diagnostic> diags, DiagLevel level, string code, string message, string opName)
        {
            // INV-6 聚合：同 op + 同 code + 同消息 的重复源只保留一次；不同消息不吞并
            foreach (Diagnostic d in diags)
                if (d.Code == code && d.OperationName == opName && d.Message == message) return;
            diags.Add(new Diagnostic { Level = level, Code = code, Message = message, OperationName = opName });
        }
    }
}
