// CamProbeV2RegionFull.cs — 区域配对研究批判别 3：OP-002 完整区域 dump（2026-09-06，run_journal
// 批处理，单件只读，不 regen 不 Save）
//
// 背景：regionclone 四值矩阵（gt 本体 36 区/929 vs 同参克隆 119 区/41434，reb 本体=克隆 118 区）
// 与 surfdiff（53 行差异全在白名单外：非切削/转移/stepover 60/ReferenceTool）后，43× 差主因未落
// 单键。本探针从**区域空间分布**判定加工内容：
//   dump 每区质心 xyz + 面积 → z 分层结构（同 z 多区 = 同层多区）、x/y 聚类（孔壁环 vs 全层面）、
//   面积形态（窄带小片 vs 整层大片）→ 区分"本体只加工了两孔壁轮廓（36 区 z 分布有限）" vs
//   "本体也走全层但分割粒度不同（36 区分散全 z）"→ 43× 归因落点（面级几何解析 vs 参数面）。
// 件 = CAMSIG_PRT（gt 跑 test.prt；对照跑 v2.rebuilt-20260906-000810.prt——其 OP-002 = 同参
// 产物代表，regionclone 已证与克隆逐位一致）。输出：samples\camprobe-v2regionfull-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2RegionFull
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2regionfull-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2RegionFull（OP-002 区域全 dump）==");
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
            CAMSetup cam = p.CAMSetup;
            if (cam == null) throw new Exception("无 CAMSetup");

            Operation op2 = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
            if (op2 == null) throw new Exception("找不到 OP-002");
            Log("== OP-002 存档: time=" + op2.GetToolpathTime().ToString("0.####")
                + " length=" + op2.GetToolpathLength().ToString("0.####"));

            CutRegionsData crd = op2.CutRegionsData;
            if (crd == null) { Log("CutRegionsData null"); return; }
            int n = crd.NumberRegions;
            Point3d[] cents = crd.GetCentroidPoints();
            double[] areas = crd.GetAreas();
            Log("regions=" + n + "  质心数=" + (cents == null ? "null" : cents.Length.ToString())
                + " 面积数=" + (areas == null ? "null" : areas.Length.ToString()));
            if (cents == null || areas == null) return;
            Log("== 逐区（质心 x,y,z | 面积 | z 分层注记）==");
            double prevZ = double.NaN;
            int sameZ = 0;
            for (int i = 0; i < n && i < cents.Length && i < areas.Length; i++)
            {
                double z = cents[i].Z;
                if (i > 0 && Math.Abs(z - prevZ) < 1e-6) sameZ++;
                else if (i > 0)
                {
                    Log("    -- z 变层: " + prevZ.ToString("0.####") + " 层区数=" + (sameZ + 1) + " --");
                    sameZ = 0;
                }
                prevZ = z;
                Log("  区[" + i + "] x=" + cents[i].X.ToString("0.####") + " y=" + cents[i].Y.ToString("0.####")
                    + " z=" + z.ToString("0.####") + " | 面积=" + areas[i].ToString("0.###"));
            }
            Log("    -- 末层 z=" + prevZ.ToString("0.####") + " 层区数=" + (sameZ + 1) + " --");
            // 汇总
            double zMin = double.MaxValue, zMax = double.MinValue, aMin = double.MaxValue, aMax = double.MinValue, aSum = 0;
            foreach (double a in areas) { if (a < aMin) aMin = a; if (a > aMax) aMax = a; aSum += a; }
            foreach (Point3d c in cents) { if (c.Z < zMin) zMin = c.Z; if (c.Z > zMax) zMax = c.Z; }
            Log("== 汇总: z 跨度 [" + zMin.ToString("0.####") + ", " + zMax.ToString("0.####")
                + "] 面积 [min=" + aMin.ToString("0.###") + " max=" + aMax.ToString("0.###")
                + " sum=" + aSum.ToString("0.###") + "] ==");
            Log("== 结束（只读未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
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
