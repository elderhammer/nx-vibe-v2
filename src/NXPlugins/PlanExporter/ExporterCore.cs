// ExporterCore.cs — 快照 → PlanDocument 的核心映射（纯逻辑，无 NX 依赖）
// 性质落点：INV-3（1 op ↔ 1 ws）、INV-4（四父链缺失 warning）、INV-5（Tag 去重守卫）、
// INV-6（diagnostics 聚合：同 op 同 code 一次）、POST-3（ReadbackErrors → diag，不静默）、
// POST-6（歧义 → 默认对 + warning）。
// workplan 树（v1.5-①，2026-09-04）：root = NC_PROGRAM 镜像（虚拟，名 "PROGRAM" 落盘展示）；
// 树形 = 快照 ProgramTree 递归渲染（嵌套组真实展开，组内成员序 = NX GetMembers 序保刀路输出序）；
// op 经 TagKey 精确定位 ws 叶；树中缺失 op（顶层直挂/父组不在树）→ 兜底挂 root 尾。

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

            // tools（INV-2 的 tool_ref 目标；U-7 INV-U7-4：类型读回失败 → 不入 plan + error diag）
            // type/subtype = NX Tool.Types/Subtypes 原文直写（INV-U7-1，无归类表）；编号 = 入选连续序
            var toolSeqByName = new Dictionary<string, string>();   // 刀组名 → 输出 tool_id（op tool_ref 解析源）
            for (int i = 0; i < snap.Tools.Count; i++)
            {
                ToolItem t = snap.Tools[i];
                if (t.TypeReadbackError.Length > 0)
                {
                    AddDiag(diags, DiagLevel.Error, "TOOL_TYPE_UNREADABLE",
                        "刀具类型读回失败（不入 plan）: " + t.Name + " — " + t.TypeReadbackError, t.Name);
                    continue;
                }
                string tid = "T-" + (doc.resources.tools.Count + 1).ToString("D3");
                doc.resources.tools.Add(new ToolJson
                {
                    tool_id = tid,
                    type = t.NxType,
                    subtype = t.NxSubtype.Length > 0 ? t.NxSubtype : null,   // 空 → null（不填）
                    diameter = t.Diameter,
                    num_flutes = t.NumFlutes,
                    flute_length = t.FluteLength,
                    lower_corner_radius = t.LowerCornerRadius,
                });
                toolSeqByName[t.Name] = tid;
            }

            // operations / workingsteps / features（1:1）；workplan 节点树在 op 循环后渲染
            // （v1.5-①：root = NC_PROGRAM 镜像（虚拟），树形 = ProgramTree 真实嵌套展开）
            var wsByOpKey = new Dictionary<TagKey, string>();    // op.Key → wsId（生产，key 精确定位）
            var wsByOpName = new Dictionary<string, string>();   // op.Name → wsId（夹具退化，首名）
            var root = doc.workplan.root;
            root.name = "PROGRAM";

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
                // U-7：tool_ref 解析源 = 入选刀具集（被剔除刀的引用 op → error diag，不静默）
                string toolId;
                if (!toolSeqByName.TryGetValue(o.ToolParent, out toolId))
                {
                    AddDiag(diags, DiagLevel.Error, "TOOL_REF_DANGLING",
                        "工序引用的刀具不在入选集（类型读回失败/组名不匹配）: " + o.ToolParent + " / " + o.Name, o.Name);
                    toolId = "";
                }
                var oj = new OperationJson
                {
                    operation_id = opId,
                    operation_type = FamilyToOperationType(o.TypeFamily),
                    tool_ref = toolId,
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
                wsByOpKey[o.Key] = wsId;
                if (!wsByOpName.ContainsKey(o.Name)) wsByOpName[o.Name] = wsId;   // 首名（夹具退化）
            }

            // workplan 渲染（v1.5-①）：ProgramTree 递归建组（组内成员序 = NX GetMembers 序），
            // op 成员 → ws 叶（key 精确定位，同名/跨组不串挂）；树中缺失 op → 兜底挂 root 尾。
            var renderedKeys = new HashSet<TagKey>();
            var renderedNames = new HashSet<string>();   // 夹具退化（无 OpKey）渲染登记
            foreach (ProgramNode top in snap.ProgramTree)
                RenderGroup(top, root, wsByOpKey, wsByOpName, renderedKeys, renderedNames);
            foreach (OperationItem o in snap.Operations)
            {
                // 被剔除 op（Key null）或已在树内（key/退化名命中）→ 跳过
                if (o.Key == null || renderedKeys.Contains(o.Key) || renderedNames.Contains(o.Name)) continue;
                string wsId = wsByOpKey[o.Key];
                renderedKeys.Add(o.Key);
                root.children.Add(new WorkplanNodeJson { kind = "workingstep", name = o.Name, @ref = wsId });
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

        /// <summary>v1.5-①：递归渲染 ProgramTree 组节点（成员序 = NX GetMembers 序，组/ws 交错保真）。
        /// op 成员渲染后登记（生产 key / 夹具退化名），防兜底循环重复挂载。</summary>
        private static void RenderGroup(ProgramNode node, WorkplanNodeJson parentJson,
            Dictionary<TagKey, string> wsByOpKey, Dictionary<string, string> wsByOpName,
            HashSet<TagKey> renderedKeys, HashSet<string> renderedNames)
        {
            var pn = new WorkplanNodeJson { kind = "program", name = node.Name };
            foreach (ProgramMember m in node.Members)
            {
                if (m.IsOperation)
                {
                    string wsId;
                    if (m.OpKey != null)
                    {
                        if (!wsByOpKey.TryGetValue(m.OpKey, out wsId)) continue;
                        renderedKeys.Add(m.OpKey);
                    }
                    else
                    {
                        if (!wsByOpName.TryGetValue(m.OpName, out wsId)) continue;   // 夹具退化（首名）
                        renderedNames.Add(m.OpName);
                    }
                    pn.children.Add(new WorkplanNodeJson { kind = "workingstep", name = m.OpName, @ref = wsId });
                }
                else if (m.Group != null)
                    RenderGroup(m.Group, pn, wsByOpKey, wsByOpName, renderedKeys, renderedNames);
            }
            parentJson.children.Add(pn);
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

        private static void AddDiag(List<Diagnostic> diags, DiagLevel level, string code, string message, string opName)
        {
            // INV-6 聚合：同 op + 同 code + 同消息 的重复源只保留一次；不同消息不吞并
            foreach (Diagnostic d in diags)
                if (d.Code == code && d.OperationName == opName && d.Message == message) return;
            diags.Add(new Diagnostic { Level = level, Code = code, Message = message, OperationName = opName });
        }
    }
}
