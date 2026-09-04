// NxCollect.cs — ExportSnapshot 采集共享层（NX 会话内只读遍历 → 快照）
// 语义：PlanComparer 可信前提 = 导出（ExporterAdapter）与对比（ComparerAdapter）对同一件的
// 采集口径一致（docs/nx-plan-comparer-spec.md §5 D-3：单一事实源）。原为 ExporterAdapter 私有
// 方法（v11 前），2026-09-04 提取共享；刀具入选判据 2026-09-04 首跑修正为 as Tool 下转（原
// depth>=1+家族串排除漏采重建件机根直挂刀组，见 ComparerAdapter 首跑 comparer-run-141713）。
// 只读纪律：不 Commit/不修改；Builder 用毕 Destroy。日志经注入 Action<string>（适配器各自通道）。

using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using NXPlugins.PlanExporter;
using Operation = NXOpen.CAM.Operation;

public static class NxCollect
{
    // ---- 采集：机床树刀具（含 U-7 GetTypeAndSubtype 直写；失败 → TypeReadbackError 不入 plan） ----

    public static void CollectTools(CAMSetup cam, ExportSnapshot snap, Action<string> log)
    {
        NCGroup root = cam.GetRoot(CAMSetup.View.MachineTool);
        WalkTools(root, snap, cam, log);
    }

    // 入选判据 = as NXOpen.CAM.Tool 成败（2026-09-04 首跑实证修正：重建件刀组挂在机床根直接层，
    // 原 depth>=1+家族串排除判据漏采 → 改为语言无关、深度无关的 Tool 下转判据；容器组
    // （Machine/Carrier/Head/机床根）as Tool 恒 null → 递归其子，真刀不再递归）
    private static void WalkTools(NCGroup g, ExportSnapshot snap, CAMSetup cam, Action<string> log)
    {
        foreach (CAMObject m in SafeMembers(g, log))
        {
            NCGroup sub = m as NCGroup;
            if (sub == null) continue;
            NXOpen.CAM.Tool tt = sub as NXOpen.CAM.Tool;
            if (tt == null) { WalkTools(sub, snap, cam, log); continue; }   // 容器 → 递归
            // 真刀：GetTypeAndSubtype 直写 NX 枚举原文（PRE-U7-1）；失败 → 剔除此刀（INV-U7-4）
            var t = new ToolItem { Name = sub.Name, TypeFamily = NameOfTypeSafe(sub) };
            try
            {
                NXOpen.CAM.Tool.Types ty;
                NXOpen.CAM.Tool.Subtypes st;
                tt.GetTypeAndSubtype(out ty, out st);
                t.NxType = ty.ToString();
                t.NxSubtype = st.ToString();
                log("  tool " + sub.Name + " → type=" + t.NxType + " subtype=" + t.NxSubtype);
            }
            catch (Exception e) { t.TypeReadbackError = "GetTypeAndSubtype 异常: " + e.Message; }
            ReadToolParams(cam, sub, t, log);
            snap.Tools.Add(t);
        }
    }

    private static void ReadToolParams(CAMSetup cam, NCGroup toolGroup, ToolItem t, Action<string> log)
    {
        try
        {
            MillingToolBuilder b = null;
            try { b = cam.CAMGroupCollection.CreateMillToolBuilder(toolGroup) as MillingToolBuilder; }
            catch { b = null; }
            if (b == null)
            {
                try { b = cam.CAMGroupCollection.CreateDrillStdToolBuilder(toolGroup) as MillingToolBuilder; }
                catch { b = null; }
            }
            if (b == null) { t.TypeFamily += " (参数未读: builder 不匹配)"; return; }
            try { t.Diameter = b.TlDiameterBuilder.Value; } catch { }
            try { t.NumFlutes = b.TlNumFlutesBuilder.Value; } catch { }
            try { t.FluteLength = b.TlFluteLnBuilder.Value; } catch { }
            try { t.LowerCornerRadius = b.TlLowCorRadBuilder.Value; } catch { }
            b.Destroy();
        }
        catch (Exception e) { t.TypeFamily += " (参数异常: " + e.Message + ")"; }
    }

    // ---- 采集：MCS（几何树中名字含 MCS 的组） ----

    public static void CollectSetups(CAMSetup cam, ExportSnapshot snap, Action<string> log)
    {
        NCGroup root = cam.GetRoot(CAMSetup.View.Geometry);
        NCGroup mcs = FindMcs(root, log);
        var s = new SetupItem { Name = mcs == null ? "UNKNOWN" : mcs.Name, MissingMcs = mcs == null };
        if (mcs != null)
        {
            try
            {
                MillOrientGeomBuilder ob = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(mcs);
                try
                {
                    CartesianCoordinateSystem cs = ob.Mcs;
                    if (cs != null)
                    {
                        s.McsOrigin = new[] { cs.Origin.X, cs.Origin.Y, cs.Origin.Z };
                        Matrix3x3 el = cs.Orientation.Element;
                        s.McsXAxis = new[] { el.Xx, el.Xy, el.Xz };
                        s.McsZAxis = new[] { el.Zx, el.Zy, el.Zz };
                        log(string.Format("MCS 回读: origin=({0:0.###},{1:0.###},{2:0.###}) z=({3:0.###},{4:0.###},{5:0.###})",
                            cs.Origin.X, cs.Origin.Y, cs.Origin.Z, el.Zx, el.Zy, el.Zz));
                    }
                }
                finally { ob.Destroy(); }
            }
            catch (Exception e) { log("MCS 回读异常: " + e.Message); }
        }
        // FixtureOffset（P4 实证可读：MillOrientGeomBuilder.FixtureOffsetBuilder.Value；2026-09-04
        // comparer 首跑补——plan 的 fixture_offset null 缺口同源，导出随之带出）
        if (mcs != null && !s.MissingMcs)
        {
            try
            {
                MillOrientGeomBuilder ob = cam.CAMGroupCollection.CreateMillOrientGeomBuilder(mcs);
                try { s.FixtureOffset = ob.FixtureOffsetBuilder.Value; }
                finally { ob.Destroy(); }
            }
            catch (Exception e) { log("FixtureOffset 回读异常: " + e.Message); }
        }
        snap.Setups.Add(s);
    }

    private static NCGroup FindMcs(NCGroup g, Action<string> log)
    {
        foreach (CAMObject m in SafeMembers(g, log))
        {
            NCGroup sub = m as NCGroup;
            if (sub == null) continue;
            if (sub.Name.StartsWith("MCS", StringComparison.Ordinal)) return sub;
            NCGroup hit = FindMcs(sub, log);
            if (hit != null) return hit;
        }
        return null;
    }

    // ---- 采集：操作 + 程序组树（程序顺序视图，单视图；Tag 即唯一键） ----
    // v1.5-①（2026-09-04）：ProgramTree = 程序组树真实嵌套（组内成员序 = GetMembers 序，保刀路输出
    // 序）；顶层 op（NC_PROGRAM 直接层）不进树 → exporter 兜底挂 root。ProgramOrder 保留顶层组名序
    // （comparer 顶层组序比对口径）。NONE 组不进树/序，其下 op 仍收集（v1 parity：挂 root 兜底）。

    public static void CollectOperations(CAMSetup cam, ExportSnapshot snap, Action<string> log)
    {
        NCGroup root = cam.GetRoot(CAMSetup.View.ProgramOrder);
        foreach (CAMObject m in SafeMembers(root, log))
        {
            NCGroup sub = m as NCGroup;
            if (sub == null)
            {
                Operation topOp = m as Operation;                    // NC_PROGRAM 直接层 op
                if (topOp != null) CollectOperation(topOp, snap, cam, log);
                continue;
            }
            if (sub.Name == "NONE") { WalkNoneOps(sub, snap, cam, log); continue; }   // parity
            string fam = NameOfTypeSafe(sub);
            if (fam != "Generic PARAM object") continue;             // 程序组大类（既有口径）
            snap.ProgramOrder.Add(sub.Name);
            snap.ProgramTree.Add(BuildProgramNode(sub, snap, cam, log));
        }
    }

    private static void WalkNoneOps(NCGroup noneGroup, ExportSnapshot snap, CAMSetup cam, Action<string> log)
    {
        foreach (CAMObject m in SafeMembers(noneGroup, log))
        {
            NCGroup sub = m as NCGroup;
            if (sub != null) { WalkNoneOps(sub, snap, cam, log); continue; }
            Operation op = m as Operation;
            if (op != null) CollectOperation(op, snap, cam, log);
        }
    }

    private static ProgramNode BuildProgramNode(NCGroup g, ExportSnapshot snap, CAMSetup cam, Action<string> log)
    {
        var node = new ProgramNode { Name = g.Name };
        foreach (CAMObject m in SafeMembers(g, log))
        {
            NCGroup sub = m as NCGroup;
            if (sub != null)
            {
                node.Members.Add(new ProgramMember { IsOperation = false, Group = BuildProgramNode(sub, snap, cam, log) });
                continue;
            }
            Operation op = m as Operation;
            if (op != null)
            {
                OperationItem o = CollectOperation(op, snap, cam, log);
                node.Members.Add(new ProgramMember
                { IsOperation = true, OpName = op.Name, OpKey = o.Key });
            }
        }
        return node;
    }

    private static OperationItem CollectOperation(Operation op, ExportSnapshot snap, CAMSetup cam, Action<string> log)
    {
        var o = new OperationItem
        {
            Name = op.Name,
            UserName = op.UserName ?? "",
            Key = new TagKey((ulong)(long)(int)op.Tag),
            TypeFamily = NameOfTypeSafe(op),
            ProgramParent = ParentName(op.ParentProgramOrder),
            MethodParent = ParentName(op.ParentMachineMethod),
            ToolParent = ParentName(op.ParentMachineTool),
            GeometryParent = ParentName(op.ParentGeometry),
            HasGeometryParent = op.ParentGeometry != null,
        };
        if (o.TypeFamily == "Cavity Milling")
        {
            try
            {
                CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try
                {
                    // v1.5-③ S1（注册表 #1-8）：3 枚举直读 NX ToString 原文 + 3 数值 + rpm
                    TryParamS(b, o, "cut_pattern", () => b.CutPattern.CutPattern.ToString());
                    TryParamS(b, o, "cut_order", () => b.CutParameters.CutOrder.ToString());
                    TryParamS(b, o, "cut_direction", () => b.CutParameters.CutDirection.Type.ToString());
                    TryParam(b, o, "finish_passes", () => (double)b.CutParameters.FinishPasses.NumberOfFinishPasses);
                    TryParam(b, o, "boundary_intol", () => b.CutParameters.BoundaryInTol);
                    TryParam(b, o, "boundary_outtol", () => b.CutParameters.BoundaryOutTol);
                    TryParam(b, o, "tech:spindle_rpm", () => b.FeedsBuilder.SpindleRpmBuilder.Value);
                    TryParam(b, o, "part_stock", () => b.CutParameters.PartStock.Value);
                    TryParam(b, o, "floor_stock", () => b.CutParameters.FloorStock.Value);
                    TryParam(b, o, "depth_per_cut", () => b.DepthPerCut.Value);
                }
                finally { b.Destroy(); }
            }
            catch (Exception e) { o.ReadbackErrors.Add("cavity builder 打不开: " + e.Message); }
        }
        else if (o.TypeFamily == "Drilling")
        {
            // 新模板 DRILLING 家族 → HoleDrillingBuilder（camprobe-drill 实证 BottomStock 可读；
            // FeedsBuilder 经 HoleMachiningBuilder 基类可达，rpm 读回 [I] I-3 点亮）
            try
            {
                HoleDrillingBuilder b = cam.CAMOperationCollection.CreateHoleDrillingBuilder(op);
                try
                {
                    TryParam(b, o, "bottom_stock", () => b.CuttingParameters.BottomStock.Value);
                    TryParam(b, o, "tech:spindle_rpm", () => b.FeedsBuilder.SpindleRpmBuilder.Value);
                }
                finally { b.Destroy(); }
            }
            catch (Exception e) { o.ReadbackErrors.Add("hole builder 打不开: " + e.Message); }
        }
        else if (o.TypeFamily == "Point to Point")
        {
            // PTP 旧模板（打点/钻头G83）→ PointToPointBuilder（2406 实证：CreateHoleDrillingBuilder
            // 会类型转换失败）；参数面仅 HoleDepth/Retract 等（孔细分参数面属 #3 范围，不扩读）
            try
            {
                PointToPointBuilder b = cam.CAMOperationCollection.CreatePointToPointBuilder(op);
                try
                {
                    TryParam(b, o, "hole_depth", () => b.HoleDepth.Value);
                    // v1.5-③ S1：rpm 读（探针实证打点 3000 / G83 500）→ plan 供给重建近似 DRILLING 写 rpm
                    TryParam(b, o, "tech:spindle_rpm", () => b.FeedsBuilder.SpindleRpmBuilder.Value);
                    log("  PTP op " + o.Name + " 参数面细分待后续批（cycle/细分 U-1 负证；rpm 已扩读）");
                }
                finally { b.Destroy(); }
            }
            catch (Exception e) { o.ReadbackErrors.Add("ptp builder 打不开: " + e.Message); }
        }
        snap.Operations.Add(o);
        return o;
    }

    private static void TryParam(object builder, OperationItem o, string key, Func<double> getter)
    {
        try { o.Params[key] = new ParamValue(getter()); }
        catch (Exception e) { o.ReadbackErrors.Add("参数 " + key + " 回读失败: " + e.Message); }
    }

    // v1.5-③：枚举键读 NX 枚举 ToString 原文（词 = schema 词集，语言无关）
    private static void TryParamS(object builder, OperationItem o, string key, Func<string> getter)
    {
        try { o.Params[key] = new ParamValue(getter()); }
        catch (Exception e) { o.ReadbackErrors.Add("参数 " + key + " 回读失败: " + e.Message); }
    }

    // ---- 工具 ----

    private static CAMObject[] SafeMembers(NCGroup g, Action<string> log)
    {
        try { return g.GetMembers(); }
        catch (Exception e) { if (log != null) log("GetMembers 失败(" + g.Name + "): " + e.Message); return new CAMObject[0]; }
    }


    private static string NameOfTypeSafe(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch { return "(unknown)"; }
    }

    private static string ParentName(NCGroup g) { return g == null ? "" : g.Name; }
}
