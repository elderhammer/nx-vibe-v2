// CamProbeStepRebuild.cs — STEP 导入重建链探针（2026-09-05 重开，run_journal 批处理驱动）
//
// 背景：早前"STEP 无公开导入 API"定案系检索缺陷假负结案（类名含数字 203/214/242 被字母型
// pattern 漏检 + head 截断吃掉 T: 区段）。证伪三关后确认公开通道：
//   .NET: Session.DexManager.CreateStep203/214/242Importer（NXOpen.dll 反射 + XML T: 实证，
//         CreateStep214Importer remarks = Created NX6.0.0 / License None）
//   C++ 头: Step203/214/242Importer.hxx（: BaseImporter；注释 "used when importing the STEP214 Data"）
//   样例: UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport\GeometryImporter.cs（完整范式）
//   基类: BaseImporter.InputFile/OutputFile（string）；Commit/Destroy 样例调用实证
//
// 收口（2026-09-05，12 轮批处理观察）：
//   ① API 存在性 = 三路铁证（§索引 §2.1 增补）；导入链在 APP_NONE 全部正常执行（Commit ok、
//      坏路径抛 "Part Import Error"）。
//   ② translator 真实执行（samples/camprobe-steprebuild-*_1.log）：手写 AP214 方块被全量解析
//      （115 实体含 8 vertex），失败点 = "Processing of step_manifold_solid_brep failed in new
//      workflow" + "No parts in current input file" → 资产级文件结构问题（product/brep 链细节），
//      非环境 stub（坏资产定案——第二波以官方有效资产同环境对照证实，见下）。
//   ③ 导出侧（StepCreator）：FileSaveFlag=false + OutputFile(BaseCreator) 使 translator 进程
//      可启（ST-DEVELOPER banner），但 ExportFrom{ExistingPart,DisplayPart}/ExportAs(Ap214)/
//      ObjectTypes.Solids=true/SelectionScope{EntirePart}/SettingsFile 多组合 solids input=0
//      → 首因 = SettingsFile 方向错配（step214ug.def 为导入向；导出须 ugstep214.def），
//      第二波 CamProbeStepExport 修正后闭环（见下）。
//   ④ StepCreator 基类 BaseCreator 提供 OutputFile/FileSaveFlag 语义（remarks: FileSaveFlag=
//      false 为文件导出模式）；env STEP214UG_DIR 自带尾斜杠（拼接防双斜杠）。
//
// 第二波（2026-09-05，资产缺口收口）：官方有效资产就地命中 = CAMSetupImport 样例库
//   sample/library/parts/sim_final2.stp（NX 12.0 ST-DEVELOPER 导出的真 AP214：1 MSB / 31
//   advanced_face / 平面+圆柱+圆锥 21+9+2，892 实体 33KB）。导入链改以官方资产为输入复验
//   （手写 probe-box-214.step 保留为坏资产对照，OfficialAsset 缺路径时回退）。
//
// 判死（两段）：
//   P1 导入（核心）：NewDisplay 空件 → Step214Importer 照样例 → Commit → 验 Body/Faces > 0
//   P2 CAM 共存：目标件上 CreateCamSession + CreateCamSetup + 组 + CAVITY op（不依赖导入几何）
//   结论（第二波终跑 012104）：P1 = 随资产而定——官方有效资产 α（1 body/31 面 = 实体计数）、
//   手写 probe-box γ（brep 结构缺陷，见 ②）；P2 = α（CAM 全套可建，CAM 侧不受导入影响）
//
// 输出：samples\camprobe-steprebuild-<ts>.txt（args[0] 可覆盖）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeStepRebuild
{
    private const string ProbeBox = @"C:\Users\21505\Code\nx-vibe-v2\samples\probe-box-214.step";
    private const string OfficialAsset = @"C:\Program Files\Siemens\NX2406\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport\sample\library\parts\sim_final2.stp";
    private static string _out;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-steprebuild-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeStepRebuild（STEP 导入重建链，官方资产路线）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        Part work = null;
        try
        {
            s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            string asset = File.Exists(OfficialAsset) ? OfficialAsset : ProbeBox;
            Log("资产: " + asset + " (" + new FileInfo(asset).Length + " B)"
                + (asset == OfficialAsset ? " [官方样例库 sim_final2.stp，手写 probe-box 转坏资产对照]"
                                           : " [官方资产缺失 → 回退手写 probe-box]"));
            if (asset == OfficialAsset) Log("坏资产对照: " + ProbeBox + " (" + new FileInfo(ProbeBox).Length + " B)");

            Step("P1 导入判死（Step214Importer → 空件）", () =>
            {
                work = s.Parts.NewDisplay("CamProbeStepRebuild", Part.Units.Millimeters);
                s.Parts.SetWork(work);
                // NewDisplay 空件未落盘时 FullPath 为空 → OutputFile 目标无效（样例 workPart 均落盘）；
                // 先 SaveAs 保证导入目标部件路径有效
                string prtPath = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
                    "camprobe-steprebuild-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".prt");
                R("SaveAs 目标件", () => { work.SaveAs(prtPath); return prtPath + "  FullPath=" + work.FullPath; });
                Log("  目标: " + work.Name + " 初始 Bodies=" + work.Bodies.ToArray().Length);

                // 变体 A：FileOpenFlag=false + SettingsFile（样例全同款；ORIENTED_EDGE 起终点已修正）
                Step214Importer imp = s.DexManager.CreateStep214Importer();
                try
                {
                    R("设置 ObjectTypes.Solids", () => { imp.ObjectTypes.Solids = true; return "ok"; });
                    R("设置 InputFile", () => { imp.InputFile = asset; return "ok"; });
                    R("设置 OutputFile", () => { imp.OutputFile = work.FullPath; return "ok"; });
                    string stepDir = s.GetEnvironmentVariableValue("STEP214UG_DIR");
                    if (!string.IsNullOrEmpty(stepDir))
                    {
                        string def = stepDir.TrimEnd('\\') + "\\step214ug.def";
                        R("设置 SettingsFile", () =>
                        {
                            imp.SettingsFile = def;
                            return File.Exists(def) ? "ok: " + def : "def 不存在(仅提示): " + def;
                        });
                    }
                    else Log("  STEP214UG_DIR 未设 → SettingsFile 保持默认");
                    R("设置 FileOpenFlag=false", () => { imp.FileOpenFlag = false; return "ok"; });
                    R("Commit(FileOpenFlag=false, 有 def)", () => { imp.Commit(); return "ok"; });
                }
                finally { imp.Destroy(); }
                Body[] bodies = work.Bodies.ToArray();
                int faces = 0;
                foreach (Body b in bodies)
                    if (!b.IsBlanked && b.IsSolidBody) faces += b.GetFaces().Length;
                Log("  变体A 导入后(目标件): Bodies=" + bodies.Length + " solidFaces=" + faces);

                // 变体 B：无 def（隔离 SettingsFile 变量）；坏路径判别保留为变体 C
                if (faces == 0)
                {
                    Log("  变体B: 无 def 隔离试跑");
                    Step214Importer impB = s.DexManager.CreateStep214Importer();
                    try
                    {
                        impB.ObjectTypes.Solids = true;
                        impB.InputFile = asset;
                        impB.OutputFile = work.FullPath;
                        impB.FileOpenFlag = false;
                        R("Commit(FileOpenFlag=false, 无 def)", () => { impB.Commit(); return "ok"; });
                    }
                    finally { impB.Destroy(); }
                    Body[] bodies2 = work.Bodies.ToArray();
                    int faces2 = 0;
                    foreach (Body b in bodies2)
                        if (!b.IsBlanked && b.IsSolidBody) faces2 += b.GetFaces().Length;
                    Log("  变体B 导入后: Bodies=" + bodies2.Length + " solidFaces=" + faces2);
                    faces = faces2;
                }

                // 变体 C：坏路径判别——Commit 是否真正处理输入（stub 判别）
                R("变体C 坏路径判别", () =>
                {
                    Step214Importer imp2 = s.DexManager.CreateStep214Importer();
                    try
                    {
                        imp2.ObjectTypes.Solids = true;
                        imp2.InputFile = @"C:\Users\21505\Code\nx-vibe-v2\samples\__no_such_file__.step";
                        imp2.OutputFile = work.FullPath;
                        imp2.Commit();
                        return "坏路径 Commit 无异常 → 疑似 stub/未触发翻译";
                    }
                    catch (Exception e) { return "坏路径 Commit 抛异常(" + e.GetType().Name + "): " + e.Message + " → Commit 处理输入"; }
                    finally { imp2.Destroy(); }
                });
                int totalFaces = faces;
                foreach (Part p in s.Parts.ToArray())
                {
                    if (p == work) continue;
                    try
                    {
                        foreach (Body b in p.Bodies.ToArray())
                            if (!b.IsBlanked && b.IsSolidBody) totalFaces += b.GetFaces().Length;
                    }
                    catch { }
                }
                Log("  全会话 solidFaces=" + totalFaces);
                if (totalFaces == 0)
                    throw new Exception("导入无几何 → P1 未过（见变体C stub 判别 + translator *_1.log 资产级错误）");
                Log("  P1 判定: 含体导入成功 → α");
            });

            if (work != null)
            {
                Step("P2 CAM 共存（目标件上建 CAMSetup + 组 + op；官方资产件已含导入几何）", () =>
                {
                    if (!s.IsCamSessionInitialized()) s.CreateCamSession();
                    CAMSetup cam = work.CreateCamSetup("mill_contour");
                    Log("  CreateCamSetup OK");
                    NCGroup pr = TryGroup(cam, CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "GR_PR");
                    NCGroup mt = TryGroup(cam, CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "GR_MT");
                    NCGroup tl = TryGroup(cam, CAMSetup.View.MachineTool, "mill_planar", "MILL", "GR_TL");
                    NCGroup ge = TryGroup(cam, CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "GR_GE");
                    if (pr == null || mt == null || tl == null || ge == null) throw new Exception("组创建不全");
                    Operation op = cam.CAMOperationCollection.Create(pr, mt, tl, ge,
                        "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.False, "OP_CAV1");
                    Log("  op=" + op.Name + "; faces 保持 = " + CountSolidFaces(work));
                    Log("  P2 判定: 目标件上 CAM 全套可建 → α（与导入几何解耦）");
                });
            }
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static int CountSolidFaces(Part part)
    {
        int faces = 0;
        foreach (Body b in part.Bodies.ToArray())
            if (!b.IsBlanked && b.IsSolidBody) faces += b.GetFaces().Length;
        return faces;
    }

    private static NCGroup TryGroup(CAMSetup cam, CAMSetup.View view, string tn, string st, string name)
    {
        try
        {
            NCGroup root = cam.GetRoot(view);
            NCGroupCollection g = cam.CAMGroupCollection;
            switch (view)
            {
                case CAMSetup.View.ProgramOrder:
                    return g.CreateProgram(root, tn, st, NCGroupCollection.UseDefaultName.False, name);
                case CAMSetup.View.MachineMethod:
                    return g.CreateMethod(root, tn, st, NCGroupCollection.UseDefaultName.False, name);
                case CAMSetup.View.MachineTool:
                    return g.CreateTool(root, tn, st, NCGroupCollection.UseDefaultName.False, name);
                default:
                    return g.CreateGeometry(root, tn, st, NCGroupCollection.UseDefaultName.False, name);
            }
        }
        catch (Exception e) { Log("  组创建 " + name + " 失败: " + e.Message); return null; }
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
