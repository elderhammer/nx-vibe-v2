// V2GeomTests.cs — v2 几何重建 [U] 性质红线（2026-09-05，docs/nx-v2-geom-spec.md §3 V2-*）
// 含 ExecutorCore 签名解析/匹配 + Comparer 三维判据（自包含夹具，不依赖模块样例构造）。

using System;
using System.Collections.Generic;
using NXPlugins.PlanComparer;
using NXPlugins.PlanExporter;
using NXPlugins.PlanExecutor;

namespace NXPlugins.PlanExporterTests
{
    public static class V2GeomTests
    {
        // ---------- 夹具 ----------

        // F1 实测签名的 3 面子集（camprobe-v2face-A-033810 档内取值；0.01mm 粒度存储）
        private static readonly FaceSignature[] F1Three =
        {
            new FaceSignature { FaceType = 22, NormalAxis = "Z+", Rx = 75.00, Ry = -135.62, Rz = 100.00, Radius = 0 },
            new FaceSignature { FaceType = 22, NormalAxis = "Y+", Rx = 75.00, Ry = -90.25, Rz = 90.00, Radius = 0 },
            new FaceSignature { FaceType = 16, NormalAxis = "Z-", Rx = 80.00, Ry = -31.00, Rz = 78.25, Radius = 5 },
        };

        private static FaceSignatureJson ToJson(FaceSignature s)
        {
            return new FaceSignatureJson
            {
                face_type = s.FaceType, normal_axis = s.NormalAxis,
                rx = s.Rx, ry = s.Ry, rz = s.Rz, radius = s.Radius,
            };
        }

        private static PlanDocument PlanWithCavitySig(List<FaceSignatureJson> sigs)
        {
            var plan = new PlanDocument();
            plan.contract_version = "3.0";
            plan.plan_id = "P-v2";
            plan.setups.Add(new SetupJson { setup_id = "S-01", name = "MCS_MILL" });
            plan.resources.tools.Add(new ToolJson { tool_id = "T-001", type = "铣刀-5 参数", diameter = 10 });
            plan.operations.Add(new OperationJson
            {
                operation_id = "OP-001",
                operation_type = "milling",
                nx_template = new NxTemplateJson { type = "mill_contour", subtype = "CAVITY_MILL" },
                tool_ref = "T-001",
                method_ref = "MILL_ROUGH",
                cut_area_signatures = sigs,
                strategy = new Dictionary<string, ParamValue>(),
                technology = new Dictionary<string, ParamValue>(),
            });
            plan.workingsteps.Add(new WorkingstepJson
            {
                workingstep_id = "WS-01", feature_ref = "F-01", operation_ref = "OP-001", setup_ref = "S-01",
            });
            plan.workplan.root.name = "PROGRAM";
            plan.workplan.root.children.Add(new WorkplanNodeJson { kind = "workingstep", name = "CAV", @ref = "WS-01" });
            return plan;
        }

        private static OpCommand FirstOp(RebuildPlan r)
        {
            foreach (OpCommand c in r.Operations) return c;
            return null;
        }

        // ---------- V2-PRE-1/PRE-3：签名可选 + 空表行为 ----------

        // V2-PRE-1：合法签名列表解析入指令；无签名 = 兼容（腔 op 记 GEOM_SIG_ABSENT warning 不阻止）。
        public static void test_V2PRE1_signatures_optional_and_absent_diag()
        {
            var sigs = new List<FaceSignatureJson>();
            foreach (FaceSignature s in F1Three) sigs.Add(ToJson(s));
            RebuildPlan r = ExecutorCore.Build(PlanWithCavitySig(sigs));
            Assert.True(r.Ok, "带签名 plan 应 Ok");
            OpCommand c = FirstOp(r);
            Assert.NotNull(c, "应有 op 指令");
            Assert.Equal(3, c.Signatures.Count, "签名解析入指令 3 条");
            Assert.True(c.HasCutAreaSignatures, "HasCutAreaSignatures=true");

            RebuildPlan r2 = ExecutorCore.Build(PlanWithCavitySig(null));   // v1 旧形状
            Assert.True(r2.Ok, "无签名 plan 应 Ok（v1 兼容）");
            Assert.False(FirstOp(r2).HasCutAreaSignatures, "无签名 → 指令空");
            Assert.True(HasDiag(r2, "GEOM_SIG_ABSENT", "OP-001"), "腔 op 应记 GEOM_SIG_ABSENT");
        }

        // V2-PRE-2：值域违规（normal_axis 越界）→ GEOM_SIG_INVALID error + 该 op 不指派。
        public static void test_V2PRE2_invalid_signature_rejected()
        {
            var sigs = new List<FaceSignatureJson>();
            sigs.Add(new FaceSignatureJson { face_type = 22, normal_axis = "W+", rx = 1, ry = 1, rz = 1, radius = 0 });
            RebuildPlan r = ExecutorCore.Build(PlanWithCavitySig(sigs));
            Assert.False(FirstOp(r).HasCutAreaSignatures, "违规签名 → op 不指派");
            Assert.True(HasDiag(r, "GEOM_SIG_INVALID", "OP-001"), "应记 GEOM_SIG_INVALID");
            Assert.True(r.Ok, "单 op 违规不 fatal（其余指令照发）");
        }

        // V2-POST-2：匹配器 1:1 唯一命中（F1 三面）；缺面 → -1 未命中；body 侧重复 → 歧义计数。
        public static void test_V2POST2_matcher_unique_missing_ambiguous()
        {
            var bodySigs = new List<FaceSignature>();
            // body = 与 plan 同 3 面 + 2 个干扰面 + 1 个与 s1 同签名重复面（歧义制造）
            foreach (FaceSignature s in F1Three) bodySigs.Add(s);
            bodySigs.Add(new FaceSignature { FaceType = 22, NormalAxis = "X+", Rx = 5, Ry = 5, Rz = 5, Radius = 0 });
            bodySigs.Add(new FaceSignature { FaceType = 16, NormalAxis = "Y-", Rx = 9, Ry = 9, Rz = 9, Radius = 2 });
            int dupPos = bodySigs.Count;
            bodySigs.Add(new FaceSignature { FaceType = 22, NormalAxis = "Z+", Rx = 75.00, Ry = -135.62, Rz = 100.00, Radius = 0 });

            var planFaces = new List<FaceSignature>(F1Three);
            FaceMatchResult m = ExecutorCore.MatchSignatures(planFaces, bodySigs);
            Assert.Equal(0, m.MissingCount, "三面全命中");
            Assert.Equal(1, m.AmbiguousCount, "s1 在 body 侧重复 → 歧义计数 1");
            Assert.Equal(1, m.BodyIndexByPlanFace[1], "s2 唯一命中 body[1]");
            Assert.Equal(2, m.BodyIndexByPlanFace[2], "s3 唯一命中 body[2]");
            Assert.Equal(0, m.BodyIndexByPlanFace[0], "s1 取首候选（body[0]，重复面在 body[5]）");

            // 缺面：body 不含 s3
            var body2 = new List<FaceSignature> { F1Three[0], F1Three[1] };
            FaceMatchResult m2 = ExecutorCore.MatchSignatures(planFaces, body2);
            Assert.Equal(1, m2.MissingCount, "缺一面 → 未命中 1");
            Assert.Equal(-1, m2.BodyIndexByPlanFace[2], "缺面下标 -1");
        }

        // V2-INV-2：Key() 取整粒度稳定性——构造值（已取整）→ Key 幂等；round-trip 序列化不二次变形。
        public static void test_V2INV2_signature_key_stable_and_roundtrip()
        {
            foreach (FaceSignature s in F1Three)
            {
                FaceSignature copy = new FaceSignature
                {
                    FaceType = s.FaceType, NormalAxis = s.NormalAxis,
                    Rx = s.Rx, Ry = s.Ry, Rz = s.Rz, Radius = s.Radius,
                };
                Assert.Equal(s.Key(), copy.Key(), "Key 幂等（同值同键）");
            }
            var sigs = new List<FaceSignatureJson>();
            foreach (FaceSignature s in F1Three) sigs.Add(ToJson(s));
            PlanDocument p = PlanWithCavitySig(sigs);
            string json = new PlanJsonSerializer().Serialize(p);
            PlanDocument q = new PlanJsonSerializer().Deserialize(json);
            Assert.Equal(1, q.operations.Count, "序列化不吞 op");
            Assert.NotNull(q.operations[0].cut_area_signatures, "签名列表落盘并解回");
            Assert.Equal(3, q.operations[0].cut_area_signatures.Count, "签名 3 条 round-trip");
            for (int i = 0; i < 3; i++)
            {
                FaceSignatureJson a = sigs[i], b = q.operations[0].cut_area_signatures[i];
                Assert.Equal(a.face_type + "|" + a.normal_axis + "|" + a.rx + "," + a.ry + "," + a.rz + "|" + a.radius,
                    b.face_type + "|" + b.normal_axis + "|" + b.rx + "," + b.ry + "," + b.rz + "|" + b.radius,
                    "签名值 round-trip 无损（V2-INV-2）");
            }
        }

        // ---------- Comparer 三维（V2-POST-4/5/6） ----------

        private static OperationItem OpWithV2(string name, double? tpTime, double? tpLen,
            int? regCnt, double? regArea, params FaceSignature[] sigs)
        {
            var o = new OperationItem { Name = name };
            o.ToolpathTime = tpTime;
            o.ToolpathLength = tpLen;
            o.RegionCount = regCnt;
            o.RegionAreaSum = regArea;
            foreach (FaceSignature s in sigs) o.CutAreaSignatures.Add(s);
            return o;
        }

        private static ComparerResult CompareOps(OperationItem a, OperationItem b)
        {
            var sa = new ExportSnapshot();
            sa.Operations.Add(a);
            var sb = new ExportSnapshot();
            sb.Operations.Add(b);
            return CompareCore.Compare(sa, sb);
        }

        // V2-POST-4：刀路 time/length 双判据——同值 PASS；恰 1 变异 → TOOLPATH_DIFF；单侧缺 → FAIL。
        public static void test_V2POST4_toolpath_dim()
        {
            ComparerResult ok = CompareOps(
                OpWithV2("CAV", 58.3195879, 118746.82, null, null),
                OpWithV2("CAV", 58.3195879, 118746.82, null, null));
            Assert.Equal(2, ok.ToolpathChecks, "time+length 两 check");
            Assert.Equal(2, ok.ToolpathPass, "同值全过");

            ComparerResult var = CompareOps(
                OpWithV2("CAV", 58.3, 118746.82, null, null),
                OpWithV2("CAV", 543.4, 118746.82, null, null));
            Assert.Equal(1, CountCode(var, "TOOLPATH_DIFF"), "time 变异 → 恰 1 FAIL");

            ComparerResult miss = CompareOps(
                OpWithV2("CAV", 58.3, null, null, null),
                OpWithV2("CAV", null, null, null, null));
            Assert.Equal(1, CountCode(miss, "TOOLPATH_DIFF"), "单侧缺失 → FAIL 不静默");
        }

        // V2-POST-5：区域计数（int 等）+ 面积和（双判据）。
        public static void test_V2POST5_region_dim()
        {
            ComparerResult ok = CompareOps(
                OpWithV2("CAV", null, null, 80, 671095.64),
                OpWithV2("CAV", null, null, 80, 671095.64));
            Assert.Equal(2, ok.RegionChecks, "区数+面积和两 check");
            Assert.Equal(2, ok.RegionPass, "同值全过");

            ComparerResult var = CompareOps(
                OpWithV2("CAV", null, null, 80, 671095.64),
                OpWithV2("CAV", null, null, 242, 671095.64));
            Assert.Equal(1, CountCode(var, "REGION_DIFF"), "区数变异 → FAIL");
        }

        // V2-POST-6：签名面集差——双侧同 3 → SigPass；B 删 1 → SIG_FACE_DIFF（A-only=1）。
        public static void test_V2POST6_signature_faceset_dim()
        {
            ComparerResult ok = CompareOps(
                OpWithV2("CAV", null, null, null, null, F1Three),
                OpWithV2("CAV", null, null, null, null, F1Three));
            Assert.Equal(1, ok.SigChecks, "签名集一 check");
            Assert.Equal(1, ok.SigPass, "同集全过");

            var bOnly = new[] { F1Three[0], F1Three[1] };
            ComparerResult diff = CompareOps(
                OpWithV2("CAV", null, null, null, null, F1Three),
                OpWithV2("CAV", null, null, null, null, bOnly));
            Assert.Equal(1, CountCode(diff, "SIG_FACE_DIFF"), "删 1 面 → FAIL");
            Assert.Equal(0, diff.SigPass, "SigPass 不增");
        }

        private static bool HasDiag(RebuildPlan r, string code, string scope)
        {
            foreach (RebuildDiag d in r.Diagnostics)
                if (d.Code == code && d.Scope == scope) return true;
            return false;
        }

        private static int CountCode(ComparerResult r, string code)
        {
            int n = 0;
            foreach (ComparerIssue i in r.Issues)
                if (i.Code == code) n++;
            return n;
        }
    }
}
