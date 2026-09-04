// CamProbeStepExport.cs — STEP 导出/回导闭环探针（2026-09-05，run_journal 批处理驱动）
//
// 目的：闭合索引 §3 项 6 的导出侧悬置——产出仓库自建有效资产 samples/test.step
//（samples/README"test.step（计划）"转实态），并用回导验证闭环。
//
// 此前失败线索与本次修正（2026-09-05 静态实证）：
//   ① SettingsFile 方向错配——step214ug.def = "STEP to UG"（导入向配置）；同目录
//      ugstep214.def = "UG to STEP"（导出向，MODULES_MASK=Solids）——旧导出尝试全用
//      导入向 def，translator 以错误方向处理输入 → "solids input=0"疑首因。
//   ② 选体语义（.NET 反射）：StepCreator.ObjectTypes : ObjectTypeSelector（Solids 等掩码）；
//      ExportSelectionBlock : ObjectSelector（Scope: EntirePart|SelectedObjects|EntireAssembly）；
//      ExportFrom : {DisplayPart|ExistingPart}；ExportAs : {Ap203|Ap214|Ap242|Ap242ED2}；
//      FileSaveFlag=false = 文件导出模式（XML remarks）；BaseCreator.OutputFile + 可选
//      OutputFileExtension（二者交互语义未明 → 导出失败时换变体 2 拆分扩展名）。
//
// 判死：P0 test.prt 含体前提 → P1 导出（变体 1 全扩展名；无产物 → 变体 2 扩展名拆交
// OutputFileExtension）→ P2 回导闭环（Step214Importer 导入产物 → Bodies/面数对照 P0）。
//
// 输出：samples\camprobe-stepexport-<ts>.txt（args[0] 可覆盖）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeStepExport
{
    private const string SrcPrt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private const string SamplesDir = @"C:\Users\21505\Code\nx-vibe-v2\samples";
    private const string DefDir = @"C:\Program Files\Siemens\NX2406\step214ug";
    private static string _out;

    public static void Main(string[] args)
    {
        _out = Path.Combine(SamplesDir, "camprobe-stepexport-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeStepExport（STEP 导出 + 回导闭环，ugstep214.def 导出向）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        try
        {
            s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            if (!File.Exists(SrcPrt)) throw new Exception("源件缺失: " + SrcPrt);

            int srcBodies = 0, srcFaces = 0;
            Step("P0 打开源件 test.prt（取件纪律：OpenDisplay + UF SetDisplayPart + SetWork）", () =>
            {
                PartLoadStatus st;
                Part p = s.Parts.OpenDisplay(SrcPrt, out st);
                UFSession uf = UFSession.GetUFSession();
                R("uf.SetDisplayPart", () => { uf.Part.SetDisplayPart(p.Tag); return "ok"; });
                s.Parts.SetWork(p);
                srcBodies = CountSolids(p, out srcFaces);
                Log("  源件: " + p.Name + " bodies=" + srcBodies + " solidFaces=" + srcFaces);
                if (srcBodies == 0) throw new Exception("源件无 solid → 导出前提不成立");
                Log("  P0 判定: 源件含体 → 前提成立");
            });

            string exported = null;
            Step("P1 导出变体 1（OutputFile 全路径含 .step；ugstep214.def 导出向 def）", () =>
            {
                exported = TryExport(s, Path.Combine(SamplesDir, "test.step"), "");
                if (exported != null && File.Exists(exported) && new FileInfo(exported).Length > 200)
                    Log("  产物: " + exported + " (" + new FileInfo(exported).Length + " B)");
                else
                    throw new Exception("变体 1 无产物（变体 2 接续）");
            });
            if (exported == null || !File.Exists(exported))
            {
                Step("P1b 导出变体 2（OutputFile 无扩展 + OutputFileExtension=\"stp\"）", () =>
                {
                    exported = TryExport(s, Path.Combine(SamplesDir, "test-v2"), "stp");
                    if (exported != null && File.Exists(exported) && new FileInfo(exported).Length > 200)
                        Log("  产物: " + exported + " (" + new FileInfo(exported).Length + " B)");
                    else throw new Exception("变体 2 亦无产物 → 导出链未通");
                });
            }
            if (exported == null) throw new Exception("无导出产物");

            Step("P2 回导闭环（Step214Importer 导入导出产物 → 面数对照源件）", () =>
            {
                Part tgt = s.Parts.NewDisplay("StepExportRoundtrip", Part.Units.Millimeters);
                s.Parts.SetWork(tgt);
                string prtPath = Path.Combine(SamplesDir,
                    "camprobe-stepexport-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".prt");
                tgt.SaveAs(prtPath);
                Step214Importer imp = s.DexManager.CreateStep214Importer();
                try
                {
                    imp.ObjectTypes.Solids = true;
                    imp.InputFile = exported;
                    imp.OutputFile = tgt.FullPath;
                    string def = Path.Combine(DefDir, "step214ug.def");
                    if (File.Exists(def)) imp.SettingsFile = def;
                    imp.FileOpenFlag = false;
                    imp.Commit();
                }
                finally { imp.Destroy(); }
                int rtFaces;
                int rtBodies = CountSolids(tgt, out rtFaces);
                Log("  回导件: bodies=" + rtBodies + " solidFaces=" + rtFaces + "（源件 " + srcBodies + "/" + srcFaces + "）");
                if (rtBodies == 0 || rtFaces == 0)
                    throw new Exception("回导无几何 → 产物无效");
                if (rtBodies != srcBodies || rtFaces != srcFaces)
                    Log("  回导体/面数与源件不符（产物几何与源件不一致？translator *_log 复核）");
                else
                    Log("  回导体/面数与源件一致 → 产物保真");
                Log("  P2 判定: 回导含体且面数一致 → 产物有效 → α（导出闭环通）");
            });
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // 单次导出尝试：返回产物路径（存在即返回），异常/无产物 → 返回 null
    private static string TryExport(Session s, string outPath, string ext)
    {
        try
        {
            StepCreator cr = s.DexManager.CreateStepCreator();
            string filePath = outPath;
            try
            {
                R("ExportAs = Ap214", () => { cr.ExportAs = StepCreator.ExportAsOption.Ap214; return "ok"; });
                R("ExportFrom = DisplayPart", () => { cr.ExportFrom = StepCreator.ExportFromOption.DisplayPart; return "ok"; });
                R("ObjectTypes.Solids", () => { cr.ObjectTypes.Solids = true; return "ok"; });
                R("SelectionScope = EntirePart", () =>
                { cr.ExportSelectionBlock.SelectionScope = NXOpen.ObjectSelector.Scope.EntirePart; return "ok"; });
                R("FileSaveFlag = false", () => { cr.FileSaveFlag = false; return "ok"; });
                R("ExportDestination = NativeFileSystem", () =>
                { cr.ExportDestination = NXOpen.BaseCreator.ExportDestinationOption.NativeFileSystem; return "ok"; });
                if (string.IsNullOrEmpty(ext))
                    R("OutputFile = " + outPath, () => { cr.OutputFile = outPath; return "ok"; });
                else
                {
                    R("OutputFile(无扩展) = " + outPath, () => { cr.OutputFile = outPath; return "ok"; });
                    R("OutputFileExtension = " + ext, () => { cr.OutputFileExtension = ext; return "ok"; });
                }
                string def = Path.Combine(DefDir, "ugstep214.def");
                R("SettingsFile = " + def, () =>
                { cr.SettingsFile = def; return File.Exists(def) ? "ok" : "def 缺失(仅提示)"; });
                R("Commit", () => { cr.Commit(); return "ok"; });
            }
            finally { cr.Destroy(); }
            string probe = string.IsNullOrEmpty(ext) ? outPath : outPath + "." + ext;
            string probe2 = string.IsNullOrEmpty(ext) ? outPath : outPath;
            foreach (string cand in new[] { outPath, probe, probe2, outPath + ".step", outPath + ".stp" })
            {
                if (File.Exists(cand) && new FileInfo(cand).Length > 200) return cand;
            }
            Log("  无产物文件（试过: " + outPath + " / 变体扩展名）→ null");
            return null;
        }
        catch (Exception e)
        {
            Log("  TryExport 异常: " + e.GetType().Name + " " + e.Message);
            return null;
        }
    }

    private static int CountSolids(Part p, out int faces)
    {
        faces = 0;
        int bodies = 0;
        foreach (Body b in p.Bodies.ToArray())
            if (!b.IsBlanked && b.IsSolidBody)
            {
                bodies++;
                faces += b.GetFaces().Length;
            }
        return bodies;
    }

    private static void Step(string label, Action act)
    {
        Log("");
        Log("== 阶段: " + label + " ==");
        try { act(); }
        catch (Exception e)
        {
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
