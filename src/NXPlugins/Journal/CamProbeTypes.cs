// CamProbeTypes.cs — 步骤 0 实证件 #2（第三轮）：CAMSession 模板枚举 + 合法 (typeName,
// subtypeName) 配对建组 + 建 CAVITY_MILL + 四形态读写
//
// 背景（2026-09-03）：组创建 Create* 的 typeName/subtypeName 语义 = 「typeName=模板部件名
// (如 mill_planar) + subtypeName=模板对象类型(如 MILL/BALL_MILL)」；第二轮把对象类型当
// typeName 传故全部"模板不存在"。本件用 CAMSession 枚举全部合法类型/子类型（.NET 反射实证：
// Session.CAMSession / GetTemplateTypes() / GetTemplateSubtypes(type, ObjectSubtype)。
// ObjectSubtype = Setup|Tool|Method|Geometry|Operation|Program。
//
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。内存空 Part，不落盘。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camprobe-types.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeTypes
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-types.txt";
    private static readonly List<string> _lines = new List<string>();

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        try
        {
            _lines.Add("== CamProbeTypes ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Session theSession = Session.GetSession();
            var parts = theSession.Parts;
            Part part = parts.NewDisplay("CamProbeTypes", Part.Units.Millimeters);
            CAMSetup cam = part.CreateCamSetup("mill_contour");
            _lines.Add("CreateCamSetup(mill_contour) ok");

            // ---- CAMSession 初始化 ----
            CAMSession cs = theSession.CAMSession;
            if (theSession.IsCamSessionInitialized())
                _lines.Add("  IsCamSessionInitialized=True");
            else { theSession.CreateCamSession(); _lines.Add("  CreateCamSession() 已调用"); }

            // ---- 枚举模板类型 ----
            string[] types = cs.GetTemplateTypes();
            _lines.Add("");
            _lines.Add("== GetTemplateTypes (" + types.Length + ") ==");
            foreach (string t in types) _lines.Add("  " + t);

            // ---- 枚举各 ObjectSubtype 的子类型 ----
            var subTypes = new Dictionary<string, string[]>();
            foreach (object v in Enum.GetValues(typeof(CAMSession.ObjectSubtype)))
            {
                CAMSession.ObjectSubtype cls = (CAMSession.ObjectSubtype)v;
                foreach (string t in types)
                {
                    try
                    {
                        string[] subs = cs.GetTemplateSubtypes(t, cls);
                        if (subs.Length == 0) continue;
                        _lines.Add("  [" + cls + "] " + t + " -> " + string.Join(", ", subs));
                        subTypes[cls + "\u0001" + t] = subs;
                    }
                    catch (Exception e)
                    {
                        _lines.Add("  [" + cls + "] " + t + " 枚举异常: " + e.Message);
                    }
                }
            }

            // ---- 选型：按偏好取 (type, subtype) 配对 ----
            _lines.Add("");
            _lines.Add("== 选型 ==");
            NCGroupCollection g = cam.CAMGroupCollection;
            CAMSetup.View pv = CAMSetup.View.ProgramOrder, mv = CAMSetup.View.MachineMethod,
                          tv = CAMSetup.View.MachineTool, gv = CAMSetup.View.Geometry;
            Pair progP   = Pick(subTypes, CAMSession.ObjectSubtype.Program,  new[] { "mill_contour", "mill_planar" }, new[] { "PROGRAM", "MAIN" });
            Pair methodP = Pick(subTypes, CAMSession.ObjectSubtype.Method,    new[] { "mill_contour", "mill_planar" }, new[] { "MILL_ROUGH", "MILL_METHOD", "MILL" });
            Pair toolP   = Pick(subTypes, CAMSession.ObjectSubtype.Tool,      new[] { "mill_planar", "mill_contour" }, new[] { "MILL", "BALL_MILL", "MILL_5_PARAMETER" });
            Pair geomP   = Pick(subTypes, CAMSession.ObjectSubtype.Geometry,  new[] { "mill_contour", "mill_planar" }, new[] { "MCS_MILL", "MCS_MAIN", "WORKPIECE" });
            _lines.Add("  Program  -> " + progP);
            _lines.Add("  Method   -> " + methodP);
            _lines.Add("  Tool     -> " + toolP);
            _lines.Add("  Geometry -> " + geomP);

            // ---- 建组 ----
            NCGroup prog = null, method = null, tool = null, geom = null;
            prog   = TryCreate(g, cam.GetRoot(pv), "CreateProgram",  progP, "PROBE_PROG");
            method = TryCreate(g, cam.GetRoot(mv), "CreateMethod", methodP, "PROBE_METHOD");
            tool   = TryCreate(g, cam.GetRoot(tv), "CreateTool",    toolP, "PROBE_TOOL");
            geom   = TryCreate(g, cam.GetRoot(gv), "CreateGeometry", geomP, "PROBE_MCS");

            // ---- 兜底模板自带组 ----
            _lines.Add("");
            _lines.Add("== 兜底 ==");
            if (prog == null)   prog = FindChild(cam.GetRoot(pv), "PROGRAM");
            if (method == null) method = FindChild(cam.GetRoot(mv), "MILL_ROUGH");
            if (method == null) method = FindChild(cam.GetRoot(mv), "MILL_METHOD");
            if (geom == null)   geom = FindChild(cam.GetRoot(gv), "MCS_MILL");
            _lines.Add("  prog=" + NameOrNull(prog) + " method=" + NameOrNull(method)
                       + " tool=" + NameOrNull(tool) + " geom=" + NameOrNull(geom));

            // ---- 建操作（typeName 候选：枚举得到的 Operation 子类型优先）----
            Operation op = null;
            if (prog != null && method != null && tool != null && geom != null)
            {
                _lines.Add("");
                _lines.Add("== Create 操作 ==");
                string[] opCands = OpCandidates(subTypes);
                foreach (string tn in opCands)
                {
                    try
                    {
                        op = cam.CAMOperationCollection.Create(prog, method, tool, geom,
                            tn, "", OperationCollection.UseDefaultName.False, "CAVITY_PROBE");
                        _lines.Add("  OK typeName=" + tn);
                        break;
                    }
                    catch (Exception e) { _lines.Add("  FAIL " + tn + " : " + e.Message); }
                }
            }
            else _lines.Add("  组不全，跳过操作创建");

            // ---- 四形态 + 回读（复用口径）----
            if (op != null)
            {
                _lines.Add("");
                _lines.Add("== 四形态写入/Commit/回读 ==");
                CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try
                {
                    P("形态1 PartStock=0.3", () => b.CutParameters.PartStock.Value = 0.3);
                    P("形态1 DepthPerCut=2.0", () => b.DepthPerCut.Value = 2.0);
                    P("形态2 BoundaryInTol=0.01", () => b.CutParameters.BoundaryInTol = 0.01);
                    P("形态3 CutOrder=LevelFirst",
                        () => b.CutParameters.CutOrder = CutParametersCutOrderTypes.LevelFirst);
                    P("形态4 CutDirection.Type=Climb",
                        () => b.CutParameters.CutDirection.Type = CutDirection.Types.Climb);
                    P("步距链 StepoverType=PercentToolFlat",
                        () => b.CutParameters.Stepover.StepoverType = StepoverBuilder.StepoverTypes.PercentToolFlat);
                    P("步距链 PercentToolFlat.Value=50",
                        () => b.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0);
                    R("Commit", () => { b.Commit(); return "ok"; });
                }
                finally { b.Destroy(); }
                CavityMillingBuilder b2 = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try
                {
                    R("回读 PartStock.Value", () => b2.CutParameters.PartStock.Value.ToString("0.####"));
                    R("回读 CutOrder", () => b2.CutParameters.CutOrder.ToString());
                    R("回读 CutDirection.Type", () => b2.CutParameters.CutDirection.Type.ToString());
                    R("回读 StepoverType", () => b2.CutParameters.Stepover.StepoverType.ToString());
                    R("回读 PercentToolFlat.Value", () => b2.CutParameters.Stepover.PercentToolFlatBuilder.Value.ToString("0.####"));
                    R("回读 FloorStock.Value(未设=继承生效值?)", () => b2.CutParameters.FloorStock.Value.ToString("0.####"));
                }
                finally { b2.Destroy(); }
            }
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    private struct Pair { public string Type; public string Subtype; public string Label; }

    private static Pair Pick(Dictionary<string, string[]> subs, CAMSession.ObjectSubtype cls,
        string[] typePref, string[] subPref)
    {
        // 偏好类型优先：在类型偏好内找含偏好子类型的第一个；无则返回 null 标记
        foreach (string tp in typePref)
        {
            string[] s;
            if (subs.TryGetValue(cls + "\u0001" + tp, out s))
            {
                foreach (string sp in subPref)
                    foreach (string cand in s)
                        if (cand == sp)
                            return new Pair { Type = tp, Subtype = cand, Label = "pref" };
                return new Pair { Type = tp, Subtype = s[0], Label = "first" };
            }
        }
        // 任意类型兜底
        foreach (KeyValuePair<string, string[]> kv in subs)
        {
            if (!kv.Key.StartsWith(cls + "\u0001")) continue;
            string tp = kv.Key.Substring(cls.ToString().Length + 1);
            foreach (string sp in subPref)
                foreach (string cand in kv.Value)
                    if (cand == sp)
                        return new Pair { Type = tp, Subtype = cand, Label = "pref-any" };
            return new Pair { Type = tp, Subtype = kv.Value[0], Label = "first-any" };
        }
        return new Pair { Label = "NONE" };
    }

    private static string[] OpCandidates(Dictionary<string, string[]> subs)
    {
        var list = new List<string>();
        foreach (KeyValuePair<string, string[]> kv in subs)
        {
            if (!kv.Key.StartsWith(CAMSession.ObjectSubtype.Operation + "\u0001")) continue;
            foreach (string s in kv.Value)
                if (s.IndexOf("CAVITY", StringComparison.OrdinalIgnoreCase) >= 0) list.Add(s);
        }
        list.Add("CAVITY_MILL");
        return list.ToArray();
    }

    private static NCGroup TryCreate(NCGroupCollection g, NCGroup root, string kind, Pair p, string name)
    {
        if (root == null) { _lines.Add(kind + " 根组 null"); return null; }
        if (p.Subtype == null) { _lines.Add(kind + " 无配对，跳过"); return null; }
        try
        {
            NCGroup ng;
            switch (kind)
            {
                case "CreateProgram": ng = g.CreateProgram(root, p.Type, p.Subtype, NCGroupCollection.UseDefaultName.False, name); break;
                case "CreateMethod": ng = g.CreateMethod(root, p.Type, p.Subtype, NCGroupCollection.UseDefaultName.False, name); break;
                case "CreateTool": ng = g.CreateTool(root, p.Type, p.Subtype, NCGroupCollection.UseDefaultName.False, name); break;
                default: ng = g.CreateGeometry(root, p.Type, p.Subtype, NCGroupCollection.UseDefaultName.False, name); break;
            }
            _lines.Add("  OK " + kind + " (" + p.Type + "," + p.Subtype + ") -> " + ng.Name);
            return ng;
        }
        catch (Exception e) { _lines.Add("  FAIL " + kind + " (" + p.Type + "," + p.Subtype + ") : " + e.Message); return null; }
    }

    private static NCGroup FindChild(NCGroup parent, string name)
    {
        try
        {
            foreach (CAMObject m in parent.GetMembers())
            {
                NCGroup grp = m as NCGroup;
                if (grp != null && grp.Name == name) { _lines.Add("  找到模板组: " + name); return grp; }
            }
        }
        catch (Exception e) { _lines.Add("  FindChild(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static string NameOrNull(NCGroup ng) { return ng == null ? "(null)" : ng.Name; }

    private static void P(string label, Action act)
    {
        try { act(); _lines.Add("  OK   " + label); }
        catch (Exception e) { _lines.Add("  FAIL " + label + " : " + e.Message); }
    }

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
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-types-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
