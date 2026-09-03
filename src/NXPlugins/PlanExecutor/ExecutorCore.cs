// ExecutorCore.cs — PlanDocument → RebuildPlan 的核心映射（纯逻辑，无 NX 依赖）
// 性质落点（docs/nx-plan-executor-spec.md §3）：PRE-1 版本/必填、PRE-2 ref 闭合（fatal）、
// PRE-3 nx_template 支持集、POST-1 四父锚点、POST-2 拒写 diag、INV-1 op↔ws 1:1、
// INV-2 参数一一对应、INV-3 指令序 = workplan DFS、MONO-1 Ok=false 无指令、INV-4 聚合。
// 语义要点：v1 无几何（U-5/U-5c）→ 不消费 face_anchors/geometry_ref/feature 内容。

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanExecutor
{
    public static class ExecutorCore
    {
        /// <summary>重建支持的操作模板对（与导出白名单同族；Create 已实证对）。</summary>
        public static readonly TemplatePair[] SupportedTemplates =
        {
            new TemplatePair("mill_contour", "CAVITY_MILL"),
            new TemplatePair("hole_making", "DRILLING"),
            new TemplatePair("hole_making", "SPOT_DRILLING"),
            new TemplatePair("hole_making", "DEEP_HOLE_DRILLING"),
            new TemplatePair("hole_making", "TAPPING"),
        };

        /// <summary>模板默认方法组名（直接复用，不新建）。</summary>
        private static readonly string[] DefaultMethodGroups =
        {
            "MILL_METHOD", "MILL_ROUGH", "MILL_SEMI_FINISH", "MILL_FINISH", "DRILL_METHOD",
        };

        /// <summary>根层程序锚点（默认 PROGRAM 组，模板自带不新建）。</summary>
        private const string DefaultProgramFull = "PROGRAM";

        public static RebuildPlan Build(PlanDocument plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            var r = new RebuildPlan();

            // ---- A1 结构校验（PRE-1），fatal ----
            if (plan.contract_version != "3.0")
            {
                AddDiag(r, RebuildDiagLevel.Error, "VERSION_MISMATCH", "",
                    "contract_version=" + plan.contract_version + " ≠ 3.0");
                r.Ok = false;
                return r;
            }
            if (string.IsNullOrEmpty(plan.plan_id) || plan.setups == null || plan.resources == null
                || plan.resources.tools == null || plan.operations == null || plan.workingsteps == null
                || plan.workplan == null || plan.workplan.root == null)
            {
                AddDiag(r, RebuildDiagLevel.Error, "MISSING_FIELD", "",
                    "plan 必填域缺失（plan_id/setups/resources.tools/operations/workingsteps/workplan）");
                r.Ok = false;
                return r;
            }

            // ---- 索引（REF 目标集合）----
            var toolIds = new HashSet<string>();
            foreach (ToolJson t in plan.resources.tools)
            {
                if (string.IsNullOrEmpty(t.tool_id) || !toolIds.Add(t.tool_id))
                {
                    AddDiag(r, RebuildDiagLevel.Error, "DUP_TOOL_ID", t.tool_id, "刀具 tool_id 缺失/重复");
                    r.Ok = false;
                }
            }
            var setupIds = new HashSet<string>();
            foreach (SetupJson s in plan.setups) setupIds.Add(s.setup_id);
            var opById = new Dictionary<string, OperationJson>();
            foreach (OperationJson o in plan.operations)
                if (!opById.ContainsKey(o.operation_id)) opById[o.operation_id] = o;

            // ---- A2 ref 闭合（PRE-2）+ INV-1 计数 ----
            var wsById = new Dictionary<string, WorkingstepJson>();
            var wsByOp = new Dictionary<string, WorkingstepJson>();   // 首个 ws（用于 1:1 判定）
            var wsRefCount = new Dictionary<string, int>();           // op 被引次数
            foreach (WorkingstepJson w in plan.workingsteps)
            {
                wsById[w.workingstep_id] = w;
                if (string.IsNullOrEmpty(w.operation_ref) || !opById.ContainsKey(w.operation_ref))
                {
                    AddDiag(r, RebuildDiagLevel.Error, "REF_DANGLING", w.workingstep_id,
                        "ws.operation_ref 悬空: " + w.operation_ref);
                    r.Ok = false;
                    continue;
                }
                if (!wsRefCount.ContainsKey(w.operation_ref)) wsRefCount[w.operation_ref] = 0;
                wsRefCount[w.operation_ref]++;
                if (!wsByOp.ContainsKey(w.operation_ref)) wsByOp[w.operation_ref] = w;

                if (string.IsNullOrEmpty(w.setup_ref) || !setupIds.Contains(w.setup_ref))
                {
                    AddDiag(r, RebuildDiagLevel.Error, "REF_DANGLING", w.workingstep_id,
                        "ws.setup_ref 悬空: " + w.setup_ref);
                    r.Ok = false;
                }
            }
            foreach (OperationJson o in plan.operations)
                if (string.IsNullOrEmpty(o.tool_ref) || !toolIds.Contains(o.tool_ref))
                {
                    AddDiag(r, RebuildDiagLevel.Error, "REF_DANGLING", o.operation_id,
                        "op.tool_ref 悬空: " + o.tool_ref);
                    r.Ok = false;
                }
            if (!r.Ok) return r;   // MONO-1：fatal 无任何指令

            // ---- workplan DFS：程序指令 + ws 叶序 + ws→程序锚点 ----
            var programsByFull = new Dictionary<string, ProgramCommand>();
            var wsOrder = new List<string>();            // DFS 前序叶（INV-3）
            var wsToProgramFull = new Dictionary<string, string>();
            var wsToName = new Dictionary<string, string>();
            CollectWorkplan(plan.workplan.root, "", programsByFull, wsOrder, wsToProgramFull, wsToName, r);
            foreach (WorkingstepJson w in plan.workingsteps)
                if (!wsToProgramFull.ContainsKey(w.workingstep_id))
                {
                    AddDiag(r, RebuildDiagLevel.Error, "WS_NOT_IN_WORKPLAN", w.workingstep_id,
                        "workingstep 未出现在 workplan 树中（结构不一致）");
                    r.Ok = false;
                }
            if (!r.Ok) return r;

            foreach (ProgramCommand p in programsByFull.Values) r.Programs.Add(p);

            // ---- 指令组装（Tools / Setups 全量；Ops 按 DFS 叶序）----
            foreach (ToolJson t in plan.resources.tools)
            {
                ToolFamilyMap.Resolution res = ToolFamilyMap.Resolve(t.type);
                r.Tools.Add(new ToolCommand(t.tool_id, res.Pair, res.Inferred)
                {
                    Diameter = t.diameter,
                    NumFlutes = t.num_flutes,
                    FluteLength = t.flute_length,
                    LowerCornerRadius = t.lower_corner_radius,
                });
                if (res.Inferred)
                    AddDiag(r, RebuildDiagLevel.Warning, "TOOL_TYPE_INFERRED", t.tool_id,
                        "刀具家族未命中关键词（type=" + t.type + "），默认铣 (mill_planar,MILL)");
            }
            foreach (SetupJson s in plan.setups)
            {
                string mcsName = string.IsNullOrEmpty(s.name) ? "MCS_" + s.setup_id : s.name;
                var gc = new GeometryChainCommand(s.setup_id, mcsName, "WORKPIECE");
                if (s.mcs != null)
                {
                    gc.McsOrigin = s.mcs.origin;
                    gc.McsZAxis = s.mcs.z_axis;
                    gc.McsXAxis = s.mcs.x_axis;
                }
                gc.FixtureOffset = s.fixture_offset;
                r.Setups.Add(gc);
            }

            // INV-1：孤儿/双 ws 判定（不入指令，各自 diag）
            foreach (OperationJson o in plan.operations)
            {
                int n = wsRefCount.ContainsKey(o.operation_id) ? wsRefCount[o.operation_id] : 0;
                if (n == 0)
                    AddDiag(r, RebuildDiagLevel.Error, "OP_NO_WS", o.operation_id,
                        "op 无任何 workingstep 引用（几何/装夹锚点缺失）");
                else if (n > 1)
                    AddDiag(r, RebuildDiagLevel.Error, "OP_MULTI_WS", o.operation_id,
                        "op 被 " + n + " 个 workingstep 引用（1:1 违反）");
            }

            foreach (string wsId in wsOrder)   // INV-3：DFS 叶序
            {
                WorkingstepJson w = wsById[wsId];
                OperationJson o = opById[w.operation_ref];
                if (wsRefCount[o.operation_id] != 1) continue;   // INV-1 违规 op 跳过（diag 已记）

                // PRE-3：模板对支持集
                if (!IsSupportedTemplate(o.nx_template.type, o.nx_template.subtype))
                {
                    AddDiag(r, RebuildDiagLevel.Error, "TPL_UNSUPPORTED", o.operation_id,
                        "模板对不在支持集: (" + o.nx_template.type + ", " + o.nx_template.subtype + ")");
                    continue;
                }

                // POST-1 锚点
                string progFull = wsToProgramFull[wsId];
                if (progFull.Length == 0) progFull = DefaultProgramFull;
                string methodRef = NormalizeMethodRoot(o.method_ref);
                bool methodNeedsCreate = methodRef.Length > 0 && !IsDefaultMethodGroup(methodRef);
                if (methodNeedsCreate)
                    AddDiag(r, RebuildDiagLevel.Warning, "METHOD_CREATED_APPROX", o.operation_id,
                        "方法组名非模板默认（" + o.method_ref + "）→ 将新建组（MILL_METHOD 族近似）");

                string displayName = wsToName.ContainsKey(wsId) && wsToName[wsId].Length > 0
                    ? wsToName[wsId] : o.operation_id;
                var opCmd = new OpCommand(o.operation_id, displayName,
                    new TemplatePair(o.nx_template.type, o.nx_template.subtype),
                    progFull, methodRef, methodNeedsCreate, o.tool_ref, w.setup_ref);

                // POST-2/INV-2：白名单过滤
                AppendParams(opCmd, o.strategy, false, r);
                AppendParams(opCmd, o.technology, true, r);

                r.Operations.Add(opCmd);
            }
            return r;
        }

        // ---------- 内部工具 ----------

        private static bool IsSupportedTemplate(string type, string subtype)
        {
            foreach (TemplatePair p in SupportedTemplates)
                if (p.Type == type && p.Subtype == subtype) return true;
            return false;
        }

        private static bool IsDefaultMethodGroup(string name)
        {
            foreach (string m in DefaultMethodGroups)
                if (m == name) return true;
            return false;
        }

        /// <summary>method_ref 归一：空/根名（"METHOD"）→ ""（方法根锚点，不建组）。</summary>
        private static string NormalizeMethodRoot(string methodRef)
        {
            if (string.IsNullOrEmpty(methodRef) || methodRef == "METHOD") return "";
            return methodRef;
        }

        private static void AppendParams(OpCommand opCmd, Dictionary<string, double> kv, bool inTechnology, RebuildPlan r)
        {
            if (kv == null) return;
            foreach (KeyValuePair<string, double> e in kv)
            {
                string path;
                if (ParamWhiteList.TryGetPath(e.Key, inTechnology, out path))
                    opCmd.Params.Add(new ParamInstruction(path, e.Value));
                else
                    AddDiag(r, RebuildDiagLevel.Warning, "PARAM_UNSUPPORTED", opCmd.OpId,
                        "参数字段不在写入面白名单（v1 不写）: " + e.Key
                        + (e.Key == "stepover" ? "（U-6：Stepover 整链写无效）" : ""));
            }
        }

        /// <summary>DFS：程序指令（去重 by Full）、ws 叶前序、ws → 最近程序路径（无 → ""）与叶名。</summary>
        private static void CollectWorkplan(WorkplanNodeJson node, string parentFull,
            Dictionary<string, ProgramCommand> programsByFull,
            List<string> wsOrder, Dictionary<string, string> wsToProgramFull,
            Dictionary<string, string> wsToName, RebuildPlan r)
        {
            foreach (WorkplanNodeJson child in node.children)
            {
                if (child.kind == "workingstep")
                {
                    if (!string.IsNullOrEmpty(child.@ref) && !wsToProgramFull.ContainsKey(child.@ref))
                    {
                        wsOrder.Add(child.@ref);
                        wsToProgramFull[child.@ref] = parentFull;
                        wsToName[child.@ref] = child.name;
                    }
                    continue;
                }
                string full = parentFull.Length == 0 ? child.name : parentFull + "/" + child.name;
                if (programsByFull.ContainsKey(full))
                    AddDiag(r, RebuildDiagLevel.Warning, "DUP_PROGRAM", full,
                        "程序组路径重复（合并）: " + full);
                else
                    programsByFull[full] = new ProgramCommand(child.name, parentFull);
                CollectWorkplan(child, full, programsByFull, wsOrder, wsToProgramFull, wsToName, r);
            }
        }

        private static void AddDiag(RebuildPlan r, RebuildDiagLevel level, string code, string scope, string message)
        {
            // INV-4：同 code+scope 聚合一次（消息取首条）
            foreach (RebuildDiag d in r.Diagnostics)
                if (d.Code == code && d.Scope == scope) return;
            r.Diagnostics.Add(new RebuildDiag { Level = level, Code = code, Scope = scope, Message = message });
        }
    }
}
