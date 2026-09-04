// CamProbeU7.cs — U-7 收口探针（2026-09-04，run_journal 无界面批处理驱动）
//
// 目标（docs/nx-tool-type-enum-spec.md 待实测项 [T]-U7）：
//   P1 test.prt 六把库刀具：NCGroup → NXOpen.CAM.Tool 下转型 + GetTypeAndSubtype
//      (Tool.Types, Tool.Subtypes) 读回 → U-7 通道 A（组级、语言无关）运行时可行性
//   P2 新建注册对校准：scratch 件建 (mill_planar,MILL) / (hole_making,STD_DRILL) 组
//      → 读回 (Types, Subtypes) → 建立 模板 subtype 串 ↔ NX 枚举 对应表（重建映射表依据）
//   P3 对照记录：GetNameOfType 家族串 + builder 运行时类型 + CutterSubtype（P1 旧通道）
//      ——用于文档实证对照（语言敏感通道 vs 枚举通道）
//
// 纪律：写侧全在内存空 Part（不保存）；test.prt 只读。批处理 CAM 会话顺序（索引 §2.1）：
// NewDisplay → Session.CreateCamSession() → CreateCamSetup。每行即时落盘。
// 输出：samples\camprobe-u7-<ts>.txt（args[0] 可覆盖）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using CamTool = NXOpen.CAM.Tool;   // 与 System.Tool/变量名避免歧义

public class CamProbeU7
{
    private static string _out;
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static Part _testPart;      // 会话内缓存（避免二次 Open 943006）
    private static int _ok, _fail;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-u7-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeU7 ==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        try
        {
            s = Session.GetSession();
            CAMSetup cam = null;
            Step("S1 空 Part 写环境（建件→CAM 会话→CreateCamSetup）", () =>
            {
                Part part = s.Parts.NewDisplay("CamProbeU7", Part.Units.Millimeters);
                if (!s.IsCamSessionInitialized())
                {
                    s.CreateCamSession();
                    Log("  CreateCamSession OK");
                }
                cam = part.CreateCamSetup("mill_contour");
                Log("  CAMSetup=" + (cam != null));
            });
            if (cam == null) { Note("S1 失败，中止"); return; }

            Step("S2 test.prt 库刀具 GetTypeAndSubtype 读回（P1/P3）", () => SectionRealTools(s));
            Step("S3 新建注册对读回校准（P2）", () => SectionNewPairs(s, cam));
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ================= S2 (P1/P3)：test.prt 六把库刀具 =================
    private static void SectionRealTools(Session s)
    {
        Part part = OpenTestPart(s);
        if (part == null) { Note("test.prt 不可用，S2 中止"); return; }
        CAMSetup cam = part.CAMSetup;
        Note("-- 六把库刀具：as Tool + GetTypeAndSubtype（U-7 通道 A）");
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
                    Note("-- 刀具组: " + sub.Name + "  家族(GetNameOfType)=" + fam);
                    CamTool t = sub as CamTool;
                    P("  as NXOpen.CAM.Tool", () =>
                    {
                        if (t == null) throw new Exception("下转型失败（运行时非 Tool 派生）");
                    });
                    if (t != null)
                    {
                        R("  GetTypeAndSubtype", () =>
                        {
                            Tool.Types ty;
                            Tool.Subtypes sty;
                            t.GetTypeAndSubtype(out ty, out sty);
                            return ty + " / " + sty;
                        });
                    }
                    // P3 对照：builder 运行时类型 + CutterSubtype（旧通道，语言无关性对比）
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
                        R("   对照-builder 运行时类型", () => mb.GetType().FullName);
                        MillToolBuilder mt = mb as MillToolBuilder;
                        R("   对照-as MillToolBuilder", () => (mt == null ? "(null)" : "有"));
                        if (mt != null) R("   对照-CutterSubtype", () => mt.CutterSubtype.ToString());
                    }
                    finally { mb.Destroy(); }
                }
                WalkTools(sub, cam, depth + 1);
            }
        }
        catch (Exception e) { Note("  WalkTools 异常: " + e.Message); }
    }

    // ================= S3 (P2)：新建注册对读回校准 =================
    private static void SectionNewPairs(Session s, CAMSetup scratchCam)
    {
        // 执行器重建注册对（D-2 实证）：铣=(mill_planar,MILL) 钻=(hole_making,STD_DRILL)
        NCGroup mill = TryCreateGroup(scratchCam, CAMSetup.View.MachineTool, "mill_planar", "MILL", "U7_MILL_NEW");
        NCGroup drill = TryCreateGroup(scratchCam, CAMSetup.View.MachineTool, "hole_making", "STD_DRILL", "U7_DRL_NEW");
        if (mill != null) ReadPair(mill, "(mill_planar, MILL) 新建");
        if (drill != null) ReadPair(drill, "(hole_making, STD_DRILL) 新建");
        if (mill == null || drill == null) Note("  组创建不全（模板对差异？）——S3 部分完成");
    }

    private static void ReadPair(NCGroup grp, string label)
    {
        Note("-- " + label + " 组: " + grp.Name);
        CamTool t = grp as CamTool;
        P("  as NXOpen.CAM.Tool", () =>
        {
            if (t == null) throw new Exception("下转型失败（新建组运行时非 Tool 派生）");
        });
        if (t == null) return;
        R("  GetTypeAndSubtype", () =>
        {
            Tool.Types ty;
            Tool.Subtypes sty;
            t.GetTypeAndSubtype(out ty, out sty);
            return ty + " / " + sty;
        });
    }

    // ================= 工具 =================
    private static string SafeNameOfType(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch (Exception e) { return "(GetNameOfType 异常: " + e.Message + ")"; }
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
