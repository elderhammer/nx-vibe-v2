// CamWriteProbe.cs — 步骤 0 实证件 #2：空 Part 创建侧冒烟（建 CAMSetup → 四组 → 一条
// CAVITY_MILL → Builder 四形态参数读写 → Commit → 回读生效值）
//
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camwrite.txt）。
// 只读约束不适用——本件在内存空 Part 上创建对象，但**不落盘保存**，退出后部件留在会话可手工关闭。
//
// API 事实（2026-09-03 本机反射实证，见 docs/nx2406-install-index.md §2.1-2.3）：
//   Part.Units.Millimeters；Part.CreateCamSetup(string)（模板字面量 "mill_contour" 待实测）；
//   NCGroupCollection.CreateProgram/CreateTool/CreateGeometry/CreateMethod
//     (NCGroup parent, string typeName, string subtypeName, NCGroupCollection.UseDefaultName, string name)
//     —— typeName 字面量（如 "MainProgram"/"MillingTool"/"MCS"/"MILL_ROUGH"）待实测；
//   OperationCollection.Create(4 父组, typeName, subtypeName, OperationCollection.UseDefaultName, name)
//     —— "CAVITY_MILL" 字面量待实测；工厂 CreateCavityMillingBuilder(operation)；
//   参数四形态（附 A）：PartStock/DepthPerCut .Value；BoundaryInTol 直接 double；
//   CutOrder 直接枚举（CutParametersCutOrderTypes.LevelFirst）；CutDirection.Type 类+嵌套枚举；
//   StepoverType + PercentToolFlatBuilder.Value 链路（待实测项）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
// 消歧：NXOpen.CAM.Path 与 System.IO.Path、NXOpen.Operation 与 NXOpen.CAM.Operation 同名
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamWriteProbe
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camwrite.txt";

    private static readonly List<string> _lines = new List<string>();
    private static int _ok, _fail;

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        Session theSession = null;
        Part part = null;
        try
        {
            _lines.Add("== CamWriteProbe ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            theSession = Session.GetSession();
            var parts = theSession.Parts;
            // 阶段 1：空 Part（内存，不落盘）
            Step("新建空 Part", () =>
            {
                part = parts.NewDisplay("CamWriteProbe", Part.Units.Millimeters);
                _lines.Add("  part=" + part.Name);
            });

            CAMSetup cam = null;
            // 阶段 2：CreateCamSetup（模板字面量待实测；"mill_contour" 为主试，失败再试 "mill_planar"）
            Step("CreateCamSetup", () =>
            {
                try { cam = part.CreateCamSetup("mill_contour"); }
                catch (Exception e)
                {
                    _lines.Add("  mill_contour 失败: " + e.Message);
                    cam = part.CreateCamSetup("mill_planar");
                    _lines.Add("  改用 mill_planar 成功");
                }
                _lines.Add("  CAMSetup ok=" + (cam != null));
            });
            if (cam == null) { Note("CAMSetup 创建失败，后续跳过"); Finish(outPath); return; }

            // 阶段 3：观察模板化初始化后的根组形态（重建侧 Executor 依据）
            Step("模板初始化后根组清单", () =>
            {
                foreach (object v in Enum.GetValues(typeof(CAMSetup.View)))
                {
                    CAMSetup.View view = (CAMSetup.View)v;
                    NCGroup root = cam.GetRoot(view);
                    string kids = "";
                    CAMObject[] ms;
                    try { ms = root.GetMembers(); }
                    catch { ms = new CAMObject[0]; }
                    foreach (CAMObject m in ms)
                        kids += (kids.Length > 0 ? ", " : "") + m.Name + "(" + SafeNameOfType(m) + ")";
                    _lines.Add("  view=" + view + "  root=" + root.Name + "  members=" + kids);
                }
            });

            // 阶段 4：建四组（typeName 字面量待实测，逐个独立尝试，失败不阻断）
            NCGroup prog = null, method = null, tool = null, geom = null;
            Step("建四组", () =>
            {
                NCGroupCollection g = cam.CAMGroupCollection;
                CAMSetup.View pv = CAMSetup.View.ProgramOrder, mv = CAMSetup.View.MachineMethod,
                              tv = CAMSetup.View.MachineTool, gv = CAMSetup.View.Geometry;
                prog   = TryCreate(() => g.CreateProgram(cam.GetRoot(pv), "MainProgram", "",
                        NCGroupCollection.UseDefaultName.True, "PROBE_PROG"), "CreateProgram MainProgram");
                method = TryCreate(() => g.CreateMethod(cam.GetRoot(mv), "MILL_ROUGH", "",
                        NCGroupCollection.UseDefaultName.True, "PROBE_MILL_ROUGH"), "CreateMethod MILL_ROUGH");
                tool   = TryCreate(() => g.CreateTool(cam.GetRoot(tv), "MillingTool", "",
                        NCGroupCollection.UseDefaultName.True, "PROBE_T1"), "CreateTool MillingTool");
                geom   = TryCreate(() => g.CreateGeometry(cam.GetRoot(gv), "MCS", "",
                        NCGroupCollection.UseDefaultName.True, "PROBE_MCS"), "CreateGeometry MCS");
            });

            // 阶段 5：建一条 CAVITY_MILL 操作（typeName 字面量待实测）
            Operation op = null;
            Step("Create CAVITY_MILL", () =>
            {
                if (prog == null || method == null || tool == null || geom == null)
                    throw new Exception("四组不全，跳过操作创建");
                op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                    "CAVITY_MILL", "", OperationCollection.UseDefaultName.True, "CAVITY_PROBE");
                _lines.Add("  op=" + op.Name + "  type=" + SafeNameOfType(op));
            });

            // 阶段 6：Builder 四形态写参数（各参数独立 try，失败不阻断后续形态）
            if (op != null) Step("四形态参数写入", () => WriteParams(cam, op));

            // 阶段 7：重开 Builder 回读（显式设的 + 未设的 FloorStock——探"生效值"可读性）
            if (op != null) Step("回读", () => ReadBack(cam, op));

            _lines.Add("");
            _lines.Add("== 汇总 ==");
            _lines.Add("ok=" + _ok + "  fail=" + _fail);
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    // ---- 阶段 6 ----
    private static void WriteParams(CAMSetup cam, Operation op)
    {
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            TryParam("形态1 .Value:  PartStock=0.3", () => b.CutParameters.PartStock.Value = 0.3);
            TryParam("形态1 .Value:  DepthPerCut=2.0", () => b.DepthPerCut.Value = 2.0);
            TryParam("形态2 直接double: BoundaryInTol=0.01", () => b.CutParameters.BoundaryInTol = 0.01);
            TryParam("形态3 直接枚举: CutOrder=LevelFirst",
                () => b.CutParameters.CutOrder = CutParametersCutOrderTypes.LevelFirst);
            TryParam("形态4 类+嵌套: CutDirection.Type=Climb",
                () => b.CutParameters.CutDirection.Type = CutDirection.Types.Climb);
            TryParam("步距链: StepoverType=PercentToolFlat",
                () => b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat);
            TryParam("步距链: PercentToolFlatBuilder.Value=50",
                () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0);
            Step("Commit", () => { b.Commit(); _lines.Add("  commit 成功"); });
        }
        finally { b.Destroy(); }
    }

    // ---- 阶段 7 ----
    private static void ReadBack(CAMSetup cam, Operation op)
    {
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            Note("回读（显式值）:");
            TryRead("PartStock.Value", () => b.CutParameters.PartStock.Value.ToString("0.####"));
            TryRead("DepthPerCut.Value", () => b.DepthPerCut.Value.ToString("0.####"));
            TryRead("BoundaryInTol", () => b.CutParameters.BoundaryInTol.ToString("0.####"));
            TryRead("CutOrder", () => b.CutParameters.CutOrder.ToString());
            TryRead("CutDirection.Type", () => b.CutParameters.CutDirection.Type.ToString());
            TryRead("StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            TryRead("PercentToolFlat.Value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            Note("回读（未显式设置——探继承生效值）:");
            TryRead("FloorStock.Value", () => b.CutParameters.FloorStock.Value.ToString("0.####"));
        }
        finally { b.Destroy(); }
    }

    // ---- 工具 ----
    private static NCGroup TryCreate(Func<NCGroup> f, string label)
    {
        try { NCGroup g = f(); _lines.Add("  OK   " + label + " -> " + g.Name); return g; }
        catch (Exception e) { _lines.Add("  FAIL " + label + " : " + e.Message); return null; }
    }

    private static void TryParam(string label, Action act)
    {
        try { act(); _lines.Add("  OK   " + label); }
        catch (Exception e) { _lines.Add("  FAIL " + label + " : " + e.Message); }
    }

    private static void TryRead(string label, Func<string> f)
    {
        try { _lines.Add("  READ " + label + " = " + f()); }
        catch (Exception e) { _lines.Add("  READ " + label + " 异常: " + e.Message); }
    }

    private static void Step(string label, Action act)
    {
        _lines.Add("");
        _lines.Add("== 阶段: " + label + " ==");
        try { act(); _ok++; }
        catch (Exception e)
        {
            _fail++;
            _lines.Add("  !! 阶段异常: " + e.Message);
            if (e.InnerException != null) _lines.Add("     inner: " + e.InnerException.Message);
        }
    }

    private static void Note(string s) { _lines.Add("  " + s); }

    private static string SafeNameOfType(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch (Exception ex) { return "(GetNameOfType 异常: " + ex.Message + ")"; }
    }

    private static void Finish(string outPath)
    {
        try { File.WriteAllLines(outPath, _lines.ToArray()); }
        catch (Exception ex)
        {
            string fallback = Path.Combine(Path.GetTempPath(), "camwrite-fallback.txt");
            try { File.WriteAllLines(fallback, _lines.ToArray()); }
            catch { /* 无处可写时放弃 */ }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fallback);
        }
    }
}
