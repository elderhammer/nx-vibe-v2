// CamProbeV2ApiClone.cs — OP-003 空刀路判别实验 7：gt 件 API 新建同 3 面 op（2026-09-05，
// run_journal 批处理，单件模式，内存态不 Save）
//
// 背景：体保真核对（camprobe-v2body）——gt 与 rebuilt 的 body 全同（area/vol/COF/26 面/68 边），
// OP-003 的 3 面（4 边平面 Z+ z=100）两侧一致；参数面/DPC/写回/面集判别全排除。剩判别轴 =
// gt OP-003 为工程师复制件（带 API 不可见内部状态？）vs API 新建 op：
//   在 gt 件上以 executor 同款 API（Create + CutAreaGeometry set0 SetArray 3 面 + 默认参数）
//   新建 APICLONE op → 生成：非零（= API 新建即可出 → rebuilt 差在体上下文/其他）vs
//   零（= 与 gt OP-003 同 3 面但 API 建 → 零：复制件内部状态差异 → executor 不可复刻 → 永久校准）。
// 只读纪律：gt 件不 Save（内存态新增 op，退出即弃）。输出：samples\camprobe-v2apiclone-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2ApiClone
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_CLONE_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2apiclone-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2ApiClone（同 3 面 API 新建 op 判别）==");
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

            Operation op3 = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY_COPY");
            if (op3 == null) throw new Exception("找不到 OP-003");
            // 取 OP-003 的 3 面
            TaggedObject[] f3 = null;
            CavityMillingBuilder bg = cam.CAMOperationCollection.CreateCavityMillingBuilder(op3);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                f3 = cag.GeometryList.FindItem(0).GetItems();
            }
            finally { bg.Destroy(); }
            Log("OP-003 面数=" + f3.Length);

            // 四父锚点 = OP-003 自身的父链（与 gt OP-003 完全同上下文）
            NCGroup prog = op3.ParentProgramOrder;
            NCGroup method = op3.ParentMachineMethod;
            NCGroup tool = op3.ParentMachineTool;
            NCGroup geom = op3.ParentGeometry;
            Log("锚点: prog=" + (prog != null ? prog.Name : "null") + " method="
                + (method != null ? method.Name : "null") + " tool="
                + (tool != null ? tool.Name : "null") + " geom=" + (geom != null ? geom.Name : "null"));
            if (prog == null || method == null || tool == null || geom == null)
                throw new Exception("锚点缺失，中止");

            Operation neu = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, "APICLONE_OP003");
            Log("APICLONE 创建 OK: " + neu.Name);

            // 指派 OP-003 的 3 面到默认集（executor 同款 G1 通道）
            CavityMillingBuilder bn = cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                NXOpen.CAM.Geometry cag = bn.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("新 op 无默认几何集");
                NXOpen.CAM.GeometrySet gs = cag.GeometryList.FindItem(0);
                gs.Selection.SetArray(f3);
                bn.Commit();
            }
            finally { bn.Destroy(); }
            Log("面指派 3 → ok");

            cam.GenerateToolPath(new CAMObject[] { neu });
            double tp = neu.GetToolpathTime();
            double tl = neu.GetToolpathLength();
            string reg = "-";
            try
            {
                CutRegionsData crd = neu.CutRegionsData;
                if (crd != null) reg = crd.NumberRegions.ToString();
            }
            catch (Exception e) { reg = "ERR " + e.Message; }
            Log("== APICLONE(默认参数+3 面) time=" + tp.ToString("0.####") + " length=" + tl.ToString("0.####")
                + " regions=" + reg + (tp > 0 ? "  <<< 非零" : ""));
            Log("== 对照 gt OP-003（工程师复制件，同 3 面） ===");
            cam.GenerateToolPath(new CAMObject[] { op3 });
            CutRegionsData crd3 = op3.CutRegionsData;
            Log("  OP-003 time=" + op3.GetToolpathTime().ToString("0.####") + " length="
                + op3.GetToolpathLength().ToString("0.####")
                + " regions=" + (crd3 == null ? "null" : crd3.NumberRegions.ToString()));
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static NCGroup FindGroup(NCGroup node, string name)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            NCGroup g = m as NCGroup;
            if (g != null)
            {
                if (name == null || g.Name == name) return g;
                NCGroup hit = FindGroup(g, name);
                if (hit != null) return hit;
            }
        }
        return null;
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
