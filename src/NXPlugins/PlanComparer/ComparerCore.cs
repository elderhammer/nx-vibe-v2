// ComparerCore.cs — 两件 ExportSnapshot 的维度对比核心（纯逻辑，无 NX 依赖）
// 性质来源：docs/nx-plan-comparer-spec.md §3（PRE-C1..POST-C7、INV-C1..C4、MONO-C1；§4 算法映射）。
// A=gt 手编件快照，B=重建件快照（方向语义影响 missing/extra）。只读不改写输入（INV-C2）。

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanComparer
{
    public static class CompareCore
    {
        /// <summary>A=gt 手编件快照，B=重建件快照（方向影响 missing/extra 含义）。opt 缺省用决策④默认。</summary>
        public static ComparerResult Compare(ExportSnapshot a, ExportSnapshot b, ComparerOptions opt = null)
        {
            if (a == null) throw new ArgumentNullException("a");   // PRE-C1
            if (b == null) throw new ArgumentNullException("b");
            if (opt == null) opt = new ComparerOptions();
            var r = new ComparerResult();
            r.ToolsA = a.Tools.Count; r.ToolsB = b.Tools.Count;
            r.SetupsA = a.Setups.Count; r.SetupsB = b.Setups.Count;

            // ---- PRE-C1：元素合法性 ----
            foreach (OperationItem o in a.Operations)
                if (string.IsNullOrEmpty(o.Name)) throw new ArgumentException("A 快照含空名 op（PRE-C1）");
            foreach (OperationItem o in b.Operations)
                if (string.IsNullOrEmpty(o.Name)) throw new ArgumentException("B 快照含空名 op（PRE-C1）");

            // ---- A1 索引与 DUP（INV-C1） ----
            var dupNames = new HashSet<string>();          // 任一侧出现 >1 的名
            var aNameCount = new Dictionary<string, int>();
            var bNameCount = new Dictionary<string, int>();
            foreach (OperationItem o in a.Operations) Inc(aNameCount, o.Name);
            foreach (OperationItem o in b.Operations) Inc(bNameCount, o.Name);
            foreach (KeyValuePair<string, int> kv in aNameCount)
                if (kv.Value > 1 || (bNameCount.ContainsKey(kv.Key) && bNameCount[kv.Key] > 1))
                    dupNames.Add(kv.Key);
            foreach (KeyValuePair<string, int> kv in bNameCount)
                if (kv.Value > 1) dupNames.Add(kv.Key);
            foreach (string dup in dupNames)
                AddIssue(r, dup, "DUP_NAME", "op 名双侧非唯一（该名下全部实例不参与配对）: " + dup);

            // ---- A2 配对（名键；dup 不配；单侧 → 结构条目 POST-C6） ----
            var aByName = new Dictionary<string, OperationItem>();
            foreach (OperationItem o in a.Operations)
                if (!dupNames.Contains(o.Name) && !aByName.ContainsKey(o.Name)) aByName[o.Name] = o;
            var matched = new List<KeyValuePair<OperationItem, OperationItem>>();   // A→B
            var bMatchedNames = new HashSet<string>();
            foreach (OperationItem o in b.Operations)
            {
                if (dupNames.Contains(o.Name)) continue;
                OperationItem aOp;
                if (!aByName.TryGetValue(o.Name, out aOp)) { r.OpsExtra++; AddIssue(r, o.Name, "OP_STRUCT", "重建多余 op（仅 B 有）: " + o.Name); continue; }
                matched.Add(new KeyValuePair<OperationItem, OperationItem>(aOp, o));
                bMatchedNames.Add(o.Name);
            }
            foreach (OperationItem o in a.Operations)
                if (!dupNames.Contains(o.Name) && !bMatchedNames.Contains(o.Name))
                { r.OpsMissing++; AddIssue(r, o.Name, "OP_STRUCT", "重建缺失 op（仅 A 有）: " + o.Name); }
            r.OpsMatched = matched.Count;

            // ---- POST-C7：名序（非 dup 名，各自采集序） ----
            var seqA = new List<string>(); var seqB = new List<string>();
            foreach (OperationItem o in a.Operations) if (!dupNames.Contains(o.Name)) seqA.Add(o.Name);
            foreach (OperationItem o in b.Operations) if (!dupNames.Contains(o.Name)) seqB.Add(o.Name);
            if (!SeqEqual(seqA, seqB)) AddIssue(r, "", "ORDER_SHIFT", "op 名集相同但采集序不同（刀路输出序语义差异）");

            // ---- A3 配对维度对比（POST-C1/C2） ----
            foreach (KeyValuePair<OperationItem, OperationItem> pair in matched)
                CompareOpPair(r, pair.Key, pair.Value, opt);

            // ---- A4 刀具序对（POST-C3） ----
            int toolPairs = Math.Min(a.Tools.Count, b.Tools.Count);
            for (int i = 0; i < toolPairs; i++) CompareToolPair(r, a.Tools[i], b.Tools[i], i + 1, opt);
            if (a.Tools.Count != b.Tools.Count)
                AddIssue(r, "tools", "TOOL_STRUCT",
                    "刀具数差: A=" + a.Tools.Count + " B=" + b.Tools.Count
                    + (a.Tools.Count > b.Tools.Count ? "（重建缺 " + (a.Tools.Count - b.Tools.Count) + "）" : "（重建多 " + (b.Tools.Count - a.Tools.Count) + "）"));

            // ---- A5 setup 名对（POST-C4） ----
            var bSetupByName = new Dictionary<string, SetupItem>();
            foreach (SetupItem s in b.Setups) if (!bSetupByName.ContainsKey(s.Name)) bSetupByName[s.Name] = s;
            foreach (SetupItem sA in a.Setups)
            {
                SetupItem sB;
                if (!bSetupByName.TryGetValue(sA.Name, out sB))
                { AddIssue(r, "setup:" + sA.Name, "SETUP_STRUCT", "setup 仅 A 有（重建缺失）: " + sA.Name); continue; }
                CompareSetupPair(r, sA, sB, opt);
                bSetupByName.Remove(sA.Name);
            }
            foreach (KeyValuePair<string, SetupItem> kv in bSetupByName)
                AddIssue(r, "setup:" + kv.Key, "SETUP_STRUCT", "setup 仅 B 有（重建多余）: " + kv.Key);

            // ---- A6 顶层组序（POST-C6） ----
            if (!SeqEqual(a.ProgramOrder, b.ProgramOrder))
                AddIssue(r, "", "PROGRAM_ORDER_DIFF",
                    "顶层程序组序差: A=[" + string.Join("/", a.ProgramOrder.ToArray()) + "] B=[" + string.Join("/", b.ProgramOrder.ToArray()) + "]");

            return r;
        }

        // ---------- 维度对比 ----------

        private static void CompareOpPair(ComparerResult r, OperationItem a, OperationItem b, ComparerOptions opt)
        {
            // POST-C2：模板对（WhiteList.Resolve 归一；两侧同语言由采集纪律保证）
            TemplateResolution ra = WhiteList.Resolve(a.TypeFamily);
            TemplateResolution rb = WhiteList.Resolve(b.TypeFamily);
            r.TemplateChecks++;
            string pairA = ra.Pair == null ? "(无)" : ra.Pair.ToString();
            string pairB = rb.Pair == null ? "(无)" : rb.Pair.ToString();
            if (ra.Pair == null || rb.Pair == null
                || ra.Pair.Type != rb.Pair.Type || ra.Pair.Subtype != rb.Pair.Subtype)
                AddIssue(r, a.Name, "OP_TEMPLATE_DIFF",
                    "模板对失配（家族 " + a.TypeFamily + " vs " + b.TypeFamily + "）: " + pairA + " vs " + pairB);
            else r.TemplatePass++;

            // POST-C1 / V15-POST-3：Params 键并集逐键按 kind 判据——双侧同形（N/N → 双判据回归；S/S →
            // ordinal equality）；形异/单侧缺失 → FAIL 不静默。kind 由采集侧按键固定（同采集面两侧同形）。
            var keys = new HashSet<string>();
            foreach (KeyValuePair<string, ParamValue> kv in a.Params) keys.Add(kv.Key);
            foreach (KeyValuePair<string, ParamValue> kv in b.Params) keys.Add(kv.Key);
            foreach (string k in keys)
            {
                bool aHas = a.Params.ContainsKey(k), bHas = b.Params.ContainsKey(k);
                if (!aHas || !bHas)
                {
                    r.ParamChecks++;
                    AddIssue(r, a.Name, "OP_PARAM_DIFF",
                        "参数 " + k + " 单侧缺失: A=" + (aHas ? Fmt(a.Params[k]) : "(无)") + " B=" + (bHas ? Fmt(b.Params[k]) : "(无)"));
                    continue;
                }
                ParamValue pva = a.Params[k], pvb = b.Params[k];
                bool aN = pva.N.HasValue, bN = pvb.N.HasValue;
                bool aS = pva.S != null, bS = pvb.S != null;
                if (aN && bN)
                {
                    double abs = Math.Abs(pva.N.Value - pvb.N.Value);
                    r.ParamChecks++;
                    if (Passes(abs, pva.N.Value, pvb.N.Value, opt)) { r.ParamPass++; continue; }
                    AddIssue(r, a.Name, "OP_PARAM_DIFF",
                        "参数 " + k + ": A=" + Fmt(pva.N.Value) + " B=" + Fmt(pvb.N.Value) + " |差|=" + Fmt(abs)
                        + (abs > opt.EpsLen ? "（超绝对容差 " + opt.EpsLen + "）" : "（超相对容差 " + opt.RelTol + "）"),
                        abs);
                    continue;
                }
                if (aS && bS)
                {
                    r.ParamChecks++;
                    if (pva.S == pvb.S) { r.ParamPass++; continue; }
                    AddIssue(r, a.Name, "OP_PARAM_DIFF",
                        "参数 " + k + "（枚举）: A=" + pva.S + " B=" + pvb.S, 1.0);
                    continue;
                }
                // 形异（N vs S 等）→ 显式 FAIL
                r.ParamChecks++;
                AddIssue(r, a.Name, "OP_PARAM_DIFF",
                    "参数 " + k + " 值形不一致: A=" + Fmt(pva) + " B=" + Fmt(pvb), 1.0);
            }

            // v2 三维（nx-v2-geom-spec V2-POST-4/5/6）：刀路 time/length、区域摘要、签名面集差
            CompareV2(r, a, b, opt);
        }

        // ---------- v2 维度（2026-09-05，nx-v2-geom-spec §3 V2-POST-4/5/6） ----------

        private static void CompareV2(ComparerResult r, OperationItem a, OperationItem b, ComparerOptions opt)
        {
            // 刀路维（V2-POST-4）：双侧值双判据沿 v1（EpsLen/RelTol）；单侧缺 → FAIL 不静默
            if (a.ToolpathTime.HasValue || b.ToolpathTime.HasValue)
            {
                r.ToolpathChecks++;
                if (!a.ToolpathTime.HasValue || !b.ToolpathTime.HasValue)
                    AddIssue(r, a.Name, "TOOLPATH_DIFF",
                        "刀路时间单侧缺失: A=" + (a.ToolpathTime.HasValue ? Fmt(a.ToolpathTime.Value) : "(未生成)")
                        + " B=" + (b.ToolpathTime.HasValue ? Fmt(b.ToolpathTime.Value) : "(未生成)"));
                else if (NumPass(a.ToolpathTime.Value, b.ToolpathTime.Value, opt)) r.ToolpathPass++;
                else AddIssue(r, a.Name, "TOOLPATH_DIFF",
                    "刀路时间: A=" + Fmt(a.ToolpathTime.Value) + " B=" + Fmt(b.ToolpathTime.Value)
                    + " 相对偏差=" + RelDiff(a.ToolpathTime.Value, b.ToolpathTime.Value).ToString("0.0%") + "（超容差）",
                    Math.Abs(a.ToolpathTime.Value - b.ToolpathTime.Value));
            }
            if (a.ToolpathLength.HasValue || b.ToolpathLength.HasValue)
            {
                r.ToolpathChecks++;
                if (!a.ToolpathLength.HasValue || !b.ToolpathLength.HasValue)
                    AddIssue(r, a.Name, "TOOLPATH_DIFF",
                        "刀路长度单侧缺失: A=" + (a.ToolpathLength.HasValue ? Fmt(a.ToolpathLength.Value) : "(未生成)")
                        + " B=" + (b.ToolpathLength.HasValue ? Fmt(b.ToolpathLength.Value) : "(未生成)"));
                else if (NumPass(a.ToolpathLength.Value, b.ToolpathLength.Value, opt)) r.ToolpathPass++;
                else AddIssue(r, a.Name, "TOOLPATH_DIFF",
                    "刀路长度: A=" + Fmt(a.ToolpathLength.Value) + " B=" + Fmt(b.ToolpathLength.Value)
                    + " 相对偏差=" + RelDiff(a.ToolpathLength.Value, b.ToolpathLength.Value).ToString("0.0%") + "（超容差）",
                    Math.Abs(a.ToolpathLength.Value - b.ToolpathLength.Value));
            }

            // 区域维（V2-POST-5）：区数（int 等）+ 面积和（双判据）；单侧缺 → FAIL
            if (a.RegionCount.HasValue || b.RegionCount.HasValue)
            {
                r.RegionChecks++;
                if (!a.RegionCount.HasValue || !b.RegionCount.HasValue)
                    AddIssue(r, a.Name, "REGION_DIFF",
                        "区域计数单侧缺失: A=" + (a.RegionCount.HasValue ? a.RegionCount.Value.ToString() : "(无)")
                        + " B=" + (b.RegionCount.HasValue ? b.RegionCount.Value.ToString() : "(无)"));
                else if (a.RegionCount.Value == b.RegionCount.Value) r.RegionPass++;
                else AddIssue(r, a.Name, "REGION_DIFF",
                    "区域数: A=" + a.RegionCount.Value + " B=" + b.RegionCount.Value,
                    Math.Abs(a.RegionCount.Value - b.RegionCount.Value));
            }
            if (a.RegionAreaSum.HasValue || b.RegionAreaSum.HasValue)
            {
                r.RegionChecks++;
                if (!a.RegionAreaSum.HasValue || !b.RegionAreaSum.HasValue)
                    AddIssue(r, a.Name, "REGION_DIFF",
                        "区域面积和单侧缺失: A=" + (a.RegionAreaSum.HasValue ? Fmt(a.RegionAreaSum.Value) : "(无)")
                        + " B=" + (b.RegionAreaSum.HasValue ? Fmt(b.RegionAreaSum.Value) : "(无)"));
                else if (NumPass(a.RegionAreaSum.Value, b.RegionAreaSum.Value, opt)) r.RegionPass++;
                else AddIssue(r, a.Name, "REGION_DIFF",
                    "区域面积和: A=" + Fmt(a.RegionAreaSum.Value) + " B=" + Fmt(b.RegionAreaSum.Value)
                    + " 相对偏差=" + RelDiff(a.RegionAreaSum.Value, b.RegionAreaSum.Value).ToString("0.0%"),
                    Math.Abs(a.RegionAreaSum.Value - b.RegionAreaSum.Value));
            }

            // 签名面集维（V2-POST-6）：双侧 cut-area 签名集差集（Key 多集）；双侧齐才比
            if (a.CutAreaSignatures.Count > 0 || b.CutAreaSignatures.Count > 0)
            {
                r.SigChecks++;
                Dictionary<string, int> aKeys = new Dictionary<string, int>();
                foreach (NXPlugins.PlanExporter.FaceSignature s in a.CutAreaSignatures) IncKey(aKeys, s.Key());
                Dictionary<string, int> bKeys = new Dictionary<string, int>();
                foreach (NXPlugins.PlanExporter.FaceSignature s in b.CutAreaSignatures) IncKey(bKeys, s.Key());
                int aOnly = 0, bOnly = 0;
                foreach (KeyValuePair<string, int> kv in aKeys)
                {
                    int cb;
                    if (!bKeys.TryGetValue(kv.Key, out cb)) aOnly += kv.Value;
                    else if (cb < kv.Value) aOnly += kv.Value - cb;
                }
                foreach (KeyValuePair<string, int> kv in bKeys)
                {
                    int ca;
                    if (!aKeys.TryGetValue(kv.Key, out ca)) bOnly += kv.Value;
                    else if (ca < kv.Value) bOnly += kv.Value - ca;
                }
                if (aOnly == 0 && bOnly == 0) r.SigPass++;
                else AddIssue(r, a.Name, "SIG_FACE_DIFF",
                    "cut-area 签名面集差: A-only=" + aOnly + " B-only=" + bOnly
                    + "（A=" + a.CutAreaSignatures.Count + " B=" + b.CutAreaSignatures.Count + "）");
            }
        }

        private static void IncKey(Dictionary<string, int> d, string k)
        {
            int n;
            if (d.TryGetValue(k, out n)) d[k] = n + 1;
            else d[k] = 1;
        }

        /// <summary>沿 v1 双判据（EpsLen 或 RelTol），无 AbsDiff 包装版（time/length/面积数值）。</summary>
        private static bool NumPass(double va, double vb, ComparerOptions opt)
        {
            double abs = Math.Abs(va - vb);
            if (abs <= opt.EpsLen) return true;
            double rel = RelDiff(va, vb);
            return rel <= opt.RelTol;
        }

        private static double RelDiff(double va, double vb)
        {
            double m = Math.Max(Math.Abs(va), Math.Abs(vb));
            if (m < 1e-9) return 0;
            return Math.Abs(va - vb) / m;
        }

        private static void CompareToolPair(ComparerResult r, ToolItem a, ToolItem b, int idx, ComparerOptions opt)
        {
            string key = "tool#" + idx;
            r.ToolChecks++;
            bool ok = true;
            // 类型键：NxType/NxSubtype 优先、TypeFamily 兜底（D-2 时代资产可比）
            string ta = TypeKey(a), tb = TypeKey(b);
            if (ta != tb)
            {
                ok = false;
                AddIssue(r, key, "TOOL_TYPE_DIFF", "刀具类型失配: A=" + ta + " B=" + tb);
            }
            // 数值：双侧非 null 字段双判据（单侧 null → 缺读处理：显式差异）
            ok = CompareNum(r, key, "直径", a.Diameter, b.Diameter, opt) && ok;
            ok = CompareNum(r, key, "刃数", a.NumFlutes, b.NumFlutes, opt) && ok;
            ok = CompareNum(r, key, "刃长", a.FluteLength, b.FluteLength, opt) && ok;
            ok = CompareNum(r, key, "圆角", a.LowerCornerRadius, b.LowerCornerRadius, opt) && ok;
            if (a.Name != b.Name)
                r.Notes.Add("tool#" + idx + " 名差（非致命）: A=" + a.Name + " B=" + b.Name);
            if (ok) r.ToolPass++;
        }

        private static void CompareSetupPair(ComparerResult r, SetupItem a, SetupItem b, ComparerOptions opt)
        {
            string key = "setup:" + a.Name;
            if (a.MissingMcs || b.MissingMcs)
            {
                AddIssue(r, key, "READ_MISSING",
                    "MCS 单侧缺读（无法对比）: A=" + (a.MissingMcs ? "缺" : "有") + " B=" + (b.MissingMcs ? "缺" : "有"));
                return;
            }
            // origin 欧氏距离（POST-C4）
            r.McsChecks++;
            bool pass = true;
            if (a.McsOrigin == null || b.McsOrigin == null)
            {
                AddIssue(r, key, "READ_MISSING", "MCS origin 单侧未读");
                return;
            }
            double dist = Dist(a.McsOrigin, b.McsOrigin);
            if (dist > opt.EpsLen)
            {
                pass = false;
                AddIssue(r, key, "MCS_DIFF", "origin 欧氏差=" + Fmt(dist) + "mm（容差 " + opt.EpsLen + "）", dist);
            }
            if (!AxisOk(a.McsZAxis, b.McsZAxis, opt.EpsAxis))
            {
                pass = false;
                AddIssue(r, key, "MCS_DIFF", "z_axis 元素差超 " + opt.EpsAxis + "（回读口径 row2=Z）");
            }
            if (!AxisOk(a.McsXAxis, b.McsXAxis, opt.EpsAxis))
            {
                pass = false;
                AddIssue(r, key, "MCS_DIFF", "x_axis 元素差超 " + opt.EpsAxis + "（回读口径 row0=X）");
            }
            if (pass) r.McsPass++;
            // fixture（双侧显式才比；缺省继承值不判差——fixture 默认语义由 NX 侧给）
            if (a.FixtureOffset != null && b.FixtureOffset != null)
            {
                r.FixtureChecks++;
                if (a.FixtureOffset == b.FixtureOffset) r.FixturePass++;
                else AddIssue(r, key, "FIXTURE_DIFF", "fixture_offset: A=" + a.FixtureOffset + " B=" + b.FixtureOffset);
            }
        }

        // ---------- 工具 ----------

        private static bool CompareNum(ComparerResult r, string key, string field, double? a, double? b, ComparerOptions opt)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null)
            {
                AddIssue(r, key, "TOOL_PARAM_DIFF", field + " 单侧未读: A=" + (a.HasValue ? Fmt(a.Value) : "(无)") + " B=" + (b.HasValue ? Fmt(b.Value) : "(无)"));
                return false;
            }
            double abs = Math.Abs(a.Value - b.Value);
            if (Passes(abs, a.Value, b.Value, opt)) return true;
            AddIssue(r, key, "TOOL_PARAM_DIFF",
                field + ": A=" + Fmt(a.Value) + " B=" + Fmt(b.Value) + " |差|=" + Fmt(abs), abs);
            return false;
        }

        private static bool Passes(double abs, double va, double vb, ComparerOptions opt)
        {
            if (abs <= opt.EpsLen) return true;                       // 绝对容差（含浮点回读噪声）
            double denom = Math.Max(Math.Max(Math.Abs(va), Math.Abs(vb)), 1e-9);
            return abs / denom <= opt.RelTol;                         // 相对偏差
        }

        private static string TypeKey(ToolItem t)
        {
            if (!string.IsNullOrEmpty(t.NxType)) return t.NxType + "|" + (t.NxSubtype ?? "");
            return "FAM:" + (t.TypeFamily ?? "");
        }

        private static bool AxisOk(double[] x, double[] y, double eps)
        {
            if (x == null || y == null) return x == null && y == null;
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (Math.Abs(x[i] - y[i]) > eps) return false;
            return true;
        }

        private static double Dist(double[] x, double[] y)
        {
            double s = 0;
            int n = Math.Min(x.Length, y.Length);
            for (int i = 0; i < n; i++) s += (x[i] - y[i]) * (x[i] - y[i]);
            return Math.Sqrt(s);
        }

        private static bool SeqEqual(List<string> x, List<string> y)
        {
            if (x == null || y == null) return x == null && y == null;
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
                if (x[i] != y[i]) return false;
            return true;
        }

        private static void Inc(Dictionary<string, int> m, string k)
        {
            if (!m.ContainsKey(k)) m[k] = 0;
            m[k]++;
        }

        private static string Fmt(double d) { return d.ToString("0.###"); }

        /// <summary>v1.5-③：联合值展示（数值 0.### / 枚举原文串）。</summary>
        private static string Fmt(ParamValue v)
        {
            if (v == null) return "(空)";
            if (v.N.HasValue) return Fmt(v.N.Value);
            if (v.S != null) return v.S;
            return "(空)";
        }

        private static void AddIssue(ComparerResult r, string key, string code, string detail, double? abs = null)
        {
            // INV-C4：同 key+code+detail 聚合一次
            foreach (ComparerIssue i in r.Issues)
                if (i.Key == key && i.Code == code && i.Detail == detail) return;
            r.Issues.Add(new ComparerIssue { Key = key, Code = code, Detail = detail, AbsDiff = abs });
        }
    }
}
