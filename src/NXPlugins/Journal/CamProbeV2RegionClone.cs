// CamProbeV2RegionClone.cs — 区域配对研究批前置判别：OP-002 克隆实验（2026-09-06，run_journal
// 批处理，单件模式，内存态不 Save；判别⑦ apiclone 方法论应用到 OP-002 + 参数复刻）
//
// 背景：OP-002（CAVITY_MILL_COPY，6 面 Profile）gt 本体 regen = 0.515s/929.6/36 区（215614 已证
// 新鲜），rebuilt 本体（executor 同参复刻） = 28.37s/39850/118 区（43×）→ 已判 γ 类（体上下文）。
// 判别⑦（apiclone，OP-003 3 面默认参数）证明"仅件不同"；本探针在**双件各建**同款克隆（OP-002
// 同 6 面 + 参数复刻：Profile/CutLevel DPC=0.2/rpm 3000/feed 1200）→ 四值矩阵定案：
//   gt 克隆 ≈ 929/36 → 同参在 gt 件正常 → rebuilt 体上下文 γ 确证（配对算法须保真差 FAIL）；
//   gt 克隆 ≈ 39850/118 → 同参在 gt 件也超密 → rebuilt 无件差异，gt 本体 36 区另有隐藏面
//   （本体特有的集级/范围属性）→ 需另判别（非体上下文叙事）。
// 对照：各自本体 regen（同会话）+ 克隆。区域原料顺带 dump（质心/面积 vector 读取验证，
// CutRegionsData = NX_NO_DOC 内部 API，NX10.0.2/cam_base，头文件实证——规格须标不稳定边界）。
// 只读纪律：不 Save（内存态新增 op，退出即弃）。输出：samples\camprobe-v2regionclone-<ts>.txt。
// 件 = CAMSIG_CLONE_PRT 环境变量（默认 samples\test.prt；rebuilt 档跑 v2.rebuilt-20260906-000810.prt）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2RegionClone
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_CLONE_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2regionclone-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2RegionClone（OP-002 同 6 面 + 参数复刻克隆判别）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("件: " + prt);
        try
        {
            Session s = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(prt, out st);
            s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            Log("打开 OK: " + p.Name + "  camSession=" + s.IsCamSessionInitialized());
            CAMSetup cam = p.CAMSetup;
            if (cam == null) throw new Exception("无 CAMSetup");

            Operation op2 = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
            if (op2 == null) throw new Exception("找不到 OP-002");
            Log("== 本体对照（regen，215614 已证 gt 侧 = 存档）==");
            GenAndRead(cam, op2, "OP-002 本体");

            // 读本体面（同件 Tag 直接复用）+ 本体参数实况（值源对照，plan OP-002 同源）
            TaggedObject[] f6 = null;
            CavityMillingBuilder bg = cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("本体无几何集");
                f6 = cag.GeometryList.FindItem(0).GetItems();
                R("本体 CutPattern", () => bg.CutPattern.CutPattern.ToString());
                R("本体 CutLevel DPC", () => bg.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value.ToString("0.####"));
                R("本体 feedCut", () => bg.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
            }
            finally { bg.Destroy(); }
            Log("OP-002 面数=" + f6.Length);

            NCGroup prog = op2.ParentProgramOrder, method = op2.ParentMachineMethod;
            NCGroup tool = op2.ParentMachineTool, geom = op2.ParentGeometry;
            Log("锚点: prog=" + (prog != null ? prog.Name : "null") + " method="
                + (method != null ? method.Name : "null") + " tool="
                + (tool != null ? tool.Name : "null") + " geom=" + (geom != null ? geom.Name : "null"));
            if (prog == null || method == null || tool == null || geom == null)
                throw new Exception("锚点缺失，中止");

            Operation neu = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, "REGIONCLONE_OP002");
            Log("克隆创建 OK: " + neu.Name);

            // 参数复刻（executor 同款写链；值源 = plan OP-002：Profile/0.2/3000/1200，stocks 0）
            CavityMillingBuilder bn = cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                bn.CutPattern.CutPattern = CutPatternBuilder.Types.Profile;
                bn.CutParameters.CutOrder = CutParametersCutOrderTypes.DepthFirst;
                bn.CutParameters.CutDirection.Type = CutDirection.Types.Climb;
                bn.CutParameters.FinishPasses.NumberOfFinishPasses = 0;
                bn.CutParameters.PartStock.Value = 0;
                bn.CutParameters.FloorStock.Value = 0;
                NXOpen.CAM.CutLevel cl = bn.CutLevel;
                if (cl != null) cl.GlobalDepthPerCut.DistanceBuilder.Value = 0.2;
                else Log("  !! 克隆 CutLevel null → DPC 0.2 未写（参数不完整，结果存疑）");
                bn.FeedsBuilder.SpindleRpmBuilder.Value = 3000;
                bn.FeedsBuilder.FeedCutBuilder.Value = 1200;
                // 面指派（executor 同款 G1 通道：默认集 SetArray）
                NXOpen.CAM.Geometry cag = bn.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("克隆无默认几何集");
                cag.GeometryList.FindItem(0).Selection.SetArray(f6);
                bn.Commit();
            }
            finally { bn.Destroy(); }
            Log("参数复刻 + 面指派 6 → ok");

            GenAndRead(cam, neu, "克隆(同参同面)");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void GenAndRead(CAMSetup cam, Operation op, string label)
    {
        try { cam.GenerateToolPath(new CAMObject[] { op }); }
        catch (Exception e) { Log("  [" + label + "] 生成异常: " + e.Message); return; }
        double tp = op.GetToolpathTime();
        double tl = op.GetToolpathLength();
        Log("  [" + label + "] time=" + tp.ToString("0.####") + " length=" + tl.ToString("0.####")
            + (tp > 0 ? "  <<< 非零" : ""));
        // 区域原料 dump（内部 API 边界注记；质心/面积 vector 与 NumberRegions 同源）
        try
        {
            CutRegionsData crd = op.CutRegionsData;
            if (crd == null) { Log("  [" + label + "] CutRegionsData null"); return; }
            int n = crd.NumberRegions;
            Point3d[] cents = crd.GetCentroidPoints();
            double[] areas = crd.GetAreas();
            Log("  [" + label + "] regions=" + n + " 质心数=" + (cents == null ? "null" : cents.Length.ToString())
                + " 面积数=" + (areas == null ? "null" : areas.Length.ToString()));
            int show = Math.Min(n, 3);
            for (int i = 0; i < show; i++)
                Log("     区[" + i + "] 质心=(" + cents[i].X.ToString("0.###") + ","
                    + cents[i].Y.ToString("0.###") + "," + cents[i].Z.ToString("0.###")
                    + ") 面积=" + (areas != null && i < areas.Length ? areas[i].ToString("0.###") : "-"));
        }
        catch (Exception e) { Log("  [" + label + "] 区域读异常: " + e.GetType().Name + " " + e.Message); }
    }

    private static Operation FindOp(NCGroup node, string name)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.Name == name) return op;
            NCGroup sub = m as NCGroup;
            if (sub != null)
            {
                Operation hit = FindOp(sub, name);
                if (hit != null) return hit;
            }
        }
        return null;
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
