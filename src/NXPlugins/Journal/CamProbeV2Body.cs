// CamProbeV2Body.cs — OP-003 空刀路判别实验 6：体/面几何保真核对（2026-09-05，
// run_journal 批处理，单件模式，gt / rebuilt 各一跑）
//
// 背景：OP-003 3 面（平面 Z+ z=100）在 gt 原生件上单独成 cut-area = 3 区域出刀路，在
// rebuilt（STEP 回导件）上确定性 0 区域；同件加壁面（10/13 面）即正常。判别体保真：
//   ① body 体积/包围盒（U-5 正对照：body 级 mass props 可行）；
//   ② 两件上同 3 面的拓扑（边数、顶点）——签名通道（型/法向/代表点/半径）不覆盖
//      loop/拓扑分裂差异，若 rebuilt 匹配面边数不同 → STEP 回导拓扑分裂/合并差异
//      = 区域形成的体上下文根因（非参数/复刻缺口）。
// 只读纪律：不 Save。用法：CAMSIG_BODY_PRT + CAMSIG_BODY_LABEL（gt|reb）。
// 输出：samples\camprobe-v2body-<label>-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Body
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_BODY_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        string label = System.Environment.GetEnvironmentVariable("CAMSIG_BODY_LABEL");
        if (string.IsNullOrEmpty(label)) label = "gt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2body-" + label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Body（体/面几何保真核对）==");
        Log("label=" + label + "  件: " + prt);
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

            // ① body 体积/bbox（签名沿 CamProbeFinalize S6 实证：acc[0]=精度，mass[0]=面积 mass[1]=体积）
            foreach (Body b in p.Bodies.ToArray())
            {
                if (!b.IsSolidBody || b.IsBlanked) continue;
                double[] acc = new double[11];
                acc[0] = 0.001;
                double[] mass = new double[47];
                double[] stat = new double[13];
                try
                {
                    uf.Modl.AskMassProps3d(new Tag[] { b.Tag }, 1, 1, 4, 0.0, 1, acc, mass, stat);
                    Log("body area=" + mass[0].ToString("0.####")
                        + " vol=" + mass[1].ToString("0.####")
                        + " COF=(" + mass[2].ToString("0.####") + "," + mass[3].ToString("0.####") + ","
                        + mass[4].ToString("0.####") + ")");
                }
                catch (Exception e) { Log("body mass props 异常: " + e.Message); }
                Log("body faces=" + b.GetFaces().Length + " edges=" + b.GetEdges().Length);
                break;
            }

            // ② OP-003 的 3 面（按签名 rep 点定位 z=100 平面）边数
            CAMSetup cam = p.CAMSetup;
            if (cam != null)
            {
                Operation op3 = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
                if (op3 != null)
                {
                    CavityMillingBuilder b3 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op3);
                    try
                    {
                        NXOpen.CAM.Geometry cag = b3.CutAreaGeometry;
                        if (cag != null && cag.GeometryList.Length > 0)
                        {
                            TaggedObject[] items = cag.GeometryList.FindItem(0).GetItems();
                            Log("OP-003 面数=" + items.Length);
                            foreach (TaggedObject it in items)
                            {
                                Face fc = it as Face;
                                if (fc == null) { Log("  !! 非 Face item"); continue; }
                                int nE = fc.GetEdges().Length;
                                double[] box = new double[6];
                                double[] pt = new double[3], dir = new double[3];
                                double radius, radData;
                                int ftypeI, normDir;
                                // 签名沿 CamProbeGeom.DescribeFace 实证（radData 标量、normDir int）
                                uf.Modl.AskFaceData(fc.Tag, out ftypeI, pt, dir, box, out radius, out radData, out normDir);
                                Log("  face edges=" + nE + " ftype=" + ftypeI
                                    + " rep=(" + pt[0].ToString("0.####") + "," + pt[1].ToString("0.####") + ","
                                    + pt[2].ToString("0.####") + ") r=" + radius.ToString("0.###"));
                            }
                        }
                        else Log("OP-003 无几何集");
                    }
                    finally { b3.Destroy(); }
                }
                else Log("找不到 OP-003");
            }
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
