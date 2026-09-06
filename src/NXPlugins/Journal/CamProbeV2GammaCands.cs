// CamProbeV2GammaCands.cs — OP-003 γ（rebuilt 件空刀路）五候选开关判别探针
// （2026-09-06，run_journal 批处理，rebuilt 件内存态不 Save）
//
// 背景（静态审查 2026-09-06 结论，头文件/样例/XML 实证）：
//   γ = rebuilt（STEP 回导）体上复刻的 CAVITY_MILL_COPY_COPY（3 面）GenerateToolPath=0，
//   gt 件同码同参同面 = 8.03s/3 区。判别链七探针已证 executor 复刻完备 → 归因"体上下文
//   引擎区域形成差异"。头文件/样例审查：区域词族（InteriorRegion/ClosedRegion/...）腔铣
//   宿主零命中；可调项全是"区域已形成后"的执行/滤除旋钮。为把 γ 从"候选开关缺检"升级为
//   闭合，试五个公开面低投入候选（全部 XML 实测成员，License cam_base）：
//   E1  RegionSequencing（MillCutParameters，NX6.0.0）：Optimize / RegionPoints
//       ——区域排序模式，若引擎按序寻区失败，切模式或可重建区域链。
//   E2  SmallAreaAvoidance（MillCutParameters.SmallAreaAvoidance，NX6.0.0）：
//       SmallAreaStatus=Cut + AreaSize=0/PartUnits——排除"3 面被当小面积滤除"假设。
//   E3  GeometrySet.Reversed=true（NX2007，面集反转法向标记）——排除"面法向语义差"假设。
//   E4  CAMSetup.ExtractCutArea(op)（NX2306，诊断）：非空 = 引擎判 op 有 cut area 且产物
//       为 FeatureGeometry（Base Geometry Group，NX7.5）——同时充当 E5v2 的几何父。
//   E5v2  指派通道变体（"AREA" 组字面量不可用——The desired template does not exist 已证）：
//       op 几何父挂 ExtractCutArea 产物区组（同 3 面由引擎复制）、op 级不指派 → 生成——
//       排除"op 级默认集 SetArray 通道本身/直接集引用上下文"假设。产物直生成顺带一试。
// 判定：任一实验 regen 非零 = 候选命中（γ 翻案窗口）；全零 = γ 从候选开关缺检闭合。
// B0 = 无候选克隆基线（预期 0，复现哨兵）。
// 件：默认 samples 最新 v2.rebuilt-*.prt（环境变量 CAMSIG_PRT 可覆盖）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2GammaCands
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt))
        {
            string samples = @"C:\Users\21505\Code\nx-vibe-v2\samples";
            string newest = null;
            DateTime newestT = DateTime.MinValue;
            foreach (string f in Directory.GetFiles(samples, "v2.rebuilt-*.prt"))
            {
                DateTime t = File.GetLastWriteTime(f);
                if (t > newestT) { newestT = t; newest = f; }
            }
            if (newest != null) prt = newest;
        }
        if (string.IsNullOrEmpty(prt)) { Console.WriteLine("无件：设 CAMSIG_PRT 或 samples 下放 v2.rebuilt-*.prt"); return; }
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2gammacands-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2GammaCands（OP-003 γ 五候选开关判别）==");
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

            Operation op3 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
            if (op3 == null) throw new Exception("找不到 γ op CAVITY_MILL_COPY_COPY");
            TaggedObject[] f3 = null;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op3);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("γ op 无几何默认集");
                f3 = cag.GeometryList.FindItem(0).GetItems();
            }
            finally { bg.Destroy(); }
            Log("γ op 面数=" + f3.Length + "（预期 3）当前刀=" + op3.ParentMachineTool.Name);
            if (f3.Length != 3) Log("  !! 面数非 3——签名匹配面数变了吗？继续（判别仍有效）");
            NCGroup prog = op3.ParentProgramOrder, method = op3.ParentMachineMethod;
            NCGroup tool = op3.ParentMachineTool, geom = op3.ParentGeometry;

            Log("");
            Log("== B0 基线克隆（无候选写，γ 复现哨兵）==");
            Operation b0 = CloneGamma(op3, "GAMMA_B0", null, null);
            if (b0 != null) GenAndRead(b0, "B0 基线(预期 0)");

            Log("");
            Log("== E1a RegionSequencing=Optimize ==");
            Operation e1a = CloneGamma(op3, "GAMMA_E1A_OPT", delegate(CavityMillingBuilder b)
            {
                b.CutParameters.RegionSequencing = CutParametersRegionSequencingTypes.Optimize;
            }, delegate(CavityMillingBuilder b)
            {
                Log("   读回 RegionSequencing=" + b.CutParameters.RegionSequencing);
            });
            if (e1a != null) GenAndRead(e1a, "E1a Optimize");

            Log("");
            Log("== E1b RegionSequencing=RegionPoints ==");
            Operation e1b = CloneGamma(op3, "GAMMA_E1B_RPTS", delegate(CavityMillingBuilder b)
            {
                b.CutParameters.RegionSequencing = CutParametersRegionSequencingTypes.RegionPoints;
            }, delegate(CavityMillingBuilder b)
            {
                Log("   读回 RegionSequencing=" + b.CutParameters.RegionSequencing);
            });
            if (e1b != null) GenAndRead(e1b, "E1b RegionPoints");

            Log("");
            Log("== E2 SmallAreaAvoidance: Status=Cut + AreaSize=0/PartUnits ==");
            Operation e2 = CloneGamma(op3, "GAMMA_E2_SMALL", delegate(CavityMillingBuilder b)
            {
                SmallAreaAvoidance sa = b.CutParameters.SmallAreaAvoidance;
                sa.SmallAreaStatus = SmallAreaAvoidance.StatusTypes.Cut;
                sa.AreaSize.Value = 0;
                sa.AreaSize.Intent = ParamValueIntent.PartUnits;
            }, delegate(CavityMillingBuilder b)
            {
                SmallAreaAvoidance sa = b.CutParameters.SmallAreaAvoidance;
                Log("   读回 SmallAreaStatus=" + sa.SmallAreaStatus + " AreaSize="
                    + sa.AreaSize.Value.ToString("0.####") + "/" + sa.AreaSize.Intent);
            });
            if (e2 != null) GenAndRead(e2, "E2 小面积滤除关");

            Log("");
            Log("== E3 GeometrySet.Reversed=true（面集法向反转）==");
            Operation e3 = CloneGamma(op3, "GAMMA_E3_REV", delegate(CavityMillingBuilder b)
            {
                NXOpen.CAM.GeometrySet gs0 = b.CutAreaGeometry.GeometryList.FindItem(0);
                Log("   写前 Reversed=" + gs0.Reversed);
                gs0.Reversed = true;
            }, delegate(CavityMillingBuilder b)
            {
                NXOpen.CAM.GeometrySet gs0 = b.CutAreaGeometry.GeometryList.FindItem(0);
                Log("   读回 Reversed=" + gs0.Reversed);
            });
            if (e3 != null) GenAndRead(e3, "E3 Reversed");

            Log("");
            Log("== E4 + E5v2 CAMSetup.ExtractCutArea(γ op) → 产物 FeatureGeometry 当几何父 ==");
            CAMObject created = null;
            try
            {
                created = _cam.ExtractCutArea(op3);
                if (created == null) { Log("   ExtractCutArea → null（引擎判无 cut area 旁证）"); }
                else Log("   ExtractCutArea → " + created.GetType().Name + " 名=" + created.Name);
            }
            catch (Exception ex) { Log("   !! ExtractCutArea 异常: " + ex.GetType().Name + " " + ex.Message); }

            if (created != null)
            {
                // 对产物直接生成（若非 op 会抛 → 日志即有结论）
                try
                {
                    _cam.GenerateToolPath(new CAMObject[] { created });
                    Log("   产物直生成 OK（未抛 = 引擎对 FeatureGeometry 有生成语义）");
                }
                catch (Exception exg) { Log("   产物直生成 → 非 op 语义: " + exg.Message); }
                // E5v2: 产物（Base Geometry Group）当 op 几何父，op 级不指派
                NCGroup asGroup = created as NCGroup;
                if (asGroup == null)
                { Log("   !! 产物非 NCGroup（" + created.GetType().Name + "）→ E5v2 中止"); }
                else
                {
                    try
                    {
                        Operation e5 = _cam.CAMOperationCollection.Create(prog, method, tool, asGroup,
                            "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, "GAMMA_E5");
                        CavityMillingBuilder b5 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(e5);
                        try
                        {
                            b5.CutPattern.CutPattern = CutPatternBuilder.Types.Profile;
                            b5.CutParameters.CutOrder = CutParametersCutOrderTypes.DepthFirst;
                            b5.CutParameters.CutDirection.Type = CutDirection.Types.Climb;
                            b5.CutParameters.FinishPasses.NumberOfFinishPasses = 0;
                            b5.CutParameters.PartStock.Value = 0;
                            b5.CutParameters.FloorStock.Value = 0;
                            CutLevel cl = b5.CutLevel;
                            if (cl != null) cl.GlobalDepthPerCut.DistanceBuilder.Value = 0.2;
                            b5.FeedsBuilder.SpindleRpmBuilder.Value = 3000;
                            b5.FeedsBuilder.FeedCutBuilder.Value = 1200;
                            b5.Commit();
                            Log("   E5v2 op 创建 OK: " + e5.Name + " geom=" + asGroup.Name);
                        }
                        finally { b5.Destroy(); }
                        GenAndRead(e5, "E5v2 ExtractCutArea 区组通道");
                    }
                    catch (Exception ex5) { Log("   !! E5v2 异常: " + ex5.GetType().Name + " " + ex5.Message); }
                }
            }
            else { Log("   E5v2 无产物可用——跳过"); }

            Log("对照: gt OP-003 = 8.03s/4506.3/3 区；rebuilt 本体 = 0（comparer 143758）");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // 克隆 γ op（同四父锚点 + 默认配方 + 面指派），write = 候选写动作（null=纯基线），
    // readback = commit 后新 builder 读回断言（null=跳过）
    private static Operation CloneGamma(Operation src, string kind, Action<CavityMillingBuilder> write,
        Action<CavityMillingBuilder> readback)
    {
        try
        {
            NCGroup prog = src.ParentProgramOrder, method = src.ParentMachineMethod;
            NCGroup tool = src.ParentMachineTool, geom = src.ParentGeometry;
            if (prog == null || method == null || tool == null || geom == null)
            { Log("  !! 锚点缺失，中止克隆"); return null; }
            TaggedObject[] faces = null;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(src);
            try
            {
                faces = bg.CutAreaGeometry.GeometryList.FindItem(0).GetItems();
            }
            finally { bg.Destroy(); }
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
                bn.CutAreaGeometry.GeometryList.FindItem(0).Selection.SetArray(faces);
                if (write != null) write(bn);
                bn.Commit();
                Log("  克隆创建 OK: " + neu.Name);
            }
            finally { bn.Destroy(); }
            if (readback != null)
            {
                CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
                try { readback(b2); }
                finally { b2.Destroy(); }
            }
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
            + " regions=" + reg + (tp > 0 ? "  <<< 非零——候选命中!" : ""));
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
