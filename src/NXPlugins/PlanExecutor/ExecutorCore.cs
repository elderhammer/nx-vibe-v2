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
            CollectWorkplan(plan.workplan.root, "", true, programsByFull, wsOrder, wsToProgramFull, wsToName, r);
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
                // U-7：键源 = (type, subtype) NX 词注册对表 → 家族关键词回退 → 默认铣（INV-U7-3 链）
                ToolFamilyMap.Resolution res = ToolFamilyMap.Resolve(t.type, t.subtype);
                r.Tools.Add(new ToolCommand(t.tool_id, res.Pair, res.Inferred)
                {
                    Diameter = t.diameter,
                    NumFlutes = t.num_flutes,
                    FluteLength = t.flute_length,
                    LowerCornerRadius = t.lower_corner_radius,
                });
                if (res.Inferred)
                    AddDiag(r, RebuildDiagLevel.Warning, "TOOL_TYPE_INFERRED", t.tool_id,
                        NxToolWords.IsTypeWord(t.type)
                            ? "NX 注册对表未覆盖 (" + t.type + ", " + (t.subtype ?? "") + ")，按默认铣 (mill_planar,MILL) 重建（v1 表，见 U-7 spec §5b）"
                            : "刀具家族未命中关键词（type=" + t.type + "），默认铣 (mill_planar,MILL)");
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

            // 逐 ws 构建 op 指令（wsOrder = DFS 叶序，INV-3）
            var opByWsCmd = new Dictionary<string, OpCommand>();
            foreach (string wsId in wsOrder)
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

                // v2：cut_area_signatures 解析（V2-PRE-1/PRE-2/PRE-3）
                AppendSignatures(opCmd, o, r);

                r.Operations.Add(opCmd);
                opByWsCmd[wsId] = opCmd;
            }

            // v1.5-① 保序：Steps = 树 DFS 交错（组 ↔ 工序），executor 依此创建（NX 成员序=创建序）
            BuildSteps(plan.workplan.root, "", true, programsByFull, opByWsCmd, r);
            return r;
        }

        // ---------- v2 签名解析与匹配（nx-v2-geom-spec V2-PRE-*/POST-2） ----------

        /// <summary>V2-PRE-1/PRE-2：签名列表解析——形状/值域违规 → error 该 op 不指派（不静默）；
        /// 空字段 = 无签名（V2-PRE-3：v1 旧形状兼容，腔铣 op 记 GEOM_SIG_ABSENT warning）。
        /// V2-PRE-2 轴词集 = 自由串六值（采集侧恒产），schema 不断言枚举、此处运行时校验。</summary>
        private static void AppendSignatures(OpCommand opCmd, OperationJson o, RebuildPlan r)
        {
            List<FaceSignatureJson> list = o.cut_area_signatures;
            if (list == null || list.Count == 0)
            {
                if (o.nx_template.type == "mill_contour" && o.nx_template.subtype == "CAVITY_MILL")
                    AddDiag(r, RebuildDiagLevel.Warning, "GEOM_SIG_ABSENT", o.operation_id,
                        "腔铣 op 无 cut_area_signatures（v1 旧形状）→ 不指派面/不生成刀路，行为同 v1");
                return;
            }
            foreach (FaceSignatureJson s in list)
            {
                bool bad = s == null || s.face_type < 0 || s.radius < 0
                    || !IsAxisWord(s.normal_axis)
                    || double.IsNaN(s.rx) || double.IsNaN(s.ry) || double.IsNaN(s.rz)
                    || double.IsInfinity(s.rx) || double.IsInfinity(s.ry) || double.IsInfinity(s.rz);
                if (bad)
                {
                    AddDiag(r, RebuildDiagLevel.Error, "GEOM_SIG_INVALID", o.operation_id,
                        "cut_area_signatures 元素形状/值域违规（face_type/normal_axis/radius/rx..rz）→ 该 op 不指派面");
                    return;   // 整 op 拒绝（值域纪律，不部分指派）
                }
            }
            foreach (FaceSignatureJson s in list)
                opCmd.Signatures.Add(new FaceSignature
                {
                    FaceType = s.face_type,
                    NormalAxis = s.normal_axis,
                    Rx = s.rx, Ry = s.ry, Rz = s.rz,
                    Radius = s.radius,
                });
        }

        private static bool IsAxisWord(string w)
        {
            return w == "X+" || w == "X-" || w == "Y+" || w == "Y-" || w == "Z+" || w == "Z-";
        }

        /// <summary>V2-POST-2 匹配器（纯逻辑）：plan 签名 → body 面签名 1:1 唯一匹配（F1 实证零歧义）。
        /// 键 = FaceSignature.Key()（取整粒度同 Key() 语义，V2-INV-2 无二次取整）。</summary>
        public static FaceMatchResult MatchSignatures(List<FaceSignature> planFaces, List<FaceSignature> bodyFaces)
        {
            var bodyByKey = new Dictionary<string, List<int>>();
            for (int i = 0; i < bodyFaces.Count; i++)
            {
                string k = bodyFaces[i].Key();
                List<int> idx;
                if (!bodyByKey.TryGetValue(k, out idx)) { idx = new List<int>(); bodyByKey[k] = idx; }
                idx.Add(i);
            }
            int[] result = new int[planFaces.Count];
            int missing = 0, ambiguous = 0;
            for (int i = 0; i < planFaces.Count; i++)
            {
                List<int> cand;
                if (!bodyByKey.TryGetValue(planFaces[i].Key(), out cand) || cand.Count == 0)
                {
                    result[i] = -1;
                    missing++;
                }
                else
                {
                    result[i] = cand[0];        // 取首面（歧义时适配器对多余候选记 diag）
                    if (cand.Count > 1) ambiguous++;
                }
            }
            return new FaceMatchResult(result, missing, ambiguous);
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

        /// <summary>v1.5-③（V15-POST-2）：union 值按写面目标 kind 分派——Kind=Number 且值含 N → 指令；
        /// Kind=Enum 且值含 S → 词集校验（NxParamWords，∉ → PARAM_ENUM_UNKNOWN error 该键不入指令）；
        /// 值 kind 与写面 kind 不符（V15-PRE-1）→ error；表外键 → PARAM_UNSUPPORTED warning（含注册表指针）。</summary>
        private static void AppendParams(OpCommand opCmd, Dictionary<string, ParamValue> kv, bool inTechnology, RebuildPlan r)
        {
            if (kv == null) return;
            foreach (KeyValuePair<string, ParamValue> e in kv)
            {
                ParamTarget target;
                ParamValue v = e.Value;
                bool hasN = v != null && v.N.HasValue;
                bool hasS = v != null && v.S != null;
                if (ParamWhiteList.TryGetTarget(e.Key, inTechnology, out target))
                {
                    if (target.Kind == ParamKind.Number && hasN)
                    {
                        opCmd.Params.Add(new ParamInstruction(target.MemberPath, target.Kind, v.N, null));
                    }
                    else if (target.Kind == ParamKind.Enum && hasS)
                    {
                        if (!NxParamWords.IsWord(e.Key, v.S))
                        {
                            AddDiag(r, RebuildDiagLevel.Error, "PARAM_ENUM_UNKNOWN", opCmd.OpId,
                                "枚举词不在词集（schema/NxParamWords 镜像）: " + e.Key + "=" + v.S);
                        }
                        else
                        {
                            opCmd.Params.Add(new ParamInstruction(target.MemberPath, target.Kind, null, v.S));
                        }
                    }
                    else
                    {
                        AddDiag(r, RebuildDiagLevel.Error, "PARAM_KIND_MISMATCH", opCmd.OpId,
                            "值形态与写面 kind 不符（注册表按键定 kind）: " + e.Key
                            + " 期望 " + target.Kind + " 实为 " + (hasN ? "N" : hasS ? "S" : "(空)"));
                    }
                }
                else
                {
                    AddDiag(r, RebuildDiagLevel.Warning, "PARAM_UNSUPPORTED", opCmd.OpId,
                        "参数字段不在写入面白名单（重建不写，读面可导出）: " + e.Key
                        + (e.Key == "stepover" ? "（U-6：Stepover 整链写无效）"
                           : e.Key.StartsWith("boundary_") ? "（Boundary 容差族负结案，注册表 #7/#8）"
                           : "（见 docs/nx-param-registry-spec.md）"));
                }
            }
        }

        /// <summary>DFS：程序指令（去重 by Full）、ws 叶前序、ws → 最近程序路径（无 → ""）与叶名。
        /// 根语义（v1.5-①，2026-09-04）：plan root = NC_PROGRAM 镜像；root 的直接程序子节点 = 顶层组，
        /// 建在 NX 程序根（ProgramCommand.ParentFull=""）；其中与模板默认同名（"PROGRAM"）→ 复用默认组
        /// 不产生指令（子孙父链以 "PROGRAM/…" 表达）；root 直挂 ws → ""（fallback 默认组，同 v1）。</summary>
        private static void CollectWorkplan(WorkplanNodeJson node, string parentFull, bool isRoot,
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
                if (isRoot && child.name == "PROGRAM")
                {
                    // 顶层组与模板默认同名 → 复用默认组（不建指令），其子孙容器 = "PROGRAM"
                    CollectWorkplan(child, "PROGRAM", false, programsByFull, wsOrder,
                        wsToProgramFull, wsToName, r);
                    continue;
                }
                string full = parentFull.Length == 0 ? child.name : parentFull + "/" + child.name;
                if (programsByFull.ContainsKey(full))
                    AddDiag(r, RebuildDiagLevel.Warning, "DUP_PROGRAM", full,
                        "程序组路径重复（合并）: " + full);
                else
                    programsByFull[full] = new ProgramCommand(child.name, parentFull);
                CollectWorkplan(child, full, false, programsByFull, wsOrder, wsToProgramFull, wsToName, r);
            }
        }

        /// <summary>v1.5-① 保序：按 plan workplan 树 DFS 生成交错执行序（组↔工序）。
        /// 顶层与模板默认同名 PROGRAM → 复用容器不产生组步（子孙 op 挂默认组，无组步前导）；
        /// 树外 ws（顶层直挂）→ op 步照发（放默认组，同 v1 fallback）。</summary>
        private static void BuildSteps(WorkplanNodeJson node, string container, bool isRoot,
            Dictionary<string, ProgramCommand> programsByFull,
            Dictionary<string, OpCommand> opByWsCmd, RebuildPlan r)
        {
            foreach (WorkplanNodeJson child in node.children)
            {
                if (child.kind == "workingstep")
                {
                    OpCommand opCmd;
                    if (string.IsNullOrEmpty(child.@ref)) continue;
                    if (opByWsCmd.TryGetValue(child.@ref, out opCmd)) r.Steps.Add(RebuildStep.ForOperation(opCmd));
                    continue;
                }
                if (isRoot && child.name == "PROGRAM")
                {
                    BuildSteps(child, "PROGRAM", false, programsByFull, opByWsCmd, r);   // 复用默认组
                    continue;
                }
                string full = container.Length == 0 ? child.name : container + "/" + child.name;
                ProgramCommand pc;
                if (programsByFull.TryGetValue(full, out pc)) r.Steps.Add(RebuildStep.ForProgram(pc));
                BuildSteps(child, full, false, programsByFull, opByWsCmd, r);
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
