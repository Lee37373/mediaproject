using System.Linq;
using System.Text;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The guild's household (살림, K; economy round 2, user decision 2026-10-29): the food stock and buying more, and
    /// what the clients of the open quests will pay when they are done.
    /// </summary>
    public class HouseholdView : UIPanel
    {
        public override string Title => "살림";
        protected override float RefreshInterval => 0.5f;

        Text _food, _foodMessage, _commissions, _choreInfo;
        Button _buySmall, _buyBulk;
        readonly System.Collections.Generic.Dictionary<ChoreKind, Button> _chores = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);

            UIBuilder.Label(body, "식량", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 26);
            var food = UIBuilder.Panel(body, "Food", UITheme.PanelBgLight);
            food.Size(-1, 140);
            UIBuilder.VLayout(food, 6, 10);
            _food = UIBuilder.Label(food.transform, "", 16, TextAnchor.UpperLeft).Size(-1, 48);
            _food.verticalOverflow = VerticalWrapMode.Overflow;
            var row = UIBuilder.Row(food.transform, 34, 10);
            _buySmall = UIBuilder.Button(row, $"{FoodRules.SmallSize}개 사기 ({FoodRules.Price(FoodRules.SmallSize)}G)", () => Buy(FoodRules.SmallSize), 180, 32);
            _buyBulk = UIBuilder.Button(row, $"{FoodRules.BulkSize}개 사기 ({FoodRules.Price(FoodRules.BulkSize)}G)", () => Buy(FoodRules.BulkSize), 180, 32);
            _foodMessage = UIBuilder.Label(food.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);

            // Small income on demand (user decision 2026-10-29): residents' odd jobs as minigames, once a day each.
            UIBuilder.Label(body, "일손 돕기", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 26);
            var chores = UIBuilder.Panel(body, "Chores", UITheme.PanelBgLight);
            chores.Size(-1, 84);
            UIBuilder.VLayout(chores, 6, 8);
            var choreRow = UIBuilder.Row(chores.transform, 34, 10);
            foreach (var kind in ChoreRules.All)
            {
                var k = kind;
                _chores[k] = UIBuilder.Button(choreRow, $"{ChoreRules.Resident(k).Split(' ')[1]} · {ChoreRules.Title(k)}", () => Chore(k), 220, 32);
            }
            _choreInfo = UIBuilder.Label(chores.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);

            UIBuilder.Label(body, "의뢰 대금", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 26);
            var list = UIBuilder.Panel(body, "Commissions", UITheme.PanelBgLight);
            list.Size(-1, -1, -1, 1);
            _commissions = UIBuilder.Label(list.transform, "", 15, TextAnchor.UpperLeft);
            _commissions.rectTransform.Stretch(12, 12, 10, 10);
            _commissions.verticalOverflow = VerticalWrapMode.Overflow;
        }

        void Buy(int portions)
        {
            if (!Services.TryGet<FoodStore>(out var store)) return;
            _foodMessage.text = store.TryBuy(portions)
                ? UIBuilder.Colored($"빵집에서 식량 {portions}개를 샀습니다.", UITheme.Good)
                : UIBuilder.Colored("길드 자금이 부족합니다.", UITheme.Bad);
            Refresh();
        }

        void Chore(ChoreKind kind)
        {
            if (!Services.TryGet<ChoreService>(out var service) || !Services.TryGet<GameClock>(out var clock)) return;
            var check = service.CanDo(kind, clock.TotalHours, clock.HourOfDay);
            if (!check.Ok) { _choreInfo.text = UIBuilder.Colored(check.Reason, UITheme.Warn); return; }
            if (Services.TryGet<ChoreView>(out var view)) view.Show(kind);
        }

        protected override void Refresh()
        {
            if (Services.TryGet<ChoreService>(out var chores) && Services.TryGet<GameClock>(out var now))
            {
                foreach (var kv in _chores)
                {
                    var check = chores.CanDo(kv.Key, now.TotalHours, now.HourOfDay);
                    kv.Value.interactable = check.Ok;
                    string tag = check.Ok ? "" : check.Reason.StartsWith("오늘") ? " (내일)" : " (쉬는 중)";
                    kv.Value.SetLabel($"{ChoreRules.Resident(kv.Key).Split(' ')[1]} · {ChoreRules.Title(kv.Key)}{tag}");
                }
                _choreInfo.text = $"주민 일을 거들고 수고비(최대 {ChoreRules.MaxPay}G)와 평판을 받습니다. 주민마다 하루 한 번. 지금까지 {chores.Earned}G ({chores.Done}회)";
            }

            if (Services.TryGet<FoodStore>(out var store))
            {
                int days = store.DaysLeft;
                var color = days < FoodRules.WarnDays ? UITheme.Bad : days < 4 ? UITheme.Warn : UITheme.Good;
                _food.text = $"보유 {store.Food}개 · 길드원 {store.Eaters}명이 매일 자정에 1개씩 먹음 · " +
                             UIBuilder.Colored($"약 {days}일치", color) + "\n" +
                             UIBuilder.Colored($"모자라면 못 먹은 길드원의 스트레스 +{FoodRules.HungerStress:0}, 피로 +{FoodRules.HungerFatigue:0}. 밥값(1인 10G)은 그대로 길드에 들어옵니다.", UITheme.TextMuted);
                bool can = Services.TryGet<GuildTreasury>(out var t);
                _buySmall.interactable = can && t.Gold >= FoodRules.Price(FoodRules.SmallSize);
                _buyBulk.interactable = can && t.Gold >= FoodRules.Price(FoodRules.BulkSize);
            }

            var sb = new StringBuilder();
            if (Services.TryGet<CommissionService>(out var commissions) && Services.TryGet<QuestBoard>(out var board))
            {
                sb.Append(UIBuilder.Colored("의뢰를 끝내면 의뢰인이 보상(시세까지)과 길드 수수료를 냅니다. 시세보다 높게 건 보상은 길드 몫입니다.", UITheme.TextMuted)).Append("\n\n");
                int count = 0;
                foreach (var q in board.Quests.Where(q => q.IsActive))
                {
                    if (!commissions.TryGet(q, out var c)) continue;
                    count++;
                    int pay = c.Payment(q.RewardGold - q.BonusGold);
                    sb.Append($"· {q.Title} — 의뢰인 {c.Client} · 완료 시 {UIBuilder.Colored(pay + "G", UITheme.Good)} (수수료 {c.Fee}G, 시세 {c.Budget}G)\n");
                }
                if (count == 0) sb.Append("진행 중인 의뢰가 없습니다.\n");
                sb.Append("\n").Append(UIBuilder.Colored($"지금까지 받은 수수료 {commissions.TotalFees}G · 대금을 받은 의뢰 {commissions.Paid}건", UITheme.TextMuted));
            }
            if (Services.TryGet<TributeService>(out var tributes))
                sb.Append("\n\n").Append(UIBuilder.Colored($"상납금: 지금까지 {tributes.Collected}G ({tributes.Times}회) · 길드원 관리 창의 '상납 요구'로 받아냅니다.", UITheme.TextMuted));
            _commissions.text = sb.ToString();
        }
    }
}
