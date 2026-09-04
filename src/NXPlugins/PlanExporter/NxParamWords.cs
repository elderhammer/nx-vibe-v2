// NxParamWords.cs — 策略枚举键的 NX 原文词集（v1.5-③；schema enum 词集的纯逻辑镜像）
// 词源：NXOpen.xml F: 全词实证（2026-09-04）——枚举值 = .NET ToString 原文（语言无关），
// 导出直写（NxCollect TryParamS）/重建 Enum.Parse 直用（ExecutorAdapter），词集校验在纯逻辑层
// （ExecutorCore V15-POST-2：词 ∉ 本表 → PARAM_ENUM_UNKNOWN，不引 NX 类型）。
// 键 = strategy plan 键名；与 schema/autocam-plan.schema.json strategy 枚举词集同步（schema 为准，
// 本表为校验镜像——不同步即 bug，由 [U] V15-PRE-1 夹具钉住）。

using System;
using System.Collections.Generic;

namespace NXPlugins.PlanExporter
{
    public static class NxParamWords
    {
        /// <summary>枚举键 → NX 原文词集（排序无所谓，Contains 语义）。</summary>
        public static readonly Dictionary<string, HashSet<string>> EnumWords =
            new Dictionary<string, HashSet<string>>
        {
            { "cut_pattern", Words("AdaptiveRoughing", "AdaptiveZig", "ConcentricZig", "ConcentricZigWithContour",
                "ConcentricZigWithStepover", "ConcentricZigZag", "CrosscutZig", "CrosscutZigZag",
                "CrosscutZigZagWithLifts", "FollowPart", "FollowPeriphery", "Helical", "HelicalAroundPart",
                "HelicalSpiral", "Mixed", "None", "Profile", "RadialZig", "RadialZigWithContour",
                "RadialZigWithStepover", "RadialZigZag", "RadialZigZagWithLifts", "SameAsNonSteep", "Spiral",
                "StandardDrive", "Trochoidal", "Zig", "ZigWithContour", "ZigWithStepover", "ZigZag",
                "ZigZagWithLifts", "ZlevelHelical", "ZlevelZig", "ZlevelZigZag", "ZlevelZigZagWithLifts") },
            { "cut_order", Words("DepthFirst", "DepthFirstAlways", "LevelFirst") },
            { "cut_direction", Words("Climb", "Conventional", "Forward", "Mixed", "Reverse") },
        };

        /// <summary>枚举键的合法词判定（未知键 → false：调用方按自身语义处置）。</summary>
        public static bool IsEnumKey(string key)
        {
            return EnumWords.ContainsKey(key);
        }

        public static bool IsWord(string key, string word)
        {
            HashSet<string> set;
            return word != null && EnumWords.TryGetValue(key, out set) && set.Contains(word);
        }

        private static HashSet<string> Words(params string[] ws)
        {
            return new HashSet<string>(ws, StringComparer.Ordinal);
        }
    }
}
