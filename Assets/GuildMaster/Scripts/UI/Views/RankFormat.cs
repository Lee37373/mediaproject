using GuildMaster.Guild;

namespace GuildMaster.UI
{
    /// <summary>How the guild's rank conditions read on screen (P4-106): the HUD's next goal and the rank window.</summary>
    public static class RankFormat
    {
        /// <summary>"둥지 5곳 토벌 2/5", green once met.</summary>
        public static string Line(in Goal g, in GuildStanding s)
        {
            int now = GuildRankRules.Progress(g, s);
            bool met = now >= g.Count;
            string count = UIBuilder.Colored($"{System.Math.Min(now, g.Count)}/{g.Count}", met ? UITheme.Good : UITheme.Warn);
            return met ? UIBuilder.Colored($"{g.Label} 완료", UITheme.Good) : $"{g.Label} {count}";
        }

        public static string Rank(GuildRank r) => GuildRankRules.Name(r);
    }
}
