// CamProbeV2BpDiff.cs — OP-003 空刀路判别实验 2：BuilderProperties 双档 JSON dump（2026-09-05，
// run_journal 批处理，单件模式）
//
// 背景：判别实验 1（camprobe-v2regen）已排除会话态/生成顺序（新会话单独/按序重生成均确定性空）；
// 判别读探针已排除集属性/DPC/feed。本探针 = 对 gt 与 rebuilt 各 dump 目标 op 的
// BuilderProperties（= 已提交态全参数 JSON，P1 快照语义已证，两件均已 Save）：
//   gt:  OP-001（出刀路对照）+ OP-003（出刀路 8.03s）
//   reb: OP-001（出刀路对照）+ OP-003（空刀路 0）
// 离线 python diff：剔除含 Tag/Handle 的易失路径后，报告两侧差异叶路径（gt diff 集 = 无害差异
// 基准，减去后剩余 = OP-003 零化候选）。
// 只读纪律：不 Save。用法：OS 环境变量 CAMSIG_BP_PRT = prt 路径；CAMSIG_BP_LABEL = gt|reb
// （缺省 gt/test.prt）。输出：samples\camprobe-v2bp-<label>-<ts>.txt（每 op 段含 BuilderProperties 原文）。

using System;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeV2BpDiff
{
    private static string _out;

    public static void Main(string[] args)
    {
        string prt = System.Environment.GetEnvironmentVariable("CAMSIG_BP_PRT");
        if (string.IsNullOrEmpty(prt)) prt = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
        string label = System.Environment.GetEnvironmentVariable("CAMSIG_BP_LABEL");
        if (string.IsNullOrEmpty(label)) label = "gt";
        _out = Path.Combine(@"C:\Users\21505\Code\nx-vibe-v2\samples",
            "camprobe-v2bp-" + label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        Log("== CamProbeV2BpDiff（BuilderProperties dump）==");
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

            string[] targets = { "CAVITY_MILL", "CAVITY_MILL_COPY_COPY" };
            foreach (string nm in targets)
            {
                Operation op = FindOp(cam, cam.GetRoot(CAMSetup.View.ProgramOrder), nm);
                if (op == null) { Log("!! 找不到 " + nm); continue; }
                Log("");
                Log("== op: " + op.Name + "  pathT=" + op.GetToolpathTime().ToString("0.####"));
                try
                {
                    string bp = op.BuilderProperties;
                    Log("BP length=" + (bp == null ? "null" : bp.Length.ToString()));
                    Log("==BPJSON_BEGIN==");
                    if (bp != null) LogRaw(bp);
                    Log("==BPJSON_END==");
                }
                catch (Exception e) { Log("BP 读异常: " + e.GetType().Name + " " + e.Message); }
            }
        }
        catch (Exception ex)
        {
            Log("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) Log("   inner: " + ex.InnerException.Message);
        }
        Log("== 结束 ==");
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

    private static void LogRaw(string s)
    {
        try { File.AppendAllText(_out, s + Environment.NewLine); }
        catch { }
    }
}
