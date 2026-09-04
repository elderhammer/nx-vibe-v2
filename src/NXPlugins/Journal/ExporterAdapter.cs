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

            CollectTools(cam, snap);
            CollectSetups(cam, snap);
            CollectOperations(cam, snap);
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

    // ---- 采集：机床树刀具 ----
    private static void CollectTools(CAMSetup cam, ExportSnapshot snap)
    {
        NCGroup root = cam.GetRoot(CAMSetup.View.MachineTool);
        WalkTools(root, snap, 0, cam);
    }

    private static void WalkTools(NCGroup g, ExportSnapshot snap, int depth, CAMSetup cam)
    {
        foreach (CAMObject m in SafeMembers(g))
        {
            NCGroup sub = m as NCGroup;
            if (sub == null) continue;
            string fam = NameOfTypeSafe(sub);
            bool container = fam == "Generic PARAM object" || fam == "Tool Carrier" || fam == "Head" || fam == "Machine";
            if (depth >= 1 && !container)
            {
                var t = new ToolItem { Name = sub.Name, TypeFamily = fam };
                // U-7（PRE-U7-1）：真刀 as Tool + GetTypeAndSubtype 直写 NX 枚举原文（语言无关；
                // 容器组 as Tool 应为 null——入选判定已按家族串排除，双保险）；失败 → 剔除此刀（INV-U7-4）
                NXOpen.CAM.Tool tt = sub as NXOpen.CAM.Tool;
                if (tt == null)
                    t.TypeReadbackError = "NCGroup 非 Tool 子类（as Tool 失败）";
                else
                {
                    try
                    {
                        NXOpen.CAM.Tool.Types ty;
                        NXOpen.CAM.Tool.Subtypes st;
                        tt.GetTypeAndSubtype(out ty, out st);
                        t.NxType = ty.ToString();
                        t.NxSubtype = st.ToString();
                        Log("  tool " + sub.Name + " → type=" + t.NxType + " subtype=" + t.NxSubtype);
                    }
                    catch (Exception e) { t.TypeReadbackError = "GetTypeAndSubtype 异常: " + e.Message; }
                }
                ReadToolParams(cam, sub, t);
                snap.Tools.Add(t);
            }
            WalkTools(sub, snap, depth + 1, cam);
        }
    }

    private static void ReadToolParams(CAMSetup cam, NCGroup toolGroup, ToolItem t)
    {
        try
        {
            MillingToolBuilder b = null;
            try { b = cam.CAMGroupCollection.CreateMillToolBuilder(toolGroup) as MillingToolBuilder; }
            catch { b = null; }
            if (b == null)
            {
                try { b = cam.CAMGroupCollection.CreateDrillStdToolBuilder(toolGroup) as MillingToolBuilder; }
                catch { b = null; }
            }
            if (b == null) { t.TypeFamily += " (参数未读: builder 不匹配)"; return; }
            try { t.Diameter = b.TlDiameterBuilder.Value; } catch { }
            try { t.NumFlutes = b.TlNumFlutesBuilder.Value; } catch { }
            try { t.FluteLength = b.TlFluteLnBuilder.Value; } catch { }
            try { t.LowerCornerRadius = b.TlLowCorRadBuilder.Value; } catch { }
            b.Destroy();
        }
        catch (Exception e) { t.TypeFamily += " (参数异常: " + e.Message + ")"; }
    }

    // ---- 采集：MCS（几何树中名字含 MCS 的组）----
    private static void CollectSetups(CAMSetup cam, ExportSnapshot snap)
    {
        NCGroup root = cam.GetRoot(CAMSetup.View.Geometry);
        NCGroup mcs = FindMcs(root);
        var s = new SetupItem { Name = mcs == null ? "UNKNOWN" : mcs.Name, MissingMcs = mcs == null };
        if (mcs != null)
        {
            try
            {
                MillOrientGeomBuilder ob = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(mcs);
                try
                {
                    CartesianCoordinateSystem cs = ob.Mcs;
                    if (cs != null)
                    {
                        s.McsOrigin = new[] { cs.Origin.X, cs.Origin.Y, cs.Origin.Z };
                        Matrix3x3 el = cs.Orientation.Element;
                        s.McsXAxis = new[] { el.Xx, el.Xy, el.Xz };
                        s.McsZAxis = new[] { el.Zx, el.Zy, el.Zz };
                        Log(string.Format("MCS 回读: origin=({0:0.###},{1:0.###},{2:0.###}) z=({3:0.###},{4:0.###},{5:0.###})",
                            cs.Origin.X, cs.Origin.Y, cs.Origin.Z, el.Zx, el.Zy, el.Zz));
                    }
                }
                finally { ob.Destroy(); }
            }
            catch (Exception e) { Log("MCS 回读异常: " + e.Message); }
        }
        snap.Setups.Add(s);
    }

    private static NCGroup FindMcs(NCGroup g)
    {
        foreach (CAMObject m in SafeMembers(g))
        {
            NCGroup sub = m as NCGroup;
            if (sub == null) continue;
            if (sub.Name.StartsWith("MCS", StringComparison.Ordinal)) return sub;
            NCGroup hit = FindMcs(sub);
            if (hit != null) return hit;
        }
        return null;
    }

    // ---- 采集：操作（程序顺序树，单视图；Tag 即唯一键）----
    private static void CollectOperations(CAMSetup cam, ExportSnapshot snap)
    {
        NCGroup root = cam.GetRoot(CAMSetup.View.ProgramOrder);
        foreach (NCGroup top in TopProgramGroups(root))
            snap.ProgramOrder.Add(top.Name);
        WalkOps(root, snap, cam);
    }

    private static void WalkOps(NCGroup g, ExportSnapshot snap, CAMSetup cam)
    {
        foreach (CAMObject m in SafeMembers(g))
        {
            if (m is NCGroup) { WalkOps((NCGroup)m, snap, cam); continue; }
            Operation op = m as Operation;
            if (op == null) continue;
            var o = new OperationItem
            {
                Name = op.Name,
                UserName = op.UserName ?? "",
                Key = new TagKey((ulong)(long)(int)op.Tag),
                TypeFamily = NameOfTypeSafe(op),
                ProgramParent = ParentName(op.ParentProgramOrder),
                MethodParent = ParentName(op.ParentMachineMethod),
                ToolParent = ParentName(op.ParentMachineTool),
                GeometryParent = ParentName(op.ParentGeometry),
                HasGeometryParent = op.ParentGeometry != null,
            };
            if (o.TypeFamily == "Cavity Milling")
            {
                try
                {
                    CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                    try
                    {
                        TryParam(b, o, "part_stock", () => b.CutParameters.PartStock.Value);
                        TryParam(b, o, "floor_stock", () => b.CutParameters.FloorStock.Value);
                        TryParam(b, o, "depth_per_cut", () => b.DepthPerCut.Value);
                    }
                    finally { b.Destroy(); }
                }
                catch (Exception e) { o.ReadbackErrors.Add("cavity builder 打不开: " + e.Message); }
            }
            else if (o.TypeFamily == "Drilling")
            {
                // 新模板 DRILLING 家族 → HoleDrillingBuilder（camprobe-drill 实证 BottomStock 可读）
                try
                {
                    HoleDrillingBuilder b = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
                    try { TryParam(b, o, "bottom_stock", () => b.CuttingParameters.BottomStock.Value); }
                    finally { b.Destroy(); }
                }
                catch (Exception e) { o.ReadbackErrors.Add("hole builder 打不开: " + e.Message); }
            }
            else if (o.TypeFamily == "Point to Point")
            {
                // PTP 旧模板（打点/钻头G83）→ PointToPointBuilder（2406 实证：CreateHoleDrillingBuilder
                // 会类型转换失败）；参数面仅 HoleDepth/Retract 等（孔细分参数面属 #3 范围，不扩读）
                try
                {
                    PointToPointBuilder b = cam.CAMOperationCollection.CreatePointToPointBuilder(op);
                    try
                    {
                        TryParam(b, o, "hole_depth", () => b.HoleDepth.Value);
                        Log("  PTP op " + o.Name + " 参数面待 #3 细化（builder 已开验证，当前仅读 hole_depth）");
                    }
                    finally { b.Destroy(); }
                }
                catch (Exception e) { o.ReadbackErrors.Add("ptp builder 打不开: " + e.Message); }
            }
            snap.Operations.Add(o);
        }
    }

    private static void TryParam(object builder, OperationItem o, string key, Func<double> getter)
    {
        try { o.Params[key] = getter(); }
        catch (Exception e) { o.ReadbackErrors.Add("参数 " + key + " 回读失败: " + e.Message); }
    }

    private static List<NCGroup> TopProgramGroups(NCGroup root)
    {
        var list = new List<NCGroup>();
        foreach (CAMObject m in SafeMembers(root))
        {
            NCGroup sub = m as NCGroup;
            if (sub == null || sub.Name == "NONE") continue;
            string fam = NameOfTypeSafe(sub);
            if (fam == "Generic PARAM object") list.Add(sub);   // 程序组大类；机床/方法等树同名大类不在此调用
        }
        return list;
    }

    // ---- 工具 ----
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

    private static CAMObject[] SafeMembers(NCGroup g)
    {
        try { return g.GetMembers(); }
        catch (Exception e) { Log("GetMembers 失败(" + g.Name + "): " + e.Message); return new CAMObject[0]; }
    }

    private static string NameOfTypeSafe(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch { return "(unknown)"; }
    }

    private static string ParentName(NCGroup g) { return g == null ? "" : g.Name; }

    // NX Part.Name 无扩展名 → 与去扩展名基准比对（忽略大小写）
    private static bool IsTargetName(string name, string targetBase)
    {
        return string.Equals(name, targetBase, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(name), targetBase, StringComparison.OrdinalIgnoreCase);
    }

    // 即时追加写盘（每行立即落文件；写失败静默——日志通道不得阻塞主流程）
    private static void Log(string s)
    {
        lock (_logLock)
        {
            try { File.AppendAllText(_outPath, s + Environment.NewLine); }
            catch { /* 忽略日志写失败 */ }
        }
    }
}
