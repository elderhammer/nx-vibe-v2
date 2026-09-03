// PlanWriter.cs — 原子落盘（POST-2：失败无半成品、旧文件不被破坏）
// 策略：同目录写 <name>.tmp → File.Replace/rename 覆盖目标；任一步失败清理 .tmp。

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
                    File.Delete(targetPath);
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
