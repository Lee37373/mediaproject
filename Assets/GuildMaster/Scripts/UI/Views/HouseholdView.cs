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

        Text _food, _foodMessage, _commissions, _choreInfo, _townLine;
        Button _buySmall, _buyBulk;
        readonly System.Collections.Generic.Dictionary<ChoreKind, Button> _chores = new();
        readonly System.Collections.Generic.List<(Text label, Button open)> _residentRows = new();

        void OpenResident(int index)
        {
            if (!Services.TryGet<Social.ResidentService>(out var residents) || index >= residents.Residents.Count) return;
            if (Services.TryGet<ResidentRequestView>(out var view)) view.Show(residents.Residents[index]);
        }

        /// <summary>Each resident's opinion, favour and request (P4-134), and what the town's opinion tells new visitors.</summary>
        void RefreshResidents()
        {
            if (!Services.TryGet<Social.ResidentService>(out var residents) || !Services.TryGet<Social.ResidentRequests>(out var requests)) return;
            int town = requests.TownOpinion;
            _townLine.text = $"마을 평판 {town} ({Social.ResidentRequestRules.TownOpinionLabel(town)}) · 새 방문자 시작 호감 {Signed(requests.VisitorWordNow)} · " +
                             $"부탁 해결 {requests.Done}건, 실패 {requests.Failed}건 · 평판 {string.Join("·", Social.ResidentRequestRules.TierAt)}마다 단골 혜택 10%씩";
            for (int i = 0; i < _residentRows.Count; i++)
            {
                var (label, open) = _residentRows[i];
                bool on = i < residents.Residents.Count;
                label.transform.parent.gameObject.SetActive(on);
                if (!on) continue;
                var p = residents.Residents[i].Profile;
                int tier = Social.ResidentRequestRules.Tier(p.GuildOpinion);
                string favour = tier > 0 ? UIBuilder.Colored(Social.ResidentRequestRules.FavourText(p.Job, tier), UITheme.Good) : UIBuilder.Colored("혜택 없음", UITheme.TextMuted);
                var q = requests.ActiveOf(p.Id);
                string ask = q == null ? "" : q.IsOpen
                    ? "  " + UIBuilder.Colored($"! {requests.WhatText(q)} ({requests.HoursLeft(q):0}시간)", UITheme.Warn)
                    : "  " + UIBuilder.Colored($"… {q.MemberName} 하는 중", UITheme.Exp);
                label.text = $"{p.Name} {UIBuilder.Colored(p.JobLabel, UITheme.TextMuted)} {Signed(p.GuildOpinion)} · {favour}{ask}";
                open.SetLabel(q != null && q.IsOpen ? "들어주기" : "보기");
            }
        }

        static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

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

            // The residents' standing favours and requests (P4-134): one line each, click to open a request.
            UIBuilder.Label(body, "주민 평판·부탁", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 26);
            var residents = UIBuilder.Panel(body, "Residents", UITheme.PanelBgLight);
            residents.Size(-1, 214);
            UIBuilder.VLayout(residents, 2, 8);
            _townLine = UIBuilder.Label(residents.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                var line = UIBuilder.Row(residents.transform, 28, 8);
                var label = UIBuilder.Label(line, "", 14).Size(-1, 26, 1, 0);
                label.verticalOverflow = VerticalWrapMode.Overflow;
                var open = UIBuilder.Button(line, "부탁 보기", () => OpenResident(index), 96, 26, null, 13);
                _residentRows.Add((label, open));
            }

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
            RefreshResidents();
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
                // The baker's favour (P4-134) is in the price.
                int small = FoodStore.PriceNow(FoodRules.SmallSize), bulk = FoodStore.PriceNow(FoodRules.BulkSize);
                _buySmall.SetLabel($"{FoodRules.SmallSize}개 사기 ({small}G)");
                _buyBulk.SetLabel($"{FoodRules.BulkSize}개 사기 ({bulk}G)");
                _buySmall.interactable = can && t.Gold >= small;
                _buyBulk.interactable = can && t.Gold >= bulk;
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
