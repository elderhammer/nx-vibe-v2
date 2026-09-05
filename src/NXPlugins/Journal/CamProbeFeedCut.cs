// CamProbeFeedCut.cs — feed_cut（注册表 #15）写面探针（2026-09-05，run_journal 批处理，
// 三跑纪律同 v1.5-④ params2）
//
// 目的：注册表 #15 feed_cut 标注"未测写"；v2 校准②已指认 gt feedCut 2000/500 vs rebuilt
// 默认 250 为腔刀路时间差主因（192456 时间 4 FAIL）→ 本探针判定写面持久性：
//   F1 FeedCutBuilder.Value=2000 → 持久 = feed_cut 入写面白名单候选（v1.5-⑤ 扩展键）
//                                   还原 = 负结案（时间差转引擎内在，评分规格按此定容差）
//   F2 锚点+ rpm=3000（executor [I] 已证持久族，本批健康基线；还原 = 会话写路径失效，本跑无效）
// 判据（U-6 口径）：commit → 重开（独立 builder 实例）== 写入值 → 持久。
// 纪律：写侧内存空 Part（mill_contour 模板）不保存；独立 op 零串扰；每行即时落盘。
// 输出：samples\camprobe-feedcut-<ts>.txt。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeFeedCut
{
    private static string _out;
    private static int _ok, _fail;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-feedcut-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeFeedCut（feed_cut 写面探针，注册表 #15）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            Session s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName);
            Part part = s.Parts.NewDisplay("CamProbeFeedCut", Part.Units.Millimeters);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            _cam = part.CreateCamSetup("mill_contour");
            Log("写侧环境 OK, camReady=" + s.IsCamSessionInitialized());

            NCGroup prog = TryCreateGroup(CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "FC_PROG");
            NCGroup method = TryCreateGroup(CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "FC_METHOD");
            NCGroup tool = TryCreateGroup(CAMSetup.View.MachineTool, "mill_planar", "MILL", "FC_TOOL");
            NCGroup geom = TryCreateGroup(CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "FC_GEOM");
            if (prog == null || method == null || tool == null || geom == null) { Log("组创建不全，中止"); return; }

            // F2 锚点+ 先跑：rpm（已知持久族，健康基线）
            CavityRun(prog, method, tool, geom, "FC_F2", "F2 锚点+: rpm=3000",
                (b) => { b.FeedsBuilder.SpindleRpmBuilder.Value = 3000; },
                "rpm=3000");
            // F1 目标键：feed_cut=2000（gt OP-001 实值）
            CavityRun(prog, method, tool, geom, "FC_F1", "F1 feed_cut=2000",
                (b) => { b.FeedsBuilder.FeedCutBuilder.Value = 2000; },
                "feed=2000");
            // F1b 邻接：FeedPerTooth（同 FeedsBuilder 兄弟，判别键级 vs 整 builder）
            CavityRun(prog, method, tool, geom, "FC_F1b", "F1b 邻接: FeedPerTooth=0.3",
                (b) => { b.FeedsBuilder.FeedPerToothBuilder.Value = 0.3; },
                "perTooth=0.3");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    private static void CavityRun(NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom,
        string opName, string label, Action<CavityMillingBuilder> write, params string[] expectTokens)
    {
        Log("-- " + label);
        Operation op = NewOp(prog, method, tool, geom, "mill_contour", "CAVITY_MILL", opName);
        if (op == null) return;
        string before, after;
        CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            before = Snap(b);
            Log("  写前: " + before);
            try { write(b); b.Commit(); Log("  写入+commit OK"); }
            catch (Exception e) { Log("  写入/commit 异常: " + e.Message); return; }
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try { after = Snap(b2); Log("  重开: " + after); }
        finally { b2.Destroy(); }
        Verdict(label, before, after, expectTokens);
    }

    private static string Snap(CavityMillingBuilder b)
    {
        try
        {
            return string.Format("feed={0} rpm={1} perTooth={2} feedStatus={3} rpmStatus={4}",
                b.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"),
                b.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"),
                b.FeedsBuilder.FeedPerToothBuilder.Value.ToString("0.####"),
                b.FeedsBuilder.FeedCutBuilder.InheritanceStatus,
                b.FeedsBuilder.SpindleRpmBuilder.InheritanceStatus);
        }
        catch (Exception e) { return "(快照异常: " + e.Message + ")"; }
    }

    private static void Verdict(string label, string before, string after, string[] expectTokens)
    {
        foreach (string t in expectTokens)
        {
            if (after.Contains(t))
            {
                string tag = before.Contains(t) ? "（写前已含，无判别力）" : "";
                Log("  判定 [" + label + "] " + t + " → 持久 ✓ " + tag);
            }
            else
            {
                Log("  判定 [" + label + "] " + t + " → 还原 ✗（重开不含写入值）");
            }
        }
    }

    private static Operation NewOp(NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom,
        string typeName, string subtype, string name)
    {
        try
        {
            return _cam.CAMOperationCollection.Create(prog, method, tool, geom,
                typeName, subtype, OperationCollection.UseDefaultName.False, name);
        }
        catch (Exception e) { Log("  NewOp(" + name + ") 失败: " + e.Message); return null; }
    }

    private static NCGroup TryCreateGroup(CAMSetup.View view, string typeName, string subtype, string name)
    {
        try
        {
            NCGroup root = _cam.GetRoot(view);
            if (root == null) { Log("  根组 null (view=" + view + ")"); return null; }
            NCGroupCollection g = _cam.CAMGroupCollection;
            switch (view)
            {
                case CAMSetup.View.ProgramOrder:
                    return g.CreateProgram(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
                case CAMSetup.View.MachineMethod:
                    return g.CreateMethod(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
                case CAMSetup.View.MachineTool:
                    return g.CreateTool(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
                default:
                    return g.CreateGeometry(root, typeName, subtype,
                        NCGroupCollection.UseDefaultName.False, name);
            }
        }
        catch (Exception e) { Log("  组创建 " + name + " 失败: " + e.Message); return null; }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
