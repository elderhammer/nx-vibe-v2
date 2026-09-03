// SmokeOpen.cs — 打开通道矩阵探针（排查 OpenDisplay 943006 语义）
// 依次尝试：Open / OpenDisplay / OpenActiveDisplay（同一文件）；再对照开 NX 自带模板部件。
// 每步独立 try 并即时落盘（samples\smoke-open-<ts>.txt），含异常完整 ToString。

using System;
using System.IO;
using NXOpen;
using Path = System.IO.Path;

public class SmokeOpen
{
    private static string _out;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "smoke-open-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== SmokeOpen ==");
        try
        {
            Session s = Session.GetSession();
            Log("IsCamSessionInitialized=" + s.IsCamSessionInitialized());
            Log("ApplicationName=" + s.ApplicationName);
            var parts = s.Parts;
            Log("Work=" + (parts.Work == null ? "(null)" : parts.Work.Name));
            Log("DisplayedCount=" + parts.GetDisplayedParts().Length);

            string f = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
            Try("Open", delegate { PartLoadStatus ls; Part p = parts.Open(f, out ls); return p.Name; });
            Try("OpenDisplay", delegate { PartLoadStatus ls; Part p = parts.OpenDisplay(f, out ls); return p.Name; });
            Try("OpenActiveDisplay", delegate
            {
                PartLoadStatus ls;
                BasePart p = parts.OpenActiveDisplay(f, NXOpen.DisplayPartOption.AllowAdditional, out ls);
                return p.Name;
            });

            string tpl = @"C:\Program Files\Siemens\NX2406\mach\resource\template_part\metric\mill_contour.prt";
            Try("OpenDisplay(template mill_contour)", delegate { PartLoadStatus ls; Part p = parts.OpenDisplay(tpl, out ls); return p.Name; });
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex);
        }
        Log("== 结束 ==");
    }

    private static void Try(string label, Func<string> act)
    {
        try { Log("OK   " + label + " -> " + act()); }
        catch (Exception ex) { Log("FAIL " + label + " : " + ex.GetType().FullName + " " + ex.Message); }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
