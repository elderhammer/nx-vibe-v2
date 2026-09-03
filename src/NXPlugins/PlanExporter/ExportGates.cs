// ExportGates.cs — 导出前置门（PRE-1/PRE-2/POST-5 的离线判据）
// NX 会话（OpenDisplay/SetWork/许可 Reserve）属 [I] 层适配器；本文件把"判定"抽成纯逻辑：
// 适配器把会话状态翻译成 ISessionGate 后，Preflight 输出结构化失败原因（POST-5：中止不落盘）。

using System.Collections.Generic;

namespace NXPlugins.PlanExporter
{
    /// <summary>会话状态端口：由 NX 适配器实现（[I]），单测用替身。</summary>
    public interface ISessionGate
    {
        bool HasDisplayedWorkPartWithCamSetup { get; }   // PRE-1
        bool CanReserveCamBase { get; }                  // PRE-2
    }

    public sealed class PreflightResult
    {
        public readonly List<string> Failures = new List<string>();
        public bool Ok { get { return Failures.Count == 0; } }
    }

    public static class ExportGates
    {
        /// <summary>PRE-1/PRE-2 判据；POST-5：任一失败 → 调用方中止且不得落盘。</summary>
        public static PreflightResult Preflight(ISessionGate gate, bool whiteListReady)
        {
            var r = new PreflightResult();
            if (gate == null) { r.Failures.Add("会话端口未就绪"); return r; }
            if (!gate.HasDisplayedWorkPartWithCamSetup)
                r.Failures.Add("PRE-1 不满足：无显示工作部件或部件无 CAMSetup");
            if (!gate.CanReserveCamBase)
                r.Failures.Add("PRE-2 不满足：cam_base 许可不可用");
            if (!whiteListReady)
                r.Failures.Add("PRE-3 不满足：模板白名单未初始化");
            return r;
        }
    }
}
