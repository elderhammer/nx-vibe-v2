// WhiteList.cs — nx_template 模板对白名单（U-1 决议，spec §5）
// 依据：docs/nx2406-install-index.md §2.1 实证 + samples/camprobe-types.txt 注册表枚举。
// 覆盖 MVP 口径（铣 CAVITY + 孔 PTP 家族）；其余家族返回 null + Ambiguous=false。
// 性质：PRE-3（表非空）、POST-6（PTP 歧义 → 默认对 + Ambiguous=true）。

using System;
using System.Collections.Generic;

namespace NXPlugins.PlanExporter
{
    public sealed class TemplateResolution
    {
        public TemplatePair Pair = null;
        public bool Ambiguous = false;
    }

    public static class WhiteList
    {
        // TypeFamily（GetNameOfType 大类）→ 候选模板对（首项为默认对）
        private static readonly Dictionary<string, List<TemplatePair>> Map =
            new Dictionary<string, List<TemplatePair>>
        {
            { "Cavity Milling", new List<TemplatePair> { new TemplatePair("mill_contour", "CAVITY_MILL") } },
            { "Drilling",       new List<TemplatePair> { new TemplatePair("hole_making", "DRILLING") } },
            // PTP 家族含多种子类型无法程序化区分（打点/钻头G83 实证）→ 默认 DRILLING + 歧义标记
            { "Point to Point", new List<TemplatePair> {
                new TemplatePair("hole_making", "DRILLING"),
                new TemplatePair("hole_making", "SPOT_DRILLING"),
                new TemplatePair("hole_making", "DEEP_HOLE_DRILLING"),
                new TemplatePair("hole_making", "TAPPING") } },
        };

        /// <summary>PRE-3：白名单已初始化且非空（测试用守卫）。</summary>
        public static bool IsReady { get { return Map.Count > 0; } }

        public static TemplateResolution Resolve(string typeFamily)
        {
            List<TemplatePair> cands;
            if (typeFamily == null || !Map.TryGetValue(typeFamily, out cands) || cands.Count == 0)
                return new TemplateResolution();
            return new TemplateResolution { Pair = cands[0], Ambiguous = cands.Count > 1 };
        }
    }
}
