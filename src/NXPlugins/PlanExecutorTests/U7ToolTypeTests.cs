// U7ToolTypeTests.cs — U-7 A′ 执行侧性质单测（红线）
// 性质原文见 docs/nx-tool-type-enum-spec.md §3：INV-U7-2（注册对表 = 当前注册对集，P2 实测
// 校准：(Mill,Mill5)→(mill_planar,MILL)、(Drill,DrillStandard)→(hole_making,STD_DRILL)）、
// INV-U7-3（旧家族串回退分支保留：枚举未命中 → 关键词表 → 默认铣 + Inferred）。

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;
using NXPlugins.PlanExecutor;

namespace NXPlugins.PlanExporterTests
{
    public static class U7ExecutorToolTypeTests
    {
        // ---------- INV-U7-2：注册对表精确命中（行 = P2 实测校准对） ----------

        public static void test_inv_u7_2_mill_pair_resolves()
        {
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("Mill", "Mill5");
            Assert.False(r.Inferred, "注册对命中非推断");
            Assert.Equal("mill_planar", r.Pair.Type, "P2 校准对 (Mill,Mill5)→mill_planar");
            Assert.Equal("MILL", r.Pair.Subtype, "P2 校准对 (Mill,Mill5)→MILL");
        }

        public static void test_inv_u7_2_drill_pair_resolves()
        {
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("Drill", "DrillStandard");
            Assert.False(r.Inferred, "注册对命中非推断");
            Assert.Equal("hole_making", r.Pair.Type, "P2 校准对 (Drill,DrillStandard)→hole_making");
            Assert.Equal("STD_DRILL", r.Pair.Subtype, "P2 校准对 (Drill,DrillStandard)→STD_DRILL");
        }

        public static void test_inv_u7_2_uncovered_pair_inferred()
        {
            // 注册对表外组合（如 MillChamfer 型，spec §5b：精准建刀超 v1）→ 不伪造精确命中
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("Mill", "MillChamfer");
            Assert.True(r.Inferred, "(Mill,MillChamfer) 未覆盖 → Inferred");
        }

        public static void test_inv_u7_2_enum_words_case_sensitive()
        {
            // NX 枚举原文精确匹配（大小写敏感）：小写 "mill" 不是词集命中
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("mill", "Mill5");
            Assert.True(r.Inferred, "小写 type 非词集精确命中（原文直写语义）");
        }

        // ---------- INV-U7-3：旧家族串回退分支（D-2 兼容） ----------

        public static void test_inv_u7_3_chinese_family_fallback()
        {
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("铣刀-5 参数", null);
            Assert.False(r.Inferred, "中文家族串关键词命中非推断");
            Assert.True(r.KeywordFallback, "走关键词回退标记");
            Assert.Equal("mill_planar", r.Pair.Type, "铣刀→mill_planar/MILL（D-2 结果保持）");
        }

        public static void test_inv_u7_3_english_family_fallback()
        {
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("Drilling Tool", "");
            Assert.False(r.Inferred, "英文家族串关键词命中非推断");
            Assert.True(r.KeywordFallback, "走关键词回退标记");
            Assert.Equal("STD_DRILL", r.Pair.Subtype, "Drilling Tool→STD_DRILL（D-2 结果保持）");
        }

        public static void test_inv_u7_3_unknown_default_inferred()
        {
            ToolFamilyMap.Resolution r = ToolFamilyMap.Resolve("mystery-family", null);
            Assert.True(r.Inferred, "未知串 → 默认铣 + Inferred（现状不变）");
            Assert.Equal("MILL", r.Pair.Subtype, "默认 (mill_planar,MILL)");
        }

        // ---------- ExecutorCore 全链路：新词集重建 + NX 词未覆盖 diag 分诊 ----------

        private static PlanDocument SampleNxWordPlan()
        {
            var plan = new PlanDocument();
            plan.contract_version = "3.0";
            plan.plan_id = "U7"; plan.name = "n"; plan.input_ref = "i";
            plan.setups.Add(new SetupJson { setup_id = "S-01", name = "MCS_MILL", mcs = null });
            plan.resources.tools.Add(new ToolJson
            { tool_id = "T-001", type = "Mill", subtype = "Mill5", diameter = 10 });
            plan.resources.tools.Add(new ToolJson
            { tool_id = "T-002", type = "Drill", subtype = "DrillStandard", diameter = 8.5 });
            plan.workplan.root = new WorkplanNodeJson
            {
                kind = "program", name = "PROGRAM", @ref = "",
                children = new List<WorkplanNodeJson>
                {
                    new WorkplanNodeJson { kind = "workingstep", name = "OP-1", @ref = "WS-01" },
                },
            };
            plan.operations.Add(new OperationJson
            {
                operation_id = "OP-001", operation_type = "milling",
                nx_template = new NxTemplateJson { type = "mill_contour", subtype = "CAVITY_MILL" },
                tool_ref = "T-001", method_ref = "",
                strategy = new Dictionary<string, double>(), technology = new Dictionary<string, double>(),
            });
            plan.workingsteps.Add(new WorkingstepJson
            { workingstep_id = "WS-01", feature_ref = "", operation_ref = "OP-001", setup_ref = "S-01" });
            return plan;
        }

        public static void test_executor_nx_words_rebuild()
        {
            RebuildPlan r = ExecutorCore.Build(SampleNxWordPlan());
            Assert.True(r.Ok, "新词集 plan 全绿构建: " + Describe(r));
            Assert.Equal(2, r.Tools.Count, "两刀均入指令");
            Assert.Equal("mill_planar", r.Tools[0].Pair.Type, "(Mill,Mill5)→mill_planar");
            Assert.Equal("hole_making", r.Tools[1].Pair.Type, "(Drill,DrillStandard)→hole_making");
            Assert.False(r.Tools[0].TypeInferred, "铣刀非推断");
        }

        public static void test_executor_nx_word_uncovered_diag_message()
        {
            // 注册对表外 NX 词（Mill,Mill7）→ TOOL_TYPE_INFERRED 消息走"NX 词未覆盖"分支（分诊）
            PlanDocument plan = SampleNxWordPlan();
            plan.resources.tools.Clear();
            plan.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "Mill", subtype = "Mill7" });
            RebuildPlan r = ExecutorCore.Build(plan);
            bool found = false;
            foreach (RebuildDiag d in r.Diagnostics)
                if (d.Code == "TOOL_TYPE_INFERRED" && d.Message.Contains("注册对表未覆盖")) found = true;
            Assert.True(found, "NX 词未覆盖 → '注册对表未覆盖' 消息");
        }

        public static void test_executor_family_plan_still_rebuilds()
        {
            // 旧家族串 plan（中文 type，无 subtype）→ 回退分支仍可重建（D-2 行为不变）
            PlanDocument plan = SampleNxWordPlan();
            plan.resources.tools.Clear();
            plan.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "铣刀-5 参数", subtype = null, diameter = 10 });
            RebuildPlan r = ExecutorCore.Build(plan);
            Assert.True(r.Ok, "旧家族串 plan 可重建（INV-U7-3）: " + Describe(r));
            Assert.Equal("mill_planar", r.Tools[0].Pair.Type, "回退重建为铣注册对");
        }

        private static string Describe(RebuildPlan r)
        {
            var s = new List<string>();
            foreach (RebuildDiag d in r.Diagnostics) s.Add(d.Code + ":" + d.Message);
            return string.Join(" | ", s);
        }
    }
}
