// CamProbeExecutor.cs — PlanExecutor 预检探针（2026-09-04，run_journal 无界面批处理驱动）
//
// 目标（docs/nx-plan-executor-spec.md §7 决策探针 + A5 写路径灭雷）：
//   P1 库刀具子类型读回：test.prt 六把库刀具 CutterSubtype 是否可读（U-7/D-2 可行性）
//   P2 新建刀具写链：建 (mill_planar,MILL)/(hole_making,STD_DRILL) 组 → 写直径/刃数 → 重开回读
//   P3 MCS/csys 写入：空件构造 CartesianCoordinateSystem 赋 MillOrientGeomBuilder.Mcs → 回读原点
//   P4 FixtureOffset 写入往返（PRE-4 白名单 [I] → 实证）
//   P5 方法父变体：op 建在方法根 vs 模板默认 MILL_ROUGH 组下（method_ref 锚点规则实证）
//   P6 CreateCamSetup("hole_making") 字面量（全钻 plan 模板选择，[I]-5）
//
// 纪律：写侧全在内存空 Part（不保存）；test.prt 只读。批处理 CAM 会话顺序（索引 §2.1）：
// NewDisplay → Session.CreateCamSession() → CreateCamSetup。每行即时落盘。
// 输出：samples\camprobe-executor-<ts>.txt（args[0] 可覆盖）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeExecutor
{
    private static string _out;
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static Part _testPart;      // 会话内缓存（避免二次 Open 943006）
    private static int _ok, _fail;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-executor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeExecutor ==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        try
        {
            s = Session.GetSession();
            Part part = null;
            CAMSetup cam = null;
            Step("S0 空 Part 写环境（顺序：建件→CAM 会话→CreateCamSetup）", () =>
            {
                part = s.Parts.NewDisplay("CamProbeExecutor", Part.Units.Millimeters);
                if (!s.IsCamSessionInitialized())
                {
                    s.CreateCamSession();
                    Log("  CreateCamSession OK");
                }
                cam = part.CreateCamSetup("mill_contour");
                Log("  CAMSetup=" + (cam != null) + "  GetRoot(ProgramOrder)="
                    + cam.GetRoot(CAMSetup.View.ProgramOrder).Name);
            });
            if (cam == null) { Note("S0 失败，中止"); return; }

            Step("S1 新建刀具写链（P2）", () => SectionWriteTools(cam));
            Step("S2 MCS/csys + FixtureOffset（P3/P4）", () => SectionMcsWrite(cam));
            Step("S3 test.prt 库刀具 CutterSubtype 读回（P1）", () => SectionRealTools(s));
            Step("S4 方法父变体 op 创建（P5）", () => SectionMethodParents(cam));
            Step("S5 CreateCamSetup(\"hole_making\") 字面量（P6）", () => SectionHoleMakingTemplate(s));
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ================= S1 (P2)：新建刀具写链 =================
    private static void SectionWriteTools(CAMSetup cam)
    {
        NCGroup mill = TryCreateGroup(cam, CAMSetup.View.MachineTool, "mill_planar", "MILL", "PLAN_MILL_D10");
        NCGroup drill = TryCreateGroup(cam, CAMSetup.View.MachineTool, "hole_making", "STD_DRILL", "PLAN_DRL_8.5");
        if (mill != null)
        {
            Note("-- 铣刀组写链（TlDiameter=10, TlNumFlutes=4）");
            MillingToolBuilder mb = null;
            try { mb = cam.CAMGroupCollection.CreateMillToolBuilder(mill) as MillingToolBuilder; }
            catch (Exception e) { Note("  CreateMillToolBuilder 异常: " + e.Message); }
            if (mb != null)
            {
                try
                {
                    P("写 TlDiameterBuilder.Value=10", () => mb.TlDiameterBuilder.Value = 10.0);
                    P("写 TlNumFlutesBuilder.Value=4", () => mb.TlNumFlutesBuilder.Value = 4);
                    R("Commit", () => { mb.Commit(); return "ok"; });
                }
                finally { mb.Destroy(); }
                MillingToolBuilder mb2 = cam.CAMGroupCollection.CreateMillToolBuilder(mill) as MillingToolBuilder;
                if (mb2 != null)
                {
                    try
                    {
                        R("重开 TlDiameter.Value", () => mb2.TlDiameterBuilder.Value.ToString("0.####"));
                        R("重开 TlNumFlutes.Value", () => mb2.TlNumFlutesBuilder.Value.ToString());
                        R("重开 运行时类型", () => mb2.GetType().FullName);
                        R("as MillToolBuilder CutterSubtype", () =>
                        {
                            MillToolBuilder mt = mb2 as MillToolBuilder;
                            return mt == null ? "(null——运行时非 MillToolBuilder)" : mt.CutterSubtype.ToString();
                        });
                    }
                    finally { mb2.Destroy(); }
                }
            }
        }
        if (drill != null)
        {
            Note("-- 钻头组写链（TlDiameter=8.5）");
            MillingToolBuilder db = null;
            try { db = cam.CAMGroupCollection.CreateDrillStdToolBuilder(drill) as MillingToolBuilder; }
            catch (Exception e) { Note("  CreateDrillStdToolBuilder 异常: " + e.Message); }
            if (db != null)
            {
                try
                {
                    P("写 TlDiameterBuilder.Value=8.5", () => db.TlDiameterBuilder.Value = 8.5);
                    R("Commit", () => { db.Commit(); return "ok"; });
                }
                finally { db.Destroy(); }
                MillingToolBuilder db2 = cam.CAMGroupCollection.CreateDrillStdToolBuilder(drill) as MillingToolBuilder;
                if (db2 != null)
                {
                    try
                    {
                        R("重开 TlDiameter.Value", () => db2.TlDiameterBuilder.Value.ToString("0.####"));
                        R("重开 运行时类型", () => db2.GetType().FullName);
                    }
                    finally { db2.Destroy(); }
                }
            }
        }
    }

    // ================= S2 (P3/P4)：MCS/csys 写入 + FixtureOffset =================
    private static void SectionMcsWrite(CAMSetup cam)
    {
        // 取模板默认 MCS_MILL 的 csys 作源（origin+matrix 读回）
        NCGroup defMcs = FindMcs(cam.GetRoot(CAMSetup.View.Geometry));
        if (defMcs == null) { Note("  默认 MCS_MILL 未找到"); return; }
        Point3d o0 = new Point3d();
        Matrix3x3 m0 = new Matrix3x3();
        CartesianCoordinateSystem srcCs = null;
        MillOrientGeomBuilder ob0 = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(defMcs);
        try
        {
            R("源 MCS csys 读回(仅参考)", () =>
            {
                srcCs = ob0.Mcs;
                if (srcCs == null) return "(Mcs null)";
                return string.Format("o=({0:0.###},{1:0.###},{2:0.###})", srcCs.Origin.X, srcCs.Origin.Y, srcCs.Origin.Z);
            });
        }
        finally { ob0.Destroy(); }
        // 非零往返目标：plan 真实值原点 (75,0,100) + 单位阵（X=(1,0,0), Y=(0,1,0), Z=(0,0,1)）
        o0 = new Point3d(75.0, 0.0, 100.0);
        m0 = new Matrix3x3();
        m0.Xx = 1.0; m0.Yy = 1.0; m0.Zz = 1.0;
        Log("  目标 csys：origin=(75,0,100) + 单位阵");

        // P3a：构造新 csys（CreateCoordinateSystem(Point3d, Matrix3x3, bool)）
        CartesianCoordinateSystem newCs = null;
        P("Part.CoordinateSystems.CreateCoordinateSystem(o, matrix, false)", () =>
        {
            newCs = part_CoordSysCreate(cam, o0, m0);
        });
        R("新 csys 类型", () => (newCs == null ? "(null)" : newCs.GetType().FullName));

        // P3b：新建 MCS 几何组（优先 (mill_contour,MCS)，回退 WORKPIECE）并把 csys 赋给 builder.Mcs
        NCGroup mcsGroup = TryCreateGroup(cam, CAMSetup.View.Geometry, "mill_contour", "MCS", "PLAN_MCS");
        if (mcsGroup == null)
            mcsGroup = TryCreateGroup(cam, CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "PLAN_MCS");
        NCGroup target = mcsGroup != null ? mcsGroup : defMcs;   // 兜底写默认组（不保存无碍）
        MillOrientGeomBuilder ob = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(target);
        try
        {
            if (newCs != null)
            {
                P("赋 Mcs = 新 csys", () => { ob.Mcs = newCs; });
                R("Commit", () => { ob.Commit(); return "ok"; });
            }
        }
        finally { ob.Destroy(); }
        if (newCs != null)
        {
            MillOrientGeomBuilder ob2 = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(target);
            try
            {
                R("重开 Mcs(原点+Z/X 行)", () =>
                {
                    CartesianCoordinateSystem cs = ob2.Mcs;
                    if (cs == null) return "(Mcs null)";
                    Matrix3x3 el = cs.Orientation.Element;
                    return string.Format("o=({0:0.###},{1:0.###},{2:0.###}) X=({3:0.###},{4:0.###},{5:0.###}) Z=({6:0.###},{7:0.###},{8:0.###})",
                        cs.Origin.X, cs.Origin.Y, cs.Origin.Z,
                        el.Xx, el.Xy, el.Xz, el.Zx, el.Zy, el.Zz);
                });
            }
            finally { ob2.Destroy(); }
        }

        // P4：FixtureOffset 写入往返（白名单 [I]→实证）
        Note("-- FixtureOffset 写链（目标组 " + target.Name + "）");
        MillOrientGeomBuilder fb = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(target);
        try
        {
            P("写 FixtureOffsetBuilder.Value=2", () => fb.FixtureOffsetBuilder.Value = 2);
            R("Commit", () => { fb.Commit(); return "ok"; });
        }
        finally { fb.Destroy(); }
        MillOrientGeomBuilder fb2 = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(target);
        try
        {
            R("重开 FixtureOffset.Value", () => fb2.FixtureOffsetBuilder.Value.ToString());
            R("重开 FixtureOffset.InheritanceStatus", () => fb2.FixtureOffsetBuilder.InheritanceStatus.ToString());
        }
        finally { fb2.Destroy(); }
    }

    // 建 csys 的独立小步（类型/签名若错只影响本段）
    private static CartesianCoordinateSystem part_CoordSysCreate(CAMSetup cam, Point3d o, Matrix3x3 m)
    {
        BasePart part = (BasePart)((Part)Session.GetSession().Parts.Work);
        return part.CoordinateSystems.CreateCoordinateSystem(o, m, false);
    }

    // ================= S3 (P1)：test.prt 库刀具 CutterSubtype 读回 =================
    private static void SectionRealTools(Session s)
    {
        Part part = OpenTestPart(s);
        if (part == null) { Note("test.prt 不可用，S3 中止"); return; }
        CAMSetup cam = part.CAMSetup;
        Note("-- 六把库刀具：运行时类型 + CutterSubtype（可行则 U-7 立即可做）");
        WalkTools(cam.GetRoot(CAMSetup.View.MachineTool), cam, 0);
    }

    private static void WalkTools(NCGroup g, CAMSetup cam, int depth)
    {
        try
        {
            foreach (CAMObject m in g.GetMembers())
            {
                NCGroup sub = m as NCGroup;
                if (sub == null) continue;
                string fam = SafeNameOfType(sub);
                bool container = fam == "Generic PARAM object" || fam == "Tool Carrier" || fam == "Head" || fam == "Machine";
                if (depth >= 1 && !container)
                {
                    Note("-- 刀具组: " + sub.Name + "  家族=" + fam);
                    MillingToolBuilder mb = null;
                    try { mb = cam.CAMGroupCollection.CreateMillToolBuilder(sub) as MillingToolBuilder; }
                    catch { mb = null; }
                    if (mb == null)
                    {
                        try { mb = cam.CAMGroupCollection.CreateDrillStdToolBuilder(sub) as MillingToolBuilder; }
                        catch { mb = null; }
                    }
                    if (mb == null) { Note("   builder 打不开"); continue; }
                    try
                    {
                        R("   运行时类型", () => mb.GetType().FullName);
                        R("   TlDiameter", () => mb.TlDiameterBuilder.Value.ToString("0.####"));
                        MillToolBuilder mt = mb as MillToolBuilder;
                        R("   as MillToolBuilder", () => (mt == null ? "(null)" : "有"));
                        if (mt != null) R("   CutterSubtype", () => mt.CutterSubtype.ToString());
                    }
                    finally { mb.Destroy(); }
                }
                WalkTools(sub, cam, depth + 1);
            }
        }
        catch (Exception e) { Note("  WalkTools 异常: " + e.Message); }
    }

    // ================= S4 (P5)：方法父变体 op 创建 =================
    private static void SectionMethodParents(CAMSetup cam)
    {
        NCGroup prog = TryCreateGroup(cam, CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "PLAN_PROG");
        NCGroup methodRoot = cam.GetRoot(CAMSetup.View.MachineMethod);
        NCGroup millRough = FindGroupByName(methodRoot, "MILL_ROUGH");
        NCGroup mill = FindGroupByName(cam.GetRoot(CAMSetup.View.MachineTool), "PLAN_MILL_D10");
        NCGroup geom = FindGroupByName(cam.GetRoot(CAMSetup.View.Geometry), "PLAN_MCS");
        if (geom == null) geom = FindGroupByName(cam.GetRoot(CAMSetup.View.Geometry), "MCS_MILL");
        if (prog == null || mill == null || geom == null)
        { Note("  组不全（prog/mill/geom），S4 中止"); return; }

        Note("-- op A：方法父=方法根（test.prt ground truth 形态）");
        P("Create(方法父=methodRoot)", () =>
        {
            Operation op = cam.CAMOperationCollection.Create(prog, methodRoot, mill, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.False, "PLAN_OP_ROOT");
            Log("    -> " + op.Name);
        });
        if (millRough != null)
        {
            Note("-- op B：方法父=模板默认 MILL_ROUGH 组");
            P("Create(方法父=MILL_ROUGH)", () =>
            {
                Operation op = cam.CAMOperationCollection.Create(prog, millRough, mill, geom,
                    "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.False, "PLAN_OP_ROUGH");
                Log("    -> " + op.Name);
            });
        }
        else Note("  MILL_ROUGH 默认组未找到（模板差异？）");
    }

    // ================= S5 (P6)：hole_making 模板字面量 =================
    private static void SectionHoleMakingTemplate(Session s)
    {
        Part part = s.Parts.NewDisplay("CamProbeHole", Part.Units.Millimeters);
        P("CreateCamSetup(\"hole_making\")", () =>
        {
            CAMSetup cam = part.CreateCamSetup("hole_making");
            Log("  根组成员 MachineMethod: " + RootKids(cam, CAMSetup.View.MachineMethod));
            Log("  根组成员 MachineTool: " + RootKids(cam, CAMSetup.View.MachineTool));
        });
    }

    // ================= 工具 =================
    private static string RootKids(CAMSetup cam, CAMSetup.View view)
    {
        try
        {
            List<string> kids = new List<string>();
            foreach (CAMObject m in cam.GetRoot(view).GetMembers())
                kids.Add(m.Name + "(" + SafeNameOfType(m) + ")");
            return string.Join(", ", kids.ToArray());
        }
        catch (Exception e) { return "(异常: " + e.Message + ")"; }
    }

    private static string SafeNameOfType(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch (Exception e) { return "(GetNameOfType 异常: " + e.Message + ")"; }
    }

    private static NCGroup FindMcs(NCGroup g)
    {
        try
        {
            if (g == null) return null;
            foreach (CAMObject m in g.GetMembers())
            {
                NCGroup sub = m as NCGroup;
                if (sub == null) continue;
                if (sub.Name.StartsWith("MCS", StringComparison.Ordinal)) return sub;
                NCGroup hit = FindMcs(sub);
                if (hit != null) return hit;
            }
        }
        catch { }
        return null;
    }

    private static NCGroup FindGroupByName(NCGroup root, string name)
    {
        try
        {
            if (root == null) return null;
            foreach (CAMObject m in root.GetMembers())
                if (m is NCGroup && m.Name == name) return (NCGroup)m;
        }
        catch (Exception e) { Note("  FindGroupByName(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static NCGroup TryCreateGroup(CAMSetup cam, CAMSetup.View view, string typeName, string subtype, string name)
    {
        try
        {
            NCGroup root = cam.GetRoot(view);
            if (root == null) { Note("  根组 null (view=" + view + ")"); return null; }
            NCGroupCollection g = cam.CAMGroupCollection;
            switch (view)
            {
                case CAMSetup.View.ProgramOrder:
                    return g.CreateProgram(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
                case CAMSetup.View.MachineMethod:
                    return g.CreateMethod(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
                case CAMSetup.View.MachineTool:
                    return g.CreateTool(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
                default:
                    return g.CreateGeometry(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
            }
        }
        catch (Exception e) { Note("  组创建 " + name + " 失败: " + e.Message); return null; }
    }

    private static Part OpenTestPart(Session s)
    {
        if (_testPart != null) return _testPart;
        try
        {
            PartLoadStatus ls;
            _testPart = s.Parts.OpenDisplay(TestPart, out ls);
            return _testPart;
        }
        catch (Exception e1)
        {
            Log("  OpenDisplay(test.prt) 失败(" + e1.Message + ") → 试 Open");
            try
            {
                PartLoadStatus ls2;
                _testPart = s.Parts.Open(TestPart, out ls2);
                return _testPart;
            }
            catch (Exception e2) { Log("  Open(test.prt) 也失败: " + e2.Message); return null; }
        }
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

    private static void Note(string s) { Log("  " + s); }

    private static void P(string label, Action act)
    {
        try { act(); Log("  OK   " + label); }
        catch (Exception e) { Log("  FAIL " + label + " : " + e.Message); }
    }

    private static void R(string label, Func<string> f)
    {
        try { Log("  " + label + " = " + f()); }
        catch (Exception e) { Log("  " + label + " 异常: " + e.GetType().Name + " " + e.Message); }
    }

    // 即时追加落盘（硬崩保留阶段痕迹；日志通道失败不阻断主流程）
    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
