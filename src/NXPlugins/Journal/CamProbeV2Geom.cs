// CamProbeV2Geom.cs — v2 几何重建预检探针（2026-09-05 第二版，run_journal 批处理驱动）
//
// 目的：为 v2 spec 钉死"带几何重建"链的运行时实态（G1-G3）：
//   G1 几何指派机制：几何组/op 的 CAM.Geometry 如何挂上 body/faces（路线 A/B 判别）
//   G2 带几何刀路：几何生效后 CAVITY_MILL 生成刀路 + GetToolpathTime/Length 回读
//   G3 区域级读回：op.CutRegionsData（NumberRegions/GetAreas/GetCentroidPoints）通道活性
//
// 首跑（031421）结论与第二版假设：
//   - 组级 PartGeometry.CreateGeometrySet + gs.Selection.SetArray(body) + Commit → 回读
//     GeometryList 仍 1、items=0 → Selection.SetArray 不落库（selection intent 非 items 源）
//   - 本版判别：A2 = 对模板默认 set[0] 直接 SetArray(body)；B = op 级 CutAreaGeometry
//     （CavityMillingBuilder）新 set + SetArray(26 faces)；均 Commit 后新 builder 回读 items
//   - P5 原 SaveAs 同路径抛 "File already exists"（P0 已落盘）→ 本版 P5 只复核文件存在
//
// 静态依据：同首版（MillGeomBuilder.PartGeometry : CAM.Geometry；GeometrySet.Selection :
// SelectTaggedObjectList；Operation.CutRegionsData；官方 VB 样例同款取法）。
//
// 资产：samples/test.step（自产，1 body/26 面）；产物 samples/v2geom-rebuild-<ts>.prt。
// 输出：samples\camprobe-v2geom-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Geom
{
    private const string StepAsset = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.step";
    private const string SamplesDir = @"C:\Users\21505\Code\nx-vibe-v2\samples";
    private const string DefDir = @"C:\Program Files\Siemens\NX2406\step214ug";
    private static string _out;

    public static void Main(string[] args)
    {
        _out = Path.Combine(SamplesDir, "camprobe-v2geom-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeV2Geom v2（几何指派路线判别 A2/B + 带几何刀路 + 区域回读）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        Part work = null;
        NCGroup pr = null, mt = null, tl = null, wp = null;
        Operation opA = null, opB = null;
        Body body = null;
        try
        {
            s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            if (!File.Exists(StepAsset)) throw new Exception("资产缺失: " + StepAsset);

            Step("P0 导入 test.step", () =>
            {
                work = s.Parts.NewDisplay("CamProbeV2Geom", Part.Units.Millimeters);
                s.Parts.SetWork(work);
                string prtPath = Path.Combine(SamplesDir,
                    "v2geom-rebuild-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".prt");
                R("SaveAs 目标件", () => { work.SaveAs(prtPath); return work.FullPath; });
                Step214Importer imp = s.DexManager.CreateStep214Importer();
                try
                {
                    imp.ObjectTypes.Solids = true;
                    imp.InputFile = StepAsset;
                    imp.OutputFile = work.FullPath;
                    string def = Path.Combine(DefDir, "step214ug.def");
                    if (File.Exists(def)) imp.SettingsFile = def;
                    imp.FileOpenFlag = false;
                    R("Commit", () => { imp.Commit(); return "ok"; });
                }
                finally { imp.Destroy(); }
                int faces = CountSolidFaces(work);
                Log("  导入后: Bodies=" + work.Bodies.ToArray().Length + " solidFaces=" + faces + "（期望 1/26）");
                if (faces == 0) throw new Exception("导入无几何");
                foreach (Body b in work.Bodies.ToArray())
                    if (!b.IsBlanked && b.IsSolidBody) { body = b; break; }
                Log("  P0 判定: 导入 α → 前提成立（body 就位）");
            });

            if (work != null)
            {
                Step("P1 CAM 骨架 + 两 op（A=组级指派对象 / B=op 级指派对象）", () =>
                {
                    if (!s.IsCamSessionInitialized()) s.CreateCamSession();
                    CAMSetup cam = work.CreateCamSetup("mill_contour");
                    NCGroupCollection g = cam.CAMGroupCollection;
                    pr = g.CreateProgram(cam.GetRoot(CAMSetup.View.ProgramOrder),
                        "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "GR_PG");
                    mt = g.CreateMethod(cam.GetRoot(CAMSetup.View.MachineMethod),
                        "mill_contour", "MILL_METHOD", NCGroupCollection.UseDefaultName.False, "GR_MT");
                    tl = g.CreateTool(cam.GetRoot(CAMSetup.View.MachineTool),
                        "mill_planar", "MILL", NCGroupCollection.UseDefaultName.False, "GR_TL");
                    NCGroup mcs = g.CreateGeometry(cam.GetRoot(CAMSetup.View.Geometry),
                        "mill_contour", "MCS", NCGroupCollection.UseDefaultName.False, "GR_MCS");
                    wp = g.CreateGeometry(mcs, "mill_contour", "WORKPIECE",
                        NCGroupCollection.UseDefaultName.False, "GR_WP");
                    if (pr == null || mt == null || tl == null || mcs == null || wp == null)
                        throw new Exception("组创建不全");
                    opA = cam.CAMOperationCollection.Create(pr, mt, tl, wp,
                        "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.False, "OP_V2_A");
                    opB = cam.CAMOperationCollection.Create(pr, mt, tl, wp,
                        "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.False, "OP_V2_B");
                    Log("  四父组 + opA/opB 创建 OK（wp 父 = mcs）");
                    MillingToolBuilder mtb = g.CreateMillToolBuilder(tl) as MillingToolBuilder;
                    mtb.TlDiameterBuilder.Value = 12.0;
                    R("刀具直径=12 写入", () => { mtb.Commit(); return "ok"; });
                    mtb.Destroy();
                    Log("  P1 判定: CAM 骨架 α");
                });

                Step("P2-A2 组级默认集 SetArray(body)", () =>
                {
                    if (work == null || wp == null || body == null) throw new Exception("前置缺失");
                    CAMSetup cam = work.CAMSetup;
                    MillGeomBuilder mgb = cam.CAMGroupCollection.CreateMillGeomBuilder(wp);
                    try
                    {
                        NXOpen.CAM.Geometry pg = mgb.PartGeometry;
                        int before = pg.GeometryList.Length;
                        Log("  指派前 GeometryList.Length=" + before);
                        if (before > 0)
                        {
                            NXOpen.CAM.GeometrySet gs0 = pg.GeometryList.FindItem(0);
                            Log("    set0 items(前)=" + gs0.GetItems().Length);
                            R("set0.Selection.SetArray(body)", () =>
                            { gs0.Selection.SetArray(new TaggedObject[] { body }); return "ok"; });
                            R("Commit(mgb)", () => { mgb.Commit(); return "ok"; });
                        }
                    }
                    catch (Exception e) { Log("  !! P2-A2 异常: " + e.Message); }
                    finally { mgb.Destroy(); }
                    // 回读
                    try
                    {
                        MillGeomBuilder mgb2 = cam.CAMGroupCollection.CreateMillGeomBuilder(wp);
                        NXOpen.CAM.Geometry pg2 = mgb2.PartGeometry;
                        int n = (pg2 == null) ? -1 : pg2.GeometryList.Length;
                        Log("  回读 GeometryList.Length=" + n);
                        if (pg2 != null && n > 0)
                        {
                            NXOpen.CAM.GeometrySet gs = pg2.GeometryList.FindItem(0);
                            int cnt = gs.GetItems().Length;
                            Log("    set0 items(回读)=" + cnt);
                            Log(cnt > 0 ? "  P2-A2 判定: 默认集 SetArray 落库 → α" : "  P2-A2 判定: items 仍 0 → 组级路线死（转 op 级 B）");
                        }
                        mgb2.Destroy();
                    }
                    catch (Exception e) { Log("  !! P2-A2 回读异常: " + e.Message); }
                });

                Step("P2-B op 级 CutAreaGeometry 默认集 SetArray(面)（gt 对照：gt=13 面选区，033332）", () =>
                {
                    if (work == null || opB == null || body == null) throw new Exception("前置缺失");
                    CAMSetup cam = work.CAMSetup;
                    CavityMillingBuilder cb = cam.CAMOperationCollection.CreateCavityMillingBuilder(opB);
                    try
                    {
                        NXOpen.CAM.Geometry cag = cb.CutAreaGeometry;
                        Log("  CutAreaGeometry=" + (cag == null ? "null" : cag.ToString()));
                        if (cag != null)
                        {
                            Log("  指派前 GeometryList.Length=" + cag.GeometryList.Length);
                            Face[] faces = body.GetFaces();
                            TaggedObject[] faceTags = new TaggedObject[faces.Length];
                            for (int i = 0; i < faces.Length; i++) faceTags[i] = faces[i];
                            if (cag.GeometryList.Length > 0)
                            {
                                NXOpen.CAM.GeometrySet gs0 = cag.GeometryList.FindItem(0);
                                Log("    set0 items(前)=" + gs0.GetItems().Length);
                                R("set0.Selection.SetArray(faces×" + faces.Length + ")", () =>
                                { gs0.Selection.SetArray(faceTags); return "ok"; });
                            }
                            else
                            {
                                NXOpen.CAM.GeometrySet gs = cag.CreateGeometrySet();
                                R("新建集 SetArray(faces×" + faces.Length + ")", () =>
                                { gs.Selection.SetArray(faceTags); return "ok"; });
                            }
                            R("Commit(cb)", () => { cb.Commit(); return "ok"; });
                        }
                    }
                    catch (Exception e) { Log("  !! P2-B 异常: " + e.Message); }
                    finally { cb.Destroy(); }
                    // 回读（新 builder）
                    try
                    {
                        CavityMillingBuilder cb2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(opB);
                        NXOpen.CAM.Geometry cag2 = cb2.CutAreaGeometry;
                        int n = (cag2 == null) ? -1 : cag2.GeometryList.Length;
                        Log("  回读 CutAreaGeometry.GeometryList.Length=" + n);
                        if (cag2 != null && n > 0)
                        {
                            NXOpen.CAM.GeometrySet gs2 = cag2.GeometryList.FindItem(0);
                            int cnt = gs2.GetItems().Length;
                            Log("    set0 items(回读)=" + cnt);
                            Log(cnt > 0 ? "  P2-B 判定: op 级默认集落库 → α（与组级 A2 同机制）"
                                       : "  P2-B 判定: items 仍 0 → op 级默认集亦不落库");
                        }
                        cb2.Destroy();
                    }
                    catch (Exception e) { Log("  !! P2-B 回读异常: " + e.Message); }
                });

                Step("P3 带几何刀路（两 op 各生成 + 时间/长度）", () =>
                {
                    if (work == null) throw new Exception("前置缺失");
                    CAMSetup cam = work.CAMSetup;
                    Operation[] ops = { opA, opB };
                    foreach (Operation o in ops)
                    {
                        if (o == null) continue;
                        R("GenerateToolPath(" + o.Name + ")", () => { cam.GenerateToolPath(new CAMObject[] { o }); return "ok"; });
                        double t = o.GetToolpathTime();
                        double len = o.GetToolpathLength();
                        Log("  " + o.Name + ": time=" + t + " length=" + len);
                    }
                });

                Step("P4 区域级读回（op.CutRegionsData，B 路优先）", () =>
                {
                    Operation target = opB != null ? opB : opA;
                    if (target == null) throw new Exception("无 op");
                    try
                    {
                        CutRegionsData crd = target.CutRegionsData;
                        if (crd == null) throw new Exception("CutRegionsData null");
                        Log("  " + target.Name + ": NumberRegions=" + crd.NumberRegions);
                        double[] areas = crd.GetAreas();
                        Point3d[] cps = crd.GetCentroidPoints();
                        double sum = 0;
                        if (areas != null) foreach (double a in areas) sum += a;
                        Log("  areas=" + (areas == null ? 0 : areas.Length) + " 合计=" + sum
                            + " centroids=" + (cps == null ? 0 : cps.Length));
                    }
                    catch (Exception e) { Log("  !! P4 异常: " + e.Message); }
                });

                Step("P5 落盘（原地 Save——P0 SaveAs 在导入前，含几何必须本步持久）", () =>
                {
                    if (work == null) throw new Exception("前置缺失");
                    // 教训（033422/033828 诊断）：SaveAs 须在导入后；原地保存用
                    // BasePart.Save(SaveComponents.False, CloseAfterSave.False)
                    R("Save", () => { work.Save(NXOpen.BasePart.SaveComponents.False, NXOpen.BasePart.CloseAfterSave.False); return "ok"; });
                    Log("  文件: " + work.FullPath + " 大小=" + (File.Exists(work.FullPath) ? new FileInfo(work.FullPath).Length : -1));
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
