// ComparerAdapter.cs — [I] 层集成验证：真实 NX 会话内跑通「两件 prt → NxCollect 双快照 →
// CompareCore → comparer-run-<ts>.txt 报告」（docs/nx-plan-comparer-spec.md §1 调用序列）
//
// 纯逻辑核心（PlanExporter/PlanExecutor/PlanComparer，无 NX 依赖）随本文件一起 csc 编译。
// 执行：干净 NX 会话（两件均未打开）→ File → Execute → NX Open → 本 exe。
// 产物：samples\comparer-run-<ts>.txt。
// ⚠️ I-1（[T]）：双 Part 同会话轮换纪律——A、B 各 OpenDisplay 一次（943006 已装载拒绝 Open*），
//   采集前 SetDisplayPart+SetWork 轮换到目标件；首跑点亮。
// 只读纪律：不 Commit/不修改/不保存两件源 prt（INV-C2 侧保证 + 评审）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.UF;
using NXOpen.Utilities;
using NXPlugins.PlanComparer;
using NXPlugins.PlanExporter;
using Path = System.IO.Path;

public class ComparerAdapter
{
    private const string DefaultPartA = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private const string DefaultPartB = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.rebuilt.prt";
    private static readonly object _logLock = new object();
    private static string _outPath = @"C:\Users\21505\Code\nx-vibe-v2\samples\comparer-run.txt";

    public static void Main(string[] args)
    {
        // NX Execute 对话框实况（2026-09-04 实证）：多参整体入 args[0]（引号含入，Path 非法字符异常）
        // → 单参形态 = args[0] 作 B（rebuilt）覆盖；A（gt）恒默认 test.prt。args[1]/args[2] 保留兼容旧多参。
        string partAPath = DefaultPartA;
        string partBPath = DefaultPartB;
        string b0 = TrimQuotes(args.Length > 0 ? args[0] : "");
        if (b0.Length > 0) partBPath = b0;
        else if (args.Length > 1 && !string.IsNullOrEmpty(args[1])) partBPath = TrimQuotes(args[1]);
        if (args.Length > 2 && !string.IsNullOrEmpty(args[2])) _outPath = args[2];
        _outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)),
            "comparer-run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== ComparerAdapter ==");
        try
        {
            Session theSession = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("A=" + partAPath + "  B=" + partBPath);

            // ---- 开 A → 轮换显示/工作 → 采集快照 A ----
            Part partA = OpenPart(theSession, uf, partAPath);
            if (partA == null) { Log("!! 打开 A 失败"); return; }
            Activate(uf, theSession.Parts, partA);
            Log("== 采集 A: " + partA.Name + " ==");
            var snapA = new ExportSnapshot
            {
                Name = partA.Name, InputRef = partAPath,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            };
            Collect(partA, snapA);

            // ---- 开 B → 轮换 → 采集快照 B（I-1 [T] 首跑点亮） ----
            Part partB = OpenPart(theSession, uf, partBPath);
            if (partB == null) { Log("!! 打开 B 失败"); return; }
            Activate(uf, theSession.Parts, partB);
            Log("== 采集 B: " + partB.Name + " ==");
            var snapB = new ExportSnapshot
            {
                Name = partB.Name, InputRef = partBPath,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            };
            Collect(partB, snapB);

            Log(string.Format("快照 A: ops={0} tools={1} setups={2}  快照 B: ops={3} tools={4} setups={5}",
                snapA.Operations.Count, snapA.Tools.Count, snapA.Setups.Count,
                snapB.Operations.Count, snapB.Tools.Count, snapB.Setups.Count));

            // 防呆护栏（2026-09-04 复跑教训：会话残留导致 A/B 取到同一件 → 自比废话中止）
            if (partA == partB)
            {
                Log("!! A 与 B 为同一部件（会话残留/取件级联拿错）——请干净会话（两件未开）重跑");
                Log("== 结束 ==");
                return;
            }

            // ---- CompareCore → 报告 ----
            ComparerResult r = CompareCore.Compare(snapA, snapB);
            Render(r);
            Log("== 结束 ==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
            Log("== 结束 ==");
        }
    }

    // ---- 单件取件（干净会话 OpenDisplay；已载 → UF 枚举复用；失败提示重开会话） ----

    private static Part OpenPart(Session theSession, UFSession uf, string partPath)
    {
        var parts = theSession.Parts;
        string targetBase = Path.GetFileNameWithoutExtension(partPath);
        try
        {
            PartLoadStatus ls;
            Part p = parts.OpenDisplay(partPath, out ls);
            Log("OpenDisplay: " + p.Name);
            return p;
        }
        catch (Exception e)
        {
            Log("OpenDisplay 失败(" + e.Message + ")——尝试 UF 已载枚举复用");
        }
        try
        {
            int n = uf.Part.AskNumParts();
            for (int i = 0; i < n; i++)
            {
                NXOpen.Tag tag = uf.Part.AskNthPart(i);
                string fspec;
                uf.Part.AskPartName(tag, out fspec);
                Part loaded = NXObjectManager.Get(tag) as Part;
                string loadedName = loaded == null ? "(null)" : loaded.Name;
                Log("  候选[" + i + "] name=" + loadedName);
                if (loaded != null && (string.Equals(loadedName, targetBase, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(loadedName), targetBase, StringComparison.OrdinalIgnoreCase)))
                { Log("复用已装载: " + loadedName); return loaded; }
            }
        }
        catch (Exception e) { Log("UF 枚举失败: " + e.Message); }
        Log("!! 请关闭/重开 NX 会话（两件均未打开）后重跑");
        return null;
    }

    // ---- I-1 轮换：目标件显示 + 工作（uf.SetDisplayPart → SetWork） ----

    private static void Activate(UFSession uf, PartCollection parts, Part part)
    {
        try { uf.Part.SetDisplayPart(part.Tag); Log("SetDisplayPart: " + part.Name); }
        catch (Exception e) { Log("SetDisplayPart 失败(" + e.Message + ")"); }
        try { parts.SetWork(part); Log("SetWork: " + part.Name); }
        catch (Exception e) { Log("!! SetWork 失败: " + e.Message); }
    }

    // ---- 采集（共享 NxCollect——与导出同口径，D-3） ----

    private static void Collect(Part part, ExportSnapshot snap)
    {
        NXOpen.CAM.CAMSetup cam = part.CAMSetup;
        if (cam == null) { Log("!! 部件无 CAMSetup: " + part.Name); return; }
        NxCollect.CollectTools(cam, snap, Log);
        NxCollect.CollectSetups(cam, snap, Log);
        NxCollect.CollectOperations(cam, snap, Log);
    }

    // ---- 报告渲染（逐项 issues + 汇总；格式为 [I] 人工/正则验收面） ----

    private static void Render(ComparerResult r)
    {
        Log("== 结构 ==");
        Log(string.Format("  op 配对={0} 仅A(重建缺)={1} 仅B(重建多)={2}  刀具 A={3} B={4}  setup A={5} B={6}",
            r.OpsMatched, r.OpsMissing, r.OpsExtra, r.ToolsA, r.ToolsB, r.SetupsA, r.SetupsB));
        Log("== 逐项（非 PASS） ==");
        if (r.Issues.Count == 0) Log("  （无）");
        foreach (ComparerIssue i in r.Issues)
            Log(string.Format("  [FAIL] {0}@{1} {2}", i.Code, i.Key, i.Detail));
        foreach (string n in r.Notes)
            Log("  [note] " + n);
        Log(string.Format("== 汇总: issues={0} op={1}/{2} param={3}/{4} tool={5}/{6} mcs={7}/{8} fixture={9}/{10} template={11}/{12} ==",
            r.Issues.Count, r.OpsMatched, r.OpsMatched + r.OpsMissing + r.OpsExtra,
            r.ParamPass, r.ParamChecks, r.ToolPass, r.ToolChecks, r.McsPass, r.McsChecks,
            r.FixturePass, r.FixtureChecks, r.TemplatePass, r.TemplateChecks));
        // v2（2026-09-05，nx-v2-geom-spec V2-POST-4/5/6）：刀路/区域/签名面集计数
        Log(string.Format("== v2 汇总: toolpath={0}/{1} region={2}/{3} sigfaceset={4}/{5} ==",
            r.ToolpathPass, r.ToolpathChecks, r.RegionPass, r.RegionChecks, r.SigPass, r.SigChecks));
    }

    private static void Log(string s)
    {
        lock (_logLock)
        {
            try { File.AppendAllText(_outPath, s + Environment.NewLine); }
            catch { /* 忽略日志写失败 */ }
        }
    }

    // 双引号清洗（Execute 对话框可能保留引号，2026-09-04 200022 实证）
    private static string TrimQuotes(string s)
    {
        if (s == null) return "";
        return s.Trim().Trim('"').Trim();
    }
}
