// CamProbeOp.cs — 步骤 0 实证件 #2（第四轮收尾）：Operation Create 用 (typeName=模板部件名,
// subtypeName=操作子类型) 语义创建 CAVITY_MILL，随后四形态写入 + Commit + 回读（含继承生效值）
//
// 背景（2026-09-03）：第三轮实证组创建 Create* 的 typeName=模板部件名（mill_contour 等）、
// subtypeName=对象类型（PROGRAM/MILL_METHOD/MILL/WORKPIECE…，CAMSession 枚举得）；
// OperationCollection.Create 曾以 typeName="CAVITY_MILL"、subtype="" 调用报"需要的模板不存在"，
// 推断与组同族语义：应为 typeName="mill_contour"、subtypeName="CAVITY_MILL"（枚举表证实该对存在）。
//
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。内存空 Part，不落盘。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camprobe-op.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeOp
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-op.txt";
    private static readonly List<string> _lines = new List<string>();

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        try
        {
            _lines.Add("== CamProbeOp ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Session theSession = Session.GetSession();
            var parts = theSession.Parts;
            Part part = parts.NewDisplay("CamProbeOp", Part.Units.Millimeters);
            CAMSetup cam = part.CreateCamSetup("mill_contour");
            _lines.Add("CreateCamSetup(mill_contour) ok");

            NCGroupCollection g = cam.CAMGroupCollection;
            NCGroup prog = g.CreateProgram(cam.GetRoot(CAMSetup.View.ProgramOrder),
                "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "PROBE_PROG");
            NCGroup method = g.CreateMethod(cam.GetRoot(CAMSetup.View.MachineMethod),
                "mill_contour", "MILL_METHOD", NCGroupCollection.UseDefaultName.False, "PROBE_METHOD");
            NCGroup tool = g.CreateTool(cam.GetRoot(CAMSetup.View.MachineTool),
                "mill_planar", "MILL", NCGroupCollection.UseDefaultName.False, "PROBE_TOOL");
            NCGroup geom = g.CreateGeometry(cam.GetRoot(CAMSetup.View.Geometry),
                "mill_contour", "WORKPIECE", NCGroupCollection.UseDefaultName.False, "PROBE_MCS");
            _lines.Add("四组创建 OK (第三轮实证的字面量对)");

            // ---- Operation Create：候选 (typeName, subtypeName) 对 ----
            Operation op = null;
            string[][] pairs = new[] {
                new[] { "mill_contour", "CAVITY_MILL" },
                new[] { "mill_planar",  "CAVITY_MILL" },   // 理论上不存在，留作对照
                new[] { "mill_contour", "cavity_mill" },
            };
            foreach (string[] p in pairs)
            {
                try
                {
                    op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                        p[0], p[1], OperationCollection.UseDefaultName.False, "CAVITY_PROBE");
                    _lines.Add("  OK  (" + p[0] + ", " + p[1] + ") -> op=" + op.Name);
                    break;
                }
                catch (Exception e) { _lines.Add("  FAIL (" + p[0] + ", " + p[1] + ") : " + e.Message); }
            }

            if (op == null) { Note("操作创建失败，后续跳过"); Finish(outPath); return; }

            // ---- 四形态写入 ----
            CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try
            {
                P("形态1 .Value: PartStock=0.3", () => b.CutParameters.PartStock.Value = 0.3);
                P("形态1 .Value: DepthPerCut=2.0", () => b.DepthPerCut.Value = 2.0);
                P("形态2 直接double: BoundaryInTol=0.01", () => b.CutParameters.BoundaryInTol = 0.01);
                P("形态3 直接枚举: CutOrder=LevelFirst",
                    () => b.CutParameters.CutOrder = CutParametersCutOrderTypes.LevelFirst);
                P("形态4 类+嵌套: CutDirection.Type=Climb",
                    () => b.CutParameters.CutDirection.Type = CutDirection.Types.Climb);
                P("步距链: StepoverType=PercentToolFlat",
                    () => b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat);
                P("步距链: PercentToolFlatBuilder.Value=50",
                    () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0);
                R("Commit", () => { b.Commit(); return "ok"; });
            }
            finally { b.Destroy(); }

            // ---- 回读（显式值 + 未设 FloorStock 的继承生效值探针）----
            CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try
            {
                R("回读 PartStock.Value", () => b2.CutParameters.PartStock.Value.ToString("0.####"));
                R("回读 DepthPerCut.Value", () => b2.DepthPerCut.Value.ToString("0.####"));
                R("回读 BoundaryInTol", () => b2.CutParameters.BoundaryInTol.ToString("0.####"));
                R("回读 CutOrder", () => b2.CutParameters.CutOrder.ToString());
                R("回读 CutDirection.Type", () => b2.CutParameters.CutDirection.Type.ToString());
                R("回读 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
                R("回读 PercentToolFlat.Value", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
                R("回读 FloorStock.Value(未显式设=继承生效值?)", () => b2.CutParameters.FloorStock.Value.ToString("0.####"));
            }
            finally { b2.Destroy(); }
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
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
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-op-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
