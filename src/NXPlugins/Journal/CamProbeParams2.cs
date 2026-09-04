// CamProbeParams2.cs — v1.5-④ 收口复跑探针（c5/c6 负结论确认 + 邻接面判别，2026-09-04）
//
// 目的（2026-09-04 收口批；结论定稿已落 docs/nx-param-registry-spec.md §2 #5-8 与 §5 D-7/D-8；
//   上游首跑 = samples/camprobe-params-20260904-155341.txt S3）：
//   v1 S3 判定行是雷同通用文案，须按重开值逐键人工判读；且 c5（MultiDepthCut.Toggle bool）
//   c6（BoundaryInTol 直 double）写后还原只跑一次，未正式结案（U-6 纪律 = 多跑 + 结案回填）。
//   本批：带自动判定的复跑（重开 == 写入值 → 逐键打 持久/还原），并加邻接面判别实验：
//     E3  MultiDepthCut.StepMethod（嵌套枚举）+ Toggle 同写 —— 判别"toggle 键级死区" vs
//         "MultiDepthCut 整对象丢弃"（U-6 stepover 复合对象丢弃同款）；
//     E5  BoundaryOutTol（同族兄弟直 double）—— 判别"Boundary 容差族死区" vs "InTol 键级"；
//     E6  BoundaryInTol 在 PLANAR_MILL（mill_planar 模板）—— 排除 CAVITY_MILL 模板特化。
//   阳性锚点：E1 cut_pattern=Zig、E7 finish_passes=2（v1 已证持久——每跑健康基线，
//   若锚点还原 = 会话写路径整体失效，本跑无效重跑）。
// 判据（U-6 口径）：重开（独立 builder 实例）== 写入值 → 持久；还原模板默认 → 不可写。
// 成员面实证（写码前已三路核实）：MultiDepthCut{Toggle:bool, StepMethod:MultiDepthCut.Types{Increment,Passes}}
//   （NXOpen.xml P:/F: + CAM_MultiDepthCut.hxx:57/70/83）；MillCutParameters.BoundaryInTol/OutTol（XML P:）。
// 纪律：写侧内存空 Part（mill_contour 模板）不保存；每实验独立 op 零串扰；每行即时落盘。
// 输出：samples\camprobe-params2-<ts>.txt（args[0] 可覆盖）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeParams2
{
    private static string _out;
    private static int _ok, _fail;
    private static CAMSetup _cam;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-params2-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeParams2（v1.5-④ 收口复跑：c5/c6 负结案确认 + 邻接判别）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        try
        {
            s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName
                + "  IsCamSessionInitialized=" + s.IsCamSessionInitialized());

            Step("S1 写侧环境（建件→CAM 会话→CreateCamSetup）", () =>
            {
                Part part = s.Parts.NewDisplay("CamProbeParams2", Part.Units.Millimeters);
                if (!s.IsCamSessionInitialized()) s.CreateCamSession();
                _cam = part.CreateCamSetup("mill_contour");
                Log("  写侧环境 OK, camReady=" + s.IsCamSessionInitialized());
            });

            Step("S2 复跑与邻接判别实验（逐键自动判定）", () => SectionRerun());
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ================= S2：复跑实验（每实验独立 op，commit→重开自动判定） =================
    private static void SectionRerun()
    {
        NCGroup prog = TryCreateGroup(CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "P2_PROG");
        NCGroup method = TryCreateGroup(CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "P2_METHOD");
        NCGroup tool = TryCreateGroup(CAMSetup.View.MachineTool, "mill_planar", "MILL", "P2_TOOL");
        NCGroup geom = TryCreateGroup(CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "P2_GEOM");
        if (prog == null || method == null || tool == null || geom == null) { Note("组创建不全，S2 中止"); return; }

        // E1 阳性锚点（v1 c1）：cut_pattern=Zig（.Type 类嵌套枚举）→ 期望持久
        CavityRun(prog, method, tool, geom, "PM2_E1", "E1 锚点+: cut_pattern=Zig",
            (b) => { b.CutPattern.CutPattern = CutPatternBuilder.Types.Zig; },
            "pattern=Zig");
        // E2 复刻 v1 c5：MultiDepthCut.Toggle=true（bool 直赋）→ 期望还原（负确认）
        CavityRun(prog, method, tool, geom, "PM2_E2", "E2 复刻 c5: MultiDepthCut.Toggle=true",
            (b) => { b.CutParameters.MultiDepthCut.Toggle = true; },
            "multiDepth=True");
        // E3 邻接判别：MultiDepthCut.StepMethod=Passes + Toggle=true —— 整对象丢弃 vs toggle 键级
        CavityRun(prog, method, tool, geom, "PM2_E3", "E3 邻接: StepMethod=Passes + Toggle=true",
            (b) =>
            {
                b.CutParameters.MultiDepthCut.StepMethod = MultiDepthCut.Types.Passes;
                b.CutParameters.MultiDepthCut.Toggle = true;
            },
            "stepMethod=Passes", "multiDepth=True");
        // E4 复刻 v1 c6：BoundaryInTol=0.02（直 double）→ 期望还原（负确认）
        CavityRun(prog, method, tool, geom, "PM2_E4", "E4 复刻 c6: BoundaryInTol=0.02",
            (b) => { b.CutParameters.BoundaryInTol = 0.02; },
            "intol=0.02");
        // E5 邻接判别：BoundaryOutTol=0.03（同族兄弟直 double）—— 族死区 vs InTol 键级
        CavityRun(prog, method, tool, geom, "PM2_E5", "E5 邻接: BoundaryOutTol=0.03",
            (b) => { b.CutParameters.BoundaryOutTol = 0.03; },
            "outtol=0.03");
        // E6 邻接判别：BoundaryInTol 在 PLANAR_MILL（mill_planar 模板）—— CAVITY_MILL 特化排除
        PlanarRun(prog, method, tool, geom, "PM2_E6", "E6 邻接: PLANAR_MILL 上 BoundaryInTol=0.02",
            (b) => { b.CutParameters.BoundaryInTol = 0.02; },
            "intol=0.02");
        // E7 阳性锚点（v1 c4）：finish_passes=2（int 直赋）→ 期望持久
        CavityRun(prog, method, tool, geom, "PM2_E7", "E7 锚点+: finish_passes=2",
            (b) => { b.CutParameters.FinishPasses.NumberOfFinishPasses = 2; },
            "finish=2");
    }

    // 腔铣实验：建独立 op → 写前快照 → 写入（commit）→ 重开快照 → 逐期望 token 自动判定
    private static void CavityRun(NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom,
        string opName, string label, Action<CavityMillingBuilder> write, params string[] expectTokens)
    {
        Note("-- " + label);
        Operation op = NewOp(prog, method, tool, geom, "mill_contour", "CAVITY_MILL", opName);
        if (op == null) return;
        CavityMillingBuilder b = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        string before;
        try
        {
            before = CavitySnap(b);
            R("写前", () => before);
            try { write(b); b.Commit(); Log("  写入+commit OK"); }
            catch (Exception e) { Log("  写入/commit 异常: " + e.Message); b.Destroy(); return; }
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        string after;
        try { after = CavitySnap(b2); R("重开", () => after); }
        finally { b2.Destroy(); }
        Verdict(label, before, after, expectTokens);
    }

    // 平面铣实验（E6）：同上，仅快照面为 intol
    private static void PlanarRun(NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom,
        string opName, string label, Action<PlanarMillingBuilder> write, params string[] expectTokens)
    {
        Note("-- " + label);
        Operation op = NewOp(prog, method, tool, geom, "mill_planar", "PLANAR_MILL", opName);
        if (op == null) return;
        PlanarMillingBuilder b = _cam.CAMOperationCollection.CreatePlanarMillingBuilder(op);
        string before;
        try
        {
            before = PlanarIntolSnap(b);
            R("写前", () => before);
            try { write(b); b.Commit(); Log("  写入+commit OK"); }
            catch (Exception e) { Log("  写入/commit 异常: " + e.Message); b.Destroy(); return; }
        }
        finally { b.Destroy(); }
        PlanarMillingBuilder b2 = _cam.CAMOperationCollection.CreatePlanarMillingBuilder(op);
        string after;
        try { after = PlanarIntolSnap(b2); R("重开", () => after); }
        finally { b2.Destroy(); }
        Verdict(label, before, after, expectTokens);
    }

    // 自动判定：重开串含期望 token → 持久 ✓；写前串已含（模板默认巧合）→ 该 token 无判别力；
    // 否则 → 还原 ✗
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

    private static string CavitySnap(CavityMillingBuilder b)
    {
        try
        {
            return string.Format("pattern={0} order={1} dir={2} multiDepth={3} stepMethod={4} finish={5} intol={6} outtol={7}",
                b.CutPattern.CutPattern,
                b.CutParameters.CutOrder,
                b.CutParameters.CutDirection.Type,
                b.CutParameters.MultiDepthCut.Toggle,
                b.CutParameters.MultiDepthCut.StepMethod,
                b.CutParameters.FinishPasses.NumberOfFinishPasses,
                b.CutParameters.BoundaryInTol.ToString("0.####"),
                b.CutParameters.BoundaryOutTol.ToString("0.####"));
        }
        catch (Exception e) { return "(快照异常: " + e.Message + ")"; }
    }

    private static string PlanarIntolSnap(PlanarMillingBuilder b)
    {
        try
        {
            return string.Format("intol={0} outtol={1}",
                b.CutParameters.BoundaryInTol.ToString("0.####"),
                b.CutParameters.BoundaryOutTol.ToString("0.####"));
        }
        catch (Exception e) { return "(快照异常: " + e.Message + ")"; }
    }

    // ================= 工具 =================
    private static Operation NewOp(NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom,
        string typeName, string subtype, string name)
    {
        try
        {
            return _cam.CAMOperationCollection.Create(prog, method, tool, geom,
                typeName, subtype, OperationCollection.UseDefaultName.False, name);
        }
        catch (Exception e) { Note("  NewOp(" + name + ") 失败: " + e.Message); return null; }
    }

    private static NCGroup TryCreateGroup(CAMSetup.View view, string typeName, string subtype, string name)
    {
        try
        {
            NCGroup root = _cam.GetRoot(view);
            if (root == null) { Note("  根组 null (view=" + view + ")"); return null; }
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
        catch (Exception e) { Note("  组创建 " + name + " 失败: " + e.Message); return null; }
    }

    private static void Step(string label, Action act)
    {
        Log("");
        Log("== 阶段: " + label + " ==");
        try { act(); _ok++; }
        catch (Exception e)
        {
            _fail++;
            Log("  !! 阶段异常: " + e.Message);
            if (e.InnerException != null) Log("     inner: " + e.InnerException.Message);
        }
    }

    private static void Note(string s) { Log("  " + s); }

    private static void R(string label, Func<string> f)
    {
        try { Log("  " + label + " = " + f()); }
        catch (Exception e) { Log("  " + label + " 异常: " + e.GetType().Name + " " + e.Message); }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
