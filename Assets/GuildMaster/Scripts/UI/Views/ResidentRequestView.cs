using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Quests;
using GuildMaster.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// A resident's request window (P4-134 F-14, user decision 2026-11-08 "주민에게 직접"): opened by clicking a resident (or its
    /// notification). Who asks and what they think of the guild, the standing favour of their trade now and at the next step,
    /// what they ask and the thanks — then hand the things over from the stock, or send one member (the ones who could go,
    /// with the errand's risk for each alone). A request under way shows who is on it.
    /// </summary>
    public class ResidentRequestView : UIPanel
    {
        public override string Title => "주민 부탁";
        protected override float RefreshInterval => 0.5f;

        string _residentId;
        Text _who, _ask, _info, _message, _sendHint, _sendHeader;
        GameObject _membersPanel;
        Button _handOver;
        RectTransform _list;
        Text _empty;

        class Row
        {
            public RectTransform Root;
            public Text Label;
            public Button Send;
            public Adventurer Member;
        }

        readonly List<Row> _rows = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);

            var head = UIBuilder.Panel(body, "Who", UITheme.PanelBgLight);
            head.Size(-1, 92);
            _who = UIBuilder.Label(head.transform, "", 15, TextAnchor.UpperLeft);
            _who.rectTransform.Stretch(12, 12, 8, 8);
            _who.verticalOverflow = VerticalWrapMode.Overflow;

            var ask = UIBuilder.Panel(body, "Ask", UITheme.PanelBgLight);
            ask.Size(-1, 128);
            UIBuilder.VLayout(ask, 4, 10);
            _ask = UIBuilder.Label(ask.transform, "", 16, TextAnchor.UpperLeft, UITheme.Accent).Size(-1, 44);
            _ask.verticalOverflow = VerticalWrapMode.Overflow;
            _info = UIBuilder.Label(ask.transform, "", 15, TextAnchor.UpperLeft).Size(-1, 54);
            _info.verticalOverflow = VerticalWrapMode.Overflow;

            var hand = UIBuilder.Row(body, 36, 10);
            _handOver = UIBuilder.Button(hand, "창고에서 건네기", HandOver, 260, 34, UITheme.Header);
            _handOver.Tooltip(() => "창고에 있는 물건을 바로 건넵니다. 사례는 곧바로 받습니다.");
            _message = UIBuilder.Label(hand, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 34, 1, 0);

            _sendHeader = UIBuilder.Label(body, "길드원 보내기", 17, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 24);
            _sendHint = UIBuilder.Label(body, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
            var panel = UIBuilder.Panel(body, "Members", UITheme.PanelBgLight);
            panel.Size(-1, -1, -1, 1);
            _membersPanel = panel.gameObject;
            _list = UIBuilder.ScrollList(panel.transform, out var scroll, 4);
            scroll.GetComponent<RectTransform>().Stretch(8, 8, 8, 8);
            _empty = UIBuilder.Label(_list, "", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 48);
        }

        /// <summary>Opens the window on this resident (its request, or what it thinks when it asks nothing).</summary>
        public void Show(Resident r)
        {
            if (r == null || r.Profile == null) return;
            if (_residentId != r.Profile.Id) _message.text = "";
            _residentId = r.Profile.Id;
            if (!Visible) Open();
            else BringToFront();
            Refresh();
        }

        public void ShowById(string residentId)
        {
            if (Services.TryGet<ResidentService>(out var residents) && residents.Find(residentId) is { } r) Show(r);
        }

        ResidentRequest Current(ResidentRequests svc)
        {
            var active = svc.ActiveOf(_residentId);
            if (active != null) return active;
            // The last one it asked, to show how it went.
            ResidentRequest last = null;
            foreach (var r in svc.All) if (r.ResidentId == _residentId) last = r;
            return last;
        }

        void HandOver()
        {
            if (!Services.TryGet<ResidentRequests>(out var svc)) return;
            var r = svc.ActiveOf(_residentId);
            _message.text = r != null && svc.TryHandOver(r, out var error)
                ? UIBuilder.Colored("건넸습니다. 사례를 받았습니다.", UITheme.Good)
                : UIBuilder.Colored(r == null ? "들어줄 부탁이 없습니다." : svc.CannotHandOverReason(r) ?? "건넬 수 없습니다.", UITheme.Bad);
            Refresh();
        }

        void Send(Adventurer member)
        {
            if (!Services.TryGet<ResidentRequests>(out var svc)) return;
            var r = svc.ActiveOf(_residentId);
            if (r == null) return;
            _message.text = svc.TrySend(r, member, out var error)
                ? UIBuilder.Colored(r.Kind == RequestKind.Story ? $"{member.Data.name}이(가) 모험담을 들려주었습니다." : $"{member.Data.name}이(가) 부탁을 맡아 나섭니다.", UITheme.Good)
                : UIBuilder.Colored(error, UITheme.Bad);
            Refresh();
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<ResidentRequests>(out var svc) || !Services.TryGet<ResidentService>(out var residents)) return;
            var resident = residents.Find(_residentId);
            if (resident == null) return;
            var p = resident.Profile;
            _who.text = WhoText(p, svc);

            var r = Current(svc);
            bool open = r != null && r.IsOpen;
            if (r == null || !r.IsActive)
            {
                _ask.text = UIBuilder.Colored("지금은 부탁이 없습니다.", UITheme.TextMuted);
                _info.text = r == null ? UIBuilder.Colored("주민은 낮(6~18시)에 가끔 길드에 부탁합니다. 머리 위에 '!'가 뜹니다.", UITheme.TextMuted)
                                       : UIBuilder.Colored($"지난 부탁: {svc.WhatText(r)} — {StatusText(r)}", UITheme.TextMuted);
            }
            else
            {
                _ask.text = $"\"{svc.AskLine(r)}\"";
                var sb = new StringBuilder();
                sb.Append("부탁: ").Append(UIBuilder.Colored(svc.WhatText(r), UITheme.Accent));
                if (open) sb.Append(" · ").Append(UIBuilder.Colored($"{svc.HoursLeft(r):0}시간 남음", UITheme.Warn));
                else sb.Append(" · ").Append(UIBuilder.Colored(UnderwayText(r), UITheme.Good));
                sb.Append('\n').Append("사례: ").Append(UIBuilder.Colored(ThanksText(p.Job), UITheme.Good))
                  .Append($" · 평판 +{ResidentRequestRules.OpinionWon}")
                  .Append(UIBuilder.Colored($" (보낸 길드원이 실패하면 −{ResidentRequestRules.OpinionLost}, 그냥 두면 사라질 뿐)", UITheme.TextMuted));
                _info.text = sb.ToString();
            }

            // ---- hand over ----
            bool goods = open && r.Kind == RequestKind.Goods;
            _handOver.gameObject.SetActive(goods);
            if (goods)
            {
                int have = Services.TryGet<GuildTreasury>(out var t) ? t.GetStock(r.ItemId) : 0;
                _handOver.SetLabel($"창고에서 건네기 ({have}/{r.Count})");
                _handOver.interactable = svc.CannotHandOverReason(r) == null;
            }

            // ---- members ----
            var candidates = open ? svc.Candidates(r) : new List<ResidentRequests.Candidate>();
            QuestDraft draft = default;
            string plan = open && r.Kind != RequestKind.Story ? svc.Plan(r, out draft, out _, out _, out _) : null;
            _sendHint.text = !open ? (r != null && r.Status == RequestStatus.Underway ? "맡은 길드원이 하는 중입니다." : "")
                           : r.Kind == RequestKind.Story ? "마을에서 쉬고 있는 길드원이 그 자리에서 모험담을 들려줍니다."
                           : plan != null ? UIBuilder.Colored(plan, UITheme.Bad)
                           : $"한 명이 나섭니다. 보수 {draft.RewardGold}G / {draft.RewardExp}EXP는 주민이 그 길드원에게 줍니다(길드 수수료 없음). 위험은 혼자 갔을 때.";
            EnsureMemberRows(candidates.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i < candidates.Count;
                if (_rows[i].Root.gameObject.activeSelf != on) _rows[i].Root.gameObject.SetActive(on);
                if (!on) continue;
                var c = candidates[i];
                var d = c.Member.Data;
                _rows[i].Member = c.Member;
                string risk = string.IsNullOrEmpty(c.Risk) ? "" : "  위험 " + UIBuilder.Colored(c.Risk, RiskColor(c.Risk));
                _rows[i].Label.text = $"{d.name}  {UIBuilder.Colored($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}", UITheme.TextMuted)}{risk}  " +
                                      UIBuilder.Colored(c.Member.StateLabel, UITheme.TextMuted);
                _rows[i].Send.SetLabel(r.Kind == RequestKind.Story ? "들려주기" : "보내기");
            }
            // Nobody can be sent at all (no known spot, no village): the reason above says it; no "nobody free" below.
            _empty.text = !open || plan != null ? "" : r.Kind == RequestKind.Story ? "마을에서 쉬고 있는 길드원이 없습니다." : "지금 마을에서 쉬고 있는, 맡은 의뢰가 없는 길드원이 없습니다.";
            _empty.gameObject.SetActive(_empty.text != "" && candidates.Count == 0);
            _empty.transform.SetAsLastSibling();
            // Nothing to answer: no member section.
            _sendHeader.gameObject.SetActive(open);
            _membersPanel.SetActive(open);
        }

        void EnsureMemberRows(int n)
        {
            while (_rows.Count < n)
            {
                var row = new Row();
                row.Root = UIBuilder.Row(_list, 34, 8);
                row.Label = UIBuilder.Label(row.Root, "", 15).Size(-1, 32, 1, 0);
                row.Send = UIBuilder.Button(row.Root, "보내기", () => Send(row.Member), 110, 30, UITheme.Header, 15);
                _rows.Add(row);
            }
        }

        static Color RiskColor(string risk) => risk switch
        {
            QuestRisk.Easy => UITheme.Good,
            QuestRisk.Fair => UITheme.Accent,
            QuestRisk.Risky => UITheme.Warn,
            _ => UITheme.Bad,
        };

        static string WhoText(ResidentProfile p, ResidentRequests svc)
        {
            int tier = ResidentRequestRules.Tier(p.GuildOpinion);
            int next = ResidentRequestRules.NextTierAt(p.GuildOpinion);
            var color = p.GuildOpinion >= ResidentProfile.GoodOpinion ? UITheme.Good : p.GuildOpinion <= -ResidentProfile.GoodOpinion ? UITheme.Bad : UITheme.TextMuted;
            var sb = new StringBuilder();
            sb.Append(UIBuilder.Colored(ResidentRequestRules.Who(p), UITheme.Accent))
              .Append($"  ·  길드 평판 ").Append(UIBuilder.Colored(Signed(p.GuildOpinion), color))
              .Append(tier > 0 ? $" ({tier}단계)" : "").Append('\n');
            sb.Append("단골 혜택: ").Append(tier > 0 ? UIBuilder.Colored(ResidentRequestRules.FavourText(p.Job, tier), UITheme.Good) : UIBuilder.Colored("없음", UITheme.TextMuted));
            if (next > 0) sb.Append(UIBuilder.Colored($"  (평판 {next}부터 {ResidentRequestRules.FavourText(p.Job, tier + 1)})", UITheme.TextMuted));
            sb.Append('\n').Append(UIBuilder.Colored(
                $"마을 평판 {svc.TownOpinion} ({ResidentRequestRules.TownOpinionLabel(svc.TownOpinion)}) — 새 방문자 시작 호감 {Signed(svc.VisitorWordNow)}", UITheme.TextMuted));
            return sb.ToString();
        }

        static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        static string ThanksText(ResidentJob job)
        {
            var t = ResidentRequestRules.ThanksOf(job, 0.5f);
            if (job == ResidentJob.Child)
                return $"토비가 주운 물건({Quest.ItemName("stone_shard")}·{Quest.ItemName(ErrandRules.Ore)}·{Quest.ItemName(ErrandRules.Herb)}, 드물게 {Quest.ItemName("crown_shard")})";
            if (t.Gold > 0) return $"{t.Gold}G";
            if (t.Food > 0) return $"식량 {t.Food}개";
            return ErrandRules.ItemCount(Quest.ItemName(t.Item.itemId), t.Item.count) + " (창고로)";
        }

        static string UnderwayText(ResidentRequest r)
        {
            var q = Services.TryGet<QuestBoard>(out var board) && r.QuestId != null ? board.FindById(r.QuestId) : null;
            return $"{r.MemberName}이(가) 하는 중" + (q != null && q.ProgressText != "" ? $" ({q.ProgressText})" : "");
        }

        static string StatusText(ResidentRequest r) => r.Status switch
        {
            RequestStatus.Done => r.MemberName != null ? $"{r.MemberName}이(가) 들어주었습니다" : "창고에서 건넸습니다",
            RequestStatus.Failed => $"{r.MemberName ?? "길드원"}이(가) 해내지 못했습니다",
            RequestStatus.Lapsed => "흐지부지되었습니다",
            _ => "",
        };
    }
}
