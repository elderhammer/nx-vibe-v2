// ExecutorAdapter.cs — [I] 层集成验证：NX 会话内按 RebuildPlan 重建 prj′
//
// 输入：plan.json（PlanDocument，schema v3；缺省 samples\test.plan.json）
// 产物：samples\test.rebuilt-<ts>.prt（重建件，自建资产；主名被会话占用时自动时间戳兜底）
//       + samples\executor-run-<ts>.txt（过程报告 + 回读对照 PASS/FAIL，spec I-1..I-4 + MONO-1 执行期）。
// 纪律（索引 §2.1）：建件 → Session.CreateCamSession() → CreateCamSetup → 全预检通过才动 NX。
// 运行：scripts\compile-executor-adapter.ps1 合编纯逻辑核心 + 本文件 → .claude\tmp\<ExeName>.exe
//       → NX 会话 File → Execute → NX Open（与 ExporterAdapter 同款；⚠️ run_journal 单文件 journal
//       合并不适用——核心依赖 DataContractJsonSerializer，journal 编译器缺 System.Runtime.Serialization，
//       见 spec §1 注记）。
// 已知范围（spec D-1=A）：v1 无几何指派、不生成刀路；对照维度=结构/刀具数值/MCS/fixture/可写参数。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXPlugins.PlanExporter;
using NXPlugins.PlanExecutor;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class ExecutorAdapter
{
    private static string _out;
    private const string DefaultPlan = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.plan.json";
    private const string DefaultPrj = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.rebuilt.prt";
    private static int _ok, _fail;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "executor-run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        string planPath = DefaultPlan;
        string prjPath = DefaultPrj;
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) planPath = args[0];
        if (args.Length > 1 && !string.IsNullOrEmpty(args[1])) prjPath = args[1];
        Log("== ExecutorAdapter ==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("plan=" + planPath + "  prj=" + prjPath);
        try
        {
            // ---- 纯逻辑预检（MONO-1：全部预检通过前不动 NX）----
            string json = File.ReadAllText(planPath);
            NXPlugins.PlanExporter.PlanDocument plan =
                new NXPlugins.PlanExporter.PlanJsonSerializer().Deserialize(json);
            Log("解析 OK: plan_id=" + plan.plan_id + " ops=" + plan.operations.Count
                + " tools=" + plan.resources.tools.Count + " setups=" + plan.setups.Count);
            RebuildPlan rp = ExecutorCore.Build(plan);
            foreach (RebuildDiag d in rp.Diagnostics)
                Log("  diag[" + d.Level.ToString().ToLowerInvariant() + "] " + d.Code
                    + (d.Scope.Length > 0 ? " scope=" + d.Scope : "") + " " + d.Message);
            if (!rp.Ok)
            {
                Log("!! 结构级失败（pre-flight）——中止，不创建任何 NX 对象（MONO-1）");
                return;
            }
            Log("pre-flight OK: programs=" + rp.Programs.Count + " tools=" + rp.Tools.Count
                + " setups=" + rp.Setups.Count + " ops=" + rp.Operations.Count);

            // ---- NX 会话（索引 §2.1 纪律：建件 → CreateCamSession → CreateCamSetup）----
            Session s = Session.GetSession();
            Part part = s.Parts.NewDisplay("ExecutorOut" + DateTime.Now.ToString("HHmmss"),
                Part.Units.Millimeters);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            Log("NewDisplay+CreateCamSession OK");

            bool hasMill = false;
            foreach (OpCommand c in rp.Operations)
                if (c.Pair.Type == "mill_contour") { hasMill = true; break; }
            CAMSetup cam = part.CreateCamSetup(hasMill ? "mill_contour" : "hole_making");
            Log("CreateCamSetup(" + (hasMill ? "mill_contour" : "hole_making") + ") OK");

            // 许可 gate（cam_base）
            Step("许可 gate", () =>
            {
                try
                {
                    s.LicenseManager.Reserve("cam_base", "ExecutorAdapter");
                    Log("  cam_base Reserve OK");
                }
                finally { try { s.LicenseManager.Release("cam_base", "ExecutorAdapter"); } catch { } }
            });

            // ---- 执行重建（指令序 = rp.Operations DFS 序，INV-3）----
            var toolMap = new Dictionary<string, NCGroup>();
            var progMap = new Dictionary<string, NCGroup>();
            var methodMap = new Dictionary<string, NCGroup>();
            var setupWpMap = new Dictionary<string, NCGroup>();

            Step("程序组（模板默认 PROGRAM 根，子组按 plan）", () =>
            {
                NCGroup root = cam.GetRoot(CAMSetup.View.ProgramOrder);
                NCGroup defProg = FindChildByName(root, "PROGRAM");
                if (defProg == null)
                {
                    defProg = cam.CAMGroupCollection.CreateProgram(root, "mill_contour", "PROGRAM",
                        NCGroupCollection.UseDefaultName.False, "PROGRAM");
                }
                progMap["PROGRAM"] = defProg;
                // 父先子后（按 Full 排序）
                var ordered = new List<ProgramCommand>(rp.Programs);
                ordered.Sort(delegate(ProgramCommand a, ProgramCommand b)
                { return StringComparer.Ordinal.Compare(a.Full, b.Full); });
                foreach (ProgramCommand pc in ordered)
                {
                    if (pc.Full == "PROGRAM" && pc.ParentFull == "") continue;   // 默认组本身
                    NCGroup parent = pc.ParentFull.Length == 0 ? defProg
                        : (progMap.ContainsKey(pc.ParentFull) ? progMap[pc.ParentFull] : defProg);
                    if (FindChildByName(parent, pc.Name) != null) { progMap[pc.Full] = parent; continue; }
                    NCGroup g = cam.CAMGroupCollection.CreateProgram(parent, "mill_contour", "PROGRAM",
                        NCGroupCollection.UseDefaultName.False, pc.Name);
                    progMap[pc.Full] = g;
                    Log("  program: " + pc.Full + " (parent=" + pc.ParentFull + ")");
                }
            });

            Step("刀具组（模板对 + 数值直填）", () =>
            {
                NCGroup root = cam.GetRoot(CAMSetup.View.MachineTool);
                foreach (ToolCommand tc in rp.Tools)
                {
                    NCGroup g = cam.CAMGroupCollection.CreateTool(root, tc.Pair.Type, tc.Pair.Subtype,
                        NCGroupCollection.UseDefaultName.False, tc.ToolId);
                    toolMap[tc.ToolId] = g;
                    MillingToolBuilder mb = null;
                    try
                    {
                        mb = tc.Pair.Subtype == "STD_DRILL"
                            ? cam.CAMGroupCollection.CreateDrillStdToolBuilder(g) as MillingToolBuilder
                            : cam.CAMGroupCollection.CreateMillToolBuilder(g) as MillingToolBuilder;
                    }
                    catch (Exception e) { Log("  " + tc.ToolId + " builder 打不开: " + e.Message); }
                    if (mb != null)
                    {
                        try
                        {
                            if (tc.Diameter.HasValue) mb.TlDiameterBuilder.Value = tc.Diameter.Value;
                            if (tc.NumFlutes.HasValue) mb.TlNumFlutesBuilder.Value = tc.NumFlutes.Value;
                            if (tc.FluteLength.HasValue) mb.TlFluteLnBuilder.Value = tc.FluteLength.Value;
                            if (tc.LowerCornerRadius.HasValue) mb.TlLowCorRadBuilder.Value = tc.LowerCornerRadius.Value;
                            mb.Commit();
                        }
                        catch (Exception e) { Log("  " + tc.ToolId + " 参数写异常: " + e.Message); }
                        finally { mb.Destroy(); }
                    }
                    Log("  tool: " + tc.ToolId + " " + tc.Pair + " 直径="
                        + (tc.Diameter.HasValue ? tc.Diameter.Value.ToString("0.####") : "(缺)"));
                }
            });

            Step("setup 几何链（MCS + WORKPIECE）+ MCS/fixture 写", () =>
            {
                NCGroup root = cam.GetRoot(CAMSetup.View.Geometry);
                foreach (GeometryChainCommand gc in rp.Setups)
                {
                    NCGroup mcs = FindChildByName(root, gc.McsGroupName);
                    if (mcs == null)
                    {
                        try
                        {
                            mcs = cam.CAMGroupCollection.CreateGeometry(root, "mill_contour", "MCS",
                                NCGroupCollection.UseDefaultName.False, gc.McsGroupName);
                        }
                        catch (Exception e)
                        {
                            Log("  " + gc.McsGroupName + " 建 MCS 组失败(" + e.Message + ") → 用模板默认 MCS_MILL");
                            mcs = FindChildByName(root, "MCS_MILL");
                        }
                    }
                    NCGroup wp = FindChildByName(mcs, gc.WorkpieceName);
                    if (wp == null)
                        wp = cam.CAMGroupCollection.CreateGeometry(mcs, "mill_contour", "WORKPIECE",
                            NCGroupCollection.UseDefaultName.False, gc.WorkpieceName);
                    setupWpMap[gc.SetupId] = wp;
                    Log("  setup " + gc.SetupId + ": " + mcs.Name + "/" + wp.Name);

                    // MCS csys + fixture（P3/P4 实证链路）
                    if (gc.McsOrigin != null && gc.McsOrigin.Length == 3
                        && gc.McsZAxis != null && gc.McsZAxis.Length == 3
                        && gc.McsXAxis != null && gc.McsXAxis.Length == 3)
                    {
                        try
                        {
                            Matrix3x3 m = BuildMatrix(gc.McsXAxis, gc.McsZAxis);
                            CartesianCoordinateSystem csys = part.CoordinateSystems
                                .CreateCoordinateSystem(new Point3d(gc.McsOrigin[0], gc.McsOrigin[1], gc.McsOrigin[2]),
                                    m, false);
                            MillOrientGeomBuilder ob = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(mcs);
                            try
                            {
                                ob.Mcs = csys;
                                if (gc.FixtureOffset.HasValue)
                                    ob.FixtureOffsetBuilder.Value = gc.FixtureOffset.Value;
                                ob.Commit();
                            }
                            finally { ob.Destroy(); }
                            Log("  MCS 写: origin=(" + gc.McsOrigin[0] + "," + gc.McsOrigin[1] + "," + gc.McsOrigin[2] + ")"
                                + (gc.FixtureOffset.HasValue ? " fixture=" + gc.FixtureOffset.Value : ""));
                        }
                        catch (Exception e) { Log("  MCS 写异常: " + e.Message); }
                    }
                    else Log("  MCS 数组不全 → 用模板默认 csys（fixture="
                        + (gc.FixtureOffset.HasValue ? gc.FixtureOffset.Value.ToString() : "缺") + "）");
                }
            });

            Step("方法组锚点", () =>
            {
                NCGroup root = cam.GetRoot(CAMSetup.View.MachineMethod);
                foreach (OpCommand c in rp.Operations)
                {
                    if (methodMap.ContainsKey(c.MethodAnchor)) continue;
                    if (c.MethodAnchor.Length == 0) { methodMap[""] = root; continue; }
                    NCGroup g = FindChildByName(root, c.MethodAnchor);
                    if (g == null && c.MethodNeedsCreate)
                    {
                        try
                        {
                            g = cam.CAMGroupCollection.CreateMethod(root, "mill_contour", "MILL_METHOD",
                                NCGroupCollection.UseDefaultName.False, c.MethodAnchor);
                        }
                        catch (Exception e) { Log("  方法组 " + c.MethodAnchor + " 创建失败: " + e.Message); }
                    }
                    if (g == null) g = root;   // 兜底挂方法根
                    methodMap[c.MethodAnchor] = g;
                }
                Log("  方法锚点数=" + methodMap.Count);
            });

            Step("逐 op 创建 + 白名单参数写（DFS 序）", () =>
            {
                foreach (OpCommand c in rp.Operations)
                {
                    NCGroup prog = progMap.ContainsKey(c.ProgramFull) ? progMap[c.ProgramFull] : progMap["PROGRAM"];
                    NCGroup method = methodMap.ContainsKey(c.MethodAnchor) ? methodMap[c.MethodAnchor] : null;
                    NCGroup tool = toolMap.ContainsKey(c.ToolId) ? toolMap[c.ToolId] : null;
                    NCGroup geom = setupWpMap.ContainsKey(c.SetupId) ? setupWpMap[c.SetupId] : null;
                    if (method == null || tool == null || geom == null)
                    {
                        Log("  !! " + c.OpId + " 锚点缺失（method/tool/geom）——跳过"); _fail++;
                        continue;
                    }
                    Operation op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                        c.Pair.Type, c.Pair.Subtype, OperationCollection.UseDefaultName.False, c.DisplayName);
                    Log("  op: " + c.OpId + " 名=" + c.DisplayName + " " + c.Pair
                        + "  prog=" + prog.Name + " method=" + method.Name + " tool=" + tool.Name + " geom=" + geom.Name);
                    foreach (ParamInstruction pi in c.Params)
                        WriteParam(cam, op, c.Pair, pi);
                }
            });

            Step("落盘 prj′（主名被本会话占用 → 时间戳名兜底；不预删既有资产）", () =>
            {
                try
                {
                    part.SaveAs(prjPath);
                    Log("  已落盘: " + prjPath);
                }
                catch (Exception e)
                {
                    Log("  主名 SaveAs 失败(" + e.Message + ") → 时间戳名兜底");
                    string alt = Path.Combine(Path.GetDirectoryName(prjPath),
                        "test.rebuilt-" + DateTime.Now.ToString("HHmmss") + ".prt");
                    try { part.SaveAs(alt); Log("  已落盘(兜底): " + alt); }
                    catch (Exception e2) { Log("  兜底 SaveAs 也失败: " + e2.Message); }
                }
            });

            Step("回读对照（I-2：结构/刀具直径/MCS/fixture/可写参数 vs plan）", () =>
                ReadbackCompare(s, rp, prjPath, part));
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ---- 白名单参数写（PRE-4 成员路径分派；单字段失败记录不阻断）----
    private static void WriteParam(CAMSetup cam, Operation op, TemplatePair pair, ParamInstruction pi)
    {
        try
        {
            if (pair.Subtype == "CAVITY_MILL")
            {
                CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try
                {
                    switch (pi.MemberPath)
                    {
                        case "CutParameters.PartStock": b.CutParameters.PartStock.Value = pi.Value; break;
                        case "CutParameters.FloorStock": b.CutParameters.FloorStock.Value = pi.Value; break;
                        case "CutParameters.WallStock": b.CutParameters.WallStock.Value = pi.Value; break;
                        case "DepthPerCut": b.DepthPerCut.Value = pi.Value; break;
                        case "HoleDepth": b.HoleDepth.Value = pi.Value; break;
                        case "FeedsBuilder.SpindleRpmBuilder": b.FeedsBuilder.SpindleRpmBuilder.Value = pi.Value; break;
                        default: Log("    " + op.Name + " 参数路径无写实现: " + pi.MemberPath); return;
                    }
                    b.Commit();
                    Log("    " + op.Name + " 写 " + pi.MemberPath + "=" + pi.Value.ToString("0.####"));
                }
                finally { b.Destroy(); }
                return;
            }
            // 孔族（DRILLING/SPOT/DEEP_HOLE/TAP）
            if (pair.Subtype == "DRILLING" || pair.Subtype == "SPOT_DRILLING"
                || pair.Subtype == "DEEP_HOLE_DRILLING" || pair.Subtype == "TAPPING")
            {
                HoleDrillingBuilder b = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
                try
                {
                    switch (pi.MemberPath)
                    {
                        case "HoleDepth": b.HoleDepth.Value = pi.Value; break;
                        case "FeedsBuilder.SpindleRpmBuilder": b.FeedsBuilder.SpindleRpmBuilder.Value = pi.Value; break;
                        default: Log("    " + op.Name + " 参数路径对孔族无写实现: " + pi.MemberPath); return;
                    }
                    b.Commit();
                    Log("    " + op.Name + " 写 " + pi.MemberPath + "=" + pi.Value.ToString("0.####"));
                }
                finally { b.Destroy(); }
                return;
            }
            Log("    " + op.Name + " 模板 " + pair + " 参数写未实现（skip）");
        }
        catch (Exception e) { Log("    " + op.Name + " 参数写异常(" + pi.MemberPath + "): " + e.Message); }
    }

    // ---- 回读对照 ----
    private static void ReadbackCompare(Session s, RebuildPlan rp, string prjPath, Part inMemoryPart)
    {
        // 重建件重开（I-3）：SaveAs 后部件仍在会话（Open* 报 943006"文件已存在"）→ 复用内存 part（同一对象）
        Part rb = null;
        try
        {
            PartLoadStatus ls;
            rb = s.Parts.OpenDisplay(prjPath, out ls);
            Log("  重开 prj′ OK: " + rb.Name);
        }
        catch (Exception e)
        {
            Log("  OpenDisplay 943006（已装载，SaveAs 后同一对象）→ 复用内存 part: " + e.Message);
            rb = inMemoryPart;
        }
        CAMSetup cam = rb.CAMSetup;
        if (cam == null) { Log("  prj′ 无 CAMSetup"); return; }

        // 结构：ops 数与 DFS 序名（vs rp.Operations DisplayName 序）
        var rbOps = new List<Operation>();
        CollectOps(cam.GetRoot(CAMSetup.View.ProgramOrder), rbOps);
        R("工序数对照", () =>
        {
            int expect = rp.Operations.Count;
            bool same = rbOps.Count == expect;
            if (!same) _fail++; else _ok++;
            return (same ? "PASS" : "FAIL(期望 " + expect + " 实得 " + rbOps.Count + ")");
        });
        int mism = 0;
        for (int i = 0; i < rbOps.Count && i < rp.Operations.Count; i++)
            if (rbOps[i].Name != rp.Operations[i].DisplayName) mism++;
        R("工序序/名对照", () => (mism == 0 ? "PASS" : "FAIL(错位 " + mism + ")"));

        // 刀具直径回读（vs plan tools）
        NCGroup toolRoot = cam.GetRoot(CAMSetup.View.MachineTool);
        foreach (ToolCommand tc in rp.Tools)
        {
            NCGroup g = FindChildByName(toolRoot, tc.ToolId);
            if (g == null) { Log("  FAIL 刀具组未找到: " + tc.ToolId); _fail++; continue; }
            try
            {
                MillingToolBuilder mb = tc.Pair.Subtype == "STD_DRILL"
                    ? cam.CAMGroupCollection.CreateDrillStdToolBuilder(g) as MillingToolBuilder
                    : cam.CAMGroupCollection.CreateMillToolBuilder(g) as MillingToolBuilder;
                if (mb == null) { Log("  FAIL " + tc.ToolId + " builder 打不开"); _fail++; continue; }
                try
                {
                    double d = mb.TlDiameterBuilder.Value;
                    bool pass = tc.Diameter.HasValue && Math.Abs(d - tc.Diameter.Value) < 1e-9;
                    Log("  " + (pass ? "PASS" : "FAIL") + " 刀具 " + tc.ToolId + " 直径回读="
                        + d.ToString("0.####") + "（plan=" + (tc.Diameter.HasValue ? tc.Diameter.Value.ToString("0.####") : "缺") + "）");
                    if (pass) _ok++; else _fail++;
                }
                finally { mb.Destroy(); }
            }
            catch (Exception e) { Log("  FAIL " + tc.ToolId + " 回读异常: " + e.Message); _fail++; }
        }

        // MCS 原点回读（vs plan setups）
        foreach (GeometryChainCommand gc in rp.Setups)
        {
            NCGroup mcs = FindChildByName(cam.GetRoot(CAMSetup.View.Geometry), gc.McsGroupName);
            if (mcs == null) { Log("  FAIL MCS 组未找到: " + gc.McsGroupName); _fail++; continue; }
            try
            {
                MillOrientGeomBuilder ob = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(mcs);
                try
                {
                    CartesianCoordinateSystem cs = ob.Mcs;
                    string actual = cs == null ? "(null)" : string.Format("({0:0.######},{1:0.######},{2:0.######})",
                        cs.Origin.X, cs.Origin.Y, cs.Origin.Z);
                    string expect = gc.McsOrigin == null ? "(默认)" : string.Format("({0:0.######},{1:0.######},{2:0.######})",
                        gc.McsOrigin[0], gc.McsOrigin[1], gc.McsOrigin[2]);
                    bool pass = gc.McsOrigin != null && cs != null
                        && Math.Abs(cs.Origin.X - gc.McsOrigin[0]) < 1e-6
                        && Math.Abs(cs.Origin.Y - gc.McsOrigin[1]) < 1e-6
                        && Math.Abs(cs.Origin.Z - gc.McsOrigin[2]) < 1e-6;
                    Log("  " + (pass ? "PASS" : "FAIL") + " MCS 原点 " + gc.McsGroupName + " 回读=" + actual + "（plan=" + expect + "）");
                    if (pass) _ok++; else _fail++;
                    int fx = ob.FixtureOffsetBuilder.Value;
                    bool fxPass = gc.FixtureOffset.HasValue && fx == gc.FixtureOffset.Value;
                    Log("  " + (fxPass ? "PASS" : (gc.FixtureOffset.HasValue ? "FAIL" : "(未设对照)"))
                        + " fixture=" + fx + "（plan=" + (gc.FixtureOffset.HasValue ? gc.FixtureOffset.Value.ToString() : "缺") + "）");
                    if (gc.FixtureOffset.HasValue) { if (fxPass) _ok++; else _fail++; }
                }
                finally { ob.Destroy(); }
            }
            catch (Exception e) { Log("  FAIL MCS 回读异常: " + e.Message); _fail++; }
        }
    }

    private static void CollectOps(NCGroup g, List<Operation> acc)
    {
        foreach (CAMObject m in g.GetMembers())
        {
            if (m is Operation) acc.Add((Operation)m);
            else if (m is NCGroup) CollectOps((NCGroup)m, acc);
        }
    }

    // ---- 工具 ----
    private static Matrix3x3 BuildMatrix(double[] xAxis, double[] zAxis)
    {
        // Element 行语义（实测）：row0=X、row2=Z；Y = Z×X 归一
        double[] z = new double[] { zAxis[0], zAxis[1], zAxis[2] };
        double[] x = new double[] { xAxis[0], xAxis[1], xAxis[2] };
        double[] y = Cross(z, x);
        var m = new Matrix3x3();
        m.Xx = x[0]; m.Xy = x[1]; m.Xz = x[2];
        m.Yx = y[0]; m.Yy = y[1]; m.Yz = y[2];
        m.Zx = z[0]; m.Zy = z[1]; m.Zz = z[2];
        return m;
    }

    private static double[] Cross(double[] a, double[] b)
    {
        return new double[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        };
    }

    private static NCGroup FindChildByName(NCGroup g, string name)
    {
        try
        {
            if (g == null) return null;
            foreach (CAMObject m in g.GetMembers())
                if (m is NCGroup && m.Name == name) return (NCGroup)m;
        }
        catch (Exception e) { Log("  FindChildByName(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static void Step(string label, Action act)
    {
        Log("");
        Log("== 阶段: " + label + " ==");
        try { act(); _ok++; }
        catch (Exception e)
        {
            _fail++;
            Log("  !! 阶段异常: " + e.Message);
            if (e.InnerException != null) Log("     inner: " + e.InnerException.Message);
        }
    }

    private static void R(string label, Func<string> f)
    {
        try { Log("  " + label + " = " + f()); }
        catch (Exception e) { Log("  " + label + " 异常: " + e.GetType().Name + " " + e.Message); }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
