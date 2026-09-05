// CamProbeV2DepthWrite.cs — v2.5 深度键修正前置：rebuilt 侧 CutLevel DPC 写面验证探针
//（2026-09-05，run_journal 批处理，单件内存态不 Save）
//
// 背景：v2 [I] 腔 12 条长度/区域结构差残余归因 = CutLevel.GlobalDepthPerCut 未复刻（gt 0.3/0.2/20/20
//   vs reb 模板默认 1，docs/nx-v2-geom-spec.md §7）；plan 现读写的 op 级 b.DepthPerCut（test.plan.json
//   四腔 op depth_per_cut 全 = 0，op 级恒 0 继承）非腔真实 stepdown 成员（camprobe-v2fix 头注释 =
//   gt 各 op CutLevel 值实录：OP-001=0.3 / OP-002=0.2 / OP-003(γ)=20 / OP-004=20）。
//   camprobe-v2flip 已证：**gt 侧** CutLevel 子树写 → commit → 新 builder 读回持久 ✓；
//   本批关键未知 = **rebuilt 侧把 DPC 写成 gt 值 → 引擎消费 → 刀路长度/区域向 gt 收敛**。
//
// 实验（每 op：基线 → 判别 → 还原；判据 = commit 后独立 builder 读回 / 重生成读值）：
//   ① op 级对照（仅非 γ op）：b.DepthPerCut = gt 值 → 重生成 → 记录长度（预期不变 →
//      证实"op 级写不消费"，即现 executor 行为为何无效）；
//   ② CutLevel.GlobalDepthPerCut.DistanceBuilder = gt 值 → 重生成 → 预期 length/regions 按
//      深度反比方向移动（消费 ✓；若读回 != 写入 = U-6 式静默还原 ✗；若写持久但长度不动 = 引擎不消费 ✗）；
//   ③ γ op（名 = CAVITY_MILL_COPY_COPY，flip/v2fix 实证名；缺失则按树序 slot2 兜底）写 gt 值 20
//      → 重生成 → 预期仍 0（γ 稳定阴性对照，证明写机制本身不改变 γ 行为）。
// 还原纪律：每 op 测毕还原至测前原值；全程不 Save（资产只读）。
// 输出：samples\camprobe-v2depth-<ts>.txt；件 = CAMSIG_PRT 环境变量（默认 samples\v2.rebuilt-20260905-203402.prt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2DepthWrite
{
    private static string _out;
    private static CAMSetup _cam;
    private static double _lenBefore = double.NaN, _lastTime, _lastLen;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt))
            prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\v2.rebuilt-20260905-203402.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2depth-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2DepthWrite（rebuilt 侧 CutLevel DPC 写面验证）==");
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

            // 腔 op 按树序收集（test.plan.json workplan 实证序：slot0=OP-001 CAVITY_MILL /
            // slot1=OP-002 CAVITY_MILL_COPY / slot2=OP-003 CAVITY_MILL_COPY_COPY(γ) /
            // slot3=OP-004 CAVITY_MILL_COPY_COPY_COPY）
            var ops = new List<Operation>();
            WalkOps(_cam.GetRoot(CAMSetup.View.ProgramOrder), ops);
            Log("腔 op 数=" + ops.Count);
            Operation gamma = null;
            foreach (Operation o in ops) if (o.Name == "CAVITY_MILL_COPY_COPY") gamma = o;
            if (gamma == null && ops.Count >= 4) gamma = ops[2];
            if (ops.Count < 4) Log("!! 腔 op 不足 4，序位映射可能错位（继续按可得序位跑）");

            // gt 值表 = camprobe-v2fix 头注释实录（2026-09-05 同件同源）：slot0=0.3 / slot1=0.2 /
            // slot2(γ)=20 / slot3=20（写后新 builder 读回自行核对）
            double[] gtBySlot = { 0.3, 0.2, 20.0, 20.0 };
            for (int i = 0; i < ops.Count; i++)
            {
                Operation op = ops[i];
                bool isGamma = op == gamma;
                Log("");
                Log("== slot" + i + " op: " + op.Name + (isGamma ? "  [γ 阴性对照]" : "") + " ==");
                RunOp(op, isGamma ? 20.0 : gtBySlot[Math.Min(i, 3)], isGamma);
            }
            Log("== 结束（全程未 Save；已逐 op 还原）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // 单 op：读基线 → （非 γ）op 级对照写 → CutLevel 写 gt 值 → 判定 → 还原
    private static void RunOp(Operation op, double gtValue, bool isGamma)
    {
        double curCutLevel = double.NaN;
        CavityMillingBuilder b0 = null;
        try
        {
            b0 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            R("op 级 DepthPerCut", () => b0.DepthPerCut.Value.ToString("0.####"));
            R("op 级 DepthPerCut status", () => b0.DepthPerCut.InheritanceStatus.ToString());
            curCutLevel = ReadCutLevelDepth(b0);
            Log("CutLevel.GlobalDepthPerCut.DistanceBuilder = " + curCutLevel.ToString("0.####"));
        }
        catch (Exception e)
        {
            Log("  !! 基线读异常: " + e.GetType().Name + " " + e.Message + "（本 op 跳过）");
            return;
        }
        finally { if (b0 != null) b0.Destroy(); }

        GenAndRead(op, "基线");
        if (double.IsNaN(_lenBefore) || _lenBefore <= 0) Log("  !! 基线长度 0/无 → 消费判别不可用（记录写持久即可）");
        Log("（gt 值 = " + gtValue.ToString("0.####") + "，d0=" + curCutLevel.ToString("0.####") + "）");

        if (!isGamma)
        {
            // ① op 级对照：写 gt 值到 op 级 DepthPerCut → 重生成（预期长度不变 = op 级写不消费）
            Log("-- ① op 级对照：b.DepthPerCut=" + gtValue.ToString("0.####") + " --");
            try
            {
                CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try { b.DepthPerCut.Value = gtValue; b.Commit(); }
                finally { b.Destroy(); }
                GenAndRead(op, "op 级写后");
            }
            catch (Exception e) { Log("  !! op 级写异常: " + e.GetType().Name + " " + e.Message); }
        }

        // ② CutLevel 子树写 gt 值（commit → 新 builder 读回判持久 → 重生成判消费）
        Log("-- ② CutLevel DPC=" + gtValue.ToString("0.####") + " --");
        try
        {
            CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try { WriteCutLevelDepth(b, gtValue); b.Commit(); }
            finally { b.Destroy(); }
        }
        catch (Exception e) { Log("  !! CutLevel 写异常: " + e.GetType().Name + " " + e.Message); }

        double after = double.NaN;
        CavityMillingBuilder b2 = null;
        try
        {
            b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            after = ReadCutLevelDepth(b2);
        }
        catch (Exception e) { Log("  !! 读回异常: " + e.Message); }
        finally { if (b2 != null) b2.Destroy(); }
        Log("  commit 后新 builder 读回 = " + (double.IsNaN(after) ? "-" : after.ToString("0.####"))
            + (Math.Abs(after - gtValue) < 1e-9 ? "  <<< 写持久 ✓" : "  <<< 写未持久/静默还原 ✗"));

        GenAndRead(op, "CutLevel 写后（gt 值）");
        Verdict(op.Name, curCutLevel, gtValue, isGamma);

        // 还原
        Log("-- 还原 CutLevel DPC=" + curCutLevel.ToString("0.####") + " --");
        try
        {
            CavityMillingBuilder b3 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try { WriteCutLevelDepth(b3, curCutLevel); b3.Commit(); }
            finally { b3.Destroy(); }
            if (!isGamma)
            {
                CavityMillingBuilder b4 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try { b4.DepthPerCut.Value = 0.0; b4.Commit(); }
                finally { b4.Destroy(); }
            }
            Log("  （已还原）");
        }
        catch (Exception e) { Log("  !! 还原异常: " + e.Message + "（后续 op 可能受污染）"); }
    }

    // 消费判别：非 γ 期望长度沿深度反比方向移动（d↓→层数↑→len↑）；γ 期望仍 0
    private static void Verdict(string opName, double d0, double d1, bool isGamma)
    {
        if (isGamma)
        {
            Log("  判定[" + opName + "] γ 对照: time=" + _lastTime.ToString("0.####")
                + (_lastTime > 0 ? "  <<< 意外非零！" : "  <<< 仍 0 ✓（γ 稳定，写机制未改其行为）"));
            return;
        }
        if (_lastTime <= 0) { Log("  判定[" + opName + "] 写后仍零刀路，消费判别不可用"); return; }
        if (double.IsNaN(_lenBefore) || _lenBefore <= 0) { Log("  判定[" + opName + "] 基线零刀路，无法判消费"); return; }
        double ratio = _lastLen / _lenBefore;
        bool expectUp = d1 < d0;           // 深度变小 → 层数变多 → 长度↑
        bool moved = expectUp ? (ratio > 1.2) : (ratio < 0.8);
        Log("  判定[" + opName + "] 深度 " + d0.ToString("0.####") + "→" + d1.ToString("0.####")
            + "，length 比 = " + ratio.ToString("0.###") + " regions=" + _lastRegions + "/" + _baseRegions
            + (moved ? "  <<< 消费 ✓（方向符合深度反比）" : "  <<< 长度未按预期移动 ✗（引擎不消费/未生效）"));
    }

    private static double _baseRegions = double.NaN, _lastRegions = double.NaN;

    private static void GenAndRead(Operation op, string label)
    {
        try { _cam.GenerateToolPath(new CAMObject[] { op }); }
        catch (Exception e) { Log("  [" + label + "] 生成异常: " + e.Message); return; }
        double tp = op.GetToolpathTime();
        double tl = op.GetToolpathLength();
        double regN = double.NaN;
        try
        {
            CutRegionsData crd = op.CutRegionsData;
            if (crd != null) regN = crd.NumberRegions;
        }
        catch (Exception e) { Log("  [" + label + "] regions 读异常: " + e.Message); }
        if (label == "基线") { _lenBefore = tl; _baseRegions = regN; }
        _lastTime = tp; _lastLen = tl; _lastRegions = regN;
        Log("  [" + label + "] time=" + tp.ToString("0.####") + " length=" + tl.ToString("0.####")
            + " regions=" + (double.IsNaN(regN) ? "-" : regN.ToString("0"))
            + (tp > 0 ? "  <<< 非零" : ""));
    }

    private static double ReadCutLevelDepth(CavityMillingBuilder b)
    {
        NXOpen.CAM.CutLevel cl = b.CutLevel;
        if (cl == null) throw new Exception("CutLevel null");
        return cl.GlobalDepthPerCut.DistanceBuilder.Value;
    }

    private static void WriteCutLevelDepth(CavityMillingBuilder b, double v)
    {
        NXOpen.CAM.CutLevel cl = b.CutLevel;
        if (cl == null) throw new Exception("CutLevel null");
        cl.GlobalDepthPerCut.DistanceBuilder.Value = v;
    }

    private static void WalkOps(NCGroup node, List<Operation> acc)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.GetNameOfType().ToLower().Contains("cavity")) { acc.Add(op); continue; }
            NCGroup sub = m as NCGroup;
            if (sub != null) WalkOps(sub, acc);
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
