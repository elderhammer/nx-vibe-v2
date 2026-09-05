// CamProbeV2OpDiag.cs — OP-003 空刀路判别探针（2026-09-05，run_journal 批处理，单件模式）
//
// 背景：v2 重建（191434）OP-001/002/004 出刀路、OP-003（COPY_COPY 3 面）空（gt 8.03s/3 区）。
// 组级 body/op 级面/白名单参数与 gt 全同仍空 → 判别候选：几何集属性（GeometrySet.MaterialSide/
// Stock 族/Intol/OutTol/PartOffset 等——UI 选面时的集级设置）与 op 级 CutLevel/区域参数面。
//
// 用法：OS 环境变量 CAMSIG_PRT = prt 路径（run_journal 不收参数）；件内取前 4 个腔 op：
//   每 op 读 CutAreaGeometry 每集（GeometrySet 集属性）+ 刀路时间 + 区域数。
// 两跑（gt / v2.rebuilt）后离线 diff 判别。
// 输出：samples\camprobe-v2op-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2OpDiag
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2op-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2OpDiag（op 级几何集/参数判别读）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("件: " + prt);
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            UFSession uf = UFSession.GetUFSession();
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(prt, out st);
            s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            Log("打开 OK: " + p.Name + "  camSession=" + s.IsCamSessionInitialized());

            CAMSetup cam = p.CAMSetup;
            if (cam == null) throw new Exception("无 CAMSetup");
            // 收集腔 op（程序树递归，取前 4）
            var cavityOps = new List<Operation>();
            WalkOps(cam, cam.GetRoot(CAMSetup.View.ProgramOrder), cavityOps, 4);
            Log("腔 op 数=" + cavityOps.Count);
            foreach (Operation op in cavityOps)
                ReadOp(cam, op);
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void ReadOp(CAMSetup cam, Operation op)
    {
        Log("");
        Log("== op: " + op.Name + "  pathT=" + op.GetToolpathTime() + " pathL=" + op.GetToolpathLength());
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            Log("  --- op 级参数面 ---");
            R("CutPattern", () => b.CutPattern.CutPattern.ToString());
            R("CutOrder", () => b.CutParameters.CutOrder.ToString());
            R("CutDirection", () => b.CutParameters.CutDirection.Type.ToString());
            R("FinishPasses", () => "N=" + b.CutParameters.FinishPasses.NumberOfFinishPasses);
            R("PartStock", () => b.CutParameters.PartStock.Value.ToString("0.####"));
            R("FloorStock", () => b.CutParameters.FloorStock.Value.ToString("0.####"));
            R("DepthPerCut", () => b.DepthPerCut.Value.ToString("0.####"));
            R("DepthPerCutBuilder status", () => b.DepthPerCut.InheritanceStatus.ToString());
            R("CutLevel", () => b.CutLevel == null ? "null" : "CUTLEVEL@" + b.CutLevel.GetType().Name);
            R("rpm", () => b.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            R("feedCut", () => b.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
            R("CutRegions", () => { CutRegionsData crd = op.CutRegionsData; return crd == null ? "null" : "n=" + crd.NumberRegions; });

            Log("  --- CutAreaGeometry 几何集属性（集级判别候选） ---");
            NXOpen.CAM.Geometry cag = b.CutAreaGeometry;
            if (cag != null)
            {
                for (int i = 0; i < cag.GeometryList.Length; i++)
                {
                    NXOpen.CAM.GeometrySet gs = cag.GeometryList.FindItem(i);
                    Log("    set[" + i + "] items=" + gs.GetItems().Length);
                    Log("      MaterialSide=" + gs.MaterialSide.ToString());
                    Log("      Intol=" + gs.Intol.ToString("0.####") + " Outtol=" + gs.Outtol.ToString("0.####"));
                    Log("      PartOffset=" + gs.PartOffset.ToString("0.####"));
                    Log("      InitialStock=" + gs.InitialStock.ToString("0.####") + " FinalStock=" + gs.FinalStock.ToString("0.####"));
                    Log("      CheckStock=" + gs.CheckStock.ToString("0.####") + " CustomStock=" + (gs.CustomStock ? "T" : "F") + " Reversed=" + (gs.Reversed ? "T" : "F"));
                }
            }
            else Log("    CutAreaGeometry null");

            // 组级 part 几何集属性（判别组级差异）
            NCGroup geomParent = op.ParentGeometry;
            if (geomParent != null)
            {
                MillGeomBuilder mgb = cam.CAMGroupCollection.CreateMillGeomBuilder(geomParent);
                try
                {
                    NXOpen.CAM.Geometry pg = mgb.PartGeometry;
                    Log("  --- 组级 part 几何集 ---");
                    if (pg != null)
                        for (int i = 0; i < pg.GeometryList.Length; i++)
                        {
                            NXOpen.CAM.GeometrySet gs = pg.GeometryList.FindItem(i);
                            Log("    set[" + i + "] items=" + gs.GetItems().Length
                                + " MaterialSide=" + gs.MaterialSide
                                + " PartOffset=" + gs.PartOffset.ToString("0.####")
                                + " InitialStock=" + gs.InitialStock.ToString("0.####")
                                + " FinalStock=" + gs.FinalStock.ToString("0.####"));
                        }
                }
                finally { mgb.Destroy(); }
            }
        }
        finally { b.Destroy(); }
    }

    private static void WalkOps(CAMSetup cam, NCGroup node, List<Operation> acc, int max)
    {
        if (acc.Count >= max) return;
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.GetNameOfType().ToLower().Contains("cavity") && acc.Count < max)
            { acc.Add(op); continue; }
            NCGroup sub = m as NCGroup;
            if (sub != null) WalkOps(cam, sub, acc, max);
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
