// CamProbeV2NcmGap.cs — OP-002 残余（C3 之上 1141.9 vs gt 928.8）补漏键单键贡献判别
// （2026-09-06，run_journal 批处理，gt 件内存态克隆不 Save）
//
// 事实基础（2026-09-06 头文件/样例/XML 三语料审查，先查证后写码）：
//   ① C1/C3 全集 = ref + TransferWithinLevelsType=Direct + 6 EngRet builder（Height/螺旋角/
//      IfEngageDoesNotFit/MinimumClearance/MinClearanceBuilder）+ 光顺族——C3 实测收敛
//      1298.8→1141.9（17%），gt 本体 928.8 仍有 213mm 差。
//   ② 审查定案：`Trim` 自 **NX10.0.3 废弃**（CAM_NcmPlanarEngRetBuilder.hxx:331-344 → 用
//      MinimumClearance 代替）→ Trim 差键不写（遗留别名）。
//   ③ `TransferWithinLevelsWith` = NcmPlanarBuilder 直属性（嵌套枚举 Withs 5 值，
//      hxx:89-95 **UseEngret = "Use engage and retract defs"** = 层内转移启用进/退刀定义
//      总开关，C1/C3 从未写过 → 第一号候选。
//   ④ 其余补漏候选（全库样例零写范，仅静态面 CanWrite=True）：
//      EngRetType×5（ClosedArea 已写 SameAsEngage；gt 其余五 builder 读值可能同为
//      SameAsEngage vs 模板 Helical/PlungeLift…）、MinimumClearance 扩展形态（ExtendAndTrim
//      /ExtendOnly…，Trim 活通道）、MinRampLengthBuilder.Value、HeightFrom（MeasureHeightFrom
//      三值 hxx:84-92）、ExtendBeforeArc/ExtendAfterArc、MillCutParameters.MinimizeNumberOfEngages。
//
// 判别设计（每实验 = B3 全集 + 单个补漏键按 gt 读值写；commit → 新 builder 读回断言 → regen）：
//   B1 = C1 等价（Type=Direct；height 已撤采 stub，不写）→ 哨兵 1379.9/0.934
//   B3 = C3 等价（全集）→ 哨兵 1141.9/0.694（复现锚）
//   G1  Withs=UseEngret
//   G2  6×EngRetType 按 gt 读值（含 ClosedArea 复核）
//   G3  6×MinimumClearance 扩展形态按 gt
//   G4  6×MinRampLengthBuilder 按 gt
//   G5  6×HeightFrom 按 gt
//   G6  6×ExtendBeforeArc/ExtendAfterArc 按 gt
//   G7  MinimizeNumberOfEngages=gt 值（bool）
// 每 G 实验值 = 启动阶段 gt OP-002 builder 实读（日志落档 = 写前读数，供事后归因）。
// 判定：任一单键贡献显著（B3 基础上再收敛 >2%）→ 命中；全零 → C3 已达公开键面上限，
// 残余 213mm 转永久校准候选。持久性以读回断言为据（#19 Height 教训：Value 写后回填）。
// 件：默认 samples\test.prt（CAMSIG_PRT 可覆盖）。全程不 Save。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2NcmGap
{
    private static string _out;
    private static CAMSetup _cam;

    private class GtRow
    {
        public string EngRetType, HeightFrom, IfEngage, MinClear;
        public double Height = double.NaN, Ramp = double.NaN, MinClearVal = double.NaN;
        public double MinRampLen = double.NaN;
        public double ExtBefore = double.NaN, ExtAfter = double.NaN;
    }
    private static GtRow[] _gt = new GtRow[6];   // 0=ClosedArea 1=InitClosed 2=InitOpen 3=OpenArea 4=RetractArea 5=RetractFinal
    private static string _gtWiths, _gtMinEngages;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2ncmgap-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2NcmGap（OP-002 补漏键单键贡献判别）==");
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
            TaggedObject[] f6 = null; NCGroup refTool = null;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try
            {
                f6 = bg.CutAreaGeometry.GeometryList.FindItem(0).GetItems();
                Tool rt = bg.ReferenceTool;
                if (rt == null) throw new Exception("gt OP-002 参考刀具读回 null（前置要求）");
                refTool = rt as NCGroup;
            }
            finally { bg.Destroy(); }
            Log("OP-002 面数=" + f6.Length + " 参考刀具=" + refTool.Name);

            // 写前读数：gt 六 builder + 层/参数级键（G 系列取值源 + 归因底账）
            Log("");
            Log("== 写前读数（gt 现值，G 系列取值源）==");
            ReadAllGt(op2);

            Log("");
            Log("== B1 哨兵（ref + Type=Direct，C1 等价）==");
            Operation b1 = CloneGap(op2, "GAP_B1", f6, refTool, delegate(CavityMillingBuilder b) { WriteTransfer(b); });
            if (b1 != null) GenAndRead(b1, "B1 哨兵(预期 1379.9/0.934)");

            Log("");
            Log("== B3 哨兵（ref + C3 全集）==");
            Operation b3 = CloneGap(op2, "GAP_B3", f6, refTool, delegate(CavityMillingBuilder b)
            {
                WriteTransfer(b);
                WriteEngRetAll(b);
                WriteSmoothing(b);
            });
            if (b3 != null) GenAndRead(b3, "B3 哨兵(预期 1141.9/0.694)");

            Log("");
            Log("== G1 TransferWithinLevelsWith=UseEngret ==");
            RunG(op2, "G1_WITHS", f6, refTool, delegate(CavityMillingBuilder b)
            {
                b.NonCuttingBuilder.TransferWithinLevelsWith = NcmPlanarBuilder.TransferWithinLevelsWiths.UseEngret;
            }, delegate(CavityMillingBuilder b)
            {
                Log("   读回 With=" + b.NonCuttingBuilder.TransferWithinLevelsWith);
            });

            Log("");
            Log("== G2 6×EngRetType 按 gt ==");
            RunG(op2, "G2_ENGRETTYPE", f6, refTool, delegate(CavityMillingBuilder b)
            {
                NcmPlanarEngRetBuilder[] es = EngRetAll(b);
                for (int i = 0; i < 6; i++)
                {
                    string v = _gt[i].EngRetType;
                    if (v == null || v.Length == 0) continue;
                    NcmPlanarEngRetBuilder.EngRetTypes ev =
                        (NcmPlanarEngRetBuilder.EngRetTypes)Enum.Parse(typeof(NcmPlanarEngRetBuilder.EngRetTypes), v);
                    es[i].EngRetType = ev;
                }
            }, null);

            Log("");
            Log("== G3 6×MinimumClearance 扩展形态按 gt ==");
            RunG(op2, "G3_MINCLR", f6, refTool, delegate(CavityMillingBuilder b)
            {
                NcmPlanarEngRetBuilder[] es = EngRetAll(b);
                for (int i = 0; i < 6; i++)
                {
                    string v = _gt[i].MinClear;
                    if (v == null || v.Length == 0) continue;
                    NcmPlanarEngRetBuilder.MinClearanceTypes ev =
                        (NcmPlanarEngRetBuilder.MinClearanceTypes)Enum.Parse(typeof(NcmPlanarEngRetBuilder.MinClearanceTypes), v);
                    es[i].MinimumClearance = ev;
                }
            }, null);

            Log("");
            Log("== G4 6×MinRampLengthBuilder 按 gt ==");
            RunG(op2, "G4_MINRAMP", f6, refTool, delegate(CavityMillingBuilder b)
            {
                NcmPlanarEngRetBuilder[] es = EngRetAll(b);
                for (int i = 0; i < 6; i++)
                    if (!double.IsNaN(_gt[i].MinRampLen)) es[i].MinRampLengthBuilder.Value = _gt[i].MinRampLen;
            }, delegate(CavityMillingBuilder b)
            {
                NcmPlanarEngRetBuilder[] es = EngRetAll(b);
                Log("   读回 MinRamp[0]=" + es[0].MinRampLengthBuilder.Value.ToString("0.####"));
            });

            Log("");
            Log("== G5 6×HeightFrom 按 gt ==");
            RunG(op2, "G5_HFROM", f6, refTool, delegate(CavityMillingBuilder b)
            {
                NcmPlanarEngRetBuilder[] es = EngRetAll(b);
                for (int i = 0; i < 6; i++)
                {
                    string v = _gt[i].HeightFrom;
                    if (v == null || v.Length == 0) continue;
                    NcmPlanarEngRetBuilder.MeasureHeightFrom ev =
                        (NcmPlanarEngRetBuilder.MeasureHeightFrom)Enum.Parse(typeof(NcmPlanarEngRetBuilder.MeasureHeightFrom), v);
                    es[i].HeightFrom = ev;
                }
            }, null);

            Log("");
            Log("== G6 6×ExtendBeforeArc/ExtendAfterArc 按 gt ==");
            RunG(op2, "G6_EXTARC", f6, refTool, delegate(CavityMillingBuilder b)
            {
                NcmPlanarEngRetBuilder[] es = EngRetAll(b);
                for (int i = 0; i < 6; i++)
                {
                    if (!double.IsNaN(_gt[i].ExtBefore)) es[i].ExtendBeforeArc.Value = _gt[i].ExtBefore;
                    if (!double.IsNaN(_gt[i].ExtAfter)) es[i].ExtendAfterArc.Value = _gt[i].ExtAfter;
                }
            }, null);

            Log("");
            Log("== G7 MinimizeNumberOfEngages 按 gt ==");
            RunG(op2, "G7_MINENG", f6, refTool, delegate(CavityMillingBuilder b)
            {
                bool v = true;
                if (_gtMinEngages == "False") v = false;
                b.CutParameters.MinimizeNumberOfEngages = v;
            }, delegate(CavityMillingBuilder b)
            {
                Log("   读回 MinimizeNumberOfEngages=" + b.CutParameters.MinimizeNumberOfEngages);
            });

            Log("对照: C1 1379.9/0.934 → C3 1141.9/0.694 → gt 本体 928.8/0.515（36 区同构）");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // ---- gt 现值读档（六 builder × 10 键 + 层/参数级 3 键）----
    private static void ReadAllGt(Operation op2)
    {
        for (int i = 0; i < 6; i++) _gt[i] = new GtRow();
        CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
        try
        {
            NcmPlanarBuilder ncm = bg.NonCuttingBuilder;
            try { _gtWiths = ncm.TransferWithinLevelsWith.ToString(); } catch (Exception e) { _gtWiths = "ERR " + e.Message; }
            try { _gtMinEngages = bg.CutParameters.MinimizeNumberOfEngages.ToString(); }
            catch (Exception e) { _gtMinEngages = "ERR " + e.Message; }
            Log("   gt With=" + _gtWiths + " MinimizeNumberOfEngages=" + _gtMinEngages);
            NcmPlanarEngRetBuilder[] es = EngRetAll(bg);
            string[] names = { "Closed", "InitClsd", "InitOpen", "OpenArea", "RetArea", "RetFinal" };
            for (int i = 0; i < 6; i++)
            {
                GtRow r = _gt[i];
                try { r.EngRetType = es[i].EngRetType.ToString(); } catch (Exception e) { r.EngRetType = "ERR " + e.Message; }
                try { r.HeightFrom = es[i].HeightFrom.ToString(); } catch (Exception e) { r.HeightFrom = "ERR " + e.Message; }
                try { r.IfEngage = es[i].IfEngageDoesNotFit.ToString(); } catch (Exception e) { r.IfEngage = "ERR " + e.Message; }
                try { r.MinClear = es[i].MinimumClearance.ToString(); } catch (Exception e) { r.MinClear = "ERR " + e.Message; }
                try { r.Height = es[i].HeightBuilder.Value; } catch { }
                try { r.Ramp = es[i].HelicalRampAngleBuilder.Value; } catch { }
                try { r.MinClearVal = es[i].MinClearanceBuilder.Value; } catch { }
                try { r.MinRampLen = es[i].MinRampLengthBuilder.Value; } catch { }
                try { r.ExtBefore = es[i].ExtendBeforeArc.Value; } catch { }
                try { r.ExtAfter = es[i].ExtendAfterArc.Value; } catch { }
                Log("   gt[" + names[i] + "] EngRetType=" + r.EngRetType + " IfEngage=" + r.IfEngage
                    + " MinClear=" + r.MinClear + " HFrom=" + r.HeightFrom);
                Log("       Height=" + (double.IsNaN(r.Height) ? "-" : r.Height.ToString("0.####"))
                    + " Ramp=" + (double.IsNaN(r.Ramp) ? "-" : r.Ramp.ToString("0.####"))
                    + " MinClrVal=" + (double.IsNaN(r.MinClearVal) ? "-" : r.MinClearVal.ToString("0.####"))
                    + " MinRamp=" + (double.IsNaN(r.MinRampLen) ? "-" : r.MinRampLen.ToString("0.####"))
                    + " ExtArc=" + (double.IsNaN(r.ExtBefore) ? "-" : r.ExtBefore.ToString("0.####"))
                    + "/" + (double.IsNaN(r.ExtAfter) ? "-" : r.ExtAfter.ToString("0.####")));
            }
        }
        finally { bg.Destroy(); }
    }

    private static NcmPlanarEngRetBuilder[] EngRetAll(CavityMillingBuilder b)
    {
        NcmPlanarBuilder n = b.NonCuttingBuilder;
        return new NcmPlanarEngRetBuilder[]
        {
            n.EngageClosedAreaBuilder, n.EngageInitialClosedBuilder, n.EngageInitialOpenBuilder,
            n.EngageOpenAreaBuilder, n.RetractAreaBuilder, n.RetractFinalBuilder
        };
    }

    // B3 全集（C3 码：不含已撤采 height——v2ncm C3 原含 height 写但 stub 不持久，等价于不写）
    private static void RunG(Operation src, string kind, TaggedObject[] f6, NCGroup refTool,
        Action<CavityMillingBuilder> gap, Action<CavityMillingBuilder> readback)
    {
        Operation neu = CloneGap(src, kind, f6, refTool, delegate(CavityMillingBuilder b)
        {
            WriteTransfer(b);
            WriteEngRetAll(b);
            WriteSmoothing(b);
            if (gap != null) gap(b);
        });
        if (neu == null) return;
        if (readback != null)
        {
            CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try { readback(b2); }
            finally { b2.Destroy(); }
        }
        GenAndRead(neu, kind);
    }

    private static void WriteTransfer(CavityMillingBuilder b)
    {
        b.NonCuttingBuilder.TransferWithinLevelsType = NcmPlanarBuilder.TransferWithinLevelsTypes.Direct;
    }

    private static void WriteEngRetAll(CavityMillingBuilder b)
    {
        NcmPlanarBuilder ncm = b.NonCuttingBuilder;
        try { ncm.EngageClosedAreaBuilder.EngRetType = NcmPlanarEngRetBuilder.EngRetTypes.SameAsEngage; }
        catch (Exception ex) { Log("  !! EngageClosedArea.EngRetType 写异常: " + ex.Message); }
        WriteEngRet(ncm.EngageClosedAreaBuilder, 0.5, 3.0, true, true);
        WriteEngRet(ncm.EngageInitialClosedBuilder, 0.5, double.NaN, true, false);
        WriteEngRet(ncm.EngageInitialOpenBuilder, 0.3, double.NaN, true, true);
        WriteEngRet(ncm.EngageOpenAreaBuilder, 0.3, double.NaN, true, true);
        WriteEngRet(ncm.RetractAreaBuilder, 20.0, double.NaN, true, true);
        WriteEngRet(ncm.RetractFinalBuilder, 20.0, double.NaN, true, true);
    }

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
        }
        catch (Exception ex) { Log("  !! EngRet 写异常: " + ex.GetType().Name + " " + ex.Message); }
    }

    private static void WriteSmoothing(CavityMillingBuilder b)
    {
        NcmSmoothingBuilder sm = b.NonCuttingBuilder.SmoothingBuilder;
        try { sm.TransferTolerance = 0.02; } catch (Exception e) { Log("  !! TransferTolerance 写异常: " + e.Message); }
        try { sm.EngRetTolerance = 0.02; } catch (Exception e) { Log("  !! EngRetTolerance 写异常: " + e.Message); }
        try { sm.SmoothingRadius.Value = 0; sm.SmoothingRadius.Intent = ParamValueIntent.PartUnits; }
        catch (Exception e) { Log("  !! SmoothingRadius 写异常: " + e.Message); }
    }

    private static Operation CloneGap(Operation src, string kind, TaggedObject[] faces, NCGroup refTool,
        Action<CavityMillingBuilder> extra)
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
                bn.CutAreaGeometry.GeometryList.FindItem(0).Selection.SetArray(faces);
                Tool rt = refTool as Tool;
                if (rt != null) bn.ReferenceTool = rt;
                else Log("  !! refTool 非 Tool 运行时实例——ref 未写（基线失效）");
                if (extra != null) extra(bn);
                bn.Commit();
                Log("  克隆创建 OK: " + neu.Name);
            }
            finally { bn.Destroy(); }
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
            + " regions=" + reg + (tp > 0 ? "" : "  <<< 零刀路"));
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
