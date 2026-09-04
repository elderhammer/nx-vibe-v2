// NxToolWords.cs — NX Tool.Types/Subtypes 枚举词集（U-7 A′，docs/nx-tool-type-enum-spec.md）
// 词集来源：NX2406 NXOpen.xml F:NXOpen.CAM.Tool.{Types,Subtypes} 字段清单实证抽取（2026-09-04，
// Types=14 / Subtypes=49 含 Undefined）。schema tool.type/subtype enum 与本表同源（INV-U7-1
// 原文直写语义：无中间归类表）。纯逻辑层无 NX 依赖——导出/校验/重建共用。

using System;

namespace NXPlugins.PlanExporter
{
    public static class NxToolWords
    {
        public static readonly string[] Types =
        {
            "Mill", "Drill", "Turn", "Groove", "Thread", "Wedm",
            "Barrel", "Tcutter", "Form", "DrillSpcGroove", "Solid",
            "MillForm", "Laser", "Soft",
        };

        public static readonly string[] Subtypes =
        {
            "Undefined", "Mill5", "Mill7", "Mill10", "MillBall",
            "DrillStandard", "DrillCenterBell", "DrillCountersink",
            "DrillSpotFace", "DrillSpotDrill", "DrillBore", "DrillReam",
            "DrillCounterbore", "DrillTap", "DrillBurnishing", "DrillThreadMill",
            "DrillBackSpotFace", "DrillStep", "TurnStandard", "TurnButton",
            "TurnBoringBar", "GrooveStandard", "GrooveRing", "GrooveFullNoseRadius",
            "GrooveUserDefined", "ThreadStandard", "ThreadButress", "ThreadAcme",
            "ThreadTrapezoidal", "Generic", "Probe", "MillChamfer",
            "MillSpherical", "DrillCore", "StdLaser", "Laser",
            "DrillBackCountersink", "CoaxialLaser", "DrillBoringBar",
            "DrillChamferBoringBar", "DrillBackBore", "ThreadTriangularStandard",
            "ThreadTriangularTrapezoidal", "BarrelStandard", "BarrelTangent",
            "BarrelTaper", "BarrelLens", "MillDovetail", "TurnPrime",
        };

        /// <summary>精确词判定（大小写敏感——NX 枚举原文）。null/空串 → false。</summary>
        public static bool IsTypeWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (string w in Types) if (w == s) return true;
            return false;
        }

        public static bool IsSubtypeWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (string w in Subtypes) if (w == s) return true;
            return false;
        }
    }
}
