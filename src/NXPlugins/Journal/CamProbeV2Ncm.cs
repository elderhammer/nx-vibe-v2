// CamProbeV2Ncm.cs — OP-002 残余长度差（36 区同构 3257/4718 vs gt 929）非切削族写面判别
// （2026-09-06，run_journal 批处理，单件内存态不 Save）
//
// 事实基础（先查证后写码，2026-09-06）：
//   ① 挂点 = CavityMillingBuilder.NonCuttingBuilder : NcmPlanarBuilder（反射：只读挂点，子树可写；
//      C++ SetTransferWithinLevelsType Created NX5.0.0/License cam_base——CAM_NcmPlanarBuilder.hxx）。
//   ② .NET 面（反射）：NcmPlanarBuilder.TransferWithinLevelsType（10 值，CanWrite=True）/
//      TransferWithinLevelsHeightBuilder 子树；NcmPlanarEngRetBuilder.EngRetType/IfEngageDoesNotFit/
//      MinimumClearance/Trim（CanWrite=True）+ HeightBuilder/HelicalRampAngleBuilder/MinClearanceBuilder
//      子树（.Value/.Intent）；NcmSmoothingBuilder.TransferTolerance/EngRetTolerance（Double）。
//   ③ 官方样例（四路第 4 路命中，非零）：PlanarOpsSetNCMCycleAll.vb（SampleNXOpenApplications\DotNet\CAM）
//      写面范式 = planarOperationBuilder.NonCuttingBuilder → 子树 builder .Value/.Intent → **operation
//      builder .Commit()**（非 ncm 子树 commit），CavityMillingBuilder 分支含于样例。
//   ④ 差键映射（surfdiff 002349 ↔ v2surf-gt 195205 OP-002 段，行号精确对齐）：gt vs 克隆 diff 53 键中
//      Ncm 族 ~44——转移族（TransferWithinLevelsType Direct vs Clearance、层内转移高度 0.5 vs 3）、
//      进刀/退刀 6 builder（EngageClosedArea EngRetType SameAsEngage vs Helical、Height 0.5/0.5/0.3/0.3/20/20
//      vs 3、螺旋角 3 vs 15、IfEngageDoesNotFit Plunge vs Skip ×6、MinimumClearance None vs SameAs*、
//      MinClearanceBuilder 0/PartUnits vs 50/Function、Trim False vs True）、光顺族（SmoothingRadius
//      0 vs 10、容差 0.02 vs 0.03）。值差全在白名单外（非切削族从未入写链）。
//
// 判别设计（基线 = 带 ReferenceTool=17 克隆 36 区 4718.5/1.268，regen 秒级）：
//   C0 基线（ref only）→ 对照 4718.5/1.268（同会话健康锚）。
//   C1 = ref + 转移族 2 键（TransferWithinLevelsType=Direct、层内转移高度 0.5）→ 单族贡献。
//   C3 = ref + 转移族 + 进刀/退刀族全键 + 光顺族（C1 全集 + 6 EngRet builder 差键按 gt 值）→
//        收敛向 929/0.515 = 非切削族全因定案；部分收敛 → 拆贡献；不收敛 → 残余在 Vector/Trim
//        对象键（下一轮）。
// 每克隆写后：新 builder 读回断言（TransferWithinLevelsType/抽查值,faceset 写面不持久教训）→ regen
// → time/length/regions。键路径 = 反射/样例实证面（直属性直赋；子树 builder .Value/.Intent）。
// 只读纪律：不 Save（克隆退出即弃）。输出：samples\camprobe-v2ncm-<ts>.txt。件 = CAMSIG_PRT。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Ncm
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2ncm-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Ncm（OP-002 残余长度差 非切削族写面判别）==");
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
            TaggedObject[] f6 = null; NCGroup refTool = null; NCGroup curTool = op2.ParentMachineTool;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                f6 = cag.GeometryList.FindItem(0).GetItems();
                NXOpen.CAM.Tool rt = bg.ReferenceTool;
                if (rt == null) throw new Exception("gt OP-002 参考刀具读回 null（前置判别要求）");
                refTool = rt as NCGroup;
            }
            finally { bg.Destroy(); }
            Log("OP-002 面数=" + f6.Length + " 当前刀=" + curTool.Name + " 参考刀具=" + refTool.Name);

            Log("");
            Log("== C0 基线（ref only）==");
            Operation c0 = CloneOp(op2, "NCM_C0", f6, refTool, null);
            if (c0 != null) GenAndRead(c0, "C0 ref-only");

            Log("");
            Log("== C1 转移族（ref + TransferWithinLevelsType=Direct + 层内转移高度 0.5）==");
            Operation c1 = CloneOp(op2, "NCM_C1", f6, refTool, WriteTransfer);
            if (c1 != null) GenAndRead(c1, "C1 转移族");

            Log("");
            Log("== C3 全键（ref + 转移族 + 6 EngRet builder 差键 + 光顺族）==");
            Operation c3 = CloneOp(op2, "NCM_C3", f6, refTool, delegate(CavityMillingBuilder b, NCGroup tool)
            {
                WriteTransfer(b, tool);
                WriteEngRetAll(b);
                WriteSmoothing(b);
            });
            if (c3 != null) GenAndRead(c3, "C3 全键");

            Log("对照: reftool 探针 P1 gt 克隆带 ref = 36 区 4718.5/1.268；gt 本体 = 36 区 928.8/0.515");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // ---- 转移族（C1）----
    private static void WriteTransfer(CavityMillingBuilder b, NCGroup _)
    {
        NcmPlanarBuilder ncm = b.NonCuttingBuilder;
        ncm.TransferWithinLevelsType = NcmPlanarBuilder.TransferWithinLevelsTypes.Direct;
        // gt 层内转移高度 0.5/PartUnits vs 模板默认 3/ToolDep（surfdiff 831 区；Inheritable 带 Intent）
        ncm.TransferWithinLevelsHeightBuilder.Value = 0.5;
        try { ncm.TransferWithinLevelsHeightBuilder.Intent = ParamValueIntent.PartUnits; }
        catch (Exception e) { Log("  !! WithinLevelsHeight Intent 写异常: " + e.Message); }
    }

    // ---- 进刀/退刀族：6 EngRet builder 差键按 gt 值（surfdiff 映射：行区 190-733）----
    private static void WriteEngRetAll(CavityMillingBuilder b)
    {
        NcmPlanarBuilder ncm = b.NonCuttingBuilder;
        // EngageClosedArea: EngRetType=SameAsEngage、Height 0.5、螺旋角 3、IfEngage=Plunge
        try { ncm.EngageClosedAreaBuilder.EngRetType = NcmPlanarEngRetBuilder.EngRetTypes.SameAsEngage; }
        catch (Exception ex) { Log("  !! EngageClosedArea.EngRetType 写异常: " + ex.Message); }
        WriteEngRet(ncm.EngageClosedAreaBuilder, 0.5, 3.0, true, true);
        // EngageInitialClosed: Height 0.5、IfEngage=Plunge（EngRetType 双侧同，不写）
        WriteEngRet(ncm.EngageInitialClosedBuilder, 0.5, double.NaN, true, false);
        // EngageInitialOpen: Height 0.3、IfEngage=Plunge、MinimumClearance=None、MinClearance 0/PartUnits
        WriteEngRet(ncm.EngageInitialOpenBuilder, 0.3, double.NaN, true, true);
        // EngageOpenArea: Height 0.3、IfEngage=Plunge、MinClearance None/0、Trim=False
        WriteEngRet(ncm.EngageOpenAreaBuilder, 0.3, double.NaN, true, true);
        // RetractArea: Height 20、IfEngage=Plunge、MinClearance None/0
        WriteEngRet(ncm.RetractAreaBuilder, 20.0, double.NaN, true, true);
        // RetractFinal: Height 20、IfEngage=Plunge、MinClearance None/0
        WriteEngRet(ncm.RetractFinalBuilder, 20.0, double.NaN, true, true);
    }

    // height = gt HeightBuilder 值（NaN=双侧同不写）；ramp = EngageClosedArea 螺旋角 3（gt）——NaN=不写；
    // engSameAsEngage=true 且 builder=ClosedArea 时写 EngRetType=SameAsEngage；withMinClear=true 时写
    // MinimumClearance=None + MinClearanceBuilder 0/PartUnits（gt 4 builder 差键形态）
    private static void WriteEngRet(NcmPlanarEngRetBuilder e, double height, double ramp, bool plunge, bool withMinClear)
    {
        try
        {
            if (!double.IsNaN(height)) { e.HeightBuilder.Value = height; e.HeightBuilder.Intent = ParamValueIntent.PartUnits; }
            if (!double.IsNaN(ramp)) e.HelicalRampAngleBuilder.Value = ramp;
            if (plunge) e.IfEngageDoesNotFit = NcmPlanarEngRetBuilder.IfEngageDoesNotFitTypes.Plunge;
            if (withMinClear)
            {
                e.MinimumClearance = NcmPlanarEngRetBuilder.MinClearanceTypes.None;
                e.MinClearanceBuilder.Value = 0;
                e.MinClearanceBuilder.Intent = ParamValueIntent.PartUnits;
            }
            Log("    EngRet 写: height=" + (double.IsNaN(height) ? "-" : height.ToString("0.####"))
                + " ramp=" + (double.IsNaN(ramp) ? "-" : ramp.ToString("0.####"))
                + " plunge=" + plunge + " minClear=" + withMinClear);
        }
        catch (Exception ex) { Log("  !! EngRet 写异常: " + ex.GetType().Name + " " + ex.Message); }
    }

    // ---- 光顺族（Smoothing）----
    private static void WriteSmoothing(CavityMillingBuilder b)
    {
        NcmSmoothingBuilder sm = b.NonCuttingBuilder.SmoothingBuilder;
        try { sm.TransferTolerance = 0.02; } catch (Exception e) { Log("  !! TransferTolerance 写异常: " + e.Message); }
        try { sm.EngRetTolerance = 0.02; } catch (Exception e) { Log("  !! EngRetTolerance 写异常: " + e.Message); }
        // gt SmoothingRadius 0/PartUnits vs 模板 10/Function（surfdiff 778-781 区）
        try { sm.SmoothingRadius.Value = 0; sm.SmoothingRadius.Intent = ParamValueIntent.PartUnits; }
        catch (Exception e) { Log("  !! SmoothingRadius 写异常: " + e.Message); }
    }

    // regionclone 同款克隆 + ref tool；extra = 附加 Ncm 写动作（null = 仅 ref）
    private static Operation CloneOp(Operation src, string kind, TaggedObject[] faces, NCGroup refTool,
        Action<CavityMillingBuilder, NCGroup> extra)
    {
        try
        {
            NCGroup prog = src.ParentProgramOrder, method = src.ParentMachineMethod;
            NCGroup tool = src.ParentMachineTool, geom = src.ParentGeometry;
            if (prog == null || method == null || tool == null || geom == null)
            { Log("  !! 锚点缺失，中止克隆"); return null; }
            Operation neu = _cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, kind);
            CavityMillingBuilder bn = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                bn.CutPattern.CutPattern = CutPatternBuilder.Types.Profile;
                bn.CutParameters.CutOrder = CutParametersCutOrderTypes.DepthFirst;
                bn.CutParameters.CutDirection.Type = CutDirection.Types.Climb;
                bn.CutParameters.FinishPasses.NumberOfFinishPasses = 0;
                bn.CutParameters.PartStock.Value = 0;
                bn.CutParameters.FloorStock.Value = 0;
                CutLevel cl = bn.CutLevel;
                if (cl != null) cl.GlobalDepthPerCut.DistanceBuilder.Value = 0.2;
                bn.FeedsBuilder.SpindleRpmBuilder.Value = 3000;
                bn.FeedsBuilder.FeedCutBuilder.Value = 1200;
                NXOpen.CAM.Geometry cag = bn.CutAreaGeometry;
                cag.GeometryList.FindItem(0).Selection.SetArray(faces);
                Tool rt = refTool as Tool;
                if (rt != null) bn.ReferenceTool = rt;
                else Log("  !! refTool 非 Tool 运行时实例——ref 未写（基线失效）");
                if (extra != null) extra(bn, tool);
                bn.Commit();
                Log("  克隆创建 OK: " + neu.Name);
            }
            finally { bn.Destroy(); }
            // 读回断言：ref tool + 抽查 TransferWithinLevelsType（持久判据）
            CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                Tool rt2 = b2.ReferenceTool;
                string tr = "-";
                try { tr = b2.NonCuttingBuilder.TransferWithinLevelsType.ToString(); }
                catch (Exception e) { tr = "ERR " + e.Message; }
                Log("  读回断言: ReferenceTool=" + (rt2 == null ? "null!!" : rt2.Name)
                    + "  TransferWithinLevelsType=" + tr);
            }
            finally { b2.Destroy(); }
            return neu;
        }
        catch (Exception e) { Log("  !! 克隆异常: " + e.Message); return null; }
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

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
