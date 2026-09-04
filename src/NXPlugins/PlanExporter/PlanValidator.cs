// PlanValidator.cs — schema v3 子集校验器（INV-1 / INV-2 / INV-3 / INV-6 / POST-1 的离线判据）
// 范围声明：本项目合同子集校验（字段必填/引用闭合/1:1 挂载/nx_template 成对/diagnostics 形状），
// 非完整 JSON Schema 引擎；约束镜像 autocam-plan.schema.json 的导出 MVP 域。

using System;
using System.Collections.Generic;

namespace NXPlugins.PlanExporter
{
    public static class PlanValidator
    {
        /// <summary>返回校验错误列表；空 = 合法。规则以代码注释挂接性质编号。</summary>
        public static List<string> Validate(PlanDocument doc)
        {
            var errors = new List<string>();
            if (doc == null) { errors.Add("doc is null"); return errors; }

            // INV-1 / POST-1：必填域与形状（schema required 子集）
            if (string.IsNullOrEmpty(doc.contract_version)) errors.Add("contract_version 缺失");
            if (doc.contract_version != "3.0") errors.Add("contract_version 必须为 3.0，实际 " + doc.contract_version);
            if (string.IsNullOrEmpty(doc.plan_id)) errors.Add("plan_id 缺失");
            if (string.IsNullOrEmpty(doc.name)) errors.Add("name 缺失");
            if (string.IsNullOrEmpty(doc.input_ref)) errors.Add("input_ref 缺失");
            if (doc.meta == null) errors.Add("meta 缺失");
            if (doc.setups == null || doc.resources == null || doc.features == null
                || doc.operations == null || doc.workingsteps == null
                || doc.workplan == null || doc.workplan.root == null || doc.diagnostics == null)
                errors.Add("顶层数组/对象缺失（setups/resources/features/operations/workingsteps/workplan.root/diagnostics）");

            if (errors.Count > 0) return errors;   // 结构缺失时不再下钻

            // nx_template 成对（实证语义：type=模板部件名/subtype=对象类型；导出恒填对，见 POST-6）
            foreach (OperationJson op in doc.operations)
            {
                if (string.IsNullOrEmpty(op.operation_id)) errors.Add("operation_id 缺失");
                if (op.nx_template == null || string.IsNullOrEmpty(op.nx_template.type)
                    || string.IsNullOrEmpty(op.nx_template.subtype))
                    errors.Add("operation " + op.operation_id + " nx_template 未成对 (type/subtype 均需非空)");
                if (string.IsNullOrEmpty(op.tool_ref)) errors.Add("operation " + op.operation_id + " tool_ref 缺失");
                if (string.IsNullOrEmpty(op.operation_type)) errors.Add("operation " + op.operation_id + " operation_type 缺失");
            }

            // INV-2：ref 闭合
            CheckRefClosure(doc, errors);

            // INV-3：1 operation ↔ ≤1 workingstep；workingstep 引用回指存在
            var opCount = new Dictionary<string, int>();
            foreach (OperationJson op in doc.operations)
                opCount[op.operation_id] = opCount.ContainsKey(op.operation_id) ? opCount[op.operation_id] + 1 : 1;
            var wsOpRefs = new Dictionary<string, int>();
            foreach (WorkingstepJson ws in doc.workingsteps)
            {
                if (string.IsNullOrEmpty(ws.workingstep_id)) errors.Add("workingstep_id 缺失");
                if (string.IsNullOrEmpty(ws.operation_ref)) errors.Add("ws " + ws.workingstep_id + " operation_ref 缺失");
                wsOpRefs[ws.operation_ref] = wsOpRefs.ContainsKey(ws.operation_ref) ? wsOpRefs[ws.operation_ref] + 1 : 1;
            }
            foreach (KeyValuePair<string, int> kv in wsOpRefs)
            {
                if (!opCount.ContainsKey(kv.Key)) errors.Add("ws 引用不存在的 operation: " + kv.Key);
                if (kv.Value > 1) errors.Add("operation 被多个 ws 挂载（违反 1:1）: " + kv.Key);
            }

            // INV-6：diagnostics 形状（level 合法；error 级需 code+message）
            foreach (DiagnosticJson d in doc.diagnostics)
            {
                if (d.level != "info" && d.level != "warning" && d.level != "error")
                    errors.Add("diagnostic level 非法: " + d.level);
                if (d.level == "error" && (string.IsNullOrEmpty(d.code) || string.IsNullOrEmpty(d.message)))
                    errors.Add("error 级 diagnostic 缺 code/message");
                if (string.IsNullOrEmpty(d.code)) errors.Add("diagnostic code 缺失");
            }
            return errors;
        }

        private static void CheckRefClosure(PlanDocument doc, List<string> errors)
        {
            var toolIds = new Dictionary<string, bool>();
            if (doc.resources.tools != null)
                foreach (ToolJson t in doc.resources.tools)
                {
                    if (string.IsNullOrEmpty(t.tool_id)) errors.Add("tool_id 缺失");
                    else if (toolIds.ContainsKey(t.tool_id)) errors.Add("tool_id 重复: " + t.tool_id);
                    else toolIds[t.tool_id] = true;
                    // U-7 收紧（cleanup spec §6：A′ 批收尾开启）：type/subtype 须命中 NX 词集（schema enum 镜像）
                    if (!NxToolWords.IsTypeWord(t.type))
                        errors.Add("tool " + (string.IsNullOrEmpty(t.tool_id) ? "?" : t.tool_id)
                            + " type 不在 NX Tool.Types 词集: " + t.type);
                    if (t.subtype != null && !NxToolWords.IsSubtypeWord(t.subtype))
                        errors.Add("tool " + (string.IsNullOrEmpty(t.tool_id) ? "?" : t.tool_id)
                            + " subtype 不在 NX Tool.Subtypes 词集: " + t.subtype);
                }
            var setupIds = new Dictionary<string, bool>();
            foreach (SetupJson s in doc.setups)
            {
                if (string.IsNullOrEmpty(s.setup_id)) errors.Add("setup_id 缺失");
                else if (setupIds.ContainsKey(s.setup_id)) errors.Add("setup_id 重复: " + s.setup_id);
                else setupIds[s.setup_id] = true;
            }
            var featIds = new Dictionary<string, bool>();
            foreach (FeatureJson f in doc.features)
            {
                if (string.IsNullOrEmpty(f.feature_id)) errors.Add("feature_id 缺失");
                else if (featIds.ContainsKey(f.feature_id)) errors.Add("feature_id 重复: " + f.feature_id);
                else featIds[f.feature_id] = true;
            }
            foreach (WorkingstepJson ws in doc.workingsteps)
            {
                if (!string.IsNullOrEmpty(ws.setup_ref) && !setupIds.ContainsKey(ws.setup_ref))
                    errors.Add("ws " + ws.workingstep_id + " setup_ref 不闭合: " + ws.setup_ref);
                if (!string.IsNullOrEmpty(ws.feature_ref) && !featIds.ContainsKey(ws.feature_ref))
                    errors.Add("ws " + ws.workingstep_id + " feature_ref 不闭合: " + ws.feature_ref);
            }
            foreach (OperationJson op in doc.operations)
            {
                if (!toolIds.ContainsKey(op.tool_ref))
                    errors.Add("operation " + op.operation_id + " tool_ref 不闭合: " + op.tool_ref);
            }
        }
    }
}
