// CamProbeDrill.cs — 步骤 0 实证件 #3：孔加工创建侧（hole_making 模板对 + HoleDrillingBuilder）
// + 刀路生成/回读（在 samples/test.prt 上对 CAVITY_MILL 与打点各生成一次并读时间/长度）
//
// 背景（2026-09-03）：实证件 #2 已实证 Create* 的 (typeName=模板部件名, subtypeName=对象类型)
// 语义与四形态读写（CAVITY_MILL 全链通过）。本件扩展两个未知面：
//   A. 孔加工：hole_making 部件下 DRILLING/SPOT_DRILLING 模板对 + HoleDrillingBuilder 基础读写；
//   B. 刀路：CAMSetup.GenerateToolPath(CAMObject[])（research §3.11）+ Operation.GetToolpathTime()/
//      GetToolpathLength() ——空部件无几何无法出刀路，故在含实体与工序的 test.prt 上做（不保存）。
//
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。test.prt 只生成内存刀路不落盘。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camprobe-drill.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeDrill
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-drill.txt";
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static readonly List<string> _lines = new List<string>();

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        try
        {
            _lines.Add("== CamProbeDrill ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Session theSession = Session.GetSession();
            var parts = theSession.Parts;

            SectionCreateHole(parts);        // A. 孔加工创建侧（空 Part）
            SectionToolpath(parts);          // B. 刀路生成/回读（test.prt）
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    // ---- A. 孔加工创建侧 ----
    private static void SectionCreateHole(PartCollection parts)
    {
        Part part = parts.NewDisplay("CamProbeDrill", Part.Units.Millimeters);
        CAMSetup cam = part.CreateCamSetup("mill_contour");
        _lines.Add("");
        _lines.Add("== A. 孔加工创建侧（空 Part, mill_contour）==");
        NCGroupCollection g = cam.CAMGroupCollection;
        NCGroup prog = g.CreateProgram(cam.GetRoot(CAMSetup.View.ProgramOrder),
            "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "PROBE_PROG");
        NCGroup method = g.CreateMethod(cam.GetRoot(CAMSetup.View.MachineMethod),
            "mill_contour", "DRILL_METHOD", NCGroupCollection.UseDefaultName.False, "PROBE_DRILL_METHOD");
        NCGroup tool = g.CreateTool(cam.GetRoot(CAMSetup.View.MachineTool),
            "hole_making", "STD_DRILL", NCGroupCollection.UseDefaultName.False, "PROBE_DRILL_8.5");
        NCGroup geom = g.CreateGeometry(cam.GetRoot(CAMSetup.View.Geometry),
            "mill_contour", "WORKPIECE", NCGroupCollection.UseDefaultName.False, "PROBE_MCS");
        _lines.Add("  组创建: " + (prog != null && method != null && tool != null && geom != null ? "OK" : "失败"));

        // 钻头刀具 Builder 参数（直径）
        R("CreateDrillStdToolBuilder -> 刀具直径", () =>
        {
            DrillStdToolBuilder tb = g.CreateDrillStdToolBuilder(tool);
            try { tb.TlDiameterBuilder.Value = 8.5; tb.Commit(); return "ok(8.5)"; }
            finally { tb.Destroy(); }
        });

        // DRILLING 操作创建（候选对；打点=SPOT_DRILLING 留作对照）
        string[][] pairs = { new[] { "hole_making", "DRILLING" }, new[] { "hole_making", "SPOT_DRILLING" } };
        foreach (string[] p in pairs)
        {
            try
            {
                Operation op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                    p[0], p[1], OperationCollection.UseDefaultName.False, "PROBE_DRILL");
                _lines.Add("  OK Create (" + p[0] + ", " + p[1] + ") -> " + op.Name + "  GetNameOfType=" + NameOfType(op));
                // Builder：HoleDrillingBuilder 优先，失败走通用 CreateBuilder 看类型
                HoleDrillingBuilder b = null;
                try { b = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op); }
                catch (Exception e) { _lines.Add("  CreateHoleDrillingBuilder 失败: " + e.Message); }
                if (b == null)
                {
                    OperationBuilder gb = cam.CAMOperationCollection.CreateBuilder(op);
                    _lines.Add("  通用 CreateBuilder -> " + gb.GetType().FullName);
                    gb.Destroy();
                    continue;
                }
                try
                {
                    P("  BottomStock.Value=0.5", () => b.CuttingParameters.BottomStock.Value = 0.5);
                    R("  CycleTable 类型", () => (b.CycleTable == null ? "(null)" : b.CycleTable.GetType().FullName));
                    R("  Commit", () => { b.Commit(); return "ok"; });
                }
                finally { b.Destroy(); }
                // 回读
                HoleDrillingBuilder b2 = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
                try { R("  回读 BottomStock.Value", () => b2.CuttingParameters.BottomStock.Value.ToString("0.####")); }
                finally { b2.Destroy(); }
            }
            catch (Exception e) { _lines.Add("  FAIL (" + p[0] + ", " + p[1] + ") : " + e.Message); }
        }
    }

    // ---- B. test.prt 刀路生成/回读 ----
    private static void SectionToolpath(PartCollection parts)
    {
        _lines.Add("");
        _lines.Add("== B. test.prt 刀路（内存生成，不保存）==");
        PartLoadStatus ls;
        Part part = parts.OpenDisplay(TestPart, out ls);
        parts.SetWork(part);
        CAMSetup cam = part.CAMSetup;
        if (cam == null) { Note("test.prt 无 CAMSetup"); return; }
        foreach (string opName in new[] { "CAVITY_MILL", "打点_COPY_COPY_COPY" })
        {
            Operation op = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), opName);
            if (op == null) { Note("未找到操作: " + opName); continue; }
            Note("-- " + opName + " (GetNameOfType=" + NameOfType(op) + ")");
            R("  GenerateToolPath", () =>
            {
                cam.GenerateToolPath(new CAMObject[] { op });
                return "ok";
            });
            R("  GetToolpathTime (min)", () => op.GetToolpathTime().ToString("0.####"));
            R("  GetToolpathLength (mm)", () => op.GetToolpathLength().ToString("0.####"));
        }
    }

    // 递归按名找 Operation
    private static Operation FindOp(NCGroup group, string name)
    {
        try
        {
            foreach (CAMObject m in group.GetMembers())
            {
                if (m is Operation && m.Name == name) return (Operation)m;
                if (m is NCGroup)
                {
                    Operation hit = FindOp((NCGroup)m, name);
                    if (hit != null) return hit;
                }
            }
        }
        catch (Exception e) { _lines.Add("  FindOp(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static string NameOfType(CAMObject o)
    {
        try { return o.GetNameOfType(); }
        catch (Exception e) { return "(异常: " + e.Message + ")"; }
    }

    private static void Note(string s) { _lines.Add("  " + s); }

    private static void P(string label, Action act)
    {
        try { act(); _lines.Add("  OK   " + label); }
        catch (Exception e) { _lines.Add("  FAIL " + label + " : " + e.Message); }
    }

    private static void R(string label, Func<string> f)
    {
        try { _lines.Add("  " + label + " = " + f()); }
        catch (Exception e) { _lines.Add("  " + label + " 异常: " + e.Message); }
    }

    private static void Finish(string outPath)
    {
        try { File.WriteAllLines(outPath, _lines.ToArray()); }
        catch (Exception ex)
        {
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-drill-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
