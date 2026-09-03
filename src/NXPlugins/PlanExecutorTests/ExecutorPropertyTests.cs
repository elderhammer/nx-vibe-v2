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
                strategy = new Dictionary<string, double> { { "part_stock", 0.3 }, { "floor_stock", 0.1 } },
                technology = new Dictionary<string, double>(),
            });
            plan.operations.Add(new OperationJson
            {
                operation_id = "OP-002", operation_type = "drilling",
                nx_template = new NxTemplateJson { type = "hole_making", subtype = "DRILLING" },
                tool_ref = "T-002", method_ref = "METHOD",
                strategy = new Dictionary<string, double> { { "hole_depth", 20 } },
                technology = new Dictionary<string, double>(),
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

        // PRE-4：白名单非空且排除已知不可写键（stepover 家族 U-6）。
        public static void test_PRE4_whitelist_nonempty_excludes_stepover()
        {
            Assert.True(ParamWhiteList.IsReady, "白名单应就绪");
            Assert.False(ParamWhiteList.StrategyWritable.ContainsKey("stepover"),
                "stepover 不得在白名单（U-6 整链写无效）");
            Assert.True(ParamWhiteList.StrategyWritable.ContainsKey("part_stock"),
                "part_stock 应可写（E4 实证）");
            Assert.False(ParamWhiteList.StrategyWritable.ContainsKey("cut_pattern"),
                "枚举面 v1 不在白名单（写入需形态分派，[I] 增强前不固话）");
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
                Assert.True(pi.MemberPath == "CutParameters.PartStock" && pi.Value == 0.3
                    || pi.MemberPath == "CutParameters.FloorStock" && pi.Value == 0.1,
                    "OP-001 参数指令内容不符: " + pi.MemberPath + "=" + pi.Value);
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
    }
}
