using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>
    /// Korean-capable UI font. Uses an OS font at runtime (no font asset needed);
    /// replace with a bundled font asset for distribution builds if desired.
    /// </summary>
    public static class UIFonts
    {
        static readonly string[] Candidates =
        {
            "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic", "NanumGothic", "Noto Sans CJK KR", "Noto Sans KR", "Arial",
        };

        static Font s_main;

        public static Font Main
        {
            get
            {
                if (s_main != null) return s_main;
                s_main = Font.CreateDynamicFontFromOSFont(Candidates, 16);
                if (s_main == null) s_main = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return s_main;
            }
        }
    }
}
