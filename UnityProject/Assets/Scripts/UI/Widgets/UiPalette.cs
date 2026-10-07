using UnityEngine;

namespace Xianxia.Sect.UI
{
    public static class UiPalette
    {
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var c); return c; }
        public static readonly Color Paper = Hex("#D6D2C8");
        public static readonly Color PaperDark = Hex("#BFBAB0");
        public static readonly Color Portrait = Hex("#1E1C1A");
        public static readonly Color Ink = Hex("#232020");
        public static readonly Color Text = Hex("#2A2622");
        // Contrast-safe on PaperDark; the original #6B655C fails the requested contrast.
        public static readonly Color Secondary = Hex("#4C473F");
        public static readonly Color LightText = Hex("#E8E4DA");
        public static readonly Color Vermilion = Hex("#B7642C");
        public static readonly Color Danger = Hex("#A33228");
        public static readonly Color Jade = Hex("#4E7A5E");
        public static readonly Color Blue = Hex("#43637E");
        public static readonly Color Disabled = Hex("#AAA59B");
        public static readonly Color Backdrop = new Color(0, 0, 0, 0.6f);
        public static readonly Color Clear = Color.clear;
        public static Color RankAccent(DiscipleRank rank) => rank switch
        {
            DiscipleRank.SectMaster => Danger,
            DiscipleRank.Elder => Jade,
            DiscipleRank.InnerDisciple => Blue,
            _ => Vermilion,
        };
        public static string RankLabel(DiscipleRank rank) => rank switch
        {
            DiscipleRank.SectMaster => "เจ้าสำนัก",
            DiscipleRank.Elder => "ผู้อาวุโส",
            DiscipleRank.InnerDisciple => "ศิษย์ใน",
            DiscipleRank.OuterDisciple => "ศิษย์นอก",
            _ => "ไม่ระบุ",
        };
    }
}
