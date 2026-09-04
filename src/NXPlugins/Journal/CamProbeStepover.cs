// CamProbeStepover.cs — U-6 Stepover 有效写入通道收口探针（2026-09-04，run_journal 批处理驱动）
//
// 覆盖（docs/nx-stepover-probe-spec.md §4 实验树 P0-P8，三跑复现一致：
// samples\camprobe-stepover-20260904-{152830,153003,153051}.txt）：
//   P0 复刻 E1（PercentToolFlat.Value=50 → commit → 重开）——环境复现基线（预期 70）
//   P1 BuilderProperties 快照语义：builder A 写 PartStock=0.3 + Stepover 50（不 commit，
//      destroy A）→ JSON 扫描——对照自证（连已写 PartStock 的 JSON 亦保持 0）：
//      BuilderProperties 只反映已提交态，非未提交写入实时视图 → P1 不作传播判别（P2/P8 承担）
//   P2 dirty 记账判别：同 builder 双写 PartStock=0.3 + Stepover 50 → commit → 重开
//      （PartStock 持久 + stepover 还原 = stepover 叶子从不入 dirty 集）
//   P3 StepoverLimit 通道（官方样例同款写法）：越界值 75 → Commit NXException（值域 [100,300]%
//      实证）；界内非默认值 200 → Commit OK 但重开仍回填默认 150 → 不可持久
//   P4 Distance+Intent：Constant 型 .Intent=PartUnits + Value=1.5 → commit → 重开 type/value/intent
//   P5 Planar 对照：mill_planar/PLANAR_MILL 复刻 P2（排除 CAVITY_MILL 特化）
//   P6 方法组级通道：CreateMillMethodBuilder(组) 写同链 → commit → 重开
//   P7 直接 int 成员：type=Number + NumberOfStepovers=4 → commit → 重开
//   P8 暂存 flush 假设：builder A 写 Stepover 50（不 commit，destroy）→ builder B 空 Commit → 重开
//
// 结论（γ 负结案）：公开 .NET 面无 stepover 有效写入通道——8 通道形态全负，commit 后必还原
// 模板默认；回填与机制残留注记见 docs/nx-stepover-probe-spec.md §6。
//
// 纪律：内存空 Part（mill_contour 模板）不保存；每实验独立 op（FZ_P0..FZ_P8）零串扰；
//   每行即时落盘（硬崩保留阶段痕迹）。输出：samples\camprobe-stepover-<ts>.txt（args[0] 可覆盖）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeStepover
{
    private static string _out;
    private static int _ok, _fail;

    public static void Main(string[] args)
    {
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-stepover-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        if (args.Length > 0 && !string.IsNullOrEmpty(args[0])) _out = args[0];
        Log("== CamProbeStepover（U-6 收口探针）==");
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
            Step("S1 空 Part 写环境（建件→CAM 会话→CreateCamSetup）", () =>
            {
                part = s.Parts.NewDisplay("CamProbeStepover", Part.Units.Millimeters);
                if (!s.IsCamSessionInitialized()) s.CreateCamSession();
                cam = part.CreateCamSetup("mill_contour");
                Log("  NewDisplay+CreateCamSession+CreateCamSetup OK, camReady=" + s.IsCamSessionInitialized());
                envOk = true;
            });
            if (!envOk) { Log("!! 环境不可用 → 中止（GUI 补跑项）"); return; }

            Step("S2 实验树 P0-P8", () => RunExperiments(cam, s));
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 汇总 ok=" + _ok + " fail=" + _fail + " ==");
        Log("== 结束 ==");
    }

    // ================= S2：实验树（P0-P8，每实验独立 op） =================
    private static void RunExperiments(CAMSetup cam, Session s)
    {
        NCGroup prog = TryCreateGroup(cam, CAMSetup.View.ProgramOrder, "mill_contour", "PROGRAM", "FZ_PROG");
        NCGroup method = TryCreateGroup(cam, CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "FZ_METHOD");
        NCGroup tool = TryCreateGroup(cam, CAMSetup.View.MachineTool, "mill_planar", "MILL", "FZ_TOOL");
        NCGroup geom = TryCreateGroup(cam, CAMSetup.View.Geometry, "mill_contour", "WORKPIECE", "FZ_MCS");
        if (prog == null || method == null || tool == null || geom == null) { Note("组创建不全，S2 中止"); return; }

        try { P0(cam, prog, method, tool, geom); } catch (Exception e) { Note("P0 异常: " + e.Message); }
        try { P1(cam, prog, method, tool, geom); } catch (Exception e) { Note("P1 异常: " + e.Message); }
        try { P2(cam, prog, method, tool, geom); } catch (Exception e) { Note("P2 异常: " + e.Message); }
        try { P3(cam, prog, method, tool, geom); } catch (Exception e) { Note("P3 异常: " + e.Message); }
        try { P4(cam, prog, method, tool, geom); } catch (Exception e) { Note("P4 异常: " + e.Message); }
        try { P5(cam, prog, method, tool, geom); } catch (Exception e) { Note("P5 异常: " + e.Message); }
        try { P6(cam, method); } catch (Exception e) { Note("P6 异常: " + e.Message); }
        try { P7(cam, prog, method, tool, geom); } catch (Exception e) { Note("P7 异常: " + e.Message); }
        try { P8(cam, prog, method, tool, geom); } catch (Exception e) { Note("P8 异常: " + e.Message); }
    }

    // P0：E1 复刻——环境复现基线
    private static void P0(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P0 复刻 E1（环境基线）：PercentToolFlat.Value=50 → commit → 重开，预期回 70");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P0");
        if (op == null) return;
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("写前 StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            R("写前 PercentToolFlat.Value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            P("写 StepoverType=PercentToolFlat + Value=50", () =>
            {
                b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0;
            });
            R("写后(未Commit) PercentToolFlat.Value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("重开 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
            R("重开 PercentToolFlat.Value", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
        }
        finally { b2.Destroy(); }
    }

    // P1：BuilderProperties 快照语义判别——写后不 commit，destroy builder，扫 JSON
    // 对照自证：PartStock=0.3 已写而 JSON 仍 0 → JSON 只反映已提交态，未提交写入不可见
    // → 本实验不作为传播判别；传播证据由 P2（dirty 记账）+ P8（destroy 丢写）承担。
    private static void P1(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P1 BuilderProperties 快照语义：A 写（不 commit）→ JSON 扫 Value（对照自证）");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P1");
        if (op == null) return;
        CavityMillingBuilder a = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("写前 PartStock.Value", () => a.CutParameters.PartStock.Value.ToString("0.####"));
            R("写前 PercentToolFlat.Value", () => a.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            P("写 PartStock.Value=0.3", () => a.CutParameters.PartStock.Value = 0.3);
            P("写 StepoverType=PercentToolFlat + Value=50", () =>
            {
                a.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                a.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0;
            });
            R("写后(同 builder A) PercentToolFlat.Value", () => a.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
        }
        finally { a.Destroy(); }
        // destroy 后扫 BuilderProperties（已提交态快照视图）
        R("JSON 中 PartStock 对象 Value 命中（写 0.3 后）", () => Join(ScanJsonValues(op, "PartStock", 4), " | "));
        R("JSON 中 PercentToolFlatBuilder Value 命中（写 50 后）", () => Join(ScanJsonValues(op, "PercentToolFlatBuilder", 6), " | "));
        Note("  P1 语义：对照 PartStock JSON 亦不变 → JSON=已提交态快照，本实验无传播判别力（由 P2/P8 承担）");
    }

    // P2：dirty 记账判别（决定性）——同 builder 双写 → commit → 重开
    private static void P2(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P2 dirty 记账：同 builder 写 PartStock=0.3 + Stepover 50 → commit → 重开");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P2");
        if (op == null) return;
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            P("写 PartStock.Value=0.3", () => b.CutParameters.PartStock.Value = 0.3);
            P("写 StepoverType=PercentToolFlat + Value=50", () =>
            {
                b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0;
            });
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("重开 PartStock.Value", () => b2.CutParameters.PartStock.Value.ToString("0.####"));
            R("重开 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
            R("重开 PercentToolFlat.Value", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("重开 PercentToolFlat.InheritanceStatus", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.InheritanceStatus.ToString());
            R("P2 判定", () =>
            {
                double v = b2.CutParameters.Stepover.PercentToolFlatBuilder.Value;
                if (v == 50.0) return "stepover 持久 → E 系列另有原因（转 P5/P8）";
                return "PartStock 持久 + stepover 还原 → stepover 叶子从不入 dirty 集（stub 定论）";
            });
        }
        finally { b2.Destroy(); }
    }

    // P3：StepoverLimit 通道（官方样例同款写法）
    // 越界值 75 → Commit 抛 NXException "Stepover Limit must be between 100 and 300 percent."
    //   ——写入可达 NX 校验层（与主链静默丢弃迥异），取值域 [100,300]% 实证；
    // 界内非默认值 200 → Commit OK 但重开仍回填默认 150 → 不可持久。
    private static void P3(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P3 StepoverLimit 通道：Value=200（默认 150；v2 越界 75 被 Commit 校验拒绝）→ commit → 重开");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P3");
        if (op == null) return;
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("写前 StepoverLimit.Value", () => b.CutParameters.StepoverLimit.Value.ToString("0.####"));
            R("写前 StepoverLimit.InheritanceStatus", () => b.CutParameters.StepoverLimit.InheritanceStatus.ToString());
            P("写 StepoverLimit.Value=200", () => b.CutParameters.StepoverLimit.Value = 200.0);
            R("写后(未Commit) StepoverLimit.Value", () => b.CutParameters.StepoverLimit.Value.ToString("0.####"));
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("重开 StepoverLimit.Value", () => b2.CutParameters.StepoverLimit.Value.ToString("0.####"));
            R("重开 StepoverLimit.InheritanceStatus", () => b2.CutParameters.StepoverLimit.InheritanceStatus.ToString());
        }
        finally { b2.Destroy(); }
    }

    // P4：Constant + Distance + Intent
    private static void P4(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P4 Distance+Intent：Constant + .Intent=PartUnits + Value=1.5 → commit → 重开");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P4");
        if (op == null) return;
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("写前 StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            R("写前 DistanceBuilder.Intent", () => b.CutParameters.Stepover.DistanceBuilder.Intent.ToString());
            R("写前 DistanceBuilder.Value", () => b.CutParameters.Stepover.DistanceBuilder.Value.ToString("0.####"));
            P("写 type=Constant + Intent=PartUnits + Value=1.5", () =>
            {
                b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.Constant;
                b.CutParameters.Stepover.DistanceBuilder.Intent = ParamValueIntent.PartUnits;
                b.CutParameters.Stepover.DistanceBuilder.Value = 1.5;
            });
            R("写后(未Commit) DistanceBuilder.Value", () => b.CutParameters.Stepover.DistanceBuilder.Value.ToString("0.####"));
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("重开 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
            R("重开 DistanceBuilder.Value", () => b2.CutParameters.Stepover.DistanceBuilder.Value.ToString("0.####"));
            R("重开 DistanceBuilder.Intent", () => b2.CutParameters.Stepover.DistanceBuilder.Intent.ToString());
        }
        finally { b2.Destroy(); }
    }

    // P5：Planar 对照（mill_planar/PLANAR_MILL，复刻 P2）
    private static void P5(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P5 Planar 对照：PLANAR_MILL 同写 → commit → 重开");
        Operation op = null;
        P("Create (mill_planar, PLANAR_MILL)", () =>
        {
            op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_planar", "PLANAR_MILL", OperationCollection.UseDefaultName.False, "FZ_P5");
        });
        if (op == null) { Note("PLANAR_MILL 创建失败 → P5 SKIP"); return; }
        PlanarMillingBuilder b = cam.CAMOperationCollection.CreatePlanarMillingBuilder(op);
        try
        {
            R("写前 StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            R("写前 PercentToolFlat.Value", () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            P("写 PartStock=0.3 + type=PercentToolFlat + Value=50", () =>
            {
                b.CutParameters.PartStock.Value = 0.3;
                b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0;
            });
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        PlanarMillingBuilder b2 = cam.CAMOperationCollection.CreatePlanarMillingBuilder(op);
        try
        {
            R("重开 PartStock.Value", () => b2.CutParameters.PartStock.Value.ToString("0.####"));
            R("重开 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
            R("重开 PercentToolFlat.Value", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
        }
        finally { b2.Destroy(); }
    }

    // P6：方法组级通道（CreateMillMethodBuilder）
    private static void P6(CAMSetup cam, NCGroup methodRoot)
    {
        Note("-- P6 方法组级通道：CreateMillMethodBuilder(组) 写同链 → commit → 重开");
        NCGroup mg = TryCreateGroup(cam, CAMSetup.View.MachineMethod, "mill_contour", "MILL_METHOD", "FZ_P6_METHOD");
        if (mg == null) { Note("P6 组创建失败，SKIP"); return; }
        NCGroupCollection g = cam.CAMGroupCollection;
        MillMethodBuilder mb = g.CreateMillMethodBuilder(mg);
        try
        {
            R("CutParameters 运行时类型", () => mb.CutParameters.GetType().FullName);
            MillCutParameters mcp = mb.CutParameters as MillCutParameters;
            if (mcp == null) { Note("方法组 CutParameters 非 MillCutParameters（无 Stepover 成员面）→ P6 无通道，记录即止"); return; }
            R("写前 StepoverType", () => mcp.Stepover.StepoverType.ToString());
            R("写前 PercentToolFlat.Value", () => mcp.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            P("写 type=PercentToolFlat + Value=50", () =>
            {
                mcp.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                mcp.Stepover.PercentToolFlatBuilder.Value = 50.0;
            });
            R("Commit", () => { mb.Commit(); return "ok"; });
        }
        finally { mb.Destroy(); }
        MillMethodBuilder mb2 = g.CreateMillMethodBuilder(mg);
        try
        {
            MillCutParameters mcp2 = mb2.CutParameters as MillCutParameters;
            if (mcp2 == null) { Note("重开 CutParameters 非 MillCutParameters"); return; }
            R("重开 StepoverType", () => mcp2.Stepover.StepoverType.ToString());
            R("重开 PercentToolFlat.Value", () => mcp2.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
        }
        finally { mb2.Destroy(); }
    }

    // P7：直接 int 成员（type=Number + NumberOfStepovers）
    private static void P7(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P7 直接 int 成员：type=Number + NumberOfStepovers=4 → commit → 重开");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P7");
        if (op == null) return;
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("写前 StepoverType", () => b.CutParameters.Stepover.StepoverType.ToString());
            R("写前 NumberOfStepovers", () => b.CutParameters.Stepover.NumberOfStepovers.ToString());
            P("写 type=Number + NumberOfStepovers=4", () =>
            {
                b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.Number;
                b.CutParameters.Stepover.NumberOfStepovers = 4;
            });
            R("写后(未Commit) NumberOfStepovers", () => b.CutParameters.Stepover.NumberOfStepovers.ToString());
            R("Commit", () => { b.Commit(); return "ok"; });
        }
        finally { b.Destroy(); }
        CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("重开 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
            R("重开 NumberOfStepovers", () => b2.CutParameters.Stepover.NumberOfStepovers.ToString());
        }
        finally { b2.Destroy(); }
    }

    // P8：暂存 flush 假设（A 写不 commit destroy → B 空 Commit → C 重开）
    private static void P8(CAMSetup cam, NCGroup prog, NCGroup method, NCGroup tool, NCGroup geom)
    {
        Note("-- P8 暂存 flush：A 写 Stepover 50（不 commit，destroy）→ B 空 Commit → 重开");
        Operation op = NewCavity(cam, prog, method, tool, geom, "FZ_P8");
        if (op == null) return;
        CavityMillingBuilder a = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            P("A 写 type=PercentToolFlat + Value=50", () =>
            {
                a.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat;
                a.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0;
            });
            R("A 读(同实例) PercentToolFlat.Value", () => a.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
        }
        finally { a.Destroy(); }
        CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try { R("B 空 Commit", () => { b.Commit(); return "ok"; }); }
        finally { b.Destroy(); }
        CavityMillingBuilder c = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
        try
        {
            R("C 重开 PercentToolFlat.Value", () => c.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
            R("P8 判定", () =>
            {
                double v = c.CutParameters.Stepover.PercentToolFlatBuilder.Value;
                return v == 50.0 ? "暂存 flush 成立（任意后续 Commit flush）" : "暂存 flush 不成立";
            });
        }
        finally { c.Destroy(); }
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

    // BuilderProperties JSON 中某键后 160 字符窗口内首个 "Value":"xxx" 的 xxx 清单（去重保留序）
    private static List<string> ScanJsonValues(CAMObject o, string key, int maxHits)
    {
        var hits = new List<string>();
        string s = o.BuilderProperties;
        if (string.IsNullOrEmpty(s)) { hits.Add("(空)"); return hits; }
        string quoted = "\"" + key + "\"";
        int idx = 0;
        var re = new Regex("\"Value\"\\s*:\\s*\"([^\"]*)\"");
        while (hits.Count < maxHits && (idx = s.IndexOf(quoted, idx, StringComparison.Ordinal)) >= 0)
        {
            int from = idx + quoted.Length;
            int len = Math.Min(160, s.Length - from);
            Match m = re.Match(s, from, len);
            hits.Add(m.Success ? m.Groups[1].Value : "(无Value)");
            idx = from;
        }
        if (hits.Count == 0) hits.Add("(无命中)");
        return hits;
    }

    private static string Join(List<string> items, string sep) { return string.Join(sep, items.ToArray()); }

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
