using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildMaster.Economy;

namespace GuildMaster.Reports
{
    /// <summary>Where the guild's gold came from or went (F-3). Income kinds first, then expense kinds.</summary>
    public enum GoldCategory
    {
        Tax, GoodsSold, MerchantSales, LivingCosts, QuestRefund, Commission, Tribute, SideJob, OtherIncome,
        QuestEscrow, Negotiation, LootBought, Crafting, Upgrade, Bonus, Food, OtherExpense,
    }

    public static class GoldCategories
    {
        public static readonly GoldCategory[] Income =
            { GoldCategory.Tax, GoldCategory.GoodsSold, GoldCategory.MerchantSales, GoldCategory.LivingCosts, GoldCategory.QuestRefund,
              GoldCategory.Commission, GoldCategory.Tribute, GoldCategory.SideJob, GoldCategory.OtherIncome };

        public static readonly GoldCategory[] Expense =
            { GoldCategory.QuestEscrow, GoldCategory.Negotiation, GoldCategory.LootBought, GoldCategory.Crafting, GoldCategory.Upgrade, GoldCategory.Bonus,
              GoldCategory.Food, GoldCategory.OtherExpense };

        public static bool IsIncome(GoldCategory c) => c <= GoldCategory.OtherIncome;

        /// <summary>
        /// Sorts one treasury movement (positive = in, negative = out) into a category by its reason.
        /// No reason is what the quest board uses: gold leaving is the reward escrowed for a new quest, gold
        /// arriving is a refund. Anything unknown, or a reason that does not fit the direction, lands in "other"
        /// so the categories always add up to the real change in gold.
        /// </summary>
        public static GoldCategory Classify(string reason, int amount)
        {
            var c = reason switch
            {
                GoldReason.Tax => GoldCategory.Tax,
                GoldReason.Goods => GoldCategory.GoodsSold,
                GoldReason.Merchant => GoldCategory.MerchantSales,
                GoldReason.Living => GoldCategory.LivingCosts,
                GoldReason.Loot => GoldCategory.LootBought,
                GoldReason.Craft => GoldCategory.Crafting,
                GoldReason.Upgrade => GoldCategory.Upgrade,
                GoldReason.Bonus => GoldCategory.Bonus,
                GoldReason.Commission => GoldCategory.Commission,
                GoldReason.Tribute => GoldCategory.Tribute,
                GoldReason.SideJob => GoldCategory.SideJob,
                GoldReason.Food => GoldCategory.Food,
                null => amount > 0 ? GoldCategory.QuestRefund : GoldCategory.QuestEscrow,
                _ => amount > 0 ? GoldCategory.OtherIncome : GoldCategory.OtherExpense,
            };
            if (IsIncome(c) != amount > 0) c = amount > 0 ? GoldCategory.OtherIncome : GoldCategory.OtherExpense;
            return c;
        }

        public static string Label(GoldCategory c) => c switch
        {
            GoldCategory.Tax => "일일 세금",
            GoldCategory.GoodsSold => "상품 판매(길드원 구매)",
            GoldCategory.MerchantSales => "행상인 판매",
            GoldCategory.LivingCosts => "길드원 생활비",
            GoldCategory.QuestRefund => "의뢰 보상 환급",
            GoldCategory.Commission => "의뢰 대금(수수료 포함)",
            GoldCategory.Tribute => "상납금",
            GoldCategory.SideJob => "일손 돕기",
            GoldCategory.Food => "식량 구매",
            GoldCategory.OtherIncome => "기타 수입",
            GoldCategory.QuestEscrow => "의뢰 보상 예치",
            GoldCategory.Negotiation => "협상 인상분",
            GoldCategory.LootBought => "전리품 매입",
            GoldCategory.Crafting => "제작 비용",
            GoldCategory.Upgrade => "건물 업그레이드",
            GoldCategory.Bonus => "길드원 보너스",
            _ => "기타 지출",
        };
    }

    /// <summary>One game day of the guild's accounts and events. Filled in by <see cref="DailyLedger"/>, read by the report window.</summary>
    public class DailyReport
    {
        readonly int[] _amounts = new int[System.Enum.GetValues(typeof(GoldCategory)).Length];

        public int Day { get; }
        /// <summary>Hour of day the figures start from: 8 on the first day, 0 afterwards.</summary>
        public float StartHour { get; }
        /// <summary>False while the day is still running (the figures keep changing).</summary>
        public bool Closed { get; internal set; }

        public int GoldStart { get; internal set; }
        public int GoldEnd { get; internal set; }
        public int ExpStart { get; internal set; }
        public int ExpEnd { get; internal set; }

        // Activity of the day.
        public int QuestsPosted { get; internal set; }
        public int QuestsCompleted { get; internal set; }
        public int QuestsFailed { get; internal set; }
        public int QuestsCancelled { get; internal set; }
        /// <summary>Gold the quests paid out to adventurers.</summary>
        public int QuestPaidGold { get; internal set; }
        public int Crafted { get; internal set; }
        public int Raids { get; internal set; }
        public List<string> Recruited { get; } = new();
        public List<string> Departed { get; } = new();
        public List<string> Deaths { get; } = new();
        public List<string> LevelUps { get; } = new();
        public List<string> BuildingsLost { get; } = new();

        public DailyReport(int day, float startHour, int gold, int exp)
        {
            Day = day;
            StartHour = startHour;
            GoldStart = GoldEnd = gold;
            ExpStart = ExpEnd = exp;
        }

        public int Amount(GoldCategory c) => _amounts[(int)c];

        internal void Add(GoldCategory c, int amount) => _amounts[(int)c] += amount;

        /// <summary>Moves part of one category to another (a raised reward was first counted as a new escrow).</summary>
        internal void Move(GoldCategory from, GoldCategory to, int amount)
        {
            int moved = System.Math.Min(System.Math.Max(0, amount), _amounts[(int)from]);
            _amounts[(int)from] -= moved;
            _amounts[(int)to] += moved;
        }

        public int Income
        {
            get
            {
                int sum = 0;
                foreach (var c in GoldCategories.Income) sum += _amounts[(int)c];
                return sum;
            }
        }

        public int Expense
        {
            get
            {
                int sum = 0;
                foreach (var c in GoldCategories.Expense) sum += _amounts[(int)c];
                return sum;
            }
        }

        public int Net => Income - Expense;
    }

    public enum LineTone { Normal, Good, Bad, Muted }

    public readonly struct ReportLine
    {
        public string Text { get; }
        public LineTone Tone { get; }

        public ReportLine(string text, LineTone tone)
        {
            Text = text;
            Tone = tone;
        }
    }

    /// <summary>Report wording, kept apart from the bookkeeping so it can be tested and reworded in one place.</summary>
    public static class DailyReportTexts
    {
        const int MaxNames = 6;

        public static string Money(int gold) => gold.ToString("N0", CultureInfo.InvariantCulture) + "G";

        /// <summary>With an explicit sign: "+120G", "-40G", "0G".</summary>
        public static string SignedMoney(int gold) => gold.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture) + "G";

        public static string Names(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "";
            if (names.Count <= MaxNames) return string.Join(", ", names);
            return string.Join(", ", names.Take(MaxNames)) + $" 외 {names.Count - MaxNames}명";
        }

        /// <summary>One line for the notification: the day's result in gold.</summary>
        public static string Summary(DailyReport r)
        {
            string text = $"{r.Day}일차 정산: 순이익 {SignedMoney(r.Net)} (수입 {Money(r.Income)} / 지출 {Money(r.Expense)}), 잔액 {Money(r.GoldEnd)}";
            if (r.Deaths.Count > 0) text += $" · 사망 {r.Deaths.Count}명";
            return text;
        }

        /// <summary>The events of the day, one line each. Empty categories are left out.</summary>
        public static List<ReportLine> ActivityLines(DailyReport r)
        {
            var lines = new List<ReportLine>();
            if (r.QuestsPosted + r.QuestsCompleted + r.QuestsFailed + r.QuestsCancelled > 0)
                lines.Add(new ReportLine($"의뢰  등록 {r.QuestsPosted} · 달성 {r.QuestsCompleted} · 실패 {r.QuestsFailed} · 취소 {r.QuestsCancelled}", LineTone.Normal));
            if (r.QuestPaidGold > 0)
                lines.Add(new ReportLine($"의뢰 보상 지급  {Money(r.QuestPaidGold)}", LineTone.Normal));
            if (r.Recruited.Count > 0)
                lines.Add(new ReportLine($"영입  {Names(r.Recruited)}", LineTone.Good));
            if (r.Departed.Count > 0)
                lines.Add(new ReportLine($"이탈  {Names(r.Departed)}", LineTone.Muted));
            if (r.Deaths.Count > 0)
                lines.Add(new ReportLine($"사망  {Names(r.Deaths)}", LineTone.Bad));
            if (r.LevelUps.Count > 0)
                lines.Add(new ReportLine($"레벨 업  {Names(r.LevelUps)}", LineTone.Good));
            if (r.Raids > 0)
                lines.Add(new ReportLine($"침공  {r.Raids}회", LineTone.Bad));
            if (r.BuildingsLost.Count > 0)
                lines.Add(new ReportLine($"파괴된 건물  {Names(r.BuildingsLost)}", LineTone.Bad));
            if (r.Crafted > 0)
                lines.Add(new ReportLine($"제작 완료  {r.Crafted}개", LineTone.Normal));
            if (lines.Count == 0)
                lines.Add(new ReportLine("특별한 일이 없었습니다.", LineTone.Muted));
            return lines;
        }
    }
}
