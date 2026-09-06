// CamProbeV2Dims.cs — 尺寸核实终证探针（2026-09-06，run_journal 批处理，单件只读）
//
// 背景：用户观感"rebuilt 之后尺寸好像变大了"。已有证据（v2body 体级 area/vol/COF 逐位全同、
// 拓扑 26 面/68 边同、F1 签名跨件 13/13 唯一命中含半径 0.001 取整）已指向"几何未变、变化的是
// 加工范围"（regionfull：OP-002 reb 切 23.3mm 全程 vs gt 只切 3.3mm 底段 = 43× 刀路视觉）。
// 本探针 = 直观读数终证：双档各跑，**全部 26 面**逐面 AskFaceData（类型/半径/代表点/面包围盒，
// 0.001mm 输出）+ 全域包围盒（26 面 box 并集），按代表点排序消除面序噪声 → 离线逐行 diff。
// 若排序后逐行全等 → 几何尺寸逐特征 0.001mm 级未变（终证）；若有差 → 定位到具体面与量级。
// 只读纪律：不 Save。件 = CAMSIG_PRT（gt 跑 test.prt；对照跑 v2.rebuilt-20260906-003620.prt）。
// 输出：samples\camprobe-v2dims-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.UF;
using Path = System.IO.Path;

public class CamProbeV2Dims
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2dims-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Dims（全 26 面几何读数）==");
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
            Log("打开 OK: " + p.Name);

            Body body = null;
            foreach (Body b in p.Bodies.ToArray())
                if (b.IsSolidBody && !b.IsBlanked) { body = b; break; }
            if (body == null) { Log("无 solid body"); return; }
            Log("body: faces=" + body.GetFaces().Length + " edges=" + body.GetEdges().Length);

            var rows = new List<KeyValuePair<string, string>>();   // (排序键, 行)
            double gx0 = double.MaxValue, gy0 = double.MaxValue, gz0 = double.MaxValue;
            double gx1 = double.MinValue, gy1 = double.MinValue, gz1 = double.MinValue;
            int idx = 0;
            foreach (Face fc in body.GetFaces())
            {
                double[] pt = new double[3], dir = new double[3], box = new double[6];
                int ftype; double radius = 0, radData = 0; int normDir;
                try { uf.Modl.AskFaceData(fc.Tag, out ftype, pt, dir, box, out radius, out radData, out normDir); }
                catch (Exception e) { Log("  face[" + idx + "] AskFaceData 异常: " + e.Message); idx++; continue; }
                // 全域包围盒（面 box 并集 = 体范围 0.001 级）
                gx0 = Math.Min(gx0, box[0]); gy0 = Math.Min(gy0, box[1]); gz0 = Math.Min(gz0, box[2]);
                gx1 = Math.Max(gx1, box[3]); gy1 = Math.Max(gy1, box[4]); gz1 = Math.Max(gz1, box[5]);
                string key = (pt[0] * 1000).ToString("000000000") + "|" + (pt[1] * 1000).ToString("000000000")
                    + "|" + (pt[2] * 1000).ToString("000000000");
                string row = "f=" + ftype + " r=" + radius.ToString("0.0000")
                    + " p=(" + pt[0].ToString("0.0000") + "," + pt[1].ToString("0.0000") + "," + pt[2].ToString("0.0000") + ")"
                    + " box=(" + box[0].ToString("0.0000") + "," + box[1].ToString("0.0000") + "," + box[2].ToString("0.0000")
                    + ")-(" + box[3].ToString("0.0000") + "," + box[4].ToString("0.0000") + "," + box[5].ToString("0.0000") + ")";
                rows.Add(new KeyValuePair<string, string>(key, row));
                idx++;
            }
            rows.Sort((x, y) => x.Key.CompareTo(y.Key));
            Log("== 逐面（按代表点排序）==");
            foreach (KeyValuePair<string, string> kv in rows) Log("  " + kv.Value);
            Log("== 全域包围盒 ==");
            Log("  X[" + gx0.ToString("0.0000") + ", " + gx1.ToString("0.0000") + "] 宽=" + (gx1 - gx0).ToString("0.0000"));
            Log("  Y[" + gy0.ToString("0.0000") + ", " + gy1.ToString("0.0000") + "] 宽=" + (gy1 - gy0).ToString("0.0000"));
            Log("  Z[" + gz0.ToString("0.0000") + ", " + gz1.ToString("0.0000") + "] 高=" + (gz1 - gz0).ToString("0.0000"));
            Log("== 结束（只读未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
