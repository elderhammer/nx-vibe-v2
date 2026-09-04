// CamProbeParams.cs — v1.5-④ 参数面键集注册表探针（2026-09-04，run_journal 批处理驱动）
//
// 目的（docs/nx-plan-comparer-spec.md §5：策略/技术全参数面待导出白名单扩展，v1.5 排队）：
//   读面扫描 —— test.prt 真实 op 各类型对候选键逐一 TryParam，产出"键 → 值/status/异常"实测表
//               （U-1 边界预告：PTP 循环细分读不到；值+InheritanceStatus 供导出语义与校准清单解释）；
//   写面矩阵 —— 内存件对"形态代表键"逐一 写→commit→重开（U-6 教训：形态同类 ≠ 可写；
//               v1 写面全 .Value 形态，本批验证 直枚举/.Type/int/bool/直 double 形态是否可持久）。
// 产出：键集注册表草案（键 / NX 成员路径 / 形态 / 可读 / 可写持久 / 值样例），供 ④ 实现。
//
// S2 读面候选键（§4.3 表 v1.5 子集）：
//   腔铣(CAVITY_MILL)：part_stock/floor_stock/depth_per_cut(现状) + cut_pattern/cut_order/
//     cut_direction/multi_depth_cut(toggle)/finish_passes(int)/stepover(type+%+status)/boundary_intol
//   PTP(打点/G83)：hole_depth(现状) + hole_depth_type/hole_axis_type/retract_distance/
//     tool_drive_point(串)/cycle_table(null 对照)
// S3 写面矩阵（内存空件独立 op，形态代表）：
//   c1 cut_pattern  = Zig（.Type 类嵌套枚举，默认 FollowPart）
//   c2 cut_order    = DepthFirst（直枚举，默认 LevelFirst）
//   c3 cut_direction= Conventional（.Type，默认 Climb）
//   c4 finish_passes= 2（int 直赋）
//   c5 multi_depth_cut = true（bool 直赋）
//   c6 boundary_intol = 0.02（直 double，camprobe-op 曾报写 0.01 读 0——复查）
//
// 纪律：test.prt 只读（不 Commit/不保存）；写侧内存空 Part（mill_contour 模板）不保存；
//   每实验独立 op 零串扰；每行即时落盘。输出：samples\camprobe-params-<ts>.txt（args[0] 可覆盖）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeParams
{
    private static string _out;
    private static int _ok, _fail;
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static Part _testPart;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-params-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeParams v1（v1.5-④ 键集注册表探针）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        try
        {
            s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName
                + "  IsCamSessionInitialized=" + s.IsCamSessionInitialized());

            Part part = null;
            CAMSetup cam = null;
            bool envOk = false;
            Step("S1 写侧环境（建件→CAM 会话→CreateCamSetup）", () =>
            {
                part = s.Parts.NewDisplay("CamProbeParams", Part.Units.Millimeters);
                if (!s.IsCamSessionInitialized()) s.CreateCamSession();
                cam = part.CreateCamSetup("mill_contour");
                Log("  写侧环境 OK, camReady=" + s.IsCamSessionInitialized());
                envOk = true;
            });

            Step("S2 读面扫描：test.prt 真实 op", () => SectionRead(s));
            if (envOk) Step("S3 写面矩阵：形态代表键（commit→重开判据）", () => SectionWriteMatrix(cam));
            else Log("!! 写侧环境不可用 → S3 跳过（GUI 补跑项）");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ================= S2：读面扫描（test.prt，只读） =================
    private static void SectionRead(Session s)
    {
        Part part = OpenTestPart(s);
        if (part == null) { Note("test.prt 打开失败，S2 中止"); return; }
        CAMSetup cam = part.CAMSetup;
        if (cam == null) { Note("test.prt 无 CAMSetup"); return; }

        string[] cavity = { "CAVITY_MILL", "CAVITY_MILL_COPY" };        // 代表（4 腔同族）
        string[] ptp = { "打点_COPY_COPY_COPY", "钻头G83_COPY_3_COPY_COPY_COPY_1" };
        foreach (string n in cavity) ProbeCavity(cam, n);
        foreach (string n in ptp) ProbePtp(cam, n);
    }

    private static void ProbeCavity(CAMSetup cam, string opName)
    {
        Operation op = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), opName);
        if (op == null) { Note("  未找到 " + opName); return; }
        Note("-- 读面 腔: " + opName);
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            Rv("cut_pattern", () => b.CutPattern.CutPattern.ToString());   // 宿主 = CavityMillingBuilder 直接成员
            Rv("cut_order", () => b.CutParameters.CutOrder.ToString());
            Rv("cut_direction", () => b.CutParameters.CutDirection.Type.ToString());
            Rv("multi_depth_cut.toggle", () => b.CutParameters.MultiDepthCut.Toggle.ToString());
            Rv("finish_passes.count", () => b.CutParameters.FinishPasses.NumberOfFinishPasses.ToString());
            Rv("boundary_intol", () => b.CutParameters.BoundaryInTol.ToString("0.####"));
            Rv("stepover.type", () => b.CutParameters.Stepover.StepoverType.ToString());
            Rv("stepover.percent_tool_flat.value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            Rv("stepover.percent_tool_flat.inheritance", () => b.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
            Rv("part_stock.value", () => b.CutParameters.PartStock.Value.ToString("0.####"));
            Rv("part_stock.inheritance", () => b.CutParameters.PartStock.InheritanceStatus.ToString());
            Rv("floor_stock.value", () => b.CutParameters.FloorStock.Value.ToString("0.####"));
            Rv("depth_per_cut.value", () => b.DepthPerCut.Value.ToString("0.####"));
            Rv("rpm", () => b.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            Rv("feed_cut", () => b.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
        }
        finally { b.Destroy(); }
    }

    private static void ProbePtp(CAMSetup cam, string opName)
    {
        Operation op = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), opName);
        if (op == null) { Note("  未找到 " + opName); return; }
        Note("-- 读面 PTP: " + opName);
        PointToPointBuilder b = cam.CAMOperationCollection.CreatePointToPointBuilder(op);
        try
        {
            Rv("hole_depth.value", () => b.HoleDepth.Value.ToString("0.####"));
            Rv("hole_depth.inheritance", () => b.HoleDepth.InheritanceStatus.ToString());
            Rv("hole_depth_type", () => b.HoleDepthType.ToString());
            Rv("hole_axis_type", () => b.HoleAxisType.ToString());
            Rv("retract_distance.value", () => b.RetractDistance.Value.ToString("0.####"));
            Note("    tool_drive_point / cycle_table: PTP 无 HoleDrillingBuilder 面（编译实证 cast 非法）——既有负证（U-1/PTP）");
            Rv("rpm", () => b.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            Rv("rpm.inheritance", () => b.FeedsBuilder.SpindleRpmBuilder.InheritanceStatus.ToString());
            Rv("feed_cut", () => b.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
        }
        finally { b.Destroy(); }
    }

    // ================= S3：写面矩阵（内存空件，形态代表键；独立 op） =================
    private static void SectionWriteMatrix(CAMSetup cam)
    {
        NCGroup prog = TryCreateGroup(cam, CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "PM_PROG");
        NCGroup method = TryCreateGroup(cam, CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "PM_METHOD");
        NCGroup tool = TryCreateGroup(cam, CAMSetup.View.MachineTool, "mill_planar", "MILL", "PM_TOOL");
        NCGroup geom = TryCreateGroup(cam, CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "PM_MCS");
        if (prog == null || method == null || tool == null || geom == null) { Note("组创建不全，S3 中止"); return; }

        WriteOne(cam, prog, method, tool, geom, "PM_C1", "cut_pattern=Zig（.Type 类嵌套枚举，宿主 builder 直接成员）", (b) =>
        {
            b.CutPattern.CutPattern = CutPatternBuilder.Types.Zig;
            return "CutPattern=" + b.CutPattern.CutPattern;
        });
        WriteOne(cam, prog, method, tool, geom, "PM_C2", "cut_order=DepthFirst（直枚举）", (b) =>
        {
            b.CutParameters.CutOrder = CutParametersCutOrderTypes.DepthFirst;
            return "CutOrder=" + b.CutParameters.CutOrder;
        });
        WriteOne(cam, prog, method, tool, geom, "PM_C3", "cut_direction=Conventional（.Type）", (b) =>
        {
            b.CutParameters.CutDirection.Type = CutDirection.Types.Conventional;
            return "CutDirection=" + b.CutParameters.CutDirection.Type;
        });
        WriteOne(cam, prog, method, tool, geom, "PM_C4", "finish_passes=2（int 直赋）", (b) =>
        {
            b.CutParameters.FinishPasses.NumberOfFinishPasses = 2;
            return "FinishPasses=" + b.CutParameters.FinishPasses.NumberOfFinishPasses;
        });
        WriteOne(cam, prog, method, tool, geom, "PM_C5", "multi_depth_cut.toggle=true（bool 直赋）", (b) =>
        {
            b.CutParameters.MultiDepthCut.Toggle = true;
            return "MultiDepthCut.Toggle=" + b.CutParameters.MultiDepthCut.Toggle;
        });
        WriteOne(cam, prog, method, tool, geom, "PM_C6", "boundary_intol=0.02（直 double，复查 camprobe-op）", (b) =>
        {
            b.CutParameters.BoundaryInTol = 0.02;
            return "BoundaryInTol=" + b.CutParameters.BoundaryInTol.ToString("0.####");
        });
    }

    // 写 → commit → 重开读回；重开用独立 builder 实例（U-6 判据：commit→重开为准）
    private static void WriteOne(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom,
        string opName, string label, Func<CavityMillingBuilder, string> write)
    {
        Note("-- S3 " + opName + "：" + label);
        Operation op = NewCavity(cam, prog, method, tool, geom, opName);
        if (op == null) return;
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("写前 ", () => ProbeCavityValues(b));
            R("写入", () => { string s = write(b); b.Commit(); return s; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("重开", () => ProbeCavityValues(b2));
            Note("  S3 判定：重开 == 写入值 → 该形态可持久（入注册表）；还原模板默认 → 不可写（U-6 同款）");
        }
        finally { b2.Destroy(); }
    }

    // 腔 op 全候选值快照（写前/重开对照用）
    private static string ProbeCavityValues(CavityMillingBuilder b)
    {
        try
        {
            return string.Format("pattern={0} order={1} dir={2} finish={3} multi={4} intol={5}",
                b.CutPattern.CutPattern,
                b.CutParameters.CutOrder,
                b.CutParameters.CutDirection.Type,
                b.CutParameters.FinishPasses.NumberOfFinishPasses,
                b.CutParameters.MultiDepthCut.Toggle,
                b.CutParameters.BoundaryInTol.ToString("0.####"));
        }
        catch (Exception e) { return "(快照异常: " + e.Message + ")"; }
    }

    // ================= 工具 =================
    private static Operation NewCavity(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom, string name)
    {
        try
        {
            return cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.False, name);
        }
        catch (Exception e) { Note("  NewCavity(" + name + ") 失败: " + e.Message); return null; }
    }

    private static NCGroup TryCreateGroup(CAMSetup cam, CAMSetup.View view, string typeName, string subtype, string name)
    {
        try
        {
            NCGroup root = cam.GetRoot(view);
            if (root == null) { Note("  根组 null (view=" + view + ")"); return null; }
            NCGroupCollection g = cam.CAMGroupCollection;
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

    private static Part OpenTestPart(Session s)
    {
        if (_testPart != null) return _testPart;
        try
        {
            PartLoadStatus ls;
            _testPart = s.Parts.OpenDisplay(TestPart, out ls);
            return _testPart;
        }
        catch (Exception e1)
        {
            Log("  OpenDisplay(test.prt) 失败(" + e1.Message + ") → 试 Open");
            try
            {
                PartLoadStatus ls2;
                _testPart = s.Parts.Open(TestPart, out ls2);
                return _testPart;
            }
            catch (Exception e2) { Log("  Open(test.prt) 也失败: " + e2.Message); return null; }
        }
    }

    private static Operation FindOp(NCGroup group, string name)
    {
        try
        {
            if (group == null) return null;
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
        catch (Exception e) { Note("  FindOp(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static string Str(object o) { return o == null ? "(null)" : o.ToString(); }

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

    // 读面值（含异常捕获，异常 = 该键不可读 → 注册表"不可读"证据）
    private static void Rv(string label, Func<string> f)
    {
        try { Log("    " + label + " = " + f()); }
        catch (Exception e) { Log("    " + label + " 异常: " + e.GetType().Name + " " + e.Message); }
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
