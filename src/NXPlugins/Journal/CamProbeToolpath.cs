// CamProbeToolpath.cs — 步骤 0 实证件 #3（B 段重跑）：test.prt 刀路生成/回读
// 取件纪律（2026-09-03 实测）：空会话只能用 OpenDisplay + SetWork；文件若已被会话打开，
// Open/OpenDisplay 均报"文件已存在"且空会话无法把仅装载部件提升为显示/工作部件
// → 探针须在干净会话运行（本文件已按此最终口径实现）。
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。内存生成刀路，不保存。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camprobe-toolpath.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeToolpath
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-toolpath.txt";
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static readonly List<string> _lines = new List<string>();

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        try
        {
            _lines.Add("== CamProbeToolpath ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Session theSession = Session.GetSession();
            var parts = theSession.Parts;

            // ---- 取 test.prt：空会话下唯一可用路径 = OpenDisplay（2026-09-03 实证：
            //      空会话无法把仅装载部件提升为显示/工作部件；已打开时 Open/OpenDisplay 均报
            //      "文件已存在"）----
            Part part = null;
            try
            {
                PartLoadStatus ls;
                part = parts.OpenDisplay(TestPart, out ls);
                _lines.Add("OpenDisplay 成功");
            }
            catch (Exception e)
            {
                _lines.Add("OpenDisplay 失败: " + e.Message);
                _lines.Add("提示：先关闭会话中已打开的 test.prt（或重开新 NX 会话）再运行本探针。");
                Finish(outPath);
                return;
            }
            parts.SetWork(part);
            CAMSetup cam = part.CAMSetup;
            if (cam == null) { Note("test.prt 无 CAMSetup"); Finish(outPath); return; }

            foreach (string opName in new[] { "CAVITY_MILL", "打点_COPY_COPY_COPY" })
            {
                Operation op = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), opName);
                if (op == null) { Note("未找到操作: " + opName); continue; }
                Note("-- " + opName + " (GetNameOfType=" + NameOfType(op) + ")");
                R("  GenerateToolPath", () => { cam.GenerateToolPath(new CAMObject[] { op }); return "ok"; });
                R("  GetToolpathTime (min)", () => op.GetToolpathTime().ToString("0.####"));
                R("  GetToolpathLength (mm)", () => op.GetToolpathLength().ToString("0.####"));
            }
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    private static Operation FindOp(NCGroup group, string name)
    {
        try
        {
            foreach (CAMObject m in group.GetMembers())
            {
                if (m is Operation && m.Name == name) return (Operation)m;
                if (m is NCGroup)
                {
                    Operation hit = FindOp((NCGroup)m, name);
                    if (hit != null) return hit;
                }
            }
        }
        catch (Exception e) { _lines.Add("  FindOp(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static string NameOfType(CAMObject o)
    {
        try { return o.GetNameOfType(); }
        catch (Exception e) { return "(异常: " + e.Message + ")"; }
    }

    private static void Note(string s) { _lines.Add("  " + s); }

    private static void R(string label, Func<string> f)
    {
        try { _lines.Add("  " + label + " = " + f()); }
        catch (Exception e) { _lines.Add("  " + label + " 异常: " + e.Message); }
    }

    private static void Finish(string outPath)
    {
        try { File.WriteAllLines(outPath, _lines.ToArray()); }
        catch (Exception ex)
        {
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-toolpath-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
