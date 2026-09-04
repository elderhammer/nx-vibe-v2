// ToolFamilyMap.cs — plan tools[].type/subtype → 重建模板对（U-7 A′ 定案，docs/nx-tool-type-enum-spec.md）
// 解析链（INV-U7-3）：① NX 注册对表（(Types, Subtypes) 精确命中，INV-U7-2，P2 实测校准）
// → ② 旧家族关键词表（中/英文键，D-2 保留为旧 plan 兼容回退——GetNameOfType 语言敏感见索引 §2.1）
// → ③ 默认 (mill_planar,MILL) + Inferred（diag）。
// 注：模板 subtype 串（MILL/STD_DRILL，Create 字面量）与 NX 枚举词（Mill5/DrillStandard）是两个
// 词汇表；注册对 = 按默认刀型建通用组 + 数值直填（spec §5b：按 subtype 精准建刀超 v1）。

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

        /// <summary>注册对表（INV-U7-2）：(NX Types|Subtypes) → Create 注册对。行 = 探针 P2 实测校准
        /// （camprobe-u7-115251：新建 (mill_planar,MILL) 读回 (Mill,Mill5)、(hole_making,STD_DRILL)
        /// 读回 (Drill,DrillStandard)）；枚举原文精确匹配（大小写敏感）。</summary>
        private static readonly Dictionary<string, TemplatePair> RegisterPairs =
            new Dictionary<string, TemplatePair>
        {
            { "Mill|Mill5", new TemplatePair("mill_planar", "MILL") },
            { "Drill|DrillStandard", new TemplatePair("hole_making", "STD_DRILL") },
        };

        /// <summary>PRE-3/U-7 判据源：注册对表与关键词表均非空。</summary>
        public static bool IsReady
        {
            get { return RegisterPairs.Count > 0 && DrillKeywords.Length > 0 && MillKeywords.Length > 0; }
        }

        /// <summary>解析结果：Pair=模板对；Inferred=true=未命中任何表（默认铣兜底，diag）；<br/>
        /// KeywordFallback=true=经家族关键词回退（旧 plan 兼容路径，非精确但非兜底）。</summary>
        public sealed class Resolution
        {
            public TemplatePair Pair = new TemplatePair("mill_planar", "MILL");
            public bool Inferred = true;
            public bool KeywordFallback = false;
        }

        /// <summary>type（NX 词或家族串）+ subtype（NX 词，可空）→ Resolution。大小写：①敏感 ②不敏感。</summary>
        public static Resolution Resolve(string type, string subtype)
        {
            var res = new Resolution();
            if (string.IsNullOrEmpty(type)) return res;
            // ① NX 注册对表
            if (!string.IsNullOrEmpty(subtype))
            {
                TemplatePair hit;
                if (RegisterPairs.TryGetValue(type + "|" + subtype, out hit))
                {
                    res.Pair = hit;
                    res.Inferred = false;
                    return res;
                }
            }
            // ② 家族关键词回退（旧 plan 家族串；NX 词不命中的经此 → 默认 + Inferred，见 ③）
            string t = type.ToLowerInvariant();
            foreach (string k in DrillKeywords)
                if (t.IndexOf(k.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    res.Pair = new TemplatePair("hole_making", "STD_DRILL");
                    res.Inferred = false;
                    res.KeywordFallback = true;
                    return res;
                }
            foreach (string k in MillKeywords)
                if (t.IndexOf(k.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                {
                    res.Pair = new TemplatePair("mill_planar", "MILL");
                    res.Inferred = false;
                    res.KeywordFallback = true;
                    return res;
                }
            return res; // ③ 默认铣 + Inferred=true
        }
    }
}
