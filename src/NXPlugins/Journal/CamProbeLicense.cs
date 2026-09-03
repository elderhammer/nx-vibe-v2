// CamProbeLicense.cs — 步骤 0 实证件 #4：许可探测原型（风险 #5 缓解验证）
// 做法：① 从 NXOpen.xml remarks 提取关键成员的 "License requirements:" 注记（预期许可）；
//      ② 运行时用 LicenseManager.Reserve/CheckPresence/IsCheckedOut 逐许可验证本机可用性；
//      ③ 汇总对照表。镜像 nxopen-research 附 B 验证计划 #7。
// API 事实（2026-09-03 反射实证）：Session.LicenseManager : NXOpen.LicenseManager；
// Reserve(string license, string contextName)/Release；CheckPresence(string);IsCheckedOut(string)。
// 执行：NX2406 会话 File → Execute → NX Open（编译后 exe）。无需部件。
// 参数：args[0] = 输出 txt（缺省 <仓库>\samples\camprobe-license.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NXOpen;
using Path = System.IO.Path;

public class CamProbeLicense
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-license.txt";
    private const string NxOpenXml = @"C:\Program Files\Siemens\NX2406\NXBIN\managed\NXOpen.xml";
    private const string Ctx = "CamProbeLicense";
    private static readonly List<string> _lines = new List<string>();

    // 待查成员（XML member name 前缀；用首个命中块）
    private static readonly string[][] Members = {
        new[] { "OperationCollection.Create",              "M:NXOpen.CAM.OperationCollection.Create(" },
        new[] { "NCGroupCollection.CreateTool",            "M:NXOpen.CAM.NCGroupCollection.CreateTool(" },
        new[] { "CAMSetup.CreateFeatureProcessBuilder",    "M:NXOpen.CAM.CAMSetup.CreateFeatureProcessBuilder(" },
        new[] { "CAMSetup.GenerateToolPath",               "M:NXOpen.CAM.CAMSetup.GenerateToolPath(" },
        new[] { "OperationCollection.CreateHoleDrillingBuilder", "M:NXOpen.CAM.OperationCollection.CreateHoleDrillingBuilder(" },
        new[] { "OperationCollection.CreateCavityMillingBuilder", "M:NXOpen.CAM.OperationCollection.CreateCavityMillingBuilder(" },
        new[] { "CAMSetup.GougeCheck",                     "M:NXOpen.CAM.CAMSetup.GougeCheck(" },
    };

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        var memberLicense = new Dictionary<string, string>();
        try
        {
            _lines.Add("== CamProbeLicense ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            // ---- ① 解析 NXOpen.xml ----
            _lines.Add("== ① NXOpen.xml remarks 提取 ==");
            ParseLicenseRequirements(memberLicense);
            foreach (KeyValuePair<string, string> kv in memberLicense)
                _lines.Add("  " + kv.Key + " -> " + (string.IsNullOrEmpty(kv.Value) ? "(无注记)" : kv.Value));

            // ---- ② 运行时验证 ----
            _lines.Add("");
            _lines.Add("== ② LicenseManager 逐许可验证 ==");
            var licenses = new List<string>();
            foreach (string v in memberLicense.Values)
            {
                foreach (Match m in Regex.Matches(v, @"[a-z][a-z0-9_]+"))
                    if (!licenses.Contains(m.Value)) licenses.Add(m.Value);
            }
            LicenseManager lm = Session.GetSession().LicenseManager;
            foreach (string lic in licenses)
            {
                bool present = false, checkedOut = false, reserveOk = false;
                try { present = lm.CheckPresence(lic); } catch (Exception e) { _lines.Add("  CheckPresence(" + lic + ") 异常: " + e.Message); }
                try { checkedOut = lm.IsCheckedOut(lic); } catch (Exception e) { _lines.Add("  IsCheckedOut(" + lic + ") 异常: " + e.Message); }
                try { lm.Reserve(lic, Ctx); reserveOk = true; } catch (Exception e) { _lines.Add("  Reserve(" + lic + ") 失败: " + e.Message); }
                finally { try { lm.Release(lic, Ctx); } catch { } }
                _lines.Add("  license=" + lic + "  CheckPresence=" + present + "  IsCheckedOut=" + checkedOut + "  Reserve=" + (reserveOk ? "OK" : "不可用"));
            }

            // ---- ③ 汇总 ----
            _lines.Add("");
            _lines.Add("== ③ 汇总（成员 → 注记许可 → 本机可用性）==");
            foreach (KeyValuePair<string, string> kv in memberLicense)
                _lines.Add("  " + kv.Key + " | " + (string.IsNullOrEmpty(kv.Value) ? "-" : kv.Value) + " | 见上逐许可结果");
            _lines.Add("");
            _lines.Add("结论口径：若 cam_base Reserve=OK 而 ug_holemaking 不可用，则 CreateFeatureProcessBuilder 应报许可错误——验证风险#5 缓解可行。");
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    // XML member name 前缀匹配；部分成员名无参数括（如 ...CreateFeatureProcessBuilder）→ 去掉尾部 "(" 再试
    private static bool Matches(string line, string key)
    {
        if (line.IndexOf(key, StringComparison.Ordinal) >= 0) return true;
        return key.EndsWith("(", StringComparison.Ordinal)
            && line.IndexOf(key.Substring(0, key.Length - 1), StringComparison.Ordinal) >= 0;
    }

    // 按前缀找 member 块，取其 remarks 内 "License requirements:" 行
    private static void ParseLicenseRequirements(Dictionary<string, string> result)
    {
        if (!File.Exists(NxOpenXml)) { _lines.Add("  NXOpen.xml 不存在: " + NxOpenXml); return; }
        string[] lines = File.ReadAllLines(NxOpenXml);
        foreach (string[] m in Members)
        {
            string license = null;
            bool inBlock = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!inBlock && line.IndexOf("name=\"", StringComparison.Ordinal) >= 0 && Matches(line, m[1]))
                    inBlock = true;
                if (inBlock)
                {
                    int idx = line.IndexOf("License requirements:", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        string rest = line.Substring(idx + "License requirements:".Length).Trim();
                        int end = rest.IndexOf("</para>", StringComparison.Ordinal);
                        license = end >= 0 ? rest.Substring(0, end).Trim() : rest.Trim(' ', '<', '/', '>');
                        break;
                    }
                    if (line.Contains("</member>")) break;
                }
            }
            result[m[0]] = license == null ? "" : license;
        }
    }

    private static void Finish(string outPath)
    {
        try { File.WriteAllLines(outPath, _lines.ToArray()); }
        catch (Exception ex)
        {
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-license-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
