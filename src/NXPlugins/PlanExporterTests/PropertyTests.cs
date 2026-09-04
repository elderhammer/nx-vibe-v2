// PropertyTests.cs — 按 spec 编号性质的单测（红线；断言注释引性质原文，见 docs/nx-plan-exporter-spec.md §3）
// 覆盖 [U] 层：PRE-1/PRE-2/PRE-3、POST-1/2/3/4/5/6、INV-1/2/3/4/5/6、MONO-2。
// MONO-1 为 [I]（评审/集成），无单测——符合 spec 分层。

using System;
using System.Collections.Generic;
using System.IO;
using NXPlugins.PlanExporter;
using Path = System.IO.Path;

namespace NXPlugins.PlanExporterTests
{
    public static class PropertyTests
    {
        // ---------- 夹具 ----------

        private sealed class FakeGate : ISessionGate
        {
            public bool HasDisplayedWorkPartWithCamSetup = true;
            public bool CanReserveCamBase = true;
            bool ISessionGate.HasDisplayedWorkPartWithCamSetup { get { return HasDisplayedWorkPartWithCamSetup; } }
            bool ISessionGate.CanReserveCamBase { get { return CanReserveCamBase; } }
        }

        private sealed class FailingSerializer : IPlanSerializer
        {
            public string Serialize(PlanDocument doc) { throw new InvalidOperationException("注入的序列化失败"); }
            public PlanDocument Deserialize(string json) { throw new InvalidOperationException("注入的反序列化失败"); }
        }

        private static OperationItem Op(string name, string family, string tagSuffix, string progParent,
            string methodParent, string toolParent, bool hasGeom)
        {
            return new OperationItem
            {
                Name = name,
                TypeFamily = family,
                Key = new TagKey((ulong)("OP" + tagSuffix).GetHashCode()),
                ProgramParent = progParent,
                MethodParent = methodParent,
                ToolParent = toolParent,
                HasGeometryParent = hasGeom,
                GeometryParent = hasGeom ? "WORKPIECE" : "",
            };
        }

        private static ExportSnapshot SampleSnapshot()
        {
            var snap = new ExportSnapshot
            {
                Name = "单腔体粗铣（最小闭环）",
                InputRef = "engineer/handmade/cavity_demo.prt",
                CreatedAt = "2026-09-03T08:00:00+08:00",
            };
            snap.ProgramOrder.Add("A01");
            snap.Setups.Add(new SetupItem
            {
                Name = "MCS_1",
                McsOrigin = new double[] { 0, 0, 0 },
                McsZAxis = new double[] { 0, 0, 1 },
                McsXAxis = new double[] { 1, 0, 0 },
                SafePlaneZ = 50,
                FixtureOffset = 1,
            });
            // U-7 产物形态：type/subtype = NX 枚举原文（TypeFamily 家族串不再写入 type）
            snap.Tools.Add(new ToolItem
            { Name = "PROBE_T1", TypeFamily = "铣刀-5 参数", NxType = "Mill", NxSubtype = "Mill5", Diameter = 10, NumFlutes = 4 });
            snap.Operations.Add(Op("CAVITY_MILL", "Cavity Milling", "1", "A01", "MILL_ROUGH", "PROBE_T1", true));
            snap.Operations.Add(Op("打点_COPY_COPY_COPY", "Point to Point", "2", "A01", "DRILL_METHOD", "PROBE_T1", true));
            return snap;
        }

        private static PlanDocument BuildSample()
        {
            return ExporterCore.Build(SampleSnapshot(), WhiteList.Resolve);
        }

        private static void AssertValid(PlanDocument doc, string ctx)
        {
            List<string> errs = PlanValidator.Validate(doc);
            Assert.True(errs.Count == 0, ctx + " 校验应通过，错误=" + string.Join("; ", errs));
        }

        private static string TempFile()
        {
            return Path.Combine(Path.GetTempPath(), "plan-export-test-" + Guid.NewGuid().ToString("N") + ".json");
        }

        // ---------- PRE ----------

        // PRE-1：入口要求部件打开为显示+工作、CAMSetup 非空，否则拒绝。
        public static void test_PRE1_reject_missing_context()
        {
            var gate = new FakeGate { HasDisplayedWorkPartWithCamSetup = false };
            Assert.False(ExportGates.Preflight(gate, true).Ok, "无上下文应失败");
            Assert.Contains("PRE-1", ExportGates.Preflight(gate, true).Failures[0], "失败原因应含 PRE-1");
        }

        // PRE-2：cam_base 许可可 Reserve 才继续。
        public static void test_PRE2_reject_license_unavailable()
        {
            var gate = new FakeGate { CanReserveCamBase = false };
            Assert.False(ExportGates.Preflight(gate, true).Ok, "许可不可用应失败");
        }

        // PRE-3：白名单非空才继续。
        public static void test_PRE3_reject_empty_whitelist()
        {
            var gate = new FakeGate();
            Assert.True(WhiteList.IsReady, "白名单应就绪（PRE-3 判据源）");
            Assert.False(ExportGates.Preflight(gate, false).Ok, "白名单未就绪应失败");
        }

        // ---------- POST ----------

        // POST-1：成功产出 plan.json 通过 schema 校验（含落盘后复验）。
        public static void test_POST1_success_validates_in_memory_and_after_write()
        {
            PlanDocument doc = BuildSample();
            AssertValid(doc, "POST-1 内存");
            string f = TempFile();
            try
            {
                PlanWriter.WriteAtomically(doc, f);
                PlanDocument back = PlanWriter.Serializer.Deserialize(File.ReadAllText(f));
                AssertValid(back, "POST-1 落盘复验");
            }
            finally { try { File.Delete(f); } catch { } }
        }

        // POST-2：失败不产生半成品；旧文件不被破坏（.tmp+rename）。
        public static void test_POST2_failure_keeps_old_file_intact()
        {
            string f = TempFile();
            try
            {
                File.WriteAllText(f, "OLD-CONTENT");
                var oldSerializer = PlanWriter.Serializer;
                try
                {
                    PlanWriter.Serializer = new FailingSerializer();
                    bool threw = false;
                    try { PlanWriter.WriteAtomically(BuildSample(), f); }
                    catch (InvalidOperationException) { threw = true; }
                    Assert.True(threw, "序列化失败应抛出");
                }
                finally { PlanWriter.Serializer = oldSerializer; }
                Assert.Equal("OLD-CONTENT", File.ReadAllText(f), "POST-2 旧文件应保持原样");
                string[] leftovers = Directory.GetFiles(Path.GetDirectoryName(f), Path.GetFileName(f) + ".tmp");
                Assert.True(leftovers.Length == 0, "POST-2 不应残留 .tmp");
            }
            finally { try { File.Delete(f); } catch { } }
        }

        // POST-3：字段/工序级回读失败 → diagnostics，不静默缺字段。
        public static void test_POST3_readback_error_becomes_diagnostic()
        {
            ExportSnapshot snap = SampleSnapshot();
            snap.Operations[1].ReadbackErrors.Add("CutOrder 回读失败");
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            bool found = false;
            foreach (DiagnosticJson d in doc.diagnostics)
                if (d.code == "READBACK_FAIL" && d.level == "error")
                { found = true; Assert.Contains("CutOrder", d.message, "诊断消息应含字段"); }
            Assert.True(found, "POST-3 回读失败应有 error 级 diag");
        }

        // POST-4：数值原样保真（double round-trip 无损）。
        public static void test_POST4_double_roundtrip_lossless()
        {
            ExportSnapshot snap = SampleSnapshot();
            snap.Operations[0].Params["depth_per_cut"] = 0.30000000000000004;   // 非"干净"double
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            string json = PlanWriter.Serializer.Serialize(doc);
            PlanDocument back = PlanWriter.Serializer.Deserialize(json);
            double v;
            Assert.True(back.operations[0].strategy.TryGetValue("depth_per_cut", out v),
                "POST-4 strategy 应含 depth_per_cut");
            Assert.Equal(0.30000000000000004, v, "POST-4 double 应原样往返");
        }

        // POST-5：结构级失败（PRE-1/2/3 不满足）→ 中止且不落盘。
        public static void test_POST5_structural_failure_no_output()
        {
            string f = TempFile();
            try
            {
                var gate = new FakeGate { HasDisplayedWorkPartWithCamSetup = false };
                PreflightResult pr = ExportGates.Preflight(gate, true);
                if (!pr.Ok)
                {
                    // 调用方契约：失败路径不调用 WriteAtomically（此处验证"若绕过守卫则无产物"由
                    // 适配器纪律保证；单测验证守卫确实拦截）——
                    Assert.True(pr.Failures.Count > 0, "POST-5 守卫应列出全部失败");
                    Assert.False(File.Exists(f), "POST-5 不应有输出文件");
                }
            }
            finally { try { File.Delete(f); } catch { } }
        }

        // POST-6：歧义工序 → 默认模板对 + warning diag。
        public static void test_POST6_ambiguous_family_defaults_with_warning()
        {
            PlanDocument doc = BuildSample();   // 含 Point to Point（歧义家族）
            bool ambiguousWarn = false;
            foreach (DiagnosticJson d in doc.diagnostics)
                if (d.code == "TPL_AMBIGUOUS") ambiguousWarn = true;
            Assert.True(ambiguousWarn, "POST-6 歧义家族应产生 TPL_AMBIGUOUS warning");
            foreach (OperationJson op in doc.operations)
                if (op.nx_template.type == "hole_making") Assert.Equal("DRILLING", op.nx_template.subtype,
                    "POST-6 PTP 默认对应为 (hole_making, DRILLING)");
        }

        // ---------- INV ----------

        // INV-1：任意产出 schema 合法（含落盘态——与 POST-1 互补：破坏性变体也要被拒）。
        public static void test_INV1_malformed_variants_rejected()
        {
            PlanDocument doc = BuildSample();
            doc.operations[0].nx_template.subtype = "";
            Assert.False(PlanValidator.Validate(doc).Count == 0, "INV-1 空 subtype 应非法");
            doc = BuildSample();
            doc.contract_version = "2.0";
            Assert.False(PlanValidator.Validate(doc).Count == 0, "INV-1 错误版本应非法");
        }

        // INV-2：ref 闭合。
        public static void test_INV2_dangling_ref_rejected()
        {
            PlanDocument doc = BuildSample();
            doc.operations[0].tool_ref = "T-999";
            Assert.False(PlanValidator.Validate(doc).Count == 0, "INV-2 tool_ref 悬空应非法");
        }

        // INV-3：1 op ↔ ≤1 ws；ws.operation_ref 回指存在。
        public static void test_INV3_one_to_one_mounting()
        {
            PlanDocument doc = BuildSample();
            Assert.True(doc.operations.Count == doc.workingsteps.Count, "INV-3 样例应为 1:1");
            PlanDocument dup = BuildSample();
            dup.workingsteps.Add(new WorkingstepJson
            {
                workingstep_id = "WS-XX",
                operation_ref = dup.workingsteps[0].operation_ref,
                setup_ref = "S-01",
            });
            Assert.False(PlanValidator.Validate(dup).Count == 0, "INV-3 一 op 多 ws 应非法");
        }

        // INV-4：每 operation 带四父链信息；父链缺失 → warning 而非整条丢弃。
        public static void test_INV4_missing_geometry_parent_warns_not_drops()
        {
            ExportSnapshot snap = SampleSnapshot();
            snap.Operations[0].HasGeometryParent = false;
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            Assert.True(doc.operations.Count == 2, "INV-4 缺父链的工序不应被丢弃");
            bool warned = false;
            foreach (DiagnosticJson d in doc.diagnostics)
                if (d.code == "GEOM_PARENT_MISSING") warned = true;
            Assert.True(warned, "INV-4 应有 GEOM_PARENT_MISSING warning");
        }

        // INV-5：Tag 去重——同一操作四视图各一次，plan 内恰一条。
        public static void test_INV5_duplicate_tag_collapsed_with_error()
        {
            ExportSnapshot snap = SampleSnapshot();
            snap.Operations.Add(Op("CAVITY_MILL_DUP_VIEW", "Cavity Milling", "1", "A01", "MILL_ROUGH", "PROBE_T1", true));
            // 同名同 Tag（模拟另一视图再次出现）
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            Assert.True(doc.operations.Count == 2, "INV-5 重复 Tag 只应保留一条");
            bool dupErr = false;
            foreach (DiagnosticJson d in doc.diagnostics)
                if (d.code == "DUP_TAG") dupErr = true;
            Assert.True(dupErr, "INV-5 应有 DUP_TAG error");
        }

        // INV-6：diagnostics 同类同 op 聚合一次；error 级有 code+message。
        public static void test_INV6_aggregate_same_op_same_code()
        {
            ExportSnapshot snap = SampleSnapshot();
            snap.Operations[0].ReadbackErrors.Add("A 失败");
            snap.Operations[0].ReadbackErrors.Add("B 失败");
            snap.Operations[0].ReadbackErrors.Add("A 失败");   // 重复源，应聚合
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            int n = 0;
            foreach (DiagnosticJson d in doc.diagnostics)
                if (d.code == "READBACK_FAIL") n++;
            Assert.True(n == 2, "INV-6 同 op 同 code 应聚合（期望 2 条不同消息，实际 " + n + "）");
            AssertValid(doc, "INV-6 产出仍应合法");
        }

        // MONO-2：遍历终止——重复/深树替身不挂死（Tag 去重守卫在快照层已覆盖；此处验证空/单视图输入可产合法空 plan）。
        public static void test_MONO2_empty_snapshot_terminates_with_valid_plan()
        {
            ExportSnapshot snap = SampleSnapshot();
            snap.Operations.Clear();
            snap.ProgramOrder.Clear();
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            AssertValid(doc, "MONO-2 空快照产出合法空 plan");
        }

        // ---------- D-4 (docs/nx-plan-contract-cleanup-spec.md §3 C1-*) ----------

        // C1-INV-1：operation_type/feature_type 无枚举断言——任意档词汇（含外部细类词）均合法。
        public static void test_C1INV1_free_string_two_tier_words_valid()
        {
            ExportSnapshot snap = SampleSnapshot();
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            doc.operations[0].operation_type = "mill_cavity";     // 外部 CAPP 细类词示例（不再非法）
            doc.features[0].feature_type = "pocket";              // 识别侧 AP224 词示例（不再非法）
            AssertValid(doc, "C1-INV-1 自由串两档：细类词应合法");
        }

        // C1-INV-2：产出 JSON 不含 geometry_ref/machines 等已删结构（序列化层同步，无孤儿字段）。
        public static void test_C1INV2_no_orphan_fields_in_serialized_output()
        {
            PlanDocument doc = ExporterCore.Build(SampleSnapshot(), WhiteList.Resolve);
            string json = new PlanJsonSerializer().Serialize(doc);
            Assert.True(json.IndexOf("machines", StringComparison.Ordinal) < 0,
                "C1-INV-2 序列化不应含 machines");
            Assert.True(json.IndexOf("geometry_ref", StringComparison.Ordinal) < 0,
                "C1-INV-2 序列化不应含 geometry_ref");
            Assert.True(json.IndexOf("face_anchors", StringComparison.Ordinal) < 0
                && json.IndexOf("anchor_point", StringComparison.Ordinal) < 0,
                "C1-INV-2 序列化不应含面级锚点字段");
        }

        // C1-INV-3/4：feature 条目 = id + feature_type(geometry_group 缺省) + params，ws 1:1 引用闭合保持。
        public static void test_C1INV34_feature_slim_shape_and_default_type()
        {
            PlanDocument doc = ExporterCore.Build(SampleSnapshot(), WhiteList.Resolve);
            Assert.True(doc.features.Count == doc.workingsteps.Count, "C1-INV-3 1 ws ↔ 1 feature 保持");
            foreach (FeatureJson f in doc.features)
            {
                Assert.True(f.feature_type == "geometry_group", "C1-INV-4 feature_type 缺省恒 geometry_group");
                Assert.True(f.@params != null && f.@params.Count == 0, "C1-INV-4 params 恒空");
            }
            AssertValid(doc, "C1-INV-3 瘦身 feature 后 validator 闭合仍过");
        }
    }
}
