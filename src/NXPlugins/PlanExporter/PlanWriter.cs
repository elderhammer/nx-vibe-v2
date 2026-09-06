// PlanWriter.cs — 原子落盘（POST-2：失败无半成品、旧文件不被破坏）
// 策略：同目录写 <name>.tmp → 目标存在用 File.Replace（NTFS 原子替换，旧文件在新文件就位前
// 不被破坏）；目标不存在用 File.Move。任一步失败清理 .tmp（进程崩溃残留的 .tmp 由下次写入覆盖）。
// 2026-09-06 修正：原实现为 Delete→Move（非原子，Delete 与 Move 之间崩溃即丢旧文件），
// 头注释却声称 File.Replace——现按注释兑现。

using System;
using System.IO;

namespace NXPlugins.PlanExporter
{
    public static class PlanWriter
    {
        /// <summary>序列化器可注入（POST-2 测试注入抛错用）。默认 PlanJsonSerializer。</summary>
        public static IPlanSerializer Serializer = new PlanJsonSerializer();

        public static void WriteAtomically(PlanDocument doc, string targetPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            string tmp = Path.Combine(dir, Path.GetFileName(targetPath) + ".tmp");
            try
            {
                string json = Serializer.Serialize(doc);   // 序列化失败 → 直接抛，未创建 .tmp
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                if (File.Exists(targetPath))
                    File.Replace(tmp, targetPath, null);   // 原子替换（旧文件保持至新文件就位）
                else
                    File.Move(tmp, targetPath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 清理失败不遮蔽原异常 */ }
                throw;
            }
        }
    }
}
