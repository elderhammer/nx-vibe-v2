// ToolFamilyMap.cs — plan tools[].type（NX 家族串，语言敏感见索引 §2.1）→ 重建模板对（D-2=A）
// 依据（2026-09-04 camprobe-executor 012518）：重建侧 Create 模板对 =
// (mill_planar,MILL)/(hole_making,STD_DRILL)（P2 实证）；test.prt 家族实态：
// 铣刀-5 参数/Milling Tool-5 Parameters→Mill5；D6.0X90 中心钻家族=Chamfer Mill（=倒斜铣刀 中文名，
// CutterSubtype=ChamferTool，铣族倒角刀——打点定心用，**非钻族**）；钻刀/Drilling Tool→STD_DRILL。
// 语言敏感 → 关键词表同时含中/英文键；未命中 → 默认 (mill_planar,MILL) + Inferred=true（diag）。

using System;
using System.Collections.Generic;
using NXPlugins.PlanExporter;

namespace NXPlugins.PlanExecutor
{
    public static class ToolFamilyMap
    {
        /// <summary>钻族关键词（命中即 STD_DRILL）。顺序无关——钻/铣键互斥命名。</summary>
        private static readonly string[] DrillKeywords =
        {
            "钻刀", "Drilling Tool", "drill",
        };

        /// <summary>铣族关键词（含 Chamfer Mill 倒角铣刀；倒斜铣刀 = Chamfer Mill 中文名）。</summary>
        private static readonly string[] MillKeywords =
        {
            "铣刀", "Milling Tool", "Chamfer Mill", "倒斜铣刀", "End Mill", "Ball", "Bull Nose",
        };

        /// <summary>PRE-3/D-2 判据源：表非空。</summary>
        public static bool IsReady { get { return DrillKeywords.Length > 0 && MillKeywords.Length > 0; } }

        /// <summary>解析结果：Pair=模板对；Inferred=true=未命中关键词（默认铣兜底）。</summary>
        public sealed class Resolution
        {
            public TemplatePair Pair = new TemplatePair("mill_planar", "MILL");
            public bool Inferred = true;
        }

        /// <summary>family 串（原样，含中文/英文）→ Resolution。大小写不敏感。</summary>
        public static Resolution Resolve(string typeRaw)
        {
            var res = new Resolution();
            if (string.IsNullOrEmpty(typeRaw)) return res;
            string t = typeRaw.ToLowerInvariant();
            foreach (string k in DrillKeywords)
                if (t.IndexOf(k.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    res.Pair = new TemplatePair("hole_making", "STD_DRILL");
                    res.Inferred = false;
                    return res;
                }
            foreach (string k in MillKeywords)
                if (t.IndexOf(k.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    res.Pair = new TemplatePair("mill_planar", "MILL");
                    res.Inferred = false;
                    return res;
                }
            return res; // 默认铣 + Inferred=true
        }
    }
}
