// CamProbePtpKeys.cs — PTP 4 条键错位收尾判定探针（2026-09-05，run_journal 批处理）
//
// 背景：comparer 残留 4 条 = OP_PARAM_DIFF 单侧缺失（hole_depth A=0/B=无、bottom_stock A=无/B=0，×2 op）。
//   根因 = PTP（打点/钻头G83）→ DRILLING 重建近似（executor spec approximation）+ 采集键面不对称：
//   NxCollect PTP 分支读 HoleDepth（OperationBuilder 基类 InheritableDoubleBuilder，NX5 头文件实证），
//   孔族分支读 CuttingParameters.BottomStock（HoleMachiningCutParameters，NX9.0.2）——两侧键不同。
//   官方样例库 HoleDepth/BottomStock 零 CAM 参数面用法（仅 PolygonalHole 特征样例，无关）。
//
// 实验（判定本批关闭路径，U-6 判据：commit → 独立 builder 读回 == 写入 = 持久）：
//   mode=P1 写面持久（fresh part，会话纪律 = 建件→CreateCamSession→CreateCamSetup，单件/会话）：
//     P1A 写 HoleDepth=8.5 → commit → 读回？   P1B 写 CuttingParameters.BottomStock=0.3 → commit → 读回？
//     （执行两次 run_journal = 双会话复现，持久键判据满足 registry 纪律）
//   mode=P2 读面双档（件 = CAMSIG_PRT，默认 samples\test.prt；rebuilt 档跑 v2.rebuilt-20260905-215029.prt）：
//     逐钻孔族 op（NameOfType 含 drill / point to point）：PointToPointBuilder 或 HoleDrillingBuilder
//     读 HoleDepth.Value/InheritanceStatus/HoleDepthType + （drilling 型）BottomStock/BottomClearance
//     → 键面实况（gt PTP 0/True=继承 vs rebuilt DRILLING 写 0 后 False 预期）→ 对称化可行性判定。
// 只读纪律：P2 不 Save。输出：samples\camprobe-ptpkeys-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbePtpKeys
{
    private static string _out;
    private static Session _s;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt))
            prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-ptpkeys-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbePtpKeys（PTP 键错位收尾判定）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        string mode = System.Environment.GetEnvironmentVariable("CAMSIG_MODE");
        if (string.IsNullOrEmpty(mode)) mode = "P1";
        Log("mode=" + mode + "（P1=写面持久 fresh part / P2=读面双档 CAMSIG_PRT）");
        try
        {
            _s = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            // 会话纪律（params2:49-51 实证）：建件后再 CreateCamSession；单件/会话，不混开第二件
            if (mode == "P1") S1WritePersistence(uf);
            else if (mode == "P2") S2ReadDual(prt, uf);
            else Log("!! 未知 mode: " + mode);
            Log("== 结束（未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // ---- S1 写面持久（hole_making/DRILLING 重建同款；每键一 op）----
    private static void S1WritePersistence(UFSession uf)
    {
        Log("");
        Log("== S1 写面持久（fresh part, mill_contour + hole_making/DRILLING）==");
        Part part = null;
        CAMSetup cam = null;
        try
        {
            part = _s.Parts.NewDisplay("CamProbePtpKeys", Part.Units.Millimeters);   // 1. 先建件
            _s.Parts.SetWork(part);
            uf.Part.SetDisplayPart(part.Tag);
            if (!_s.IsCamSessionInitialized()) _s.CreateCamSession();                // 2. 再 CAM 会话
            cam = part.CreateCamSetup("mill_contour");                               // 3. CreateCamSetup
            Log("建件 OK: " + part.Name);
            NCGroupCollection g = cam.CAMGroupCollection;
            NCGroup prog = g.CreateProgram(cam.GetRoot(CAMSetup.View.ProgramOrder),
                "mill_contour", "PROGRAM", NCGroupCollection.UseDefaultName.False, "PROBE_PROG");
            NCGroup method = g.CreateMethod(cam.GetRoot(CAMSetup.View.MachineMethod),
                "mill_contour", "DRILL_METHOD", NCGroupCollection.UseDefaultName.False, "PROBE_DRILL_METHOD");
            NCGroup tool = g.CreateTool(cam.GetRoot(CAMSetup.View.MachineTool),
                "hole_making", "STD_DRILL", NCGroupCollection.UseDefaultName.False, "PROBE_DRILL_8.5");
            NCGroup geom = g.CreateGeometry(cam.GetRoot(CAMSetup.View.Geometry),
                "mill_contour", "WORKPIECE", NCGroupCollection.UseDefaultName.False, "PROBE_MCS");
            Log("组创建: " + (prog != null && method != null && tool != null && geom != null ? "OK" : "失败"));

            // P1A：HoleDepth 写持久（executor DRILLING 分支写链同款成员）
            TestWritePersistence(cam, prog, method, tool, geom,
                "P1A HoleDepth=8.5", "PROBE_P1A",
                b => { b.HoleDepth.Value = 8.5; },
                b => b.HoleDepth.Value.ToString("0.####"));
            // P1B：BottomStock 写持久（B 侧采集键）
            TestWritePersistence(cam, prog, method, tool, geom,
                "P1B BottomStock=0.3", "PROBE_P1B",
                b => { b.CuttingParameters.BottomStock.Value = 0.3; },
                b => b.CuttingParameters.BottomStock.Value.ToString("0.####"));
        }
        catch (Exception e)
        {
            Log("!! S1 异常: " + e.GetType().Name + " " + e.Message);
        }
    }

    private static void TestWritePersistence(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool,
        NCGroup geom, string label, string opName, Action<HoleDrillingBuilder> write, Func<HoleDrillingBuilder, string> read)
    {
        Log("");
        Log("-- " + label + " --");
        Operation op = null;
        try
        {
            op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "hole_making", "DRILLING", OperationCollection.UseDefaultName.False, opName);
            Log("  Create (" + opName + ") OK -> " + op.Name);
        }
        catch (Exception e) { Log("  !! Create 失败: " + e.Message); return; }

        HoleDrillingBuilder b = null;
        try
        {
            b = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
            write(b);
            b.Commit();
        }
        catch (Exception e) { Log("  !! 写/commit 异常: " + e.GetType().Name + " " + e.Message); return; }
        finally { if (b != null) b.Destroy(); }

        // 新 builder 读回（U-6 口径：独立实例判持久）
        HoleDrillingBuilder b2 = null;
        try
        {
            b2 = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
            string v = read(b2);
            Log("  commit 后新 builder 读回 = " + v + "  <<< 判定: " + label + " 持久性见值");
        }
        catch (Exception e) { Log("  !! 读回异常: " + e.Message); }
        finally { if (b2 != null) b2.Destroy(); }
    }

    // ---- S2 读面双档（逐钻孔族 op 键面实况）----
    private static void S2ReadDual(string prt, UFSession uf)
    {
        Log("");
        Log("== S2 读面双档: " + prt + " ==");
        Part p = null;
        try
        {
            PartLoadStatus st;
            p = _s.Parts.OpenDisplay(prt, out st);
            _s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!_s.IsCamSessionInitialized()) _s.CreateCamSession();   // flip/v2depth 实证序：开件后初始化
            Log("打开 OK: " + p.Name + "  camSession=" + _s.IsCamSessionInitialized());
            CAMSetup cam = p.CAMSetup;
            if (cam == null) { Log("无 CAMSetup"); return; }

            var ops = new List<Operation>();
            WalkOps(cam.GetRoot(CAMSetup.View.ProgramOrder), ops);
            Log("钻孔族 op 数=" + ops.Count);
            foreach (Operation op in ops) ReadOp(cam, op);
        }
        catch (Exception e)
        {
            Log("!! S2 异常: " + e.GetType().Name + " " + e.Message);
        }
    }

    private static void ReadOp(CAMSetup cam, Operation op)
    {
        Log("");
        Log("== op: " + op.Name + "  type=" + NameOfType(op));
        // 先试 PTP builder（gt 旧模板），失败再 HoleDrillingBuilder（rebuilt 近似）；反之亦然
        PointToPointBuilder pb = null;
        HoleDrillingBuilder hb = null;
        try { pb = cam.CAMOperationCollection.CreatePointToPointBuilder(op); }
        catch { pb = null; }
        if (pb == null)
        {
            try { hb = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op); }
            catch { hb = null; }
        }
        if (pb == null && hb == null) { Log("  !! 两 builder 均不可建（非钻孔族?）"); return; }

        if (pb != null)
        {
            Log("  builder = PointToPointBuilder");
            try
            {
                R("HoleDepth", () => pb.HoleDepth.Value.ToString("0.####"));
                R("HoleDepth status", () => pb.HoleDepth.InheritanceStatus.ToString());
                R("HoleDepthType", () => pb.HoleDepthType.ToString());
                R("rpm", () => pb.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
                R("feedCut", () => pb.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
            }
            finally { pb.Destroy(); }
        }
        if (hb != null)
        {
            Log("  builder = HoleDrillingBuilder");
            try
            {
                R("HoleDepth", () => hb.HoleDepth.Value.ToString("0.####"));
                R("HoleDepth status", () => hb.HoleDepth.InheritanceStatus.ToString());
                R("HoleDepthType", () => hb.HoleDepthType.ToString());
                R("BottomStock", () => hb.CuttingParameters.BottomStock.Value.ToString("0.####"));
                R("BottomStock status", () => hb.CuttingParameters.BottomStock.InheritanceStatus.ToString());
                R("BottomClearance", () => hb.CuttingParameters.BottomClearance.Value.ToString("0.####"));
                R("rpm", () => hb.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
                R("feedCut", () => hb.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
            }
            finally { hb.Destroy(); }
        }
    }

    private static void WalkOps(NCGroup node, List<Operation> acc)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null)
            {
                string t = NameOfType(op).ToLower();
                if (t.Contains("drill") || t.Contains("point to point")) acc.Add(op);
                continue;
            }
            NCGroup sub = m as NCGroup;
            if (sub != null) WalkOps(sub, acc);
        }
    }

    private static string NameOfType(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch { return "(unknown)"; }
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
