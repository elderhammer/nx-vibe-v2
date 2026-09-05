// RegionPairing.cs — 区域配对纯逻辑（v2.5 区域配对研究批，2026-09-06）
//
// 目标：comparer 区域维从"摘要计数/面积和"升级为"分层统计配对"——回答"差在哪"（层差/未配对区
// 位置/合并粒度/面积漂移），并把粒度噪音（OP-001 型 ±1 区）与结构真差（OP-002 18 层×2 vs 118 层×1、
// OP-003 3 vs 0）分开（regionfull/regionclone 判别基线，2026-09-06）。
//
// 口径边界（诚实声明）：
//   * 区域明细源 = CutRegionsData（NX_NO_DOC 内部 API，NX10.0.2/cam_base）——跨 NX 版本可能漂移；
//   * 配对 = 分层统计性对齐（区域天然分层：层距 ≈ DPC，regionfull 实证 z 等距），**非区身份追踪**
//     （内部面无区 ID）；层内区少（≤3 常见），用贪心最近 + 2:1 合并检测，不做通用分配。
//   * 判据阈值（NoteAreaRatio/MergeRelTol/ZTol）为初值，**待 [I] 真数据校准**——调整按
//     nx-plan-comparer-spec §7 变更纪律留痕；哨兵（面积漂移超 RelTol 须 FAIL）不因阈值放宽掩盖。
//
// 判据三态（Judge）：
//   Pass      —— 层数同 + 无未配对区（或未配对面积占比 ≤ NoteAreaRatio）+ 配对面积漂移 ≤ RelTol
//   Note      —— 粒度差：未配对面积占比小（±1 区/2:1 合并）→ note 不 FAIL（不静默，detail 含位置）
//   Fail      —— 结构差：层数不同（FailStructure）/未配对占比大（FailUnmatched）/配对面积漂移超 RelTol
//                （FailAreaDrift——哨兵：stepover 等真实残余保持可见）

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanComparer
{
    /// <summary>配对阈值（初值待 [I] 校准；§7 变更纪律）。</summary>
    public sealed class RegionPairOptions
    {
        public double ZTol = 0.05;        // 层聚类/层对齐容差 mm（层距 ≈ DPC 0.2，半距安全）
        public double NoteAreaRatio = 0.02; // 未配对面积占比较小 → note（粒度差）阈值
        public double MergeRelTol = 0.2;    // 2:1 合并候选的面积和相对容差
    }

    public enum RegionVerdictKind { Pass, Note, FailStructure, FailUnmatched, FailAreaDrift }

    public sealed class RegionVerdict
    {
        public RegionVerdictKind Kind;
        public string Detail = "";          // 人类可读（层数/未配对位置/漂移%）
        public bool IsPass { get { return Kind == RegionVerdictKind.Pass || Kind == RegionVerdictKind.Note; } }
    }

    public sealed class RegionLayer
    {
        public readonly double Z;                        // 层代表 z（首区）
        public readonly List<RegionItem> Items = new List<RegionItem>();
        public RegionLayer(double z) { Z = z; }
    }

    public sealed class RegionPairResult
    {
        public readonly List<RegionLayer> LevelsA = new List<RegionLayer>();
        public readonly List<RegionLayer> LevelsB = new List<RegionLayer>();
        public int MatchedLevels = 0;                     // 层级 1:1 匹配数
        public readonly List<RegionItem> AOnly = new List<RegionItem>();   // 未配对区（A 侧）
        public readonly List<RegionItem> BOnly = new List<RegionItem>();
        public int MergePairs = 0;                        // 2:1 合并粒度对（note 级）
        public double PairedAreaA = 0, PairedAreaB = 0;   // 配对区面积和（漂移判据原料）
        public double OnlyAreaA = 0, OnlyAreaB = 0;
    }

    public static class RegionPairing
    {
        /// <summary>按 z 聚类分层（z 排序后相邻差 ≤ ZTol 同层）。</summary>
        public static List<RegionLayer> ClusterLevels(List<RegionItem> items, double zTol)
        {
            var sorted = new List<RegionItem>(items);
            sorted.Sort((x, y) => x.Cz.CompareTo(y.Cz));
            var layers = new List<RegionLayer>();
            RegionLayer cur = null;
            double prevZ = double.NaN;
            foreach (RegionItem it in sorted)
            {
                if (cur == null || Math.Abs(it.Cz - prevZ) > zTol)
                {
                    cur = new RegionLayer(it.Cz);
                    layers.Add(cur);
                }
                cur.Items.Add(it);
                prevZ = it.Cz;
            }
            return layers;
        }

        /// <summary>分层统计配对（口径见文件头）。纯函数，无 NX 依赖。</summary>
        public static RegionPairResult Pair(List<RegionItem> a, List<RegionItem> b, RegionPairOptions o)
        {
            var res = new RegionPairResult();
            res.LevelsA.AddRange(ClusterLevels(a, o.ZTol));
            res.LevelsB.AddRange(ClusterLevels(b, o.ZTol));
            // 层配对 = 排序后按序对齐（003738 校准：绝对 z 搜索在两侧层距微差下累积漂移失效——
            // gt 层距 0.1944 vs reb 0.1991，实测 OP-001 只配上前 46/80 层；层序才是稳定配对键）
            int nL = Math.Min(res.LevelsA.Count, res.LevelsB.Count);
            for (int i = 0; i < nL; i++)
            {
                res.MatchedLevels++;
                PairLayer(res.LevelsA[i].Items, res.LevelsB[i].Items, o, res);
            }
            for (int i = nL; i < res.LevelsA.Count; i++)
                foreach (RegionItem it in res.LevelsA[i].Items) res.AOnly.Add(it);
            for (int j = nL; j < res.LevelsB.Count; j++)
                foreach (RegionItem it in res.LevelsB[j].Items) res.BOnly.Add(it);
            foreach (RegionItem it in res.AOnly) res.OnlyAreaA += it.Area;
            foreach (RegionItem it in res.BOnly) res.OnlyAreaB += it.Area;
            return res;
        }

        // 层内配对（口径 = 统计对齐非身份追踪）：
        //   1) 2:1 合并候选优先（面积和容差 MergeRelTol——stepover 驱动区融合的粒度表达）；
        //   2) 通用贪心 1:1 配 min(la,lb) 对（x/y 最近）；
        //   3) 剩余区 → 未配对（面积占比由 Judge 判 note/fail）。
        private static void PairLayer(List<RegionItem> la, List<RegionItem> lb, RegionPairOptions o, RegionPairResult res)
        {
            // 2:1 合并候选
            if (la.Count == 2 && lb.Count == 1)
            {
                if (Math.Abs(la[0].Area + la[1].Area - lb[0].Area) <= o.MergeRelTol * lb[0].Area)
                { res.MergePairs++; res.PairedAreaA += la[0].Area + la[1].Area; res.PairedAreaB += lb[0].Area; return; }
            }
            else if (la.Count == 1 && lb.Count == 2)
            {
                if (Math.Abs(lb[0].Area + lb[1].Area - la[0].Area) <= o.MergeRelTol * la[0].Area)
                { res.MergePairs++; res.PairedAreaA += la[0].Area; res.PairedAreaB += lb[0].Area + lb[1].Area; return; }
            }
            // 贪心 1:1（min 对；配不完的剩余单侧入 only）
            int n = Math.Min(la.Count, lb.Count);
            bool[] usedB = new bool[lb.Count];
            bool[] usedA = new bool[la.Count];
            for (int k = 0; k < n; k++)
            {
                int ai = -1, bi = -1;
                double best = double.MaxValue;
                for (int i = 0; i < la.Count; i++)
                {
                    if (usedA[i]) continue;
                    for (int j = 0; j < lb.Count; j++)
                    {
                        if (usedB[j]) continue;
                        double d = Dx2(la[i], lb[j]);
                        if (d < best) { best = d; ai = i; bi = j; }
                    }
                }
                if (ai < 0 || bi < 0) break;
                usedA[ai] = true; usedB[bi] = true;
                res.PairedAreaA += la[ai].Area; res.PairedAreaB += lb[bi].Area;
            }
            for (int i = 0; i < la.Count; i++) if (!usedA[i]) res.AOnly.Add(la[i]);
            for (int j = 0; j < lb.Count; j++) if (!usedB[j]) res.BOnly.Add(lb[j]);
        }

        /// <summary>判据（三态 + detail）。哨兵：配对面积漂移超 relTol → FailAreaDrift（不因粒度 note 掩盖）。</summary>
        public static RegionVerdict Judge(List<RegionItem> a, List<RegionItem> b, RegionPairOptions o, double relTol)
        {
            var v = new RegionVerdict();
            RegionPairResult res = Pair(a, b, o);
            // 结构差分级（003738 校准）：层数差（36 型 vs 118 型、3 vs 0）或空侧 → FailStructure；
            // 端层粒度差（OP-001 型：差 1 层且差层面积占比小 = 顶部收尾层有/无）→ Note 不 FAIL
            int la = res.LevelsA.Count, lb = res.LevelsB.Count;
            if (la != lb)
            {
                double onlyArea = res.OnlyAreaA + res.OnlyAreaB;
                double ratio = Math.Max(SumArea(a), SumArea(b)) > 0 ? onlyArea / Math.Max(SumArea(a), SumArea(b)) : 1;
                if (ratio <= o.NoteAreaRatio)
                {
                    v.Kind = RegionVerdictKind.Note;
                    v.Detail = "端层粒度差 note: A=" + la + " 层 B=" + lb + " 层（差层面积占比 "
                        + ratio.ToString("0.0%") + " ≤ " + o.NoteAreaRatio.ToString("0%") + "）";
                }
                else
                {
                    v.Kind = RegionVerdictKind.FailStructure;
                    v.Detail = "分层结构差: A=" + la + " 层 B=" + lb + " 层（A-only 区 " + res.AOnly.Count
                        + " / B-only 区 " + res.BOnly.Count + "，差层面积占比 " + ratio.ToString("0.0%") + "）";
                }
                return v;
            }
            if (la == 0 || lb == 0)
            {
                v.Kind = RegionVerdictKind.FailStructure;
                v.Detail = "分层结构差（空侧）: A=" + la + " 层 B=" + lb + " 层";
                return v;
            }
            // 未配对占比（粒度 vs 真差）
            double total = Math.Max(SumArea(a), SumArea(b));
            double only = res.OnlyAreaA + res.OnlyAreaB;
            if (only > 0)
            {
                double ratio = only / total;
                if (ratio > o.NoteAreaRatio)
                {
                    v.Kind = RegionVerdictKind.FailUnmatched;
                    v.Detail = "未配对区面积占比 " + ratio.ToString("0.0%") + " > note 阈值 " + o.NoteAreaRatio.ToString("0%")
                        + ": A-only " + res.AOnly.Count + " 区(面积 " + res.OnlyAreaA.ToString("0.###") + ")"
                        + " B-only " + res.BOnly.Count + " 区(面积 " + res.OnlyAreaB.ToString("0.###") + ")"
                        + (res.AOnly.Count > 0 ? " 例@" + res.AOnly[0] : "");
                    return v;
                }
                v.Kind = RegionVerdictKind.Note;
                v.Detail = "粒度差 note: 未配对面积占比 " + ratio.ToString("0.0%") + "（≤ " + o.NoteAreaRatio.ToString("0%")
                    + "）: A-only " + res.AOnly.Count + " 区 B-only " + res.BOnly.Count + " 区"
                    + (res.MergePairs > 0 ? " 含 2:1 合并 " + res.MergePairs + " 对" : "")
                    + (res.AOnly.Count > 0 ? " 例@" + res.AOnly[0] : "");
                return v;
            }
            // 配对面积漂移（哨兵：stepover 等真实残余须 FAIL 可见）
            if (res.PairedAreaA > 0 && res.PairedAreaB > 0)
            {
                double drift = Math.Abs(res.PairedAreaA - res.PairedAreaB) / Math.Max(res.PairedAreaA, res.PairedAreaB);
                if (drift > relTol)
                {
                    v.Kind = RegionVerdictKind.FailAreaDrift;
                    v.Detail = "配对区面积漂移 " + drift.ToString("0.0%") + " > RelTol " + relTol.ToString("0%")
                        + "（A=" + res.PairedAreaA.ToString("0.###") + " B=" + res.PairedAreaB.ToString("0.###") + "）";
                    return v;
                }
            }
            v.Kind = RegionVerdictKind.Pass;
            v.Detail = "层数 " + la + "=匹配, 全配对, 漂移 ≤ RelTol";
            return v;
        }

        private static double Dx2(RegionItem a, RegionItem b)
        {
            double dx = a.Cx - b.Cx, dy = a.Cy - b.Cy;
            return dx * dx + dy * dy;
        }

        private static double SumArea(List<RegionItem> items)
        {
            double s = 0;
            foreach (RegionItem it in items) s += it.Area;
            return s;
        }
    }
}
