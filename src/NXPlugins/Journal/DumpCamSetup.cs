// DumpCamSetup.cs — samples/test.prt CAM 结构只读盘点（步骤 0 实证件 #1）
//
// 执行：先 csc 编译成 exe（参考仓库旁批注的编译命令），NX2406 会话 File → Execute → NX Open
//       选该 exe（NX 的 Execute 只接受 dll/exe/jar/class，不直接吃 .cs 源码）。
//       run_journal.exe 批处理参数仍待实测（索引 §3 待验证项 4）。
// 参数：args[0] = prt 路径（缺省 DefaultPart）；args[1] = 输出 txt（缺省与 prt 同目录 <名>.camdump.txt）。
// 只读约束：只调用读取成员（Open/GetRoot/GetMembers/GetNameOfType/Name…），不 Commit、不修改、不保存。
//
// API 事实（2026-09-03 本机反射/XML 实证，见 docs/nx2406-install-index.md §2.1、§2.5）：
//   CAMSetup.CAMGroupCollection / CAMOperationCollection 属性存在；
//   CAMSetup.GetRoot(CAMSetup.View) -> NCGroup；View = ProgramOrder|MachineMethod|Geometry|MachineTool；
//   NCGroup.GetParent() -> NCGroup；NCGroup.GetMembers() -> CAMObject[]（元素为组或操作）；
//   CAMObject.GetNameOfType() -> string —— XML 标注 internal API，仅本 dump 诊断用，
//     返回字面量形态（是否即 Create() 的 typeName）正是本次要实测的对象；
//   subtypeName 无公开读回成员；PartCollection.Open(string, out PartLoadStatus) -> Part（无只读重载）；
//   PartCollection.OpenDisplay(string, out PartLoadStatus) -> Part：打开并显示（Open 只装载；
//     空会话 CAM 查询报"无显示部件"，须用 OpenDisplay）；PartCollection.SetWork(BasePart)
//     设工作部件（2406 无旧版 SetWorkPart）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
// 消歧：NXOpen.CAM.Path 与 System.IO.Path、NXOpen.Operation 与 NXOpen.CAM.Operation 同名
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class DumpCamSetup
{
    // 默认被测件（本仓库样例；换机/移动仓库时用 args[0] 覆盖）
    private const string DefaultPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";

    private static readonly List<string> _lines = new List<string>();
    private static int _opCount;
    private static readonly HashSet<Tag> _seenOps = new HashSet<Tag>(); // 四视图树重复出现同一操作，按 Tag 去重
    private static readonly Dictionary<string, int> _opTypeCount = new Dictionary<string, int>();

    public static void Main(string[] args)
    {
        string partPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultPart;
        string outPath = args.Length > 1 && !string.IsNullOrEmpty(args[1]) ? args[1]
            : Path.Combine(Path.GetDirectoryName(partPath),
                           Path.GetFileNameWithoutExtension(partPath) + ".camdump.txt");
        Session theSession = null;
        Part prevWork = null;
        try
        {
            _lines.Add("== DumpCamSetup ==");
            _lines.Add("part: " + partPath);
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            theSession = Session.GetSession();
            var parts = theSession.Parts;
            try { prevWork = parts.Work; } catch { /* 空会话取 Work 失败，则无需恢复 */ }
            PartLoadStatus loadStatus;
            // OpenDisplay：打开并显示（空会话下 CAM 查询要求有显示部件；Open 只装载不显示）
            Part part = parts.OpenDisplay(partPath, out loadStatus);
            parts.SetWork(part);   // CAM 查询要求活动工作部件

            CAMSetup cam = part.CAMSetup;
            if (cam == null)
            {
                _lines.Add("CAMSetup: NONE —— 部件内无 CAM 数据（与 UI 观察不符则需复核）");
                Finish(outPath);
                return;
            }
            _lines.Add("CAMSetup.GetNameOfType() = " + SafeNameOfType(cam));

            foreach (object v in Enum.GetValues(typeof(CAMSetup.View)))
            {
                CAMSetup.View view = (CAMSetup.View)v;
                NCGroup root = cam.GetRoot(view);
                _lines.Add("");
                _lines.Add("[root] view=" + view + "  " + Describe(root));
                Walk(root, 1);
            }

            _lines.Add("");
            _lines.Add("== 汇总 ==");
            _lines.Add("operations 总数: " + _opCount);
            if (_opCount > 0)
            {
                _lines.Add("按 GetNameOfType 分组:");
                foreach (KeyValuePair<string, int> kv in _opTypeCount)
                    _lines.Add("    " + kv.Key + " × " + kv.Value);
            }
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        finally
        {
            // 只读纪律：恢复原工作部件（文件不落盘改动）
            if (prevWork != null && theSession != null)
            {
                try { theSession.Parts.SetWork(prevWork); }
                catch { /* 恢复失败不阻塞输出 */ }
            }
        }
        Finish(outPath);
    }

    // 递归遍历：成员是 NCGroup 则下钻，是 Operation 则记录
    private static void Walk(NCGroup group, int depth)
    {
        string indent = new string(' ', depth * 2);
        CAMObject[] members;
        try { members = group.GetMembers(); }
        catch (Exception ex) { _lines.Add(indent + "!! GetMembers 失败: " + ex.Message); return; }
        foreach (CAMObject m in members)
        {
            try
            {
                if (m is NCGroup)
                {
                    _lines.Add(indent + "[G] " + Describe(m));
                    Walk((NCGroup)m, depth + 1);
                }
                else if (m is Operation)
                {
                    Operation op = (Operation)m;
                    if (_seenOps.Add(op.Tag))   // 同一操作会在四视图树各出现一次，只统计一次
                    {
                        _opCount++;
                        string t = SafeNameOfType(op);
                        _opTypeCount[t] = _opTypeCount.ContainsKey(t) ? _opTypeCount[t] + 1 : 1;
                    }
                    _lines.Add(indent + "[O] " + Describe(op)
                        + "  parents: prog=" + ParentName(op.ParentProgramOrder)
                        + " tool=" + ParentName(op.ParentMachineTool)
                        + " geom=" + ParentName(op.ParentGeometry)
                        + " method=" + ParentName(op.ParentMachineMethod));
                }
                else
                {
                    _lines.Add(indent + "[?] 未知成员类型: " + m.GetType().FullName + " name=" + m.Name);
                }
            }
            catch (Exception ex)
            {
                _lines.Add(indent + "!! 成员处理异常: " + ex.Message);
            }
        }
    }

    // type=GetNameOfType  name=Name（user=UserName 仅在与 name 不同时附注）
    private static string Describe(CAMObject o)
    {
        string s = "type=" + SafeNameOfType(o) + "  name=" + o.Name;
        if (o.UserName != null && o.UserName != o.Name) s += "  user=" + o.UserName;
        return s;
    }

    private static string SafeNameOfType(CAMObject o)
    {
        try { string t = o.GetNameOfType(); return string.IsNullOrEmpty(t) ? "(empty)" : t; }
        catch (Exception ex) { return "(GetNameOfType 异常: " + ex.Message + ")"; }
    }

    private static string ParentName(NCGroup g)
    {
        return g == null ? "(null)" : g.Name;
    }

    private static void Finish(string outPath)
    {
        try { File.WriteAllLines(outPath, _lines.ToArray()); }
        catch (Exception ex)
        {
            // 输出文件写失败时至少保留现场：追加到系统临时目录
            string fallback = Path.Combine(Path.GetTempPath(), "camdump-fallback.txt");
            try { File.WriteAllLines(fallback, _lines.ToArray()); }
            catch { /* 无处可写时放弃 */ }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fallback);
        }
    }
}
