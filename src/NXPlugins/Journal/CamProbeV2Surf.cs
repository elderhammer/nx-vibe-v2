// CamProbeV2Surf.cs — OP-003 空刀路判别实验 2b：同 op builder 深面反射 dump（2026-09-05，
// run_journal 批处理，单件模式）
//
// 背景：BuilderProperties 双档实验 2 失效——同件内 OP-001 与 OP-003 的 JSON 逐字节相同
// （OP-003 无独立参数快照）→ BP JSON 非逐 op 生效态（实证修正，回填索引 §2.1）。
// 本探针 = 对每腔 op 的 CavityMillingBuilder 做递归反射 surface dump（不手写成员表）：
//   叶子（enum/数值/string/bool/struct）直读；builder/参数对象属性白名单递归（限深 3），
//   跳过 Geometry/List 族（面集已在判别读探针覆盖）。
// 两件各跑（gt / v2.rebuilt-191437），4 个腔 op 全录 → 离线 diff 找 OP-003 专属差异：
//   ① 件内 OP-003 vs 兄弟 op（executor 未复刻的逐 op 设置候选）；
//   ② 件间 OP-003 diff ⊖ 件间 OP-001 diff（滤掉模板默认族差）。
// 只读纪律：不 Save。
// 用法：OS 环境变量 CAMSIG_SURF_PRT / CAMSIG_SURF_LABEL。输出：samples\camprobe-v2surf-<label>-<ts>.txt。

using System;
using System.IO;
using System.Reflection;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2Surf
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_SURF_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        string label = System.Environment.GetEnvironmentVariable("CAMSIG_SURF_LABEL");
        if (string.IsNullOrEmpty(label)) label = "gt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2surf-" + label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2Surf（腔 op builder 深面反射 dump）==");
        Log("label=" + label + "  件: " + prt);
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

            CAMSetup cam = p.CAMSetup;
            if (cam == null) throw new Exception("无 CAMSetup");

            string[] order = { "CAVITY_MILL", "CAVITY_MILL_COPY", "CAVITY_MILL_COPY_COPY", "CAVITY_MILL_COPY_COPY_COPY" };
            foreach (string nm in order)
            {
                Operation op = FindOp(cam, cam.GetRoot(CAMSetup.View.ProgramOrder), nm);
                if (op == null) { Log("!! 找不到 " + nm); continue; }
                Log("");
                Log("== op: " + op.Name + "  pathT=" + op.GetToolpathTime().ToString("0.####"));
                CavityMillingBuilder b = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
                try { DumpObj(b, "b", 0); }
                finally { b.Destroy(); }
            }
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

    private static void DumpObj(object o, string name, int depth)
    {
        if (depth > 3 || o == null) return;
        Type t = o.GetType();
        Log(Ind(depth) + "[" + name + "] : " + t.Name);
        foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (pi.GetIndexParameters().Length > 0) continue;
            string pn = pi.Name;
            if (pn == "Tag" || pn.Contains("Handle")) continue;
            object v;
            try { v = pi.GetValue(o, null); }
            catch (Exception e) { Log(Ind(depth + 1) + "~ " + pn + " = !!ERR " + e.GetType().Name); continue; }
            if (v == null) { Log(Ind(depth + 1) + "~ " + pn + " = null"); continue; }
            Type vt = v.GetType();
            if (vt.IsEnum || vt == typeof(string) || vt == typeof(double) || vt == typeof(bool)
                || vt == typeof(int) || vt.IsPrimitive)
            {
                string sv = (v is double) ? ((double)v).ToString("0.####") : v.ToString();
                Log(Ind(depth + 1) + "~ " + pn + " = " + sv);
            }
            else if (v is NXOpen.TaggedObject)
            {
                if (Recurse(pn, vt.Name))
                    DumpObj(v, pn, depth + 1);
                else
                    Log(Ind(depth + 1) + "~ " + pn + " = [" + vt.Name + "]");
            }
            else if (v is System.Collections.IEnumerable)
            {
                int n = 0;
                foreach (object e in (System.Collections.IEnumerable)v) { n++; if (n > 9) break; }
                Log(Ind(depth + 1) + "~ " + pn + " = [IEnumerable n>=" + n + "]");
            }
            else
            {
                string sv = v.ToString();
                Log(Ind(depth + 1) + "~ " + pn + " = " + (sv.Length > 150 ? sv.Substring(0, 150) + "…" : sv));
            }
        }
    }

    private static string Ind(int d)
    {
        return d == 0 ? "" : new string(' ', d * 2);
    }

    private static Operation FindOp(CAMSetup cam, NCGroup node, string name)
    {
        foreach (CAMObject m in node.GetMembers())
        {
            Operation op = m as Operation;
            if (op != null && op.Name == name) return op;
            NCGroup sub = m as NCGroup;
            if (sub != null)
            {
                Operation hit = FindOp(cam, sub, name);
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
