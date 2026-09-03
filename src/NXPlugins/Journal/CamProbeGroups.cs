// CamProbeGroups.cs — 步骤 0 实证件 #2（第二轮）：组创建 typeName 字面量候选矩阵
//
// 背景（2026-09-03）：实证件 #2 第一轮已实证 CreateCamSetup("mill_contour") 成功且模板自带
// 默认组（MILL_* 方法组 / MCS_MILL / PROGRAM / GENERIC_MACHINE）；四组创建用猜测字面量
// （"MainProgram"/"MILL_ROUGH"/"MillingTool"/"MCS"）全部报"需要的模板不存在"。
// 官方样例与 NXOpen.xml remarks 均无字面量样例 → 候选取自模板部件二进制内 ASCII token
// （mach\resource\template_part\metric\*.prt，如 MILL_7_PARAMETER / SPOTDRILLING_TOOL /
// MCS_MAIN / NCGEOM / BALL_MILL…），逐候选尝试，成功即止。
//
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。内存空 Part，不落盘。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camprobe-groups.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeGroups
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-groups.txt";
    private static readonly List<string> _lines = new List<string>();

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        try
        {
            _lines.Add("== CamProbeGroups ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Session theSession = Session.GetSession();
            var parts = theSession.Parts;
            Part part = parts.NewDisplay("CamProbeGroups", Part.Units.Millimeters);
            CAMSetup cam = part.CreateCamSetup("mill_contour");   // 第一轮已实证
            _lines.Add("CreateCamSetup(mill_contour) ok");

            NCGroupCollection g = cam.CAMGroupCollection;
            CAMSetup.View pv = CAMSetup.View.ProgramOrder, mv = CAMSetup.View.MachineMethod,
                          tv = CAMSetup.View.MachineTool, gv = CAMSetup.View.Geometry;

            // ---- 候选矩阵：逐候选尝试，成功即止 ----
            string[] progCands = { "PROGRAM", "NC_PROGRAM", "main", "MAIN" };
            string[] methodCands = { "MILL_METHOD", "MILL_ROUGH", "mill_rough", "DRILL_METHOD" };
            string[] toolCands = { "BALL_MILL", "MILL_7_PARAMETER", "MILL_10_PARAMETER",
                                   "SPOTDRILLING_TOOL", "SPOTFACING_TOOL", "THREAD_MILL", "DRILL", "drill" };
            string[] geomCands = { "MCS_MILL", "MCS_MAIN", "WORKPIECE", "NCGEOM", "MCS" };

            NCGroup prog = TryMatrix(g, SafeRoot(cam, pv, "ProgramOrder"), "CreateProgram",  progCands, "PROBE_PROG");
            NCGroup method = TryMatrix(g, SafeRoot(cam, mv, "MachineMethod"), "CreateMethod", methodCands, "PROBE_METHOD");
            NCGroup tool = TryMatrix(g, SafeRoot(cam, tv, "MachineTool"), "CreateTool",    toolCands, "PROBE_TOOL");
            NCGroup geom = TryMatrix(g, SafeRoot(cam, gv, "Geometry"), "CreateGeometry", geomCands, "PROBE_MCS");

            // ---- 失败种类的兜底：用模板自带组（第一轮实证存在）----
            _lines.Add("");
            _lines.Add("== 兜底：模板自带组 ==");
            if (prog == null)   prog = FindChild(cam.GetRoot(pv), "PROGRAM");
            if (method == null) method = FindChild(cam.GetRoot(mv), "MILL_ROUGH");
            if (method == null) method = FindChild(cam.GetRoot(mv), "MILL_METHOD");
            if (geom == null)   geom = FindChild(cam.GetRoot(gv), "MCS_MILL");
            _lines.Add("  兜底结果: prog=" + NameOrNull(prog) + " method=" + NameOrNull(method)
                       + " tool=" + NameOrNull(tool) + " geom=" + NameOrNull(geom));

            // ---- 建操作 ----
            Operation op = null;
            if (prog != null && method != null && tool != null && geom != null)
            {
                _lines.Add("");
                _lines.Add("== Create 操作: 候选 CAVITY_MILL / cavity_mill ==");
                foreach (string tn in new[] { "CAVITY_MILL", "cavity_mill" })
                {
                    try
                    {
                        op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                            tn, "", OperationCollection.UseDefaultName.True, "CAVITY_PROBE");
                        _lines.Add("  OK typeName=" + tn + " -> op=" + op.Name);
                        break;
                    }
                    catch (Exception e) { _lines.Add("  FAIL typeName=" + tn + " : " + e.Message); }
                }
            }
            else _lines.Add("  组不全，跳过操作创建");

            if (op != null)
            {
                _lines.Add("");
                _lines.Add("== 四形态参数写入与回读 ==");
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
                    R("Commit", () => { b.Commit(); return "commit 成功"; });
                }
                finally { b.Destroy(); }

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
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    // 矩阵：同一种组的多个候选逐个 Create，成功返回并记录
    private static NCGroup TryMatrix(NCGroupCollection g, NCGroup root,
        string kind, string[] cands, string name)
    {
        _lines.Add("");
        _lines.Add("== " + kind + " 候选 ==");
        if (root == null) { _lines.Add("  根组为 null，跳过"); return null; }
        foreach (string c in cands)
        {
            try
            {
                NCGroup ng = CreateByKind(g, kind, root, c, name);
                _lines.Add("  OK  typeName=\"" + c + "\" -> " + ng.Name + "  GetNameOfType=" + NameOfType(ng));
                return ng;
            }
            catch (Exception e) { _lines.Add("  FAIL \"" + c + "\" : " + e.Message); }
        }
        return null;
    }

    private static NCGroup SafeRoot(CAMSetup cam, CAMSetup.View view, string label)
    {
        try { NCGroup r = cam.GetRoot(view); _lines.Add("GetRoot(" + label + ") -> " + r.Name); return r; }
        catch (Exception e) { _lines.Add("GetRoot(" + label + ") 异常: " + e.Message); return null; }
    }

    private static NCGroup CreateByKind(NCGroupCollection g, string kind, NCGroup root, string tn, string name)
    {
        var udn = NCGroupCollection.UseDefaultName.True;
        switch (kind)
        {
            case "CreateProgram": return g.CreateProgram(root, tn, "", udn, name);
            case "CreateMethod": return g.CreateMethod(root, tn, "", udn, name);
            case "CreateTool": return g.CreateTool(root, tn, "", udn, name);
            default: return g.CreateGeometry(root, tn, "", udn, name);
        }
    }

    private static NCGroup FindChild(NCGroup parent, string name)
    {
        try
        {
            foreach (CAMObject m in parent.GetMembers())
            {
                NCGroup grp = m as NCGroup;
                if (grp != null && grp.Name == name) { _lines.Add("  找到模板组: " + name); return grp; }
            }
        }
        catch (Exception e) { _lines.Add("  FindChild(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static string NameOrNull(NCGroup ng) { return ng == null ? "(null)" : ng.Name; }

    private static string NameOfType(CAMObject o)
    {
        try { return o.GetNameOfType(); }
        catch (Exception e) { return "(异常: " + e.Message + ")"; }
    }

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
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
