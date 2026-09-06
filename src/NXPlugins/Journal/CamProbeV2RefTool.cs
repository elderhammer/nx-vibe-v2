// CamProbeV2RefTool.cs — OP-002 只切一小截因果判别：ReferenceTool（参考刀具）写回判别（2026-09-06，
// run_journal 批处理，单件内存态不 Save）
//
// 事实基础（先查证后写码，2026-09-06）：
//   ① API 通道四路实证——CavityMillingBuilder : PlanarOperationBuilder : MillOperationBuilder
//      : OperationBuilder（hxx 逐级声明）；MillOperationBuilder::ReferenceTool() 返回 CAM::Tool* +
//      SetReferenceTool(Tool*)（Created NX7.5.0/License None，CAM_MillOperationBuilder.hxx）；
//      .NET 侧 ReferenceTool : NXOpen.CAM.Tool CanWrite=True（反射）；官方样例库零写面范式
//      （SampleNXOpenApplications/NXOpenExamples 递归零命中）；nxopen-research 附 A 已记
//      "参考刀具（用于残余加工）"。
//   ② 测试件离线矩阵（camprobe-v2surf-gt-195205 builder 表面直读，零新运行）：gt 四腔 op 中
//      **仅 CAVITY_MILL_COPY（OP-002）ReferenceTool=[Tool] 非空**，其余三 op 均 null；OP-002
//      正是 gt 唯一"只切两孔底段 18 层×2 窄带 36 区/929/0.515s"者（rebuilt 无此键全 null →
//      全程 118 区/39850/28.4s 43×）→ 4-op 矩阵一一对应；OP-003（rebuilt 空刀路 γ）gt 无参考
//      刀具 → γ 归因与参考刀具无关保持独立。
//   ③ 写面持久判据先例（camprobe-v2faceset-005710）：GeometrySet 属性赋值同对象读回成功、
//      新 builder 读回丢失（commit 未携带）→ 本探针每次写入后必须**新 builder 读回断言**。
//
// 判别设计（每步 regen 后读 regions/time/length；regionclone 同款克隆与读法）：
//   P0（gt 档）：四腔 op ReferenceTool 矩阵直读（null 或 工具名+直径 0.####）+ 当前刀具名对照。
//   P1（gt 档）：OP-002 克隆 + 写 ReferenceTool = 本体参考刀具（同件 tag）→ 读回断言 → regen。
//        预期：读回=gt 参考刀具名（持久）且 119 区 → 收窄向 36 级 = 因果实锤。
//   P2（gt 档，对照）：第二克隆 + 写 ReferenceTool = 当前刀具（9.96 自刀）→ 读回断言 → regen。
//        语义方向佐证：参考刀具直径语义生效则结果 ≠ 119 全程（自刀参考 → 残料 ≈ 0/极窄）。
//   P3（rebuilt 档）：OP-002 本体写 ReferenceTool = 按直径匹配的 B 侧库刀 → 读回断言 → regen。
//        预期 118 → 36 级 = executor 补写键后修复窗口可行（扩展批依据）；无匹配直径 → N/A。
// 件 = CAMSIG_PRT 环境变量（gt 跑 samples\test.prt；rebuilt 跑 v2.rebuilt-<ts>.prt）。
// 只读纪律：不 Save（gt 件克隆退出即弃；rebuilt 件写入仅内存态）。输出：samples\camprobe-v2reftool-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2RefTool
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2reftool-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2RefTool（OP-002 ReferenceTool 因果判别）==");
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

            bool isReb = prt.ToLowerInvariant().Contains("rebuilt");
            Log("档别: " + (isReb ? "rebuilt（P0 复核 + P3 修复窗口）" : "gt（P0 矩阵 + P1 因果 + P2 对照）"));

            // P0：四腔 op ReferenceTool 矩阵（直读 + 当前刀具对照）
            Log("");
            Log("== P0 参考刀具矩阵 ==");
            string[] order = { "CAVITY_MILL", "CAVITY_MILL_COPY", "CAVITY_MILL_COPY_COPY", "CAVITY_MILL_COPY_COPY_COPY" };
            string refToolName = null; Tool refToolObj = null;
            foreach (string nm in order)
            {
                Operation op = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), nm);
                if (op == null) { Log("  !! 找不到 " + nm); continue; }
                string cur = op.ParentMachineTool != null ? op.ParentMachineTool.Name : "null";
                string rt = "null"; double rtDia = double.NaN;
                CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try
                {
                    Tool t = b.ReferenceTool;
                    if (t != null)
                    {
                        rt = t.Name; rtDia = ReadToolDia(t);
                        if (nm == "CAVITY_MILL_COPY") { refToolName = t.Name; refToolObj = t; }
                    }
                }
                catch (Exception e) { rt = "!!ERR " + e.Message; }
                finally { b.Destroy(); }
                Log("  " + nm + ": 当前刀=" + cur + "  参考刀具=" + rt
                    + (double.IsNaN(rtDia) ? "" : "（直径 " + rtDia.ToString("0.####") + "）")
                    + "  time=" + op.GetToolpathTime().ToString("0.####")
                    + " len=" + op.GetToolpathLength().ToString("0.####"));
            }

            if (!isReb)
            {
                // ---- gt 档：P1 因果 + P2 对照 ----
                Operation op2 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
                if (op2 == null) throw new Exception("找不到 OP-002");
                if (refToolObj == null) { Log("!! gt OP-002 参考刀具读回为 null——无法判别，中止 P1/P2"); return; }
                TaggedObject[] f6 = null;
                CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
                try
                {
                    NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                    f6 = cag.GeometryList.FindItem(0).GetItems();
                }
                finally { bg.Destroy(); }
                Log("OP-002 面数=" + f6.Length + "  参考刀具=" + refToolName);

                Log("");
                Log("== P1 因果判别：克隆 + ReferenceTool=" + refToolName + " ==");
                Operation c1 = CloneOp(op2, "REFTool_A", f6);
                if (c1 != null)
                {
                    if (WriteRefTool(c1, refToolObj))
                        GenAndRead(c1, "克隆(参考刀具=" + refToolName + ")");
                    else Log("  !! 读回断言失败——写入未持久，本步结果作废");
                }

                Log("");
                Log("== P2 对照：克隆 + ReferenceTool=当前刀具（自刀）==");
                Operation c2 = CloneOp(op2, "REFTool_B", f6);
                if (c2 != null)
                {
                    NCGroup curTool = op2.ParentMachineTool;
                    if (curTool == null) Log("  !! 本体当前刀具为 null，跳过 P2");
                    else if (WriteRefTool(c2, curTool))
                        GenAndRead(c2, "克隆(参考刀具=自刀 " + curTool.Name + ")");
                    else Log("  !! 读回断言失败——写入未持久，本步结果作废");
                }
            }
            else
            {
                // ---- rebuilt 档：P3 修复窗口（参考刀具 = 按直径匹配 B 侧库刀）----
                Operation op2 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
                if (op2 == null) throw new Exception("找不到 OP-002");
                // gt 参考刀具直径需经 gt 档 P0 结果人工指定：CAMSIG_REFTOOL_DIA 环境变量
                string diaEnv = System.Environment.GetEnvironmentVariable("CAMSIG_REFTOOL_DIA");
                double wantDia = double.NaN;
                if (!string.IsNullOrEmpty(diaEnv)) { double.TryParse(diaEnv, out wantDia); }
                if (double.IsNaN(wantDia))
                {
                    Log("!! 未给 CAMSIG_REFTOOL_DIA（gt 档 P0 实测直径），P3 中止");
                    return;
                }
                Log("");
                Log("== P3 修复窗口：OP-002 写参考刀具 直径=" + wantDia.ToString("0.####") + " ==");
                Tool target = null;
                foreach (NCGroup g in EnumerateTools(_cam.GetRoot(CAMSetup.View.MachineTool)))
                {
                    double d = ReadToolDia(g);
                    if (!double.IsNaN(d) && Math.Abs(d - wantDia) < 0.001) { target = g as Tool; break; }
                }

                if (target == null) { Log("  N/A: rebuilt 库无直径 " + wantDia.ToString("0.####") + " 刀具（写面需先建刀）"); return; }
                Log("  匹配库刀: " + target.Name + "（直径 " + wantDia.ToString("0.####") + "）");
                if (WriteRefTool(op2, target))
                    GenAndRead(op2, "OP-002(参考刀具=" + target.Name + ")");
                else Log("  !! 读回断言失败——写入未持久，本步结果作废");
            }
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // regionclone 同款克隆：同 6 面 + Profile/DPC 0.2/3000/1200 + 父链
    private static Operation CloneOp(Operation src, string kind, TaggedObject[] faces)
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
                bn.Commit();
            }
            finally { bn.Destroy(); }
            Log("  克隆创建 OK: " + neu.Name);
            return neu;
        }
        catch (Exception e) { Log("  !! 克隆异常: " + e.Message); return null; }
    }

    // 写 ReferenceTool + 新 builder 读回断言（faceset 写面不持久教训：同对象读回不算数）
    private static bool WriteRefTool(Operation op, NCGroup toolGroup)
    {
        try
        {
            Tool tool = toolGroup as Tool;
            if (tool == null) { Log("  !! 参考刀具对象非 Tool 运行时实例（" + toolGroup.Name + "），中止"); return false; }
            CavityMillingBuilder b1 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try { b1.ReferenceTool = tool; b1.Commit(); }
            finally { b1.Destroy(); }
            // 新 builder 读回断言（commit 落盘/落 op 的判据）
            CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try
            {
                Tool rt = b2.ReferenceTool;
                if (rt == null) { Log("  !! 读回=null（写入未持久）"); return false; }
                Log("  读回断言 OK: ReferenceTool=" + rt.Name);
                return true;
            }
            finally { b2.Destroy(); }
        }
        catch (Exception e) { Log("  !! 写参考刀具异常: " + e.Message); return false; }
    }

    // NxCollect 同款：Tool 直径读（Mill builder → Drill 兜底）
    private static double ReadToolDia(NCGroup tool)
    {
        MillingToolBuilder b = null;
        try { b = _cam.CAMGroupCollection.CreateMillToolBuilder(tool) as MillingToolBuilder; }
        catch { b = null; }
        if (b == null)
        {
            try { b = _cam.CAMGroupCollection.CreateDrillStdToolBuilder(tool) as MillingToolBuilder; }
            catch { b = null; }
        }
        if (b == null) return double.NaN;
        try { return b.TlDiameterBuilder.Value; }
        catch { return double.NaN; }
        finally { b.Destroy(); }
    }

    private static System.Collections.Generic.List<NCGroup> EnumerateTools(NCGroup node)
    {
        var acc = new System.Collections.Generic.List<NCGroup>();
        foreach (CAMObject m in node.GetMembers())
        {
            NCGroup sub = m as NCGroup;
            if (sub == null) continue;
            Tool t = m as Tool;
            if (t != null) acc.Add(t);
            else acc.AddRange(EnumerateTools(sub));
        }
        return acc;
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
