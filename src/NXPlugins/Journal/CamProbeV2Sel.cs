// CamProbeV2Sel.cs — OP-003 空刀路判别实验 5：rebuilt 侧选择集内容判别（2026-09-05，
// run_journal 批处理，单件模式，内存态不 Save）
//
// 背景：gt OP-003 的 3 面（Z+ 平面 z=100）⊂ OP-001 的 13 面；gt 侧 3 面可出 3 区域刀路而
// rebuilt 侧同 3 面（签名 3/3 匹配）确定性 0 区域。参数面/DPC/写回全排除 → 剩面集内容与
// 区域形成依赖：本探针在 rebuilt 上把 OP-003 指派换成不同面集组合：
//   S1 = OP-001 的 13 面全集；S2 = OP-001 的 13 面 minus OP-003 的 3 面（10 面）；
//   S3 = 还原 OP-003 自身 3 面（对照可复现 0）。
// 判读：S1/S2 非零而 S3 零 → 零化与"该 3 面单独成区域"有关（区域形成需壁/闭包面）；全零 →
// 面集内容无关 → 定案 NX 内部/体上下文，转永久校准。
// 只读纪律：不 Save。输出：samples\camprobe-v2sel-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Sel
{
    private static string _out;
    private static Session _s;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        string prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\v2.rebuilt-20260905-191437.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2sel-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Sel（rebuilt 侧选择集内容判别）==");
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

            Operation op1 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL");
            Operation op3 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
            if (op1 == null || op3 == null) throw new Exception("找不到 OP-001/OP-003");

            TaggedObject[] f13 = GetOpFaces(op1);   // OP-001 指派面
            TaggedObject[] f3 = GetOpFaces(op3);    // OP-003 指派面
            Log("OP-001 set faces=" + (f13 == null ? "null" : f13.Length.ToString())
                + "  OP-003 set faces=" + (f3 == null ? "null" : f3.Length.ToString()));
            // 朴素差集（无 HashSet：journal 编译器缺 System.Core）
            List<TaggedObject> rest = new List<TaggedObject>();
            foreach (TaggedObject t in f13)
            {
                bool in3 = false;
                foreach (TaggedObject t3 in f3) if (t3 == t) { in3 = true; break; }
                if (!in3) rest.Add(t);
            }
            Log("S2 候选（13 minus 3）= " + rest.Count);

            Assign(op3, f3);   // 还原确认基线
            GenAndRead(op3, "S0 还原自身 3 面");

            Assign(op3, f13);
            GenAndRead(op3, "S1 = OP-001 13 面全集");
            GenAndRead(op1, "S1 对照 OP-001（未动）");

            Assign(op3, rest.ToArray());
            GenAndRead(op3, "S2 = 13 minus 3（10 面）");

            Assign(op3, f3);
            GenAndRead(op3, "S3 = 还原自身 3 面（0 可复现对照）");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    // 指派 = 默认集 SetArray（G1 实证通道），经 op builder 的 CutAreaGeometry
    private static void Assign(Operation op, TaggedObject[] faces)
    {
        CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            NXOpen.CAM.Geometry cag = b.CutAreaGeometry;
            if (cag == null || cag.GeometryList.Length == 0) throw new Exception("无默认几何集");
            NXOpen.CAM.GeometrySet gs = cag.GeometryList.FindItem(0);
            gs.Selection.SetArray(faces);
            b.Commit();
        }
        finally { b.Destroy(); }
    }

    private static TaggedObject[] GetOpFaces(Operation op)
    {
        CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            NXOpen.CAM.Geometry cag = b.CutAreaGeometry;
            if (cag == null || cag.GeometryList.Length == 0) throw new Exception("无默认几何集");
            return cag.GeometryList.FindItem(0).GetItems();
        }
        finally { b.Destroy(); }
    }

    private static void GenAndRead(Operation op, string label)
    {
        try { _cam.GenerateToolPath(new CAMObject[] { op }); }
        catch (Exception e) { Log("  [" + label + "] 生成异常: " + e.Message); return; }
        double tp = op.GetToolpathTime();
        string reg = "-";
        try
        {
            CutRegionsData crd = op.CutRegionsData;
            if (crd != null) reg = crd.NumberRegions.ToString();
        }
        catch (Exception e) { reg = "ERR " + e.Message; }
        Log("  [" + label + "] time=" + tp.ToString("0.####") + " length="
            + op.GetToolpathLength().ToString("0.####") + " regions=" + reg
            + (tp > 0 ? "  <<< 非零" : ""));
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
