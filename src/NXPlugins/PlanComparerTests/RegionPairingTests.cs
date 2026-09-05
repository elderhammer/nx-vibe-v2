// RegionPairingTests.cs — 区域配对 [U] 红线（v2.5 研究批，2026-09-06）
// 覆盖 RegionPairing.Judge 判据（分层统计配对）+ ComparerCore 区域维明细路径。
// 判别基线（regionclone/regionfull，2026-09-06 实证）：粒度差（±1 微区/2:1 合并）→ note/PASS；
// 结构真差（36 型 18 层×2 vs 118 型 118 层×1、3 vs 0）→ FAIL 不静默；面积漂移（stepover 等真实
// 残余哨兵）超 RelTol → FAIL。阈值初值待 [I] 真数据校准（comparer spec §7 变更纪律）。

using System;
using System.Collections.Generic;
using NXPlugins.PlanComparer;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanExporterTests
{
    public static class RegionPairingTests
    {
        // ---------- 夹具：层 × 列 区序列生成 ----------
        // cols: double[]{x, y, area}，每层全部列一区；层 = z0 + i*dz, i < nLayers

        private static List<RegionItem> Gen(int nLayers, double z0, double dz, double[][] cols)
        {
            var list = new List<RegionItem>();
            for (int i = 0; i < nLayers; i++)
            {
                double z = z0 + i * dz;
                foreach (double[] c in cols) list.Add(new RegionItem(c[0], c[1], z, c[2]));
            }
            return list;
        }

        private static readonly RegionPairOptions Opt = new RegionPairOptions();

        // 36 型（regionfull 实测 gt 本体：两孔带 18 层 × 2 区）
        private static readonly List<RegionItem> T36 = Gen(18, 76.5, 0.2,
            new[] { new[] { 70.909, -30.091, 2.47 }, new[] { 79.091, -30.091, 2.47 } });
        // 118 型（rebuilt：全层带 118 层 × 1 区）
        private static readonly List<RegionItem> T118 = Gen(118, 76.5, 0.2,
            new[] { new[] { 75.0, 38.98, 1410.68 } });

        public static void test_region_identical_pass()
        {
            RegionVerdict v = RegionPairing.Judge(T36, Clone(T36), Opt, 0.05);
            Assert.True(v.IsPass, "同区 → PASS: " + v.Detail);
        }

        public static void test_region_struct_diff_36_vs_118_fail()
        {
            // 判别基线：层数差（18 vs 118 层）→ FailStructure（OP-002 真差必须 FAIL，配对不得消化）
            RegionVerdict v = RegionPairing.Judge(T36, T118, Opt, 0.05);
            Assert.True(v.Kind == RegionVerdictKind.FailStructure, "36 型 vs 118 型 → FailStructure: " + v.Detail);
        }

        public static void test_region_struct_diff_3_vs_0_fail()
        {
            RegionVerdict v = RegionPairing.Judge(Gen(3, 76.5, 0.2, new[] { new[] { 75.0, 39.0, 100.0 } }),
                new List<RegionItem>(), Opt, 0.05);
            Assert.True(v.Kind == RegionVerdictKind.FailStructure, "3 区 vs 0 → FailStructure: " + v.Detail);
        }

        public static void test_region_granularity_small_extra_is_note()
        {
            // 粒度差型（OP-001 80/79 类比）：A 含 1 个面积占比 ~0.012% 的微区，B 无 → Note 不 FAIL
            List<RegionItem> a = Gen(40, 76.5, 0.2, new[] { new[] { 70.0, 0.0, 100.0 }, new[] { 75.0, 0.0, 100.0 } });
            a.Add(new RegionItem(77.0, 1.0, 76.5, 0.1));   // 81 区：首层 3 区（+1 微区 0.1），非独立层
            List<RegionItem> b = Gen(40, 76.5, 0.2, new[] { new[] { 70.0, 0.0, 100.0 }, new[] { 75.0, 0.0, 100.0 } });
            RegionVerdict v = RegionPairing.Judge(a, b, Opt, 0.05);
            Assert.True(v.Kind == RegionVerdictKind.Note, "微区粒度差 → Note（不 FAIL 不静默）: " + v.Detail);
        }

        public static void test_region_endlayer_granularity_diff_is_note()
        {
            // 003738 校准（OP-001 型）：层数差 1（端层收尾有/无）且差层面积占比小 → Note
            List<RegionItem> a = Gen(39, 76.5, 0.2, new[] { new[] { 75.0, 0.0, 100.0 } });   // 39 层×100
            a.Add(new RegionItem(75.0, 0.0, 84.3, 50.0));                                  // +收尾小层（占 1.27%）
            List<RegionItem> b = Gen(39, 76.5, 0.2, new[] { new[] { 75.0, 0.0, 100.0 } });
            RegionVerdict v = RegionPairing.Judge(a, b, Opt, 0.05);
            Assert.True(v.Kind == RegionVerdictKind.Note, "端层差 1 层（小面积收尾层）→ Note: " + v.Detail);
        }

        public static void test_region_layer_dist_drift_same_count_passes()
        {
            // 003738 校准（层距微差）：同层数、层距 0.200 vs 0.197（绝对 z 累积漂移）→ 按序配对 → Pass
            List<RegionItem> a = Gen(40, 76.5, 0.2, new[] { new[] { 75.0, 0.0, 100.0 } });
            List<RegionItem> b = new List<RegionItem>();
            for (int i = 0; i < 40; i++) b.Add(new RegionItem(75.0, 0.0, 76.5 + i * 0.197, 100.0));
            RegionVerdict v = RegionPairing.Judge(a, b, Opt, 0.05);
            Assert.True(v.IsPass, "层距微差同层数 → 按序配对 Pass: " + v.Detail);
        }

        public static void test_region_area_drift_fails_sentinel()
        {
            // 面积漂移哨兵：同结构每区 ×1.066（OP-001 stepover 残余类比）→ FailAreaDrift 保持可见
            List<RegionItem> b = new List<RegionItem>();
            foreach (RegionItem it in T36)
                b.Add(new RegionItem(it.Cx, it.Cy, it.Cz, it.Area * 1.066));
            RegionVerdict v = RegionPairing.Judge(T36, b, Opt, 0.05);
            Assert.True(v.Kind == RegionVerdictKind.FailAreaDrift, "6.6% 漂移 → FailAreaDrift（哨兵）: " + v.Detail);
        }

        public static void test_region_2to1_merge_passes()
        {
            // 2:1 合并粒度（stepover 驱动区融合）：A 每层 2 区(50+50) vs B 每层 1 区(100) → 合并 → Pass
            List<RegionItem> a = Gen(10, 76.5, 0.2, new[] { new[] { 70.0, 0.0, 50.0 }, new[] { 75.0, 0.0, 50.0 } });
            List<RegionItem> b = Gen(10, 76.5, 0.2, new[] { new[] { 72.5, 0.0, 100.0 } });
            RegionVerdict v = RegionPairing.Judge(a, b, Opt, 0.05);
            Assert.True(v.IsPass, "2:1 合并粒度 → Pass: " + v.Detail);
        }

        public static void test_region_2to1_mismatch_fails()
        {
            // 2:1 面积不匹配（真差，合并假设不成立）→ FailUnmatched
            List<RegionItem> a = Gen(10, 76.5, 0.2, new[] { new[] { 70.0, 0.0, 50.0 }, new[] { 75.0, 0.0, 50.0 } });
            List<RegionItem> b = Gen(10, 76.5, 0.2, new[] { new[] { 72.5, 0.0, 30.0 } });
            RegionVerdict v = RegionPairing.Judge(a, b, Opt, 0.05);
            Assert.True(v.Kind == RegionVerdictKind.FailUnmatched || v.Kind == RegionVerdictKind.FailStructure,
                "2:1 面积不匹配 → FAIL: " + v.Detail);
        }

        // ---------- ComparerCore 区域维明细路径（V2-POST-5 v2.5） ----------

        private static ExportSnapshot SnapWith(string name, List<RegionItem> regions)
        {
            var snap = new ExportSnapshot { Name = "t", InputRef = "x", CreatedAt = "2026-09-06T00:00:00+08:00" };
            snap.ProgramOrder.Add("A01");
            snap.Setups.Add(new SetupItem { Name = "MCS_MILL", McsOrigin = new double[] { 0, 0, 0 }, McsZAxis = new double[] { 0, 0, 1 }, McsXAxis = new double[] { 1, 0, 0 }, FixtureOffset = 1 });
            var op = new OperationItem
            {
                Name = name, TypeFamily = "Cavity Milling", ProgramParent = "A01", MethodParent = "METHOD",
                ToolParent = "", GeometryParent = "WORKPIECE", HasGeometryParent = true, Key = new TagKey(7),
            };
            foreach (RegionItem it in regions) op.RegionItems.Add(new RegionItem(it.Cx, it.Cy, it.Cz, it.Area));
            op.ToolpathTime = 1.0; op.ToolpathLength = 100.0;
            snap.Operations.Add(op);
            return snap;
        }

        public static void test_region_comparer_detail_path_pass()
        {
            ComparerResult r = CompareCore.Compare(SnapWith("CAV", T36), SnapWith("CAV", Clone(T36)));
            Assert.Equal(1, r.RegionChecks, "明细路径 = 1 check（配对判据）");
            Assert.Equal(1, r.RegionPass, "同型全过");
            Assert.Equal(0, CountCode(r, "REGION_DIFF"), "无 FAIL");
        }

        public static void test_region_comparer_detail_path_struct_fail()
        {
            ComparerResult r = CompareCore.Compare(SnapWith("CAV", T36), SnapWith("CAV", T118));
            Assert.Equal(1, CountCode(r, "REGION_DIFF"), "36 vs 118 → REGION_DIFF 1 条（配对判据合一）");
            Assert.Equal(0, r.RegionPass, "结构真差不 PASS");
        }

        public static void test_region_comparer_detail_path_drift_fail()
        {
            List<RegionItem> b = new List<RegionItem>();
            foreach (RegionItem it in T36)
                b.Add(new RegionItem(it.Cx, it.Cy, it.Cz, it.Area * 1.066));
            ComparerResult r = CompareCore.Compare(SnapWith("CAV", T36), SnapWith("CAV", b));
            Assert.Equal(1, CountCode(r, "REGION_DIFF"), "面积漂移哨兵 → REGION_DIFF");
        }

        private static int CountCode(ComparerResult r, string code)
        {
            int n = 0;
            foreach (ComparerIssue i in r.Issues) if (i.Code == code) n++;
            return n;
        }

        private static List<RegionItem> Clone(List<RegionItem> src)
        {
            var c = new List<RegionItem>();
            foreach (RegionItem it in src) c.Add(new RegionItem(it.Cx, it.Cy, it.Cz, it.Area));
            return c;
        }
    }
}
