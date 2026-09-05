// CamProbeV2SurfDiff.cs — 区域配对研究批判别 2：gt 件内 OP-002 本体 vs 克隆 深反射 diff
//（2026-09-06，run_journal 批处理，单件内存态不 Save）
//
// 背景：camprobe-v2regionclone（002142/002206）四值矩阵定案——gt 侧同参同面克隆 = 27.4s/119 区
// （≈ rebuilt 118 区），gt 工程师本体却 = 0.515s/36 区（每区 2.47mm² 窄环）→ OP-002 差异非体上下文，
// 本体存在白名单复刻面之外的隐藏参数使 Profile 只走窄轮廓（可修候选，区别于 OP-003 真 γ）。
// 本探针 = 同件同会话：建克隆（regionclone 同款：OP-002 6 面 + Profile/0.2/3000/1200 + 父链锚点）→
// 对**本体与克隆**的 CavityMillingBuilder 各做深反射 dump（surf 同款递归，收集行缓冲）→ 行级 diff：
//   仅本体有 / 仅克隆有 / 同行异值 → 暴露隐藏差异键（白名单外的逐 op 参数面/集级属性）。
// 只读纪律：不 Save（克隆退出即弃）。输出：samples\camprobe-v2surfdiff-<ts>.txt。
// 件 = CAMSIG_SURF_PRT（默认 samples\test.prt——本体差异最大侧；rebuilt 侧克隆=本体已证无差）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2SurfDiff
{
    private static string _out;
    private static CAMSetup _cam;
    private static readonly List<string> _buf = new List<string>();

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_SURF_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2surfdiff-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2SurfDiff（OP-002 本体 vs 克隆 深反射 diff）==");
        Log("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Log("件: " + prt);
        try
        {
            Session s = Session.GetSession();
            UFSession uf = UFSession.GetUFSession();
            PartLoadStatus st;
            Part p = s.Parts.OpenDisplay(prt, out st);
            s.Parts.SetWork(p);
            uf.Part.SetDisplayPart(p.Tag);
            if (!s.IsCamSessionInitialized()) s.CreateCamSession();
            Log("打开 OK: " + p.Name + "  camSession=" + s.IsCamSessionInitialized());
            _cam = p.CAMSetup;
            if (_cam == null) throw new Exception("无 CAMSetup");

            Operation op2 = FindOp(_cam.GetRoot(CAMSetup.View.ProgramOrder), "CAVITY_MILL_COPY");
            if (op2 == null) throw new Exception("找不到 OP-002");

            // 克隆（regionclone 同款）
            TaggedObject[] f6 = null;
            CavityMillingBuilder bg = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try
            {
                NXOpen.CAM.Geometry cag = bg.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("本体无几何集");
                f6 = cag.GeometryList.FindItem(0).GetItems();
            }
            finally { bg.Destroy(); }
            NCGroup prog = op2.ParentProgramOrder, method = op2.ParentMachineMethod;
            NCGroup tool = op2.ParentMachineTool, geom = op2.ParentGeometry;
            Operation neu = _cam.CAMOperationCollection.Create(prog, method, tool, geom,
                "mill_contour", "CAVITY_MILL", OperationCollection.UseDefaultName.True, "REGIONCLONE_OP002");
            CavityMillingBuilder bn = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try
            {
                bn.CutPattern.CutPattern = CutPatternBuilder.Types.Profile;
                bn.CutParameters.CutOrder = CutParametersCutOrderTypes.DepthFirst;
                bn.CutParameters.CutDirection.Type = CutDirection.Types.Climb;
                bn.CutParameters.FinishPasses.NumberOfFinishPasses = 0;
                bn.CutParameters.PartStock.Value = 0;
                bn.CutParameters.FloorStock.Value = 0;
                NXOpen.CAM.CutLevel cl = bn.CutLevel;
                if (cl != null) cl.GlobalDepthPerCut.DistanceBuilder.Value = 0.2;
                bn.FeedsBuilder.SpindleRpmBuilder.Value = 3000;
                bn.FeedsBuilder.FeedCutBuilder.Value = 1200;
                NXOpen.CAM.Geometry cag = bn.CutAreaGeometry;
                if (cag == null || cag.GeometryList.Length == 0) throw new Exception("克隆无默认几何集");
                cag.GeometryList.FindItem(0).Selection.SetArray(f6);
                bn.Commit();
            }
            finally { bn.Destroy(); }
            Log("克隆创建 + 参数复刻 + 面指派 6 → ok (" + neu.Name + ")");

            // 双段 dump（同一 DumpObj 递归逻辑 → 键序一致）
            List<string> dumpA = new List<string>();
            CavityMillingBuilder bA = _cam.CAMOperationCollection.CreateCavityMillingBuilder(op2);
            try { DumpObj(bA, "b", 0, dumpA); } finally { bA.Destroy(); }
            List<string> dumpB = new List<string>();
            CavityMillingBuilder bB = _cam.CAMOperationCollection.CreateCavityMillingBuilder(neu);
            try { DumpObj(bB, "b", 0, dumpB); } finally { bB.Destroy(); }
            Log("段行数: 本体=" + dumpA.Count + " 克隆=" + dumpB.Count);

            // 行级 diff（同序逐索引 + 差值行统计）
            Log("");
            Log("== diff（A=本体 gt 工程师件  B=克隆 API 新建）==");
            int diffCount = 0;
            int n = Math.Max(dumpA.Count, dumpB.Count);
            for (int i = 0; i < n; i++)
            {
                string la = i < dumpA.Count ? dumpA[i] : "(A 无此行)";
                string lb = i < dumpB.Count ? dumpB[i] : "(B 无此行)";
                if (la == lb) continue;
                diffCount++;
                Log("  [" + i + "] A: " + la);
                Log("  [" + i + "] B: " + lb);
            }
            Log("== diff 行数=" + diffCount + " ==");
            Log("== 结束（全程未 Save）==");
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
    }

    private static bool Recurse(string pname, string tname)
    {
        if (pname.Contains("Geometry") || pname.Contains("List")) return false;   // 面集/列表：判别读已覆盖
        if (pname == "Mcs") return false;                                          // csys：回读已覆盖
        if (tname.EndsWith("Builder")) return true;
        if (tname == "CutParameters" || tname == "FeedsBuilder" || tname == "CutLevel"
            || tname == "Stepover" || tname == "MultiDepthCut" || tname == "CutPattern") return true;
        return false;
    }

    private static void DumpObj(object o, string name, int depth, List<string> buf)
    {
        if (depth > 3 || o == null) return;
        Type t = o.GetType();
        buf.Add(Ind(depth) + "[" + name + "] : " + t.Name);
        foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (pi.GetIndexParameters().Length > 0) continue;
            string pn = pi.Name;
            if (pn == "Tag" || pn.Contains("Handle")) continue;
            object v;
            try { v = pi.GetValue(o, null); }
            catch (Exception e) { buf.Add(Ind(depth + 1) + "~ " + pn + " = !!ERR " + e.GetType().Name); continue; }
            if (v == null) { buf.Add(Ind(depth + 1) + "~ " + pn + " = null"); continue; }
            Type vt = v.GetType();
            if (vt.IsEnum || vt == typeof(string) || vt == typeof(double) || vt == typeof(bool)
                || vt == typeof(int) || vt.IsPrimitive)
            {
                string sv = (v is double) ? ((double)v).ToString("0.####") : v.ToString();
                buf.Add(Ind(depth + 1) + "~ " + pn + " = " + sv);
            }
            else if (v is NXOpen.TaggedObject)
            {
                if (Recurse(pn, vt.Name))
                    DumpObj(v, pn, depth + 1, buf);
                else
                    buf.Add(Ind(depth + 1) + "~ " + pn + " = [" + vt.Name + "]");
            }
            else if (v is System.Collections.IEnumerable)
            {
                int cnt = 0;
                foreach (object e in (System.Collections.IEnumerable)v) { cnt++; if (cnt > 9) break; }
                buf.Add(Ind(depth + 1) + "~ " + pn + " = [IEnumerable n>=" + cnt + "]");
            }
            else
            {
                string sv = v.ToString();
                buf.Add(Ind(depth + 1) + "~ " + pn + " = " + (sv.Length > 150 ? sv.Substring(0, 150) + "…" : sv));
            }
        }
    }

    private static string Ind(int d)
    {
        return d == 0 ? "" : new string(' ', d * 2);
    }

    private static Operation FindOp(NCGroup node, string name)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.Name == name) return op;
            NCGroup sub = m as NCGroup;
            if (sub != null)
            {
                Operation hit = FindOp(sub, name);
                if (hit != null) return hit;
            }
        }
        return null;
    }

    private static void Log(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
