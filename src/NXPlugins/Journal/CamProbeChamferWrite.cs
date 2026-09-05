// CamProbeChamferWrite.cs — CHAMFER_MILL 刀具参数写序列探针（2026-09-05，run_journal 批处理）
//
// 背景：I-2 复跑（202730）暴露新缺陷——(mill_planar, CHAMFER_MILL) 重建的 T-004 写直径 6 被拒：
// "倒斜角或拐角半径不能与刀具的中心线交叉"，回读模板默认 16（→ comparer 刀具直径 FAIL 新增）。
// 本探针：建独立 CHAMFER_MILL 刀，① 反射 dump Tl*/Chamfer/Rad 参数面当前值；
// ② 独立刀试写序列（commit→新 builder 读回判据）：
//   s1 直径=6 单独（复现预期异常）；s2 LowCorRad=0 → 直径=6；s3（据 ① 面补）候选倒角相关参数
//   置 0/角 180 等 → 直径=6。命中 → executor 刀具写块按对 subtype 加预写（修复依据）。
// 纪律：内存空 Part 不保存。输出：samples\camprobe-chamferwrite-<ts>.txt。

using System;
using System.IO;
using System.Reflection;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;

public class CamProbeChamferWrite
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-chamferwrite-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeChamferWrite（CHAMFER_MILL 参数写序列）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            Part part = s.Parts.NewDisplay("CamProbeChamferWrite", Part.Units.Millimeters);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            _cam = part.CreateCamSetup("mill_contour");
            Log("写侧环境 OK");
            NCGroup root = _cam.GetRoot(CAMSetup.View.MachineTool);

            // ① 参数面 dump（默认 CHAMFER_MILL 刀）
            NCGroup g0 = _cam.CAMGroupCollection.CreateTool(root, "mill_planar", "CHAMFER_MILL",
                NCGroupCollection.UseDefaultName.False, "CW_DUMP");
            try
            {
                MillingToolBuilder mb = _cam.CAMGroupCollection.CreateMillToolBuilder(g0) as MillingToolBuilder;
                try { DumpSurf(mb); }
                finally { mb.Destroy(); }
            }
            finally { Log("（dump 刀未改，弃）"); }

            // ② 试写序列（每序列独立新刀）
            Seq("s1", root, b => b.TlDiameterBuilder.Value = 6.0, "直径=6 单独（预期复现异常）");
            Seq("s2", root, b => { b.TlLowCorRadBuilder.Value = 0.0; b.TlDiameterBuilder.Value = 6.0; },
                "LowCorRad=0 → 直径=6");
            Seq("s3", root, b => { b.TlLowCorRadBuilder.Value = 0.0; b.TlFluteLnBuilder.Value = 20.0; b.TlDiameterBuilder.Value = 6.0; },
                "LowCorRad=0+FluteLn=20 → 直径=6");
            SeqR("s4", root, b => { SetChamferLen(b, 3.0); b.TlDiameterBuilder.Value = 6.0; },
                "ChamferLength=3(90°尖角=D/2，反射写) → 直径=6");
            SeqR("s5", root, b => { SetChamferLen(b, 2.0); b.TlDiameterBuilder.Value = 6.0; },
                "ChamferLength=2（反射写） → 直径=6（< D/2 判别 apex 触碰是否允许）");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static void DumpSurf(MillingToolBuilder mb)
    {
        Log("-- CHAMFER_MILL 默认参数面（Tl*/Chamfer/Rad/Angle）--");
        Type t = mb.GetType();
        foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            string pn = pi.Name;
            if (pn.IndexOf("Tl", StringComparison.Ordinal) != 0
                && pn.IndexOf("Chamfer", StringComparison.OrdinalIgnoreCase) < 0
                && pn.IndexOf("Rad", StringComparison.OrdinalIgnoreCase) < 0
                && pn.IndexOf("Angle", StringComparison.OrdinalIgnoreCase) < 0
                && pn.IndexOf("Diameter", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (pn.Contains("Handle") || pn == "Tag") continue;
            try
            {
                object v = pi.GetValue(mb, null);
                if (v == null) { Log("  ~ " + pn + " = null"); continue; }
                Type vt = v.GetType();
                if (vt.IsEnum || vt == typeof(string) || vt == typeof(double) || vt == typeof(bool)
                    || vt == typeof(int) || vt.IsPrimitive)
                    Log("  ~ " + pn + " = " + (v is double ? ((double)v).ToString("0.####") : v.ToString()));
                else if (v is NXOpen.TaggedObject)
                {
                    // builder 叶子（如 TlDiameterBuilder）：递归一层找 Value
                    string sub = "";
                    foreach (PropertyInfo p2 in v.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        if (p2.Name == "Value")
                        {
                            try
                            {
                                object vv = p2.GetValue(v, null);
                                sub = " .Value=" + (vv is double ? ((double)vv).ToString("0.####") : Convert.ToString(vv));
                            }
                            catch (Exception e) { sub = " .Value ERR " + e.Message; }
                        }
                    Log("  ~ " + pn + " = [" + vt.Name + "]" + sub);
                }
                else Log("  ~ " + pn + " = [" + vt.Name + "]");
            }
            catch (Exception e) { Log("  ~ " + pn + " = !!ERR " + e.GetType().Name); }
        }
    }

    // 反射写 ChamferLength（ChamferLengthBuilder 不在 MillingToolBuilder 编译期公开面）
    private static void SetChamferLen(MillingToolBuilder mb, double v)
    {
        System.Reflection.PropertyInfo pi = mb.GetType().GetProperty("ChamferLengthBuilder");
        if (pi == null) throw new Exception("运行时无 ChamferLengthBuilder 属性");
        object leaf = pi.GetValue(mb, null);
        System.Reflection.PropertyInfo pv = leaf.GetType().GetProperty("Value");
        pv.SetValue(leaf, v, null);
    }

    // 反射写序列（与 Seq 同判据；ChamferLength 写走反射）
    private static void SeqR(string id, NCGroup root, Action<MillingToolBuilder> write, string desc)
    {
        Log("");
        Log("-- " + id + "：" + desc + " --");
        NCGroup g = null;
        try
        {
            g = _cam.CAMGroupCollection.CreateTool(root, "mill_planar", "CHAMFER_MILL",
                NCGroupCollection.UseDefaultName.False, "CW_" + id);
            MillingToolBuilder mb = _cam.CAMGroupCollection.CreateMillToolBuilder(g) as MillingToolBuilder;
            try
            {
                try { write(mb); mb.Commit(); Log("  写入+commit OK"); }
                catch (Exception e) { Log("  写入/commit 异常: " + e.Message); return; }
            }
            finally { mb.Destroy(); }
            MillingToolBuilder mb2 = _cam.CAMGroupCollection.CreateMillToolBuilder(g) as MillingToolBuilder;
            try
            {
                Log("  重开: 直径=" + mb2.TlDiameterBuilder.Value.ToString("0.####")
                    + " ChamferLen(反射)=" + ReadChamferLen(mb2).ToString("0.####"));
            }
            finally { mb2.Destroy(); }
            NXOpen.CAM.Tool t = g as NXOpen.CAM.Tool;
            Tool.Types ty; Tool.Subtypes sty;
            t.GetTypeAndSubtype(out ty, out sty);
            Log("  类型读回: " + ty + "|" + sty);
        }
        catch (Exception e)
        {
            Log("  !! 建刀异常: " + e.Message);
        }
    }

    private static double ReadChamferLen(MillingToolBuilder mb)
    {
        System.Reflection.PropertyInfo pi = mb.GetType().GetProperty("ChamferLengthBuilder");
        object leaf = pi.GetValue(mb, null);
        System.Reflection.PropertyInfo pv = leaf.GetType().GetProperty("Value");
        return (double)pv.GetValue(leaf, null);
    }

    private static void Seq(string id, NCGroup root, Action<MillingToolBuilder> write, string desc)
    {
        Log("");
        Log("-- " + id + "：" + desc + " --");
        NCGroup g = null;
        try
        {
            g = _cam.CAMGroupCollection.CreateTool(root, "mill_planar", "CHAMFER_MILL",
                NCGroupCollection.UseDefaultName.False, "CW_" + id);
            MillingToolBuilder mb = _cam.CAMGroupCollection.CreateMillToolBuilder(g) as MillingToolBuilder;
            try
            {
                try { write(mb); mb.Commit(); Log("  写入+commit OK"); }
                catch (Exception e) { Log("  写入/commit 异常: " + e.Message); return; }
            }
            finally { mb.Destroy(); }
            MillingToolBuilder mb2 = _cam.CAMGroupCollection.CreateMillToolBuilder(g) as MillingToolBuilder;
            try
            {
                Log("  重开: 直径=" + mb2.TlDiameterBuilder.Value.ToString("0.####")
                    + " LowCorRad=" + mb2.TlLowCorRadBuilder.Value.ToString("0.####")
                    + " FluteLn=" + mb2.TlFluteLnBuilder.Value.ToString("0.####"));
            }
            finally { mb2.Destroy(); }
            NXOpen.CAM.Tool t = g as NXOpen.CAM.Tool;
            Tool.Types ty; Tool.Subtypes sty;
            t.GetTypeAndSubtype(out ty, out sty);
            Log("  类型读回: " + ty + "|" + sty);
        }
        catch (Exception e)
        {
            Log("  !! 建刀异常: " + e.Message);
        }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
