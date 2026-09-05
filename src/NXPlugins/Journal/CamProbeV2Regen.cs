// CamProbeV2Regen.cs — OP-003 空刀路判别实验 1：新会话重生成探针（2026-09-05，run_journal 批处理，单件模式）
//
// 背景：executor v2（191434）同会话按序生成刀路：OP-001/002/004 出、OP-003（CAVITY_MILL_COPY_COPY，
// 3 面）空（gt 8.03s/3 区）。判别读探针（camprobe-v2op-191955/192013）排除集属性/DPC/feed 假设 →
// 本探针 = 干净会话打开已落盘重建件，仅重生成 OP-003：
//   * 重生成后刀路 >0 → "空刀路"与生成顺序/会话态相关（可修：executor 逐 op 生成后复核 0 者重试）；
//   * 仍 0 → 再按原序全量重生成 OP-001..004（顺序依赖判别）；仍 0 → 配置/几何级缺陷定局，
//     进 BuilderProperties 双档 JSON diff（判别实验 2，CamProbeV2BpDiff）。
// 只读纪律：不 Save（重生成仅内存态，退出即弃；源件为入库资产不改写）。
// 用法：OS 环境变量 CAMSIG_REGEN_PRT = 重建件路径（可选，缺省 samples\v2.rebuilt-20260905-191437.prt）。
// 输出：samples\camprobe-v2regen-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Regen
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_REGEN_PRT");
        if (string.IsNullOrEmpty(prt))
            prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\v2.rebuilt-20260905-191437.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2regen-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Regen（OP-003 新会话重生成判别）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("件: " + prt);
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            UFSession uf = UFSession.GetUFSession();
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(prt, out st);
            s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            Log("打开 OK: " + p.Name + "  camSession=" + s.IsCamSessionInitialized());

            CAMSetup cam = p.CAMSetup;
            if (cam == null) throw new Exception("无 CAMSetup");

            Operation op003 = FindOp(cam, cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
            if (op003 == null) throw new Exception("找不到 OP-003（CAVITY_MILL_COPY_COPY）");
            Log("op 定位 OK: " + op003.Name);

            DateTime t0 = DateTime.Now;
            Log("--- 重生成前 ---");
            ReadOpResult(cam, op003, "OP-003 前");

            Log("--- 判别实验 1a：单独重生成 OP-003 ---");
            DateTime g0 = DateTime.Now;
            cam.GenerateToolPath(new CAMObject[] { op003 });
            Log("生成 wall=" + (DateTime.Now - g0).TotalSeconds.ToString("0.0") + "s");
            ReadOpResult(cam, op003, "OP-003 单独重生成后");

            if (op003.GetToolpathTime() <= 0)
            {
                Log("--- 判别实验 1b：按原生成序重生成 OP-001..004（顺序依赖判别） ---");
                string[] order = { "CAVITY_MILL", "CAVITY_MILL_COPY", "CAVITY_MILL_COPY_COPY", "CAVITY_MILL_COPY_COPY_COPY" };
                foreach (string nm in order)
                {
                    Operation o = FindOp(cam, cam.GetRoot(CAMSetup.View.ProgramOrder), nm);
                    if (o == null) { Log("  !! 找不到 " + nm); continue; }
                    DateTime g1 = DateTime.Now;
                    try
                    {
                        cam.GenerateToolPath(new CAMObject[] { o });
                        Log("  生成 " + nm + " wall=" + (DateTime.Now - g1).TotalSeconds.ToString("0.0")
                            + "s time=" + o.GetToolpathTime().ToString("0.####")
                            + " length=" + o.GetToolpathLength().ToString("0.####"));
                    }
                    catch (Exception e) { Log("  !! 生成 " + nm + " 异常: " + e.Message); }
                }
                ReadOpResult(cam, op003, "OP-003 按序重生成后");
            }

            Log("总 wall=" + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + "s（未 Save，只读会话）");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void ReadOpResult(CAMSetup cam, Operation op, string label)
    {
        double tp = op.GetToolpathTime();
        Log("  [" + label + "] time=" + tp.ToString("0.####") + " length=" + op.GetToolpathLength().ToString("0.####"));
        try
        {
            CutRegionsData crd = op.CutRegionsData;
            Log("    CutRegions n=" + (crd == null ? "null" : crd.NumberRegions.ToString()));
        }
        catch (Exception e) { Log("    CutRegions 读异常: " + e.Message); }
        // op 级面数（确认指派仍在）
        try
        {
            CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try
            {
                NXOpen.CAM.Geometry cag = b.CutAreaGeometry;
                if (cag != null && cag.GeometryList.Length > 0)
                    Log("    CutArea set[0] items=" + cag.GeometryList.FindItem(0).GetItems().Length);
            }
            finally { b.Destroy(); }
        }
        catch (Exception e) { Log("    builder 读异常: " + e.Message); }
    }

    private static Operation FindOp(CAMSetup cam, NCGroup node, string name)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.Name == name) return op;
            NCGroup sub = m as NCGroup;
            if (sub != null)
            {
                Operation hit = FindOp(cam, sub, name);
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
