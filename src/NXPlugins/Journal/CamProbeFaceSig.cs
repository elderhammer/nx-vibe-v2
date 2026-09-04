// CamProbeFaceSig.cs — 面签名采集（单件单会话模式，2026-09-05）
//
// 背景：双件同会话在批次 APP_NONE 下 AV（CamProbeV2FaceAlign 首发 033545 实证——
// 显示/工作轮换纪律为 GUI Execute 专属）→ 拆两跑、签名落盘后离线比对：
//   A 跑（件 = test.prt）：gt 腔铣 op CutAreaGeometry 13 面签名
//   B 跑（件 = v2geom-rebuild-*.prt）：body 全 26 面签名
// 判定（离线）：A 13 签名是否在 B 中唯一命中（签名 = ftype|法向轴|代表点0.01|半径）。
//
// 用法：args[0]=prt 路径（含 "test.prt" → A 跑）；输出 samples\camprobe-v2face-<ts>.txt。
// 签名纪律：UFModl.AskFaceData（camprobe-geom 实证面）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeFaceSig
{
    private static string _out;
    private const string SamplesDir = @"C:\Users\21505\Code\nx-vibe-v2\samples";

    public static void Main(string[] args)
    {
        // run_journal 不收多余参数（"more than one argument"实证）→ 件路径走 OS 环境变量 CAMSIG_PRT
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        bool gtMode = prt.ToLower().Contains("test.prt");
        _out = Path.Combine(SamplesDir, "camprobe-v2face-" + (gtMode ? "A" : "B") + "-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeFaceSig（" + (gtMode ? "A=gt 腔铣 op 面" : "B=body 全 面") + "签名）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("件: " + prt);
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            UFSession uf = UFSession.GetUFSession();
            // 顺序纪律（索引 §2.1）：先开件再 CreateCamSession——无部件时 CreateCamSession 原生 AV
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(prt, out st);
            s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            Log("打开 OK: " + p.Name + "  camSession=" + s.IsCamSessionInitialized());

            List<Face> faces = new List<Face>();
            if (gtMode)
            {
                CAMSetup cam = p.CAMSetup;
                Operation cav = FindCavityOp(cam, cam.GetRoot(CAMSetup.View.ProgramOrder));
                if (cav == null) throw new Exception("未找到腔 op");
                Log("腔 op: " + cav.Name);
                CavityMillingBuilder cb = cam.CAMOperationCollection.CreateCavityMillingBuilder(cav);
                try
                {
                    NXOpen.CAM.Geometry cag = cb.CutAreaGeometry;
                    for (int i = 0; i < cag.GeometryList.Length; i++)
                    {
                        NXOpen.CAM.GeometrySet gs = cag.GeometryList.FindItem(i);
                        foreach (TaggedObject it in gs.GetItems())
                        {
                            Face fc = it as Face;
                            if (fc != null) faces.Add(fc);
                        }
                    }
                }
                finally { cb.Destroy(); }
            }
            else
            {
                // 重开会话过滤行为诊断：不筛 IsBlanked/IsSolidBody，逐个 body 直读并打印状态
                foreach (Body b in p.Bodies.ToArray())
                {
                    Log("  body: " + b.GetType().Name + " IsBlanked=" + b.IsBlanked
                        + " IsSolidBody=" + b.IsSolidBody + " 层=" + b.Layer);
                    foreach (Face fc in b.GetFaces()) faces.Add(fc);
                }
            }
            Log("面数=" + faces.Count);
            foreach (Face fc in faces)
                Log("SIG " + FaceSig(uf, fc));
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static Operation FindCavityOp(CAMSetup cam, NCGroup node)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.GetNameOfType().ToLower().Contains("cavity")) return op;
            NCGroup sub = m as NCGroup;
            if (sub != null)
            {
                Operation found = FindCavityOp(cam, sub);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static string FaceSig(UFSession uf, Face fc)
    {
        try
        {
            double[] pt = new double[3], dir = new double[3], box = new double[6];
            int ftype; double radius = 0, radData = 0; int normDir;
            uf.Modl.AskFaceData(fc.Tag, out ftype, pt, dir, box, out radius, out radData, out normDir);
            string nq = "";
            double ax = Math.Abs(dir[0]), ay = Math.Abs(dir[1]), az = Math.Abs(dir[2]);
            if (ax >= ay && ax >= az) nq = (dir[0] >= 0 ? "X+" : "X-");
            else if (ay >= ax && ay >= az) nq = (dir[1] >= 0 ? "Y+" : "Y-");
            else nq = (dir[2] >= 0 ? "Z+" : "Z-");
            string px = Math.Round(pt[0] / 0.01) + "," + Math.Round(pt[1] / 0.01) + "," + Math.Round(pt[2] / 0.01);
            return ftype + "|" + nq + "|" + px + "|r" + Math.Round(radius, 3);
        }
        catch { return "ERR"; }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
