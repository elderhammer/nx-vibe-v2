// CamProbeChamfer.cs — MillChamfer 注册对扫描探针（2026-09-05，run_journal 批处理）
//
// 目的（tool#4 校准条目，U-7 spec §5b 预留路径）：T-004 中心钻 (Mill,MillChamfer) 现重建为
// 默认铣 (mill_planar,MILL)→Mill5（TOOL_TYPE_INFERRED + comparer tool#4 1 FAIL，192456 残余）。
// 若存在创建注册对 (typeName, subtype) 其默认刀具读回 (Mill, **MillChamfer**) → 重建表加行，
// diag 消除、192456 残余 -1。
// 方法：枚举 Session.CAMSession.GetTemplateTypes() × GetTemplateSubtypes(type, Tool)，
//   逐对 CreateTool（CAMSetup 默认根）→ as Tool → GetTypeAndSubtype → 统计默认读回；
//   重点找 (Mill,MillChamfer)；锚点对 (mill_planar,MILL)→(Mill,Mill5)、(hole_making,STD_DRILL)
//   →(Drill,DrillStandard) 应命中（U-7 P2 回归）。
// 纪律：内存空 Part 不保存。输出：samples\camprobe-chamfer-<ts>.txt。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;

public class CamProbeChamfer
{
    private static string _out;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-chamfer-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeChamfer（MillChamfer 注册对扫描）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            Part part = s.Parts.NewDisplay("CamProbeChamfer", Part.Units.Millimeters);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            _cam = part.CreateCamSetup("mill_contour");
            CAMSession cs = s.CAMSession;
            string[] types = cs.GetTemplateTypes();
            Log("模板部件数=" + types.Length);
            NCGroup root = _cam.GetRoot(CAMSetup.View.MachineTool);

            var seen = new System.Collections.Generic.Dictionary<string, string>();
            string match = null;
            int created = 0, fail = 0;
            foreach (string tn in types)
            {
                string[] subs = cs.GetTemplateSubtypes(tn, CAMSession.ObjectSubtype.Tool);
                foreach (string st in subs)
                {
                    string pair = tn + "/" + st;
                    if (seen.ContainsKey(pair)) continue;
                    seen[pair] = "?";
                    string name = "SCAN_" + (created++);
                    string readback = null;
                    try
                    {
                        NCGroup g = _cam.CAMGroupCollection.CreateTool(root, tn, st,
                            NCGroupCollection.UseDefaultName.True, name);
                        NXOpen.CAM.Tool t = g as NXOpen.CAM.Tool;
                        Tool.Types ty;
                        Tool.Subtypes sty;
                        t.GetTypeAndSubtype(out ty, out sty);
                        readback = ty + "|" + sty;
                        seen[pair] = readback;
                        if (ty == Tool.Types.Mill && sty == Tool.Subtypes.MillChamfer)
                        {
                            Log("!! MATCH (Mill,MillChamfer) <= " + pair);
                            if (match == null) match = pair;
                        }
                    }
                    catch (Exception e) { fail++; readback = "FAIL " + e.Message; seen[pair] = readback; }
                    if (created % 20 == 0 || readback != null && (readback.Contains("Chamfer") || readback.Contains("Drill")))
                        Log("  [" + created + "] " + pair + " → " + readback);
                    if (created > 800) break;
                }
                if (created > 800) break;
            }
            Log("");
            Log("创建=" + created + " 失败=" + fail);
            // 汇总：列出含 Chamfer/Mill5/DrillStandard 读回的所有对
            Log("== 含 Chamfer/锚点读回的注册对 ==");
            foreach (var kv in seen)
                if (kv.Value != null && (kv.Value.Contains("Chamfer") || kv.Value.Contains("Mill5")
                    || kv.Value.Contains("DrillStandard")))
                    Log("  " + kv.Key + " → " + kv.Value);
            Log("== 结论: (Mill,MillChamfer) 命中=" + (match == null ? "无" : match) + " ==");
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
