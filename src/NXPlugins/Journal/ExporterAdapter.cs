// ExporterAdapter.cs — [I] 层集成验证：真实 NX 会话内跑通「test.prt → ExportSnapshot →
// ExporterCore.Build → PlanWriter 原子落盘 → 复验」（spec A1-A12 的 NX 侧半程）
//
// 纯逻辑核心（src/NXPlugins/PlanExporter/*.cs，无 NX 依赖）随本文件一起 csc 编译。
// 执行：干净 NX 会话（test.prt 未打开）→ File → Execute → NX Open → 本 exe。
// 产物：samples\test.plan.json（schema 复验通过）+ samples\exporter-adapter.txt（过程报告）。
// 只读纪律：不 Commit/不修改/不保存源文件；参数字段按 MVP 子集（U-4 探针性质）。
//
// 已知简化（如实记录，不做静默）：① workplan 仅顶层程序组序列 + 工序节点挂其父组（缺父挂根，
//   嵌套程序组层级首版不展开——spec A8 口径）；② 参数回读仅 double 字段子集；③ MCS 轴取自
//   csys.Orientation.Element 矩阵行（X/Z）；④ 刀具参数经 MillingToolBuilder 通用成员（多态 `as`）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXOpen.Utilities;
using NXPlugins.PlanExporter;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class ExporterAdapter
{
    private const string DefaultPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static string _outPath = @"C:\Users\21505\Code\nx-vibe-v2\samples\adapter-run.txt";
    private const string DefaultPlan = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.plan.json";
    private static readonly object _logLock = new object();

    public static void Main(string[] args)
    {
        // NX Execute 会给 Main 传 1 个空参数（args[0]=""）—— 必须判非空，否则路径为空串
        string partPath = (args.Length > 0 && !string.IsNullOrEmpty(args[0])) ? args[0] : DefaultPart;
        if (args.Length > 1 && !string.IsNullOrEmpty(args[1])) _outPath = args[1];
        string planPath = (args.Length > 2 && !string.IsNullOrEmpty(args[2])) ? args[2] : DefaultPlan;
        // 即时追加写盘：每行立即落文件（时间戳命名，避免旧文件/锁/缓存歧义；硬崩也保留阶段痕迹）
        _outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_outPath)),
            "adapter-run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== ExporterAdapter v11 ==");
        Session theSession = null;
        Part part = null;
        try
        {
            theSession = Session.GetSession();
            var parts = theSession.Parts;
            UFSession uf = UFSession.GetUFSession();
            string targetBase = Path.GetFileNameWithoutExtension(partPath);   // NX Part.Name 无扩展名
            // ---- 取件级联（v7.1）----
            // 1) 工作部件匹配 → 2) 已显示部件匹配 → 3) UF 已装载枚举（隐藏装载也能接管）
            //    → 4) OpenDisplay（干净会话）。943006=文件已存在：所有 Open* 对已装载文件都拒绝，
            //    而隐藏装载的部件不出现在 Work/GetDisplayedParts —— 必须经 UF AskNumParts/AskNthPart 枚举。
            // 教训：NX Part.Name 不带扩展名（"test" 而非 "test.prt"），比较须用去扩展名基准。
            try
            {
                if (parts.Work != null && IsTargetName(parts.Work.Name, targetBase))
                { part = parts.Work; Log("取工作部件: " + part.Name); }
            }
            catch (Exception e) { Log("Work 查询跳过(" + e.Message + ")"); }
            if (part == null)
            {
                try
                {
                    foreach (BasePart bp in parts.GetDisplayedParts())
                        if (IsTargetName(bp.Name, targetBase))
                        { part = bp as Part; Log("取已显示部件: " + bp.Name); break; }
                }
                catch (Exception e) { Log("GetDisplayedParts 跳过(" + e.Message + ")"); }
            }
            if (part == null)
            {
                try
                {
                    int n = uf.Part.AskNumParts();
                    Log("UF 已装载部件数=" + n);
                    for (int i = 0; i < n && part == null; i++)
                    {
                        NXOpen.Tag tag = uf.Part.AskNthPart(i);
                        string fspec;
                        uf.Part.AskPartName(tag, out fspec);
                        Part loaded = NXObjectManager.Get(tag) as Part;
                        if (loaded != null)
                            Log("  装载项[" + i + "] name=" + loaded.Name + " fspec=" + fspec);
                        if (loaded != null && IsTargetName(loaded.Name, targetBase))
                        { part = loaded; Log("取 UF 隐藏装载部件: " + part.Name); }
                    }
                }
                catch (Exception e) { Log("UF 装载枚举跳过(" + e.Message + ")"); }
            }
            if (part == null)
            {
                try
                {
                    PartLoadStatus ls;
                    part = parts.OpenDisplay(partPath, out ls);
                    Log("OpenDisplay: " + part.Name);
                }
                catch (Exception e)
                {
                    Log("!! OpenDisplay 失败: " + e.Message);
                    Log("   请关闭/重开 NX 会话后重跑（若 UF 枚举仍看不到该文件则文件可能被外部占用）");
                    return;
                }
            }
            // ---- 确保显示（uf.SetDisplayPart 可从隐藏直接提升；失败再退化 scratch 基线）----
            bool isDisplayed = false;
            try
            {
                foreach (BasePart bp in parts.GetDisplayedParts())
                    if (bp == part) { isDisplayed = true; break; }
            }
            catch (Exception e) { Log("GetDisplayedParts 复核失败: " + e.Message); }
            if (!isDisplayed)
            {
                try { uf.Part.SetDisplayPart(part.Tag); Log("uf.SetDisplayPart 成功"); }
                catch (Exception e)
                {
                    Log("uf.SetDisplayPart 失败(" + e.Message + ")，退化 scratch 基线路径");
                    try
                    {
                        if (parts.GetDisplayedParts().Length == 0)
                        { try { parts.NewDisplay("__adapter_scratch", Part.Units.Millimeters); Log("已建显示基线 scratch"); }
                          catch (Exception e2) { Log("NewDisplay scratch 失败: " + e2.Message); } }
                        PartLoadStatus ls3;
                        parts.SetDisplay(part, true, true, out ls3);
                        Log("SetDisplay 成功");
                    }
                    catch (Exception e2) { Log("!! SetDisplay 失败: " + e2.Message); return; }
                }
            }
            try { parts.SetWork(part); Log("已设工作部件"); }
            catch (Exception e) { Log("!! SetWork 失败: " + e.Message); return; }

            CAMSetup cam = part.CAMSetup;
            if (cam == null) { Log("!! 部件无 CAMSetup"); return; }

            // ---- A2 前置闸门（PRE-1/2/3；任一失败中止且不落盘，POST-5）----
            PreflightResult pr = ExportGates.Preflight(new SessionGate(theSession, cam), WhiteList.IsReady);
            if (!pr.Ok) { foreach (string f in pr.Failures) Log("!! " + f); Log("中止（不落盘）"); return; }
            Log("前置闸门通过（部件+CAMSetup+cam_base 许可）");

            // ---- 快照采集 ----
            var snap = new ExportSnapshot
            {
                Name = "test.prt 导出冒烟（[I] 适配器）",
                InputRef = "samples/test.prt",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            };

            NxCollect.CollectTools(cam, snap, Log);
            NxCollect.CollectSetups(cam, snap, Log);
            NxCollect.CollectOperations(cam, snap, Log);
            Log(string.Format("快照: tools={0} setups={1} ops={2}",
                snap.Tools.Count, snap.Setups.Count, snap.Operations.Count));

            // ---- Build → 校验 → 原子落盘 → 复验 ----
            PlanDocument doc = ExporterCore.Build(snap, WhiteList.Resolve);
            List<string> errs = PlanValidator.Validate(doc);
            if (errs.Count > 0)
            {
                foreach (string e in errs) Log("!! 校验失败: " + e);
                Log("中止（不落盘）");
                return;
            }
            Log("schema 校验通过（内存）: ops=" + doc.operations.Count
                + " ws=" + doc.workingsteps.Count + " diag=" + doc.diagnostics.Count);
            PlanWriter.WriteAtomically(doc, planPath);
            Log("已落盘: " + planPath);

            PlanDocument back = PlanWriter.Serializer.Deserialize(File.ReadAllText(planPath));
            List<string> backErrs = PlanValidator.Validate(back);
            Log("落盘复验: " + (backErrs.Count == 0 ? "PASS" : "FAIL " + string.Join(";", backErrs)));

            foreach (DiagnosticJson d in doc.diagnostics)
                Log("  diag[" + d.level + "] " + d.code + " " + d.message + " op=" + d.operation_id);
            Log("完成");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private sealed class SessionGate : ISessionGate
    {
        private readonly Session _s;
        private readonly CAMSetup _cam;
        public SessionGate(Session s, CAMSetup cam) { _s = s; _cam = cam; }
        public bool HasDisplayedWorkPartWithCamSetup
        {
            get { try { return _cam != null && _s.Parts.Work != null; } catch { return false; } }
        }
        public bool CanReserveCamBase
        {
            get
            {
                try { _s.LicenseManager.Reserve("cam_base", "ExporterAdapter"); return true; }
                catch { return false; }
                finally { try { _s.LicenseManager.Release("cam_base", "ExporterAdapter"); } catch { } }
            }
        }
    }

    // NX Part.Name 无扩展名 → 与去扩展名基准比对（忽略大小写）
    private static bool IsTargetName(string name, string targetBase)
    {
        return string.Equals(name, targetBase, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(name), targetBase, StringComparison.OrdinalIgnoreCase);
    }

    private static void Log(string s)
    {
        lock (_logLock)
        {
            try { File.AppendAllText(_outPath, s + Environment.NewLine); }
            catch { /* 忽略日志写失败 */ }
        }
    }
}
