// CamProbeV2FaceSet.cs — OP-002 面集写通道判别探针（2026-09-06，run_journal 批处理，单件内存态不 Save）
//
// 背景：regionfull 定案 OP-002 = gt 本体只加工两孔底段 18 层×2 区窄带 vs 同参克隆/rebuilt 全程
// 118 层×1 区 → 差异在面集级几何属性（surfdiff 排除了白名单参数，Recurse 刻意跳过 Geometry 族）。
// 头文件审查（CAM_GeometrySet.hxx）：集属性 = 普通 setter（NX8.0.0/cam_base，Reversed NX2007，
// 非 NX_NO_DOC）；MaterialSideTypes = None/Same/Opposite；样例库零写面范式。
// 本探针 = gt 件（test.prt）单会话因果判别（本体属性源 + 克隆目标同会话，免跨件传值）：
//   ① 读本体 OP-002 各集属性（MaterialSide/Intol/Outtol/PartOffset/CustomStock/Initial/Final/
//      CheckStock/Reversed）② 建克隆（regionclone 同款：6 面 + Profile/DPC 0.2/3000/1200 + 父链）
//   ③ 读克隆默认集属性 → 本体 vs 克隆 diff（候选因果 = 工程师设置 ≠ 模板默认）
//   ④ 把本体属性复制到克隆（写面，Set* setter）→ Commit → 新 builder 读回（写面持久 in-session）
//   ⑤ regen 克隆 → 区数判因果：收窄向 36 → 集属性写面有效 + 因果 ✓（OP-002 修复路径开建）；
//      仍 119 → 集属性非主因（转查层范围/其他 UI 面）。
// 只读纪律：gt 件不 Save（克隆退出即弃）。输出：samples\camprobe-v2faceset-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2FaceSet
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2faceset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2FaceSet（OP-002 面集属性写面/因果判别）==");
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
            _cam = p.CAMSetup;
            if (_cam == null) throw new Exception("无 CAMSetup");

            Operation op2 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
            if (op2 == null) throw new Exception("找不到 OP-002");

            // ① 本体集属性读取（值类型捕获——GeometrySet 对象随 builder 生命周期，Destroy 后 not alive）
            TaggedObject[] f6 = null;
            var attr = new double[8];          // 0..7: PartOffset/Intol/Outtol/Initial/Final/Check/（Custom/Reversed bool 单列）
            bool customStock = false, reversed = false;
            GeometrySet.MaterialSideTypes materialSide = GeometrySet.MaterialSideTypes.None;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("本体无几何集");
                GeometrySet gs0 = cag.GeometryList.FindItem(0);
                Log("");
                Log("== 本体集[0] items=" + gs0.GetItems().Length + " ==");
                DumpSet(gs0, "本体");
                attr[0] = gs0.PartOffset; attr[1] = gs0.Intol; attr[2] = gs0.Outtol;
                attr[3] = gs0.InitialStock; attr[4] = gs0.FinalStock; attr[5] = gs0.CheckStock;
                customStock = gs0.CustomStock; reversed = gs0.Reversed; materialSide = gs0.MaterialSide;
                f6 = gs0.GetItems();
            }
            finally { bg.Destroy(); }
            Log("本体集数=1 集0 面数=" + (f6 == null ? "null" : f6.Length.ToString()));
            Log("捕获值: PartOffset=" + attr[0].ToString("0.####") + " Intol=" + attr[1].ToString("0.####")
                + " Outtol=" + attr[2].ToString("0.####") + " MaterialSide=" + materialSide);

            // ② 克隆（regionclone 同款）
            NCGroup prog = op2.ParentProgramOrder, method = op2.ParentMachineMethod;
            NCGroup tool = op2.ParentMachineTool, geom = op2.ParentGeometry;
            Operation neu = _cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, "FACESET_CLONE");
            Log("");
            Log("克隆创建 OK: " + neu.Name);
            GeometrySet[] cloneSets = null;
            CavityMillingBuilder bn = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
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
                bn.FeedsBuilder.SpindleRpmBuilder.Value = 3000;
                bn.FeedsBuilder.FeedCutBuilder.Value = 1200;
                NXOpen.CAM.Geometry cag = bn.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("克隆无默认几何集");
                GeometrySet g0 = cag.GeometryList.FindItem(0);
                g0.Selection.SetArray(f6);
                // ③ 克隆默认属性（复制前）
                Log("");
                Log("== 克隆集[0] 默认（复制前）==");
                DumpSet(g0, "克隆");
                // ④ 本体集 0 捕获值 → 克隆集 0（写面）
                CopySetValues(g0, attr, customStock, reversed, materialSide);
                Log("  本体集[0] 属性值 → 克隆集[0] 复制完成");
                bn.Commit();
            }
            finally { bn.Destroy(); }
            // 新 builder 读回（写面持久 in-session 判据）
            Log("");
            Log("== 克隆写回后读回（新 builder）==");
            CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                NXOpen.CAM.Geometry cag = b2.CutAreaGeometry;
                if (cag != null && cag.GeometryList.Length > 0)
                {
                    cloneSets = new GeometrySet[cag.GeometryList.Length];
                    for (int i = 0; i < cag.GeometryList.Length; i++)
                    {
                        cloneSets[i] = cag.GeometryList.FindItem(i);
                        DumpSet(cloneSets[i], "克隆读回");
                    }
                }
            }
            finally { b2.Destroy(); }

            // ⑤ regen 判因果
            Log("");
            Log("== regen 判别 ==");
            GenAndRead(neu, "克隆(属性复制后)");
            Log("对照: regionclone 无属性复制克隆 = 119 区/41434/27.4s；gt 本体 = 36 区/929/0.515s");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // 捕获值 → 目标集（.NET 包装 = 属性赋值——反射实证 CanWrite=True；
    // C++ 侧 Set* 方法名（NX8/cam_base）仅作声明参考；值类型捕获规避 GeometrySet 生命周期）
    private static void CopySetValues(GeometrySet dst, double[] attr, bool customStock, bool reversed,
        GeometrySet.MaterialSideTypes materialSide)
    {
        dst.PartOffset = attr[0];
        dst.Intol = attr[1];
        dst.Outtol = attr[2];
        dst.InitialStock = attr[3];
        dst.FinalStock = attr[4];
        dst.CheckStock = attr[5];
        dst.CustomStock = customStock;
        dst.Reversed = reversed;
        dst.MaterialSide = materialSide;
        Log("  属性复制完成: PartOffset=" + dst.PartOffset.ToString("0.####")
            + " Intol=" + dst.Intol.ToString("0.####") + " Outtol=" + dst.Outtol.ToString("0.####")
            + " CustomStock=" + dst.CustomStock + " InitialStock=" + dst.InitialStock.ToString("0.####")
            + " FinalStock=" + dst.FinalStock.ToString("0.####") + " CheckStock=" + dst.CheckStock.ToString("0.####")
            + " MaterialSide=" + dst.MaterialSide + " Reversed=" + dst.Reversed);
    }

    private static void DumpSet(GeometrySet gs, string label)
    {
        R(label + " MaterialSide", () => gs.MaterialSide.ToString());
        R(label + " Intol", () => gs.Intol.ToString("0.####"));
        R(label + " Outtol", () => gs.Outtol.ToString("0.####"));
        R(label + " PartOffset", () => gs.PartOffset.ToString("0.####"));
        R(label + " CustomStock", () => gs.CustomStock.ToString());
        R(label + " InitialStock", () => gs.InitialStock.ToString("0.####"));
        R(label + " FinalStock", () => gs.FinalStock.ToString("0.####"));
        R(label + " CheckStock", () => gs.CheckStock.ToString("0.####"));
        R(label + " Reversed", () => gs.Reversed.ToString());
    }

    private static void GenAndRead(Operation op, string label)
    {
        try { _cam.GenerateToolPath(new CAMObject[] { op }); }
        catch (Exception e) { Log("  [" + label + "] 生成异常: " + e.Message); return; }
        double tp = op.GetToolpathTime();
        double tl = op.GetToolpathLength();
        string reg = "-";
        try
        {
            CutRegionsData crd = op.CutRegionsData;
            if (crd != null) reg = crd.NumberRegions.ToString();
        }
        catch (Exception e) { reg = "ERR " + e.Message; }
        Log("  [" + label + "] time=" + tp.ToString("0.####") + " length=" + tl.ToString("0.####")
            + " regions=" + reg + (tp > 0 ? "  <<< 非零" : ""));
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
