// CamProbeV2NcmH.cs — transfer_within_levels_height 写面失效写序判别（2026-09-06，run_journal
// 批处理，单件内存态不 Save）
//
// 背景：[I] 复跑（executor-run-142827/comparer-run-143155）暴露：写链 Value=0.5+Intent=PartUnits
// commit 无异常、Direct 键生效（B length 3257→1299），但 comparer 采集 B 侧 height=3（模板默认）
// → height 写未落（A=0.5 B=3 ×4 op FAIL）。CamProbeV2Ncm C1 读回断言只查了 Type 漏了 height →
// 该键可能从未生效（1380 收敛可能纯 Direct 贡献）。
// API 面（反射）：InheritableDoubleBuilder.Value/ValueIntent/ExpressionString 均 CanWrite=True；
// InheritableToolDepBuilder.Intent CanWrite=True（ParamValueIntent: PartUnits|ToolDep|Function|
// ToolFluteLength|LengthPercent）。hxx：Intent NX5.0.0/cam_base；Value NX5.0.0/cam_base；
// SetValueIntent 为 NX_NO_DOC 内部（NX1980）——.NET 有面但语义未公开。
// 判别：4 写序变体（每变体独立克隆 op，commit → 新 builder 读回 Value/Intent/ValueIntent 三态）：
//   H1 = Value 先 → Intent 后（executor 现序）
//   H2 = Intent 先 → Value 后（反序）
//   H3 = H2 + ValueIntent=PartUnits（全显式）
//   H4 = H3 + ExpressionString=""（清内部表达式——若 Value 被表达式态拦截）
// 成功变体（读回 Value=0.5 且 Intent=PartUnits）→ regen 验证引擎消费。只读纪律：不 Save。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2NcmH
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2ncmh-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2NcmH（height 写面失效 写序判别）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            Session s = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(prt, out st);
            s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            _cam = p.CAMSetup;
            if (_cam == null) throw new Exception("无 CAMSetup");

            Operation op2 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
            if (op2 == null) throw new Exception("找不到 OP-002");
            TaggedObject[] f6 = null; NCGroup refTool = op2.ParentMachineTool == null ? null : op2.ParentMachineTool;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                f6 = cag.GeometryList.FindItem(0).GetItems();
                NXOpen.CAM.Tool rt = bg.ReferenceTool;
                if (rt != null) refTool = rt as NCGroup;
            }
            finally { bg.Destroy(); }

            // H1：Value 先 → Intent 后
            RunVariant("H1(Value→Intent)", f6, refTool, delegate(CavityMillingBuilder b, InheritableToolDepBuilder hb)
            {
                hb.Value = 0.5;
                hb.Intent = ParamValueIntent.PartUnits;
            });
            // H2：Intent 先 → Value 后
            RunVariant("H2(Intent→Value)", f6, refTool, delegate(CavityMillingBuilder b, InheritableToolDepBuilder hb)
            {
                hb.Intent = ParamValueIntent.PartUnits;
                hb.Value = 0.5;
            });
            // H3：H2 + ValueIntent
            RunVariant("H3(Intent→Value→ValueIntent)", f6, refTool, delegate(CavityMillingBuilder b, InheritableToolDepBuilder hb)
            {
                hb.Intent = ParamValueIntent.PartUnits;
                hb.Value = 0.5;
                try { hb.ValueIntent = NXOpen.CAM.ValueIntent.PartUnits; }
                catch (Exception e) { Log("  !! H3 ValueIntent 写异常: " + e.Message); }
            });
            // H4：H3 + ExpressionString 清空
            RunVariant("H4(+ExpressionString='')", f6, refTool, delegate(CavityMillingBuilder b, InheritableToolDepBuilder hb)
            {
                hb.Intent = ParamValueIntent.PartUnits;
                hb.Value = 0.5;
                try { hb.ValueIntent = NXOpen.CAM.ValueIntent.PartUnits; }
                catch (Exception e) { Log("  !! H4 ValueIntent 写异常: " + e.Message); }
                try { hb.ExpressionString = ""; }
                catch (Exception e) { Log("  !! H4 ExpressionString 写异常: " + e.Message); }
            });
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void RunVariant(string label, TaggedObject[] faces, NCGroup refTool,
        Action<CavityMillingBuilder, InheritableToolDepBuilder> write)
    {
        Log("");
        Log("== " + label + " ==");
        try
        {
            Operation src = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
            NCGroup prog = src.ParentProgramOrder, method = src.ParentMachineMethod;
            NCGroup tool = src.ParentMachineTool, geom = src.ParentGeometry;
            Operation neu = _cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, "NCMH_" + label.Substring(0, 2));
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
                InheritableToolDepBuilder hb = bn.NonCuttingBuilder.TransferWithinLevelsHeightBuilder;
                write(bn, hb);
                bn.Commit();
                Log("  写入+commit OK");
            }
            finally { bn.Destroy(); }
            // 新 builder 读回三态
            CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                InheritableToolDepBuilder hb = b2.NonCuttingBuilder.TransferWithinLevelsHeightBuilder;
                string vi = "-"; string es = "-";
                try { vi = hb.ValueIntent.ToString(); } catch (Exception e) { vi = "ERR " + e.Message; }
                try { es = hb.ExpressionString ?? "(null)"; } catch (Exception e) { es = "ERR"; }
                Log("  读回: Value=" + hb.Value.ToString("0.####") + " Intent=" + hb.Intent
                    + " ValueIntent=" + vi + " ExpressionString=" + es);
                bool ok = Math.Abs(hb.Value - 0.5) < 1e-9 && hb.Intent == ParamValueIntent.PartUnits;
                Log("  判据: " + (ok ? "PASS（持久）" : "FAIL（未落）"));
                if (ok)
                {
                    try
                    {
                        _cam.GenerateToolPath(new CAMObject[] { neu });
                        Log("  regen: time=" + neu.GetToolpathTime().ToString("0.####")
                            + " length=" + neu.GetToolpathLength().ToString("0.####"));
                    }
                    catch (Exception e) { Log("  regen 异常: " + e.Message); }
                }
            }
            finally { b2.Destroy(); }
        }
        catch (Exception e) { Log("  !! " + label + " 异常: " + e.Message); }
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
