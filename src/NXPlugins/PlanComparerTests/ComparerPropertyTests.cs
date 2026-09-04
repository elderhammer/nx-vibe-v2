// ComparerPropertyTests.cs — PlanComparer 性质单测（红线；断言注释引性质原文，见 docs/nx-plan-comparer-spec.md §3）
// 覆盖 [U] 层：PRE-C1、POST-C1/2/3/4/5/6/7、INV-C1/2/3/4、MONO-C1。
// I-1（双件轮换 [T]）、I-2/I-3（NX 会话首跑/重导回归）为 [I] 无单测——符合 spec 分层。
// 夹具语义：A=gt 手编件快照、B=重建件快照（同 plan 链 → 名/值一致）；变异 = 在 B 上制造已知偏差。

using System;
using System.Collections.Generic;
using NXPlugins.PlanComparer;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanExporterTests
{
    public static class ComparerPropertyTests
    {
        // ---------- 夹具 ----------

        private static ExportSnapshot Sample()
        {
            var snap = new ExportSnapshot { Name = "t", InputRef = "x", CreatedAt = "2026-09-04T00:00:00+08:00" };
            snap.ProgramOrder.Add("A01");
            snap.Tools.Add(new ToolItem { Name = "17.0", TypeFamily = "Milling Tool-5 Parameters", NxType = "Mill", NxSubtype = "Mill5", Diameter = 17, NumFlutes = 4, FluteLength = 50, LowerCornerRadius = 0 });
            snap.Tools.Add(new ToolItem { Name = "8.5", TypeFamily = "Drilling Tool", NxType = "Drill", NxSubtype = "DrillStandard", Diameter = 8.5, NumFlutes = 2, FluteLength = 35, LowerCornerRadius = 0 });
            snap.Setups.Add(new SetupItem { Name = "MCS_MILL", McsOrigin = new double[] { 75, 0, 100 }, McsZAxis = new double[] { 0, 0, 1 }, McsXAxis = new double[] { 1, 0, 0 }, FixtureOffset = 1 });
            var op1 = new OperationItem
            {
                Name = "CAVITY_1", TypeFamily = "Cavity Milling", ProgramParent = "A01", MethodParent = "MILL_ROUGH",
                ToolParent = "17.0", GeometryParent = "WORKPIECE", HasGeometryParent = true, Key = new TagKey(1),
            };
            op1.Params["part_stock"] = 0.1; op1.Params["floor_stock"] = 0; op1.Params["depth_per_cut"] = 0;
            snap.Operations.Add(op1);
            var op2 = new OperationItem
            {
                Name = "打点1", TypeFamily = "Point to Point", ProgramParent = "A01", MethodParent = "METHOD",
                ToolParent = "8.5", GeometryParent = "WORKPIECE", HasGeometryParent = true, Key = new TagKey(2),
            };
            op2.Params["hole_depth"] = 20;
            snap.Operations.Add(op2);
            return snap;
        }

        private static ExportSnapshot CloneOf(ExportSnapshot s)
        {
            // 深拷贝（测试用；INV-C2 断言 Compare 不改写输入）
            var c = new ExportSnapshot { Name = s.Name, InputRef = s.InputRef, CreatedAt = s.CreatedAt };
            c.ProgramOrder.AddRange(s.ProgramOrder);
            foreach (var t in s.Tools)
                c.Tools.Add(new ToolItem { Name = t.Name, TypeFamily = t.TypeFamily, NxType = t.NxType, NxSubtype = t.NxSubtype, Diameter = t.Diameter, NumFlutes = t.NumFlutes, FluteLength = t.FluteLength, LowerCornerRadius = t.LowerCornerRadius });
            foreach (var u in s.Setups)
                c.Setups.Add(new SetupItem { Name = u.Name, McsOrigin = u.McsOrigin, McsZAxis = u.McsZAxis, McsXAxis = u.McsXAxis, FixtureOffset = u.FixtureOffset, MissingMcs = u.MissingMcs, SafePlaneZ = u.SafePlaneZ });
            foreach (var o in s.Operations)
            {
                var n = new OperationItem
                {
                    Name = o.Name, TypeFamily = o.TypeFamily, ProgramParent = o.ProgramParent, MethodParent = o.MethodParent,
                    ToolParent = o.ToolParent, GeometryParent = o.GeometryParent, HasGeometryParent = o.HasGeometryParent,
                    Key = o.Key,
                };
                foreach (KeyValuePair<string, double> kv in o.Params) n.Params[kv.Key] = kv.Value;
                c.Operations.Add(n);
            }
            return c;
        }

        private static bool HasIssue(ComparerResult r, string code, string keyHint)
        {
            foreach (ComparerIssue i in r.Issues)
                if (i.Code == code && (keyHint == null || i.Key == keyHint)) return true;
            return false;
        }

        private static string Describe(ComparerResult r)
        {
            var s = new List<string>();
            foreach (ComparerIssue i in r.Issues) s.Add(i.Code + "@" + i.Key + ":" + i.Detail);
            return string.Join(" | ", s);
        }

        // ---------- PRE-C1：输入合法 ----------

        // PRE-C1：输入快照非空且结构合法（列表存在、每 op 有 Name）→ 明确错误/异常。
        public static void test_PREC1_null_snapshot_rejected()
        {
            try { CompareCore.Compare(null, Sample()); Assert.Fail("A=null 应抛"); }
            catch (ArgumentNullException) { }
            try { CompareCore.Compare(Sample(), null); Assert.Fail("B=null 应抛"); }
            catch (ArgumentNullException) { }
        }

        public static void test_PREC1_op_without_name_rejected()
        {
            ExportSnapshot b = Sample();
            b.Operations.Add(new OperationItem { Name = "", TypeFamily = "Cavity Milling", Key = new TagKey(9) });
            try { CompareCore.Compare(Sample(), b); Assert.Fail("空名 op 应明确报错"); }
            catch (Exception e) { Assert.True(e is ArgumentException || e is InvalidOperationException, "空名 op → 明确异常: " + e.GetType().Name); }
        }

        // ---------- POST-C1：参数逐键双判据 ----------

        // POST-C1：匹配 op 对 Params 逐键（EpsLen 或 RelTol）→ PASS/FAIL 条目含双侧值与键。
        public static void test_POSTC1_identical_params_all_pass()
        {
            ComparerResult r = CompareCore.Compare(Sample(), Sample());
            Assert.False(HasIssue(r, "OP_PARAM_DIFF", null), "一致参数零差异: " + Describe(r));
            // 4 参数键 × 双侧一致 → 4 检查 4 过（CAVITY 3 键 + 打点 1 键）
            Assert.Equal(4, r.ParamChecks, "参数检查数 = 双侧一致键数");
            Assert.Equal(4, r.ParamPass, "参数全过");
            Assert.Equal(2, r.OpsMatched, "两 op 配对");
        }

        public static void test_POSTC1_single_param_mutation_single_fail()
        {
            ExportSnapshot b = Sample();
            b.Operations[0].Params["part_stock"] = 0.1 + 0.5;   // 恰 1 处变异（超 0.01 与 5%）
            ComparerResult r = CompareCore.Compare(Sample(), b);
            int diffs = 0;
            foreach (ComparerIssue i in r.Issues) if (i.Code == "OP_PARAM_DIFF") diffs++;
            Assert.Equal(1, diffs, "恰 1 处变异 → 恰 1 条参数差异: " + Describe(r));
            Assert.Equal(3, r.ParamPass, "其余 3 键仍过");
            Assert.Equal(4, r.ParamChecks, "检查数不变");
        }

        public static void test_POSTC1_within_tolerance_passes()
        {
            // 双判据：|a-b|≤EpsLen(0.01) 或 相对≤5% → PASS（浮点回读噪声兜底）
            ExportSnapshot b = Sample();
            b.Operations[0].Params["part_stock"] = 0.1 + 0.005;   // 0.005 < 0.01 → 容差内
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.False(HasIssue(r, "OP_PARAM_DIFF", "CAVITY_1"), "容差内 → PASS: " + Describe(r));
        }

        public static void test_POSTC1_key_present_only_one_side_fails()
        {
            // 键单侧有 → FAIL 条目（不静默缺字段）
            ExportSnapshot b = Sample();
            b.Operations[1].Params.Remove("hole_depth");
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "OP_PARAM_DIFF", "打点1"), "单侧缺键 → FAIL 条目: " + Describe(r));
        }

        // ---------- POST-C2：模板对失配显式 ----------

        // POST-C2：TypeFamily resolve 归一后 pair 不等 → 显式条目。
        public static void test_POSTC2_template_mismatch_explicit()
        {
            ExportSnapshot b = Sample();
            b.Operations[0].TypeFamily = "Point to Point";   // 同名 op 换家族
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "OP_TEMPLATE_DIFF", "CAVITY_1"), "模板对失配显式: " + Describe(r));
            Assert.Equal(2, r.TemplateChecks, "模板检查 = 配对 op 数（CAVITY_1 + 打点1）");
            Assert.Equal(1, r.TemplatePass, "打点对模板一致通过，CAVITY_1 对失配");
        }

        // ---------- POST-C3：刀具逐把（序对）类型 + 数值 ----------

        public static void test_POSTC3_tool_params_pass_and_detect()
        {
            ComparerResult r = CompareCore.Compare(Sample(), Sample());
            Assert.False(HasIssue(r, "TOOL_PARAM_DIFF", null), "刀具一致零差异: " + Describe(r));
            Assert.Equal(2, r.ToolPass, "两把刀全过");

            ExportSnapshot b = Sample();
            b.Tools[0].Diameter = 22.0;   // +5（29%）——须同时超绝对(0.01)与相对(5%)双判据才 FAIL
            ComparerResult r2 = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r2, "TOOL_PARAM_DIFF", "tool#1"), "直径变异检出: " + Describe(r2));
            Assert.Equal(1, r2.ToolPass, "另一把仍过");
            // 校准注记：+0.5/17 = 2.9% 落在 RelTol=5% 内判 PASS（OR 判据）——变异量须超双判据，见 spec 决策④
        }

        public static void test_POSTC3_tool_type_mismatch_explicit()
        {
            ExportSnapshot b = Sample();
            b.Tools[1].NxType = "Mill"; b.Tools[1].NxSubtype = "Mill5";   // 钻变铣
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "TOOL_TYPE_DIFF", "tool#2"), "类型失配显式: " + Describe(r));
        }

        public static void test_POSTC3_tool_type_family_fallback()
        {
            // 双侧均无 NxType（D-2 时代两资产，U-7 前）→ TypeFamily 兜底可比；混合态（一侧 NX 词
            // 一侧 FAM）为真差异 → 显式 TOOL_TYPE_DIFF（不静默）
            ExportSnapshot a = Sample();
            ExportSnapshot b = Sample();
            a.Tools[0].NxType = ""; a.Tools[0].NxSubtype = "";
            b.Tools[0].NxType = ""; b.Tools[0].NxSubtype = "";
            ComparerResult r = CompareCore.Compare(a, b);
            Assert.False(HasIssue(r, "TOOL_TYPE_DIFF", null), "双侧 FAM 兜底可比: " + Describe(r));

            b.Tools[0].TypeFamily = "Drilling Tool";   // 兜底后家族不同 → 差异
            ComparerResult r2 = CompareCore.Compare(a, b);
            Assert.True(HasIssue(r2, "TOOL_TYPE_DIFF", "tool#1"), "FAM 家族不同仍检出: " + Describe(r2));
        }

        public static void test_POSTC3_tool_count_mismatch()
        {
            ExportSnapshot b = Sample();
            b.Tools.RemoveAt(1);
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "TOOL_STRUCT", null), "刀具数差 → 结构条目: " + Describe(r));
        }

        // ---------- POST-C4：setup 名对 MCS/fixture ----------

        public static void test_POSTC4_mcs_identical_and_detected()
        {
            ComparerResult r = CompareCore.Compare(Sample(), Sample());
            Assert.False(HasIssue(r, "MCS_DIFF", null), "MCS 一致零差异: " + Describe(r));
            Assert.Equal(1, r.McsPass, "setup 全过");

            ExportSnapshot b = Sample();
            b.Setups[0].McsOrigin = new double[] { 76, 0, 100 };   // 欧氏 1mm
            ComparerResult r2 = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r2, "MCS_DIFF", "setup:MCS_MILL"), "origin 偏移检出: " + Describe(r2));
        }

        public static void test_POSTC4_axis_element_mismatch()
        {
            ExportSnapshot b = Sample();
            b.Setups[0].McsZAxis = new double[] { 0.001, 0, 0.9999995 };   // 元素差 0.001 > 1e-6
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "MCS_DIFF", "setup:MCS_MILL"), "轴元素差检出: " + Describe(r));
        }

        public static void test_POSTC4_fixture_mismatch()
        {
            ExportSnapshot b = Sample();
            b.Setups[0].FixtureOffset = 2;
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "FIXTURE_DIFF", "setup:MCS_MILL"), "fixture 差检出: " + Describe(r));
        }

        public static void test_POSTC4_setup_missing_read_or_absent()
        {
            ExportSnapshot a = Sample();
            ExportSnapshot b = Sample();
            b.Setups[0].MissingMcs = true;
            ComparerResult r = CompareCore.Compare(a, b);
            Assert.True(HasIssue(r, "READ_MISSING", null), "单侧 MCS 缺读显式: " + Describe(r));

            ExportSnapshot c = Sample();
            c.Setups.Clear();
            ComparerResult r2 = CompareCore.Compare(a, c);
            Assert.True(HasIssue(r2, "SETUP_STRUCT", null), "setup 数差结构条目: " + Describe(r2));
        }

        // ---------- POST-C5 / MONO-C1：汇总确定性 + 幂等 ----------

        // POST-C5：汇总计数由条目派生（明细手算 == 汇总）。
        public static void test_POSTC5_summary_derived_from_items()
        {
            ExportSnapshot b = Sample();
            b.Operations[0].Params["part_stock"] = 3.0;      // 1 参数差
            b.Tools[0].Diameter = 99;                         // 1 刀差
            b.Setups[0].McsOrigin = new double[] { 100, 0, 100 };  // 1 setup 差
            ComparerResult r = CompareCore.Compare(Sample(), b);
            // 参数：4 检查 3 过；刀具：2 检查 1 过；MCS：1 检查 0 过
            Assert.Equal(4, r.ParamChecks, "参数检查数");
            Assert.Equal(3, r.ParamPass, "参数 PASS = 4-1");
            Assert.Equal(2, r.ToolChecks, "刀具检查数");
            Assert.Equal(1, r.ToolPass, "刀具 PASS = 2-1");
            Assert.Equal(1, r.McsChecks, "MCS 检查数");
            Assert.Equal(0, r.McsPass, "MCS PASS = 1-1");
            Assert.Equal(2, r.OpsMatched, "结构：两 op 均配对");
        }

        // MONO-C1 / POST-C5：无状态幂等——同输入重复调用恒同输出。
        public static void test_MONOC1_idempotent_repeat_calls()
        {
            ExportSnapshot b = Sample();
            b.Tools[0].Diameter = 99;
            ComparerResult r1 = CompareCore.Compare(Sample(), b);
            ComparerResult r2 = CompareCore.Compare(Sample(), b);
            Assert.Equal(r1.Issues.Count, r2.Issues.Count, "两次调用 Issues 数一致");
            Assert.Equal(r1.ParamPass, r2.ParamPass, "两次调用汇总一致");
            Assert.Equal(r1.ToolPass, r2.ToolPass, "两次调用刀具一致");
            Assert.Equal(r1.McsPass, r2.McsPass, "两次调用 MCS 一致");
        }

        // ---------- POST-C6：结构维度 ----------

        public static void test_POSTC6_missing_and_extra_op_detected()
        {
            ExportSnapshot a = Sample();
            ExportSnapshot b = Sample();
            b.Operations.RemoveAt(1);                 // 重建缺 1
            ComparerResult r = CompareCore.Compare(a, b);
            Assert.Equal(1, r.OpsMissing, "重建缺失计数");
            Assert.Equal(0, r.OpsExtra, "无多余");
            Assert.True(HasIssue(r, "OP_STRUCT", "打点1"), "缺失 op 结构条目: " + Describe(r));

            ExportSnapshot c = Sample();
            c.Operations.Add(new OperationItem { Name = "EXTRA_1", TypeFamily = "Cavity Milling", Key = new TagKey(8) });
            ComparerResult r2 = CompareCore.Compare(a, c);
            Assert.Equal(1, r2.OpsExtra, "重建多余计数");
            Assert.True(HasIssue(r2, "OP_STRUCT", "EXTRA_1"), "多余 op 结构条目");
        }

        public static void test_POSTC6_program_order_mismatch()
        {
            ExportSnapshot b = Sample();
            b.ProgramOrder.Clear(); b.ProgramOrder.Add("B02");
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "PROGRAM_ORDER_DIFF", null), "顶层组序差 → 结构条目: " + Describe(r));
        }

        // ---------- POST-C7：op 名序 ----------

        // POST-C7：名集相同而采集序不同 → ORDER_SHIFT（不静默）。
        public static void test_POSTC7_op_order_shift_diag()
        {
            ExportSnapshot b = Sample();
            b.Operations.Reverse();   // 名集同、序反
            ComparerResult r = CompareCore.Compare(Sample(), b);
            Assert.True(HasIssue(r, "ORDER_SHIFT", null), "逆序 → ORDER_SHIFT: " + Describe(r));
            Assert.Equal(2, r.OpsMatched, "逆序仍按名配对");
        }

        // ---------- INV-C1：对齐 1:1 ----------

        public static void test_INVC1_duplicate_name_not_silent()
        {
            ExportSnapshot a = Sample();
            ExportSnapshot b = Sample();
            b.Operations.Add(new OperationItem { Name = "CAVITY_1", TypeFamily = "Cavity Milling", Key = new TagKey(7) });
            ComparerResult r = CompareCore.Compare(a, b);
            Assert.True(HasIssue(r, "DUP_NAME", null), "重复名 → DUP_NAME 显式: " + Describe(r));
        }

        // ---------- INV-C2：只读不改写 ----------

        // INV-C2：Compare 不改写输入快照。
        public static void test_INVC2_snapshots_not_mutated()
        {
            ExportSnapshot a = Sample();
            ExportSnapshot b = Sample();
            b.Operations[0].Params["part_stock"] = 3.0;
            ExportSnapshot a0 = CloneOf(a), b0 = CloneOf(b);
            CompareCore.Compare(a, b);
            // 逐字段断言（a 全字段、b 变异键保持变异值而非被 Compare 回写）
            Assert.Equal(a0.Operations.Count, a.Operations.Count, "A ops 数不变");
            Assert.Equal(a0.Tools.Count, a.Tools.Count, "A tools 数不变");
            Assert.Equal(a0.Setups.Count, a.Setups.Count, "A setups 数不变");
            Assert.Equal(a0.ProgramOrder.Count, a.ProgramOrder.Count, "A 组序不变");
            Assert.Equal(3.0, b.Operations[0].Params["part_stock"], "B 变异键值未被回写");
            Assert.Equal(a0.Operations[0].Params["part_stock"], a.Operations[0].Params["part_stock"], "A 参数键值不变");
            Assert.Equal(a0.Tools[0].Diameter, a.Tools[0].Diameter, "A 刀值不变");
            Assert.Equal(a0.Setups[0].McsOrigin[0], a.Setups[0].McsOrigin[0], "A MCS 不变");
        }

        // ---------- INV-C3/INV-C4：可溯 + 聚合 ----------

        // INV-C3：每条 issue 带可溯 key（非空）。
        public static void test_INVC3_every_issue_has_key()
        {
            ExportSnapshot b = Sample();
            b.Operations[0].Params["part_stock"] = 3.0;
            b.Tools[0].Diameter = 99;
            b.Setups[0].FixtureOffset = 2;
            ComparerResult r = CompareCore.Compare(Sample(), b);
            foreach (ComparerIssue i in r.Issues)
                Assert.False(string.IsNullOrEmpty(i.Key), "issue 带 key: " + i.Code);
        }

        // INV-C4：同 key+code+detail 聚合一次。
        public static void test_INVC4_same_key_code_dedup()
        {
            // 聚合场景：B 侧同名 X 两个实例 → 该名仅一条 DUP_NAME（而非每实例一条）；X 不参与配对。
            ExportSnapshot a = Sample();
            ExportSnapshot b = Sample();
            b.Operations.Add(new OperationItem { Name = "X", TypeFamily = "Cavity Milling", Key = new TagKey(6) });
            b.Operations.Add(new OperationItem { Name = "X", TypeFamily = "Cavity Milling", Key = new TagKey(7) });
            ComparerResult r = CompareCore.Compare(a, b);
            int dupCount = 0;
            foreach (ComparerIssue i in r.Issues) if (i.Code == "DUP_NAME") dupCount++;
            Assert.Equal(1, dupCount, "同名 → DUP_NAME 单条聚合: " + Describe(r));
            Assert.Equal(2, r.OpsMatched, "非 dup op 仍正常配对（CAVITY_1/打点1）");
        }
    }
}
