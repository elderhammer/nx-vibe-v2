// CamProbeV2Fix.cs — OP-003 空刀路判别实验 3：写回候选参数 + 重生成（2026-09-05，
// run_journal 批处理，单件模式，内存态不 Save）
//
// 背景：表面 dump（camprobe-v2surf）显示 rebuilt OP-003 与 gt 唯一显著差异候选 =
//   b.CutLevel.GlobalDepthPerCut.DistanceBuilder.Value（gt=20 vs reb 模板默认 1）；
//   （gt 件内 OP-001=0.3 / OP-002=0.2 / OP-003=20 / OP-004=20——plan 现读写的 op 级
//   b.DepthPerCut 恒 0 继承，腔 real stepdown 藏在 CutLevel 子树，executor 未复刻）。
// 本探针 = 对 rebuilt OP-003 逐候选写回 gt 值 → Commit → 重生成 → 读 time/regions，
// 每候选后还原原值再测下一候选（隔离；同 op 单 builder 纪律：写经提交的那个 builder）。
// 只读纪律：不 Save（全部内存态）。输出：samples\camprobe-v2fix-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Fix
{
    private static string _out;
    private static Session _s;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\v2.rebuilt-20260905-191437.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2fix-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Fix（OP-003 候选写回 + 重生成判别）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("件: " + prt);
        try
        {
            _s = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            PartLoadStatus st;
            Part p = _s.Parts.OpenDisplay(prt, out st);
            _s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!_s.IsCamSessionInitialized()) _s.CreateCamSession();
            Log("打开 OK: " + p.Name + "  camSession=" + _s.IsCamSessionInitialized());

            _cam = p.CAMSetup;
            if (_cam == null) throw new Exception("无 CAMSetup");
            Operation op = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
            if (op == null) throw new Exception("找不到 OP-003");

            Log("== 基线（未改）==");
            GenAndRead(op, "基线");

            Test(op, "C1 CutLevel.GlobalDepthPerCut.Distance=20",
                b => opCutLevelDepth(b, 20.0), b => opCutLevelDepth(b, 1.0));
            Test(op, "C2 b.DepthPerCut=20",
                b => opDepthPerCut(b, 20.0), b => opDepthPerCut(b, 0.0));
            Test(op, "C3 Stepover.PercentToolFlat=65",
                b => opStepover(b, 65.0), b => opStepover(b, 70.0));
            Test(op, "C4 CutAreaExtensionDistance=2",
                b => opCutAreaExt(b, 2.0), b => opCutAreaExt(b, 0.0));
            Test(op, "C5 C1+C2 组合",
                b => { opCutLevelDepth(b, 20.0); opDepthPerCut(b, 20.0); },
                b => { opCutLevelDepth(b, 1.0); opDepthPerCut(b, 0.0); });
            Test(op, "C6 BlankStock=0.2",
                b => opBlankStock(b, 0.2), b => opBlankStock(b, 0.0));

            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void Test(Operation op, string label, Action<CavityMillingBuilder> apply,
        Action<CavityMillingBuilder> revert)
    {
        Log("");
        Log("== " + label + " ==");
        try
        {
            CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try { apply(b); b.Commit(); }
            finally { b.Destroy(); }
            GenAndRead(op, label);
        }
        catch (Exception e)
        {
            Log("  !! 写入/提交异常: " + e.GetType().Name + " " + e.Message);
        }
        try
        {
            CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try { revert(b2); b2.Commit(); }
            finally { b2.Destroy(); }
            Log("  （已还原）");
        }
        catch (Exception e) { Log("  !! 还原异常: " + e.Message + "（后续候选可能受污染）"); }
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
            + " regions=" + reg + (tp > 0 ? "  <<< 非零！" : ""));
    }

    private static void opCutLevelDepth(CavityMillingBuilder b, double v)
    {
        NXOpen.CAM.CutLevel cl = b.CutLevel;
        if (cl == null) throw new Exception("CutLevel null");
        cl.GlobalDepthPerCut.DistanceBuilder.Value = v;
    }

    private static void opDepthPerCut(CavityMillingBuilder b, double v) { b.DepthPerCut.Value = v; }
    private static void opStepover(CavityMillingBuilder b, double v) { b.CutParameters.Stepover.PercentToolFlatBuilder.Value = v; }
    private static void opCutAreaExt(CavityMillingBuilder b, double v) { b.CutParameters.CutAreaExtensionDistance.Value = v; }
    private static void opBlankStock(CavityMillingBuilder b, double v) { b.CutParameters.BlankStock.Value = v; }

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
