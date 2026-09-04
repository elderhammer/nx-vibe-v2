// ExecutorPropertyTests.cs — PlanExecutor 性质单测（红线；断言注释引性质原文，
// 见 docs/nx-plan-executor-spec.md §3）。覆盖 [U] 层：PRE-1/2/3/4、POST-1/2、
// INV-1/2/3/4、MONO-1（预检部分）。MONO-1 执行期、I-1..I-5 为 [I] 无单测。

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;
using NXPlugins.PlanExecutor;

namespace NXPlugins.PlanExporterTests
{
    public static class ExecutorPropertyTests
    {
        // ---------- 夹具 ----------

        // 构造等价于 ExporterCore 产物的小 plan：1 setup、1 铣刀、1 钻刀、
        // 2 op（腔=A01 下 / 打点=根 ws）、workplan 树。
        private static PlanDocument SamplePlan()
        {
            var plan = new PlanDocument();
            plan.contract_version = "3.0";
            plan.plan_id = "P-1";
            plan.input_ref = "x";

            plan.setups.Add(new SetupJson
            {
                setup_id = "S-01",
                name = "MCS_MILL",
                mcs = new McsJson
                {
                    origin = new double[] { 75, 0, 100 },
                    z_axis = new double[] { 0, 0, 1 },
                    x_axis = new double[] { 1, 0, 0 },
                },
                fixture_offset = 1,
            });

            plan.resources.tools.Add(new ToolJson
            {
                tool_id = "T-001", type = "铣刀-5 参数", diameter = 10, num_flutes = 4,
            });
            plan.resources.tools.Add(new ToolJson
            {
                tool_id = "T-002", type = "钻刀", diameter = 8.5,
            });

            plan.operations.Add(new OperationJson
            {
                operation_id = "OP-001", operation_type = "milling",
                nx_template = new NxTemplateJson { type = "mill_contour", subtype = "CAVITY_MILL" },
                tool_ref = "T-001", method_ref = "MILL_ROUGH",
                strategy = new Dictionary<string, ParamValue> { { "part_stock", 0.3 }, { "floor_stock", 0.1 } },
                technology = new Dictionary<string, ParamValue>(),
            });
            plan.operations.Add(new OperationJson
            {
                operation_id = "OP-002", operation_type = "drilling",
                nx_template = new NxTemplateJson { type = "hole_making", subtype = "DRILLING" },
                tool_ref = "T-002", method_ref = "METHOD",
                strategy = new Dictionary<string, ParamValue> { { "hole_depth", 20 } },
                technology = new Dictionary<string, ParamValue>(),
            });

            plan.workingsteps.Add(new WorkingstepJson
            {
                workingstep_id = "WS-01", feature_ref = "F-01", operation_ref = "OP-001", setup_ref = "S-01",
            });
            plan.workingsteps.Add(new WorkingstepJson
            {
                workingstep_id = "WS-02", feature_ref = "F-02", operation_ref = "OP-002", setup_ref = "S-01",
            });

            var root = plan.workplan.root;
            root.name = "PROGRAM";
            var a01 = new WorkplanNodeJson { kind = "program", name = "A01" };
            a01.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "CAVITY_MILL", @ref = "WS-01" });
            root.children.Add(a01);
            root.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "打点", @ref = "WS-02" });
            return plan;
        }

        private static RebuildPlan BuildOk()
        {
            RebuildPlan r = ExecutorCore.Build(SamplePlan());
            Assert.True(r.Ok, "样例 plan 应 Ok（夹具合法性自身断言）");
            return r;
        }

        private static bool HasDiag(RebuildPlan r, string code, string scope)
        {
            foreach (RebuildDiag d in r.Diagnostics)
                if (d.Code == code && d.Scope == scope) return true;
            return false;
        }

        // ---------- PRE ----------

        // PRE-1：输入 contract_version=3.0 否则拒绝（fatal）。
        public static void test_PRE1_wrong_version_rejected()
        {
            PlanDocument p = SamplePlan();
            p.contract_version = "2.0";
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.False(r.Ok, "版本≠3.0 应 fatal");
            Assert.True(HasDiag(r, "VERSION_MISMATCH", ""), "应含 VERSION_MISMATCH diag");
        }

        // PRE-1 边界：坏 JSON 反序列化即抛（PlanJsonSerializer 纯逻辑层）。
        public static void test_PRE1_deserialize_bad_json_throws()
        {
            bool threw = false;
            try { new PlanJsonSerializer().Deserialize("{not json"); }
            catch (Exception) { threw = true; }
            Assert.True(threw, "坏 JSON 应抛异常（由适配器转结构级中止）");
        }

        // PRE-2：tool_ref 悬空 → fatal（REF_DANGLING，scope=tool id）。
        public static void test_PRE2_dangling_tool_ref_fatal()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].tool_ref = "T-999";
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.False(r.Ok, "tool_ref 悬空应 fatal");
            Assert.True(HasDiag(r, "REF_DANGLING", "OP-001"), "diag scope 应指 OP-001");
        }

        // PRE-2：setup_ref 悬空 → fatal。
        public static void test_PRE2_dangling_setup_ref_fatal()
        {
            PlanDocument p = SamplePlan();
            p.workingsteps[0].setup_ref = "S-999";
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.False(r.Ok, "setup_ref 悬空应 fatal");
        }

        // PRE-2：workingstep.operation_ref 悬空 → fatal。
        public static void test_PRE2_dangling_ws_op_ref_fatal()
        {
            PlanDocument p = SamplePlan();
            p.workingsteps[0].operation_ref = "OP-999";
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.False(r.Ok, "ws.operation_ref 悬空应 fatal");
        }

        // PRE-3：nx_template 不在支持集 → 该 op error diag 且不入指令（不影响其余）。
        public static void test_PRE3_unsupported_template_op_excluded()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].nx_template.subtype = "FLOWCUT";   // 不在支持集
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "单 op 不支持不应整体 fatal");
            Assert.True(HasDiag(r, "TPL_UNSUPPORTED", "OP-001"), "应含 TPL_UNSUPPORTED（scope=OP-001）");
            foreach (OpCommand c in r.Operations)
                Assert.False(c.OpId == "OP-001", "不支持模板的 op 不得入指令");
            Assert.True(r.Operations.Count == 1, "其余 op 正常入指令");
        }

        // PRE-4/V15-PRE-2：白名单非空、排除负结案键、含注册表 4 持久键（v1.5-③ 修订）。
        public static void test_PRE4_whitelist_nonempty_excludes_stepover()
        {
            Assert.True(ParamWhiteList.IsReady, "白名单应就绪");
            Assert.False(ParamWhiteList.StrategyWritable.ContainsKey("stepover"),
                "stepover 不得在白名单（U-6 整链写无效）");
            Assert.False(ParamWhiteList.StrategyWritable.ContainsKey("multi_depth_cut"),
                "multi_depth_cut 不得在白名单（注册表 #5 负结案）");
            Assert.False(ParamWhiteList.StrategyWritable.ContainsKey("boundary_intol"),
                "boundary_intol 不得在白名单（注册表 #7 负结案）");
            Assert.True(ParamWhiteList.StrategyWritable.ContainsKey("part_stock"),
                "part_stock 应可写（E4 实证）");
            // V15-PRE-2：4 持久键入表（E1/E7 锚定三跑；v1.5-③ S1 写面扩展）
            foreach (string k in new[] { "cut_pattern", "cut_order", "cut_direction", "finish_passes" })
            {
                ParamTarget t;
                Assert.True(ParamWhiteList.StrategyWritable.TryGetValue(k, out t),
                    k + " 应可写（注册表 #1-4 持久键）");
                Assert.True(t.Kind == (k == "finish_passes" ? ParamKind.Number : ParamKind.Enum),
                    k + " kind 应与注册表形态一致");
            }
        }

        // ---------- POST ----------

        // POST-1：每 op 四父锚点可解析（program 全名/method/tool/setup 落点存在）。
        public static void test_POST1_every_op_has_four_anchors()
        {
            RebuildPlan r = BuildOk();
            foreach (OpCommand c in r.Operations)
            {
                Assert.False(string.IsNullOrEmpty(c.ProgramFull), "程序锚点缺失: " + c.OpId);
                Assert.NotNull(c.Pair, "模板对缺失: " + c.OpId);
                bool toolFound = false;
                foreach (ToolCommand t in r.Tools)
                    if (t.ToolId == c.ToolId) { toolFound = true; break; }
                Assert.True(toolFound, "tool 锚点缺失: " + c.OpId);
                bool setupFound = false;
                foreach (GeometryChainCommand s in r.Setups)
                    if (s.SetupId == c.SetupId) { setupFound = true; break; }
                Assert.True(setupFound, "setup 锚点缺失: " + c.OpId);
            }
            // 样例期望：2 刀具、1 setup、1 program 子组 A01、2 op
            Assert.True(r.Tools.Count == 2, "刀具指令数应为 2");
            Assert.True(r.Setups.Count == 1, "setup 指令数应为 1");
            Assert.True(r.Programs.Count == 1 && r.Programs[0].Name == "A01", "程序指令应含 A01");
            Assert.True(r.Operations.Count == 2, "op 指令数应为 2");
        }

        // POST-1 边界：method_ref=根名（"METHOD"/空）→ 锚根，不建组。
        public static void test_POST1_root_method_anchors_without_create()
        {
            RebuildPlan r = BuildOk();
            foreach (OpCommand c in r.Operations)
                if (c.OpId == "OP-002")
                {
                    Assert.False(c.MethodNeedsCreate, "根方法锚点不应建组");
                    Assert.True(c.MethodAnchor == "", "METHOD 应归一为空（根锚点）");
                }
        }

        // POST-2：白名单外参数字段 → 结构化 diag（code+scope），不静默；指令不含该键。
        public static void test_POST2_unwritable_param_diag_scoped()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].strategy["stepover"] = 50;      // U-6 拒写
            p.operations[0].strategy["cut_pattern"] = 0;    // 枚举面 v1 不写（数值占位）
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "拒写参数不整体 fatal");
            Assert.True(HasDiag(r, "PARAM_UNSUPPORTED", "OP-001"), "应含 PARAM_UNSUPPORTED（scope=OP-001）");
            foreach (OpCommand c in r.Operations)
                if (c.OpId == "OP-001")
                    foreach (ParamInstruction pi in c.Params)
                    {
                        Assert.False(pi.MemberPath == "CutParameters.Stepover.PercentToolFlatBuilder",
                            "stepover 不得产生指令");
                        Assert.False(pi.MemberPath == "CutPattern", "cut_pattern 不得产生指令");
                    }
        }

        // ---------- INV ----------

        // INV-1：op 无 ws → error 排除；op 被两 ws 引用 → error 排除。
        public static void test_INV1_op_without_ws_excluded()
        {
            PlanDocument p = SamplePlan();
            p.operations.Add(new OperationJson
            {
                operation_id = "OP-003", operation_type = "milling",
                nx_template = new NxTemplateJson { type = "mill_contour", subtype = "CAVITY_MILL" },
                tool_ref = "T-001", method_ref = "MILL_ROUGH",
            });   // 无 workingstep 引用
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "孤儿 op 不应整体 fatal（但自身被排除）");
            Assert.True(HasDiag(r, "OP_NO_WS", "OP-003"), "应含 OP_NO_WS");
            foreach (OpCommand c in r.Operations)
                Assert.False(c.OpId == "OP-003", "孤儿 op 不得入指令");
        }

        public static void test_INV1_op_in_two_ws_excluded()
        {
            PlanDocument p = SamplePlan();
            p.workingsteps.Add(new WorkingstepJson
            {
                workingstep_id = "WS-03", feature_ref = "F-03", operation_ref = "OP-001", setup_ref = "S-01",
            });
            // 双 ws 场景：WS-03 亦需挂 workplan 树（树内 ws 才有装夹/程序锚点语义）
            p.workplan.root.children.Add(new WorkplanNodeJson
            { kind = "workingstep", name = "CAVITY_MILL", @ref = "WS-03" });
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(HasDiag(r, "OP_MULTI_WS", "OP-001"), "双 ws 应报 OP_MULTI_WS");
            foreach (OpCommand c in r.Operations)
                Assert.False(c.OpId == "OP-001", "双 ws op 不得入指令（1:1 保持）");
        }

        // INV-2：plan 内白名单可写键 → 恰一条指令；无凭空参数。
        public static void test_INV2_params_one_to_one_instructions()
        {
            RebuildPlan r = BuildOk();
            OpCommand cav = null, drill = null;
            foreach (OpCommand c in r.Operations)
                if (c.OpId == "OP-001") cav = c;
                else if (c.OpId == "OP-002") drill = c;
            Assert.NotNull(cav, "OP-001 应在指令中");
            Assert.True(cav.Params.Count == 2, "OP-001 应有 part_stock+floor_stock 两条");
            foreach (ParamInstruction pi in cav.Params)
                Assert.True(pi.MemberPath == "CutParameters.PartStock" && pi.N == 0.3
                    || pi.MemberPath == "CutParameters.FloorStock" && pi.N == 0.1,
                    "OP-001 参数指令内容不符: " + pi.MemberPath + "=" + pi.N);
            Assert.NotNull(drill, "OP-002 应在指令中");
            Assert.True(drill.Params.Count == 1 && drill.Params[0].MemberPath == "HoleDepth",
                "OP-002 应有 hole_depth→HoleDepth 一条");
        }

        // INV-3：指令序 = workplan DFS 前序（ws 叶子序）。
        public static void test_INV3_op_order_matches_workplan_dfs()
        {
            RebuildPlan r = BuildOk();
            Assert.True(r.Operations.Count == 2, "应 2 op");
            Assert.True(r.Operations[0].OpId == "OP-001" && r.Operations[1].OpId == "OP-002",
                "指令序应 = workplan DFS（A01 内 WS-01 → 根 WS-02）");
        }

        // MONO-1（预检部分）：结构级 fatal 时无任何指令（不产生半成品重建）。
        public static void test_MONO1_fatal_yields_no_commands()
        {
            PlanDocument p = SamplePlan();
            p.workingsteps[0].setup_ref = "S-999";   // PRE-2 fatal
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.False(r.Ok, "fatal 判据成立");
            Assert.True(r.Operations.Count == 0 && r.Tools.Count == 0
                && r.Setups.Count == 0 && r.Programs.Count == 0, "fatal 时无任何指令");
        }

        // MONO-1 边界：Build 永不抛（一切异常转 fatal diag）。
        public static void test_MONO1_build_never_throws()
        {
            PlanDocument p = SamplePlan();
            p.workplan = new WorkplanJson();   // root 缺省对象仍在（DTP 默认），内容全空
            p.workplan.root.kind = "weird";
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.NotNull(r, "Build 不应抛，返回 RebuildPlan");
            Assert.False(r.Ok || r.Operations.Count == 0 && r.Diagnostics.Count == 0,
                "畸形输入应 Ok=false 或带 diag（不静默）");
        }

        // INV-4：diagnostics 同 code+scope 聚合一次。
        public static void test_INV4_diag_dedupe_same_scope()
        {
            PlanDocument p = SamplePlan();
            // 两个 op 都引用同一悬空 tool → scope 不同各自一条；同 op 重复仅一次
            p.operations[0].tool_ref = "T-999";
            p.operations[1].tool_ref = "T-999";
            RebuildPlan r = ExecutorCore.Build(p);
            int n = 0;
            foreach (RebuildDiag d in r.Diagnostics)
                if (d.Code == "REF_DANGLING") n++;
            Assert.True(n == 2, "两个不同 scope 各一条（n=" + n + "）");
        }

        // ---------- v1.5-①（comparer spec §2 口径破绽根因修复：workplan 根语义对齐） ----------

        private static ProgramCommand FindProgram(RebuildPlan r, string full)
        {
            foreach (ProgramCommand p in r.Programs)
                if (p.Full == full) return p;
            return null;
        }

        private static string OpProgramFull(RebuildPlan r, string opId)
        {
            foreach (OpCommand c in r.Operations)
                if (c.OpId == opId) return c.ProgramFull;
            return "(未找到 " + opId + ")";
        }

        // v1.5-① X1（语义回归锚）：plan root 直接程序子节点 = 顶层组 → ProgramCommand.ParentFull=""
        // = "NX 程序根"（v1.5 语义；v1 实现把 ParentFull="" 落到模板默认 PROGRAM 组下——错位一级的
        // 差异在 adapter 解析的 [I] 层，comparer PROGRAM_ORDER_DIFF 根因；[U] 层钉指令形状不回归）。
        public static void test_v15_toplevel_groups_map_to_nx_root_not_default_program()
        {
            PlanDocument p = SamplePlan();   // root.children = [A01(WS-01), WS-02 直挂根]
            var root = p.workplan.root;
            var top1 = new WorkplanNodeJson { kind = "program", name = "TOP1" };
            top1.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "顶组op", @ref = "WS-02" });
            root.children.Clear();
            root.children.Add(top1);                          // TOP1 顶层（原 WS-02 根挂改为挂 TOP1 下）
            var a01 = new WorkplanNodeJson { kind = "program", name = "A01" };
            a01.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "CAVITY_MILL", @ref = "WS-01" });
            root.children.Add(a01);
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "合法嵌套 plan 应 Ok");
            Assert.NotNull(FindProgram(r, "TOP1"), "TOP1 应为顶层程序指令");
            Assert.NotNull(FindProgram(r, "A01"), "A01 应为顶层程序指令");
            Assert.True(FindProgram(r, "TOP1").ParentFull == "", "TOP1.ParentFull 应为空串（=NX 程序根）");
            Assert.True(OpProgramFull(r, "OP-002") == "TOP1", "OP-002 程序锚点应 = TOP1（顶层组）");
            Assert.True(OpProgramFull(r, "OP-001") == "A01", "OP-001 程序锚点应 = A01");
        }

        // v1.5-① X2：plan 顶层组与模板默认同名（PROGRAM）→ 复用默认组不建指令，子孙组父链以
        // "PROGRAM/…" 表达（与 gt 顶层真实 PROGRAM 组语义合并）。
        public static void test_v15_toplevel_program_group_reuses_default_not_created()
        {
            var p = new PlanDocument();
            p.contract_version = "3.0";
            p.plan_id = "P-2";
            p.input_ref = "x";
            p.setups.Add(new SetupJson { setup_id = "S-01", name = "MCS_MILL" });
            p.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "Mill", subtype = "Mill5" });
            for (int i = 1; i <= 3; i++)
            {
                p.operations.Add(new OperationJson
                {
                    operation_id = "OP-00" + i, operation_type = "milling",
                    nx_template = new NxTemplateJson { type = "mill_contour", subtype = "CAVITY_MILL" },
                    tool_ref = "T-001", method_ref = "MILL_ROUGH",
                });
                p.workingsteps.Add(new WorkingstepJson
                {
                    workingstep_id = "WS-0" + i, feature_ref = "F-0" + i,
                    operation_ref = "OP-00" + i, setup_ref = "S-01",
                });
            }
            var root = p.workplan.root;
            root.name = "PROGRAM";
            var progGroup = new WorkplanNodeJson { kind = "program", name = "PROGRAM" };  // 顶层=默认组名
            var a11 = new WorkplanNodeJson { kind = "program", name = "A1-1" };
            a11.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "op嵌套", @ref = "WS-02" });
            progGroup.children.Add(a11);
            progGroup.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "op直挂PROGRAM", @ref = "WS-03" });
            root.children.Add(progGroup);
            var a01 = new WorkplanNodeJson { kind = "program", name = "A01" };
            a01.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "CAVITY_MILL", @ref = "WS-01" });
            root.children.Add(a01);
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "合法嵌套 plan 应 Ok");
            Assert.Null(FindProgram(r, "PROGRAM"), "顶层 PROGRAM 组应复用默认组，不产生程序指令");
            Assert.NotNull(FindProgram(r, "A01"), "A01 顶层指令应存在");
            Assert.NotNull(FindProgram(r, "PROGRAM/A1-1"), "A1-1 应挂在 PROGRAM/ 容器下");
            Assert.True(FindProgram(r, "PROGRAM/A1-1").ParentFull == "PROGRAM",
                "A1-1.ParentFull 应 = PROGRAM（默认组容器）");
            Assert.True(OpProgramFull(r, "OP-001") == "A01", "OP-001 锚点应 = A01");
            Assert.True(OpProgramFull(r, "OP-002") == "PROGRAM/A1-1", "OP-002 锚点应 = PROGRAM/A1-1");
            Assert.True(OpProgramFull(r, "OP-003") == "PROGRAM", "OP-003 锚点应 = PROGRAM（默认组）");
        }

        // v1.5-① 保序（160101 comparer ORDER_SHIFT 实证）：指令集应产 DFS 交错序（组/ws 交错），
        // executor 按此序创建 → rebuilt NX 组成员序与 gt 同构（刀路输出序一致）。
        public static void test_v15_steps_interleave_program_and_op_in_dfs_order()
        {
            var p = new PlanDocument();
            p.contract_version = "3.0";
            p.plan_id = "P-3";
            p.input_ref = "x";
            p.setups.Add(new SetupJson { setup_id = "S-01", name = "MCS_MILL" });
            p.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "Mill", subtype = "Mill5" });
            for (int i = 1; i <= 2; i++)
            {
                p.operations.Add(new OperationJson
                {
                    operation_id = "OP-00" + i, operation_type = "milling",
                    nx_template = new NxTemplateJson { type = "mill_contour", subtype = "CAVITY_MILL" },
                    tool_ref = "T-001", method_ref = "MILL_ROUGH",
                });
                p.workingsteps.Add(new WorkingstepJson
                {
                    workingstep_id = "WS-0" + i, feature_ref = "F-0" + i,
                    operation_ref = "OP-00" + i, setup_ref = "S-01",
                });
            }
            var root = p.workplan.root;
            root.name = "PROGRAM";
            // 模拟 test.prt 序：A01 成员 = [CAVITY(WS-01), A1-1(→WS-02)]（op 先于子组）
            var a01 = new WorkplanNodeJson { kind = "program", name = "A01" };
            a01.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "CAVITY_MILL", @ref = "WS-01" });
            var a11 = new WorkplanNodeJson { kind = "program", name = "A1-1" };
            a11.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "打点", @ref = "WS-02" });
            a01.children.Add(a11);
            root.children.Add(a01);
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "合法 plan 应 Ok");
            Assert.True(r.Steps.Count == 4, "Steps 应 = [A01组, OP-001, A1-1组, OP-002]（n=" + r.Steps.Count + "）");
            Assert.True(r.Steps[0].IsProgram && r.Steps[0].Program.Full == "A01", "Steps[0] 应 = A01 组");
            Assert.True(!r.Steps[1].IsProgram && r.Steps[1].Operation.OpId == "OP-001",
                "Steps[1] 应 = OP-001（op 先于子组，gt 成员序）");
            Assert.True(r.Steps[2].IsProgram && r.Steps[2].Program.Full == "A01/A1-1", "Steps[2] 应 = A1-1 组");
            Assert.True(!r.Steps[3].IsProgram && r.Steps[3].Operation.OpId == "OP-002", "Steps[3] 应 = OP-002");
        }

        // ---------- v1.5-③（V15-*，docs/nx-params-v15-spec.md §3） ----------

        // V15-POST-2：写面命中（注册表 4 持久键）→ 指令含 kind 与值（枚举词 S / 数值 N）。
        public static void test_V15POST2_persist_keys_become_instructions()
        {
            PlanDocument p = SamplePlan();
            OperationJson o = p.operations[0];   // OP-001 CAVITY_MILL
            o.strategy["cut_pattern"] = "FollowPeriphery";
            o.strategy["cut_order"] = "DepthFirst";
            o.strategy["cut_direction"] = "Climb";
            o.strategy["finish_passes"] = 2.0;
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "应 Ok");
            OpCommand cav = null;
            foreach (OpCommand c in r.Operations) if (c.OpId == "OP-001") cav = c;
            Assert.NotNull(cav, "OP-001 应在指令中");
            Assert.True(cav.Params.Count == 6, "应含 4 持久键 + part_stock + floor_stock（6 条）");
            ParamInstruction pat = null, ord = null, dir = null, fin = null;
            foreach (ParamInstruction pi in cav.Params)
                if (pi.MemberPath == "CutPattern.CutPattern") pat = pi;
                else if (pi.MemberPath == "CutParameters.CutOrder") ord = pi;
                else if (pi.MemberPath == "CutParameters.CutDirection.Type") dir = pi;
                else if (pi.MemberPath == "CutParameters.FinishPasses.NumberOfFinishPasses") fin = pi;
            Assert.True(pat != null && pat.Kind == ParamKind.Enum && pat.S == "FollowPeriphery" && pat.N == null,
                "cut_pattern 指令应为 Enum + NX 原文词");
            Assert.True(ord != null && ord.S == "DepthFirst", "cut_order 指令词错");
            Assert.True(dir != null && dir.S == "Climb", "cut_direction 指令词错");
            Assert.True(fin != null && fin.Kind == ParamKind.Number && fin.N == 2.0 && fin.S == null,
                "finish_passes 指令应为 Number + N=2");
        }

        // V15-POST-2：technology rpm（白名单既有）→ 数值指令（I-2 写侧 [I] 亮点的 [U] 前镜像）。
        public static void test_V15POST2_rpm_becomes_instruction()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].technology["spindle_rpm"] = 2400.0;
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "应 Ok");
            OpCommand cav = null;
            foreach (OpCommand c in r.Operations) if (c.OpId == "OP-001") cav = c;
            bool hasRpm = false;
            foreach (ParamInstruction pi in cav.Params)
                if (pi.MemberPath == "FeedsBuilder.SpindleRpmBuilder" && pi.Kind == ParamKind.Number && pi.N == 2400.0)
                    hasRpm = true;
            Assert.True(hasRpm, "spindle_rpm → FeedsBuilder.SpindleRpmBuilder Number 指令");
        }

        // V15-POST-2：枚举词 ∉ NxParamWords → PARAM_ENUM_UNKNOWN error diag，该键不入指令（不静默）。
        public static void test_V15POST2_enum_word_unknown_rejected()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].strategy["cut_pattern"] = "BogusWord";
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "非致命应 Ok");
            Assert.True(HasDiag(r, "PARAM_ENUM_UNKNOWN", "OP-001"), "应含 PARAM_ENUM_UNKNOWN diag");
            OpCommand cav = null;
            foreach (OpCommand c in r.Operations) if (c.OpId == "OP-001") cav = c;
            foreach (ParamInstruction pi in cav.Params)
                Assert.False(pi.MemberPath == "CutPattern.CutPattern", "未知词不得入指令");
        }

        // V15-PRE-1：值 kind 与写面 kind 不符 → PARAM_KIND_MISMATCH error（数值键给串 / 枚举键给数）。
        public static void test_V15PRE1_kind_mismatch_error()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].strategy["finish_passes"] = "Zig";   // 数值键收到枚举串
            p.operations[0].strategy["cut_pattern"] = 0.5;       // 枚举键收到数值
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "非致命应 Ok");
            Assert.True(HasDiag(r, "PARAM_KIND_MISMATCH", "OP-001"), "应含 PARAM_KIND_MISMATCH diag");
            OpCommand cav = null;
            foreach (OpCommand c in r.Operations) if (c.OpId == "OP-001") cav = c;
            foreach (ParamInstruction pi in cav.Params)
                Assert.False(pi.MemberPath == "CutPattern.CutPattern" || pi.MemberPath == "CutParameters.FinishPasses.NumberOfFinishPasses",
                    "kind 冲突键不得入指令");
        }

        // V15-POST-2：负结案键（Boundary 容差族，注册表 #7/#8）表外 → PARAM_UNSUPPORTED warning（不静默）。
        public static void test_V15POST2_negative_key_warned()
        {
            PlanDocument p = SamplePlan();
            p.operations[0].strategy["boundary_intol"] = 0.02;
            p.operations[0].strategy["boundary_outtol"] = 0.03;
            RebuildPlan r = ExecutorCore.Build(p);
            Assert.True(r.Ok, "非致命应 Ok");
            Assert.True(HasDiag(r, "PARAM_UNSUPPORTED", "OP-001"), "boundary 族应 PARAM_UNSUPPORTED warning");
            OpCommand cav = null;
            foreach (OpCommand c in r.Operations) if (c.OpId == "OP-001") cav = c;
            foreach (ParamInstruction pi in cav.Params)
                Assert.False(pi.MemberPath == "CutParameters.BoundaryInTol", "拒收键不得入指令");
        }
    }
}
