// CamProbeV2Gt.cs — gt 腔铣 op 几何配置只读对照（2026-09-05，run_journal 批处理驱动）
//
// 目的：为"带几何刀路空刀路（time=0）"判因——读 test.prt（gt）腔铣 op 的真实几何配置：
//   G-1 几何组树形 dump（WORKPIECE 下 PART/BLANK 节点实态）
//   G-2 组级 part/blank 几何集 items（body？面？空？）
//   G-3 op 级 CutAreaGeometry 集 items（gt=13 面?）+ op 参数（feeds/depth/stock）
//   G-4 gt op 是否已生成刀路（GetToolpathTime/Length > 0?）
// 对照用途：v2 重建侧复刻 gt 配置后刀路应非空（P3 空刀路判因：缺 blank 或 cut area）。
//
// 纪律：只读不保存；builder 用毕 Destroy（MONO-1 同款）。
// 输出：samples\camprobe-v2gt-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Gt
{
    private const string GtPrt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static string _out;
    private static readonly List<Operation> _ops = new List<Operation>();

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2gt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeV2Gt（gt 腔铣 op 几何配置只读对照）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(GtPrt, out st);
            s.Parts.SetWork(p);
            UFSession uf = UFSession.GetUFSession();
            R("uf.SetDisplayPart", () => { uf.Part.SetDisplayPart(p.Tag); return "ok"; });
            Log("件: " + p.Name);
            // APP_NONE 打开带 CAM 件需显式建 CAM 会话（索引 §2.1 纪律；IsCamSessionInitialized 判据）
            R("CreateCamSession", () =>
            {
                if (!s.IsCamSessionInitialized()) s.CreateCamSession();
                return "initialized=" + s.IsCamSessionInitialized();
            });

            CAMSetup cam = p.CAMSetup;
            if (cam == null) throw new Exception("无 CAMSetup");
            int nSolid = 0, nFaces = 0;
            foreach (Body b in p.Bodies.ToArray())
                if (!b.IsBlanked && b.IsSolidBody) { nSolid++; nFaces += b.GetFaces().Length; }
            Log("solidBodies=" + nSolid + " 总面=" + nFaces);

            Log("");
            Log("== 程序树 op 清单（NxCollect 同口径递归）==");
            WalkProgramTree(cam, cam.GetRoot(CAMSetup.View.ProgramOrder), 0);
            Log("op 总数=" + _ops.Count);
            foreach (Operation o in _ops)
                Log("  " + o.Name + "  type='" + o.GetNameOfType() + "'  pathT=" + o.GetToolpathTime()
                    + " pathL=" + o.GetToolpathLength());

            Log("");
            Log("== 几何组树 dump（几何根下递归）==");
            DumpGroupTree(cam.GetRoot(CAMSetup.View.Geometry), 0);

            Operation cav = null;
            foreach (Operation o in _ops)
                if (o.GetNameOfType().ToLower().Contains("cavity")) { cav = o; break; }
            if (cav == null) throw new Exception("未找到腔铣 op");
            Log("");
            Log("== 腔铣 op: " + cav.Name + " ==");

            NCGroup geomParent = cav.ParentGeometry;
            Log("几何父组: " + geomParent.Name);

            R("组级 part 几何", () =>
            {
                MillGeomBuilder mgb = cam.CAMGroupCollection.CreateMillGeomBuilder(geomParent);
                try
                {
                    NXOpen.CAM.Geometry pg = mgb.PartGeometry;
                    if (pg == null) return "null";
                    string desc = "sets=" + pg.GeometryList.Length;
                    for (int i = 0; i < pg.GeometryList.Length; i++)
                    {
                        NXOpen.CAM.GeometrySet gs = pg.GeometryList.FindItem(i);
                        TaggedObject[] items = gs.GetItems();
                        desc += " | set" + i + " items=" + items.Length;
                        foreach (TaggedObject it in items)
                            desc += (it is Body) ? "(Body)" : (it is Face) ? "(Face)" : "(" + it.GetType().Name + ")";
                    }
                    return desc;
                }
                finally { mgb.Destroy(); }
            });

            R("op 级 CutAreaGeometry", () =>
            {
                CavityMillingBuilder cb = cam.CAMOperationCollection.CreateCavityMillingBuilder(cav);
                try
                {
                    NXOpen.CAM.Geometry cag = cb.CutAreaGeometry;
                    if (cag == null) return "null";
                    string desc = "sets=" + cag.GeometryList.Length;
                    for (int i = 0; i < cag.GeometryList.Length; i++)
                    {
                        NXOpen.CAM.GeometrySet gs = cag.GeometryList.FindItem(i);
                        TaggedObject[] items = gs.GetItems();
                        desc += " | set" + i + " items=" + items.Length;
                        foreach (TaggedObject it in items)
                            desc += (it is Face) ? "(Face)" : (it is Body) ? "(Body)" : "(" + it.GetType().Name + ")";
                    }
                    return desc;
                }
                finally { cb.Destroy(); }
            });

            R("op 参数（feeds/depth/stock）", () =>
            {
                CavityMillingBuilder cb = cam.CAMOperationCollection.CreateCavityMillingBuilder(cav);
                try
                {
                    return "rpm=" + cb.FeedsBuilder.SpindleRpmBuilder.Value
                        + " cut=" + cb.FeedsBuilder.FeedCutBuilder.Value
                        + " depthPerCut=" + cb.DepthPerCut.Value
                        + " floorStock=" + cb.CutParameters.FloorStock.Value
                        + " partStock=" + cb.CutParameters.PartStock.Value;
                }
                finally { cb.Destroy(); }
            });

            R("op CutRegionsData", () =>
            {
                CutRegionsData crd = cav.CutRegionsData;
                if (crd == null) return "null";
                double[] areas = crd.GetAreas();
                return "NumberRegions=" + crd.NumberRegions + " areas=" + (areas == null ? 0 : areas.Length);
            });
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void WalkProgramTree(CAMSetup cam, NCGroup node, int depth)
    {
        if (depth > 8) return;
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null) { _ops.Add(op); continue; }
            NCGroup sub = m as NCGroup;
            if (sub != null) WalkProgramTree(cam, sub, depth + 1);
        }
    }

    private static void DumpGroupTree(NCGroup node, int depth)
    {
        if (node == null || depth > 8) return;
        Log(new string(' ', depth * 2) + "+ " + node.Name + "  <" + node.GetType().Name + ">");
        foreach (CAMObject m in node.GetMembers())
        {
            NCGroup sub = m as NCGroup;
            if (sub != null) DumpGroupTree(sub, depth + 1);
            else Log(new string(' ', (depth + 1) * 2) + "- " + m.GetType().Name + ":" + m.Name);
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
