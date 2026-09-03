// CamProbeFinalize.cs — 收官批探针 v5（2026-09-04，run_journal 无界面批处理驱动）
//
// 覆盖待实测项（nx2406-install-index.md §3 / nx-plan-exporter-spec.md §5）：
//   [1] Stepover 链路（写 50 读 70 机制）+ U-3 InheritanceStatus 语义
//   [2] SpindleModeBuilder 数值编码（rpm/sfm/mode 关联 + 写入实验）
//   [3] ToolDrivePoint string 取值集合（新 DRILLING op 往返 + CycleTable 表面）
//   [4] PTP 旧模板（打点/G83）参数面（builder + 用户属性 + BuilderProperties）
//   [5] MCS 扩展回读：FixtureOffset + TransferClearance + LowerLimit（U-4 收尾）
//   [6] U-5c：UFModl.AskMassProps3d 对 face 是否可用（body 正对照）
//
// 运行：run_journal.exe src\NXPlugins\Journal\CamProbeFinalize.cs（批处理无界面）。
// 关键结论（详见索引 §2/§3 回填与 samples\camprobe-finalize-20260904-010401.txt）：
//   ① 批处理 CAM 纪律：先 NewDisplay 建件 → Session.CreateCamSession() → CreateCamSetup
//      （无部件时 CreateCamSession 原生崩溃；先 CreateCamSetup 会产生坏 CAMSetup）；
//   ② Stepover 整链（StepoverType+子 Builder.Value）commit 时被静默还原（写 50→70、1.5→15、
//      Constant→PercentToolFlat），普通参数（PartStock）写入可靠 → Stepover 链写无效；
//   ③ InheritanceStatus：True=继承中；写 .Value 后 False 且值持久；模板默认值亦为 False；
//   ④ SpindleModeBuilder=int 自由槽（写啥存啥无联动），常态 rpm 场景 mode=0 + SpindleRpmToggle=1；
//   ⑤ ToolDrivePoint 默认 "SYS_CL_TIP"，setter 任意串原样回读（无校验）；
//   ⑥ PTP：Feeds 真实可读（打点 3000rpm/80、G83 500rpm/35），循环细分参数（G83 步距等）无任何读回通道；
//   ⑦ BuilderProperties = 全参数 JSON 树（Value/InheritanceStatus/Tag 内嵌），通用只读增强候选；
//   ⑧ U-5c 结案：AskMassProps3d 拒收 face（"Unknown feature type"），body 可用。
//
// 纪律：写侧全在内存空 Part（不保存）；test.prt 只读（不 Commit/不修改/不保存）。
// 每行即时落盘（硬崩保留阶段痕迹）。输出：samples\camprobe-finalize-<ts>.txt（args[0] 可覆盖）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeFinalize
{
    private static string _out;
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private const string TplPart = @"C:\Program Files\Siemens\NX2406\mach\resource\template_part\metric\mill_contour.prt";

    private static Part _testPart;      // 会话内缓存，避免重复 Open（943006）
    private static bool _camReady;      // 批处理 APP_NONE 下 CAM 会话可用性（GUI 恒 true）
    private static int _ok, _fail;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-finalize-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeFinalize v5 ==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Session s = null;
        try
        {
            s = Session.GetSession();
            Log("Session: ApplicationName=" + s.ApplicationName
                + "  IsCamSessionInitialized=" + s.IsCamSessionInitialized());

            Step("S0 会话基线（批处理 CAM 会话可用性观测）", () =>
            {
                Log("  IsCamSessionInitialized=" + s.IsCamSessionInitialized()
                    + "  （无部件时 CreateCamSession 曾原生崩溃；有部件后再试，见 S1）");
            });

            Part writePart = null;
            CAMSetup writeCam = null;
            bool writable = true;
            Step("S1 空 Part 写环境（顺序：建件→CAM 会话→CreateCamSetup）", () =>
            {
                try
                {
                    writePart = s.Parts.NewDisplay("CamProbeFinalize", Part.Units.Millimeters);
                    if (!s.IsCamSessionInitialized())
                    {
                        s.CreateCamSession();
                        Log("  Session.CreateCamSession() OK（建件后调用不崩；无件调用曾原生崩溃）");
                    }
                    writeCam = writePart.CreateCamSetup("mill_contour");
                    _camReady = s.IsCamSessionInitialized();
                    Log("  NewDisplay+CreateCamSetup OK, camReady=" + _camReady);
                    if (_camReady)
                    {
                        NCGroup root = writeCam.GetRoot(CAMSetup.View.ProgramOrder);
                        Log("  GetRoot(ProgramOrder)=" + (root == null ? "(null)" : root.Name));
                    }
                    else Log("  camReady=False——先建 CAMSetup 再建会话会导致 CAMSetup 坏（GetRoot NRE），本版已调序");
                }
                catch (Exception e)
                {
                    writable = false;
                    Log("  NewDisplay/初始化 失败(" + e.Message + ") → 退化为模板件只读环境（写段跳过）");
                    PartLoadStatus ls;
                    writePart = s.Parts.OpenDisplay(TplPart, out ls);
                    writeCam = writePart.CAMSetup;
                    Log("  模板件 CAMSetup=" + (writeCam != null));
                }
            });

            if (writable && _camReady)
            {
                Step("S2 Stepover 链路 + InheritanceStatus 语义", () => SectionStepover(writeCam));
                Step("S3 SpindleMode 数值编码", () => SectionSpindleMode(writeCam));
                Step("S4 ToolDrivePoint + CycleTable（DRILLING）", () => SectionDrillSurface(writeCam));
            }
            else Log("!! 写段跳过（无空 Part 环境或 CAM 会话不可用，camReady=" + _camReady
                + "）→ 此批 CAM 写侧/Builder 段为 GUI 补跑项");

            Step("S5 test.prt 读侧：PTP 参数面/属性/MCS 扩展回读", () => SectionReadTestPart(s));
            Step("S6 U-5c AskMassProps3d(face) 可行性", () => SectionMassProps(s));
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ================= S2：Stepover + InheritanceStatus =================
    // 实验矩阵（每实验新建独立 CAVITY_MILL op，避免串扰）
    private static void SectionStepover(CAMSetup cam)
    {
        NCGroup prog = TryCreateGroup(cam, CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "FZ_PROG");
        NCGroup method = TryCreateGroup(cam, CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "FZ_METHOD");
        NCGroup tool = TryCreateGroup(cam, CAMSetup.View.MachineTool, "mill_planar", "MILL", "FZ_TOOL");
        NCGroup geom = TryCreateGroup(cam, CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "FZ_MCS");
        if (prog == null || method == null || tool == null || geom == null) { Note("组创建不全，S2 中止"); return; }

        // E1：StepoverType=PercentToolFlat + Value=50，逐级读（同 builder 写后即读 → Commit → 重开）
        Operation op1 = NewCavity(cam, prog, method, tool, geom, "FZ_E1");
        if (op1 == null) return;
        Note("-- E1 基线：type=PercentToolFlat + value=50");
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op1);
        try
        {
            R("写前 StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            R("写前 PercentToolFlat.Value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("写前 PercentToolFlat.InheritanceStatus", () => b.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
            P("写 StepoverType=PercentToolFlat", () =>
                b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat);
            P("写 PercentToolFlat.Value=50", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0);
            R("写后(未Commit) StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            R("写后(未Commit) PercentToolFlat.Value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("写后(未Commit) InheritanceStatus", () => b.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op1);
        try
        {
            R("重开 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
            R("重开 PercentToolFlat.Value", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("重开 PercentToolFlat.InheritanceStatus", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
            R("重开 PartStock.InheritanceStatus(未写过=继承?)", () => b2.CutParameters.PartStock.InheritanceStatus.ToString());
            R("重开 PartStock.Value(继承生效值?)", () => b2.CutParameters.PartStock.Value.ToString("0.####"));
        }
        finally { b2.Destroy(); }

        // E2：只写 Value=50 不动 StepoverType
        Operation op2 = NewCavity(cam, prog, method, tool, geom, "FZ_E2");
        if (op2 == null) return;
        Note("-- E2 只写 PercentToolFlat.Value=50（不动 StepoverType）");
        CavityMillingBuilder c2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
        try
        {
            R("写前 StepoverType", () => c2.CutParameters.Stepover.StepoverType.ToString());
            P("写 PercentToolFlat.Value=50", () => c2.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0);
            R("写后 StepoverType", () => c2.CutParameters.Stepover.StepoverType.ToString());
            R("写后 InheritanceStatus", () => c2.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
            R("Commit", () => { c2.Commit(); return "ok"; });
        }
        finally { c2.Destroy(); }
        CavityMillingBuilder c2b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
        try
        {
            R("重开 StepoverType", () => c2b.CutParameters.Stepover.StepoverType.ToString());
            R("重开 PercentToolFlat.Value", () => c2b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("重开 InheritanceStatus", () => c2b.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
        }
        finally { c2b.Destroy(); }

        // E3：分数假设（0.5 代替 50）
        Operation op3 = NewCavity(cam, prog, method, tool, geom, "FZ_E3");
        if (op3 == null) return;
        Note("-- E3 分数假设 value=0.5");
        CavityMillingBuilder c3 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op3);
        try
        {
            P("写 StepoverType=PercentToolFlat + Value=0.5", () =>
            {
                c3.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                c3.CutParameters.Stepover.PercentToolFlatBuilder.Value = 0.5;
            });
            R("Commit", () => { c3.Commit(); return "ok"; });
        }
        finally { c3.Destroy(); }
        CavityMillingBuilder c3b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op3);
        try { R("重开 PercentToolFlat.Value", () => c3b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####")); }
        finally { c3b.Destroy(); }

        // E4 对照：PartStock 显式写（验证 InheritanceStatus 语义 + 显式值持久）
        Note("-- E4 对照：PartStock=0.3 显式写（op1 上）");
        CavityMillingBuilder c4 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op1);
        try
        {
            R("写前 PartStock.InheritanceStatus", () => c4.CutParameters.PartStock.InheritanceStatus.ToString());
            P("写 PartStock.Value=0.3", () => c4.CutParameters.PartStock.Value = 0.3);
            R("写后 InheritanceStatus", () => c4.CutParameters.PartStock.InheritanceStatus.ToString());
            R("Commit", () => { c4.Commit(); return "ok"; });
        }
        finally { c4.Destroy(); }
        CavityMillingBuilder c4b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op1);
        try
        {
            R("重开 PartStock.Value", () => c4b.CutParameters.PartStock.Value.ToString("0.####"));
            R("重开 PartStock.InheritanceStatus", () => c4b.CutParameters.PartStock.InheritanceStatus.ToString());
            R("重开 FloorStock.InheritanceStatus(未写)", () => c4b.CutParameters.FloorStock.InheritanceStatus.ToString());
            R("重开 FloorStock.Value(未写=继承生效值)", () => c4b.CutParameters.FloorStock.Value.ToString("0.####"));
        }
        finally { c4b.Destroy(); }

        // E5 诊断：E1 op 的 BuilderProperties 内 Stepover 存储值（50 还是 70）
        Note("-- E5 诊断：op1.BuilderProperties Stepover 键扫描");
        ScanBuilderProps(op1, new[] { "Stepover", "PercentToolFlat" }, "  E1-op");

        // E6：PercentToolFlat 链已证无效（v5 E1-E5：写 50 commit 后仍 70），替换通道 Constant+Distance 是否可写
        Operation op6 = NewCavity(cam, prog, method, tool, geom, "FZ_E6");
        if (op6 == null) return;
        Note("-- E6 替换通道：StepoverType=Constant + DistanceBuilder.Value=1.5");
        CavityMillingBuilder c6 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op6);
        try
        {
            R("写前 StepoverType", () => c6.CutParameters.Stepover.StepoverType.ToString());
            P("写 type=Constant + DistanceBuilder.Value=1.5", () =>
            {
                c6.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.Constant;
                c6.CutParameters.Stepover.DistanceBuilder.Value = 1.5;
            });
            R("写后(未Commit) DistanceBuilder.Value", () => c6.CutParameters.Stepover.DistanceBuilder.Value.ToString("0.####"));
            R("Commit", () => { c6.Commit(); return "ok"; });
        }
        finally { c6.Destroy(); }
        CavityMillingBuilder c6b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op6);
        try
        {
            R("重开 StepoverType", () => c6b.CutParameters.Stepover.StepoverType.ToString());
            R("重开 DistanceBuilder.Value", () => c6b.CutParameters.Stepover.DistanceBuilder.Value.ToString("0.####"));
            R("重开 DistanceBuilder.InheritanceStatus", () => c6b.CutParameters.Stepover.DistanceBuilder.InheritanceStatus.ToString());
        }
        finally { c6b.Destroy(); }
    }

    // ================= S3：SpindleMode 数值编码 =================
    private static void SectionSpindleMode(CAMSetup cam)
    {
        NCGroup prog = FindGroupByName(cam.GetRoot(CAMSetup.View.ProgramOrder), "FZ_PROG");
        NCGroup method = FindGroupByName(cam.GetRoot(CAMSetup.View.MachineMethod), "FZ_METHOD");
        NCGroup tool = FindGroupByName(cam.GetRoot(CAMSetup.View.MachineTool), "FZ_TOOL");
        NCGroup geom = FindGroupByName(cam.GetRoot(CAMSetup.View.Geometry), "FZ_MCS");
        if (prog == null || method == null || tool == null || geom == null) { Note("S3 组不全，中止"); return; }
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_SM");
        if (op == null) return;
        Note("-- S3 SpindleMode（新腔 op 默认 + 写入实验）");
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("默认 SpindleModeBuilder.Value", () => b.FeedsBuilder.SpindleModeBuilder.Value.ToString());
            R("默认 SpindleRpmBuilder.Value", () => b.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            R("默认 SurfaceSpeedBuilder.Value", () => b.FeedsBuilder.SurfaceSpeedBuilder.Value.ToString("0.####"));
            R("默认 SpindleRpmToggle(int)", () => b.FeedsBuilder.SpindleRpmToggle.ToString());
            R("默认 RetractSpeedToggle(int)", () => b.FeedsBuilder.RetractSpeedToggle.ToString());
        }
        finally { b.Destroy(); }
        // 写 rpm=6000 后观察 mode/sfm 联动
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            P("写 SpindleRpmBuilder.Value=6000", () => b2.FeedsBuilder.SpindleRpmBuilder.Value = 6000.0);
            R("Commit", () => { b2.Commit(); return "ok"; });
        }
        finally { b2.Destroy(); }
        CavityMillingBuilder b3 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("rpm6000 后 SpindleModeBuilder.Value", () => b3.FeedsBuilder.SpindleModeBuilder.Value.ToString());
            R("rpm6000 后 SpindleRpmBuilder.Value", () => b3.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            R("rpm6000 后 SurfaceSpeedBuilder.Value", () => b3.FeedsBuilder.SurfaceSpeedBuilder.Value.ToString("0.####"));
            R("rpm6000 后 SpindleRpmToggle", () => b3.FeedsBuilder.SpindleRpmToggle.ToString());
            R("rpm6000 后 SpindleRpm.InheritanceStatus", () => b3.FeedsBuilder.SpindleRpmBuilder.InheritanceStatus.ToString());
            R("rpm6000 后 SpindleMode.InheritanceStatus", () => b3.FeedsBuilder.SpindleModeBuilder.InheritanceStatus.ToString());
        }
        finally { b3.Destroy(); }
        // mode 值猜测扫描（0..6，每值独立 op 写→读；仅记录行为不预设语义）
        for (int m = 0; m <= 6; m++)
        {
            Operation mo = NewCavity(cam, prog, method, tool, geom, "FZ_SMODE_" + m);
            if (mo == null) continue;
            CavityMillingBuilder cb = cam.CAMOperationCollection.CreateCavityMillingBuilder(mo);
            try
            {
                P("写 SpindleModeBuilder.Value=" + m, () => cb.FeedsBuilder.SpindleModeBuilder.Value = m);
                R("Commit", () => { cb.Commit(); return "ok"; });
            }
            finally { cb.Destroy(); }
            CavityMillingBuilder cb2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(mo);
            try
            {
                R("回读 mode=" + m + " -> Value", () => cb2.FeedsBuilder.SpindleModeBuilder.Value.ToString());
                R("  rpm", () => cb2.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
                R("  sfm", () => cb2.FeedsBuilder.SurfaceSpeedBuilder.Value.ToString("0.####"));
            }
            finally { cb2.Destroy(); }
        }
    }

    // ================= S4：ToolDrivePoint + CycleTable（DRILLING） =================
    private static void SectionDrillSurface(CAMSetup cam)
    {
        NCGroup prog = FindGroupByName(cam.GetRoot(CAMSetup.View.ProgramOrder), "FZ_PROG");
        NCGroup method = FindGroupByName(cam.GetRoot(CAMSetup.View.MachineMethod), "FZ_METHOD");
        NCGroup tool = FindGroupByName(cam.GetRoot(CAMSetup.View.MachineTool), "FZ_TOOL");
        NCGroup geom = FindGroupByName(cam.GetRoot(CAMSetup.View.Geometry), "FZ_MCS");
        if (prog == null || method == null || tool == null || geom == null) { Note("S4 组不全，中止"); return; }
        Operation op = null;
        P("Create (hole_making, DRILLING)", () =>
        {
            op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "hole_making", "DRILLING", OperationCollection.UseDefaultName.False, "FZ_DRILL");
        });
        if (op == null) { Note("DRILLING 创建失败，S4 中止"); return; }
        Note("-- S4 ToolDrivePoint + CycleTable（新 DRILLING op）");
        HoleDrillingBuilder h = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
        try
        {
            R("默认 GetToolDrivePoint()", () => Str(h.GetToolDrivePoint()));
            R("CycleTable 类型", () => (h.CycleTable == null ? "(null)" : h.CycleTable.GetType().FullName));
            if (h.CycleTable != null)
            {
                R("  CycleType", () => Str(h.CycleTable.CycleType));
                R("  Dwell", () => h.CycleTable.Dwell.ToString());
                R("  AxialStepover.StepoverType", () => h.CycleTable.AxialStepover.StepoverType.ToString());
                R("  AxialStepover.InheritableDistance.Value", () =>
                    (h.CycleTable.AxialStepover.InheritableDistance == null ? "(null)"
                     : h.CycleTable.AxialStepover.InheritableDistance.Value.ToString("0.####")));
            }
        }
        finally { h.Destroy(); }
        // 候选串往返（设置→Commit→重开读回；观察是否 canonicalize/校验）
        string[] candidates = { "TIP", "tip", "Tip", "Tool Tip", "CENTER", "Center", "Tool Center", "tool_center" };
        foreach (string cand in candidates)
        {
            HoleDrillingBuilder hb = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
            try
            {
                P("SetToolDrivePoint(\"" + cand + "\")", () => hb.SetToolDrivePoint(cand));
                R("Commit", () => { hb.Commit(); return "ok"; });
            }
            finally { hb.Destroy(); }
            HoleDrillingBuilder hb2 = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
            try { R("  回读 GetToolDrivePoint()", () => Str(hb2.GetToolDrivePoint())); }
            finally { hb2.Destroy(); }
        }
    }

    // ================= S5：test.prt 读侧 =================
    private static void SectionReadTestPart(Session s)
    {
        Part part = OpenTestPart(s);
        if (part == null) { Note("test.prt 打开失败，S5 中止"); return; }
        CAMSetup cam = part.CAMSetup;
        if (cam == null) { Note("test.prt 无 CAMSetup"); return; }

        // 5a. PTP 参数面（打点 / G83）——逐 op 独立 try
        foreach (string opName in new[] { "打点_COPY_COPY_COPY", "钻头G83_COPY_3_COPY_COPY_COPY_1" })
        {
            try { ProbePtp(cam, opName); }
            catch (Exception e) { Note("  5a " + opName + " 探针异常: " + e.Message); }
        }

        // 5b. 腔 op 对照
        try { ProbeCavity(cam); }
        catch (Exception e) { Note("  5b 腔对照异常: " + e.Message); }

        // 5c. MCS 扩展回读（U-4 收尾）
        try { ProbeMcs(cam); }
        catch (Exception e) { Note("  5c MCS 探针异常: " + e.Message); }
    }

    private static void ProbePtp(CAMSetup cam, string opName)
    {
        Operation op = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), opName);
        if (op == null) { Note("未找到 " + opName); return; }
        Note("-- 5a PTP 参数面: " + opName);
        // 用户属性 dump 不依赖 CAM 会话（U-1 模板元数据候选，v3 已在腔 op 上见货）
        DumpAttrs("  用户属性", op);
        if (!_camReady)
        {
            Note("  (builder 参数面需 CAM 会话——GUI 补跑项)");
            return;
        }
        PointToPointBuilder pb = cam.CAMOperationCollection.CreatePointToPointBuilder(op);
        try
        {
            R("TopSurface 非空", () => (pb.TopSurface == null ? "(null)" : "有"));
            R("BottomSurface 非空", () => (pb.BottomSurface == null ? "(null)" : "有"));
            R("HoleDepth.Value", () => pb.HoleDepth.Value.ToString("0.####"));
            R("HoleDepth.InheritanceStatus", () => pb.HoleDepth.InheritanceStatus.ToString());
            R("HoleDepthType", () => Str(pb.HoleDepthType));
            R("HoleAxisType", () => Str(pb.HoleAxisType));
            R("RetractDistance.Value", () => pb.RetractDistance.Value.ToString("0.####"));
            R("SafeClearance.Value", () => pb.SafeClearance.Value.ToString("0.####"));
            R("DrivePoint(int, MillOperationBuilder 级)", () => pb.DrivePoint.ToString());
            R("CutParameters.PartStock.Value", () => pb.CutParameters.PartStock.Value.ToString("0.####"));
            R("FeedsBuilder.SpindleRpmBuilder.Value", () => pb.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            R("FeedsBuilder.SpindleModeBuilder.Value", () => pb.FeedsBuilder.SpindleModeBuilder.Value.ToString());
            R("FeedsBuilder.FeedCutBuilder.Value", () => pb.FeedsBuilder.FeedCutBuilder.Value.ToString("0.####"));
            R("FeedsBuilder.SpindleRpm.InheritanceStatus", () => pb.FeedsBuilder.SpindleRpmBuilder.InheritanceStatus.ToString());
        }
        finally { pb.Destroy(); }
        DumpBuilderProps("  BuilderProperties", op);
        // U-1/循环参数键扫描：PTP 旧模板 cycle 数据是否存在于 BuilderProperties 序列化里
        ScanBuilderProps(op, new[] { "Cycle", "Peck", "Dwell", "G83", "DepthIncrement", "StepRetract", "ToolDrivePoint" },
            "  PTP cycle 键");
    }

    private static void ProbeCavity(CAMSetup cam)
    {
        Operation cav = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL");
        if (cav == null) { Note("  CAVITY_MILL 未找到"); return; }
        Note("-- 5b 腔对照: CAVITY_MILL");
        DumpAttrs("  用户属性", cav);
        if (!_camReady)
        {
            Note("  (builder/BuilderProperties 需 CAM 会话——GUI 补跑项)");
            return;
        }
        DumpBuilderProps("  BuilderProperties", cav);
        CavityMillingBuilder cb = cam.CAMOperationCollection.CreateCavityMillingBuilder(cav);
        try
        {
            R("SpindleModeBuilder.Value(真实值)", () => cb.FeedsBuilder.SpindleModeBuilder.Value.ToString());
            R("SpindleRpmBuilder.Value(真实值)", () => cb.FeedsBuilder.SpindleRpmBuilder.Value.ToString("0.####"));
            R("SpindleRpmToggle", () => cb.FeedsBuilder.SpindleRpmToggle.ToString());
            R("SpindleRpm.InheritanceStatus", () => cb.FeedsBuilder.SpindleRpmBuilder.InheritanceStatus.ToString());
        }
        finally { cb.Destroy(); }
        ScanBuilderProps(cav, new[] { "Stepover", "PercentToolFlat", "CutLevel", "DepthPerCut" }, "  腔 Stepover/深度 键");
    }

    private static void ProbeMcs(CAMSetup cam)
    {
        Note("-- 5c MCS 扩展回读");
        if (!_camReady) { Note("  (需 CAM 会话——GUI 补跑项)"); return; }
        NCGroup mcs = FindMcs(cam.GetRoot(CAMSetup.View.Geometry));
        if (mcs == null) { Note("  未找到 MCS 组"); return; }
        MillOrientGeomBuilder mb = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(mcs);
        try
        {
            R("FixtureOffset.Value", () => mb.FixtureOffsetBuilder.Value.ToString());
            R("FixtureOffset.InheritanceStatus", () => mb.FixtureOffsetBuilder.InheritanceStatus.ToString());
            R("TransferClearance.ClearanceType", () => mb.TransferClearanceBuilder.ClearanceType.ToString());
            R("TransferClearance.SafeDistance", () => mb.TransferClearanceBuilder.SafeDistance.ToString("0.####"));
            R("TransferClearance.Radius", () => mb.TransferClearanceBuilder.Radius.ToString("0.####"));
            R("安全平面 PlaneXform(Origin/Normal)", () =>
            {
                Plane pl = mb.TransferClearanceBuilder.PlaneXform;
                if (pl == null) return "(PlaneXform null)";
                return string.Format("o=({0:0.###},{1:0.###},{2:0.###}) n=({3:0.###},{4:0.###},{5:0.###})",
                    pl.Origin.X, pl.Origin.Y, pl.Origin.Z, pl.Normal.X, pl.Normal.Y, pl.Normal.Z);
            });
            R("Mcs 原点", () =>
            {
                CartesianCoordinateSystem cs = mb.Mcs;
                return cs == null ? "(Mcs null)" : string.Format("({0:0.###},{1:0.###},{2:0.###})",
                    cs.Origin.X, cs.Origin.Y, cs.Origin.Z);
            });
            R("GetLowerLimitMode()", () => mb.GetLowerLimitMode().ToString());
            R("LowerLimitPlane", () => (mb.LowerLimitPlane == null ? "(null)" : mb.LowerLimitPlane.ToString()));
        }
        finally { mb.Destroy(); }
    }

    // ================= S6：U-5c AskMassProps3d(face) =================
    private static void SectionMassProps(Session s)
    {
        Part part = OpenTestPart(s);
        if (part == null) { Note("test.prt 不可用，S6 中止"); return; }
        CAMSetup cam = part.CAMSetup;
        UFSession uf = UFSession.GetUFSession();
        Note("-- S6 AskMassProps3d：face vs body 对照");
        double[] acc = new double[11];
        acc[0] = 0.001;
        double[] mass = new double[47];
        double[] stat = new double[13];
        // face 目标：body 面枚举第一面（对象类型验证问题，与 CAM 无关 → 走纯建模 API，不依赖 CAM 会话）
        Face face = null;
        try
        {
            Body[] bodies = part.Bodies.ToArray();
            if (bodies.Length > 0)
            {
                Face[] faces = bodies[0].GetFaces();
                if (faces.Length > 0) face = faces[0];
            }
        }
        catch (Exception e) { Note("  face 采集异常: " + e.Message); }
        if (face == null) { Note("  face 未取到（无 body/face），S6 降级仅 body 对照"); }
        else
        {
            R("AskMassProps3d(单 face) 调用", () =>
            {
                try { uf.Modl.AskMassProps3d(new Tag[] { face.Tag }, 1, 1, 4, 0.0, 1, acc, mass, stat); return "OK(意外成功)"; }
                catch (Exception e) { return "按预期拒绝: " + e.GetType().Name + " " + e.Message; }
            });
        }
        R("AskMassProps3d(body 正对照)", () =>
        {
            Body[] bodies = part.Bodies.ToArray();
            if (bodies.Length == 0) return "无 body";
            uf.Modl.AskMassProps3d(new Tag[] { bodies[0].Tag }, 1, 1, 4, 0.0, 1, acc, mass, stat);
            return string.Format("area={0:0.###} volume={1:0.###} COF=({2:0.###},{3:0.###},{4:0.###})",
                mass[0], mass[1], mass[2], mass[3], mass[4], mass[5]);
        });
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

    private static NCGroup FindGroupByName(NCGroup root, string name)
    {
        try
        {
            if (root == null) return null;
            foreach (CAMObject m in root.GetMembers())
            {
                if (m is NCGroup && m.Name == name) return (NCGroup)m;
            }
        }
        catch (Exception e) { Note("  FindGroupByName(" + name + ") 异常: " + e.Message); }
        return null;
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
            catch (Exception e2)
            {
                Log("  Open(test.prt) 也失败: " + e2.Message);
                return null;
            }
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

    private static NCGroup FindMcs(NCGroup g)
    {
        try
        {
            if (g == null) return null;
            foreach (CAMObject m in g.GetMembers())
            {
                NCGroup sub = m as NCGroup;
                if (sub == null) continue;
                if (sub.Name.StartsWith("MCS", StringComparison.Ordinal)) return sub;
                NCGroup hit = FindMcs(sub);
                if (hit != null) return hit;
            }
        }
        catch { }
        return null;
    }

    // 用户属性 dump（U-1 模板信息候选）
    private static void DumpAttrs(string label, CAMObject o)
    {
        R(label + " 用户属性清单", () =>
        {
            string[] attrs = o.GetUserAttributesAsStrings();
            if (attrs == null || attrs.Length == 0) return "(空)";
            int show = Math.Min(30, attrs.Length);
            List<string> parts = new List<string>();
            for (int i = 0; i < show; i++) parts.Add(attrs[i]);
            if (attrs.Length > show) parts.Add("…(" + (attrs.Length - show) + " more)");
            return string.Join(" | ", parts.ToArray());
        });
    }

    // BuilderProperties JSON 关键词上下文扫描（每键至多 3 处，每处 ±130 字符；去引号/换行）
    private static void ScanBuilderProps(CAMObject o, string[] keys, string label)
    {
        R(label + " BuilderProperties 键扫描", () =>
        {
            string s = o.BuilderProperties;
            if (string.IsNullOrEmpty(s)) return "(空)";
            List<string> hits = new List<string>();
            foreach (string key in keys)
            {
                int idx = 0, n = 0;
                while (n < 3 && (idx = s.IndexOf(key, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int from = Math.Max(0, idx - 70);
                    int len = Math.Min(280, s.Length - from);
                    string ctx = s.Substring(from, len).Replace("\"", " ").Replace("\r", " ").Replace("\n", " ");
                    hits.Add("[" + key + "#" + n + "] …" + ctx + "…");
                    idx += key.Length;
                    n++;
                }
            }
            return hits.Count == 0 ? "(无命中)" : string.Join(" || ", hits.ToArray());
        });
    }

    // BuilderProperties 字符串 dump（截断 800 字符）
    private static void DumpBuilderProps(string label, CAMObject o)
    {
        R(label, () =>
        {
            string s = o.BuilderProperties;
            if (string.IsNullOrEmpty(s)) return "(空)";
            return s.Length <= 800 ? s : s.Substring(0, 800) + "…(len=" + s.Length + ")";
        });
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

    private static void P(string label, Action act)
    {
        try { act(); Log("  OK   " + label); }
        catch (Exception e) { Log("  FAIL " + label + " : " + e.Message); }
    }

    private static void R(string label, Func<string> f)
    {
        try { Log("  " + label + " = " + f()); }
        catch (Exception e) { Log("  " + label + " 异常: " + e.GetType().Name + " " + e.Message); }
    }

    // 即时追加落盘（每行立即写，硬崩保留阶段痕迹；日志通道失败不阻断主流程）
    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
