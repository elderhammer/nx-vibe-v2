// U7ToolTypeTests.cs — U-7 A′ 导出侧/校验侧性质单测（红线）
// 性质原文见 docs/nx-tool-type-enum-spec.md §3：PRE-U7-1（替身语义=失败接线）、
// POST-U7-1（type/subtype ∈ NX 词集 + schema 校验过）、INV-U7-1（原文直写无归类表）、
// INV-U7-4（读回失败 → 不入 plan + TOOL_TYPE_UNREADABLE，不静默）。

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanExporterTests
{
    public static class U7ToolTypeTests
    {
        // ---------- 夹具 ----------

        private static ExportSnapshot SnapWithTool(ToolItem tool, string opToolParent)
        {
            var snap = new ExportSnapshot
            {
                Name = "u7", InputRef = "x", CreatedAt = "2026-09-04T00:00:00+08:00",
            };
            if (tool != null) snap.Tools.Add(tool);
            snap.Operations.Add(new OperationItem
            {
                Name = "OP_A", Key = new TagKey(1), TypeFamily = "Cavity Milling",
                ProgramParent = "", MethodParent = "MILL_ROUGH", ToolParent = opToolParent,
                GeometryParent = "WORKPIECE", HasGeometryParent = true,
            });
            return snap;
        }

        // ---------- INV-U7-1：NX 枚举原文直写（type 来自 NxType，不来自 TypeFamily/归类表） ----------

        public static void test_inv_u7_1_type_subtype_written_verbatim()
        {
            ExportSnapshot snap = SnapWithTool(new ToolItem
            {
                Name = "刀-10", TypeFamily = "Milling Tool-5 Parameters",   // 旧家族串不参与 type
                NxType = "Mill", NxSubtype = "Mill5",
            }, "刀-10");
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            Assert.Equal(1, doc.resources.tools.Count, "入选刀具数");
            Assert.Equal("Mill", doc.resources.tools[0].type, "type = NX Types 原文直写");
            Assert.Equal("Mill5", doc.resources.tools[0].subtype, "subtype = NX Subtypes 原文直写");
            Assert.False(doc.resources.tools[0].type.IndexOf("Milling", StringComparison.Ordinal) >= 0,
                "type 不得来自 TypeFamily 变换（无归类表）");
            List<string> errs = PlanValidator.Validate(doc);
            Assert.Equal(0, errs.Count, "直写产物过校验（INV-U7-1/POST-U7-1）: " + string.Join(";", errs));
        }

        public static void test_post_u7_1_drill_pair_passes_validation()
        {
            ExportSnapshot snap = SnapWithTool(new ToolItem { Name = "D8.5", NxType = "Drill", NxSubtype = "DrillStandard" }, "D8.5");
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            List<string> errs = PlanValidator.Validate(doc);
            Assert.Equal(0, errs.Count, "钻族 (Drill,DrillStandard) 产物过校验: " + string.Join(";", errs));
        }

        public static void test_post_u7_1_old_capp_word_rejected()
        {
            // 反证：旧 14 CAPP 词（"end_mill"）不再是合法 type → validator 收紧生效
            PlanDocument doc = new PlanDocument();
            doc.contract_version = "3.0";
            doc.plan_id = "P"; doc.name = "n"; doc.input_ref = "i";
            doc.setups.Add(new SetupJson { setup_id = "S-01", mcs = null });
            doc.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "end_mill" });
            doc.workplan.root = new WorkplanNodeJson();
            List<string> errs = PlanValidator.Validate(doc);
            Assert.True(errs.Count > 0, "旧 CAPP 词应被词集收紧拒绝");
            Assert.Contains("end_mill", string.Join(";", errs), "错误消息指明词");
        }

        public static void test_post_u7_1_subtype_outside_enum_rejected()
        {
            PlanDocument doc = new PlanDocument();
            doc.contract_version = "3.0";
            doc.plan_id = "P"; doc.name = "n"; doc.input_ref = "i";
            doc.setups.Add(new SetupJson { setup_id = "S-01", mcs = null });
            doc.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "Mill", subtype = "NotAWord" });
            doc.workplan.root = new WorkplanNodeJson();
            List<string> errs = PlanValidator.Validate(doc);
            Assert.True(errs.Count > 0, "词集外 subtype 应被拒绝");
        }

        public static void test_post_u7_1_null_subtype_ok()
        {
            // subtype 可空（schema optional）：空 → null 不填 → 校验不拦
            PlanDocument doc = new PlanDocument();
            doc.contract_version = "3.0";
            doc.plan_id = "P"; doc.name = "n"; doc.input_ref = "i";
            doc.setups.Add(new SetupJson { setup_id = "S-01", mcs = null });
            doc.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "Mill", subtype = null });
            doc.workplan.root = new WorkplanNodeJson();
            List<string> errs = PlanValidator.Validate(doc);
            Assert.Equal(0, errs.Count, "subtype=null 通过: " + string.Join(";", errs));
        }

        public static void test_inv_u7_1_subtype_empty_not_written()
        {
            // 空 subtype（适配器未填）→ 落盘形态为 null（不产生空串进 enum）
            ExportSnapshot snap = SnapWithTool(new ToolItem { Name = "刀", NxType = "Mill", NxSubtype = "" }, "刀");
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            Assert.Null(doc.resources.tools[0].subtype, "空 subtype 落盘为 null（不填）");
        }

        // ---------- INV-U7-4：读回失败 → 不入 plan + error diag（不静默） ----------

        public static void test_inv_u7_4_unreadable_tool_excluded_with_diag()
        {
            ExportSnapshot snap = SnapWithTool(new ToolItem
            {
                Name = "刀坏", TypeFamily = "Drilling Tool",
                TypeReadbackError = "GetTypeAndSubtype 异常: 测试替身",
            }, "刀坏");
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            Assert.Equal(0, doc.resources.tools.Count, "读回失败刀不入 plan（INV-U7-4）");
            bool hasUnreadable = false, hasDangling = false;
            foreach (DiagnosticJson d in doc.diagnostics)
            {
                if (d.level == "error" && d.code == "TOOL_TYPE_UNREADABLE") hasUnreadable = true;
                if (d.level == "error" && d.code == "TOOL_REF_DANGLING") hasDangling = true;
            }
            Assert.True(hasUnreadable, "TOOL_TYPE_UNREADABLE error diag 存在");
            Assert.True(hasDangling, "引用 op 的 TOOL_REF_DANGLING error diag 存在（不静默）");
            // 引用链悬空 → validator 结构级拒绝（与 TPL_UNKNOWN 同口径：中止不落盘由适配器承接）
            List<string> errs = PlanValidator.Validate(doc);
            Assert.True(errs.Count > 0, "tool_ref 悬空 → 校验拒绝");
            Assert.Contains("tool_ref", string.Join(";", errs), "错误含 tool_ref");
        }

        public static void test_inv_u7_4_readable_tool_among_failures_kept()
        {
            // 坏刀剔除不波及好刀：好刀保留、坏刀引用 op 报悬空
            ExportSnapshot snap = SnapWithTool(new ToolItem { Name = "好刀", NxType = "Mill", NxSubtype = "Mill5" }, "好刀");
            snap.Tools.Add(new ToolItem { Name = "坏刀", TypeReadbackError = "as Tool 失败" });
            snap.Operations.Add(new OperationItem
            {
                Name = "OP_B", Key = new TagKey(2), TypeFamily = "Cavity Milling",
                MethodParent = "", ToolParent = "坏刀", GeometryParent = "W", HasGeometryParent = true,
            });
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            Assert.Equal(1, doc.resources.tools.Count, "好刀仍入选");
            Assert.Equal("T-001", doc.resources.tools[0].tool_id, "入选连续编号");
            bool hasDangling = false;
            foreach (DiagnosticJson d in doc.diagnostics)
                if (d.code == "TOOL_REF_DANGLING") hasDangling = true;
            Assert.True(hasDangling, "坏刀引用 op 悬空 diag");
        }
    }
}
