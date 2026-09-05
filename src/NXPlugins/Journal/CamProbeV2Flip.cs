// CamProbeV2Flip.cs — OP-003 空刀路判别实验 4：gt 侧翻转 CutLevel DPC（20→1）判别（2026-09-05，
// run_journal 批处理，gt 件内存态不 Save）
//
// 背景：C1..C6 写回（rebuilt 侧 1→20 等）均未解除零刀路，但未验证 CutLevel 子树写是否持久
// （U-6 教训：同形态写可能静默还原）→ 本探针在 **gt 件**（DPC=20 出刀路侧）翻转：
//   ① 写后同 builder 读回 + 新 builder 读回双验证（写持久性判定）；
//   ② DPC=20→1 后重生成：gt OP-003 若归零 → CutLevel.GlobalDepthPerCut 即零化参（executor
//      复刻缺口，可修：plan 深度键改读 CutLevel 子树成员并写入）；若仍出刀路 → DPC 排除，
//      零化在 rebuilt 侧的面/几何解析（走区域细读或定案 NX 内部）。
// 只读纪律：gt 件不 Save（全部内存态）。输出：samples\camprobe-v2flip-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Flip
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2flip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Flip（gt 侧 CutLevel DPC 翻转判别）==");
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
            Operation op = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
            if (op == null) throw new Exception("找不到 OP-003");
            Log("== 基线 ==");
            GenAndRead(op, "基线(DPC 应为 20)");

            double orig;
            CavityMillingBuilder b = null;
            try
            {
                b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                orig = b.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value;
                Log("CutLevel DPC 原值 = " + orig.ToString("0.####"));
                Log("-- 写 DPC=1 --");
                b.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value = 1.0;
                double after1 = b.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value;
                Log("同 builder 写后读回 = " + after1.ToString("0.####"));
                b.Commit();
            }
            finally { if (b != null) b.Destroy(); }
            // 新 builder 读回（持久性终判，U-6 口径）
            CavityMillingBuilder b2 = null;
            try
            {
                b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                double after2 = b2.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value;
                Log("Commit 后新 builder 读回 = " + after2.ToString("0.####")
                    + (Math.Abs(after2 - 1.0) < 1e-9 ? "（写持久 ✓）" : "（写未持久/静默还原 ✗）"));
            }
            finally { if (b2 != null) b2.Destroy(); }
            GenAndRead(op, "DPC=1 重生成后");

            Log("-- 还原 DPC=" + orig.ToString("0.####") + " --");
            CavityMillingBuilder b3 = null;
            try
            {
                b3 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                b3.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value = orig;
                b3.Commit();
            }
            finally { if (b3 != null) b3.Destroy(); }
            GenAndRead(op, "还原后");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
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
